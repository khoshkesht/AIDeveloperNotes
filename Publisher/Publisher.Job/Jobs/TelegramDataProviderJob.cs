using System.Net;
using System.Text.RegularExpressions;

internal sealed class TelegramDataProviderJob
{
    private readonly AppConfig _appConfig;
    private readonly TelegramDataProviderConfig _config;
    private readonly ProxyConfig _proxyConfig;
    private readonly string _basePath;

    public TelegramDataProviderJob(
        AppConfig appConfig,
        string basePath)
    {
        _appConfig = appConfig;
        _config = appConfig.TelegramDataProvider;
        _proxyConfig = appConfig.Proxy;
        _basePath = basePath;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("Manual TelegramDataProvider job started.");

        if (_config.Channels.Count == 0)
        {
            Console.WriteLine("telegramDataProvider.channels is empty. Add at least one Telegram channel URL.");
            return;
        }

        var reader = new TelegramChannelReader(_proxyConfig);
        var telegramPostService = new TelegramPostService(_appConfig.Groq, _proxyConfig);
        var telegram = new TelegramPublisher(_proxyConfig);
        var sentSummaries = 0;
        foreach (var channel in _config.Channels)
        {
            var channelUrl = channel.Url.Trim();
            if (string.IsNullOrWhiteSpace(channelUrl))
            {
                Console.WriteLine("Skipped empty Telegram channel URL.");
                continue;
            }

            var promptPath = ResolvePromptPath(channel.PromptPath);
            if (string.IsNullOrWhiteSpace(promptPath))
            {
                Console.WriteLine($"Skipped Telegram channel because channel promptPath is empty: {channelUrl}");
                continue;
            }

            var bot = ConfigResolver.ResolveBot(_appConfig, channel);
            var targetChannel = ConfigResolver.ResolveChannel(channel);
            if (bot is null || targetChannel is null)
            {
                Console.WriteLine($"Skipped Telegram channel because target bot/channel was not found in config.json: {channelUrl}");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"Channel: {channelUrl}");
            Console.WriteLine($"Target chatId: {targetChannel.ChatId}");
            var postLimit = Math.Max(1, channel.PostLimit);
            IReadOnlyList<TelegramChannelPost> posts;
            try
            {
                posts = await reader.GetLatestPostsAsync(channelUrl, postLimit, _config.UseProxy);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read Telegram channel: {ex.Message}");
                continue;
            }

            if (posts.Count == 0)
            {
                Console.WriteLine("No public posts were found.");
                continue;
            }

            var textPostsQuery = posts
                .Select(post => new { Post = post, Text = post.Text.Trim() })
                .Where(item => !string.IsNullOrWhiteSpace(item.Text) && !item.Text.Equals("(no text)", StringComparison.OrdinalIgnoreCase));

            if (channel.MaxAgeMinutes > 0)
            {
                var newestAllowedPublishedAt = DateTimeOffset.UtcNow.AddMinutes(-channel.MaxAgeMinutes);
                textPostsQuery = textPostsQuery
                    .Where(item => item.Post.PublishedAt is not null && item.Post.PublishedAt.Value.ToUniversalTime() >= newestAllowedPublishedAt);
            }

            var textPosts = textPostsQuery.ToList();

            if (textPosts.Count == 0)
            {
                var ageFilterText = channel.MaxAgeMinutes > 0
                    ? $"newer than {channel.MaxAgeMinutes} minute(s)"
                    : "with any age";
                Console.WriteLine($"No text posts {ageFilterText} were found. Skipping Groq.");
                continue;
            }

            var plainTexts = textPosts.Select(item => item.Text).ToList();
            IReadOnlyList<string> generatedPosts;
            try
            {
                generatedPosts = await telegramPostService.GenerateTelegramPostsAsync(
                    new TelegramTextPostRequest(promptPath, plainTexts),
                    _config.UseProxy);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to generate Telegram posts for channel: {ex.Message}");
                continue;
            }

            if (generatedPosts.Count != 1)
            {
                Console.WriteLine($"Skipped generated output because Groq returned {generatedPosts.Count} post(s), but TelegramDataProvider expects exactly 1 summarized post per channel.");
                continue;
            }

            var postText = generatedPosts[0].Trim();
            if (string.IsNullOrWhiteSpace(postText))
            {
                Console.WriteLine("Skipped empty generated summary because no relevant Telegram channel items were found.");
                continue;
            }

            if (sentSummaries > 0)
            {
                var delaySeconds = Math.Max(0, _config.SendDelayBetweenChannelsSeconds);
                if (delaySeconds > 0)
                {
                    Console.WriteLine($"Waiting {delaySeconds} second(s) before sending the next TelegramDataProvider post.");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }
            }

            var result = await telegram.SendPostAsync(bot, targetChannel, postText, imagePath: null, channel.UseProxy);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Telegram send failed for TelegramDataProvider: {result.ErrorMessage}");
            }

            sentSummaries++;
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"Source posts: {textPosts.Count}");
            Console.WriteLine($"Newest source post: {textPosts[0].Post.Url}");
            Console.WriteLine($"Newest source publishedAt: {textPosts[0].Post.PublishedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(unknown)"}");
            Console.WriteLine(postText);
            Console.WriteLine("Sent summarized TelegramDataProvider post to Telegram.");
        }

        Console.WriteLine();
        Console.WriteLine("TelegramDataProvider job finished.");
    }

    private string ResolvePromptPath(string channelPromptPath)
    {
        if (string.IsNullOrWhiteSpace(channelPromptPath))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(channelPromptPath)
            ? channelPromptPath
            : Path.Combine(_basePath, channelPromptPath);
    }
}

internal sealed class TelegramChannelReader
{
    private readonly ProxyConfig _proxyConfig;

    public TelegramChannelReader(ProxyConfig proxyConfig)
    {
        _proxyConfig = proxyConfig;
    }

    public async Task<IReadOnlyList<TelegramChannelPost>> GetLatestPostsAsync(string channelUrl, int postLimit, bool useProxy)
    {
        var publicUrl = BuildPublicChannelUrl(channelUrl);
        using var httpClient = HttpClientFactory.Create(_proxyConfig, useProxy);
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AIDeveloperNotesPublisher/1.0");

        string html;
        try
        {
            html = await httpClient.GetStringAsync(publicUrl);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Telegram public channel request failed: {publicUrl}. UseProxy={useProxy}. {ex.Message}",
                ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new InvalidOperationException(
                $"Telegram public channel request timed out: {publicUrl}. UseProxy={useProxy}.",
                ex);
        }

        return ParsePosts(html)
            .TakeLast(Math.Max(1, postLimit))
            .Reverse()
            .ToList();
    }

    private static Uri BuildPublicChannelUrl(string channelUrl)
    {
        var value = channelUrl.Trim();
        if (value.StartsWith("@", StringComparison.Ordinal))
        {
            value = "https://telegram.me/" + value[1..];
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            value = "https://telegram.me/" + value.TrimStart('/');
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
            {
                throw new InvalidOperationException($"Invalid Telegram channel URL: {channelUrl}");
            }
        }

        if (!uri.Host.Equals("telegram.me", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Telegram channel host must be telegram.me: {channelUrl}");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException($"Telegram channel URL does not include a channel name: {channelUrl}");
        }

        var channelName = segments[0].Equals("s", StringComparison.OrdinalIgnoreCase) && segments.Length > 1
            ? segments[1]
            : segments[0];

        return new Uri($"https://telegram.me/s/{channelName}");
    }

    private static IEnumerable<TelegramChannelPost> ParsePosts(string html)
    {
        const RegexOptions options = RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var postMatches = Regex.Matches(html, @"\sdata-post=""(?<post>[^""]+)""", options);

        for (var index = 0; index < postMatches.Count; index++)
        {
            var postMatch = postMatches[index];
            var postId = WebUtility.HtmlDecode(postMatch.Groups["post"].Value).Trim();
            if (string.IsNullOrWhiteSpace(postId))
            {
                continue;
            }

            var start = postMatch.Index;
            var end = index + 1 < postMatches.Count ? postMatches[index + 1].Index : html.Length;
            var body = html[start..end];
            var text = ExtractMessageText(body);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = ExtractMessageMediaCaption(body);
            }

            yield return new TelegramChannelPost(
                PostId: postId,
                Url: "https://telegram.me/" + postId,
                Text: string.IsNullOrWhiteSpace(text) ? "(no text)" : text,
                PublishedAt: ExtractPublishedAt(body));
        }
    }

    private static string ExtractMessageText(string body)
    {
        var match = Regex.Match(
            body,
            @"<div\s+class=""tgme_widget_message_text[^""]*""[^>]*>(?<text>.*?)</div>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? CleanTelegramHtml(match.Groups["text"].Value) : string.Empty;
    }

    private static string ExtractMessageMediaCaption(string body)
    {
        var match = Regex.Match(
            body,
            @"<div\s+class=""tgme_widget_message_photo_wrap[^""]*""[^>]*aria-label=""(?<text>[^""]+)""",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["text"].Value).Trim() : string.Empty;
    }

    private static DateTimeOffset? ExtractPublishedAt(string body)
    {
        var match = Regex.Match(
            body,
            @"<time[^>]*datetime=""(?<datetime>[^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && DateTimeOffset.TryParse(match.Groups["datetime"].Value, out var parsed)
            ? parsed
            : null;
    }

    private static string CleanTelegramHtml(string value)
    {
        var withLineBreaks = Regex.Replace(value, @"<br\s*/?>", Environment.NewLine, RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withLineBreaks, "<.*?>", " ", RegexOptions.Singleline);
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var normalizedLines = decoded
            .Split('\n')
            .Select(line => Regex.Replace(line, "\\s+", " ").Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
        return string.Join(Environment.NewLine, normalizedLines);
    }
}

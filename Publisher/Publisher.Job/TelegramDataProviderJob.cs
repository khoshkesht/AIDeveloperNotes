using System.Net;
using System.Text.RegularExpressions;

internal sealed class TelegramDataProviderJob
{
    private readonly TelegramDataProviderConfig _config;
    private readonly ProxyConfig _proxyConfig;

    public TelegramDataProviderJob(TelegramDataProviderConfig config, ProxyConfig proxyConfig)
    {
        _config = config;
        _proxyConfig = proxyConfig;
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
        var postLimit = Math.Max(1, _config.PostLimit);
        foreach (var channel in _config.Channels)
        {
            var channelUrl = channel.Url.Trim();
            if (string.IsNullOrWhiteSpace(channelUrl))
            {
                Console.WriteLine("Skipped empty Telegram channel URL.");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"Channel: {channelUrl}");
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

            foreach (var post in posts)
            {
                Console.WriteLine("----------------------------------------");
                Console.WriteLine($"Post: {post.PostId}");
                Console.WriteLine($"Url: {post.Url}");
                Console.WriteLine($"PublishedAt: {post.PublishedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(unknown)"}");
                Console.WriteLine(post.Text);
            }
        }

        Console.WriteLine();
        Console.WriteLine("TelegramDataProvider job finished.");
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
            throw new InvalidOperationException($"Telegram public channel request failed: {publicUrl}", ex);
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
            value = "https://t.me/" + value[1..];
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            value = "https://t.me/" + value.TrimStart('/');
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
            {
                throw new InvalidOperationException($"Invalid Telegram channel URL: {channelUrl}");
            }
        }

        if (!uri.Host.Equals("t.me", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Telegram channel host must be t.me: {channelUrl}");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException($"Telegram channel URL does not include a channel name: {channelUrl}");
        }

        var channelName = segments[0].Equals("s", StringComparison.OrdinalIgnoreCase) && segments.Length > 1
            ? segments[1]
            : segments[0];

        return new Uri($"https://t.me/s/{channelName}");
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
                Url: "https://t.me/" + postId,
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

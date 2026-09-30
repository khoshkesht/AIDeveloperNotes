using System.Text;
using System.Text.Json;

internal sealed class ZohoCliqDataProviderJob
{
    private readonly AppConfig _appConfig;
    private readonly ZohoCliqDataProviderConfig _config;
    private readonly string _basePath;

    public ZohoCliqDataProviderJob(AppConfig appConfig, string basePath)
    {
        _appConfig = appConfig;
        _config = appConfig.ZohoCliqDataProvider;
        _basePath = basePath;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("Zoho Cliq data provider job started.");

        if (_config.Channels.Count == 0)
        {
            Console.WriteLine("zohoCliqDataProvider.channels is empty. Add at least one Telegram channel URL.");
            return;
        }

        var endpoint = BuildEndpoint();
        var reader = new TelegramChannelReader(_appConfig.Proxy);
        var postService = new TelegramPostService(_appConfig.Groq, _appConfig.Proxy);
        using var httpClient = HttpClientFactory.Create(_appConfig.Proxy, _config.UseProxy);
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AIDeveloperNotesPublisher/1.0");

        var sentCount = 0;
        foreach (var channel in _config.Channels)
        {
            var channelUrl = channel.Url.Trim();
            if (string.IsNullOrWhiteSpace(channelUrl))
            {
                Console.WriteLine("Skipped empty Telegram channel URL.");
                continue;
            }

            var promptPath = ResolvePromptPath(channel.PromptPath);
            if (string.IsNullOrWhiteSpace(promptPath) || !File.Exists(promptPath))
            {
                Console.WriteLine($"Skipped {channelUrl}: channel promptPath is empty or does not exist.");
                continue;
            }

            var postLimit = Math.Max(1, channel.PostLimit);
            IReadOnlyList<TelegramChannelPost> posts;
            try
            {
                posts = await reader.GetLatestPostsAsync(channelUrl, postLimit, _config.UseProxy);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read Telegram channel {channelUrl}: {ex.Message}");
                continue;
            }

            var textPosts = posts
                .Select(post => new { Post = post, Text = post.Text.Trim() })
                .Where(item => !string.IsNullOrWhiteSpace(item.Text) && !item.Text.Equals("(no text)", StringComparison.OrdinalIgnoreCase));

            if (channel.MaxAgeMinutes > 0)
            {
                var newestAllowedPublishedAt = DateTimeOffset.UtcNow.AddMinutes(-channel.MaxAgeMinutes);
                textPosts = textPosts.Where(item =>
                    item.Post.PublishedAt is not null &&
                    item.Post.PublishedAt.Value.ToUniversalTime() >= newestAllowedPublishedAt);
            }

            var texts = textPosts.Select(item => item.Text).ToList();
            if (texts.Count == 0)
            {
                Console.WriteLine($"No eligible text posts found for {channelUrl}.");
                continue;
            }

            IReadOnlyList<string> generatedPosts;
            try
            {
                generatedPosts = await postService.GenerateTelegramPostsAsync(
                    new TelegramTextPostRequest(promptPath, texts), _config.UseProxy);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to generate Zoho Cliq message for {channelUrl}: {ex.Message}");
                continue;
            }

            if (generatedPosts.Count != 1)
            {
                Console.WriteLine($"Skipped {channelUrl}: Groq returned {generatedPosts.Count} post(s); exactly one is required.");
                continue;
            }

            var message = TelegramDataProviderJob.CleanGeneratedPost(generatedPosts[0]);
            if (string.IsNullOrWhiteSpace(message))
            {
                Console.WriteLine($"Skipped {channelUrl}: generated message is empty.");
                continue;
            }

            message = TelegramDataProviderJob.AppendSourceChannelName(message, channelUrl);
            if (sentCount > 0)
            {
                var delaySeconds = Math.Max(0, _config.SendDelayBetweenChannelsSeconds);
                if (delaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }
            }

            await SendMessageAsync(httpClient, endpoint, message);
            sentCount++;
            Console.WriteLine($"Sent summarized post from {channelUrl} to Zoho Cliq.");
        }

        Console.WriteLine($"Zoho Cliq data provider job finished. Sent {sentCount} message(s).");
    }

    private string ResolvePromptPath(string channelPromptPath) =>
        Path.IsPathRooted(channelPromptPath)
            ? channelPromptPath
            : Path.Combine(_basePath, channelPromptPath);

    private Uri BuildEndpoint()
    {
        if (!Uri.TryCreate(_config.Endpoint.Trim(), UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("zohoCliqDataProvider.endpoint must be an absolute HTTPS URL.");
        }

        if (endpoint.Query.Contains("zapikey=", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        var apiKey = string.IsNullOrWhiteSpace(_config.ApiKey)
            ? Environment.GetEnvironmentVariable(_config.ApiKeyEnvironmentVariable)?.Trim()
            : _config.ApiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Set zohoCliqDataProvider.apiKey or environment variable '{_config.ApiKeyEnvironmentVariable}'.");
        }

        var separator = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
        return new Uri(endpoint + separator + "zapikey=" + Uri.EscapeDataString(apiKey));
    }

    private static async Task SendMessageAsync(HttpClient httpClient, Uri endpoint, string message)
    {
        var body = JsonSerializer.Serialize(new { text = message });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(endpoint, content);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException(
            $"Zoho Cliq send failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {responseBody}");
    }
}

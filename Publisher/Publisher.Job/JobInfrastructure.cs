using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

internal sealed class JobStateStore
{
    private readonly string _stateFilePath;
    private readonly string _lockFilePath;

    public JobStateStore(string stateFilePath, string lockFilePath)
    {
        _stateFilePath = stateFilePath;
        _lockFilePath = lockFilePath;
    }

    public DailyJobState Load()
    {
        if (!File.Exists(_stateFilePath))
        {
            return new DailyJobState();
        }

        var json = File.ReadAllText(_stateFilePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<DailyJobState>(json) ?? new DailyJobState();
    }

    public void SaveStarted()
    {
        Save(new DailyJobState
        {
            LastRunDate = DateOnly.FromDateTime(DateTime.Now),
            LastAttemptStartedAt = DateTimeOffset.Now,
            LastAttemptFinishedAt = null,
            LastSentCount = 0
        });
    }

    public void SaveFinished(int sentCount)
    {
        Save(new DailyJobState
        {
            LastRunDate = DateOnly.FromDateTime(DateTime.Now),
            LastAttemptStartedAt = Load().LastAttemptStartedAt,
            LastAttemptFinishedAt = DateTimeOffset.Now,
            LastSentCount = sentCount
        });
    }

    public FileStream? TryAcquireLock()
    {
        try
        {
            return new FileStream(_lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void EnsureWritable()
    {
        var stateDirectory = Path.GetDirectoryName(_stateFilePath) ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(stateDirectory);
        var testFile = Path.Combine(stateDirectory, $".publisher-job-write-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(testFile, "write-test", Encoding.UTF8);
        File.Delete(testFile);
    }

    private void Save(DailyJobState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_stateFilePath, json, Encoding.UTF8);
    }
}

internal static class ConfigResolver
{
    public static BotConfig? ResolveBot(AppConfig config, TelegramTargetConfig target)
    {
        if (!string.IsNullOrWhiteSpace(target.BotId))
        {
            var botById = config.Bots.FirstOrDefault(bot => bot.BotId.Equals(target.BotId, StringComparison.OrdinalIgnoreCase));
            if (botById is not null)
            {
                return botById;
            }
        }

        if (!string.IsNullOrWhiteSpace(target.BotName))
        {
            return config.Bots.FirstOrDefault(bot => bot.Name.Equals(target.BotName, StringComparison.OrdinalIgnoreCase));
        }

        return config.Bots.FirstOrDefault();
    }

    public static ChannelConfig? ResolveChannel(AppConfig config, TelegramTargetConfig target)
    {
        if (!string.IsNullOrWhiteSpace(target.ChatId))
        {
            var channelById = config.Channels.FirstOrDefault(channel => channel.ChatId.Equals(target.ChatId, StringComparison.OrdinalIgnoreCase));
            if (channelById is not null)
            {
                return channelById;
            }
        }

        if (!string.IsNullOrWhiteSpace(target.ChannelTitle))
        {
            return config.Channels.FirstOrDefault(channel => channel.Title.Equals(target.ChannelTitle, StringComparison.OrdinalIgnoreCase));
        }

        return config.Channels.FirstOrDefault();
    }
}

internal sealed class TelegramPublisher
{
    private readonly ProxyConfig _proxyConfig;

    public TelegramPublisher(ProxyConfig proxyConfig)
    {
        _proxyConfig = proxyConfig;
    }

    public async Task<TelegramSendResult> SendPostAsync(
        BotConfig bot,
        ChannelConfig channel,
        string postText,
        string? imagePath,
        bool useProxy)
    {
        using var httpClient = HttpClientFactory.Create(_proxyConfig, useProxy);
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return await SendTelegramTextPostAsync(httpClient, bot.BotId, channel.ChatId, postText);
        }

        if (postText.Length <= 1024)
        {
            return await SendTelegramPhotoAsync(httpClient, bot.BotId, channel.ChatId, imagePath, postText);
        }

        var photoResult = await SendTelegramPhotoAsync(httpClient, bot.BotId, channel.ChatId, imagePath, null);
        if (!photoResult.Success)
        {
            return photoResult;
        }

        return await SendTelegramTextPostAsync(httpClient, bot.BotId, channel.ChatId, postText);
    }

    private static async Task<TelegramSendResult> SendTelegramTextPostAsync(
        HttpClient httpClient,
        string botId,
        string chatId,
        string text)
    {
        foreach (var chunk in SplitTelegramText(text))
        {
            var result = await SendTelegramMessageAsync(httpClient, botId, chatId, chunk);
            if (!result.Success)
            {
                return result;
            }
        }

        return TelegramSendResult.Ok();
    }

    private static IEnumerable<string> SplitTelegramText(string text)
    {
        const int maxLength = 3900;
        var remaining = text.Trim();
        while (remaining.Length > maxLength)
        {
            var splitAt = remaining.LastIndexOf('\n', maxLength);
            if (splitAt < maxLength / 2)
            {
                splitAt = remaining.LastIndexOf(' ', maxLength);
            }

            if (splitAt < maxLength / 2)
            {
                splitAt = maxLength;
            }

            yield return remaining[..splitAt].Trim();
            remaining = remaining[splitAt..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(remaining))
        {
            yield return remaining;
        }
    }

    private static async Task<TelegramSendResult> SendTelegramMessageAsync(HttpClient httpClient, string botId, string chatId, string text)
    {
        var url = $"https://api.telegram.org/{botId}/sendMessage";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["parse_mode"] = "HTML",
            ["text"] = text
        });

        return await SendTelegramRequestAsync(httpClient, url, content);
    }

    private static async Task<TelegramSendResult> SendTelegramPhotoAsync(
        HttpClient httpClient,
        string botId,
        string chatId,
        string imagePath,
        string? caption)
    {
        var url = $"https://api.telegram.org/{botId}/sendPhoto";
        await using var imageStream = File.OpenRead(imagePath);
        using var content = new MultipartFormDataContent
        {
            { new StringContent(chatId, Encoding.UTF8), "chat_id" },
            { new StringContent("HTML", Encoding.UTF8), "parse_mode" }
        };

        if (!string.IsNullOrWhiteSpace(caption))
        {
            content.Add(new StringContent(caption, Encoding.UTF8), "caption");
        }

        using var imageContent = new StreamContent(imageStream);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(GetImageContentType(imagePath));
        content.Add(imageContent, "photo", Path.GetFileName(imagePath));

        return await SendTelegramRequestAsync(httpClient, url, content);
    }

    private static async Task<TelegramSendResult> SendTelegramRequestAsync(HttpClient httpClient, string url, HttpContent content)
    {
        using var response = await httpClient.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            return TelegramSendResult.Ok();
        }

        return TelegramSendResult.Fail($"Telegram returned {(int)response.StatusCode} {response.ReasonPhrase}: {ExtractTelegramError(responseBody)}");
    }

    private static string GetImageContentType(string imagePath)
    {
        return Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    private static string ExtractTelegramError(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return "Empty response body.";
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("description", out var description))
            {
                return description.GetString() ?? responseBody;
            }
        }
        catch (JsonException)
        {
        }

        return responseBody;
    }
}

internal sealed class GroqArticleGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GroqConfig _groqConfig;
    private readonly ProxyConfig _proxyConfig;

    public GroqArticleGenerator(GroqConfig groqConfig, ProxyConfig proxyConfig)
    {
        _groqConfig = groqConfig;
        _proxyConfig = proxyConfig;
    }

    public async Task<string> GenerateTelegramPostAsync(string prompt, bool useProxy)
    {
        using var httpClient = HttpClientFactory.Create(_proxyConfig, useProxy);
        httpClient.BaseAddress = new Uri(_groqConfig.BaseUrl.TrimEnd('/') + "/");
        httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(30, _groqConfig.TimeoutSeconds));

        var body = new
        {
            model = _groqConfig.Model,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = _groqConfig.SystemPrompt
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            temperature = _groqConfig.Temperature,
            max_completion_tokens = _groqConfig.MaxCompletionTokens
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey());
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Groq API connection failed. BaseUrl='{_groqConfig.BaseUrl}', UseProxy={useProxy}. " +
                "If direct HTTPS is blocked, set groqArticleJob.useProxy=true and verify proxy.address.",
                ex);
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Groq API failed with {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}");
            }

            using var document = JsonDocument.Parse(responseBody);
            return ExtractOutputText(document.RootElement);
        }
    }

    private string GetApiKey()
    {
        var apiKey = _groqConfig.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = Environment.GetEnvironmentVariable(_groqConfig.ApiKeyEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Groq API key is missing. Set groq.apiKey or environment variable '{_groqConfig.ApiKeyEnvironmentVariable}'.");
        }

        return apiKey.Trim();
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out var message) &&
            message.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

internal sealed class RssFeedReader
{
    private readonly ProxyConfig _proxyConfig;

    public RssFeedReader(ProxyConfig proxyConfig)
    {
        _proxyConfig = proxyConfig;
    }

    public async Task<RssFeedItem?> GetLatestItemAsync(string feedUrl, bool useProxy)
    {
        using var httpClient = HttpClientFactory.Create(_proxyConfig, useProxy);
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AIDeveloperNotesPublisher/1.0");

        string xml;
        try
        {
            xml = await httpClient.GetStringAsync(feedUrl);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"RSS feed request failed: {feedUrl}", ex);
        }

        var document = XDocument.Parse(xml);
        return ParseRss(document, feedUrl) ?? ParseAtom(document, feedUrl);
    }

    private static RssFeedItem? ParseRss(XDocument document, string feedUrl)
    {
        var channel = document.Descendants().FirstOrDefault(element => element.Name.LocalName.Equals("channel", StringComparison.OrdinalIgnoreCase));
        var sourceTitle = GetChildValue(channel, "title");
        var items = document.Descendants()
            .Where(element => element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
            .Select(item => new RssFeedItem(
                SourceTitle: FirstNonEmpty(sourceTitle, feedUrl),
                Title: FirstNonEmpty(GetChildValue(item, "title"), "(untitled)"),
                Link: FirstNonEmpty(GetChildValue(item, "link"), GetChildValue(item, "guid"), feedUrl),
                ImageUrl: GetImageUrl(item),
                Summary: CleanFeedText(FirstNonEmpty(
                    GetChildValue(item, "encoded"),
                    GetChildValue(item, "description"),
                    GetChildValue(item, "summary"),
                    GetChildValue(item, "title"))),
                PublishedAt: ParseDate(FirstNonEmpty(
                    GetChildValue(item, "pubDate"),
                    GetChildValue(item, "published"),
                    GetChildValue(item, "updated")))))
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToList();

        return PickLatest(items);
    }

    private static RssFeedItem? ParseAtom(XDocument document, string feedUrl)
    {
        var sourceTitle = GetChildValue(document.Root, "title");
        var items = document.Descendants()
            .Where(element => element.Name.LocalName.Equals("entry", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new RssFeedItem(
                SourceTitle: FirstNonEmpty(sourceTitle, feedUrl),
                Title: FirstNonEmpty(GetChildValue(entry, "title"), "(untitled)"),
                Link: FirstNonEmpty(GetAtomLink(entry), feedUrl),
                ImageUrl: GetImageUrl(entry),
                Summary: CleanFeedText(FirstNonEmpty(
                    GetChildValue(entry, "content"),
                    GetChildValue(entry, "summary"),
                    GetChildValue(entry, "title"))),
                PublishedAt: ParseDate(FirstNonEmpty(
                    GetChildValue(entry, "published"),
                    GetChildValue(entry, "updated")))))
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToList();

        return PickLatest(items);
    }

    private static RssFeedItem? PickLatest(List<RssFeedItem> items)
    {
        if (items.Count == 0)
        {
            return null;
        }

        return items
            .OrderByDescending(item => item.PublishedAt ?? DateTimeOffset.MinValue)
            .First();
    }

    private static string? GetChildValue(XElement? element, string localName)
    {
        return element?.Elements()
            .FirstOrDefault(child => child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();
    }

    private static string? GetAtomLink(XElement entry)
    {
        return entry.Elements()
            .Where(child => child.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase))
            .Select(child => child.Attribute("href")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? GetImageUrl(XElement item)
    {
        var mediaUrl = item.Elements()
            .Where(child =>
                child.Name.LocalName.Equals("content", StringComparison.OrdinalIgnoreCase) ||
                child.Name.LocalName.Equals("thumbnail", StringComparison.OrdinalIgnoreCase))
            .Select(child => child.Attribute("url")?.Value)
            .FirstOrDefault(value => IsLikelyImageUrl(value));

        if (!string.IsNullOrWhiteSpace(mediaUrl))
        {
            return mediaUrl;
        }

        var enclosureUrl = item.Elements()
            .Where(child => child.Name.LocalName.Equals("enclosure", StringComparison.OrdinalIgnoreCase))
            .Where(child => IsImageContentType(child.Attribute("type")?.Value))
            .Select(child => child.Attribute("url")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (!string.IsNullOrWhiteSpace(enclosureUrl))
        {
            return enclosureUrl;
        }

        var imageElementUrl = item.Descendants()
            .Where(child => child.Name.LocalName.Equals("url", StringComparison.OrdinalIgnoreCase))
            .Select(child => child.Value)
            .FirstOrDefault(value => IsLikelyImageUrl(value));

        return string.IsNullOrWhiteSpace(imageElementUrl) ? null : imageElementUrl.Trim();
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string CleanFeedText(string value)
    {
        var decoded = WebUtility.HtmlDecode(value);
        var withoutTags = Regex.Replace(decoded, "<.*?>", " ", RegexOptions.Singleline);
        var collapsedWhitespace = Regex.Replace(withoutTags, "\\s+", " ");
        return collapsedWhitespace.Trim();
    }

    private static bool IsImageContentType(string? value)
    {
        return value?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsLikelyImageUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            Regex.IsMatch(uri.AbsolutePath, "\\.(png|jpe?g|webp|gif)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

internal sealed class NewsImageDownloader
{
    private readonly ProxyConfig _proxyConfig;
    private readonly string _outputPath;

    public NewsImageDownloader(ProxyConfig proxyConfig, string outputPath)
    {
        _proxyConfig = proxyConfig;
        _outputPath = outputPath;
    }

    public async Task<string?> DownloadAsync(string? imageUrl, bool useProxy)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        using var httpClient = HttpClientFactory.Create(_proxyConfig, useProxy);
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AIDeveloperNotesPublisher/1.0");

        using var response = await httpClient.GetAsync(imageUrl);
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Skipped RSS image because download failed with {(int)response.StatusCode}: {imageUrl}");
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
        {
            Console.WriteLine($"Skipped RSS image because content type is not image: {contentType ?? "(none)"}");
            return null;
        }

        Directory.CreateDirectory(_outputPath);
        var extension = GetImageExtension(imageUrl, contentType);
        var imagePath = Path.Combine(_outputPath, $"{Guid.NewGuid():N}{extension}");

        await using var imageStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.Create(imagePath);
        await imageStream.CopyToAsync(fileStream);

        return imagePath;
    }

    private static string GetImageExtension(string imageUrl, string contentType)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif")
            {
                return extension;
            }
        }

        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".jpg"
        };
    }
}

internal static class HttpClientFactory
{
    public static HttpClient Create(ProxyConfig proxyConfig, bool useProxy)
    {
        if (!useProxy)
        {
            return new HttpClient();
        }

        var proxyAddress = proxyConfig.Address.Trim();
        if (string.IsNullOrWhiteSpace(proxyAddress))
        {
            throw new InvalidOperationException("Proxy is enabled, but proxy.address is empty in config.json.");
        }

        if (!Uri.TryCreate(proxyAddress, UriKind.Absolute, out var proxyUri))
        {
            throw new InvalidOperationException("proxy.address must be an absolute URI, for example socks5://127.0.0.1:4567.");
        }

        var proxy = new WebProxy(proxyUri);
        if (!string.IsNullOrWhiteSpace(proxyConfig.Username))
        {
            proxy.Credentials = new NetworkCredential(proxyConfig.Username, proxyConfig.Password);
        }

        return new HttpClient(new HttpClientHandler
        {
            Proxy = proxy,
            UseProxy = true
        }, disposeHandler: true);
    }
}

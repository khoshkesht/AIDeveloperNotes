using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal sealed class ZohoCliqDataProviderJob
{
    private const string ChannelLink = "[AIDeveloperNotes](https://t.me/AIDeveloperNotes)";

    private readonly ZohoCliqDataProviderConfig _config;
    private readonly string _postsPath;
    private readonly string _postedFilePath;
    private readonly JobStateStore _stateStore;
    private readonly ProxyConfig _proxyConfig;

    public ZohoCliqDataProviderJob(AppConfig appConfig, string postsPath, string postedFilePath, JobStateStore stateStore)
    {
        _config = appConfig.ZohoCliqDataProvider;
        _postsPath = postsPath;
        _postedFilePath = postedFilePath;
        _stateStore = stateStore;
        _proxyConfig = appConfig.Proxy;
    }

    public async Task RunAsync(bool updateState)
    {
        using var runLock = _stateStore.TryAcquireLock();
        if (runLock is null)
        {
            Console.WriteLine("Another Zoho Cliq data provider job instance is already running. Skipping this attempt.");
            return;
        }

        EnsurePersistenceFilesWritable(_postedFilePath, _stateStore);
        if (updateState)
        {
            _stateStore.SaveStarted();
        }

        var endpoint = BuildEndpoint();
        using var httpClient = HttpClientFactory.Create(_proxyConfig, _config.UseProxy);
        httpClient.Timeout = TimeSpan.FromSeconds(60);
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AIDeveloperNotesPublisher/1.0");

        var postedFiles = DailyPostsJob.LoadPostedFiles(_postedFilePath);
        var targetCount = Math.Max(1, _config.PostCount);
        var startPostNumber = Math.Max(1, _config.StartPostNumber);
        var sentCount = 0;

        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] Running Zoho Cliq data provider job from Post_{startPostNumber}.");

        foreach (var post in DailyPostsJob.GetUnpostedPostFiles(_postsPath, postedFiles)
                     .Where(post => DailyPostsJob.GetPostNumber(post.Name) >= startPostNumber))
        {
            if (sentCount >= targetCount)
            {
                break;
            }

            var message = FormatMessage(DailyPostsJob.ReadPostContent(post.Path));
            if (string.IsNullOrWhiteSpace(message))
            {
                Console.WriteLine($"Skipped {post.Name}: post text is empty.");
                continue;
            }

            try
            {
                await SendMessageAsync(httpClient, endpoint, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed {post.Name}: {ex.Message}");
                continue;
            }

            DailyPostsJob.MarkAsPosted(_postedFilePath, post.Path);
            postedFiles.Add(post.Name);
            sentCount++;
            Console.WriteLine($"Sent {post.Name} to Zoho Cliq.");
        }

        if (updateState)
        {
            _stateStore.SaveFinished(sentCount);
        }

        Console.WriteLine($"Zoho Cliq data provider job finished. Sent {sentCount} message(s).");
    }

    private static void EnsurePersistenceFilesWritable(string postedFilePath, JobStateStore stateStore)
    {
        using (File.Open(postedFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
        }

        stateStore.EnsureWritable();
    }

    private static string FormatMessage(string value)
    {
        var separatorIndex = value.IndexOf("-------", StringComparison.Ordinal);
        var content = separatorIndex >= 0 ? value[..separatorIndex] : value;
        content = content
            .Replace("**", "*", StringComparison.OrdinalIgnoreCase)
            .Replace("\\<b>", "*", StringComparison.OrdinalIgnoreCase)
            .Replace("\\</b>", "*", StringComparison.OrdinalIgnoreCase)
            .Replace("<b>", "*", StringComparison.OrdinalIgnoreCase)
            .Replace("</b>", "*", StringComparison.OrdinalIgnoreCase)
            .Trim();

        content = Regex.Replace(
            content,
            @"(?<!\\)#(?=[\p{L}\p{N}_-]+\s*\(\d+/\d+\))",
            @"\#");

        return string.IsNullOrWhiteSpace(content)
            ? string.Empty
            : $"{content}{Environment.NewLine}{Environment.NewLine}{ChannelLink}";
    }

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
            throw new InvalidOperationException($"Set zohoCliqDataProvider.apiKey or environment variable '{_config.ApiKeyEnvironmentVariable}'.");
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
        throw new InvalidOperationException($"Zoho Cliq send failed with {(int)response.StatusCode} ({response.ReasonPhrase}): {responseBody}");
    }
}

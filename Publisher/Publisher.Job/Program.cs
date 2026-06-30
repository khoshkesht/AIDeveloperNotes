using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var runner = new DailyPostJobRunner(AppContext.BaseDirectory);
if (args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Publisher.Job                 Run as a long-running daily job process.");
    Console.WriteLine("  Publisher.Job --run-once      Run the job immediately, ignoring enabled/time/state.");
    return;
}

if (args.Any(arg => arg.Equals("--run-once", StringComparison.OrdinalIgnoreCase)))
{
    await runner.RunManualAsync();
    return;
}

await runner.RunAsync();

internal sealed class DailyPostJobRunner
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private readonly string _basePath;
    private readonly string _configPath;
    private readonly string _postsPath;
    private readonly string _picsPath;
    private readonly string _postedFilePath;
    private readonly string _stateFilePath;

    public DailyPostJobRunner(string basePath)
    {
        _basePath = basePath;
        _configPath = Path.Combine(_basePath, "config.json");
        _postsPath = Path.Combine(_basePath, "Posts");
        _picsPath = Path.Combine(_basePath, "Pics");
        _postedFilePath = Path.Combine(_basePath, "posted.txt");
        _stateFilePath = Path.Combine(_basePath, "daily-job-state.json");
    }

    public async Task RunAsync()
    {
        EnsureDefaultFiles();
        Console.WriteLine("Publisher daily job started.");

        while (true)
        {
            try
            {
                var config = LoadConfig();
                if (ShouldRunNow(config))
                {
                    await RunOnceAsync(config, updateDailyState: true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] Job check failed: {ex.Message}");
            }

            await Task.Delay(CheckInterval);
        }
    }

    public async Task RunManualAsync()
    {
        EnsureDefaultFiles();
        var config = LoadConfig();
        Console.WriteLine("Publisher daily job manual run started.");
        await RunOnceAsync(config, updateDailyState: false);
    }

    private void EnsureDefaultFiles()
    {
        Directory.CreateDirectory(_postsPath);
        Directory.CreateDirectory(_picsPath);

        if (!File.Exists(_postedFilePath))
        {
            File.WriteAllText(_postedFilePath, string.Empty, Encoding.UTF8);
        }
    }

    private AppConfig LoadConfig()
    {
        var json = File.ReadAllText(_configPath, Encoding.UTF8);
        AppConfig config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            var firstCharacters = json.Length <= 40 ? json : json[..40];
            throw new InvalidOperationException(
                $"Invalid JSON in config file '{_configPath}'. The file must start with '{{'. Current first characters: '{firstCharacters}'",
                ex);
        }

        config.Proxy ??= new ProxyConfig();
        config.DailyJob ??= new DailyJobConfig();
        return config;
    }

    private bool ShouldRunNow(AppConfig config)
    {
        var dailyJob = config.DailyJob;
        if (!dailyJob.Enabled)
        {
            return false;
        }

        if (!TimeOnly.TryParse(dailyJob.Time, out var scheduledTime))
        {
            Console.WriteLine("dailyJob.time is invalid. Use HH:mm, for example 09:00.");
            return false;
        }

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        if (LoadState().LastRunDate == today)
        {
            return false;
        }

        return TimeOnly.FromDateTime(now) >= scheduledTime;
    }

    private async Task RunOnceAsync(AppConfig config, bool updateDailyState)
    {
        var dailyJob = config.DailyJob;
        var bot = ResolveBot(config, dailyJob)
            ?? throw new InvalidOperationException("dailyJob bot was not found in config.json.");
        var channel = ResolveChannel(config, dailyJob)
            ?? throw new InvalidOperationException("dailyJob channel was not found in config.json.");

        var postedFiles = LoadPostedFiles();
        var targetCount = Math.Max(1, dailyJob.PostCount);
        var sentCount = 0;

        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] Running daily job.");

        foreach (var post in GetUnpostedPostFiles(postedFiles))
        {
            if (sentCount >= targetCount)
            {
                break;
            }

            var imagePath = FindPostImagePath(post.Name);
            if (imagePath is null)
            {
                Console.WriteLine($"Skipped {post.Name}: matching image was not found in Pics.");
                continue;
            }

            var postText = ReadPostContent(post.Path).Trim();
            if (string.IsNullOrWhiteSpace(postText))
            {
                Console.WriteLine($"Skipped {post.Name}: post text is empty.");
                continue;
            }

            var result = await SendPostContentAsync(config, bot, channel, postText, imagePath, dailyJob.UseProxy);
            if (!result.Success)
            {
                Console.WriteLine($"Failed {post.Name}: {result.ErrorMessage}");
                continue;
            }

            MarkAsPosted(post.Path);
            postedFiles.Add(post.Name);
            sentCount++;
            Console.WriteLine($"Sent {post.Name}.");
        }

        if (updateDailyState)
        {
            SaveState(new DailyJobState(DateOnly.FromDateTime(DateTime.Now)));
        }

        Console.WriteLine($"Daily job finished. Sent {sentCount} post(s).");
    }

    private HashSet<string> LoadPostedFiles()
    {
        var postedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(_postedFilePath, Encoding.UTF8))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2)
            {
                postedFiles.Add(Path.GetFileName(parts[1].Trim()));
            }
        }

        return postedFiles;
    }

    private IEnumerable<PostFileItem> GetUnpostedPostFiles(HashSet<string> postedFiles)
    {
        return Directory.EnumerateFiles(_postsPath, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => IsPostFileName(file.Name))
            .Where(file => !postedFiles.Contains(file.Name))
            .OrderBy(file => GetPostSortKey(file.Name))
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new PostFileItem(file.FullName, file.Name));
    }

    private static bool IsPostFileName(string fileName)
    {
        return Regex.IsMatch(fileName, @"(?i)^Post_\d+\.txt$", RegexOptions.CultureInvariant);
    }

    private static int GetPostSortKey(string fileName)
    {
        var match = Regex.Match(fileName, @"(?i)^Post_(\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var number)
            ? number
            : int.MaxValue;
    }

    private static string ReadPostContent(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length > 0 && Regex.IsMatch(lines[0].Trim(), "^#Post_\\d+$", RegexOptions.CultureInvariant))
        {
            return string.Join(Environment.NewLine, lines.Skip(1));
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    private string? FindPostImagePath(string postFileName)
    {
        var match = Regex.Match(postFileName, @"(?i)^Post_(\d+)\.txt$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var postNumberText = match.Groups[1].Value;
        var candidates = new List<string> { postNumberText };
        if (int.TryParse(postNumberText, out var postNumber))
        {
            candidates.Add(postNumber.ToString());
            candidates.Add(postNumber.ToString("000"));
        }

        foreach (var name in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif" })
            {
                var imagePath = Path.Combine(_picsPath, name + extension);
                if (File.Exists(imagePath))
                {
                    return imagePath;
                }
            }
        }

        return null;
    }

    private static BotConfig? ResolveBot(AppConfig config, DailyJobConfig dailyJob)
    {
        if (!string.IsNullOrWhiteSpace(dailyJob.BotId))
        {
            var botById = config.Bots.FirstOrDefault(bot => bot.BotId.Equals(dailyJob.BotId, StringComparison.OrdinalIgnoreCase));
            if (botById is not null)
            {
                return botById;
            }
        }

        if (!string.IsNullOrWhiteSpace(dailyJob.BotName))
        {
            return config.Bots.FirstOrDefault(bot => bot.Name.Equals(dailyJob.BotName, StringComparison.OrdinalIgnoreCase));
        }

        return config.Bots.FirstOrDefault();
    }

    private static ChannelConfig? ResolveChannel(AppConfig config, DailyJobConfig dailyJob)
    {
        if (!string.IsNullOrWhiteSpace(dailyJob.ChatId))
        {
            var channelById = config.Channels.FirstOrDefault(channel => channel.ChatId.Equals(dailyJob.ChatId, StringComparison.OrdinalIgnoreCase));
            if (channelById is not null)
            {
                return channelById;
            }
        }

        if (!string.IsNullOrWhiteSpace(dailyJob.ChannelTitle))
        {
            return config.Channels.FirstOrDefault(channel => channel.Title.Equals(dailyJob.ChannelTitle, StringComparison.OrdinalIgnoreCase));
        }

        return config.Channels.FirstOrDefault();
    }

    private async Task<TelegramSendResult> SendPostContentAsync(
        AppConfig config,
        BotConfig bot,
        ChannelConfig channel,
        string postText,
        string imagePath,
        bool useProxy)
    {
        using var httpClient = CreateTelegramHttpClient(config.Proxy, useProxy);
        if (postText.Length <= 1024)
        {
            return await SendTelegramPhotoAsync(httpClient, bot.BotId, channel.ChatId, imagePath, postText);
        }

        var photoResult = await SendTelegramPhotoAsync(httpClient, bot.BotId, channel.ChatId, imagePath, null);
        if (!photoResult.Success)
        {
            return photoResult;
        }

        return await SendTelegramMessageAsync(httpClient, bot.BotId, channel.ChatId, postText);
    }

    private static HttpClient CreateTelegramHttpClient(ProxyConfig proxyConfig, bool useProxy)
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

    private void MarkAsPosted(string filePath)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\t{Path.GetFileName(filePath)}{Environment.NewLine}";
        File.AppendAllText(_postedFilePath, line, Encoding.UTF8);
    }

    private DailyJobState LoadState()
    {
        if (!File.Exists(_stateFilePath))
        {
            return new DailyJobState(null);
        }

        var json = File.ReadAllText(_stateFilePath, Encoding.UTF8);
        return JsonSerializer.Deserialize<DailyJobState>(json) ?? new DailyJobState(null);
    }

    private void SaveState(DailyJobState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_stateFilePath, json, Encoding.UTF8);
    }
}

internal sealed class AppConfig
{
    public List<BotConfig> Bots { get; set; } = [];
    public List<ChannelConfig> Channels { get; set; } = [];
    public ProxyConfig Proxy { get; set; } = new();
    public DailyJobConfig DailyJob { get; set; } = new();
}

internal sealed class DailyJobConfig
{
    public bool Enabled { get; set; }
    public string Time { get; set; } = "09:00";
    public int PostCount { get; set; } = 2;
    public string BotName { get; set; } = string.Empty;
    public string BotId { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public bool UseProxy { get; set; } = true;
}

internal sealed class ProxyConfig
{
    public string Address { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

internal sealed class BotConfig
{
    public string Name { get; set; } = string.Empty;
    public string BotId { get; set; } = string.Empty;
}

internal sealed class ChannelConfig
{
    public string Title { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
}

internal sealed record DailyJobState(DateOnly? LastRunDate);

internal sealed record PostFileItem(string Path, string Name);

internal sealed record TelegramSendResult(bool Success, string ErrorMessage)
{
    public static TelegramSendResult Ok() => new(true, string.Empty);

    public static TelegramSendResult Fail(string errorMessage) => new(false, errorMessage);
}

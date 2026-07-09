using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var runner = new PublisherJobRunner(AppContext.BaseDirectory);
if (args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Publisher.Job                       Run all enabled scheduled jobs.");
    Console.WriteLine("  Publisher.Job --status              Print config/state diagnostics and exit.");
    Console.WriteLine("  Publisher.Job --run-posts-once      Run the Posts job immediately, ignoring enabled/time/state.");
    Console.WriteLine("  Publisher.Job --run-groq-once       Run the Groq article job immediately, ignoring enabled/time/state.");
    Console.WriteLine("  Publisher.Job --run-once            Backward-compatible alias for --run-posts-once.");
    return;
}

if (args.Any(arg => arg.Equals("--status", StringComparison.OrdinalIgnoreCase)))
{
    runner.PrintStatus();
    return;
}

if (args.Any(arg => arg.Equals("--run-groq-once", StringComparison.OrdinalIgnoreCase)))
{
    await runner.RunGroqArticleManualAsync();
    return;
}

if (args.Any(arg =>
        arg.Equals("--run-posts-once", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--run-once", StringComparison.OrdinalIgnoreCase)))
{
    await runner.RunPostsManualAsync();
    return;
}

await runner.RunAsync();

internal sealed class PublisherJobRunner
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
    private readonly string _basePath;
    private readonly string _configPath;
    private readonly string _postsPath;
    private readonly string _picsPath;
    private readonly string _newsPicsPath;
    private readonly string _promptPath;
    private readonly string _postedFilePath;
    private readonly JobStateStore _postsState;
    private readonly JobStateStore _groqState;

    public PublisherJobRunner(string basePath)
    {
        _basePath = basePath;
        _configPath = Path.Combine(_basePath, "config.json");
        _postsPath = Path.Combine(_basePath, "Posts");
        _picsPath = Path.Combine(_basePath, "Pics");
        _newsPicsPath = Path.Combine(_picsPath, "news");
        _promptPath = Path.Combine(_basePath, "Promp", "Groq-MakeArticle.md");
        _postedFilePath = Path.Combine(_basePath, "posted.txt");
        _postsState = new JobStateStore(
            Path.Combine(_basePath, "daily-job-state.json"),
            Path.Combine(_basePath, "daily-job.lock"));
        _groqState = new JobStateStore(
            Path.Combine(_basePath, "groq-article-job-state.json"),
            Path.Combine(_basePath, "groq-article-job.lock"));
    }

    public async Task RunAsync()
    {
        EnsureDefaultFiles();
        Console.WriteLine("Publisher jobs started.");
        PrintStatus();

        while (true)
        {
            var config = LoadConfig();

            await TryRunScheduledJobAsync(
                "daily posts",
                config.DailyJob,
                _postsState,
                () => RunPostsJobAsync(config, updateDailyState: true));

            await TryRunScheduledJobAsync(
                "groq article",
                config.GroqArticleJob,
                _groqState,
                () => RunGroqArticleJobAsync(config, updateDailyState: true));

            await Task.Delay(CheckInterval);
        }
    }

    public async Task RunPostsManualAsync()
    {
        EnsureDefaultFiles();
        var config = LoadConfig();
        Console.WriteLine("Manual daily posts job started.");
        await RunPostsJobAsync(config, updateDailyState: false);
    }

    public async Task RunGroqArticleManualAsync()
    {
        EnsureDefaultFiles();
        var config = LoadConfig();
        Console.WriteLine("Manual Groq article job started.");
        try
        {
            await RunGroqArticleJobAsync(config, updateDailyState: false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Manual Groq article job failed: {ex.Message}");
            if (ex.InnerException is not null)
            {
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }
        }
    }

    public void PrintStatus()
    {
        EnsureDefaultFiles();
        var config = LoadConfig();
        var postedFiles = LoadPostedFiles();

        Console.WriteLine("Publisher job status:");
        Console.WriteLine($"  Base path: {_basePath}");
        Console.WriteLine($"  Config path: {_configPath}");
        Console.WriteLine($"  Server now: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Console.WriteLine($"  Posts folder exists: {Directory.Exists(_postsPath)}");
        Console.WriteLine($"  Pics folder exists: {Directory.Exists(_picsPath)}");
        Console.WriteLine($"  Groq prompt path: {_promptPath}");
        Console.WriteLine($"  Groq prompt exists: {File.Exists(_promptPath)}");
        Console.WriteLine($"  Unposted posts: {GetUnpostedPostFiles(postedFiles).Count()}");
        PrintJobStatus("daily posts", config.DailyJob, _postsState);
        PrintJobStatus("groq article", config.GroqArticleJob, _groqState);
    }

    private async Task TryRunScheduledJobAsync(
        string jobName,
        ScheduledJobConfig scheduledJob,
        JobStateStore stateStore,
        Func<Task> runJobAsync)
    {
        try
        {
            if (ShouldRunNow(scheduledJob, stateStore))
            {
                await runJobAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {jobName} job check failed: {ex.Message}");
        }
    }

    private void PrintJobStatus(string jobName, ScheduledJobConfig scheduledJob, JobStateStore stateStore)
    {
        var state = stateStore.Load();
        Console.WriteLine($"{jobName} job:");
        Console.WriteLine($"  Enabled: {scheduledJob.Enabled}");
        Console.WriteLine($"  Scheduled time: {scheduledJob.Time}");
        Console.WriteLine($"  Last run date: {state.LastRunDate?.ToString() ?? "(never)"}");
        Console.WriteLine($"  Last attempt started at: {state.LastAttemptStartedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(never)"}");
        Console.WriteLine($"  Last attempt finished at: {state.LastAttemptFinishedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(never)"}");
        Console.WriteLine($"  Last sent count: {state.LastSentCount}");
        Console.WriteLine($"  Should run now: {ShouldRunNow(scheduledJob, stateStore)}");
    }

    private void EnsureDefaultFiles()
    {
        Directory.CreateDirectory(_postsPath);
        Directory.CreateDirectory(_picsPath);
        Directory.CreateDirectory(_newsPicsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_promptPath) ?? _basePath);

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
        config.GroqArticleJob ??= new GroqArticleJobConfig();
        config.Groq ??= new GroqConfig();
        return config;
    }

    private static bool ShouldRunNow(ScheduledJobConfig scheduledJob, JobStateStore stateStore)
    {
        if (!scheduledJob.Enabled)
        {
            return false;
        }

        if (!TimeOnly.TryParse(scheduledJob.Time, out var scheduledTime))
        {
            Console.WriteLine($"Invalid job time '{scheduledJob.Time}'. Use HH:mm, for example 09:00.");
            return false;
        }

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        if (stateStore.Load().LastRunDate == today)
        {
            return false;
        }

        return TimeOnly.FromDateTime(now) >= scheduledTime;
    }

    private async Task RunPostsJobAsync(AppConfig config, bool updateDailyState)
    {
        var dailyJob = config.DailyJob;
        var bot = ResolveBot(config, dailyJob)
            ?? throw new InvalidOperationException("dailyJob bot was not found in config.json.");
        var channel = ResolveChannel(config, dailyJob)
            ?? throw new InvalidOperationException("dailyJob channel was not found in config.json.");

        using var runLock = _postsState.TryAcquireLock();
        if (runLock is null)
        {
            Console.WriteLine("Another daily posts job instance is already running. Skipping this attempt.");
            return;
        }

        EnsurePersistenceFilesWritable(_postsState);

        if (updateDailyState)
        {
            _postsState.SaveStarted();
        }

        var telegram = new TelegramPublisher(config.Proxy);
        var postedFiles = LoadPostedFiles();
        var targetCount = Math.Max(1, dailyJob.PostCount);
        var sentCount = 0;

        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] Running daily posts job.");

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

            var result = await telegram.SendPostAsync(bot, channel, postText, imagePath, dailyJob.UseProxy);
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
            _postsState.SaveFinished(sentCount);
        }

        Console.WriteLine($"Daily posts job finished. Sent {sentCount} post(s).");
    }

    private async Task RunGroqArticleJobAsync(AppConfig config, bool updateDailyState)
    {
        var groqJob = config.GroqArticleJob;
        var bot = ResolveBot(config, groqJob)
            ?? throw new InvalidOperationException("groqArticleJob bot was not found in config.json.");
        var channel = ResolveChannel(config, groqJob)
            ?? throw new InvalidOperationException("groqArticleJob channel was not found in config.json.");

        using var runLock = _groqState.TryAcquireLock();
        if (runLock is null)
        {
            Console.WriteLine("Another Groq article job instance is already running. Skipping this attempt.");
            return;
        }

        EnsurePersistenceFilesWritable(_groqState);

        if (updateDailyState)
        {
            _groqState.SaveStarted();
        }

        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] Running Groq article job.");

        var promptPath = ResolvePromptPath(groqJob.PromptPath);
        var prompt = File.ReadAllText(promptPath, Encoding.UTF8).Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new InvalidOperationException($"Groq prompt file is empty: {promptPath}");
        }

        if (groqJob.Feeds.Count == 0)
        {
            throw new InvalidOperationException("groqArticleJob.feeds is empty. Add at least one RSS feed URL.");
        }

        var groq = new GroqArticleGenerator(config.Groq, config.Proxy);
        var rssReader = new RssFeedReader(config.Proxy);
        var imageDownloader = new NewsImageDownloader(config.Proxy, _newsPicsPath);
        var telegram = new TelegramPublisher(config.Proxy);
        var targetCount = groqJob.Feeds.Count;
        var sentCount = 0;

        foreach (var feed in groqJob.Feeds)
        {
            var feedUrl = feed.Url.Trim();
            if (string.IsNullOrWhiteSpace(feedUrl))
            {
                Console.WriteLine("Skipped empty RSS feed URL.");
                continue;
            }

            var latestItem = await rssReader.GetLatestItemAsync(feedUrl, groqJob.UseProxy);
            if (latestItem is null)
            {
                Console.WriteLine($"Skipped RSS feed because no item was found: {feedUrl}");
                continue;
            }

            if (sentCount > 0)
            {
                await Task.Delay(TimeSpan.FromMinutes(2));
            }

            var generatedPost = await groq.GenerateTelegramPostAsync(BuildRssSummaryPrompt(prompt, latestItem), groqJob.UseProxy);
            if (string.IsNullOrWhiteSpace(generatedPost))
            {
                throw new InvalidOperationException($"Groq returned an empty article for RSS item: {latestItem.Link}");
            }

            var imagePath = groqJob.DownloadImages
                ? await imageDownloader.DownloadAsync(latestItem.ImageUrl, groqJob.UseProxy)
                : null;
            var result = await telegram.SendPostAsync(bot, channel, generatedPost.Trim(), imagePath, groqJob.UseProxy);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Telegram send failed for Groq article: {result.ErrorMessage}");
            }

            sentCount++;
            Console.WriteLine($"Sent Groq RSS article {sentCount}/{targetCount}: {latestItem.Title}");
        }

        if (updateDailyState)
        {
            _groqState.SaveFinished(sentCount);
        }

        Console.WriteLine($"Groq article job finished. Sent {sentCount} post(s).");
    }

    private static string BuildRssSummaryPrompt(string promptTemplate, RssFeedItem item)
    {
        var publishedAt = item.PublishedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Unknown";
        return $"""
{promptTemplate}

RSS item to summarize:
Title: {item.Title}
Source: {item.SourceTitle}
PublishedAt: {publishedAt}
Link: {item.Link}
ImageUrl: {item.ImageUrl ?? "None"}

Content:
{item.Summary}
""";
    }

    private void EnsurePersistenceFilesWritable(JobStateStore stateStore)
    {
        using (File.Open(_postedFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
        }

        stateStore.EnsureWritable();
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

    private string ResolvePromptPath(string promptPath)
    {
        if (string.IsNullOrWhiteSpace(promptPath))
        {
            return _promptPath;
        }

        return Path.IsPathRooted(promptPath)
            ? promptPath
            : Path.Combine(_basePath, promptPath);
    }

    private static BotConfig? ResolveBot(AppConfig config, TelegramTargetConfig target)
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

    private static ChannelConfig? ResolveChannel(AppConfig config, TelegramTargetConfig target)
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

    private void MarkAsPosted(string filePath)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\t{Path.GetFileName(filePath)}{Environment.NewLine}";
        File.AppendAllText(_postedFilePath, line, Encoding.UTF8);
    }
}

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

internal sealed class AppConfig
{
    public List<BotConfig> Bots { get; set; } = [];
    public List<ChannelConfig> Channels { get; set; } = [];
    public ProxyConfig Proxy { get; set; } = new();
    public DailyJobConfig DailyJob { get; set; } = new();
    public GroqArticleJobConfig GroqArticleJob { get; set; } = new();
    public GroqConfig Groq { get; set; } = new();
}

internal abstract class ScheduledJobConfig : TelegramTargetConfig
{
    public bool Enabled { get; set; }
    public string Time { get; set; } = "09:00";
}

internal abstract class TelegramTargetConfig
{
    public string BotName { get; set; } = string.Empty;
    public string BotId { get; set; } = string.Empty;
    public string ChannelTitle { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public bool UseProxy { get; set; }
}

internal sealed class DailyJobConfig : ScheduledJobConfig
{
    public int PostCount { get; set; } = 2;
}

internal sealed class GroqArticleJobConfig : ScheduledJobConfig
{
    public string PromptPath { get; set; } = Path.Combine("Promp", "Groq-MakeArticle.md");
    public bool DownloadImages { get; set; } = true;
    public List<RssFeedConfig> Feeds { get; set; } = [];
}

internal sealed class RssFeedConfig
{
    public string Url { get; set; } = string.Empty;
}

internal sealed class GroqConfig
{
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string ApiKeyEnvironmentVariable { get; set; } = "GROQ_API_KEY";
    public string Model { get; set; } = "llama-3.3-70b-versatile";
    public int MaxCompletionTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.7;
    public int TimeoutSeconds { get; set; } = 300;
    public string SystemPrompt { get; set; } =
        "You generate Telegram-ready Persian posts. Return only the final Telegram post text. Use Telegram-compatible HTML only when formatting is needed.";
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

internal sealed class DailyJobState
{
    public DateOnly? LastRunDate { get; set; }
    public DateTimeOffset? LastAttemptStartedAt { get; set; }
    public DateTimeOffset? LastAttemptFinishedAt { get; set; }
    public int LastSentCount { get; set; }
}

internal sealed record PostFileItem(string Path, string Name);

internal sealed record RssFeedItem(
    string SourceTitle,
    string Title,
    string Link,
    string? ImageUrl,
    string Summary,
    DateTimeOffset? PublishedAt);

internal sealed record TelegramSendResult(bool Success, string ErrorMessage)
{
    public static TelegramSendResult Ok() => new(true, string.Empty);

    public static TelegramSendResult Fail(string errorMessage) => new(false, errorMessage);
}

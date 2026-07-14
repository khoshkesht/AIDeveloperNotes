using System.Text;
using System.Text.Json;

var runner = new PublisherJobRunner(AppContext.BaseDirectory);
if (args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Publisher.Job                       Run all enabled scheduled jobs.");
    Console.WriteLine("  Publisher.Job --status              Print config/state diagnostics and exit.");
    Console.WriteLine("  Publisher.Job --run-posts-once      Run the Posts job immediately, ignoring enabled/time/state.");
    Console.WriteLine("  Publisher.Job --run-groq-once       Run the Groq article job immediately, ignoring enabled/time/state.");
    Console.WriteLine("  Publisher.Job --run-telegram-data-provider-once");
    Console.WriteLine("                                      Read latest Telegram channel posts and print them.");
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

if (args.Any(arg => arg.Equals("--run-telegram-data-provider-once", StringComparison.OrdinalIgnoreCase)))
{
    await runner.RunTelegramDataProviderManualAsync();
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
    private readonly JobStateStore _telegramDataProviderState;

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
        _telegramDataProviderState = new JobStateStore(
            Path.Combine(_basePath, "telegram-data-provider-job-state.json"),
            Path.Combine(_basePath, "telegram-data-provider-job.lock"));
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
                () => CreateDailyPostsJob(config).RunAsync(updateDailyState: true));

            await TryRunScheduledJobAsync(
                "groq article",
                config.GroqArticleJob,
                _groqState,
                () => CreateGroqArticleJob(config).RunAsync(updateDailyState: true));

            await TryRunIntervalJobAsync(
                "telegram data provider",
                config.TelegramDataProvider,
                _telegramDataProviderState,
                () => new TelegramDataProviderJob(config, _basePath).RunAsync());

            await Task.Delay(CheckInterval);
        }
    }

    public async Task RunPostsManualAsync()
    {
        EnsureDefaultFiles();
        var config = LoadConfig();
        Console.WriteLine("Manual daily posts job started.");
        await CreateDailyPostsJob(config).RunAsync(updateDailyState: false);
    }

    public async Task RunGroqArticleManualAsync()
    {
        EnsureDefaultFiles();
        var config = LoadConfig();
        Console.WriteLine("Manual Groq article job started.");
        try
        {
            await CreateGroqArticleJob(config).RunAsync(updateDailyState: false);
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

    public async Task RunTelegramDataProviderManualAsync()
    {
        EnsureDefaultFiles();
        var config = LoadConfig();
        await new TelegramDataProviderJob(config, _basePath).RunAsync();
    }

    public void PrintStatus()
    {
        EnsureDefaultFiles();
        var config = LoadConfig();
        var postedFiles = DailyPostsJob.LoadPostedFiles(_postedFilePath);

        Console.WriteLine("Publisher job status:");
        Console.WriteLine($"  Base path: {_basePath}");
        Console.WriteLine($"  Config path: {_configPath}");
        Console.WriteLine($"  Server now: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Console.WriteLine($"  Posts folder exists: {Directory.Exists(_postsPath)}");
        Console.WriteLine($"  Pics folder exists: {Directory.Exists(_picsPath)}");
        Console.WriteLine($"  Groq prompt path: {_promptPath}");
        Console.WriteLine($"  Groq prompt exists: {File.Exists(_promptPath)}");
        Console.WriteLine($"  Unposted posts: {DailyPostsJob.GetUnpostedPostFiles(_postsPath, postedFiles).Count()}");
        PrintJobStatus("daily posts", config.DailyJob, _postsState);
        PrintJobStatus("groq article", config.GroqArticleJob, _groqState);
        PrintTelegramDataProviderStatus(config.TelegramDataProvider, _telegramDataProviderState);
    }

    private DailyPostsJob CreateDailyPostsJob(AppConfig config) =>
        new(config, _postsPath, _picsPath, _postedFilePath, _postsState);

    private ArticleJob CreateGroqArticleJob(AppConfig config) =>
        new(config, _basePath, _promptPath, _newsPicsPath, _postedFilePath, _groqState);

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

    private async Task TryRunIntervalJobAsync(
        string jobName,
        TelegramDataProviderConfig jobConfig,
        JobStateStore stateStore,
        Func<Task> runJobAsync)
    {
        try
        {
            if (!ShouldRunTelegramDataProviderNow(jobConfig, stateStore))
            {
                return;
            }

            using var runLock = stateStore.TryAcquireLock();
            if (runLock is null)
            {
                Console.WriteLine("Another telegram data provider job instance is already running. Skipping this attempt.");
                return;
            }

            stateStore.SaveStarted();
            await runJobAsync();
            stateStore.SaveFinished(0);
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

    private void PrintTelegramDataProviderStatus(TelegramDataProviderConfig jobConfig, JobStateStore stateStore)
    {
        var state = stateStore.Load();
        Console.WriteLine("telegram data provider job:");
        Console.WriteLine($"  Enabled: {jobConfig.Enabled}");
        Console.WriteLine($"  Window: {jobConfig.StartTime}-{jobConfig.EndTime}");
        Console.WriteLine($"  Interval minutes: {jobConfig.IntervalMinutes}");
        Console.WriteLine($"  Last attempt started at: {state.LastAttemptStartedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(never)"}");
        Console.WriteLine($"  Last attempt finished at: {state.LastAttemptFinishedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(never)"}");
        Console.WriteLine($"  Should run now: {ShouldRunTelegramDataProviderNow(jobConfig, stateStore)}");
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
        config.TelegramDataProvider ??= new TelegramDataProviderConfig();
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

    private static bool ShouldRunTelegramDataProviderNow(TelegramDataProviderConfig jobConfig, JobStateStore stateStore)
    {
        if (!jobConfig.Enabled)
        {
            return false;
        }

        if (!TimeOnly.TryParse(jobConfig.StartTime, out var startTime))
        {
            Console.WriteLine($"Invalid telegramDataProvider.startTime '{jobConfig.StartTime}'. Use HH:mm, for example 06:00.");
            return false;
        }

        if (!TimeOnly.TryParse(jobConfig.EndTime, out var endTime))
        {
            Console.WriteLine($"Invalid telegramDataProvider.endTime '{jobConfig.EndTime}'. Use HH:mm, for example 23:59.");
            return false;
        }

        var nowTime = TimeOnly.FromDateTime(DateTime.Now);
        if (nowTime < startTime || nowTime > endTime)
        {
            return false;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, jobConfig.IntervalMinutes));
        var state = stateStore.Load();
        return state.LastAttemptStartedAt is null ||
            DateTimeOffset.Now - state.LastAttemptStartedAt.Value >= interval;
    }
}

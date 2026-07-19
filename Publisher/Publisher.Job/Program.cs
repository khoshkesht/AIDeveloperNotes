using Hangfire;
using Hangfire.InMemory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var runner = new PublisherJobRunner(AppContext.BaseDirectory);
if (args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  Publisher.Job                       Run Hangfire worker and scheduled jobs.");
    Console.WriteLine("  Publisher.Job --status              Print config/state diagnostics and exit.");
    Console.WriteLine("  Publisher.Job --run-posts-once      Run the Posts job immediately, ignoring schedule/state.");
    Console.WriteLine("  Publisher.Job --run-groq-once       Run the Groq article job immediately, ignoring schedule/state.");
    Console.WriteLine("  Publisher.Job --run-telegram-data-provider-once");
    Console.WriteLine("                                      Read latest Telegram channel posts and send summaries immediately.");
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

public sealed class PublisherJobRunner
{
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
        var config = LoadConfig();

        GlobalConfiguration.Configuration.UseInMemoryStorage();
        ConfigureRecurringJobs(config);

        using var server = new BackgroundJobServer(new BackgroundJobServerOptions
        {
            WorkerCount = Math.Max(1, config.Hangfire.WorkerCount)
        });

        Console.WriteLine("Publisher Hangfire worker started.");
        PrintStatus();
        await Task.Delay(Timeout.InfiniteTimeSpan);
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
        Console.WriteLine($"  Hangfire storage: InMemory");
        Console.WriteLine($"  Hangfire workers: {Math.Max(1, config.Hangfire.WorkerCount)}");
        Console.WriteLine($"  Posts folder exists: {Directory.Exists(_postsPath)}");
        Console.WriteLine($"  Pics folder exists: {Directory.Exists(_picsPath)}");
        Console.WriteLine($"  Groq prompt path: {_promptPath}");
        Console.WriteLine($"  Groq prompt exists: {File.Exists(_promptPath)}");
        PrintGroqKeyStatus(config.Groq);
        Console.WriteLine($"  Unposted posts: {DailyPostsJob.GetUnpostedPostFiles(_postsPath, postedFiles).Count()}");
        PrintJobStatus("daily posts", config.DailyJob, _postsState);
        PrintJobStatus("groq article", config.GroqArticleJob, _groqState);
        PrintTelegramDataProviderStatus(config.TelegramDataProvider, _telegramDataProviderState);
    }

    public static async Task RunPostsScheduledAsync(string basePath)
    {
        var runner = new PublisherJobRunner(basePath);
        runner.EnsureDefaultFiles();
        var config = runner.LoadConfig();
        if (!config.DailyJob.Enabled)
        {
            Console.WriteLine("Skipped daily posts job because dailyJob.enabled=false.");
            return;
        }

        await runner.CreateDailyPostsJob(config).RunAsync(updateDailyState: true);
    }

    public static async Task RunGroqArticleScheduledAsync(string basePath)
    {
        var runner = new PublisherJobRunner(basePath);
        runner.EnsureDefaultFiles();
        var config = runner.LoadConfig();
        if (!config.GroqArticleJob.Enabled)
        {
            Console.WriteLine("Skipped Groq article job because groqArticleJob.enabled=false.");
            return;
        }

        await runner.CreateGroqArticleJob(config).RunAsync(updateDailyState: true);
    }

    public static async Task RunTelegramDataProviderScheduledAsync(string basePath)
    {
        var runner = new PublisherJobRunner(basePath);
        runner.EnsureDefaultFiles();
        var config = runner.LoadConfig();
        if (!config.TelegramDataProvider.Enabled)
        {
            Console.WriteLine("Skipped telegram data provider job because telegramDataProvider.enabled=false.");
            return;
        }

        using var runLock = runner._telegramDataProviderState.TryAcquireLock();
        if (runLock is null)
        {
            Console.WriteLine("Another telegram data provider job instance is already running. Skipping this attempt.");
            return;
        }

        runner._telegramDataProviderState.SaveStarted();
        await new TelegramDataProviderJob(config, basePath).RunAsync();
        runner._telegramDataProviderState.SaveFinished(0);
    }

    private void ConfigureRecurringJobs(AppConfig config)
    {
        if (config.DailyJob.Enabled)
        {
            RecurringJob.AddOrUpdate(
                "daily-posts",
                () => RunPostsScheduledAsync(_basePath),
                NormalizeCron(config.DailyJob.Cron, "dailyJob.cron"),
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });
        }
        else
        {
            RecurringJob.RemoveIfExists("daily-posts");
        }

        if (config.GroqArticleJob.Enabled)
        {
            RecurringJob.AddOrUpdate(
                "groq-article",
                () => RunGroqArticleScheduledAsync(_basePath),
                NormalizeCron(config.GroqArticleJob.Cron, "groqArticleJob.cron"),
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });
        }
        else
        {
            RecurringJob.RemoveIfExists("groq-article");
        }

        if (config.TelegramDataProvider.Enabled)
        {
            RecurringJob.AddOrUpdate(
                "telegram-data-provider",
                () => RunTelegramDataProviderScheduledAsync(_basePath),
                NormalizeCron(config.TelegramDataProvider.Cron, "telegramDataProvider.cron"),
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });
        }
        else
        {
            RecurringJob.RemoveIfExists("telegram-data-provider");
        }
    }

    private DailyPostsJob CreateDailyPostsJob(AppConfig config) =>
        new(config, _postsPath, _picsPath, _postedFilePath, _postsState);

    private ArticleJob CreateGroqArticleJob(AppConfig config) =>
        new(config, _basePath, _promptPath, _newsPicsPath, _postedFilePath, _groqState);

    private void PrintJobStatus(string jobName, ScheduledJobConfig scheduledJob, JobStateStore stateStore)
    {
        var state = stateStore.Load();
        Console.WriteLine($"{jobName} job:");
        Console.WriteLine($"  Enabled: {scheduledJob.Enabled}");
        Console.WriteLine($"  Hangfire cron: {NormalizeCron(scheduledJob.Cron, $"{jobName}.cron")}");
        Console.WriteLine($"  Last run date: {state.LastRunDate?.ToString() ?? "(never)"}");
        Console.WriteLine($"  Last attempt started at: {state.LastAttemptStartedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(never)"}");
        Console.WriteLine($"  Last attempt finished at: {state.LastAttemptFinishedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(never)"}");
        Console.WriteLine($"  Last sent count: {state.LastSentCount}");
    }

    private void PrintTelegramDataProviderStatus(TelegramDataProviderConfig jobConfig, JobStateStore stateStore)
    {
        var state = stateStore.Load();
        Console.WriteLine("telegram data provider job:");
        Console.WriteLine($"  Enabled: {jobConfig.Enabled}");
        Console.WriteLine($"  Hangfire cron: {NormalizeCron(jobConfig.Cron, "telegramDataProvider.cron")}");
        Console.WriteLine($"  Last attempt started at: {state.LastAttemptStartedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(never)"}");
        Console.WriteLine($"  Last attempt finished at: {state.LastAttemptFinishedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(never)"}");
    }

    private static void PrintGroqKeyStatus(GroqConfig groqConfig)
    {
        var configKeys = groqConfig.ApiKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .ToList();
        var legacyConfigKey = string.IsNullOrWhiteSpace(groqConfig.ApiKey) ? null : groqConfig.ApiKey.Trim();
        var environmentApiKey = string.IsNullOrWhiteSpace(groqConfig.ApiKeyEnvironmentVariable)
            ? null
            : Environment.GetEnvironmentVariable(groqConfig.ApiKeyEnvironmentVariable)?.Trim();

        Console.WriteLine("groq api keys:");
        Console.WriteLine($"  config apiKeys count: {configKeys.Count}");
        Console.WriteLine($"  legacy config apiKey set: {!string.IsNullOrWhiteSpace(legacyConfigKey)}");
        Console.WriteLine($"  env variable name: {groqConfig.ApiKeyEnvironmentVariable}");
        Console.WriteLine($"  env api key set: {!string.IsNullOrWhiteSpace(environmentApiKey)}");
        Console.WriteLine($"  usable distinct keys: {BuildGroqKeyFingerprints(configKeys, legacyConfigKey, environmentApiKey)}");
    }

    private static string BuildGroqKeyFingerprints(
        List<string> configKeys,
        string? legacyConfigKey,
        string? environmentApiKey)
    {
        var keys = new List<string>();
        keys.AddRange(configKeys);

        if (!string.IsNullOrWhiteSpace(legacyConfigKey))
        {
            keys.Add(legacyConfigKey);
        }

        if (!string.IsNullOrWhiteSpace(environmentApiKey))
        {
            keys.Add(environmentApiKey);
        }

        var fingerprints = keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.Ordinal)
            .Select(CreateSecretFingerprint)
            .ToList();

        return fingerprints.Count == 0 ? "(none)" : string.Join(", ", fingerprints);
    }

    private static string CreateSecretFingerprint(string value)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
        var suffixLength = Math.Min(4, value.Length);
        return $"sha256:{hash}...{value[^suffixLength..]}";
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
        config.Hangfire ??= new HangfireConfig();
        config.DailyJob ??= new DailyJobConfig();
        config.GroqArticleJob ??= new GroqArticleJobConfig();
        config.TelegramDataProvider ??= new TelegramDataProviderConfig();
        config.Groq ??= new GroqConfig();
        config.Groq.ApiKeys ??= [];
        return config;
    }

    private static string NormalizeCron(string cron, string configPath)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            throw new InvalidOperationException($"{configPath} is empty. Use a Hangfire cron expression, for example '*/30 18-23 * * *'.");
        }

        return cron.Trim();
    }
}

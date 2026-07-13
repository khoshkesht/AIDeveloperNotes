internal sealed class ArticleJob
{
    private readonly AppConfig _config;
    private readonly string _basePath;
    private readonly string _defaultPromptPath;
    private readonly string _newsPicsPath;
    private readonly string _postedFilePath;
    private readonly JobStateStore _stateStore;

    public ArticleJob(
        AppConfig config,
        string basePath,
        string defaultPromptPath,
        string newsPicsPath,
        string postedFilePath,
        JobStateStore stateStore)
    {
        _config = config;
        _basePath = basePath;
        _defaultPromptPath = defaultPromptPath;
        _newsPicsPath = newsPicsPath;
        _postedFilePath = postedFilePath;
        _stateStore = stateStore;
    }

    public async Task RunAsync(bool updateDailyState)
    {
        var groqJob = _config.GroqArticleJob;
        var bot = ConfigResolver.ResolveBot(_config, groqJob)
            ?? throw new InvalidOperationException("groqArticleJob bot was not found in config.json.");
        var channel = ConfigResolver.ResolveChannel(_config, groqJob)
            ?? throw new InvalidOperationException("groqArticleJob channel was not found in config.json.");

        using var runLock = _stateStore.TryAcquireLock();
        if (runLock is null)
        {
            Console.WriteLine("Another Groq article job instance is already running. Skipping this attempt.");
            return;
        }

        EnsurePersistenceFilesWritable(_postedFilePath, _stateStore);

        if (updateDailyState)
        {
            _stateStore.SaveStarted();
        }

        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] Running Groq article job.");

        if (groqJob.Feeds.Count == 0)
        {
            throw new InvalidOperationException("groqArticleJob.feeds is empty. Add at least one RSS feed URL.");
        }

        var promptPath = ResolvePromptPath(groqJob.PromptPath);
        var telegramPostService = new TelegramPostService(_config.Groq, _config.Proxy);
        var rssReader = new RssFeedReader(_config.Proxy);
        var imageDownloader = new NewsImageDownloader(_config.Proxy, _newsPicsPath);
        var telegram = new TelegramPublisher(_config.Proxy);
        var targetCount = groqJob.Feeds.Count;
        var sentCount = 0;
        var itemsToSend = new List<RssFeedItem>();

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

            itemsToSend.Add(latestItem);
        }

        if (itemsToSend.Count == 0)
        {
            Console.WriteLine("Groq article job finished. Sent 0 post(s).");
            if (updateDailyState)
            {
                _stateStore.SaveFinished(0);
            }

            return;
        }

        var generatedPosts = await telegramPostService.GenerateTelegramPostsAsync(
            new GroqTelegramPostRequest(promptPath, itemsToSend),
            groqJob.UseProxy);
        if (generatedPosts.Count != itemsToSend.Count)
        {
            throw new InvalidOperationException(
                $"Groq returned {generatedPosts.Count} Telegram post(s), but {itemsToSend.Count} RSS item(s) were provided.");
        }

        for (var index = 0; index < generatedPosts.Count; index++)
        {
            var item = itemsToSend[index];
            var postText = generatedPosts[index].Trim();
            if (string.IsNullOrWhiteSpace(postText))
            {
                throw new InvalidOperationException($"Groq returned an empty article for RSS item: {item.Link}");
            }

            if (sentCount > 0)
            {
                await Task.Delay(TimeSpan.FromMinutes(2));
            }

            var imagePath = groqJob.DownloadImages
                ? await imageDownloader.DownloadAsync(item.ImageUrl, groqJob.UseProxy)
                : null;
            var result = await telegram.SendPostAsync(bot, channel, postText, imagePath, groqJob.UseProxy);
            if (!result.Success)
            {
                throw new InvalidOperationException($"Telegram send failed for Groq article: {result.ErrorMessage}");
            }

            sentCount++;
            Console.WriteLine($"Sent Groq RSS article {sentCount}/{targetCount}: {item.Title}");
        }

        if (updateDailyState)
        {
            _stateStore.SaveFinished(sentCount);
        }

        Console.WriteLine($"Groq article job finished. Sent {sentCount} post(s).");
    }

    private static void EnsurePersistenceFilesWritable(string postedFilePath, JobStateStore stateStore)
    {
        using (File.Open(postedFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
        }

        stateStore.EnsureWritable();
    }

    private string ResolvePromptPath(string promptPath)
    {
        if (string.IsNullOrWhiteSpace(promptPath))
        {
            return _defaultPromptPath;
        }

        return Path.IsPathRooted(promptPath)
            ? promptPath
            : Path.Combine(_basePath, promptPath);
    }
}

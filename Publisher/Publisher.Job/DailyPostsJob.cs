using System.Text;
using System.Text.RegularExpressions;

internal sealed class DailyPostsJob
{
    private readonly AppConfig _config;
    private readonly string _postsPath;
    private readonly string _picsPath;
    private readonly string _postedFilePath;
    private readonly JobStateStore _stateStore;

    public DailyPostsJob(AppConfig config, string postsPath, string picsPath, string postedFilePath, JobStateStore stateStore)
    {
        _config = config;
        _postsPath = postsPath;
        _picsPath = picsPath;
        _postedFilePath = postedFilePath;
        _stateStore = stateStore;
    }

    public async Task RunAsync(bool updateDailyState)
    {
        var dailyJob = _config.DailyJob;
        var bot = ConfigResolver.ResolveBot(_config, dailyJob)
            ?? throw new InvalidOperationException("dailyJob bot was not found in config.json.");
        var channel = ConfigResolver.ResolveChannel(_config, dailyJob)
            ?? throw new InvalidOperationException("dailyJob channel was not found in config.json.");

        using var runLock = _stateStore.TryAcquireLock();
        if (runLock is null)
        {
            Console.WriteLine("Another daily posts job instance is already running. Skipping this attempt.");
            return;
        }

        EnsurePersistenceFilesWritable(_postedFilePath, _stateStore);

        if (updateDailyState)
        {
            _stateStore.SaveStarted();
        }

        var telegram = new TelegramPublisher(_config.Proxy);
        var postedFiles = LoadPostedFiles(_postedFilePath);
        var targetCount = Math.Max(1, dailyJob.PostCount);
        var sentCount = 0;

        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] Running daily posts job.");

        foreach (var post in GetUnpostedPostFiles(_postsPath, postedFiles))
        {
            if (sentCount >= targetCount)
            {
                break;
            }

            var imagePath = FindPostImagePath(_picsPath, post.Name);
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

            MarkAsPosted(_postedFilePath, post.Path);
            postedFiles.Add(post.Name);
            sentCount++;
            Console.WriteLine($"Sent {post.Name}.");
        }

        if (updateDailyState)
        {
            _stateStore.SaveFinished(sentCount);
        }

        Console.WriteLine($"Daily posts job finished. Sent {sentCount} post(s).");
    }

    public static HashSet<string> LoadPostedFiles(string postedFilePath)
    {
        var postedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadLines(postedFilePath, Encoding.UTF8))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2)
            {
                postedFiles.Add(Path.GetFileName(parts[1].Trim()));
            }
        }

        return postedFiles;
    }

    public static IEnumerable<PostFileItem> GetUnpostedPostFiles(string postsPath, HashSet<string> postedFiles)
    {
        return Directory.EnumerateFiles(postsPath, "*.txt", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(file => IsPostFileName(file.Name))
            .Where(file => !postedFiles.Contains(file.Name))
            .OrderBy(file => GetPostSortKey(file.Name))
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new PostFileItem(file.FullName, file.Name));
    }

    private static void EnsurePersistenceFilesWritable(string postedFilePath, JobStateStore stateStore)
    {
        using (File.Open(postedFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
        }

        stateStore.EnsureWritable();
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

    private static string? FindPostImagePath(string picsPath, string postFileName)
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
                var imagePath = Path.Combine(picsPath, name + extension);
                if (File.Exists(imagePath))
                {
                    return imagePath;
                }
            }
        }

        return null;
    }

    private static void MarkAsPosted(string postedFilePath, string filePath)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\t{Path.GetFileName(filePath)}{Environment.NewLine}";
        File.AppendAllText(postedFilePath, line, Encoding.UTF8);
    }
}

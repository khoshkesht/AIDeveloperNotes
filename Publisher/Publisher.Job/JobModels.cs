internal sealed class AppConfig
{
    public List<BotConfig> Bots { get; set; } = [];
    public ProxyConfig Proxy { get; set; } = new();
    public DailyJobConfig DailyJob { get; set; } = new();
    public GroqArticleJobConfig GroqArticleJob { get; set; } = new();
    public TelegramDataProviderConfig TelegramDataProvider { get; set; } = new();
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

internal sealed class TelegramDataProviderConfig
{
    public bool UseProxy { get; set; }
    public List<TelegramSourceChannelConfig> Channels { get; set; } = [];
}

internal sealed class TelegramSourceChannelConfig : TelegramTargetConfig
{
    public string Url { get; set; } = string.Empty;
    public string PromptPath { get; set; } = string.Empty;
    public int PostLimit { get; set; } = 10;
    public int MaxAgeMinutes { get; set; } = 30;
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

internal sealed record TelegramChannelPost(
    string PostId,
    string Url,
    string Text,
    DateTimeOffset? PublishedAt);

internal sealed record GroqTelegramPostRequest(
    string PromptPath,
    IReadOnlyList<RssFeedItem> Items);

internal sealed record TelegramTextPostRequest(
    string PromptPath,
    IReadOnlyList<string> Items);

internal sealed record TelegramSendResult(bool Success, string ErrorMessage)
{
    public static TelegramSendResult Ok() => new(true, string.Empty);

    public static TelegramSendResult Fail(string errorMessage) => new(false, errorMessage);
}

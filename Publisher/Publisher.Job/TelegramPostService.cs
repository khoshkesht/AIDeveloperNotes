using System.Text;
using System.Text.Json;

internal sealed class TelegramPostService
{
    private readonly GroqArticleGenerator _groq;

    public TelegramPostService(GroqConfig groqConfig, ProxyConfig proxyConfig)
    {
        _groq = new GroqArticleGenerator(groqConfig, proxyConfig);
    }

    public async Task<IReadOnlyList<string>> GenerateTelegramPostsAsync(
        GroqTelegramPostRequest request,
        bool useProxy)
    {
        var promptTemplate = File.ReadAllText(request.PromptPath, Encoding.UTF8).Trim();
        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            throw new InvalidOperationException($"Groq prompt file is empty: {request.PromptPath}");
        }

        if (request.Items.Count == 0)
        {
            return [];
        }

        var response = await _groq.GenerateTelegramPostAsync(BuildBatchPrompt(promptTemplate, request.Items), useProxy);
        var posts = ParseJsonArray(response)
            .Select(post => post.Trim())
            .Where(post => !string.IsNullOrWhiteSpace(post))
            .ToList();

        return posts;
    }

    public async Task<IReadOnlyList<string>> GenerateTelegramPostsAsync(
        TelegramTextPostRequest request,
        bool useProxy)
    {
        var promptTemplate = File.ReadAllText(request.PromptPath, Encoding.UTF8).Trim();
        if (string.IsNullOrWhiteSpace(promptTemplate))
        {
            throw new InvalidOperationException($"Groq prompt file is empty: {request.PromptPath}");
        }

        if (request.Items.Count == 0)
        {
            return [];
        }

        var response = await _groq.GenerateTelegramPostAsync(BuildTextPrompt(request.PromptPath, promptTemplate, request.Items), useProxy);
        var posts = ParseJsonArray(response)
            .Select(post => post.Trim())
            .Where(post => !string.IsNullOrWhiteSpace(post))
            .ToList();

        return posts;
    }

    private static string BuildBatchPrompt(string promptTemplate, IReadOnlyList<RssFeedItem> items)
    {
        var builder = new StringBuilder();
        builder.AppendLine(promptTemplate);
        builder.AppendLine();
        builder.AppendLine("You will receive multiple RSS items.");
        builder.AppendLine("Create exactly one Telegram-ready Persian post for each RSS item, in the same order.");
        builder.AppendLine("Return only a valid JSON array of strings. Do not return Markdown, explanations, object wrappers, or numbering.");
        builder.AppendLine("Each array item must contain only the final Telegram post text for the matching RSS item.");
        builder.AppendLine();
        builder.AppendLine("RSS items:");

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var publishedAt = item.PublishedAt?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "Unknown";
            builder.AppendLine();
            builder.AppendLine($"Item {index + 1}:");
            builder.AppendLine($"Title: {item.Title}");
            builder.AppendLine($"Source: {item.SourceTitle}");
            builder.AppendLine($"PublishedAt: {publishedAt}");
            builder.AppendLine($"Link: {item.Link}");
            builder.AppendLine($"ImageUrl: {item.ImageUrl ?? "None"}");
            builder.AppendLine("Content:");
            builder.AppendLine(item.Summary);
        }

        return builder.ToString();
    }

    private static string BuildTextPrompt(string promptPath, string promptTemplate, IReadOnlyList<string> items)
    {
        return promptTemplate
            .Replace("{NewsList}", BuildBulletList(items), StringComparison.Ordinal)
            .Replace("{Rules}", ReadPromptSibling(promptPath, "Rules.md"), StringComparison.Ordinal)
            .Replace("{Format}", ReadPromptSibling(promptPath, "Format.md"), StringComparison.Ordinal);
    }

    private static string BuildBulletList(IReadOnlyList<string> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            builder.Append("- ");
            builder.AppendLine(item);
        }

        return builder.ToString().TrimEnd();
    }

    private static string ReadPromptSibling(string promptPath, string fileName)
    {
        var promptDirectory = Path.GetDirectoryName(promptPath) ?? AppContext.BaseDirectory;
        var path = Path.Combine(promptDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Prompt dependency file was not found: {path}");
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static IReadOnlyList<string> ParseJsonArray(string response)
    {
        var trimmed = response.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed.Trim('`').Trim();
            if (trimmed.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[4..].Trim();
            }
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Groq service response must be a JSON array of Telegram post strings.");
            }

            return document.RootElement
                .EnumerateArray()
                .Select(element => element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText())
                .ToList();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Groq service response was not valid JSON array: {trimmed}", ex);
        }
    }
}

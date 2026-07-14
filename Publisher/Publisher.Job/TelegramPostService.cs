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
        var posts = ParseJsonArray(response, allowEmptyResponse: false)
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
        var posts = ParseJsonArray(response, allowEmptyResponse: true)
            .Select(post => post.Trim())
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
        var filledPrompt = promptTemplate
            .Replace("{NewsList}", BuildBulletList(items), StringComparison.Ordinal)
            .Replace("{Rules}", ReadPromptSibling(promptPath, "Rules.md"), StringComparison.Ordinal)
            .Replace("{Format}", ReadPromptSibling(promptPath, "Format.md"), StringComparison.Ordinal);

        var builder = new StringBuilder();
        builder.AppendLine(filledPrompt);
        builder.AppendLine();
        builder.AppendLine("Batch output contract:");
        builder.AppendLine($"- Treat all {items.Count} News List item(s) as one batch for one Telegram channel update.");
        builder.AppendLine("- Return only one valid JSON array with exactly one string item.");
        builder.AppendLine("- That single array string must contain the final Persian Telegram post text summarized from the relevant News List items.");
        builder.AppendLine("- If the News List does not contain enough relevant information for the prompt, return [\"\"].");
        builder.AppendLine("- Do not return Markdown fences, explanations, object wrappers, numbering, or text outside the JSON array.");
        builder.AppendLine("- Escape line breaks inside JSON strings as \\n.");
        return builder.ToString();
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

    private static IReadOnlyList<string> ParseJsonArray(string response, bool allowEmptyResponse)
    {
        var trimmed = NormalizeJsonArrayResponse(response);
        if (string.IsNullOrWhiteSpace(trimmed) && allowEmptyResponse)
        {
            return [string.Empty];
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
            if (allowEmptyResponse && TryParseSingleStringArrayWithRawLineBreaks(trimmed, out var fallbackPosts))
            {
                return fallbackPosts;
            }

            if (allowEmptyResponse && !string.IsNullOrWhiteSpace(trimmed))
            {
                return [trimmed];
            }

            throw new InvalidOperationException($"Groq service response was not valid JSON array: {trimmed}", ex);
        }
    }

    private static bool TryParseSingleStringArrayWithRawLineBreaks(string value, out IReadOnlyList<string> posts)
    {
        posts = [];
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("[\"", StringComparison.Ordinal) || !trimmed.EndsWith("\"]", StringComparison.Ordinal))
        {
            return false;
        }

        var content = trimmed[2..^2];
        try
        {
            var normalizedContent = content
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
            posts = [JsonSerializer.Deserialize<string>($"\"{normalizedContent}\"") ?? string.Empty];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeJsonArrayResponse(string response)
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

        if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('[', StringComparison.Ordinal);
        var end = trimmed.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)].Trim();
        }

        return trimmed;
    }
}

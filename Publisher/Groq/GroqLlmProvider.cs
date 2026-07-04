using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ContentProducer.Worker;

public sealed class GroqLlmProvider : ILlmProvider
{
    private readonly GroqApiClient _apiClient;
    private readonly ILogger<GroqLlmProvider> _logger;
    private readonly GroqOptions _options;

    public GroqLlmProvider(
        GroqApiClient apiClient,
        IOptions<GroqOptions> options,
        ILogger<GroqLlmProvider> logger)
    {
        _apiClient = apiClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => ProviderNames.Groq;

    public async Task<GeneratedArticle> GenerateArticleAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        string research = await GetResearchAsync(prompt, cancellationToken);
        string writerPrompt = BuildWriterPrompt(prompt, research);

        _logger.LogInformation(
            "Generating article with Groq writer model {Model}. Prompt characters: {PromptCharacters}. " +
            "Max completion tokens: {MaxCompletionTokens}.",
            _options.WriterModel,
            writerPrompt.Length,
            _options.MaxCompletionTokens);

        string outputText;

        try
        {
            outputText = await GenerateArticleJsonAsync(
                writerPrompt,
                _options.WriterModel,
                _options.MaxCompletionTokens,
                useStrictSchema: true,
                cancellationToken);
        }
        catch (GroqApiException exception) when (IsModelRefusal(exception))
        {
            string failedGeneration = GetFailedGeneration(exception) ??
                "The Groq writer model refused the article request.";

            throw new InvalidOperationException(
                $"Groq writer model refused to generate the article: {failedGeneration}",
                exception);
        }
        catch (GroqApiException exception)
            when (_options.EnableWriterFallback && IsJsonGenerationFailure(exception))
        {
            _logger.LogWarning(
                exception,
                "Groq writer model {Model} failed to generate JSON. Retrying once with " +
                "fallback model {FallbackModel}.",
                _options.WriterModel,
                _options.FallbackWriterModel);

            outputText = await GenerateArticleJsonAsync(
                writerPrompt,
                _options.FallbackWriterModel,
                _options.FallbackMaxCompletionTokens,
                useStrictSchema: false,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException(
                "Groq Chat Completions API returned no article text.");
        }

        GeneratedArticle article = GeneratedArticleParser.Parse(outputText, Name);

        if (article.ArticleHtml.Length < _options.MinimumArticleHtmlCharacters)
        {
            string message =
                $"Groq generated a short article. Article HTML characters: " +
                $"{article.ArticleHtml.Length}. Required minimum: " +
                $"{_options.MinimumArticleHtmlCharacters}.";

            if (_options.RejectShortArticles)
            {
                throw new InvalidOperationException(message);
            }

            _logger.LogWarning("{Message}", message);
        }

        _logger.LogInformation(
            "Article generated with Groq. Article HTML characters: {ArticleHtmlCharacters}.",
            article.ArticleHtml.Length);

        return article;
    }

    private async Task<string> GenerateArticleJsonAsync(
        string writerPrompt,
        string model,
        int maxCompletionTokens,
        bool useStrictSchema,
        CancellationToken cancellationToken)
    {
        object responseFormat = useStrictSchema
            ? BuildStrictArticleResponseFormat()
            : new { type = "json_object" };

        using JsonDocument response = await _apiClient.PostAsync(
            "chat/completions",
            new
            {
                model,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = BuildWriterSystemPrompt()
                    },
                    new
                    {
                        role = "user",
                        content = writerPrompt
                    }
                },
                response_format = responseFormat,
                max_completion_tokens = maxCompletionTokens
            },
            cancellationToken);

        _logger.LogInformation(
            "Groq writer model {Model} returned an article response.",
            model);

        return ExtractOutputText(response.RootElement);
    }

    private static object BuildStrictArticleResponseFormat()
    {
        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "generated_article",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string" },
                        articleHtml = new { type = "string" },
                        instagramCaption = new { type = "string" },
                        focusKeyphrase = new { type = "string" },
                        seoTitle = new { type = "string" },
                        metaDescription = new { type = "string" },
                        references = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    title = new { type = "string" },
                                    url = new { type = "string" }
                                },
                                required = new[] { "title", "url" },
                                additionalProperties = false
                            }
                        }
                    },
                    required = new[]
                    {
                        "title",
                        "articleHtml",
                        "instagramCaption",
                        "focusKeyphrase",
                        "seoTitle",
                        "metaDescription",
                        "references"
                    },
                    additionalProperties = false
                }
            }
        };
    }

    private static bool IsJsonGenerationFailure(GroqApiException exception)
    {
        if (exception.StatusCode != HttpStatusCode.BadRequest)
        {
            return false;
        }

        return exception.ResponseBody.Contains(
                "json_validate_failed",
                StringComparison.OrdinalIgnoreCase) ||
            exception.ResponseBody.Contains(
                "failed_generation",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsModelRefusal(GroqApiException exception)
    {
        string? failedGeneration = GetFailedGeneration(exception);

        if (string.IsNullOrWhiteSpace(failedGeneration))
        {
            return false;
        }

        return failedGeneration.Contains("I'm sorry", StringComparison.OrdinalIgnoreCase) ||
            failedGeneration.Contains("I’m sorry", StringComparison.OrdinalIgnoreCase) ||
            failedGeneration.Contains("I cannot", StringComparison.OrdinalIgnoreCase) ||
            failedGeneration.Contains("I can't", StringComparison.OrdinalIgnoreCase) ||
            failedGeneration.Contains("I can’t", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetFailedGeneration(GroqApiException exception)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(exception.ResponseBody);

            if (document.RootElement.TryGetProperty("error", out JsonElement error) &&
                error.TryGetProperty("failed_generation", out JsonElement failedGeneration))
            {
                return failedGeneration.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string BuildWriterSystemPrompt()
    {
        return string.Join(
            Environment.NewLine,
            "You are a senior Persian technology journalist.",
            "Follow every structural and length requirement in the user prompt.",
            "The article must be detailed, source-grounded, and focused on the specific news items.",
            "Do not replace the requested analysis with a short generic overview.",
            "The articleHtml field must contain at least 5000 Persian content characters.",
            "An articleHtml value shorter than 5000 characters is invalid.",
            "Return one valid JSON object only.");
    }

    private async Task<string> GetResearchAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableWebResearch)
        {
            return string.Empty;
        }

        try
        {
            return await ResearchAsync(prompt, cancellationToken);
        }
        catch (GroqApiException exception)
            when (_options.ContinueWithoutResearchOnFailure &&
                  exception.StatusCode == HttpStatusCode.RequestEntityTooLarge)
        {
            _logger.LogWarning(
                exception,
                "Groq web research returned HTTP 413 for a {RequestBytes}-byte request. " +
                "Continuing with the writer model without research notes.",
                exception.RequestBytes);

            return string.Empty;
        }
    }

    private async Task<string> ResearchAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        string researchPrompt = BuildResearchPrompt(prompt);

        _logger.LogInformation(
            "Researching article with Groq model {Model}. Prompt characters: {PromptCharacters}.",
            _options.ResearchModel,
            researchPrompt.Length);

        using JsonDocument response = await _apiClient.PostAsync(
            "chat/completions",
            new
            {
                model = _options.ResearchModel,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = researchPrompt
                    }
                },
                max_completion_tokens = _options.ResearchMaxCompletionTokens
            },
            cancellationToken,
            BuildResearchHeaders());

        string research = ExtractOutputText(response.RootElement);

        if (string.IsNullOrWhiteSpace(research))
        {
            throw new InvalidOperationException(
                "Groq research model returned no research notes.");
        }

        if (research.Length > _options.MaxResearchCharacters)
        {
            research = research.Substring(0, _options.MaxResearchCharacters);
        }

        _logger.LogInformation(
            "Groq research completed with {ResearchCharacters} characters.",
            research.Length);

        return research;
    }

    private IReadOnlyDictionary<string, string>? BuildResearchHeaders()
    {
        if (string.IsNullOrWhiteSpace(_options.ResearchModelVersion))
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            ["Groq-Model-Version"] = _options.ResearchModelVersion
        };
    }

    private string BuildResearchPrompt(string prompt)
    {
        int maxCharacters = Math.Max(1, _options.ResearchPromptMaxCharacters);
        string topicContext = prompt.Length <= maxCharacters
            ? prompt
            : prompt.Substring(0, maxCharacters);

        return string.Join(
            Environment.NewLine,
            "Use one web search to research the topic from the Persian request below.",
            "Return concise notes for 2 or 3 recent relevant news items only.",
            "Use only sources published on or after January 1, 2026.",
            "Ignore older sources and state clearly if fewer than 2 eligible news items exist.",
            "Select only non-sensitive general technology topics related to AI, software development,",
            "work, jobs, productivity, or practical everyday applications.",
            "For each item include publication date, key facts, source title, and direct URL.",
            "Do not write the article, do not return JSON, and do not perform broad research.",
            string.Empty,
            topicContext);
    }

    private static string BuildWriterPrompt(string prompt, string research)
    {
        if (string.IsNullOrWhiteSpace(research))
        {
            return prompt;
        }

        return string.Join(
            Environment.NewLine,
            prompt,
            string.Empty,
            "# یادداشت‌های تحقیق وب",
            "برای نگارش مقاله فقط از اطلاعات قابل اتکای زیر استفاده کن.",
            "لینک منابع را از همین یادداشت‌ها در آرایه references قرار بده.",
            string.Empty,
            research);
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("choices", out JsonElement choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out JsonElement message) &&
            message.TryGetProperty("content", out JsonElement content))
        {
            return content.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

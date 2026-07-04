using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ContentProducer.Worker;

public sealed class GroqApiClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly GroqOptions _options;

    public GroqApiClient(IOptions<GroqOptions> options)
    {
        _options = options.Value;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public async Task<JsonDocument> PostAsync(
        string relativeUrl,
        object body,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        string json = JsonSerializer.Serialize(body, JsonOptions);
        int requestBytes = Encoding.UTF8.GetByteCount(json);

        using HttpRequestMessage request = new(HttpMethod.Post, relativeUrl);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", GetApiKey());

        if (headers is not null)
        {
            foreach ((string name, string value) in headers)
            {
                request.Headers.Add(name, value);
            }
        }

        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using HttpResponseMessage response =
            await _httpClient.SendAsync(request, cancellationToken);
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new GroqApiException(
                response.StatusCode,
                responseBody,
                requestBytes);
        }

        return JsonDocument.Parse(responseBody);
    }

    private string GetApiKey()
    {
        string? apiKey = _options.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = Environment.GetEnvironmentVariable(
                _options.ApiKeyEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Groq API key is missing. Set Groq:ApiKey or the " +
                $"'{_options.ApiKeyEnvironmentVariable}' environment variable.");
        }

        return apiKey.Trim();
    }
}

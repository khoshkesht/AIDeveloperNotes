using System.Net;

namespace ContentProducer.Worker;

public sealed class GroqApiException : InvalidOperationException
{
    public GroqApiException(
        HttpStatusCode statusCode,
        string responseBody,
        int requestBytes)
        : base(BuildMessage(statusCode, responseBody, requestBytes))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RequestBytes = requestBytes;
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    public int RequestBytes { get; }

    private static string BuildMessage(
        HttpStatusCode statusCode,
        string responseBody,
        int requestBytes)
    {
        string sizeHint = statusCode == HttpStatusCode.RequestEntityTooLarge
            ? $" Request body size: {requestBytes} UTF-8 bytes."
            : string.Empty;

        return $"Groq API request failed with status {(int)statusCode}: " +
            responseBody + sizeHint;
    }
}

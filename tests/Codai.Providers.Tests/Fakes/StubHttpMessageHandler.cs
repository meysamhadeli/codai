using System.Net;
using System.Text;

namespace Codai.Providers.Tests.Fakes;

/// <summary>Returns a canned response for the first request received.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _body;
    private readonly string _contentType;

    public StubHttpMessageHandler(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string contentType = "text/event-stream")
    {
        _body = body;
        _statusCode = statusCode;
        _contentType = contentType;
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastRequestBody { get; private set; }

    public Dictionary<string, IEnumerable<string>> LastRequestHeaders { get; } = new();

    public static HttpClient CreateClient(
        string body,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string contentType = "text/event-stream",
        StubHttpMessageHandler? handler = null)
    {
        handler ??= new StubHttpMessageHandler(body, statusCode, contentType);
        return new HttpClient(handler);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        foreach (var header in request.Headers)
        {
            LastRequestHeaders[header.Key] = header.Value;
        }

        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_body, Encoding.UTF8, _contentType),
        };
    }
}

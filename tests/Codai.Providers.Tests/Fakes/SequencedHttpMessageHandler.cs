using System.Net;
using System.Text;

namespace Codai.Providers.Tests.Fakes;

/// <summary>
/// Returns a canned response per request, in order, and records every request body.
/// Needed whenever a test has to observe more than one round trip — the tool loop, for
/// instance, asks again after a call has been run.
/// </summary>
internal sealed class SequencedHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body, string ContentType)> _responses;

    public SequencedHttpMessageHandler(
        params (HttpStatusCode Status, string Body, string ContentType)[] responses) =>
        _responses = new(responses);

    /// <summary>Server-sent events, as the Anthropic API sends them.</summary>
    public static SequencedHttpMessageHandler Streaming(params string[] bodies) =>
        new([.. bodies.Select(body => (HttpStatusCode.OK, body, "text/event-stream"))]);

    /// <summary>Plain JSON, as the Gemini API sends it.</summary>
    public static SequencedHttpMessageHandler Json(params string[] bodies) =>
        new([.. bodies.Select(body => (HttpStatusCode.OK, body, "application/json"))]);

    /// <summary>Every request body received, in order.</summary>
    public List<string> RequestBodies { get; } = [];

    public HttpClient CreateClient() => new(this);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestBodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var (status, body, contentType) = _responses.Count > 0
            ? _responses.Dequeue()
            : (HttpStatusCode.OK, "{}", "application/json");

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        };
    }
}

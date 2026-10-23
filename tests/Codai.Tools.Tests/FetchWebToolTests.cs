using System.Net;
using System.Text;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Tools.Tests;

public sealed class FetchWebToolTests
{
    [Theory]
    [InlineData("file:///secret.txt")]
    [InlineData("ftp://example.com/file")]
    [InlineData("http://localhost/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("https://example.com:8443/")]
    public async Task Unsafe_urls_are_rejected_before_http(string url)
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler);

        var result = await InvokeAsync(new FetchWebTool(client), url);

        result.IsSuccess.Should().BeFalse();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Html_is_parsed_without_scripts_styles_or_subresources()
    {
        using var handler = new RecordingHandler
        {
            Content = "<html><body><h1>Real &amp; useful</h1><p>Documentation.</p><script>secret-script</script><style>secret-style</style><iframe src='http://localhost'>hidden</iframe></body></html>",
        };
        using var client = new HttpClient(handler);

        var result = await InvokeAsync(new FetchWebTool(client), "https://example.com/docs");

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("Real & useful").And.Contain("Documentation.")
            .And.NotContain("secret-script").And.NotContain("secret-style").And.NotContain("hidden");
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Redirect_to_private_network_is_rejected_without_second_request()
    {
        using var handler = new RecordingHandler { Redirect = new Uri("http://127.0.0.1/private") };
        using var client = new HttpClient(handler);

        (await InvokeAsync(new FetchWebTool(client), "https://example.com/")).IsSuccess.Should().BeFalse();
        handler.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("application/pdf", "binary")]
    [InlineData("image/png", "binary")]
    public async Task Binary_content_is_refused(string type, string body)
    {
        using var handler = new RecordingHandler { ContentType = type, Content = body };
        using var client = new HttpClient(handler);

        (await InvokeAsync(new FetchWebTool(client), "https://example.com/")).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Oversized_response_is_refused()
    {
        using var handler = new RecordingHandler { Content = new string('x', FetchWebTool.MaxBytes + 1) };
        using var client = new HttpClient(handler);

        var result = await InvokeAsync(new FetchWebTool(client), "https://example.com/");

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("exceeds");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::1")]
    public async Task Dns_private_results_are_rejected_before_socket_connection(string address)
    {
        Func<Task> connect = async () => await PublicWebTransport.ConnectAsync("example.com", 443, CancellationToken.None,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse(address) }));

        await connect.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Mixed_public_and_private_dns_results_fail_closed()
    {
        Func<Task> connect = async () => await PublicWebTransport.ConnectAsync("example.com", 443, CancellationToken.None,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Loopback }));

        await connect.Should().ThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://[2606:4700:4700::1111]/")]
    public async Task Public_addresses_can_retrieve_plain_text(string url)
    {
        using var handler = new RecordingHandler { ContentType = "text/plain", Content = "Readable text." };
        using var client = new HttpClient(handler);

        var result = await InvokeAsync(new FetchWebTool(client), url);

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().Contain("Readable text.").And.Contain("untrusted data");
    }

    [Fact]
    public async Task Streamed_body_without_content_length_still_enforces_byte_limit()
    {
        using var handler = new RecordingHandler { OmitLength = true, Content = new string('x', FetchWebTool.MaxBytes + 1) };
        using var client = new HttpClient(handler);

        var result = await InvokeAsync(new FetchWebTool(client), "https://example.com/");

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("exceeds");
    }

    [Fact]
    public async Task Long_text_is_clipped_with_notice_within_tool_limit()
    {
        using var handler = new RecordingHandler { ContentType = "text/plain", Content = new string('x', FetchWebTool.MaxTextChars + 500) };
        using var client = new HttpClient(handler);

        var result = await InvokeAsync(new FetchWebTool(client), "https://example.com/");

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().EndWith("... (truncated)");
        result.Content.Length.Should().BeLessThanOrEqualTo(ToolRunner.DefaultMaxResultChars);
    }

    [Fact]
    public async Task Public_relative_redirect_preserves_final_source()
    {
        using var handler = new RecordingHandler { Redirect = new Uri("/docs", UriKind.Relative), RedirectOnce = true };
        using var client = new HttpClient(handler);

        var result = await InvokeAsync(new FetchWebTool(client), "https://example.com/");

        result.IsSuccess.Should().BeTrue();
        result.Content.Should().StartWith("Source: https://example.com/docs");
        handler.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://user:password@example.com/")]
    public async Task Redirect_cannot_downgrade_tls_or_add_credentials(string url)
    {
        using var handler = new RecordingHandler { Redirect = new Uri(url) };
        using var client = new HttpClient(handler);

        (await InvokeAsync(new FetchWebTool(client), "https://example.com/")).IsSuccess.Should().BeFalse();
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Redirect_loop_is_bounded()
    {
        using var handler = new RecordingHandler { Redirect = new Uri("https://example.com/loop") };
        using var client = new HttpClient(handler);

        (await InvokeAsync(new FetchWebTool(client), "https://example.com/")).IsSuccess.Should().BeFalse();
        handler.Calls.Should().Be(FetchWebTool.MaxRedirects + 1);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_from_active_http_request()
    {
        using var handler = new RecordingHandler { WaitForCancellation = true };
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var task = new FetchWebTool(client).InvokeAsync(ToolTestHost.Args("{\"url\":\"https://example.com/\"}"),
            new ToolContext(ToolTestHost.Root, ToolTestHost.NewProject(), NullToolObserver.Instance), cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        Func<Task> invoke = () => task;

        await invoke.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Transport_timeout_is_failure_not_caller_cancellation()
    {
        using var handler = new RecordingHandler { WaitForCancellation = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(100) };

        var result = await InvokeAsync(new FetchWebTool(client), "https://example.com/");

        result.IsSuccess.Should().BeFalse();
        result.Content.Should().Contain("timed out");
    }

    private static Task<ToolResult> InvokeAsync(FetchWebTool tool, string url) => tool.InvokeAsync(
        System.Text.Json.JsonSerializer.SerializeToElement(new { url }),
        new ToolContext(ToolTestHost.Root, ToolTestHost.NewProject(), NullToolObserver.Instance));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string Content { get; init; } = "<p>documentation</p>";
        public string ContentType { get; init; } = "text/html";
        public Uri? Redirect { get; init; }
        public bool RedirectOnce { get; init; }
        public bool OmitLength { get; init; }
        public bool WaitForCancellation { get; init; }
        public int Calls { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Started.TrySetResult();
            if (WaitForCancellation)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var redirect = Redirect is not null && (!RedirectOnce || Calls == 1);
            var response = new HttpResponseMessage(redirect ? HttpStatusCode.Found : HttpStatusCode.OK)
            {
                Content = OmitLength ? new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes(Content)))
                    : new StringContent(Content, Encoding.UTF8, ContentType),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ContentType);
            response.Headers.Location = redirect ? Redirect : null;
            return response;
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}

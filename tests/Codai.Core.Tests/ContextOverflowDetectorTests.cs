using Codai.Core.Abstractions;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers the one failure that is worth reacting to: the provider saying the request was
/// longer than the model's context window.
/// </summary>
/// <remarks>
/// Getting this wrong is expensive in both directions. A missed overflow leaves the session
/// stuck on a request that can never succeed, and a false positive shortens a conversation
/// because of an unrelated failure — which loses history for nothing.
/// </remarks>
public class ContextOverflowDetectorTests
{
    [Fact]
    public void Nothing_to_detect_is_not_an_overflow()
    {
        ContextOverflowDetector.Matches(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("This model's maximum context length is 128000 tokens")]
    [InlineData("context_length_exceeded")]
    [InlineData("Your request exceeded the context window of this model")]
    [InlineData("prompt is too long: 250000 tokens > 200000 maximum")]
    [InlineData("Error code: 400 - request_too_large")]
    [InlineData("Input is too long for the model claude-3-5-sonnet-latest")]
    [InlineData("The number of tokens in the request exceeds the maximum number of tokens")]
    public void The_providers_own_words_for_a_too_long_request_are_recognised(string message)
    {
        var exception = new ChatProviderException(message);

        exception.IsContextOverflow.Should().BeTrue();
    }

    [Fact]
    public void Wording_is_not_case_sensitive()
    {
        var exception = new ChatProviderException("Maximum CONTEXT length exceeded");

        exception.IsContextOverflow.Should().BeTrue();
    }

    [Fact]
    public void A_provider_that_wraps_the_refusal_is_still_recognised()
    {
        // The SDKs wrap: the useful sentence is the innermost one, and the outer message is
        // often nothing but a status code.
        var exception = new ChatProviderException(
            "The request failed",
            new InvalidOperationException("400 Bad Request", new ChatProviderException("prompt is too long")));

        exception.IsContextOverflow.Should().BeTrue();
    }

    [Theory]
    [InlineData("429 Too Many Requests")]
    [InlineData("Connection refused")]
    [InlineData("Invalid API key")]
    [InlineData("The model returned an empty response")]
    [InlineData("contextual grounding is not available")]
    public void Anything_else_is_not_an_overflow(string message)
    {
        // "reduce the length" appears in real overflow messages, so the markers are narrow
        // on purpose: a session that shortens itself because the network hiccuped has lost
        // history for no reason.
        var exception = new ChatProviderException(message);

        exception.IsContextOverflow.Should().BeFalse();
    }

    [Fact]
    public void An_overflow_detector_never_looks_at_exception_metadata()
    {
        // The detection is on the message only, so an exception that merely passed through a
        // frame with "context" in its name is not an overflow.
        var exception = new ChatProviderException("Request failed")
        {
            Source = "ContextSomething",
            HelpLink = "https://example.com/context-window",
        };

        exception.IsContextOverflow.Should().BeFalse();
    }
}

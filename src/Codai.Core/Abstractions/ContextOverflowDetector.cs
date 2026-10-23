namespace Codai.Core.Abstractions;

/// <summary>
/// Recognises the provider error that means "this request was longer than my context window".
/// </summary>
/// <remarks>
/// <para>
/// The estimate <see cref="Context.TokenEstimator"/> produces is an approximation, and the
/// number that decides the outcome is the provider's own. When the approximation is too low
/// the request is rejected outright, and that rejection is the only trustworthy signal that
/// the conversation really is too long — it comes from the model that refused it.
/// </para>
/// <para>
/// There is no shared error code for this. Every provider phrases it differently, several
/// wrap it in an envelope whose own message says nothing, so the text of the whole exception
/// chain is what gets matched. The patterns are deliberately narrow: treating an unrelated
/// failure as an overflow would compact a conversation that had nothing wrong with it.
/// </para>
/// </remarks>
public static class ContextOverflowDetector
{
    private static readonly string[] Markers =
    [
        "context length",
        "context_length_exceeded",
        "context window",
        "maximum context",
        "max context",
        "too many tokens",
        "token count",
        "reduce the length",
        "reduce your prompt",
        "prompt is too long",
        "prompt too long",
        "input is too long",
        "input length",
        "string too long",
        "content too large",
        "request too large",
        "request_too_large",
        "exceeds the maximum",
        "exceeded the maximum",
        "maximum number of tokens",
        "longer than the maximum",
        "too long for the model",
    ];

    /// <summary>True when <paramref name="exception"/> looks like a context-window rejection.</summary>
    public static bool Matches(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var message = current.Message;

            if (string.IsNullOrEmpty(message))
            {
                continue;
            }

            foreach (var marker in Markers)
            {
                if (message.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

namespace Codai.Core.Context;

/// <summary>
/// How many tokens a model can hold, used to decide when the conversation has to be
/// compacted.
/// </summary>
/// <remarks>
/// The table is a prefix match, so a dated model name such as
/// <c>claude-3-5-sonnet-20241022</c> resolves through its family prefix. Every number is
/// deliberately below the vendor's advertised maximum: the point is to compact before a
/// request is rejected, so underestimating is safe and overestimating is not.
/// </remarks>
public static class ContextWindow
{
    /// <summary>Used when the model is not recognised.</summary>
    public const int DefaultTokens = 128_000;

    /// <summary>Smallest window that is still worth running an agent against.</summary>
    public const int MinimumTokens = 8_000;

    private static readonly (string Prefix, int Tokens)[] Known =
    [
        // OpenAI
        ("gpt-4.1", 1_000_000),
        ("gpt-4o", 128_000),
        ("gpt-4-turbo", 128_000),
        ("gpt-4", 8_192),
        ("gpt-3.5", 16_000),
        ("o1", 200_000),
        ("o3", 200_000),
        ("o4", 200_000),

        // Anthropic
        ("claude", 200_000),

        // Google
        ("gemini-1.5-pro", 2_000_000),
        ("gemini-1.5-flash", 1_000_000),
        ("gemini-1.5", 1_000_000),
        ("gemini-2", 1_000_000),
        ("gemini", 128_000),

        // Everything else that is commonly pointed at
        ("deepseek", 128_000),
        ("mistral", 128_000),
        ("grok", 131_072),
        ("qwen", 131_072),
        ("llama3", 131_072),
        ("llama", 8_192),
        ("phi", 128_000),
        ("command-r", 128_000),
    ];

    /// <summary>
    /// The window for <paramref name="model"/>, or <see cref="DefaultTokens"/> when the
    /// model is unknown.
    /// </summary>
    public static int For(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return DefaultTokens;
        }

        // "openrouter" and friends prefix the vendor, e.g. "openai/gpt-4o": match on the
        // part after the last separator as well as the whole name.
        var name = model.Trim().ToLowerInvariant();
        var separator = name.LastIndexOf('/');
        var tail = separator >= 0 ? name[(separator + 1)..] : name;

        var best = DefaultTokens;
        var bestLength = -1;

        foreach (var (prefix, tokens) in Known)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) &&
                !tail.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            // Longest prefix wins, so "gpt-4-turbo" is not shadowed by "gpt-4".
            if (prefix.Length > bestLength)
            {
                best = tokens;
                bestLength = prefix.Length;
            }
        }

        return best;
    }
}

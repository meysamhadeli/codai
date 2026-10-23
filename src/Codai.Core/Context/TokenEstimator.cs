using Codai.Core.Tools;

namespace Codai.Core.Context;

/// <summary>
/// Estimates how many tokens a request will cost without calling a tokeniser.
/// </summary>
/// <remarks>
/// A real token count needs the provider's own tokeniser: there is no shared one, and
/// downloading a vocabulary per provider to decide whether to compact would cost more
/// than the decision is worth. The widely used approximation of one token per four
/// characters is close enough for a threshold that carries a large safety reserve, and
/// it never has to be right — only conservative. The estimate is deliberately allowed to
/// overstate: overstating compacts a little early, understating overflows the window.
/// </remarks>
public static class TokenEstimator
{
    /// <summary>Characters assumed to make up one token.</summary>
    public const double CharactersPerToken = 4.0;

    /// <summary>
    /// Tokens charged per message on top of its text, covering the role markers and
    /// delimiters a chat format adds.
    /// </summary>
    public const int PerMessageOverhead = 4;

    /// <summary>Tokens charged for a request regardless of its content.</summary>
    public const int PerRequestOverhead = 8;

    /// <summary>
    /// Tokens charged per tool on top of its text, covering the envelope a provider
    /// wraps around a function declaration.
    /// </summary>
    public const int PerToolOverhead = 12;

    /// <summary>Rounds <paramref name="characters"/> up to a token count.</summary>
    public static int FromCharacters(long characters) =>
        characters <= 0 ? 0 : (int)Math.Ceiling(characters / CharactersPerToken);

    /// <summary>Estimates the tokens in <paramref name="text"/>.</summary>
    public static int Estimate(string? text) => FromCharacters(text?.Length ?? 0);

    /// <summary>
    /// Estimates the tokens of a whole request: every part plus the per-message and
    /// per-request overhead.
    /// </summary>
    public static int EstimateRequest(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var characters = 0L;
        var messages = 0;

        foreach (var part in parts)
        {
            if (part is null)
            {
                continue;
            }

            characters += part.Length;
            messages++;
        }

        return FromCharacters(characters) + (messages * PerMessageOverhead) + PerRequestOverhead;
    }

    /// <summary>
    /// Estimates the tokens the tool definitions cost on <em>every</em> request.
    /// </summary>
    /// <remarks>
    /// A tool is charged for its name, its description and its argument schema, because
    /// that is what the provider is sent, once per call and once per tool. Nothing here
    /// shrinks as the conversation grows, so a caller deciding whether a conversation
    /// fits has to subtract this first: nine tools with a schema each are thousands of
    /// tokens that the history never gets to use.
    /// </remarks>
    public static int EstimateTools(IEnumerable<ITool>? tools)
    {
        if (tools is null)
        {
            return 0;
        }

        var total = 0;

        foreach (var tool in tools)
        {
            total += Estimate(tool.Name);
            total += Estimate(tool.Description);
            total += Estimate(tool.ParametersSchema);
            total += PerToolOverhead;
        }

        return total;
    }
}

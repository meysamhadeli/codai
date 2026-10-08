namespace Codai.Core.Models;

/// <summary>
/// Token usage reported by a provider for a single request.
/// </summary>
public readonly record struct TokenUsage(int InputTokens, int OutputTokens)
{
    public static TokenUsage Empty => new(0, 0);

    public int TotalTokens => InputTokens + OutputTokens;

    public bool HasValue => TotalTokens > 0;

    public TokenUsage Add(TokenUsage other) =>
        new(InputTokens + other.InputTokens, OutputTokens + other.OutputTokens);

    public override string ToString() =>
        $"in={InputTokens}, out={OutputTokens}, total={TotalTokens}";
}

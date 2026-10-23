using Codai.Core.Abstractions;

namespace Codai.Core.Tokens;

/// <summary>
/// Tracks cumulative and per-request token usage for the session.
/// </summary>
/// <remarks>
/// Recording is guarded, because summarising a conversation in the background is a model
/// call that costs tokens, and it records them at the same time as the request the user is
/// waiting for. Summarising a struct property and adding to it is not atomic, so without
/// the guard the two recordings would take turns overwriting each other and the session
/// total would quietly lose one of them.
/// </remarks>
public sealed class TokenTracker : ITokenTracker
{
    private readonly Lock _gate = new();

    public TokenUsage SessionUsage { get; private set; } = TokenUsage.Empty;

    public TokenUsage LastUsage { get; private set; } = TokenUsage.Empty;

    public void Record(TokenUsage usage)
    {
        lock (_gate)
        {
            LastUsage = usage;
            SessionUsage = SessionUsage.Add(usage);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            SessionUsage = TokenUsage.Empty;
            LastUsage = TokenUsage.Empty;
        }
    }
}

using Codai.Core.Abstractions;
using Codai.Core.Models;

namespace Codai.Core.Tokens;

/// <summary>
/// Tracks cumulative and per-request token usage for the session.
/// </summary>
public sealed class TokenTracker : ITokenTracker
{
    public TokenUsage SessionUsage { get; private set; } = TokenUsage.Empty;

    public TokenUsage LastUsage { get; private set; } = TokenUsage.Empty;

    public void Record(TokenUsage usage)
    {
        LastUsage = usage;
        SessionUsage = SessionUsage.Add(usage);
    }

    public void Clear()
    {
        SessionUsage = TokenUsage.Empty;
        LastUsage = TokenUsage.Empty;
    }
}

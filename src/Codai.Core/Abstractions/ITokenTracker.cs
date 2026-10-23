
using Codai.Core.Tokens;
namespace Codai.Core.Abstractions;

/// <summary>
/// Accumulates token usage for the current session.
/// </summary>
public interface ITokenTracker
{
    /// <summary>Cumulative usage since the tracker was created or last cleared.</summary>
    TokenUsage SessionUsage { get; }

    /// <summary>Usage of the most recently recorded request.</summary>
    TokenUsage LastUsage { get; }

    void Record(TokenUsage usage);

    void Clear();
}

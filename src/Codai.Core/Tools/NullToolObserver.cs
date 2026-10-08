namespace Codai.Core.Tools;

/// <summary>An observer that ignores every notification.</summary>
public sealed class NullToolObserver : IToolObserver
{
    /// <summary>The shared instance.</summary>
    public static NullToolObserver Instance { get; } = new();

    /// <inheritdoc />
    public void OnToolStarted(string toolName, string summary)
    {
    }

    /// <inheritdoc />
    public void OnToolCompleted(string toolName, bool succeeded, string detail)
    {
    }

    /// <inheritdoc />
    public void OnToolDenied(string toolName, string summary)
    {
    }
}

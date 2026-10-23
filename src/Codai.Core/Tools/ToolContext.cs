using Codai.Core.Abstractions;

namespace Codai.Core.Tools;

/// <summary>
/// Everything a tool needs while it runs: where the project is, how to read files from
/// it, and how to report progress to the user.
/// </summary>
/// <remarks>
/// Passed per invocation rather than injected, because the project root is a session
/// concern and a tool must never be able to reach outside it.
/// </remarks>
public sealed class ToolContext
{
    /// <summary>Creates a context.</summary>
    public ToolContext(
        string projectRoot,
        IFileSystem fileSystem,
        IToolObserver observer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(observer);

        ProjectRoot = projectRoot;
        FileSystem = fileSystem;
        Observer = observer;
    }

    /// <summary>Directory that bounds every path a tool may touch.</summary>
    public string ProjectRoot { get; }

    /// <summary>File access, so tools are testable without a real disk.</summary>
    public IFileSystem FileSystem { get; }

    /// <summary>Receives progress messages so the UI can show what the model is doing.</summary>
    public IToolObserver Observer { get; }
}

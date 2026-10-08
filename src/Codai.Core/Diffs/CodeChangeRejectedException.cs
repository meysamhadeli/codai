namespace Codai.Core.Diffs;

/// <summary>
/// Thrown when a model-supplied code change targets a path outside the project root.
/// </summary>
/// <remarks>
/// This is a deliberate refusal, not an I/O failure: relative paths in model output are
/// untrusted, and writing outside the directory the user asked Codai to edit would be a
/// path-traversal vulnerability.
/// </remarks>
public sealed class CodeChangeRejectedException : Exception
{
    /// <summary>Creates the exception for the rejected relative path.</summary>
    public CodeChangeRejectedException(string relativePath, string projectRoot)
        : base($"Refused to write '{relativePath}' because it resolves outside the project root '{projectRoot}'.")
    {
        RelativePath = relativePath;
        ProjectRoot = projectRoot;
    }

    /// <summary>The relative path that was rejected.</summary>
    public string RelativePath { get; }

    /// <summary>The project root the path had to stay inside.</summary>
    public string ProjectRoot { get; }
}

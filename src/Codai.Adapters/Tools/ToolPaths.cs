using Codai.Core.IO;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// Resolves model-supplied paths against the project root, refusing anything that
/// escapes it.
/// </summary>
/// <remarks>
/// Every path a tool receives came from a model response, so it is untrusted input.
/// The containment check is delegated to <see cref="ProjectPaths"/>, the same helper
/// the diff applier and the requested-context reader use, so all three guard the
/// boundary identically.
/// </remarks>
internal static class ToolPaths
{
    /// <summary>Resolves a path, or reports why it cannot be used.</summary>
    public static bool TryResolve(
        ToolContext context,
        string relativePath,
        out string fullPath,
        out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;

        string? resolved;
        try
        {
            resolved = ProjectPaths.ResolveInside(context.ProjectRoot, relativePath);
        }
        catch (ArgumentException)
        {
            error = $"'{relativePath}' is not a valid path.";
            return false;
        }

        if (resolved is null)
        {
            error =
                $"'{relativePath}' resolves outside the project root and cannot be read. " +
                "Use a path relative to the project root.";
            return false;
        }

        fullPath = resolved;
        return true;
    }

    /// <summary>
    /// The project-relative form of a resolved path, with forward slashes, for reporting
    /// a change back to the user and the model.
    /// </summary>
    public static string Relative(ToolContext context, string fullPath) =>
        Path.GetRelativePath(context.ProjectRoot, fullPath).Replace('\\', '/');
}

namespace Codai.Core.IO;

/// <summary>
/// Path helpers that keep model-supplied relative paths inside the project root.
/// </summary>
/// <remarks>
/// Every relative path that reaches Codai originates from an LLM response, so it must
/// be treated as untrusted input. Both the requested-context reader and the diff
/// applier resolve paths through this type, so escaping the root
/// (<c>../../etc/passwd</c>, <c>C:\Windows\...</c>, an absolute path) cannot be
/// guarded in one code path while being overlooked in the other.
/// </remarks>
public static class ProjectPaths
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// Normalises a model-supplied relative path to forward slashes and trims it.
    /// </summary>
    public static string Normalize(string relativePath) =>
        relativePath.Trim().Replace('\\', '/');

    /// <summary>
    /// Resolves <paramref name="relativePath"/> against <paramref name="rootDirectory"/>,
    /// returning <c>null</c> when the result would fall outside the root.
    /// </summary>
    public static string? ResolveInside(string rootDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var fullPath = Path.GetFullPath(Path.Combine(rootDirectory, Normalize(relativePath)));

        return IsInside(rootDirectory, fullPath) ? fullPath : null;
    }

    /// <summary>
    /// True when <paramref name="fullPath"/> is the root itself or lives beneath it.
    /// </summary>
    public static bool IsInside(string rootDirectory, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        return fullPath.StartsWith(root, PathComparison)
            || string.Equals(fullPath, Path.TrimEndingDirectorySeparator(root), PathComparison);
    }

    /// <summary>True when two paths refer to the same location.</summary>
    public static bool AreSame(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            PathComparison);
}

namespace Codai.Analysis;

/// <summary>
/// Ignore rules applied while scanning a project.
/// </summary>
/// <remarks>
/// Ports the original <c>IsDefaultIgnored</c> list and <c>.codai-gitignore</c> handling.
/// Unlike the original (which split on the OS separator and therefore behaved
/// differently on Windows), paths are always split on <c>/</c> and <c>\</c>.
/// </remarks>
public static class IgnoreRules
{
    /// <summary>Name of the project-specific ignore file.</summary>
    public const string IgnoreFileName = ".codai-gitignore";

    private static readonly string[] DefaultPatterns =
    [
        "codai-config.yml",
        ".codai-gitignore",
        ".git",
        ".svn",
        ".sum",
        ".tmp",
        ".tmpl",
        ".idea",
        ".vscode",
        "bin",
        "obj",
        "dist",
        "out",
        "node_modules",
        "*.exe",
        "*.dll",
        "*.log",
        "*.bak",
        "*.bkp",
        ".mp3",
        ".wav",
        ".aac",
        ".flac",
        ".ogg",
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".mkv",
        ".mp4",
        ".avi",
        ".mov",
        ".wmv",
        ".drawio",
        ".excalidraw",
    ];

    private static readonly char[] Separators = ['/', '\\'];

    public static IReadOnlyList<string> Defaults => DefaultPatterns;

    /// <summary>
    /// True when any segment of <paramref name="relativePath"/> matches a built-in
    /// ignore pattern.
    /// </summary>
    public static bool IsDefaultIgnored(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        foreach (var rawPart in relativePath.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.ToLowerInvariant();

            foreach (var pattern in DefaultPatterns)
            {
                if (pattern.StartsWith('*'))
                {
                    if (part.EndsWith(pattern[1..], StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                else if (part.StartsWith(pattern, StringComparison.Ordinal) ||
                         part.EndsWith(pattern, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Parses ignore patterns from the contents of a <c>.codai-gitignore</c> file.</summary>
    public static IReadOnlyList<string> ParsePatterns(string? fileContents)
    {
        if (string.IsNullOrWhiteSpace(fileContents))
        {
            return [];
        }

        var patterns = new List<string>();

        foreach (var line in fileContents.Split('\n'))
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            // Built-in rules always win; drop redundant user patterns.
            if (!IsDefaultIgnored(trimmed))
            {
                patterns.Add(trimmed);
            }
        }

        return patterns;
    }

    /// <summary>True when <paramref name="relativePath"/> matches a user ignore pattern.</summary>
    public static bool IsIgnoredByPatterns(string relativePath, IReadOnlyList<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return false;
        }

        var normalized = relativePath.Replace('\\', '/');

        foreach (var pattern in patterns)
        {
            var candidate = pattern.Replace('\\', '/');

            // Directory patterns ("build/") ignore everything beneath them.
            if (candidate.EndsWith('/'))
            {
                var prefix = candidate.TrimEnd('/');
                if (normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (GlobMatch(candidate, normalized) || GlobMatch(candidate, Path.GetFileName(normalized)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Minimal glob matcher with <c>*</c> (does not cross <c>/</c>) and <c>?</c>,
    /// mirroring <c>filepath.Match</c> semantics.
    /// </summary>
    internal static bool GlobMatch(string pattern, string value)
    {
        var p = 0;
        var v = 0;
        var star = -1;
        var mark = 0;

        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == value[v]))
            {
                p++;
                v++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p;
                mark = v;
                p++;
            }
            else if (star != -1 && mark < value.Length && value[mark] != '/')
            {
                mark++;
                v = mark;
                p = star + 1;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}

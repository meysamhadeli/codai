using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Codai.Tools;

/// <summary>
/// Translates glob patterns into regular expressions for matching project-relative
/// paths.
/// </summary>
/// <remarks>
/// Implemented here rather than pulled in from <c>Microsoft.Extensions.FileSystemGlobbing</c>
/// because that library enumerates the file system itself; these tools match against
/// an already-filtered list of relative paths, and need the same ignore rules as the
/// project scanner.
/// </remarks>
public static class GlobMatcher
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>Matches <paramref name="relativePath"/> against a single glob pattern.</summary>
    public static bool IsMatch(string relativePath, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        var regex = Cache.GetOrAdd(pattern, Compile);

        return regex.IsMatch(relativePath.Replace('\\', '/'));
    }

    /// <summary>
    /// Matches a pattern against a relative path or, failing that, against just its
    /// file name.
    /// </summary>
    /// <remarks>
    /// This is what a user means by an include filter such as <c>*.cs</c>: "any C# file",
    /// not "a C# file directly in the project root".
    /// </remarks>
    public static bool IsMatchPathOrFileName(string relativePath, string pattern)
    {
        if (IsMatch(relativePath, pattern))
        {
            return true;
        }

        var separator = relativePath.Replace('\\', '/').LastIndexOf('/');

        return separator >= 0 && IsMatch(relativePath[(separator + 1)..], pattern);
    }

    /// <summary>True when <paramref name="relativePath"/> matches any of the patterns.</summary>
    public static bool IsMatchAny(string relativePath, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (IsMatch(relativePath, pattern))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Matches without regard to case on file systems that are case-insensitive.</summary>
    public static bool PathsEqual(string left, string right) =>
        string.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'), PathComparison);

    private static Regex Compile(string glob)
    {
        var options = RegexOptions.CultureInvariant;
        if (PathComparison == StringComparison.OrdinalIgnoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        // Compiled patterns come from a model, so bound the work they can do.
        return new Regex(
            "^" + Translate(glob) + "$",
            options,
            TimeSpan.FromSeconds(2));
    }

    private static string Translate(string glob)
    {
        var builder = new StringBuilder(glob.Length * 2);
        var braceDepth = 0;

        for (var i = 0; i < glob.Length; i++)
        {
            var current = glob[i];

            switch (current)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        i++;

                        // "**/" also matches zero directories, so "**/*.cs" finds a
                        // file at the repository root.
                        if (i + 1 < glob.Length && glob[i + 1] == '/')
                        {
                            i++;
                            builder.Append("(?:.*/)?");
                        }
                        else
                        {
                            builder.Append(".*");
                        }
                    }
                    else
                    {
                        builder.Append("[^/]*");
                    }

                    break;

                case '?':
                    builder.Append("[^/]");
                    break;

                case '{':
                    braceDepth++;
                    builder.Append("(?:");
                    break;

                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                        builder.Append(')');
                    }
                    else
                    {
                        builder.Append(Regex.Escape("}"));
                    }

                    break;

                case ',' when braceDepth > 0:
                    builder.Append('|');
                    break;

                case '[':
                {
                    var close = glob.IndexOf(']', i + 1);
                    if (close < 0)
                    {
                        builder.Append(Regex.Escape("["));
                        break;
                    }

                    builder.Append('[');
                    for (var j = i + 1; j < close; j++)
                    {
                        var inner = glob[j];
                        builder.Append(inner is '\\' or ']' ? Regex.Escape(inner.ToString()) : inner.ToString());
                    }

                    builder.Append(']');
                    i = close;
                    break;
                }

                default:
                    builder.Append(Regex.Escape(current.ToString()));
                    break;
            }
        }

        return builder.ToString();
    }
}

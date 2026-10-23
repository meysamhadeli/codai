using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// <c>grep</c> — searches project file contents with a regular expression.
/// </summary>
/// <remarks>
/// The pattern and the include glob both come from a model, so neither is trusted:
/// the pattern is compiled with a match timeout (a catastrophic pattern must not hang
/// the session), and the search is bounded by a match count and a file count.
/// </remarks>
public sealed class GrepTool : ToolBase
{
    /// <summary>Maximum matching lines returned in one result.</summary>
    public const int MaxMatches = 300;

    /// <summary>Maximum number of characters of a matching line that are shown.</summary>
    public const int MaxLineLength = 300;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public override string Name => "grep";

    /// <inheritdoc />
    public override string Description =>
        "Search the contents of project files with a .NET regular expression. " +
        "Returns 'path, line number, line' for each match.";

    /// <inheritdoc />
    public override string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "pattern": {
              "type": "string",
              "description": "Regular expression to search for, e.g. 'class\\s+ChatRequest'."
            },
            "path": {
              "type": "string",
              "description": "Restrict the search to this directory. Defaults to the whole project."
            },
            "include": {
              "type": "string",
              "description": "Only search files matching this glob, e.g. '**/*.cs'."
            },
            "case_sensitive": {
              "type": "boolean",
              "description": "Match case. Defaults to false."
            }
          },
          "required": ["pattern"],
          "additionalProperties": false
        }
        """;

    /// <inheritdoc />
    public override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (!ToolArguments.TryGetString(arguments, "pattern", out var pattern, out var error))
        {
            return ToolResult.Failure(error);
        }

        if (!TryCompile(pattern, ToolArguments.GetBool(arguments, "case_sensitive"), out var regex, out error))
        {
            return ToolResult.Failure(error);
        }

        var path = ToolArguments.GetString(arguments, "path");
        var fullPath = context.ProjectRoot;
        if (!string.IsNullOrWhiteSpace(path) &&
            !ToolPaths.TryResolve(context, path, out fullPath, out error))
        {
            return ToolResult.Failure(error);
        }

        var include = ToolArguments.GetString(arguments, "include");

        var index = await ProjectFileIndex
            .BuildAsync(context.ProjectRoot, context.FileSystem, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var subPath = ProjectFileIndex.Normalize(ToolPaths.Relative(context, fullPath));
        if (subPath == ".")
        {
            subPath = string.Empty;
        }

        var candidates = index.FilesUnder(subPath);
        if (!string.IsNullOrWhiteSpace(include))
        {
            candidates = candidates.Where(file => GlobMatcher.IsMatchPathOrFileName(file, include));
        }

        var builder = new StringBuilder();
        var matchCount = 0;
        var fileCount = 0;
        var truncated = false;
        var timedOut = false;

        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (matchCount >= MaxMatches)
            {
                truncated = true;
                break;
            }

            string text;
            try
            {
                text = await context.FileSystem
                    .ReadAllTextAsync(Path.Combine(context.ProjectRoot, file), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (text.Contains('\0'))
            {
                continue;
            }

            fileCount++;

            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (matchCount >= MaxMatches)
                {
                    truncated = true;
                    break;
                }

                var line = lines[i].TrimEnd('\r');

                try
                {
                    if (!regex.IsMatch(line))
                    {
                        continue;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    timedOut = true;
                    continue;
                }

                builder.Append(file)
                    .Append(':')
                    .Append(i + 1)
                    .Append(": ")
                    .AppendLine(Clamp(line));

                matchCount++;
            }
        }

        if (matchCount == 0)
        {
            var scope = string.IsNullOrWhiteSpace(include) ? string.Empty : $" in files matching '{include}'";
            return ToolResult.Success(
                $"No matches for '{pattern}'{scope}. Searched {fileCount} file(s).");
        }

        var header = new StringBuilder()
            .Append(matchCount)
            .Append(" match(es) for '")
            .Append(pattern)
            .Append("' in ")
            .Append(fileCount)
            .AppendLine(" file(s):");

        builder.Insert(0, header);

        if (truncated)
        {
            builder.Append("[... stopped after ").Append(MaxMatches).Append(" matches]");
        }

        if (timedOut)
        {
            builder.AppendLine()
                .Append("[some files were skipped: the pattern timed out on them]");
        }

        return ToolResult.Success(builder.ToString().TrimEnd('\r', '\n'));
    }

    private static bool TryCompile(
        string pattern,
        bool caseSensitive,
        out Regex regex,
        out string error)
    {
        error = string.Empty;

        var options = RegexOptions.CultureInvariant;
        if (!caseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        try
        {
            regex = new Regex(pattern, options, MatchTimeout);
            return true;
        }
        catch (ArgumentException ex)
        {
            regex = null!;
            error = $"'{pattern}' is not a valid regular expression: {ex.Message}";
            return false;
        }
    }

    private static string Clamp(string line) =>
        line.Length <= MaxLineLength
            ? line
            : string.Concat(line.AsSpan(0, MaxLineLength), "...");
}

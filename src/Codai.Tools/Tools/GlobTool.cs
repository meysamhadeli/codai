using System.Text;
using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// <c>glob</c> — finds project files whose path matches a glob pattern.
/// </summary>
public sealed class GlobTool : ToolBase
{
    /// <summary>Maximum paths returned in one result.</summary>
    public const int MaxMatches = 500;

    /// <inheritdoc />
    public override string Name => "glob";

    /// <inheritdoc />
    public override string Description =>
        "Find files in the project by path pattern, for example '**/*.cs' or " +
        "'src/**/*Tests.cs'. Returns paths relative to the project root.";

    /// <inheritdoc />
    public override string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "pattern": {
              "type": "string",
              "description": "Glob pattern. Supports **, *, ? and {a,b}."
            },
            "path": {
              "type": "string",
              "description": "Restrict the search to this directory. Defaults to the whole project."
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

        var path = ToolArguments.GetString(arguments, "path");

        if (!string.IsNullOrWhiteSpace(path) &&
            !ToolPaths.TryResolve(context, path, out _, out error))
        {
            return ToolResult.Failure(error);
        }

        var index = await ProjectFileIndex
            .BuildAsync(context.ProjectRoot, context.FileSystem, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var subPath = ProjectFileIndex.Normalize(path);
        if (subPath == ".")
        {
            subPath = string.Empty;
        }

        var matches = index.FilesUnder(subPath)
            .Where(file => GlobMatcher.IsMatch(file, pattern))
            .ToList();

        if (matches.Count == 0)
        {
            return ToolResult.Success($"No files match '{pattern}'.");
        }

        var builder = new StringBuilder();
        builder.Append(matches.Count)
            .Append(" file(s) match '")
            .Append(pattern)
            .AppendLine("':");

        foreach (var match in matches.Take(MaxMatches))
        {
            builder.AppendLine(match);
        }

        if (matches.Count > MaxMatches)
        {
            builder.Append("[... ")
                .Append(matches.Count - MaxMatches)
                .Append(" more match(es)]");
        }

        return ToolResult.Success(builder.ToString().TrimEnd('\r', '\n'));
    }
}

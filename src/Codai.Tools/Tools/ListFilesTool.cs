using System.Text;
using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// <c>list_files</c> — lists the entries of a project directory.
/// </summary>
/// <remarks>
/// Listings are produced from <see cref="ProjectFileIndex"/>, so a directory listing
/// applies exactly the same ignore rules as a search. Listing a directory's real
/// contents and then ignoring the rules would leak <c>bin/</c> and <c>obj/</c> paths
/// into the conversation and pull the model towards generated code.
/// </remarks>
public sealed class ListFilesTool : ToolBase
{
    /// <summary>Maximum entries returned for a single directory.</summary>
    public const int MaxShallowEntries = 500;

    /// <summary>Maximum paths returned for a recursive listing.</summary>
    public const int MaxRecursiveEntries = 1500;

    /// <inheritdoc />
    public override string Name => "list_files";

    /// <inheritdoc />
    public override string Description =>
        "List the files and directories at a path in the project. " +
        "Uses the project's ignore rules, so build output and dependencies are not shown.";

    /// <inheritdoc />
    public override string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Directory relative to the project root. Defaults to the project root."
            },
            "recursive": {
              "type": "boolean",
              "description": "List every file below the path instead of one level. Defaults to false."
            }
          },
          "additionalProperties": false
        }
        """;

    /// <inheritdoc />
    public override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var path = ToolArguments.GetString(arguments, "path", ".");
        var recursive = ToolArguments.GetBool(arguments, "recursive");

        if (!ToolPaths.TryResolve(context, path, out var fullPath, out var error))
        {
            return ToolResult.Failure(error);
        }

        if (!context.FileSystem.DirectoryExists(fullPath))
        {
            return ToolResult.Failure($"Directory '{path}' does not exist.");
        }

        var index = await ProjectFileIndex
            .BuildAsync(context.ProjectRoot, context.FileSystem, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var subPath = ProjectFileIndex.Normalize(path);
        if (subPath == ".")
        {
            subPath = string.Empty;
        }

        return recursive
            ? ListRecursive(index, path, subPath)
            : ListOneLevel(index, path, subPath);
    }

    private static ToolResult ListOneLevel(ProjectFileIndex index, string path, string subPath)
    {
        var prefix = subPath.Length == 0 ? string.Empty : subPath + "/";

        var entries = index.Directories
            .Where(directory => IsDirectChild(directory, prefix))
            .Select(directory => directory[prefix.Length..])
            .Concat(index.Files
                .Where(file => IsDirectChild(file, prefix))
                .Select(file => file[prefix.Length..]))
            .ToList();

        if (entries.Count == 0)
        {
            return ToolResult.Success($"'{path}' is empty (or every entry is ignored).");
        }

        var builder = new StringBuilder();
        builder.Append(path).AppendLine(":");

        foreach (var entry in entries.Take(MaxShallowEntries))
        {
            builder.AppendLine(entry);
        }

        if (entries.Count > MaxShallowEntries)
        {
            AppendMore(builder, entries.Count - MaxShallowEntries, "entry/entries");
        }

        return ToolResult.Success(builder.ToString().TrimEnd('\r', '\n'));
    }

    private static ToolResult ListRecursive(ProjectFileIndex index, string path, string subPath)
    {
        var files = index.FilesUnder(subPath).ToList();

        if (files.Count == 0)
        {
            return ToolResult.Success($"'{path}' contains no files that pass the ignore rules.");
        }

        var builder = new StringBuilder();
        builder.Append(path)
            .Append(" (")
            .Append(files.Count)
            .AppendLine(" file(s)):");

        foreach (var file in files.Take(MaxRecursiveEntries))
        {
            builder.AppendLine(file);
        }

        if (files.Count > MaxRecursiveEntries)
        {
            AppendMore(builder, files.Count - MaxRecursiveEntries, "file(s)");
        }

        return ToolResult.Success(builder.ToString().TrimEnd('\r', '\n'));
    }

    /// <summary>True when <paramref name="relativePath"/> sits directly under <paramref name="prefix"/>.</summary>
    private static bool IsDirectChild(string relativePath, string prefix)
    {
        if (!relativePath.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = relativePath.AsSpan(prefix.Length);

        // Anything with another separator is nested deeper, so it is not a direct child.
        return !remainder.IsEmpty && remainder.TrimEnd('/').IndexOf('/') < 0;
    }

    private static void AppendMore(StringBuilder builder, int count, string noun) =>
        builder.Append("[... ").Append(count).Append(' ').Append(noun).Append(']');
}

using System.Text;
using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Tools;

internal sealed class ApplyPatchTool : ToolBase
{
    public const int MaxPatchChars = 200_000;

    public override string Name => "apply_patch";

    public override string Description =>
        "Apply a structured patch containing add, update, or delete file blocks. " +
        "Update hunks must match the current file exactly.";

    public override bool IsReadOnly => false;

    public override string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "patch": {
              "type": "string",
              "maxLength": 200000,
              "description": "Patch wrapped in *** Begin Patch and *** End Patch markers."
            }
          },
          "required": ["patch"],
          "additionalProperties": false
        }
        """;

    public override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (!ToolWrites.TryGetText(arguments, "patch", out var patch, out var error))
        {
            return ToolResult.Failure(error);
        }

        if (patch.Length == 0 || patch.Length > MaxPatchChars)
        {
            return ToolResult.Failure($"Patch must contain 1-{MaxPatchChars} characters.");
        }

        IReadOnlyList<PatchOperation> operations;
        try
        {
            operations = PatchParser.Parse(patch);
        }
        catch (FormatException exception)
        {
            return ToolResult.Failure(exception.Message);
        }

        var changed = new List<string>();
        var fileChanges = new List<ToolFileChange>();
        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ToolResult result;
            try
            {
                result = await ApplyOperationAsync(operation, context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                result = ToolResult.Failure($"Could not patch '{operation.Path}': {exception.Message}");
            }
            fileChanges.AddRange(result.FileChanges);
            if (!result.IsSuccess)
            {
                return result with
                {
                    Content = string.Join(Environment.NewLine, changed.Append(result.Content)),
                    FileChanges = fileChanges.ToArray(),
                };
            }
            changed.Add(result.Content);
        }

        return ToolResult.Success(string.Join(Environment.NewLine, changed)) with { FileChanges = fileChanges.ToArray() };
    }

    private static async Task<ToolResult> ApplyOperationAsync(
        PatchOperation operation,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolPaths.TryResolve(context, operation.Path, out var fullPath, out var error))
        {
            return ToolResult.Failure(error);
        }
        var relativePath = ToolPaths.Relative(context, fullPath);
        if (operation.Kind == PatchOperationKind.Add)
        {
            if (context.FileSystem.FileExists(fullPath))
            {
                return ToolResult.Failure($"Cannot add '{relativePath}': file already exists.");
            }
            if (!ToolWrites.TryResolveForWrite(context, relativePath, out fullPath, out error))
            {
                return ToolResult.Failure(error);
            }
            var added = await ToolWrites.WriteAsync(context, relativePath, fullPath, operation.Content,
                previousContent: null, cancellationToken).ConfigureAwait(false);
            return added.IsSuccess ? added with { Content = $"Added {relativePath}" } : added;
        }

        if (!context.FileSystem.FileExists(fullPath))
        {
            return ToolResult.Failure($"Cannot {operation.Kind.ToString().ToLowerInvariant()} '{relativePath}': file does not exist.");
        }
        var read = await ToolWrites.TryReadAsync(context, relativePath, fullPath, cancellationToken).ConfigureAwait(false);
        if (!read.Success)
        {
            return read.Failure;
        }
        if (operation.Kind == PatchOperationKind.Delete)
        {
            var change = ToolFileChanges.Describe(relativePath, read.Content, string.Empty);
            context.FileSystem.DeleteFile(fullPath);
            return ToolResult.Success($"Deleted {relativePath}") with { FileChanges = [change] };
        }

        string updated;
        try
        {
            updated = ApplyHunks(read.Content, operation.Hunks, relativePath);
        }
        catch (FormatException exception)
        {
            return ToolResult.Failure(exception.Message);
        }
        updated = ToolWrites.MatchNewlines(updated, read.Content);
        var written = await ToolWrites.WriteAsync(context, relativePath, fullPath, updated,
            read.Content, cancellationToken).ConfigureAwait(false);
        return written.IsSuccess ? written with { Content = $"Updated {relativePath}" } : written;
    }

    private static string ApplyHunks(string current, IReadOnlyList<PatchHunk> hunks, string path)
    {
        var lines = current.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var offset = 0;
        foreach (var hunk in hunks)
        {
            var index = FindHunk(lines, hunk, offset);
            if (index < 0)
            {
                throw new FormatException($"Patch hunk does not match '{path}'. The file changed since it was read.");
            }

            lines.RemoveRange(index, hunk.Removed.Count);
            lines.InsertRange(index, hunk.Added);
            offset = index + hunk.Added.Count;
        }

        return string.Join('\n', lines) + (current.EndsWith('\n') ? "\n" : string.Empty);
    }

    private static int FindHunk(List<string> lines, PatchHunk hunk, int start)
    {
        if (hunk.Removed.Count == 0)
        {
            return Math.Min(start, lines.Count);
        }

        for (var index = start; index <= lines.Count - hunk.Removed.Count; index++)
        {
            if (lines.Skip(index).Take(hunk.Removed.Count).SequenceEqual(hunk.Removed, StringComparer.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}

internal enum PatchOperationKind
{
    Add,
    Update,
    Delete,
}

internal sealed record PatchOperation(
    PatchOperationKind Kind,
    string Path,
    string Content,
    IReadOnlyList<PatchHunk> Hunks);

internal sealed record PatchHunk(IReadOnlyList<string> Removed, IReadOnlyList<string> Added);

internal static class PatchParser
{
    public static IReadOnlyList<PatchOperation> Parse(string patch)
    {
        var lines = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length < 2 || lines[0] != "*** Begin Patch" || lines[^1] != "*** End Patch")
        {
            throw new FormatException("Patch must start with '*** Begin Patch' and end with '*** End Patch'.");
        }

        var operations = new List<PatchOperation>();
        for (var index = 1; index < lines.Length - 1; index++)
        {
            var header = lines[index];
            if (header.StartsWith("*** Add File: ", StringComparison.Ordinal))
            {
                var path = PathOf(header, "*** Add File: ");
                var content = new StringBuilder();
                index++;
                while (index < lines.Length - 1 && !lines[index].StartsWith("*** ", StringComparison.Ordinal))
                {
                    if (!lines[index].StartsWith('+'))
                    {
                        throw new FormatException($"Added file '{path}' contains a non-added line.");
                    }

                    content.AppendLine(lines[index][1..]);
                    index++;
                }

                operations.Add(new PatchOperation(PatchOperationKind.Add, path, TrimFinalNewline(content.ToString()), []));
                index--;
            }
            else if (header.StartsWith("*** Delete File: ", StringComparison.Ordinal))
            {
                operations.Add(new PatchOperation(PatchOperationKind.Delete, PathOf(header, "*** Delete File: "), string.Empty, []));
            }
            else if (header.StartsWith("*** Update File: ", StringComparison.Ordinal))
            {
                var path = PathOf(header, "*** Update File: ");
                var hunkLines = new List<string>();
                index++;
                while (index < lines.Length - 1 && !lines[index].StartsWith("*** ", StringComparison.Ordinal))
                {
                    hunkLines.Add(lines[index]);
                    index++;
                }

                operations.Add(new PatchOperation(PatchOperationKind.Update, path, string.Empty, ParseHunks(hunkLines, path)));
                index--;
            }
            else
            {
                throw new FormatException($"Unexpected patch line '{header}'.");
            }
        }

        if (operations.Count == 0)
        {
            throw new FormatException("Patch does not contain any file operations.");
        }

        return operations;
    }

    private static List<PatchHunk> ParseHunks(List<string> lines, string path)
    {
        var hunks = new List<PatchHunk>();
        var removed = new List<string>();
        var added = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                Flush();
                continue;
            }

            if (line.Length == 0 || line[0] == ' ')
            {
                var value = line.Length == 0 ? string.Empty : line[1..];
                removed.Add(value);
                added.Add(value);
            }
            else if (line[0] == '-')
            {
                removed.Add(line[1..]);
            }
            else if (line[0] == '+')
            {
                added.Add(line[1..]);
            }
            else if (line == "\\ No newline at end of file")
            {
                continue;
            }
            else
            {
                throw new FormatException($"Invalid update line in '{path}': {line}");
            }
        }

        Flush();
        if (hunks.Count == 0)
        {
            throw new FormatException($"Update for '{path}' does not contain a hunk.");
        }

        return hunks;

        void Flush()
        {
            if (removed.Count > 0 || added.Count > 0)
            {
                hunks.Add(new PatchHunk([.. removed], [.. added]));
                removed.Clear();
                added.Clear();
            }
        }
    }

    private static string PathOf(string header, string prefix)
    {
        var path = header[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new FormatException("Patch paths must be non-empty and relative to the project.");
        }

        return path;
    }

    private static string TrimFinalNewline(string value) => value.EndsWith('\n') ? value[..^1] : value;
}
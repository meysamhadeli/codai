using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// <c>edit_file</c>: replaces an exact piece of text in a file.
/// </summary>
/// <remarks>
/// <para>
/// The edit is refused unless <c>old_string</c> appears exactly once. That is the whole
/// safety property: a replacement of text that appears several times would silently pick
/// one of them, and the model would believe it changed something else. Refusing with the
/// occurrence count turns an ambiguous edit into a message the model can act on —
/// include more surrounding lines.
/// </para>
/// <para>
/// <c>replace_all</c> is available for the case where every occurrence really is meant,
/// such as renaming an identifier.
/// </para>
/// </remarks>
internal sealed class EditFileTool : ToolBase
{
    /// <inheritdoc />
    public override string Name => "edit_file";

    /// <inheritdoc />
    public override string Description =>
        "Replace an exact piece of text in a file. Fails unless the text appears exactly " +
        "once, so include surrounding lines to make it unique.";

    /// <inheritdoc />
    public override bool IsReadOnly => false;

    /// <inheritdoc />
    public override string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "File to edit, relative to the project root."
            },
            "old_string": {
              "type": "string",
              "description": "The exact text to replace, copied from the file including its indentation."
            },
            "new_string": {
              "type": "string",
              "description": "The text to put in its place. Empty deletes the text."
            },
            "replace_all": {
              "type": "boolean",
              "description": "Replace every occurrence instead of requiring exactly one. Defaults to false."
            }
          },
          "required": ["path", "old_string", "new_string"]
        }
        """;

    /// <inheritdoc />
    public override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (!ToolArguments.TryGetString(arguments, "path", out var path, out var error))
        {
            return ToolResult.Failure(error);
        }

        if (!ToolWrites.TryGetText(arguments, "old_string", out var oldString, out error))
        {
            return ToolResult.Failure(error);
        }

        if (!ToolWrites.TryGetText(arguments, "new_string", out var newString, out error))
        {
            return ToolResult.Failure(error);
        }

        if (oldString.Length == 0)
        {
            return ToolResult.Failure(
                "'old_string' cannot be empty. To create a file use write_file, and to " +
                "insert text include the line it goes after.");
        }

        if (string.Equals(oldString, newString, StringComparison.Ordinal))
        {
            return ToolResult.Failure("'old_string' and 'new_string' are identical; nothing to change.");
        }

        if (!ToolPaths.TryResolve(context, path, out var fullPath, out error))
        {
            return ToolResult.Failure(error);
        }

        var normalized = ToolPaths.Relative(context, fullPath);

        var read = await ToolWrites
            .TryReadAsync(context, normalized, fullPath, cancellationToken)
            .ConfigureAwait(false);

        if (!read.Success)
        {
            return read.Failure;
        }

        var content = read.Content;
        var needle = ToolWrites.MatchNewlines(oldString, content);
        var occurrences = ToolWrites.CountOccurrences(content, needle);

        if (occurrences == 0)
        {
            return ToolResult.Failure(
                $"'{normalized}' does not contain the text of 'old_string'. It must match the " +
                "file exactly, including indentation and line breaks. Read the file with " +
                "read_file and copy the text from there.");
        }

        var replaceAll = ToolArguments.GetBool(arguments, "replace_all");

        if (occurrences > 1 && !replaceAll)
        {
            return ToolResult.Failure(
                "The text of 'old_string' appears " + occurrences + " times in '" + normalized +
                "'. Include more surrounding lines so it is unique, or pass " +
                "\"replace_all\": true to change every occurrence.");
        }

        var line = ToolWrites.LineOf(content, needle);
        var updated = content.Replace(
            needle,
            ToolWrites.MatchNewlines(newString, content),
            StringComparison.Ordinal);

        var written = await ToolWrites
            .WriteAsync(context, normalized, fullPath, updated, existed: true, cancellationToken)
            .ConfigureAwait(false);

        if (!written.IsSuccess)
        {
            return written;
        }

        var changed = replaceAll ? occurrences : 1;

        return ToolResult.Success(
            ToolWrites.DescribeEdit(normalized, line, needle, newString) +
            (changed > 1 ? $"\n\n({changed} occurrences replaced.)" : string.Empty));
    }
}

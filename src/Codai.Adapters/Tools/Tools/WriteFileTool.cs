using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// <c>write_file</c>: creates a file or replaces its whole contents.
/// </summary>
/// <remarks>
/// This is the sledgehammer of the tool set and the reason the tool is not read-only:
/// it overwrites whatever was there. It is offered for creating a file and for replacing
/// one whose content the model has just read; <c>edit_file</c> is the tool for changing
/// part of a file, and its description says so, because an overwrite of a large file the
/// model only partly knows is how a change turns into a deletion.
/// </remarks>
internal sealed class WriteFileTool : ToolBase, IFileTargetTool
{
    /// <inheritdoc />
    public override string Name => "write_file";

    /// <inheritdoc />
    public override string Description =>
        "Write a file: create it, or replace all of its contents. Use edit_file to change " +
        "part of an existing file.";

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
              "description": "File to write, relative to the project root. Directories are created as needed."
            },
            "content": {
              "type": "string",
              "description": "The complete new contents of the file."
            }
          },
          "required": ["path", "content"]
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

        if (!ToolWrites.TryGetText(arguments, "content", out var content, out error))
        {
            return ToolResult.Failure(error);
        }

        if (content.Length > ToolWrites.MaxWriteBytes)
        {
            return ToolResult.Failure(
                $"The content is larger than {ToolWrites.MaxWriteBytes / (1024 * 1024)} MB. " +
                "Write the file in smaller pieces.");
        }

        if (!ToolWrites.TryResolveForWrite(context, path, out var fullPath, out error))
        {
            return ToolResult.Failure(error);
        }

        var normalized = ToolPaths.Relative(context, fullPath);
        var existed = context.FileSystem.FileExists(fullPath);

        if (existed)
        {
            var existing = await ToolWrites
                .TryReadAsync(context, normalized, fullPath, cancellationToken)
                .ConfigureAwait(false);

            if (!existing.Success)
            {
                return existing.Failure;
            }

            var matched = ToolWrites.MatchNewlines(content, existing.Content);
            return await ToolWrites
                .WriteAsync(context, normalized, fullPath, matched, existing.Content, cancellationToken)
                .ConfigureAwait(false);
        }

        return await ToolWrites
            .WriteAsync(context, normalized, fullPath, content, previousContent: null, cancellationToken)
            .ConfigureAwait(false);
    }
}

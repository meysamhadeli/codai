using System.Globalization;
using System.Text;
using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// <c>read_file</c> — returns the numbered contents of one text file.
/// </summary>
public sealed class ReadFileTool : ToolBase
{
    /// <summary>Lines returned when the model does not ask for a range.</summary>
    public const int DefaultLineLimit = 2000;

    /// <summary>Hard ceiling on lines in one result, whatever the model asks for.</summary>
    public const int MaxLineLimit = 5000;

    /// <summary>A file larger than this is reported rather than read into the context.</summary>
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    /// <inheritdoc />
    public override string Name => "read_file";

    /// <inheritdoc />
    public override string Description =>
        "Read a text file from the project and return its contents with line numbers. " +
        "Use offset and limit to page through a long file.";

    /// <inheritdoc />
    public override string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
              "description": "Path relative to the project root, e.g. src/Codai.Core/Chat/ChatRequest.cs"
            },
            "offset": {
              "type": "integer",
              "description": "1-based line number to start from. Defaults to 1."
            },
            "limit": {
              "type": "integer",
              "description": "Maximum number of lines to return. Defaults to 2000."
            }
          },
          "required": ["path"],
          "additionalProperties": false
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

        if (!ToolPaths.TryResolve(context, path, out var fullPath, out error))
        {
            return ToolResult.Failure(error);
        }

        if (!context.FileSystem.FileExists(fullPath))
        {
            return ToolResult.Failure($"File '{path}' does not exist.");
        }

        try
        {
            if (context.FileSystem.GetFileSize(fullPath) > MaxFileSizeBytes)
            {
                return ToolResult.Failure(
                    $"File '{path}' is larger than {MaxFileSizeBytes / (1024 * 1024)} MB. " +
                    "Use grep or glob to narrow down the part you need.");
            }

            var text = await context.FileSystem.ReadAllTextAsync(fullPath, cancellationToken)
                .ConfigureAwait(false);

            if (text.Contains('\0'))
            {
                return ToolResult.Failure($"File '{path}' appears to be binary.");
            }

            return Render(text, path, arguments);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult.Failure($"Could not read '{path}': {ex.Message}");
        }
    }

    private static ToolResult Render(string text, string path, JsonElement arguments)
    {
        // Split on '\n' and strip a trailing '\r', so CRLF and LF files number identically.
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd('\r');
        }

        // A trailing newline produces a final empty element that is not a real line.
        var totalLines = lines.Length > 0 && lines[^1].Length == 0
            ? lines.Length - 1
            : lines.Length;

        if (totalLines == 0)
        {
            return ToolResult.Success($"{path} is empty.");
        }

        var offset = Math.Max(ToolArguments.GetInt(arguments, "offset", 1, min: 1), 1);

        if (offset > totalLines)
        {
            return ToolResult.Failure(
                $"offset {offset} is past the end of '{path}', which has {totalLines} line(s).");
        }

        var limit = Math.Clamp(
            ToolArguments.GetInt(arguments, "limit", DefaultLineLimit, min: 1),
            1,
            MaxLineLimit);

        var lastLine = Math.Min(offset + limit - 1, totalLines);
        var width = totalLines.ToString(CultureInfo.InvariantCulture).Length;

        var builder = new StringBuilder();
        builder.Append(path)
            .Append(" (lines ")
            .Append(offset)
            .Append('-')
            .Append(lastLine)
            .Append(" of ")
            .Append(totalLines)
            .AppendLine("):");

        for (var lineNumber = offset; lineNumber <= lastLine; lineNumber++)
        {
            builder.Append(lineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(width))
                .Append('\t')
                .AppendLine(lines[lineNumber - 1]);
        }

        if (lastLine < totalLines)
        {
            builder.Append("[... ")
                .Append(totalLines - lastLine)
                .Append(" more line(s); continue with offset=")
                .Append(lastLine + 1)
                .Append(']');
        }

        return ToolResult.Success(builder.ToString().TrimEnd('\r', '\n'));
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// <c>read_file</c> — returns bounded numbered contents of project text files.
/// </summary>
public sealed class ReadFileTool : ToolBase, IFileTargetTool
{
    /// <summary>Lines returned when the model does not ask for a range.</summary>
    public const int DefaultLineLimit = 2000;

    /// <summary>Hard ceiling on lines per file, whatever the model asks for.</summary>
    public const int MaxLineLimit = 5000;

    /// <summary>A file larger than this is reported rather than read into the context.</summary>
    public const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public const int MaxFileCount = 20;

    public const int MaxPathChars = 2048;

    private const string TruncationNotice = "\n[... output budget reached; narrow paths or use offset/limit.]";

    /// <inheritdoc />
    public override string Name => "read_file";

    /// <inheritdoc />
    public override string Description =>
        "Read one or up to 20 project text files with line numbers and a shared output budget. " +
        "Provide path or paths, not both. Offset and limit apply to each file.";

    /// <inheritdoc />
    public override string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "path": {
              "type": "string",
                            "maxLength": 2048,
              "description": "Path relative to the project root, e.g. src/Codai.Core/Chat/ChatRequest.cs"
            },
                        "paths": {
                            "type": "array",
                            "minItems": 1,
                            "maxItems": 20,
                            "items": { "type": "string", "minLength": 1, "maxLength": 2048 },
                            "description": "Project paths to read in order, sharing one output budget."
                        },
            "offset": {
              "type": "integer",
              "description": "1-based line number to start from. Defaults to 1."
            },
            "limit": {
              "type": "integer",
              "description": "Maximum number of lines per file. Defaults to 2000."
            }
          },
          "oneOf": [{ "required": ["path"] }, { "required": ["paths"] }],
          "additionalProperties": false
        }
        """;

    /// <inheritdoc />
    public override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return ToolResult.Failure("Arguments must be an object.");
        }

        if (arguments.TryGetProperty("paths", out var paths))
        {
            if (arguments.TryGetProperty("path", out _))
            {
                return ToolResult.Failure("Provide path or paths, not both.");
            }

            return await ReadBatchAsync(paths, arguments, context, cancellationToken).ConfigureAwait(false);
        }

        if (!ToolArguments.TryGetString(arguments, "path", out var path, out var error))
        {
            return ToolResult.Failure(error);
        }

        if (!TryResolve(context, path, out var fullPath, out error))
        {
            return ToolResult.Failure(error);
        }

        return await ReadAsync(path, fullPath, arguments, context,
            ToolRunner.DefaultMaxResultChars, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryResolve(ToolContext context, string path, out string fullPath, out string error)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxPathChars)
        {
            fullPath = string.Empty;
            error = $"File paths must contain 1 to {MaxPathChars} non-blank characters.";
            return false;
        }

        return ToolPaths.TryResolve(context, path, out fullPath, out error);
    }

    private static async Task<ToolResult> ReadBatchAsync(JsonElement paths, JsonElement arguments,
        ToolContext context, CancellationToken cancellationToken)
    {
        if (paths.ValueKind != JsonValueKind.Array || paths.GetArrayLength() is < 1 or > MaxFileCount)
        {
            return ToolResult.Failure($"paths must contain 1 to {MaxFileCount} file paths.");
        }

        var resolved = new List<(string Path, string FullPath)>();
        var unique = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var item in paths.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.ValueKind != JsonValueKind.String)
            {
                return ToolResult.Failure("Every paths entry must be a string.");
            }

            var path = item.GetString()!;
            if (!TryResolve(context, path, out var fullPath, out var error))
            {
                return ToolResult.Failure(error);
            }

            if (!unique.Add(fullPath))
            {
                return ToolResult.Failure($"Duplicate file path '{path}'.");
            }

            resolved.Add((path, fullPath));
        }

        var builder = new StringBuilder();
        var failed = false;
        var budget = ToolRunner.DefaultMaxResultChars - TruncationNotice.Length;
        foreach (var (path, fullPath) in resolved)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = budget - builder.Length - 2;
            if (remaining < path.Length + 160)
            {
                builder.Append(TruncationNotice);
                break;
            }

            var result = await ReadAsync(path, fullPath, arguments, context, remaining, cancellationToken)
                .ConfigureAwait(false);
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            failed |= !result.IsSuccess;
            var content = result.IsSuccess ? result.Content : $"[error] {result.Content}";
            builder.Append(content.AsSpan(0, Math.Min(content.Length, remaining)));
            if (content.Length > remaining)
            {
                builder.Append(TruncationNotice);
                break;
            }
        }

        return failed ? ToolResult.Failure(builder.ToString()) : ToolResult.Success(builder.ToString());
    }

    private static async Task<ToolResult> ReadAsync(string path, string fullPath, JsonElement arguments,
        ToolContext context, int maxChars, CancellationToken cancellationToken)
    {
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

            return Render(text, path, arguments, maxChars);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult.Failure($"Could not read '{path}': {ex.Message}");
        }
    }

    private static ToolResult Render(string text, string path, JsonElement arguments, int maxChars)
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
            var prefix = lineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(width) + "\t";
            var line = lines[lineNumber - 1];
            var available = maxChars - builder.Length - TruncationNotice.Length;
            if (prefix.Length + line.Length + Environment.NewLine.Length > available)
            {
                var prefixLength = Math.Min(prefix.Length, Math.Max(available, 0));
                builder.Append(prefix.AsSpan(0, prefixLength));
                available -= prefixLength;
                builder.Append(line.AsSpan(0, Math.Min(line.Length, Math.Max(available, 0))));
                builder.Append(TruncationNotice);
                return ToolResult.Success(builder.ToString());
            }

            builder.Append(prefix).AppendLine(line);
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

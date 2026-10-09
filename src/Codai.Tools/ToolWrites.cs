using System.Text;
using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// Shared file-writing behaviour: resolve inside the project, report what changed, and
/// keep the change describable.
/// </summary>
/// <remarks>
/// Write tools differ in what they are told to change, not in how a change reaches the
/// disk, so the parts that must behave identically live here rather than being repeated
/// in each tool. That includes the one thing that is easy to get wrong: line endings.
/// </remarks>
internal static class ToolWrites
{
    /// <summary>Largest file a write tool will replace.</summary>
    public const long MaxWriteBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Resolves <paramref name="relativePath"/> for writing, creating its directory.
    /// </summary>
    public static bool TryResolveForWrite(
        ToolContext context,
        string relativePath,
        out string fullPath,
        out string error)
    {
        if (!ToolPaths.TryResolve(context, relativePath, out fullPath, out error))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrEmpty(directory))
        {
            error = $"'{relativePath}' does not name a file inside the project.";
            return false;
        }

        try
        {
            if (!context.FileSystem.DirectoryExists(directory))
            {
                context.FileSystem.CreateDirectory(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"The directory for '{relativePath}' could not be created: {ex.Message}";
            return false;
        }

        return true;
    }

    /// <summary>Writes <paramref name="content"/> and describes what happened.</summary>
    public static async Task<ToolResult> WriteAsync(
        ToolContext context,
        string relativePath,
        string fullPath,
        string content,
        bool existed,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.FileSystem
                .WriteAllTextAsync(fullPath, content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ToolResult.Failure($"'{relativePath}' could not be written: {ex.Message}");
        }

        var verb = existed ? "Updated" : "Created";
        var lines = CountLines(content);

        return ToolResult.Success(
            $"{verb} {relativePath} ({lines} line(s), {content.Length} characters).");
    }

    /// <summary>Reads a file that is about to be edited.</summary>
    public static async Task<(bool Success, string Content, ToolResult Failure)> TryReadAsync(
        ToolContext context,
        string relativePath,
        string fullPath,
        CancellationToken cancellationToken)
    {
        if (!context.FileSystem.FileExists(fullPath))
        {
            return (false, string.Empty, ToolResult.Failure(
                $"'{relativePath}' does not exist. Use write_file to create it, or " +
                "list_files to find the right path."));
        }

        if (context.FileSystem.GetFileSize(fullPath) > MaxWriteBytes)
        {
            return (false, string.Empty, ToolResult.Failure(
                $"'{relativePath}' is larger than {MaxWriteBytes / (1024 * 1024)} MB and was " +
                "not opened for editing."));
        }

        try
        {
            var content = await context.FileSystem
                .ReadAllTextAsync(fullPath, cancellationToken)
                .ConfigureAwait(false);

            return (true, content, ToolResult.Success(string.Empty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, string.Empty, ToolResult.Failure(
                $"'{relativePath}' could not be read: {ex.Message}"));
        }
    }

    /// <summary>
    /// Adapts the newlines of <paramref name="text"/> to match
    /// <paramref name="reference"/>.
    /// </summary>
    /// <remarks>
    /// A model writes <c>\n</c>; a file saved on Windows contains <c>\r\n</c>. Searching a
    /// CRLF file for an LF string finds nothing, which reads to the model as "the text I
    /// am looking at is not in the file" and turns into a pointless retry. The original
    /// Go implementation had the mirror image of this bug: it stripped the <c>+</c> from
    /// added lines with a blanket string replace and corrupted any code containing one.
    /// Matching the file is the fix that does not corrupt anything.
    /// </remarks>
    public static string MatchNewlines(string text, string reference)
    {
        var usesCrlf = reference.Contains("\r\n", StringComparison.Ordinal);

        if (usesCrlf)
        {
            return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    /// <summary>Counts occurrences of <paramref name="value"/> in <paramref name="text"/>.</summary>
    public static int CountOccurrences(string text, string value)
    {
        if (value.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>One-based line number at which <paramref name="value"/> first appears.</summary>
    public static int LineOf(string text, string value)
    {
        var index = text.IndexOf(value, StringComparison.Ordinal);

        if (index < 0)
        {
            return 1;
        }

        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>One-based line number of the last line of the file.</summary>
    public static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var lines = 1;

        foreach (var character in text)
        {
            if (character == '\n')
            {
                lines++;
            }
        }

        return text.EndsWith('\n') ? lines - 1 : lines;
    }

    /// <summary>
    /// A short description of an edit, in the shape of a diff, so the model can confirm
    /// the change landed where it meant and the user can see it in the transcript.
    /// </summary>
    public static string DescribeEdit(string relativePath, int line, string removed, string added)
    {
        var builder = new StringBuilder()
            .Append("Edited ").Append(relativePath)
            .Append(" at line ").Append(line).AppendLine(".")
            .AppendLine()
            .Append(Format(removed, '-'))
            .Append(Format(added, '+'));

        return builder.ToString().TrimEnd();
    }

    private static string Format(string text, char marker)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        foreach (var line in lines)
        {
            builder.Append(marker).Append(' ').AppendLine(line);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a string argument that may be empty, which <see cref="ToolArguments.GetString"/>
    /// does not allow: an edit whose replacement is the empty string is a deletion.
    /// </summary>
    public static bool TryGetText(
        JsonElement arguments,
        string name,
        out string value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var element))
        {
            error = $"Missing required argument '{name}'.";
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"Argument '{name}' must be a string.";
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }
}

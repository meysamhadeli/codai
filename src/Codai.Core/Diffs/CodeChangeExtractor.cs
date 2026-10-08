using System.Text.RegularExpressions;
using Codai.Core.Abstractions;
using Codai.Core.Models;

namespace Codai.Core.Diffs;

/// <summary>
/// Parses a model response of the form
/// <code>
/// File: relative/path.ext
/// ```lang
/// ... annotated body ...
/// ```
/// </code>
/// into <see cref="CodeChange"/> instances.
/// </summary>
/// <remarks>
/// The file-path matching regex is a direct port of the original Go implementation,
/// so the accepted header formats stay identical.
/// </remarks>
public sealed partial class CodeChangeExtractor : ICodeChangeExtractor
{
    private const string FilePathPattern =
        @"(?i)(?:\d+\.\s*|File:\s*)[`']?([^\s*`']+?\.[a-zA-Z0-9]+)[`']?\b";

    private static readonly string[] TextFileExtensions = [".md", ".txt"];

    [GeneratedRegex(FilePathPattern)]
    private static partial Regex FilePathRegex();

    public IReadOnlyList<CodeChange> Extract(string aiResponse)
    {
        if (string.IsNullOrEmpty(aiResponse))
        {
            return [];
        }

        var lines = aiResponse.Split('\n');
        var changes = new List<CodeChange>();

        string? currentFilePath = null;
        var currentCodeBlock = new List<string>();
        var insideCodeBlock = false;
        var isTextFile = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Detect a new file path header.
            if (!insideCodeBlock && FilePathRegex().IsMatch(trimmed))
            {
                Flush(changes, currentFilePath, currentCodeBlock, isTextFile);
                currentCodeBlock = [];

                currentFilePath = FilePathRegex().Match(trimmed).Groups[1].Value;
                isTextFile = IsTextFile(currentFilePath);
                continue;
            }

            // Fence handling (never for text files, whose bodies are taken verbatim).
            if (!isTextFile && trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (!insideCodeBlock)
                {
                    if (currentFilePath is not null)
                    {
                        insideCodeBlock = true;
                    }
                }
                else
                {
                    insideCodeBlock = false;
                    Flush(changes, currentFilePath, currentCodeBlock, isTextFile);
                    currentCodeBlock = [];
                    currentFilePath = null;
                }

                continue;
            }

            if (isTextFile)
            {
                currentCodeBlock.Add(line);
            }

            if (insideCodeBlock)
            {
                currentCodeBlock.Add(line);
            }
        }

        if (isTextFile && currentCodeBlock.Count > 2)
        {
            // Text bodies are wrapped in a single fence; drop it if present.
            if (currentCodeBlock[0].Contains("```", StringComparison.Ordinal))
            {
                currentCodeBlock.RemoveAt(0);
            }

            if (currentCodeBlock.Count > 0 &&
                currentCodeBlock[^1].Contains("```", StringComparison.Ordinal))
            {
                currentCodeBlock.RemoveAt(currentCodeBlock.Count - 1);
            }
        }

        Flush(changes, currentFilePath, currentCodeBlock, isTextFile);

        return changes;
    }

    private static void Flush(
        List<CodeChange> changes,
        string? filePath,
        List<string> codeBlock,
        bool isTextFile)
    {
        if (filePath is null || codeBlock.Count == 0)
        {
            return;
        }

        changes.Add(new CodeChange(filePath, string.Join("\n", codeBlock), isTextFile));
    }

    private static bool IsTextFile(string path) =>
        TextFileExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}

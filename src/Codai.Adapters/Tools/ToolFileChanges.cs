using System.Text;
using Codai.Core.Tools;
using DiffPlex;

namespace Codai.Tools;

internal static class ToolFileChanges
{
    private const int MaxPreviewLines = 200;
    private const int MaxPreviewCharacters = 12000;

    public static ToolFileChange Describe(string path, string before, string after)
    {
        var diff = Differ.Instance.CreateDiffs(before, after, false, false, new FileLines());
        var added = 0;
        var removed = 0;
        var preview = new StringBuilder();
        var previewLines = 0;
        var truncated = false;
        foreach (var block in diff.DiffBlocks)
        {
            added += block.InsertCountB;
            removed += block.DeleteCountA;
            Append($"@@ -{block.DeleteStartA + 1},{block.DeleteCountA} +{block.InsertStartB + 1},{block.InsertCountB} @@");
            foreach (var line in diff.PiecesOld.Skip(block.DeleteStartA).Take(block.DeleteCountA))
            {
                Append("-" + line);
            }
            foreach (var line in diff.PiecesNew.Skip(block.InsertStartB).Take(block.InsertCountB))
            {
                Append("+" + line);
            }
        }
        if (truncated)
        {
            preview.AppendLine("... diff preview truncated");
        }
        return new ToolFileChange(path, added, removed, preview.ToString().TrimEnd('\r', '\n'));

        void Append(string line)
        {
            if (previewLines >= MaxPreviewLines || preview.Length + line.Length > MaxPreviewCharacters)
            {
                truncated = true;
                return;
            }
            preview.AppendLine(line);
            previewLines++;
        }
    }

    private sealed class FileLines : IChunker
    {
        public IReadOnlyList<string> Chunk(string text)
        {
            if (text.Length == 0)
            {
                return [];
            }
            var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            return (normalized.EndsWith('\n') ? normalized[..^1] : normalized).Split('\n');
        }
    }
}

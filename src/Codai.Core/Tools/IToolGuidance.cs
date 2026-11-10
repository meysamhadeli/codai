using System.Text.Json;

namespace Codai.Core.Tools;

/// <summary>Supplies newly applicable guidance before a tool is approved or executed.</summary>
public interface IToolGuidance
{
    /// <summary>Returns guidance requiring model review, or null when execution may continue.</summary>
    string? GetGuidance(ITool tool, JsonElement arguments);

    /// <summary>Loads newly applicable guidance asynchronously before approval or execution.</summary>
    Task<string?> GetGuidanceAsync(ITool tool, JsonElement arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(GetGuidance(tool, arguments));
}

/// <summary>A file tool that exposes its workspace targets for scoped instruction and rule discovery.</summary>
public interface IFileTargetTool : ITool
{
    /// <summary>Returns file targets from validated arguments; structured tools may override the standard path fields.</summary>
    IReadOnlyList<string> GetTargetPaths(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return [];
        }
        if (arguments.TryGetProperty("paths", out var batch))
        {
            if (arguments.TryGetProperty("path", out _) || batch.ValueKind != JsonValueKind.Array ||
                batch.EnumerateArray().Any(entry => entry.ValueKind != JsonValueKind.String))
            {
                return [];
            }
            return batch.EnumerateArray().Select(entry => entry.GetString()!).ToArray();
        }
        return arguments.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String ? [path.GetString()!] : [];
    }
}

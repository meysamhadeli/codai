using System.Text.Json;

namespace Codai.Core.Tools;

/// <summary>Supplies newly applicable guidance before a tool is approved or executed.</summary>
public interface IToolGuidance
{
    /// <summary>Returns guidance requiring model review, or null when execution may continue.</summary>
    string? GetGuidance(ITool tool, JsonElement arguments);
}

/// <summary>A file tool whose targets are expressed through path or paths arguments.</summary>
public interface IFileTargetTool : ITool;

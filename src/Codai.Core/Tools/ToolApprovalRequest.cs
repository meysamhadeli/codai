using System.Text.Json;

namespace Codai.Core.Tools;

/// <summary>
/// A request to run a tool, shown to the user before a tool that can change something
/// is allowed to run.
/// </summary>
/// <param name="ToolName">The tool the model asked for.</param>
/// <param name="Description">That tool's description, so the prompt is self-explanatory.</param>
/// <param name="Summary">The call rendered for display, e.g. <c>run_command(cmd=dotnet test)</c>.</param>
/// <param name="Detail">One line of plain language saying what the call will do.</param>
/// <param name="Preview">
/// The exact text being approved — a command line, a path, or a patch — shown verbatim so the
/// question can be answered without re-reading the summary.
/// </param>
public readonly record struct ToolApprovalRequest(
    string ToolName,
    string Description,
    string Summary,
    string? Detail = null,
    string? Preview = null)
{
    private static readonly string[] PreviewFields = ["command", "path", "paths", "file_path", "patch"];

    /// <summary>Gets the one-line explanation, falling back to the rendered call.</summary>
    public string DetailOrDefault => string.IsNullOrWhiteSpace(Detail) ? Summary : Detail;

    /// <summary>Gets the text shown in the body of the prompt.</summary>
    public string PreviewOrDefault => string.IsNullOrWhiteSpace(Preview) ? Summary : Preview;

    /// <summary>Builds a request from a call, including the text the user is approving.</summary>
    /// <remarks>
    /// The summary exists to label a transcript line, and it is clipped when it is long. A prompt
    /// built from it therefore hides the interesting half of a command exactly when the user needs
    /// to see it most. Reading the arguments instead keeps the approval text complete, and falls
    /// back to the summary when the call carries nothing more specific.
    /// </remarks>
    /// <param name="toolName">The tool the model asked for.</param>
    /// <param name="description">That tool's description.</param>
    /// <param name="summary">The call rendered for display.</param>
    /// <param name="arguments">The arguments the call will run with.</param>
    public static ToolApprovalRequest Create(string toolName, string description, string summary, JsonElement arguments) =>
        new(toolName, description, summary, FirstSentence(description), PreviewOf(arguments));

    private static string? PreviewOf(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var field in PreviewFields)
        {
            if (!arguments.TryGetProperty(field, out var value))
            {
                continue;
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Array => string.Join('\n',
                    value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())),
                _ => value.ToString(),
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? FirstSentence(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var line = description.Split('\n')[0].Trim();
        var stop = line.IndexOf(". ", StringComparison.Ordinal);
        if (stop > 0)
        {
            line = line[..(stop + 1)];
        }

        return line.Length <= 160 ? line : string.Concat(line.AsSpan(0, 157), "...");
    }
}

using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Core.Prompting;

/// <summary>
/// Writes the list of callable tools the way a prompt offers it.
/// </summary>
/// <remarks>
/// Shared by the main prompt and the sub-agent prompt. Two lists that described the same
/// tool differently would teach two assistants two different things about one call, and
/// the required-argument extraction is a small rule that is not worth having twice.
/// </remarks>
public static class ToolCatalogText
{
    /// <summary>One line per tool, in the order they are offered.</summary>
    public static string Lines(IReadOnlyList<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return string.Join('\n', tools.Select(Line));
    }

    /// <summary>
    /// One tool as a single line: its name, its description, and the arguments the model
    /// has to supply. The required list comes from the tool's own schema, so it cannot
    /// drift from what the provider validates against.
    /// </summary>
    public static string Line(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var required = RequiredArguments(tool);

        var arguments = required.Length > 0
            ? $" (required: {string.Join(", ", required)})"
            : string.Empty;

        return $"- {tool.Name}: {tool.Description}{arguments}";
    }

    /// <summary>The names in the schema's <c>required</c> array, or nothing when it has none.</summary>
    private static string[] RequiredArguments(ITool tool)
    {
        var schema = tool.Schema;

        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("required", out var elements) ||
            elements.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. elements
                .EnumerateArray()
                .Select(element => element.GetString())
                .Where(name => name is not null)
                .Select(name => name!),
        ];
    }
}

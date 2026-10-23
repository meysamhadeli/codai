using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools;

internal sealed class SkillsTool(ISkillCatalog catalog) : ToolBase
{
    public override string Name => "skills";

    public override string Description =>
        "Load the full instructions for an available skill when its description matches the task.";

    public override string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "name": {"type": "string", "description": "Skill name from the prompt."}
          },
          "required": ["name"],
          "additionalProperties": false
        }
        """;

    public override Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context,
        CancellationToken cancellationToken = default) =>
        ReadAsync(arguments, context, cancellationToken);

    private async Task<ToolResult> ReadAsync(JsonElement arguments, ToolContext context,
        CancellationToken cancellationToken)
    {
        if (!ToolArguments.TryGetString(arguments, "name", out var name, out var error))
        {
            return ToolResult.Failure(error);
        }

        var skill = await catalog.FindAsync(context.ProjectRoot, name, cancellationToken).ConfigureAwait(false);
        if (skill is null)
        {
            return ToolResult.Failure($"No skill named '{name}'.");
        }

        return ToolResult.Success($"# Skill: {skill.Name}\n\n{skill.Body}\n\n(From {skill.SourcePath}.)");
    }
}
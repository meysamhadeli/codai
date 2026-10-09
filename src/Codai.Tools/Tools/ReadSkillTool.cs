using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// <c>read_skill</c>: fetches the body of one of the project's skills.
/// </summary>
/// <remarks>
/// This is the second half of progressive disclosure. The prompt lists every skill by
/// name and description; a body only enters the conversation when the model decides the
/// skill applies and asks for it. A project with thirty skills therefore costs thirty
/// lines instead of thirty documents, and the model still gets the full instructions for
/// the one skill it actually needs.
/// </remarks>
internal sealed class ReadSkillTool : ToolBase
{
    private readonly ISkillCatalog _catalog;

    /// <summary>Creates the tool.</summary>
    public ReadSkillTool(ISkillCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public override string Name => "read_skill";

    /// <inheritdoc />
    public override string Description =>
        "Read a project skill's instructions. Call this when a skill's description in the " +
        "prompt matches what you are about to do.";

    /// <inheritdoc />
    public override string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "name": {
              "type": "string",
              "description": "Name of the skill, as listed in the prompt."
            }
          },
          "required": ["name"]
        }
        """;

    /// <inheritdoc />
    public override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (!ToolArguments.TryGetString(arguments, "name", out var name, out var error))
        {
            return ToolResult.Failure(error);
        }

        var skill = await _catalog
            .FindAsync(context.ProjectRoot, name, cancellationToken)
            .ConfigureAwait(false);

        if (skill is null)
        {
            var available = await _catalog
                .LoadAsync(context.ProjectRoot, cancellationToken)
                .ConfigureAwait(false);

            var names = available.Count > 0
                ? string.Join(", ", available.Select(entry => entry.Name))
                : "this project defines none";

            return ToolResult.Failure($"No skill named '{name}'. Available skills: {names}.");
        }

        return ToolResult.Success(
            $"# Skill: {skill.Name}\n\n{skill.Body}\n\n(From {skill.SourcePath}.)");
    }
}

using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools;

internal sealed class ReadSkillResourceTool(ISkillCatalog catalog) : ToolBase
{
    public const int MaxResourceChars = 100_000;

    public override string Name => "read_skill_resource";

    public override string Description =>
        "Read a bounded file bundled with a skill, such as a document, template, or script. " +
        "The path is relative to that skill directory.";

    public override string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "name": {"type": "string"},
            "path": {"type": "string", "description": "Relative resource path inside the skill directory."}
          },
          "required": ["name", "path"],
          "additionalProperties": false
        }
        """;

    public override async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (!ToolArguments.TryGetString(arguments, "name", out var name, out var error) ||
            !ToolArguments.TryGetString(arguments, "path", out var resource, out error))
        {
            return ToolResult.Failure(error);
        }

        if (Path.IsPathRooted(resource) || resource.Contains('\0'))
        {
            return ToolResult.Failure("Resource path must be relative to the skill directory.");
        }

        var skill = await catalog.FindAsync(context.ProjectRoot, name, cancellationToken).ConfigureAwait(false);
        if (skill is null)
        {
            return ToolResult.Failure($"No skill named '{name}'.");
        }

        var skillFile = skill.IsGlobal
            ? skill.SourcePath
            : Path.Combine(context.ProjectRoot, skill.SourcePath.Replace('/', Path.DirectorySeparatorChar));
        var skillDirectory = Path.GetFullPath(Path.GetDirectoryName(skillFile)!);
        var fullPath = Path.GetFullPath(resource, skillDirectory);
        if (!IsInside(skillDirectory, fullPath))
        {
            return ToolResult.Failure("Resource path must stay inside the skill directory.");
        }

        if (!context.FileSystem.FileExists(fullPath))
        {
            return ToolResult.Failure($"Skill resource '{resource}' does not exist.");
        }

        if (context.FileSystem.GetFileSize(fullPath) > MaxResourceChars)
        {
            return ToolResult.Failure($"Skill resource exceeds the {MaxResourceChars}-character limit.");
        }

        var content = await context.FileSystem.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return ToolResult.Success($"# Skill resource: {resource}\n\n{content}");
    }

    private static bool IsInside(string root, string path) =>
        string.Equals(root, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
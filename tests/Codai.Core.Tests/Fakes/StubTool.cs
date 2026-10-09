using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Core.Tests.Fakes;

/// <summary>
/// A tool with no behaviour, used where a test needs a list of tools with chosen names
/// and mutability but never calls them.
/// </summary>
internal sealed class StubTool : ITool
{
    public StubTool(
        string name,
        bool isReadOnly = true,
        string description = "A stub tool.",
        string parametersSchema = """{"type":"object","properties":{}}""")
    {
        Name = name;
        IsReadOnly = isReadOnly;
        Description = description;
        ParametersSchema = parametersSchema;
    }

    public string Name { get; }

    public string Description { get; }

    public bool IsReadOnly { get; }

    public string ParametersSchema { get; }

    public JsonElement Schema => JsonSchema.Parse(ParametersSchema);

    public Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolResult.Success("stub"));
}

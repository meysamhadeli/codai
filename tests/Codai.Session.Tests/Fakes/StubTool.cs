using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Session.Tests.Fakes;

/// <summary>
/// A tool declared only by its metadata, so the session harness can build a prompt and a
/// tool set without depending on the real tool package.
/// </summary>
internal sealed class StubTool : ToolBase
{
    /// <summary>Creates a tool.</summary>
    /// <param name="name">Name the model calls.</param>
    /// <param name="isReadOnly">False for a tool that changes something.</param>
    public StubTool(string name, bool isReadOnly = true)
    {
        Name = name;
        IsReadOnly = isReadOnly;
    }

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override string Description => $"Stub {Name}.";

    /// <inheritdoc />
    public override bool IsReadOnly { get; }

    /// <inheritdoc />
    public override string ParametersSchema =>
        """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""";

    /// <inheritdoc />
    public override Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolResult.Success($"{Name} called"));
}

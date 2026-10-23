using System.Text.Json;

namespace Codai.Core.Tools;

/// <summary>
/// Base class for <see cref="ITool"/> implementations. Parses the JSON Schema once and
/// reuses it, so a tool declared with a string constant does not re-parse on every
/// request.
/// </summary>
public abstract class ToolBase : ITool
{
    private JsonElement? _schema;

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public virtual bool IsReadOnly => true;

    /// <inheritdoc />
    public virtual bool RequiresApproval => !IsReadOnly;

    /// <inheritdoc />
    public virtual bool RequiresUserInput => false;

    /// <inheritdoc />
    public abstract string ParametersSchema { get; }

    /// <inheritdoc />
    public JsonElement Schema => _schema ??= JsonSchema.Parse(ParametersSchema);

    /// <inheritdoc />
    public abstract Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default);
}

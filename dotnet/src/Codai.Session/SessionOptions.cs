namespace Codai.Session;

/// <summary>
/// Everything the interactive session needs to know before it starts.
/// </summary>
public sealed class SessionOptions
{
    /// <summary>Directory whose files are summarised and sent to the model.</summary>
    public required string ProjectRoot { get; init; }

    /// <summary>Directory relative file paths are read from when applying changes.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Active provider identifier, e.g. <c>deepseek</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>Active model identifier, e.g. <c>deepseek-flash</c>.</summary>
    public required string Model { get; init; }

    /// <summary>Colour theme used when rendering markdown.</summary>
    public string Theme { get; init; } = "dracula";

    /// <summary>
    /// When true, a response that asks for more project files triggers a second
    /// request that includes those files.
    /// </summary>
    public bool EnableRequestedContext { get; init; } = true;

    /// <summary>Effective directory used to resolve relative paths.</summary>
    public string EffectiveWorkingDirectory => WorkingDirectory ?? ProjectRoot;
}

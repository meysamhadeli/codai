namespace Codai.Session;

/// <summary>
/// Everything the interactive session needs to know before it starts.
/// </summary>
public sealed class SessionOptions
{
    /// <summary>
    /// Directory whose files are summarised, sent to the model, and edited when a
    /// change is accepted. Every relative path in a model response is resolved against
    /// it, so it is also the boundary that keeps edits inside the project.
    /// </summary>
    public required string ProjectRoot { get; init; }

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

    /// <summary>
    /// When true, the stored conversation is loaded before the loop starts, so
    /// <c>codai code --continue</c> picks up where the previous run stopped.
    /// </summary>
    public bool ResumePreviousSession { get; init; }
}

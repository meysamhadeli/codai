namespace Codai.Core.Abstractions;

/// <summary>
/// Names of the pieces a host has to agree on before a sub-agent can be offered.
/// </summary>
public static class SubAgent
{
    /// <summary>
    /// Tool the model calls to start a sub-agent.
    /// </summary>
    /// <remarks>
    /// Declared here rather than where the tool is built because two places need it: the
    /// tool that answers to this name, and the runner that must never offer it to a
    /// sub-agent. Two spellings of the same name would be how runs start nesting.
    /// </remarks>
    public const string ToolName = "task";
}

/// <summary>
/// Runs a nested agent loop: a second assistant with its own context window, its own tool
/// budget and a read-only view of the project, which reports back in a few hundred words.
/// </summary>
/// <remarks>
/// <para>
/// What this buys is the parent's context window. An investigation that reads thirty files
/// and greps a dozen patterns costs the parent a paragraph instead of thirty file bodies,
/// and the parent never has to carry the twenty dead ends.
/// </para>
/// <para>
/// It is an abstraction rather than a class because of layering: the tool that exposes it
/// belongs to the tool package, which knows only the core, while running the loop needs the
/// provider layer. The tool names the capability; the host supplies the loop.
/// </para>
/// </remarks>
public interface ISubAgent
{
    /// <summary>
    /// Runs one investigation and returns what it found.
    /// </summary>
    /// <remarks>
    /// Failures are reported as a failed <see cref="SubAgentResult"/> rather than as an
    /// exception, because the caller is a model that has to decide what to do next and a
    /// message is something it can act on.
    /// </remarks>
    Task<SubAgentResult> RunAsync(
        SubAgentRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>What a caller asks a sub-agent to find out.</summary>
/// <param name="Description">
/// Short label for the investigation, shown to the user while the sub-agent works. It is
/// also the only thing the parent's history keeps about the run if the report is empty.
/// </param>
/// <param name="Prompt">
/// The question to answer, with whatever the caller already knows. A prompt that does not
/// say what to find out is how a sub-agent ends up reporting on everything and nothing.
/// </param>
public sealed record SubAgentRequest(string Description, string Prompt);

/// <summary>What a sub-agent found.</summary>
/// <param name="Succeeded">False when the run produced nothing the caller can use.</param>
/// <param name="Report">
/// The report for the caller, or the reason there is none. A failed report is phrased for
/// the model, so it can decide whether to try another route or answer without it.
/// </param>
public readonly record struct SubAgentResult(bool Succeeded, string Report)
{
    /// <summary>A report the caller can use.</summary>
    public static SubAgentResult Success(string report) => new(true, report);

    /// <summary>A run that produced nothing usable, and why.</summary>
    public static SubAgentResult Failure(string message) => new(false, message);
}

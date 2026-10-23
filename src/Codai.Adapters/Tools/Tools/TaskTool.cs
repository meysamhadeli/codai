using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// <c>task</c>: hands an investigation to a sub-agent that has its own context window.
/// </summary>
/// <remarks>
/// <para>
/// The point is what the caller does <em>not</em> receive. A sub-agent that reads thirty
/// files to answer one question returns a few hundred words, so the file bodies and the
/// dead ends stay in the window that is about to be thrown away instead of accumulating in
/// the one that has to last the session.
/// </para>
/// <para>
/// Three structural properties, and the first is why the tool reports itself as read-only:
/// </para>
/// <list type="bullet">
/// <item>It is read-only, so it is offered in every mode and never prompts. The
/// sub-agent's own tool set is filtered to read-only tools, so delegating cannot change
/// anything regardless of the mode it was called from.</item>
/// <item>It costs a model call, and that call is not free: the usage is added to the
/// session totals so <c>:token</c> stays honest. No approval prompt stands between the
/// user and that cost, which is the same trade every other agent CLI makes for a tool that
/// cannot write.</item>
/// <item>A sub-agent is never offered this tool, so a run cannot nest.</item>
/// </list>
/// </remarks>
internal sealed class TaskTool : ToolBase
{
    private readonly Func<ISubAgent> _subAgent;

    /// <summary>Creates the tool.</summary>
    /// <param name="subAgent">
    /// Resolves the loop when the tool is used, rather than when it is built. The runner
    /// needs the session's tool registry and this tool is part of that registry, so a
    /// direct reference would be a cycle — and the registry is built at startup, when the
    /// tool is only a name and a schema.
    /// </param>
    public TaskTool(Func<ISubAgent> subAgent)
    {
        ArgumentNullException.ThrowIfNull(subAgent);
        _subAgent = subAgent;
    }

    /// <inheritdoc />
    public override string Name => SubAgent.ToolName;

    /// <inheritdoc />
    public override string Description =>
        "Hand an investigation to a sub-agent that has its own context window. Use it when " +
        "answering means reading more of the project than you want to keep in this " +
        "conversation: the sub-agent explores with read-only tools and reports back in a few " +
        "hundred words.";

    /// <inheritdoc />
    public override string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "description": {
              "type": "string",
              "description": "Short label for the investigation, shown to the user, e.g. \"Where is the auth token validated?\"."
            },
            "prompt": {
              "type": "string",
              "description": "What to find out, plus anything you already know that narrows it down. The sub-agent does not see this conversation."
            }
          },
          "required": ["description", "prompt"]
        }
        """;

    /// <inheritdoc />
    public override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (!ToolArguments.TryGetString(arguments, "description", out var description, out var error))
        {
            return ToolResult.Failure(error);
        }

        if (!ToolArguments.TryGetString(arguments, "prompt", out var prompt, out error))
        {
            return ToolResult.Failure(error);
        }

        var result = await _subAgent()
            .RunAsync(new SubAgentRequest(description, prompt), cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded
            ? ToolResult.Success(result.Report)
            : ToolResult.Failure(result.Report);
    }
}

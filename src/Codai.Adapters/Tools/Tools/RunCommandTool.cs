using System.Text;
using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>
/// <c>run_command</c>: runs a command line from the project root.
/// </summary>
/// <remarks>
/// <para>
/// <b>This tool executes arbitrary code with the user's own privileges.</b> It is the
/// most powerful and the least safe capability in Codai, so three things are true of it
/// by construction rather than by policy:
/// </para>
/// <list type="number">
/// <item>It reports <see cref="IsReadOnly"/> as <c>false</c>, so
/// <see cref="ToolRunner"/> always asks the approver before it runs. There is no
/// configuration that turns that off.</item>
/// <item>It is only registered when a host passes <c>allowShell: true</c> to
/// <c>AddCodaiTools</c>. A host that does not opt in does not have the tool, so no
/// prompt can offer it.</item>
/// <item>The working directory is always the project root, never the process's, so a
/// command starts in the project; this is not a filesystem sandbox.</item>
/// </list>
/// <para>
/// There is no sandbox and no command allow-list. Anything that looks like one would be
/// defeated by <c>sh -c</c> anyway, and a false sense of safety is worse than a clear
/// boundary: the boundary is the user's approval.
/// </para>
/// </remarks>
internal sealed class RunCommandTool : ToolBase
{
    /// <summary>Timeout applied when the model does not ask for one.</summary>
    public const int DefaultTimeoutSeconds = 120;

    /// <summary>Longest timeout the model may ask for.</summary>
    public const int MaxTimeoutSeconds = 600;

    private readonly IProcessRunner _runner;

    /// <summary>Creates the tool.</summary>
    public RunCommandTool(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public override string Name => "run_command";

    /// <inheritdoc />
    public override string Description =>
        "Run a command line from the project root and return its output. Use it to build, " +
        "test or check something; long-running commands should be given a timeout.";

    /// <inheritdoc />
    public override bool IsReadOnly => false;

    /// <inheritdoc />
    public override string ParametersSchema =>
        $$"""
        {
          "type": "object",
          "properties": {
            "command": {
              "type": "string",
              "description": "The command line to run, e.g. \"dotnet build\" or \"npm test\"."
            },
            "timeout_seconds": {
              "type": "integer",
              "description": "Kill the command after this long. Defaults to {{DefaultTimeoutSeconds}}, maximum {{MaxTimeoutSeconds}}."
            }
          },
          "required": ["command"]
        }
        """;

    /// <inheritdoc />
    public override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (!ToolArguments.TryGetString(arguments, "command", out var command, out var error))
        {
            return ToolResult.Failure(error);
        }

        var seconds = ToolArguments.GetInt(
            arguments,
            "timeout_seconds",
            DefaultTimeoutSeconds,
            min: 1);

        seconds = Math.Min(seconds, MaxTimeoutSeconds);

        var result = await _runner
            .RunStreamingAsync(command, context.ProjectRoot, TimeSpan.FromSeconds(seconds),
                output => context.Observer.OnToolOutput(Name, output), cancellationToken)
            .ConfigureAwait(false);

        return ToolResult.Success(Render(command, seconds, result));
    }

    /// <summary>
    /// Renders the outcome. The exit code is always stated first, because a command can
    /// print a lot of reassuring text and still have failed, and a model that reads only
    /// the output will believe it succeeded.
    /// </summary>
    private static string Render(string command, int seconds, ProcessResult result)
    {
        var builder = new StringBuilder()
            .Append("$ ").AppendLine(command)
            .AppendLine();

        if (result.TimedOut)
        {
            builder.Append("Killed after ").Append(seconds).AppendLine(" second(s) without finishing.");
        }
        else
        {
            builder.Append("Exit code ").Append(result.ExitCode)
                .AppendLine(result.ExitCode == 0 ? " (success)." : " (failure).");
        }

        if (result.StandardOutput.Length > 0)
        {
            builder.AppendLine()
                .AppendLine("stdout:")
                .AppendLine(result.StandardOutput.TrimEnd());
        }

        if (result.StandardError.Length > 0)
        {
            builder.AppendLine()
                .AppendLine("stderr:")
                .AppendLine(result.StandardError.TrimEnd());
        }

        if (result.StandardOutput.Length == 0 && result.StandardError.Length == 0)
        {
            builder.AppendLine().AppendLine("(no output)");
        }

        return builder.ToString().TrimEnd();
    }
}

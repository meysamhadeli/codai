using System.Text;
using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools;

/// <summary>Shared execution and rendering for explicit .NET workflow tools.</summary>
public abstract class DotnetWorkflowToolBase : ToolBase
{
    protected const int DefaultTimeoutSeconds = 600;
    private const int MaxOutputCharacters = 80_000;

    private readonly IProcessRunner _runner;

    protected DotnetWorkflowToolBase(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public sealed override bool IsReadOnly => false;

    public sealed override string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "timeout_seconds": {
              "type": "integer",
              "description": "Maximum duration in seconds. Defaults to 600."
            }
          },
          "additionalProperties": false
        }
        """;

    public sealed override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        var timeoutSeconds = Math.Min(
            ToolArguments.GetInt(arguments, "timeout_seconds", DefaultTimeoutSeconds, min: 1),
            DefaultTimeoutSeconds);

        var result = await _runner
            .RunStreamingAsync(CommandLine, context.ProjectRoot, TimeSpan.FromSeconds(timeoutSeconds),
                output => context.Observer.OnToolOutput(Name, output), cancellationToken)
            .ConfigureAwait(false);

        if (result.TimedOut)
        {
            return ToolResult.Failure($"{Name} timed out after {timeoutSeconds} seconds.");
        }

        var output = Render(result);
        return result.ExitCode == 0
            ? ToolResult.Success(output)
            : ToolResult.Failure(output);
    }

    protected abstract string CommandLine { get; }

    private static string Render(ProcessResult result)
    {
        var builder = new StringBuilder();
        if (result.StandardOutput.Length > 0)
        {
            builder.Append(result.StandardOutput.TrimEnd());
        }

        if (result.StandardError.Length > 0)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(result.StandardError.TrimEnd());
        }

        if (builder.Length == 0)
        {
            builder.Append("(no output)");
        }

        var text = builder.ToString();
        return text.Length <= MaxOutputCharacters
            ? text
            : text[..MaxOutputCharacters] + "\n(truncated)";
    }
}

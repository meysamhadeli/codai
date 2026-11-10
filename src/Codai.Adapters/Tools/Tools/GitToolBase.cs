using System.Text;
using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools;

public abstract class GitToolBase : ToolBase
{
    protected const int DefaultTimeoutSeconds = 30;
    protected const int MaxOutputCharacters = 60_000;

    private readonly IProcessRunner _runner;

    protected GitToolBase(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public override string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public sealed override async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCommandLine(arguments, out var commandLine, out var error))
        {
            return ToolResult.Failure(error);
        }
        var result = await _runner
            .RunAsync(
                commandLine,
                context.ProjectRoot,
                TimeSpan.FromSeconds(DefaultTimeoutSeconds),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.TimedOut)
        {
            return ToolResult.Failure($"Git command timed out after {DefaultTimeoutSeconds} seconds.");
        }

        var output = Render(result);
        return result.ExitCode == 0
            ? ToolResult.Success(output)
            : ToolResult.Failure(output);
    }

    protected abstract string CommandLine { get; }

    /// <summary>Validates tool arguments before constructing a read-only Git command.</summary>
    protected virtual bool TryGetCommandLine(JsonElement arguments, out string commandLine, out string failureReason)
    {
        commandLine = CommandLine;
        failureReason = string.Empty;
        return true;
    }

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

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

    public sealed override string ParametersSchema =>
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
        var result = await _runner
            .RunAsync(
                CommandLine,
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
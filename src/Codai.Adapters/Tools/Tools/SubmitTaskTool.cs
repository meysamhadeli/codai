using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Tools;

internal sealed class SubmitTaskTool : ToolBase
{
    public const int MaxResultChars = ToolRunner.DefaultMaxResultChars;

    public override string Name => "submit_task";

    public override string Description =>
        "Submit the final result of this request after necessary work and checks are complete. " +
        "Include outcomes and any unresolved limitations. This ends the model/tool loop; " +
        "it does not approve pending edits or prove that external checks passed.";

    public override bool IsReadOnly => true;

    public override string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "result": {
              "type": "string",
              "description": "Final response to the user, including outcomes and unresolved limitations."
            }
          },
          "required": ["result"],
          "additionalProperties": false
        }
        """;

    public override Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ToolArguments.TryGetString(arguments, "result", out var result, out var error))
        {
            return Task.FromResult(ToolResult.Failure(error));
        }

        if (string.IsNullOrWhiteSpace(result) || result.Length > MaxResultChars)
        {
            return Task.FromResult(ToolResult.Failure($"Final result must contain 1-{MaxResultChars} characters of non-empty text."));
        }

        return Task.FromResult(ToolResult.Success(result) with { StopsExecution = true });
    }
}

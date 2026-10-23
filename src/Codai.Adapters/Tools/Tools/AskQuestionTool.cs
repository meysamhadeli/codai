using System.Text.Json;
using Codai.Core.Abstractions;
using Codai.Core.Tools;

namespace Codai.Tools;

internal sealed class AskQuestionTool(IClarificationHandler handler) : ToolBase
{
    public const int MaxQuestionChars = 2_000;
    public const int MaxOptions = 5;
    public const int MaxOptionChars = 200;

    public override string Name => "ask_question";
    public override string Description =>
        "Ask the user for missing information needed to proceed. Provide a concise question " +
        "and optional choices; free-form answers are allowed. Never treat an answer as permission " +
        "to write or execute. Runs without user input stop rather than assume an answer.";
    public override bool RequiresUserInput => true;
    public override string ParametersSchema =>
        """
        {
          "type": "object",
          "properties": {
            "question": {"type": "string", "maxLength": 2000},
            "options": {"type": "array", "maxItems": 5, "items": {"type": "string", "maxLength": 200}}
          },
          "required": ["question"],
          "additionalProperties": false
        }
        """;

    public override async Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!ToolArguments.TryGetString(arguments, "question", out var question, out var error))
        {
            return ToolResult.Failure(error);
        }

        if (string.IsNullOrWhiteSpace(question) || question.Length > MaxQuestionChars)
        {
            return ToolResult.Failure($"Question must contain 1-{MaxQuestionChars} characters of non-empty text.");
        }

        var options = new List<string>();
        if (arguments.TryGetProperty("options", out var choices))
        {
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() > MaxOptions)
            {
                return ToolResult.Failure($"Options must be an array of at most {MaxOptions} choices.");
            }

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(choice.GetString()) ||
                    choice.GetString()!.Length > MaxOptionChars)
                {
                    return ToolResult.Failure($"Each option must contain 1-{MaxOptionChars} characters of non-empty text.");
                }

                options.Add(choice.GetString()!);
            }
        }

        string? answer;
        try
        {
            answer = await handler.AskAsync(question, options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Clarification required: {question}. Input handler failed: {ex.Message}")
                with
            { StopsExecution = true };
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(answer) || answer.Length > ToolRunner.DefaultMaxResultChars)
        {
            return ToolResult.Failure($"Clarification required: {question}. No usable answer received; " +
                "provide the missing information in a new request.") with
            { StopsExecution = true };
        }

        return ToolResult.Success(answer);
    }
}

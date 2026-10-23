using System.Text.Json;
using Codai.Core.Tools;

namespace Codai.Providers.Tests.Fakes;

/// <summary>An <see cref="ITool"/> declared only by its metadata.</summary>
internal sealed class StubTool : ToolBase
{
    /// <summary>A schema that uses keywords Gemini does not model, to exercise filtering.</summary>
    public const string RichSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "title": "grep arguments",
          "type": "object",
          "properties": {
            "pattern": { "type": "string", "description": "Regular expression to search for." },
            "case_sensitive": { "type": "boolean", "default": false }
          },
          "required": ["pattern"],
          "additionalProperties": false
        }
        """;

    private readonly string? _schema;

    public StubTool(string name, bool isReadOnly = true, string? schema = null)
    {
        Name = name;
        IsReadOnly = isReadOnly;
        _schema = schema;
    }

    public override string Name { get; }

    public override string Description => $"Stub {Name}.";

    public override bool IsReadOnly { get; }

    public ToolResult Outcome { get; set; } = ToolResult.Success("stub");

    public int Invocations { get; private set; }

    public override string ParametersSchema =>
        _schema ?? """{"type":"object","properties":{"pattern":{"type":"string"}},"required":["pattern"]}""";

    public override Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Invocations++;
        return Task.FromResult(Outcome);
    }
}

/// <summary>One tool call the model asked for.</summary>
internal readonly record struct RecordedToolCall(string Name, JsonElement Arguments);

/// <summary>Records the tool calls a chat client makes on the model's behalf.</summary>
internal sealed class RecordingToolExecutor : IToolExecutor
{
    public RecordingToolExecutor(params ITool[] tools) => Tools = tools;

    public IReadOnlyList<ITool> Tools { get; }

    public List<RecordedToolCall> Calls { get; } = [];

    public ToolResult Outcome { get; set; } = ToolResult.Success("tool output");

    public Task<ToolResult> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(new RecordedToolCall(toolName, arguments));

        return Task.FromResult(Outcome);
    }
}

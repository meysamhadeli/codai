using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codai.Cli.Ui;

internal sealed record CliJsonEvent(string Type)
{
    public DateTimeOffset Ts { get; init; } = DateTimeOffset.UtcNow;
    public CliJsonAgentEvent? Event { get; init; }
    public string? Message { get; init; }
    public string? Text { get; init; }
    public string? FinishReason { get; init; }
    public string? Reason { get; init; }
    public string? Model { get; init; }
    public int? ExitCode { get; init; }
    public int? Iterations { get; init; }
    public double? DurationMs { get; init; }
    public CliJsonUsage? Usage { get; init; }
}

internal sealed record CliJsonAgentEvent(string Type)
{
    public string? Kind { get; init; }
    public string? TaskId { get; init; }
    public string? Description { get; init; }
    public string? Text { get; init; }
    public string? Reason { get; init; }
    public string? Model { get; init; }
    public CliJsonUsage? Usage { get; init; }
}

internal sealed record CliJsonUsage(long InputTokens, long OutputTokens, decimal Cost);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CliJsonEvent))]
internal sealed partial class CliJsonContext : JsonSerializerContext;

internal sealed class JsonDiagnosticOutput(IConsoleOutput inner) : IConsoleOutput
{
    public void Write(string message) => inner.Write(message);

    public void WriteLine(string message) => inner.WriteLine(message);

    public void WriteError(string message) => inner.WriteError(
        JsonSerializer.Serialize(new CliJsonEvent("error") { Message = message }, CliJsonContext.Default.CliJsonEvent));
}

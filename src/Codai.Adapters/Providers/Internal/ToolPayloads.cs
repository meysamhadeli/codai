using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Codai.Providers.Internal;

/// <summary>
/// Helpers for the hand-written clients, which have to serialise tools and messages
/// themselves because their wire formats are not OpenAI-compatible.
/// </summary>
internal static class ToolPayloads
{
    /// <summary>
    /// The function declarations worth advertising. Non-function tools (hosted tools and
    /// the like) have no wire representation here and are dropped.
    /// </summary>
    public static IReadOnlyList<AIFunction> Functions(IList<AITool>? tools) =>
        tools is null or { Count: 0 } ? [] : [.. tools.OfType<AIFunction>()];

    /// <summary>
    /// The arguments a call was made with, as an object. Both wire formats require an
    /// object, so a call made with no arguments becomes an empty one rather than null.
    /// </summary>
    public static IDictionary<string, object?> ArgumentsOrDefault(FunctionCallContent call) =>
        call.Arguments is { Count: > 0 } arguments
            ? arguments
            : new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// Converts a parsed argument value into the natural CLR value a tool expects, so
    /// consumers do not have to unwrap <see cref="JsonElement"/> themselves.
    /// </summary>
    /// <remarks>
    /// Written out rather than delegated to <c>Deserialize&lt;object&gt;</c>, which returns
    /// a <see cref="JsonElement"/> for every value and so would defeat the purpose.
    /// </remarks>
    public static object? ToClrValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(ToClrValue).ToArray(),
        JsonValueKind.Object => value
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => ToClrValue(property.Value),
                StringComparer.Ordinal),
        _ => null,
    };

    /// <summary>Collects text and tool calls from a set of content items in order.</summary>
    public static IEnumerable<AIContent> AsContents(IEnumerable<AIContent> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);

        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } text:
                    yield return text;
                    break;

                case FunctionCallContent call:
                    yield return call;
                    break;
            }
        }
    }
}

/// <summary>
/// Converts JSON Schema into the subset Gemini's <c>Schema</c> message accepts.
/// </summary>
/// <remarks>
/// Gemini rejects keywords it does not model — <c>additionalProperties</c> in
/// particular, which our tool schemas use — so the schema has to be filtered before it
/// is sent.
/// </remarks>
internal static class GeminiSchema
{
    private static readonly HashSet<string> Unsupported = new(StringComparer.Ordinal)
    {
        "additionalProperties",
        "unevaluatedProperties",
        "patternProperties",
        "$schema",
        "$id",
        "$ref",
        "$defs",
        "definitions",
        "$comment",
        "title",
        "default",
        "examples",
        "const",
        "if",
        "then",
        "else",
        "allOf",
        "anyOf",
        "oneOf",
        "not",
    };

    /// <summary>Returns a copy of <paramref name="schema"/> with unsupported keywords removed.</summary>
    public static JsonElement Sanitize(JsonElement schema)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, schema);
        }

        using var document = JsonDocument.Parse(buffer.ToArray());
        return document.RootElement.Clone();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject())
                {
                    if (Unsupported.Contains(property.Name))
                    {
                        continue;
                    }

                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }
}

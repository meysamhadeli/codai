using System.Text.Json;

namespace Codai.Core.Tools;

/// <summary>
/// Reads a JSON Schema literal into a <see cref="JsonElement"/> that survives disposal
/// of the parser.
/// </summary>
/// <remarks>
/// A <see cref="JsonElement"/> obtained from <see cref="JsonDocument.Parse(string, JsonDocumentOptions)"/>
/// is only valid while that document is alive. Returning the element from a property
/// would hand out a handle to a disposed document, which throws on first use. Cloning
/// detaches it from the backing document.
/// </remarks>
public static class JsonSchema
{
    /// <summary>Parses <paramref name="json"/> into a detached element.</summary>
    public static JsonElement Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

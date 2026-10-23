using System.Text.Json;

namespace Codai.Core.Tools;

/// <summary>
/// Helpers for reading a tool's arguments.
/// </summary>
/// <remarks>
/// Arguments come from a model, so every read is defensive: a missing or wrongly typed
/// property produces a message the model can act on, never an exception.
/// </remarks>
public static class ToolArguments
{
    /// <summary>Reads a required string property.</summary>
    public static bool TryGetString(
        JsonElement arguments,
        string name,
        out string value,
        out string error)
    {
        value = string.Empty;
        error = string.Empty;

        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var property))
        {
            error = $"Missing required argument '{name}'.";
            return false;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = $"Argument '{name}' must be a string.";
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            error = $"Argument '{name}' must not be empty.";
            return false;
        }

        value = text;
        return true;
    }

    /// <summary>Reads an optional string property, returning <paramref name="fallback"/> when absent.</summary>
    public static string GetString(JsonElement arguments, string name, string fallback = "")
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        return property.GetString() ?? fallback;
    }

    /// <summary>Reads an optional integer property, clamped to <paramref name="min"/>.</summary>
    public static int GetInt(JsonElement arguments, string name, int fallback, int min = 0)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value))
        {
            return fallback;
        }

        return Math.Max(value, min);
    }

    /// <summary>Reads an optional boolean property.</summary>
    public static bool GetBool(JsonElement arguments, string name, bool fallback = false)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }
}

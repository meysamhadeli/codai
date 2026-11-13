using System.Text;

namespace Codai.Cli.Ui;

/// <summary>Normalizes terminal paste payloads for single-line dialog fields.</summary>
internal static class OverlayPaste
{
    /// <summary>Shared with the typed-input guards in dialog fields.</summary>
    internal const int MaxValueLength = 4096;

    /// <summary>
    /// Drops control characters from a paste payload. A value field submits on
    /// Enter and rejects control characters at validation, so pasted line breaks
    /// and other controls cannot be kept literally.
    /// </summary>
    internal static string StripControlCharacters(string text)
    {
        if (!text.Any(char.IsControl))
        {
            return text;
        }
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    /// <summary>Clamps a value to the remaining field capacity without splitting a surrogate pair.</summary>
    internal static string Clamp(string value, int remaining)
    {
        if (value.Length <= remaining)
        {
            return value;
        }
        value = value[..remaining];
        return char.IsHighSurrogate(value[^1]) ? value[..^1] : value;
    }

    /// <summary>
    /// Sanitizes a paste payload and appends it to the field, honoring the shared
    /// length limit. Returns false when nothing was inserted.
    /// </summary>
    internal static bool TryAppend(ComposerBuffer editor, string text)
    {
        var sanitized = StripControlCharacters(text);
        var remaining = MaxValueLength - editor.Text.Length;
        if (sanitized.Length == 0 || remaining <= 0)
        {
            return false;
        }
        editor.Insert(Clamp(sanitized, remaining));
        return true;
    }
}

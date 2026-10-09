using System.Text;

namespace Codai.Core.Customization;

/// <summary>
/// Turns a command's body and the arguments the user typed into the prompt that is sent.
/// </summary>
public static class CommandExpander
{
    /// <summary>Placeholder replaced with everything the user typed after the command.</summary>
    public const string ArgumentsPlaceholder = "$ARGUMENTS";

    /// <summary>
    /// Expands <paramref name="definition"/> with <paramref name="arguments"/>.
    /// </summary>
    /// <remarks>
    /// <c>$ARGUMENTS</c> receives everything verbatim; <c>$1</c> to <c>$9</c> receive the
    /// whitespace-separated words. A body that uses neither has the arguments appended
    /// instead, so <c>:fix src/a.cs</c> does not silently lose "src/a.cs".
    /// </remarks>
    public static string Expand(CommandDefinition definition, string? arguments)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var body = definition.Body;
        var rest = (arguments ?? string.Empty).Trim();

        var positional = rest.Length == 0
            ? []
            : rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var usedPlaceholder = false;

        var builder = new StringBuilder(body.Length + rest.Length + 8);
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '$')
            {
                builder.Append(body[i]);
                continue;
            }

            if (Matches(body, i, ArgumentsPlaceholder))
            {
                builder.Append(rest);

                // The loop's own increment takes the last character, so step back one
                // from the placeholder's length.
                i += ArgumentsPlaceholder.Length - 1;
                usedPlaceholder = true;
                continue;
            }

            if (i + 1 < body.Length && body[i + 1] is >= '1' and <= '9')
            {
                var index = body[i + 1] - '1';
                builder.Append(index < positional.Length ? positional[index] : string.Empty);
                i++;
                usedPlaceholder = true;
                continue;
            }

            builder.Append('$');
        }

        if (!usedPlaceholder && rest.Length > 0)
        {
            builder.Append("\n\n").Append(rest);
        }

        return builder.ToString().Trim();
    }

    private static bool Matches(string body, int start, string value) =>
        start + value.Length <= body.Length &&
        body.AsSpan(start, value.Length).Equals(value, StringComparison.OrdinalIgnoreCase);
}

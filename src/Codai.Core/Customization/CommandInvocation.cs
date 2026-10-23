namespace Codai.Core.Customization;

/// <summary>
/// A user command or skill invocation, split into its name and arguments.
/// </summary>
/// <param name="Name">Command name without the leading colon, lower-cased.</param>
/// <param name="Arguments">Everything after the command name, trimmed.</param>
public sealed record CommandInvocation(string Name, string Arguments)
{
    /// <summary>
    /// Parses a <c>:name arguments</c> or <c>/name arguments</c> line.
    /// </summary>
    /// <returns>
    /// False when the input is not a command-shaped line, which is the case for blank
    /// input, for input that does not start with a colon, and for a bare <c>:</c>.
    /// </returns>
    /// <remarks>
    /// This only recognises the shape. Whether the name is a real command is the
    /// catalog's business, so a typo can be reported rather than passed to the model as
    /// if it were a request.
    /// </remarks>
    public static bool TryParse(string? input, out CommandInvocation invocation)
    {
        invocation = null!;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.TrimStart();

        if (trimmed.Length < 2 || trimmed[0] is not (':' or '/'))
        {
            return false;
        }

        if (trimmed[1] is ':' or '=' or '/')
        {
            // Not a command line at all, e.g. a "::" separator or a ":=" operator.
            return false;
        }

        var body = trimmed[1..];
        var separator = body.IndexOfAny([' ', '\t']);

        var name = (separator < 0 ? body : body[..separator]).Trim().ToLowerInvariant();
        var arguments = separator < 0 ? string.Empty : body[(separator + 1)..].Trim();

        if (name.Length == 0)
        {
            return false;
        }

        invocation = new CommandInvocation(name, arguments);
        return true;
    }
}

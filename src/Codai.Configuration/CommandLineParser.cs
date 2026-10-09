using Codai.Core.Agents;

namespace Codai.Configuration;

/// <summary>
/// Parses the CLI flags that can override configuration values.
/// </summary>
/// <remarks>
/// Supports both <c>--key value</c> and <c>--key=value</c> forms, plus the short
/// aliases <c>-c</c> (config) and <c>-v</c> (version). Unknown flags are recorded
/// as errors rather than silently ignored.
/// </remarks>
public static class CommandLineParser
{
    /// <summary>
    /// Canonical flag spellings, paired with the configuration key each one overrides.
    /// </summary>
    private static readonly (string Canonical, string ConfigKey)[] KnownFlags =
    [
        ("theme", ConfigKeys.Theme),
        ("provider", ConfigKeys.Provider),
        ("base-url", ConfigKeys.BaseUrl),
        ("model", ConfigKeys.Model),
        ("temperature", ConfigKeys.Temperature),
        ("reasoning-effort", ConfigKeys.ReasoningEffort),
        ("api-key", ConfigKeys.ApiKey),
        ("api-version", ConfigKeys.ApiVersion),
        ("max-tokens", ConfigKeys.MaxTokens),
        ("stream", ConfigKeys.Stream),
    ];

    /// <summary>
    /// Flag name to configuration key. The names are the kebab-case ones;
    /// <see cref="SplitFlag"/> normalises every other accepted spelling into them.
    /// </summary>
    private static readonly Dictionary<string, string> FlagToConfigKey = BuildFlagMap();

    public static CommandLineParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            return CommandLineParseResult.Empty;
        }

        string? configPath = null;
        var showVersion = false;
        var continueSession = false;
        var mode = AgentModeParser.Default;
        var allowShell = true;
        var autoCompact = true;
        var allowSubAgents = true;
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];

            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            // Skip the command name (e.g. "code") and other positional arguments:
            // only flags participate in configuration.
            if (!token.StartsWith('-'))
            {
                continue;
            }

            var (name, inlineValue) = SplitFlag(token);
            var hasInlineValue = inlineValue is not null;

            switch (name)
            {
                case "config":
                case "c":
                    configPath = hasInlineValue ? inlineValue : TakeValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(configPath))
                    {
                        errors.Add("Option '--config' requires a file path.");
                    }

                    continue;

                case "version":
                case "v":
                    showVersion = true;
                    continue;

                case "continue":
                    continueSession = true;
                    continue;

                // Switches with an off form rather than a value: there is one sensible
                // default, so the flag only ever has to turn the feature off.
                case "no-shell":
                    allowShell = false;
                    continue;

                case "allow-shell":
                    allowShell = true;
                    continue;

                case "no-auto-compact":
                    autoCompact = false;
                    continue;

                case "auto-compact":
                    autoCompact = true;
                    continue;

                case "no-sub-agents":
                    allowSubAgents = false;
                    continue;

                case "sub-agents":
                    allowSubAgents = true;
                    continue;

                case "mode":
                    // Not a configuration key: the mode belongs to the session, not to
                    // the provider settings, so it never reaches the config loader.
                    var modeName = hasInlineValue ? inlineValue : TakeValue(args, ref i);

                    if (string.IsNullOrWhiteSpace(modeName))
                    {
                        errors.Add("Option '--mode' requires a value.");
                        continue;
                    }

                    if (!AgentModeParser.TryParse(modeName, out var parsedMode))
                    {
                        errors.Add($"Unknown mode '{modeName}'. Use one of: {AgentModeParser.Names}.");
                        continue;
                    }

                    mode = parsedMode;
                    continue;
            }

            if (!FlagToConfigKey.TryGetValue(name, out var configKey))
            {
                errors.Add($"Unknown option '{token}'.");
                continue;
            }

            var value = hasInlineValue ? inlineValue : TakeValue(args, ref i);
            if (value is null)
            {
                errors.Add($"Option '--{name}' requires a value.");
                continue;
            }

            overrides[configKey] = value;
        }

        return new CommandLineParseResult(
            configPath,
            showVersion,
            continueSession,
            mode,
            allowShell,
            autoCompact,
            allowSubAgents,
            overrides,
            errors);
    }

    private static Dictionary<string, string> BuildFlagMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (canonical, configKey) in KnownFlags)
        {
            map[canonical] = configKey;
        }

        return map;
    }

    /// <summary>
    /// Splits <c>--name=value</c> or <c>-name</c> into a name and an optional value,
    /// with underscores written as dashes.
    /// </summary>
    /// <remarks>
    /// Normalising here rather than registering every spelling twice means one rule
    /// covers every flag, including the ones added after the original Go CLI: the
    /// snake_case spellings it used keep working, and a new switch only has to be
    /// written once.
    /// </remarks>
    private static (string Name, string? Value) SplitFlag(string token)
    {
        var stripped = token.StartsWith("--", StringComparison.Ordinal)
            ? token[2..]
            : token[1..];

        var separatorIndex = stripped.IndexOf('=');
        var name = (separatorIndex >= 0 ? stripped[..separatorIndex] : stripped).Replace('_', '-');
        var value = separatorIndex >= 0 ? stripped[(separatorIndex + 1)..] : null;

        return (name, value);
    }

    private static string? TakeValue(IReadOnlyList<string> args, ref int index)
    {
        if (index + 1 >= args.Count)
        {
            return null;
        }

        var next = args[index + 1];
        if (next.StartsWith('-') && next.Length > 1 && !char.IsDigit(next[1]))
        {
            return null;
        }

        index++;
        return next;
    }
}

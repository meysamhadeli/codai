using System.Globalization;
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
        ("retries", ConfigKeys.MaxConsecutiveToolFailures),
        ("plan-provider", ConfigKeys.PlanProvider),
        ("plan-model", ConfigKeys.PlanModel),
        ("agent-provider", ConfigKeys.AgentProvider),
        ("agent-model", ConfigKeys.AgentModel),
        ("checkpoint-retention", ConfigKeys.CheckpointRetention),
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
        string? resumeTaskId = null;
        var mode = AgentModeParser.Default;
        var allowShell = true;
        var autoCompact = true;
        var compactionStrategy = Codai.Core.Chat.CompactionStrategy.Agentic;
        var allowSubAgents = true;
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var promptParts = new List<string>();
        string? workingDirectory = null;
        var literalArguments = false;
        var autoApprove = false;
        var runTimeout = TimeSpan.Zero;
        string? systemPrompt = null;
        var jsonOutput = false;
        var acp = false;
        string? dataDirectory = null;
        string? configurationDirectory = null;
        string? hooksDirectory = null;
        var disableHooks = false;
        var executePlan = false;
        var disabledTools = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];

            if (token is null)
            {
                continue;
            }

            if (!literalArguments && token == "--")
            {
                literalArguments = true;
                continue;
            }

            if (literalArguments || !token.StartsWith('-'))
            {
                if (literalArguments || i != 0 || !token.Equals("code", StringComparison.OrdinalIgnoreCase))
                {
                    promptParts.Add(token);
                }

                continue;
            }

            var (name, inlineValue) = SplitFlag(token);
            var hasInlineValue = inlineValue is not null;

            switch (name)
            {
                case "disable-tool":
                    var disabledTool = hasInlineValue ? inlineValue : TakeValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(disabledTool))
                    {
                        errors.Add("Option '--disable-tool' requires a registered tool name.");
                    }
                    else
                    {
                        disabledTools.Add(disabledTool.Trim());
                    }

                    continue;

                case "execute-plan":
                    if (hasInlineValue)
                    {
                        errors.Add("Option '--execute-plan' does not accept a value.");
                    }

                    executePlan = true;
                    continue;

                case "hooks-dir":
                    hooksDirectory = hasInlineValue ? inlineValue : TakeValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(hooksDirectory))
                    {
                        errors.Add("Option '--hooks-dir' requires a directory path.");
                    }

                    continue;

                case "no-hooks":
                    if (hasInlineValue)
                    {
                        errors.Add("Option '--no-hooks' does not accept a value.");
                    }

                    disableHooks = true;
                    continue;

                case "data-dir":
                    dataDirectory = hasInlineValue ? inlineValue : TakeValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(dataDirectory))
                    {
                        errors.Add("Option '--data-dir' requires a directory path.");
                    }

                    continue;

                case "config-dir":
                    configurationDirectory = hasInlineValue ? inlineValue : TakeValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(configurationDirectory))
                    {
                        errors.Add("Option '--config-dir' requires a directory path.");
                    }

                    continue;

                case "json":
                    if (hasInlineValue)
                    {
                        errors.Add("Option '--json' does not accept a value.");
                    }

                    jsonOutput = true;
                    continue;

                case "acp":
                    if (hasInlineValue)
                    {
                        errors.Add("Option '--acp' does not accept a value.");
                    }

                    acp = true;
                    continue;

                case "system":
                case "s":
                    systemPrompt = hasInlineValue ? inlineValue : TakeValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(systemPrompt))
                    {
                        errors.Add("Option '--system' requires a non-empty prompt.");
                    }

                    continue;

                case "timeout":
                case "t":
                    var timeoutValue = hasInlineValue ? inlineValue : TakeValue(args, ref i);
                    if (!double.TryParse(timeoutValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                        !double.IsFinite(seconds) || seconds < 0 || seconds > (uint.MaxValue - 1) / 1000d)
                    {
                        errors.Add("Option '--timeout' requires non-negative seconds, at most 4294967.294 (0 disables timeout).");
                    }
                    else
                    {
                        runTimeout = TimeSpan.FromSeconds(seconds);
                    }

                    continue;

                case "auto-approve":
                    var approvalValue = hasInlineValue ? inlineValue : TakeValue(args, ref i);
                    if (!bool.TryParse(approvalValue, out autoApprove))
                    {
                        errors.Add("Option '--auto-approve' requires true or false.");
                    }

                    continue;

                case "cwd":
                    workingDirectory = hasInlineValue ? inlineValue : TakeValue(args, ref i);
                    if (string.IsNullOrWhiteSpace(workingDirectory))
                    {
                        errors.Add("Option '--cwd' requires a directory path.");
                    }

                    continue;

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
                    resumeTaskId = hasInlineValue ? inlineValue : null;
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

                case "no-checkpoints":
                    overrides[ConfigKeys.CheckpointsEnabled] = "false";
                    continue;

                case "compaction":
                    var compaction = hasInlineValue ? inlineValue : TakeValue(args, ref i);
                    switch (compaction?.ToLowerInvariant())
                    {
                        case "agentic":
                            compactionStrategy = Codai.Core.Chat.CompactionStrategy.Agentic;
                            autoCompact = true;
                            break;
                        case "basic":
                            compactionStrategy = Codai.Core.Chat.CompactionStrategy.Basic;
                            autoCompact = true;
                            break;
                        case "off":
                            autoCompact = false;
                            break;
                        default:
                            errors.Add("Option '--compaction' requires agentic, basic, or off.");
                            break;
                    }

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
            errors)
        {
            ResumeTaskId = resumeTaskId,
            Prompt = promptParts.Count == 0 ? null : string.Join(" ", promptParts),
            WorkingDirectory = workingDirectory,
            AutoApprove = autoApprove,
            RunTimeout = runTimeout,
            SystemPrompt = systemPrompt,
            JsonOutput = jsonOutput,
            Acp = acp,
            DataDirectory = dataDirectory,
            ConfigurationDirectory = configurationDirectory,
            HooksDirectory = hooksDirectory,
            DisableHooks = disableHooks,
            ExecutePlan = executePlan,
            DisabledTools = disabledTools,
            CompactionStrategy = compactionStrategy,
        };
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

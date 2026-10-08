using Microsoft.Extensions.Configuration;

namespace Codai.Configuration;

/// <summary>
/// Resolves <see cref="CodaiOptions"/> from a configuration file, environment
/// variables and command-line flags (in increasing order of precedence).
/// </summary>
public static class CodaiConfigurationLoader
{
    private static readonly string[] CandidateExtensions = [".yml", ".yaml", ".json"];

    /// <summary>Environment variables that map onto configuration keys.</summary>
    private static readonly (string Variable, string Key)[] EnvironmentMap =
    [
        ("THEME", ConfigKeys.Theme),
        ("PROVIDER", ConfigKeys.Provider),
        ("BASE_URL", ConfigKeys.BaseUrl),
        ("MODEL", ConfigKeys.Model),
        ("TEMPERATURE", ConfigKeys.Temperature),
        ("REASONING_EFFORT", ConfigKeys.ReasoningEffort),
        ("MAX_TOKENS", ConfigKeys.MaxTokens),
        ("API_KEY", ConfigKeys.ApiKey),
        ("API_VERSION", ConfigKeys.ApiVersion),
    ];

    public static CodaiOptions Load(
        CodaiConfigurationRequest request,
        IEnvironmentReader? environment = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);

        environment ??= ProcessEnvironmentReader.Instance;

        var cli = CommandLineParser.Parse(request.CommandLineArgs ?? []);
        var explicitPath = request.ConfigFilePath ?? cli.ConfigPath;

        var filePath = ResolveConfigFile(request.WorkingDirectory, explicitPath, out var isExplicit);

        var builder = new ConfigurationBuilder();

        if (filePath is not null)
        {
            AddConfigurationFile(builder, filePath, isExplicit);
        }

        builder.AddInMemoryCollection(ReadEnvironmentOverrides(environment));
        builder.AddInMemoryCollection(cli.Overrides);

        var configuration = builder.Build();

        CodaiOptions options;
        try
        {
            options = configuration.Get<CodaiOptions>() ?? new CodaiOptions();
        }
        catch (Exception ex)
        {
            throw new CodaiConfigurationException("Failed to bind configuration values.", ex);
        }

        options.LoadedFrom = filePath is null ? null : Path.GetFullPath(filePath);

        Validate(options);

        return options;
    }

    /// <summary>
    /// Locates <c>codai-config.(yml|yaml|json)</c> in <paramref name="workingDirectory"/>.
    /// </summary>
    public static string? FindDefaultConfigFile(string workingDirectory)
    {
        foreach (var extension in CandidateExtensions)
        {
            var candidate = Path.Combine(workingDirectory, CodaiOptions.FileName + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ResolveConfigFile(string workingDirectory, string? explicitPath, out bool isExplicit)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            isExplicit = true;

            if (!File.Exists(explicitPath))
            {
                throw new CodaiConfigurationException(
                    $"Configuration file '{explicitPath}' was not found.");
            }

            return explicitPath;
        }

        isExplicit = false;
        return FindDefaultConfigFile(workingDirectory);
    }

    private static void AddConfigurationFile(IConfigurationBuilder builder, string filePath, bool isExplicit)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        switch (extension)
        {
            case ".yml":
            case ".yaml":
                builder.AddYamlFile(filePath, optional: !isExplicit);
                break;

            case ".json":
                builder.AddJsonFile(filePath, optional: !isExplicit, reloadOnChange: false);
                break;

            default:
                throw new CodaiConfigurationException(
                    $"Unsupported configuration file type '{extension}'. Use .yml, .yaml or .json.");
        }
    }

    private static Dictionary<string, string?> ReadEnvironmentOverrides(IEnvironmentReader environment)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (variable, key) in EnvironmentMap)
        {
            var value = environment.GetVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        // Also accept the standard nested form: AI_PROVIDER_CONFIG__API_KEY.
        foreach (var (name, rawValue) in environment.All())
        {
            if (string.IsNullOrWhiteSpace(name) || !name.Contains("__", StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(rawValue))
            {
                values[name.Replace("__", ":", StringComparison.Ordinal)] = rawValue;
            }
        }

        return values;
    }

    private static void Validate(CodaiOptions options)
    {
        options.AiProviderConfig.Provider = options.AiProviderConfig.Provider.Trim();

        if (string.IsNullOrWhiteSpace(options.AiProviderConfig.Provider))
        {
            throw new CodaiConfigurationException("No AI provider was configured.");
        }

        if (!ProviderDefaults.IsSupported(options.AiProviderConfig.Provider))
        {
            throw new CodaiConfigurationException(
                $"Unsupported provider '{options.AiProviderConfig.Provider}'. " +
                $"Supported providers: {string.Join(", ", ProviderDefaults.SupportedProviders)}.");
        }

        // Resolve the provider-specific model default (DeepSeek -> deepseek-flash).
        options.AiProviderConfig.Model = ProviderDefaults.ResolveModel(
            options.AiProviderConfig.Provider,
            options.AiProviderConfig.Model);

        var temperature = options.AiProviderConfig.Temperature;
        if (temperature is < 0f or > 2f)
        {
            throw new CodaiConfigurationException(
                $"Temperature must be between 0 and 2 but was {temperature}.");
        }

        if (string.IsNullOrWhiteSpace(options.Theme))
        {
            options.Theme = CodaiDefaults.Theme;
        }
    }
}

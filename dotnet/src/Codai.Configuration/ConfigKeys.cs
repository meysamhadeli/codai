namespace Codai.Configuration;

/// <summary>
/// Configuration keys, in the <c>section:key</c> form used by the .NET binder.
/// </summary>
public static class ConfigKeys
{
    public const string Version = "version";
    public const string Theme = "theme";

    public const string ProviderSection = CodaiOptions.ProviderSectionName;

    public const string Provider = ProviderSection + ":provider";
    public const string BaseUrl = ProviderSection + ":base_url";
    public const string Model = ProviderSection + ":model";
    public const string Stream = ProviderSection + ":stream";
    public const string Temperature = ProviderSection + ":temperature";
    public const string ReasoningEffort = ProviderSection + ":reasoning_effort";
    public const string EncodingFormat = ProviderSection + ":encoding_format";
    public const string MaxTokens = ProviderSection + ":max_tokens";
    public const string ApiKey = ProviderSection + ":api_key";
    public const string ApiVersion = ProviderSection + ":api_version";
}

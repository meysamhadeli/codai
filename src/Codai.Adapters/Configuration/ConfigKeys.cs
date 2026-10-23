namespace Codai.Configuration;

/// <summary>
/// Configuration keys, in the <c>section:key</c> form used by the .NET binder.
/// </summary>
public static class ConfigKeys
{
    public const string Theme = "theme";

    public const string MaxConsecutiveToolFailures = "budget:max_consecutive_tool_failures";

    public const string ProviderSection = CodaiOptions.ProviderSectionName;

    public const string Provider = ProviderSection + ":provider";
    public const string BaseUrl = ProviderSection + ":base_url";
    public const string Model = ProviderSection + ":model";
    public const string Stream = ProviderSection + ":stream";
    public const string Temperature = ProviderSection + ":temperature";
    public const string ReasoningEffort = ProviderSection + ":reasoning_effort";
    public const string MaxTokens = ProviderSection + ":max_tokens";
    public const string ApiKey = ProviderSection + ":api_key";
    public const string ApiVersion = ProviderSection + ":api_version";

    public const string PlanProvider = "plan_provider";
    public const string PlanModel = "plan_model";
    public const string AgentProvider = "agent_provider";
    public const string AgentModel = "agent_model";
    public const string CheckpointRetention = "checkpoint_retention";
    public const string CheckpointsEnabled = "checkpoints_enabled";
}

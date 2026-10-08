namespace Codai.Providers.Tests;

/// <summary>
/// A fact that only runs when live provider tests are explicitly enabled. Live tests
/// require both the <c>API_KEY</c> environment variable and <c>CODAI_RUN_INTEGRATION=1</c>,
/// so the default test run stays offline and free.
/// </summary>
public sealed class RequiresApiKeyFactAttribute : FactAttribute
{
    public const string ApiKeyVariable = "API_KEY";
    public const string OptInVariable = "CODAI_RUN_INTEGRATION";

    public RequiresApiKeyFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OptInVariable)))
        {
            Skip = $"Live provider tests are opt-in. Set {OptInVariable}=1 and {ApiKeyVariable} to run them.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyVariable)))
        {
            Skip = $"{ApiKeyVariable} environment variable is not set; skipping live provider test.";
        }
    }
}

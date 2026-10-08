namespace Codai.Providers;

/// <summary>
/// Raised when provider settings are missing or invalid.
/// </summary>
public sealed class ProviderConfigurationException : Exception
{
    public ProviderConfigurationException(string message)
        : base(message)
    {
    }

    public ProviderConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

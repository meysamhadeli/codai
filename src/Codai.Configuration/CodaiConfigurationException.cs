namespace Codai.Configuration;

/// <summary>
/// Raised when configuration cannot be loaded or is invalid.
/// </summary>
public sealed class CodaiConfigurationException : Exception
{
    public CodaiConfigurationException(string message)
        : base(message)
    {
    }

    public CodaiConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

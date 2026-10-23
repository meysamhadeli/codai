namespace Codai.Application;

/// <summary>
/// Host-neutral entry point for running a Codai coding session.
/// </summary>
public interface ICodaiSession
{
    /// <summary>Runs the session until completion or cancellation.</summary>
    Task<int> RunAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs one request without reading interactive input.</summary>
    Task<int> RunTaskAsync(string request, CancellationToken cancellationToken = default);

    /// <summary>Executes the latest completed Plan proposal after explicit transition consent.</summary>
    Task<int> ExecutePlanAsync(string instructions = "", CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This host does not support reviewed plan execution.");
}

namespace Codai.Core.Abstractions;

/// <summary>
/// Reads the full contents of the files the model asked for when the project
/// signatures alone were not enough to answer a request.
/// </summary>
public interface IRequestedContextResolver
{
    /// <summary>
    /// Reads <paramref name="relativePaths"/> (resolved against
    /// <paramref name="rootDirectory"/>) and renders them as
    /// <c>**File: relative/path**\n\n&lt;content&gt;</c> blocks separated by
    /// <c>\n---------\n\n</c>.
    /// </summary>
    /// <returns>The rendered context, or <c>null</c> when no readable file was found.</returns>
    Task<string?> ResolveAsync(
        string rootDirectory,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken = default);
}

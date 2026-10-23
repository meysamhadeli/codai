using Codai.Core.Prompting;

namespace Codai.Core.Abstractions;

/// <summary>
/// Turns the <c>@path</c> mentions in a user request into file contents for the prompt.
/// </summary>
public interface IMentionResolver
{
    /// <summary>
    /// Resolves every mention in <paramref name="userInput"/> against
    /// <paramref name="rootDirectory"/>. A mention that matches nothing is reported
    /// back instead of failing the request.
    /// </summary>
    Task<MentionResolution> ResolveAsync(
        string rootDirectory,
        string userInput,
        CancellationToken cancellationToken = default);
}

using Codai.Core.Models;

namespace Codai.Core.Abstractions;

/// <summary>
/// Turns a model response into a set of file changes.
/// </summary>
public interface ICodeChangeExtractor
{
    /// <summary>
    /// Parses <c>File: path</c> headers followed by fenced code blocks into
    /// <see cref="CodeChange"/> instances. Returns an empty list when the response
    /// contains no applicable changes.
    /// </summary>
    IReadOnlyList<CodeChange> Extract(string aiResponse);
}

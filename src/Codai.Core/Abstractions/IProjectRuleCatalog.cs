using Codai.Core.Customization;

namespace Codai.Core.Abstractions;

/// <summary>
/// Project rules in <c>.codai/rules</c>, combined with explicitly configured global rules.
/// </summary>
/// <remarks>
/// A rule with no <c>applyTo</c> front matter applies to every request; one that names
/// globs applies only when the request references a matching file. Loading is separate
/// from selection so the "which rules apply now" decision stays a pure function of the
/// loaded rules and the files a request brought in.
/// </remarks>
public interface IProjectRuleCatalog
{
    /// <summary>
    /// Loads bounded project rules first, then non-overridden global rules, each sorted by path.
    /// Project-relative rule paths override identical global-relative rule paths.
    /// </summary>
    Task<IReadOnlyList<ProjectRule>> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);
}

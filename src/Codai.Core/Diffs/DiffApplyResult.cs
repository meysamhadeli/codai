namespace Codai.Core.Diffs;

/// <summary>Outcome of applying a single change to disk.</summary>
/// <param name="FullPath">Absolute path the change targeted.</param>
/// <param name="Written">True when the file was created or overwritten.</param>
/// <param name="Deleted">True when the file was deleted because the result was empty.</param>
public sealed record DiffApplyResult(string FullPath, bool Written, bool Deleted)
{
    /// <summary>True when the change had an effect on disk.</summary>
    public bool Changed => Written || Deleted;
}

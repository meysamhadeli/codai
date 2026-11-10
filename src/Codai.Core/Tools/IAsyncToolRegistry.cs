namespace Codai.Core.Tools;

/// <summary>A tool registry whose external tools can be initialized before a model request.</summary>
public interface IAsyncToolRegistry : IToolRegistry
{
    /// <summary>Initializes external tools once, without exposing partially discovered definitions.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
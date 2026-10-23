namespace Codai.Core.Tools;

/// <summary>
/// The tool set offered to the model for a session.
/// </summary>
public interface IToolRegistry
{
    /// <summary>Every registered tool, in the order they should be offered.</summary>
    IReadOnlyList<ITool> Tools { get; }

    /// <summary>Looks a tool up by the name the model used.</summary>
    bool TryGet(string name, out ITool tool);
}

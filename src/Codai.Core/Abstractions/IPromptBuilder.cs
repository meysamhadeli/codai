using Codai.Core.Chat;
using Codai.Core.Projects;

namespace Codai.Core.Abstractions;

/// <summary>
/// Builds the provider request from the project context, history and user input.
/// </summary>
public interface IPromptBuilder
{
    ChatRequest Build(
        ProjectContext context,
        IReadOnlyList<ChatTurn> history,
        string userInput,
        string? requestedContext = null);
}

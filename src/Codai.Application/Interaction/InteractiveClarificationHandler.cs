using Codai.Core.Abstractions;

namespace Codai.Application;

public sealed class InteractiveClarificationHandler(IUserInterface ui) : IClarificationHandler
{
    public Task<string?> AskAsync(string question, IReadOnlyList<string> options,
        CancellationToken cancellationToken = default) =>
        ui.AskQuestionAsync(question, options, cancellationToken);
}

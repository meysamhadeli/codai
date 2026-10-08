using Codai.Core.Abstractions;
using Codai.Core.Prompting;

namespace Codai.Session.Tests.Fakes;

/// <summary>
/// Returns the instructions a test wants the project to "ship", or none at all.
/// </summary>
internal sealed class FakeProjectInstructionsProvider : IProjectInstructionsProvider
{
    private readonly string? _content;

    public FakeProjectInstructionsProvider(string? content)
    {
        _content = content;
    }

    public int LoadCount { get; private set; }

    public Task<ProjectInstructions?> LoadAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        LoadCount++;

        return Task.FromResult(_content is null
            ? null
            : new ProjectInstructions("AGENTS.md", _content));
    }
}

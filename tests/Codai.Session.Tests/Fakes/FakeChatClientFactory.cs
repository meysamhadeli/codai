using Codai.Core.Abstractions;
using Codai.Core.Tools;
using Codai.Providers;
using Codai.Providers.Abstractions;
using Microsoft.Extensions.AI;

namespace Codai.Session.Tests.Fakes;

/// <summary>
/// Stands in for the provider layer where a test is about the loop that drives a client
/// rather than about the client, and records the executor each service was built with so
/// the test can see which tools that loop was given.
/// </summary>
public sealed class FakeChatClientFactory : IChatClientFactory
{
    /// <summary>The service every <see cref="CreateService"/> call returns.</summary>
    public ScriptedChatCompletionService Service { get; } = new();

    /// <summary>The executor each service was built with, in call order.</summary>
    public List<IToolExecutor?> Executors { get; } = [];

    /// <summary>The options each service was built with, in call order.</summary>
    public List<ResolvedProviderOptions> Options { get; } = [];

    /// <summary>The tools the last service was offered, or empty when none was built.</summary>
    public IReadOnlyList<string> OfferedToolNames =>
        Executors.Count == 0 || Executors[^1] is null
            ? []
            : [.. Executors[^1]!.Tools.Select(tool => tool.Name)];

    public IChatClient CreateClient(ResolvedProviderOptions options) =>
        throw new NotSupportedException("These tests never need a raw client.");

    public IChatCompletionService CreateService(
        ResolvedProviderOptions options,
        IToolExecutor? toolExecutor = null)
    {
        Options.Add(options);
        Executors.Add(toolExecutor);

        return Service;
    }
}

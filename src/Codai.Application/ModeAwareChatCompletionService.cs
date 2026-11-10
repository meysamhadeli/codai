using Codai.Core.Abstractions;
using Codai.Core.Agents;
using Codai.Core.Chat;
using Codai.Core.Tools;
using Codai.Providers;
using Codai.Providers.Abstractions;

namespace Codai.Application;

internal sealed class ModeAwareChatCompletionService(
    AgentModeState mode,
    ModeModelState models,
    IChatClientFactory factory,
    IToolExecutor tools,
    IChatCompletionService plan,
    IChatCompletionService agent) : IChatCompletionService
{
    private readonly ResolvedProviderOptions _initialPlan = models.Get(AgentMode.Plan);
    private readonly ResolvedProviderOptions _initialAgent = models.Get(AgentMode.Agent);

    public string ProviderName => Current.ProviderName;

    public string ModelName => Current.ModelName;

    public IAsyncEnumerable<ChatStreamUpdate> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default) =>
        Current.StreamAsync(request, cancellationToken);

    private IChatCompletionService Current
    {
        get
        {
            var selected = models.Get(mode.Mode);
            var initial = mode.Mode switch
            {
                AgentMode.Plan => plan,
                AgentMode.Agent => agent,
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode.Mode, "Unknown agent mode."),
            };

            return selected == (mode.Mode == AgentMode.Plan ? _initialPlan : _initialAgent)
                ? initial
                : factory.CreateService(selected, tools);
        }
    }
}

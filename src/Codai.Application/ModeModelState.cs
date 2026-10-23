using Codai.Configuration;
using Codai.Core.Agents;
using Codai.Providers;

namespace Codai.Application;

/// <summary>Mutable provider/model selections used by the running session.</summary>
public sealed class ModeModelState
{
    private readonly object _gate = new();
    private readonly Dictionary<AgentMode, ResolvedProviderOptions> _selections;

    /// <summary>Creates independent model selections for the two supported modes.</summary>
    /// <param name="plan">Provider and model used for read-only planning.</param>
    /// <param name="agent">Provider and model used for task execution.</param>
    public ModeModelState(
        ResolvedProviderOptions plan,
        ResolvedProviderOptions agent)
    {
        _selections = new Dictionary<AgentMode, ResolvedProviderOptions>
        {
            [AgentMode.Plan] = plan,
            [AgentMode.Agent] = agent,
        };
    }

    public ResolvedProviderOptions Get(AgentMode mode)
    {
        lock (_gate)
        {
            return _selections[mode];
        }
    }

    public void Set(AgentMode mode, string provider, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        lock (_gate)
        {
            var current = _selections[mode];
            _selections[mode] = ResolvedProviderOptions.From(new AiProviderOptions
            {
                Provider = provider,
                Model = model,
                ApiKey = current.ApiKey,
                ApiVersion = current.ApiVersion,
                BaseUrl = string.Equals(provider, current.Provider, StringComparison.OrdinalIgnoreCase)
                    ? current.BaseUrl
                    : null,
                Temperature = current.Temperature,
                ReasoningEffort = current.ReasoningEffort,
                MaxTokens = current.MaxTokens,
                Stream = current.Stream,
            });
        }
    }
}

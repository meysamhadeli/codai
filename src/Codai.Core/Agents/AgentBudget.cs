using Codai.Core.Tokens;

namespace Codai.Core.Agents;

public sealed class AgentBudget
{
    public const int DefaultMaxToolCalls = 60;

    public int MaxToolCalls { get; init; } = DefaultMaxToolCalls;
    public int MaxTokens { get; init; }
    public decimal MaxCost { get; init; }

    public int ToolCalls { get; private set; }
    public TokenUsage Usage { get; private set; } = TokenUsage.Empty;
    public decimal Cost { get; private set; }

    public void BeginTask()
    {
        ToolCalls = 0;
        Usage = TokenUsage.Empty;
        Cost = 0;
    }

    public bool TryConsumeToolCall(out string reason)
    {
        if (MaxToolCalls > 0 && ToolCalls >= MaxToolCalls)
        {
            reason = $"Task tool budget reached ({MaxToolCalls} calls). Answer with what is verified.";
            return false;
        }

        ToolCalls++;
        reason = string.Empty;
        return true;
    }

    public bool TryConsume(TokenUsage usage, decimal cost, out string reason)
    {
        var nextUsage = Usage.Add(usage);
        var nextCost = Cost + cost;
        if (MaxTokens > 0 && nextUsage.TotalTokens > MaxTokens)
        {
            reason = $"Task token budget reached ({MaxTokens} tokens).";
            return false;
        }

        if (MaxCost > 0 && nextCost > MaxCost)
        {
            reason = $"Task cost budget reached ({MaxCost:0.####}).";
            return false;
        }

        Usage = nextUsage;
        Cost = nextCost;
        reason = string.Empty;
        return true;
    }
}
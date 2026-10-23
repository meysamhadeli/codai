using Codai.Core.Tools;
using Microsoft.Extensions.Configuration;

namespace Codai.Configuration;

public sealed class AgentBudgetOptions
{
    public int MaxTokens { get; set; }
    public decimal MaxCost { get; set; }
    public int MaxToolCalls { get; set; }

    [ConfigurationKeyName("max_consecutive_tool_failures")]
    public int MaxConsecutiveToolFailures { get; set; } = ToolCallGovernor.DefaultMaxConsecutiveFailures;
}

using Microsoft.Extensions.AI;

namespace Codai.Providers.Internal;

/// <summary>
/// Helpers for reading values out of the Microsoft.Extensions.AI content model.
/// </summary>
internal static class AiContentHelpers
{
    /// <summary>
    /// Extracts token usage from a streaming update, if the update carries it.
    /// </summary>
    public static Codai.Core.Models.TokenUsage ExtractUsage(ChatResponseUpdate update)
    {
        foreach (var content in update.Contents)
        {
            if (content is UsageContent usage)
            {
                return ToTokenUsage(usage.Details);
            }
        }

        return Codai.Core.Models.TokenUsage.Empty;
    }

    /// <summary>Extracts token usage from a completed response.</summary>
    public static Codai.Core.Models.TokenUsage ExtractUsage(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Prefer the dedicated usage property, falling back to a UsageContent part
        // (clients built on non-OpenAI wire formats may only expose the latter).
        if (response.Usage is not null)
        {
            return ToTokenUsage(response.Usage);
        }

        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is UsageContent usage)
                {
                    return ToTokenUsage(usage.Details);
                }
            }
        }

        return Codai.Core.Models.TokenUsage.Empty;
    }

    public static Codai.Core.Models.TokenUsage ToTokenUsage(UsageDetails? details)
    {
        if (details is null)
        {
            return Codai.Core.Models.TokenUsage.Empty;
        }

        return new Codai.Core.Models.TokenUsage(
            (int)(details.InputTokenCount ?? 0),
            (int)(details.OutputTokenCount ?? 0));
    }
}

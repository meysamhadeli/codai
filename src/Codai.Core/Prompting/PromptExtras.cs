namespace Codai.Core.Prompting;

/// <summary>
/// The optional extra sections of the system prompt, in the order they are rendered.
/// </summary>
/// <remarks>
/// Every section is omitted when its value is empty, so a session that uses none of
/// them produces exactly the prompt the builder produced before they existed. That is
/// what keeps <c>PromptParityTests</c> meaningful.
/// </remarks>
public sealed record PromptExtras
{
    /// <summary>
    /// Contents of the project instruction file (<c>AGENTS.md</c>, <c>CLAUDE.md</c>, …)
    /// that applies to the whole session.
    /// </summary>
    public string? Instructions { get; init; }

    /// <summary>
    /// Contents of the files the user pulled into this one request with an
    /// <c>@path</c> mention.
    /// </summary>
    public string? AttachedContext { get; init; }

    /// <summary>
    /// Contents of the files the model asked for when the project summary was not
    /// enough. Produced by the retry, never by the first request.
    /// </summary>
    public string? RequestedContext { get; init; }
}

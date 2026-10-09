using Codai.Core.Agents;
using Codai.Core.Customization;

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
    /// How much freedom this request has, or <c>null</c> for a request that is not part
    /// of an agent session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mode adds a paragraph of behavioural guidance to the prompt and restricts the
    /// tools the request may call. <c>null</c> adds neither, which is what the prompt
    /// parity tests pin: a caller that never mentions a mode gets byte-for-byte the
    /// prompt the builder produced before modes existed.
    /// </para>
    /// <para>
    /// The session always sets it, from <see cref="AgentModeParser.Default"/>, so the mode
    /// the user is working in is the mode the prompt is built for. A caller that names no
    /// mode gets no guidance and every tool the builder holds, which is what keeps the
    /// prompt parity tests meaningful.
    /// </para>
    /// </remarks>
    public AgentMode? Mode { get; init; }

    /// <summary>
    /// Contents of the project instruction file (<c>AGENTS.md</c>, <c>CLAUDE.md</c>, …)
    /// that applies to the whole session.
    /// </summary>
    public string? Instructions { get; init; }

    /// <summary>
    /// The skills the project ships. Only each skill's name and description are
    /// rendered: the body is fetched by the <c>read_skill</c> tool when the model decides
    /// the skill applies, which is what keeps unused skills out of the context window.
    /// </summary>
    public IReadOnlyList<SkillDefinition>? Skills { get; init; }

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

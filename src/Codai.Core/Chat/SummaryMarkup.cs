namespace Codai.Core.Chat;

/// <summary>
/// Pulls the summary out of a summariser response that wraps it in markup.
/// </summary>
/// <remarks>
/// <para>
/// The compaction prompt asks for two blocks: an <c>&lt;analysis&gt;</c> block to think
/// in, and a <c>&lt;summary&gt;</c> block to write in. That is worth the trouble — the
/// models that follow it produce noticeably better summaries — but only the summary may
/// end up in the history. Keeping the scratch work would put the conversation's reasoning
/// back into every following request, which is the cost compaction exists to remove.
/// </para>
/// <para>
/// Every path here has to end in usable text rather than in an error. A model that
/// ignores the markup has still written a summary, and a compaction that throws away a
/// usable summary because it arrived in the wrong wrapper would leave the conversation
/// over its budget for no reason.
/// </para>
/// </remarks>
public static class SummaryMarkup
{
    private const string AnalysisOpen = "<analysis>";
    private const string AnalysisClose = "</analysis>";
    private const string SummaryOpen = "<summary>";
    private const string SummaryClose = "</summary>";

    /// <summary>
    /// Returns the text of <paramref name="response"/> that should be kept as the summary.
    /// </summary>
    /// <param name="response">What the summariser returned, of any shape.</param>
    /// <returns>
    /// The contents of the summary block when there is one, otherwise the response with
    /// any analysis block removed. Null or blank input returns null.
    /// </returns>
    public static string? Extract(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var text = Between(response, SummaryOpen, SummaryClose)
            ?? WithoutAnalysis(response);

        text = text.Trim();

        // A model that decided to answer in JSON, or to quote the conversation, can wrap
        // the whole summary in quotes. They are not part of the summary.
        if (text.Length > 1 && text[0] == '"' && text[^1] == '"' && !text.Contains('\n'))
        {
            text = text[1..^1].Trim();
        }

        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Contents of the first <paramref name="open"/>…<paramref name="close"/> pair, or the
    /// text after the opening tag when the closing tag never arrived.
    /// </summary>
    private static string? Between(string text, string open, string close)
    {
        var start = text.IndexOf(open, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return null;
        }

        start += open.Length;
        var end = text.IndexOf(close, start, StringComparison.OrdinalIgnoreCase);

        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }

    /// <summary>Drops the analysis block, keeping everything around it.</summary>
    private static string WithoutAnalysis(string text)
    {
        var start = text.IndexOf(AnalysisOpen, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return text;
        }

        var end = text.IndexOf(AnalysisClose, start, StringComparison.OrdinalIgnoreCase);

        return end < 0
            ? text[..start]
            : text[..start] + text[(end + AnalysisClose.Length)..];
    }
}

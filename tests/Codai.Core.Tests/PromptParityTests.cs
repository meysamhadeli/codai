using System.Globalization;
using System.Text;
using Codai.Core.Assets;
using Codai.Core.Chat;
using Codai.Core.Projects;
using Codai.Core.Prompting;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Re-implements the original Go <c>CodeAnalyzer.GeneratePrompt</c> and asserts the
/// C# builder produces the identical string, so prompts stay stable across the
/// rewrite.
/// </summary>
/// <remarks>
/// The only intentional divergence is the history timestamp: Go prints
/// <c>time.Now()</c> including a monotonic reading, which cannot be reproduced.
/// These tests therefore format the expected timestamp the same way the C#
/// implementation does; every other character is compared literally.
/// </remarks>
public class PromptParityTests
{
    /// <summary>Verbatim port of <c>chat_history.AddToHistory</c>'s entry format.</summary>
    private static string GoHistoryEntry(DateTimeOffset timestamp, string userRequest, string aiResponse) =>
        $"### History time:\n\n{timestamp.ToString(CultureInfo.InvariantCulture)}\n---------\n\n" +
        $"### Here is user request:\n\n{userRequest}\n---------\n\n" +
        $"### Here is the the response from using AI:\n\n{aiResponse}";

    /// <summary>Verbatim port of <c>CodeAnalyzer.GeneratePrompt</c>.</summary>
    private static (string FinalPrompt, string UserInputPrompt) GoGeneratePrompt(
        IReadOnlyList<string> codes,
        IReadOnlyList<string> history,
        string userInput,
        string promptTemplate,
        string requestedContext = "")
    {
        var code = string.Join("\n---------\n\n", codes);

        var prompt =
            $"{"## Here is the summary of context of project\n\n"}{code}\n\n______\n" +
            $"{"## Here is the general template prompt for using AI\n\n"}{promptTemplate}\n\n______\n";

        var userInputPrompt = $"## Here is user request\n{userInput}";

        if (requestedContext.Length > 0)
        {
            prompt +=
                $"{"## Here are the requsted full context files for using in your task\n\n"}{requestedContext}______\n";
        }

        var historyPrompt = "## Here is the history of chats\n\n" + string.Join("\n---------\n\n", history);

        var finalPrompt = $"{historyPrompt}\n\n______\n\n{prompt}";

        return (finalPrompt, userInputPrompt);
    }

    private static ProjectContext Context(params string[] rawCodes) => new()
    {
        Files = [],
        RawCodes = rawCodes,
    };

    private static PromptBuilder CreateBuilder() => new(EmbeddedResources.FullContextTemplate);

    [Fact]
    public void System_prompt_matches_the_go_layout_without_history_or_requested_context()
    {
        var (expected, _) = GoGeneratePrompt(
            ["**File: a.cs**\n\nclass A"],
            history: [],
            "do the thing",
            EmbeddedResources.FullContextTemplate);

        var actual = CreateBuilder().Build(Context("**File: a.cs**\n\nclass A"), [], "do the thing");

        actual.SystemPrompt.Should().Be(expected);
    }

    [Fact]
    public void System_prompt_matches_the_go_layout_with_multiple_code_blocks()
    {
        var codes = new[]
        {
            "**File: a.cs**\n\nclass A",
            "**File: b.cs**\n\nclass B",
            "**File: c.cs**\n\nclass C",
        };

        var (expected, _) = GoGeneratePrompt(codes, [], "refactor", EmbeddedResources.FullContextTemplate);

        var actual = CreateBuilder().Build(Context(codes), [], "refactor");

        actual.SystemPrompt.Should().Be(expected);
    }

    [Fact]
    public void System_prompt_matches_the_go_layout_with_history()
    {
        var timestamp = DateTimeOffset.Parse("2024-05-01T12:34:56Z");

        var history = new List<ChatTurn> { new(timestamp, "old request", "old response") };

        var (expected, _) = GoGeneratePrompt(
            ["code"],
            [GoHistoryEntry(timestamp, "old request", "old response")],
            "next",
            EmbeddedResources.FullContextTemplate);

        var actual = CreateBuilder().Build(Context("code"), history, "next");

        actual.SystemPrompt.Should().Be(expected);
    }

    [Fact]
    public void System_prompt_matches_the_go_layout_with_requested_context()
    {
        var (expected, _) = GoGeneratePrompt(
            ["code"],
            [],
            "x",
            EmbeddedResources.FullContextTemplate,
            "**File: full.cs**\n\nclass Full\n---------");

        var actual = CreateBuilder().Build(
            Context("code"),
            [],
            "x",
            "**File: full.cs**\n\nclass Full\n---------");

        actual.SystemPrompt.Should().Be(expected);
    }

    [Fact]
    public void System_prompt_matches_the_go_layout_with_history_and_requested_context()
    {
        var timestamp = DateTimeOffset.Parse("2024-05-01T12:34:56Z");
        var history = new List<ChatTurn> { new(timestamp, "first", "first answer") };

        const string RequestedContext = "**File: extra.cs**\n\nclass Extra\n---------";

        var (expected, _) = GoGeneratePrompt(
            ["alpha", "beta"],
            [GoHistoryEntry(timestamp, "first", "first answer")],
            "second",
            EmbeddedResources.FullContextTemplate,
            RequestedContext);

        var actual = CreateBuilder().Build(Context("alpha", "beta"), history, "second", RequestedContext);

        actual.SystemPrompt.Should().Be(expected);
    }

    [Fact]
    public void User_message_matches_the_go_layout()
    {
        var (_, expected) = GoGeneratePrompt([], [], "please add a test", EmbeddedResources.FullContextTemplate);

        var actual = CreateBuilder().Build(ProjectContext.Empty, [], "please add a test");

        actual.UserMessage.Should().Be(expected);
    }

    [Fact]
    public void Section_order_matches_the_go_layout()
    {
        var timestamp = DateTimeOffset.Parse("2024-05-01T12:34:56Z");

        var actual = CreateBuilder().Build(
            Context("MARKER_CODE"),
            [new ChatTurn(timestamp, "MARKER_USER", "MARKER_AI")],
            "input",
            "MARKER_REQUESTED");

        var historyIndex = actual.SystemPrompt.IndexOf("## Here is the history of chats", StringComparison.Ordinal);
        var summaryIndex = actual.SystemPrompt.IndexOf("## Here is the summary of context of project", StringComparison.Ordinal);
        var templateIndex = actual.SystemPrompt.IndexOf("## Here is the general template prompt for using AI", StringComparison.Ordinal);
        var requestedIndex = actual.SystemPrompt.IndexOf("## Here are the requsted full context files", StringComparison.Ordinal);

        historyIndex.Should().BeGreaterThanOrEqualTo(0);
        summaryIndex.Should().BeGreaterThan(historyIndex);
        templateIndex.Should().BeGreaterThan(summaryIndex);
        requestedIndex.Should().BeGreaterThan(templateIndex);

        // Sanity check that content actually lands in the right sections.
        actual.SystemPrompt[historyIndex..summaryIndex].Should().Contain("MARKER_USER").And.Contain("MARKER_AI");
        actual.SystemPrompt[summaryIndex..templateIndex].Should().Contain("MARKER_CODE");
        actual.SystemPrompt[requestedIndex..].Should().Contain("MARKER_REQUESTED");
    }

    [Fact]
    public void History_entries_are_separated_by_the_go_separator()
    {
        var timestamp = DateTimeOffset.Parse("2024-05-01T12:34:56Z");

        var actual = CreateBuilder().Build(
            Context("code"),
            [
                new ChatTurn(timestamp, "request-one", "response-one"),
                new ChatTurn(timestamp, "request-two", "response-two"),
            ],
            "input");

        var expectedJoin =
            GoHistoryEntry(timestamp, "request-one", "response-one") +
            "\n---------\n\n" +
            GoHistoryEntry(timestamp, "request-two", "response-two");

        actual.SystemPrompt.Should().Contain(expectedJoin);
    }

    [Fact]
    public void Empty_history_produces_the_same_empty_section_as_go()
    {
        var (expected, _) = GoGeneratePrompt(["code"], [], "input", EmbeddedResources.FullContextTemplate);

        var actual = CreateBuilder().Build(Context("code"), [], "input");

        // Go emits "## Here is the history of chats\n\n" followed immediately by "\n\n______\n\n".
        actual.SystemPrompt.Should().StartWith("## Here is the history of chats\n\n\n\n______\n\n");
        actual.SystemPrompt.Should().Be(expected);
    }

    [Fact]
    public void Requested_context_section_is_omitted_when_empty_string_is_supplied()
    {
        var actual = CreateBuilder().Build(Context("code"), [], "input", requestedContext: string.Empty);

        actual.SystemPrompt.Should().NotContain("## Here are the requsted full context files");
    }

    [Fact]
    public void Prompt_template_is_identical_to_the_frozen_golden_template()
    {
        // The Go template is frozen into Golden/ so the assertion survives removal of
        // the Go sources; a missing golden is a hard failure, never a silent skip.
        var goldenTemplate = GoldenFiles.ReadPromptTemplate();

        EmbeddedResources.FullContextTemplate.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Should().Be(goldenTemplate.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Full_prompt_is_byte_identical_to_go_for_a_representative_request()
    {
        var timestamp = DateTimeOffset.Parse("2024-05-01T12:34:56Z");

        var codes = new[] { "**File: Program.cs**\n\nclass Program", "**File: Util.cs**\n\nclass Util" };
        var turn = new ChatTurn(timestamp, "add logging", "Added logging.");
        const string Requested = "**File: Extra.cs**\n\nclass Extra\n---------";
        const string Input = "now add a test";

        var (expectedSystem, expectedUser) = GoGeneratePrompt(
            codes,
            [GoHistoryEntry(timestamp, "add logging", "Added logging.")],
            Input,
            EmbeddedResources.FullContextTemplate,
            Requested);

        var actual = CreateBuilder().Build(Context(codes), [turn], Input, Requested);

        actual.SystemPrompt.Should().Be(expectedSystem);
        actual.UserMessage.Should().Be(expectedUser);

        // The reconstruction must round-trip through the same byte length.
        Encoding.UTF8.GetByteCount(actual.SystemPrompt)
            .Should().Be(Encoding.UTF8.GetByteCount(expectedSystem));
    }
}

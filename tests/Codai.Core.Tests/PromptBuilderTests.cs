using Codai.Core.Assets;
using Codai.Core.Chat;
using Codai.Core.Projects;
using Codai.Core.Prompting;
using FluentAssertions;

namespace Codai.Core.Tests;

public class EmbeddedResourcesTests
{
    [Fact]
    public void FullContextTemplate_is_embedded_and_not_empty()
    {
        EmbeddedResources.FullContextTemplate.Should().NotBeNullOrWhiteSpace();
        EmbeddedResources.FullContextTemplate.Should().Contain("You are an AI code assistant");
        EmbeddedResources.FullContextTemplate.Should().Contain("CODE BLOCK");
    }
}

public class PromptBuilderTests
{
    private static PromptBuilder CreateBuilder() =>
        new(EmbeddedResources.FullContextTemplate);

    private static ProjectContext Context(params string[] rawCodes) => new()
    {
        Files = [],
        RawCodes = rawCodes,
    };

    [Fact]
    public void Build_includes_context_history_and_user_request()
    {
        var builder = CreateBuilder();
        var history = new List<ChatTurn>
        {
            new(DateTimeOffset.Parse("2024-01-01T10:00:00Z"), "prev-request-1", "prev-response-1"),
            new(DateTimeOffset.Parse("2024-01-01T11:00:00Z"), "prev-request-2", "prev-response-2"),
        };

        var request = builder.Build(
            Context("code1", "code2"),
            history,
            "User request",
            "Requested context");

        request.SystemPrompt.Should().Contain("code1");
        request.SystemPrompt.Should().Contain("code2");
        request.SystemPrompt.Should().Contain("prev-request-1");
        request.SystemPrompt.Should().Contain("prev-response-2");
        request.SystemPrompt.Should().Contain("Requested context");
        request.SystemPrompt.Should().Contain("## Here is the history of chats");
        request.SystemPrompt.Should().Contain("## Here is the summary of context of project");

        request.UserMessage.Should().Contain("User request");
        request.UserMessage.Should().StartWith("## Here is user request");
    }

    [Fact]
    public void Build_omits_requested_context_section_when_not_provided()
    {
        var request = CreateBuilder().Build(Context("code"), [], "do something");

        request.SystemPrompt.Should().NotContain("Here are the requsted full context files");
    }

    [Fact]
    public void Build_joins_raw_codes_with_separator()
    {
        var request = CreateBuilder().Build(Context("alpha", "beta"), [], "x");

        request.SystemPrompt.Should().Contain("alpha\n---------\n\nbeta");
    }

    [Fact]
    public void Build_uses_template_between_separators()
    {
        var request = CreateBuilder().Build(Context("code"), [], "x");

        request.SystemPrompt.Should().Contain(
            "## Here is the general template prompt for using AI\n\n" +
            EmbeddedResources.FullContextTemplate);
    }

    [Fact]
    public void Constructor_rejects_empty_template()
    {
        var act = () => new PromptBuilder("  ");

        act.Should().Throw<ArgumentException>();
    }
}

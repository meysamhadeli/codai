using System.Text.Json;
using Codai.Core.Tools;
using FluentAssertions;

namespace Codai.Core.Tests;

public sealed class ToolRegistryTests
{
    [Fact]
    public void Disabled_tools_are_neither_advertised_nor_routable()
    {
        var registry = new ToolRegistry([new StubTool("read_file"), new StubTool("write_file")], ["write_file"]);

        registry.Tools.Select(tool => tool.Name).Should().Equal("read_file");
        registry.TryGet("write_file", out _).Should().BeFalse();
        registry.TryGet("read_file", out _).Should().BeTrue();
    }

    [Fact]
    public void Disabled_names_are_case_insensitive_and_duplicates_are_harmless()
    {
        var registry = new ToolRegistry([new StubTool("read_file")], ["READ_FILE", "read_file"]);

        registry.Tools.Should().BeEmpty();
        registry.TryGet("READ_FILE", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("")]
    [InlineData(" ")]
    public void Invalid_disabled_names_fail_closed(string name)
    {
        var create = () => new ToolRegistry([new StubTool("read_file")], [name]);

        create.Should().Throw<ArgumentException>().WithMessage("Unknown disabled tool*");
    }

    [Fact]
    public void Disabled_names_do_not_hide_duplicate_registration_errors()
    {
        var create = () => new ToolRegistry([new StubTool("read_file"), new StubTool("READ_FILE")], ["read_file"]);

        create.Should().Throw<ArgumentException>().WithMessage("Two tools*");
    }

    private sealed class StubTool(string name) : ITool
    {
        public string Name => name;
        public string Description => name;
        public bool IsReadOnly => true;
        public string ParametersSchema => "{}";
        public JsonElement Schema => JsonSerializer.SerializeToElement(new { });
        public Task<ToolResult> InvokeAsync(JsonElement arguments, ToolContext context,
            CancellationToken cancellationToken = default) => Task.FromResult(ToolResult.Success("ok"));
    }
}

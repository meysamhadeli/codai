using System.Text;
using Codai.Application.Acp;
using Codai.Core.Abstractions;
using Codai.Core.Agents;
using FluentAssertions;

namespace Codai.Session.Tests;

public sealed class AcpJsonLinesHostTests
{
    [Fact]
    public async Task Compatibility_subset_creates_session_switches_mode_and_runs_prompt()
    {
        var factory = new FakeFactory();
        var output = new StringBuilder();
        using var host = new AcpJsonLinesHost(
            new StringReader("{\"type\":\"initialize\"}\n{\"type\":\"new_session\",\"sessionId\":\"s1\",\"cwd\":\"C:/project\",\"provider\":\"ollama\",\"model\":\"test\"}\n{\"type\":\"set_mode\",\"sessionId\":\"s1\",\"mode\":\"plan\"}\n{\"type\":\"prompt\",\"sessionId\":\"s1\",\"text\":\"inspect\"}\n"),
            new StringWriter(output),
            factory);

        (await host.RunAsync()).Should().Be(0);
        output.ToString().Should().Contain("\"type\":\"initialized\"")
            .And.Contain("\"type\":\"session_created\"")
            .And.Contain("\"type\":\"mode_changed\"")
            .And.Contain("\"type\":\"text\"")
            .And.Contain("\"type\":\"prompt_complete\"");
        factory.Requests.Should().ContainSingle().Which.Mode.Should().Be(AgentMode.Agent);
        factory.Handle!.Mode.Mode.Should().Be(AgentMode.Plan);
        factory.Handle.Options.Provider.Should().Be("ollama");
    }

    [Fact]
    public async Task Permission_response_is_consumed_while_prompt_is_in_flight()
    {
        var factory = new FakeFactory(askPermission: true);
        var output = new StringBuilder();
        using var host = new AcpJsonLinesHost(
            new StringReader("{\"type\":\"new_session\",\"sessionId\":\"s1\",\"cwd\":\"C:/project\"}\n{\"type\":\"prompt\",\"sessionId\":\"s1\",\"text\":\"edit\"}\n{\"type\":\"permission_response\",\"sessionId\":\"s1\",\"approved\":true}\n"),
            new StringWriter(output),
            factory);

        (await host.RunAsync()).Should().Be(0);
        output.ToString().Should().Contain("\"type\":\"permission_request\"").And.Contain("\"type\":\"prompt_complete\"");
    }

    [Fact]
    public async Task Removed_ask_mode_is_rejected_without_switching_session()
    {
        var factory = new FakeFactory();
        var output = new StringBuilder();
        using var host = new AcpJsonLinesHost(
            new StringReader("{\"type\":\"new_session\",\"sessionId\":\"s1\",\"cwd\":\"C:/project\",\"mode\":\"plan\"}\n{\"type\":\"set_mode\",\"sessionId\":\"s1\",\"mode\":\"ask\"}\n"),
            new StringWriter(output), factory);

        (await host.RunAsync()).Should().Be(0);

        factory.Handle!.Mode.Mode.Should().Be(AgentMode.Plan);
        output.ToString().Should().Contain("Unknown mode").And.NotContain("\"type\":\"mode_changed\"");
    }

    private sealed class FakeFactory(bool askPermission = false) : IAcpSessionFactory
    {
        public List<AcpSessionRequest> Requests { get; } = [];
        public AcpSessionHandle? Handle { get; private set; }

        public AcpSessionHandle Create(AcpSessionRequest request, IUserInterface userInterface)
        {
            Requests.Add(request);
            var session = new FakeSession(userInterface, askPermission);
            var options = new SessionOptions { ProjectRoot = request.ProjectRoot, Provider = request.Provider ?? "default", Model = request.Model ?? "default" };
            Handle = new AcpSessionHandle(session, options, new AgentModeState(request.Mode));
            return Handle;
        }
    }

    private sealed class FakeSession(IUserInterface ui, bool askPermission) : ICodaiSession
    {
        public Task<int> RunAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public async Task<int> RunTaskAsync(string request, CancellationToken cancellationToken = default)
        {
            if (askPermission)
            {
                (await ui.ConfirmAsync("Allow edit?", cancellationToken)).Should().BeTrue();
            }

            ui.WriteMarkdown("answer");
            return 0;
        }
    }
}
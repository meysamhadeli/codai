using System.Diagnostics;
using System.Net;
using System.Text;
using Codai.Application.Abstractions;
using Codai.Cli;
using FluentAssertions;

namespace Codai.Session.Tests;

public sealed class ApplicationUpdaterTests
{
    [Theory]
    [InlineData("2.0.0", "2.10.0", true)]
    [InlineData("2.10.0", "2.10.0", false)]
    [InlineData("3.0.0", "2.10.0", false)]
    [InlineData("2.10.0-beta.1", "2.10.0", true)]
    public async Task Checks_stable_versions_using_nuget_ordering(string current, string latest, bool available)
    {
        using var http = Client("{\"versions\":[\"2.9.0\",\"99.0.0-preview.1\",\"2.10.0\",\"invalid\"]}");
        using var updater = new ApplicationUpdater(http, current, ToolBase());

        var result = await updater.CheckAsync();

        result.CurrentVersion.Should().Be(current);
        result.LatestVersion.Should().Be(latest);
        result.UpdateAvailable.Should().Be(available);
        result.InstallUnavailableReason.Should().BeNull();
    }

    [Theory]
    [InlineData("{\"versions\":[]}")]
    [InlineData("{\"versions\":[\"3.0.0-beta\"]}")]
    [InlineData("not json")]
    public async Task Invalid_feed_does_not_claim_up_to_date(string payload)
    {
        using var http = Client(payload);
        using var updater = new ApplicationUpdater(http, "2.0.0", ToolBase());

        var action = () => updater.CheckAsync();

        await action.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Network_failure_is_reported()
    {
        using var http = Client("unavailable", HttpStatusCode.ServiceUnavailable);
        using var updater = new ApplicationUpdater(http, "2.0.0", ToolBase());

        var action = () => updater.CheckAsync();

        await action.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Source_build_checks_but_does_not_update_another_installation()
    {
        using var http = Client("{\"versions\":[\"2.1.0\"]}");
        using var updater = new ApplicationUpdater(http, "2.0.0", AppContext.BaseDirectory);

        var result = await updater.CheckAsync();

        result.UpdateAvailable.Should().BeTrue();
        result.InstallUnavailableReason.Should().Contain("updated manually");
        var install = () => updater.InstallAsync("2.1.0");
        await install.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Installer_targets_running_tool_path_and_exact_checked_version(int exitCode)
    {
        ProcessStartInfo? captured = null;
        var toolBase = ToolBase();
        using var http = Client("{\"versions\":[\"2.1.0\"]}");
        using var updater = new ApplicationUpdater(http, "2.0.0", toolBase, (start, token) =>
        {
            captured = start;
            return Task.FromResult((exitCode, "SDK diagnostic"));
        });
        await updater.CheckAsync();

        var action = () => updater.InstallAsync("2.1.0");
        if (exitCode == 0)
        {
            await action.Should().NotThrowAsync();
        }
        else
        {
            await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SDK diagnostic*");
        }

        captured.Should().NotBeNull();
        captured!.UseShellExecute.Should().BeFalse();
        captured.ArgumentList.Should().Equal("tool", "update", "Codai", "--tool-path",
            ApplicationUpdater.FindToolDirectory(toolBase)!, "--version", "2.1.0", "--source",
            "https://api.nuget.org/v3/index.json", "--verbosity", "minimal");
        captured.WorkingDirectory.Should().Be(Path.GetTempPath());
        captured.RedirectStandardInput.Should().BeTrue();
    }

    [Fact]
    public async Task Installer_refuses_unchecked_version_and_honors_cancellation()
    {
        using var http = Client("{\"versions\":[\"2.1.0\"]}");
        using var updater = new ApplicationUpdater(http, "2.0.0", ToolBase(), (_, _) =>
            throw new InvalidOperationException("Must not execute"));
        var uncheckedInstall = () => updater.InstallAsync("2.1.0");
        await uncheckedInstall.Should().ThrowAsync<InvalidOperationException>().WithMessage("Check for updates*");
        await updater.CheckAsync();
        var wrongVersion = () => updater.InstallAsync("9.0.0");
        await wrongVersion.Should().ThrowAsync<InvalidOperationException>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => updater.InstallAsync("2.1.0", cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("update", true)]
    [InlineData("skip", false)]
    [InlineData(null, false)]
    public async Task Local_command_installs_only_after_explicit_choice(string? choice, bool installed)
    {
        var updater = new FakeUpdater(new("2.0.0", "2.1.0", true));
        var harness = new SessionHarness { Updater = updater };
        harness.Ui.EnqueueChoice(choice);
        harness.QueueInputs("/update", "/exit");

        await harness.Build().RunAsync();

        updater.Installed.Should().Be(installed ? "2.1.0" : null);
        harness.Chat.Requests.Should().BeEmpty();
        harness.Ui.ProgressLabels.Should().Contain("Checking for updates...");
        harness.Ui.Successes.Any(message => message.Contains("Restart Codai", StringComparison.Ordinal))
            .Should().Be(installed);
    }

    [Fact]
    public async Task Current_version_reports_up_to_date_without_confirmation()
    {
        var harness = new SessionHarness { Updater = new FakeUpdater(new("2.1.0", "2.1.0", false)) };
        harness.QueueInputs("/update", "/exit");

        await harness.Build().RunAsync();

        harness.Ui.Successes.Should().Contain("Codai 2.1.0 is already up to date.");
        harness.Ui.Dialogs.Should().BeEmpty();
        harness.Chat.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Failures_and_cancelled_checks_leave_session_usable(bool installing, bool cancelled)
    {
        var updater = new FakeUpdater(new("2.0.0", "2.1.0", true))
        {
            CheckError = installing ? null : cancelled ? new OperationCanceledException() : new HttpRequestException("Offline"),
            InstallError = installing ? new IOException("Installation failed") : null,
        };
        var harness = new SessionHarness { Updater = updater };
        harness.Ui.EnqueueChoice("update");
        harness.QueueInputs("/update", "/help", "/exit");

        await harness.Build().RunAsync();

        harness.Ui.Successes.Should().BeEmpty();
        harness.Ui.Lines.Should().Contain(message => message.Contains("/update", StringComparison.Ordinal));
        harness.Chat.Requests.Should().BeEmpty();
        if (cancelled)
        {
            harness.Ui.Infos.Should().Contain("Update cancelled.");
        }
        else
        {
            harness.Ui.Errors.Should().Contain(message => message.StartsWith("Could not update Codai:", StringComparison.Ordinal));
        }
    }

    private static string ToolBase() => Path.Combine(Path.GetTempPath(), "codai-update-tests-" + Guid.NewGuid().ToString("N"),
        ".store", "codai", "2.0.0", "codai", "2.0.0", "tools", "net10.0", "any");

    private static HttpClient Client(string payload, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new FeedHandler(payload, status));

    private sealed class FeedHandler(string payload, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsoluteUri.Should().Be("https://api.nuget.org/v3-flatcontainer/codai/index.json");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeUpdater(ApplicationUpdateInfo update) : IApplicationUpdater
    {
        public string? Installed { get; private set; }
        public Exception? CheckError { get; init; }
        public Exception? InstallError { get; init; }

        public Task<ApplicationUpdateInfo> CheckAsync(CancellationToken cancellationToken = default) =>
            CheckError is null ? Task.FromResult(update) : Task.FromException<ApplicationUpdateInfo>(CheckError);

        public Task InstallAsync(string version, CancellationToken cancellationToken = default)
        {
            if (InstallError is not null)
            {
                return Task.FromException(InstallError);
            }
            Installed = version;
            return Task.CompletedTask;
        }
    }
}
using Codai.Core.Customization;
using Codai.Core.Tests.Fakes;
using FluentAssertions;

namespace Codai.Core.Tests;

/// <summary>
/// Covers discovery of the skills a project keeps in <c>.codai/skills/&lt;name&gt;</c>.
/// </summary>
public class SkillCatalogTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_ancestors_do_not_read_actual_home_skills_when_global_home_is_not_opted_in(bool separateHome)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = Path.Combine(home, "codai-home-boundary", "project");
        var fileSystem = new InMemoryFileSystem().AddDirectory(root)
            .AddFile(Path.Combine(home, ".agents", "skills", "ambient", "SKILL.md"), "Must not become project guidance.");

        var configuredHome = separateHome ? Path.Combine(Path.GetTempPath(), "codai-separate-home") : null;
        (await new SkillCatalog(fileSystem, configuredHome is null ? null : Path.Combine(configuredHome, ".codai"), configuredHome)
            .LoadAllAsync(root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Referenced_files_reveal_nested_skills_without_resetting_workspace_checks()
    {
        var fileSystem = Project().AddFile(SkillPath("review"), "Root review.")
            .AddFile(Path.Combine(Root, "src", ".claude", "skills", "review", "SKILL.md"), "Nested review.")
            .AddFile(Path.Combine(Root, "src", ".claude", "skills", "testing", "SKILL.md"), "Nested tests.");
        var catalog = new SkillCatalog(fileSystem);
        await catalog.SaveEnabledAsync(Root, [], default);

        var active = await catalog.LoadForFilesAsync(Root, ["src/File.cs"]);

        active.Select(skill => skill.Name).Should().Equal("testing");
        var discovered = await catalog.LoadAllAsync(Root);
        discovered.Single(skill => skill.Name == "review").Body.Should().Be("Nested review.");
        discovered.Single(skill => skill.Name == "review").IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Invocation_metadata_does_not_replace_saved_activation_choices()
    {
        var fileSystem = Project()
            .AddFile(SkillPath("manual"), "---\ndisable-model-invocation: true\npaths:\n  - 'src/**/*.cs'\n---\nManual instructions.")
            .AddFile(SkillPath("automatic"), "---\nuser-invocable: false\n---\nAutomatic instructions.");
        var catalog = new SkillCatalog(fileSystem);

        var all = await catalog.LoadAllAsync(Root);
        all.Single(skill => skill.Name == "manual").IsEnabled.Should().BeTrue();
        all.Single(skill => skill.Name == "manual").DisableModelInvocation.Should().BeTrue();
        all.Single(skill => skill.Name == "manual").Paths.Should().Equal("src/**/*.cs");
        all.Single(skill => skill.Name == "automatic").UserInvocable.Should().BeFalse();
        (await catalog.LoadAsync(Root)).Select(skill => skill.Name).Should().Equal("automatic");
    }

    [Fact]
    public async Task Discovery_metadata_loads_explicit_repositories_and_skill_roots()
    {
        var extra = Path.Combine(Root, "shared");
        var fileSystem = Project().AddFile(Path.Combine(Root, ".codai.json"),
            "{\"additionalDirectories\":[\"shared\"],\"skillDirectories\":[\"custom-skills\"]}")
            .AddFile(Path.Combine(extra, ".claude", "skills", "review", "SKILL.md"), "Explicit shared review.")
            .AddFile(Path.Combine(Root, "custom-skills", "audit", "SKILL.md"), "Custom audit.")
            .AddFile(SkillPath("review"), "Project review.");

        var skills = await new SkillCatalog(fileSystem).LoadAllAsync(Root);

        skills.Select(skill => skill.Name).Should().Equal("audit", "review");
        skills.Single(skill => skill.Name == "review").Body.Should().Be("Project review.");
    }

    [Fact]
    public async Task Invalid_discovery_metadata_is_rejected_without_loading_extra_roots()
    {
        var fileSystem = Project().AddFile(Path.Combine(Root, ".codai.json"), "{invalid");
        Func<Task> load = () => new SkillCatalog(fileSystem).LoadAllAsync(Root);
        await load.Should().ThrowAsync<IOException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ancestor_skills_stop_at_git_boundary_and_nearest_definition_wins(bool worktree)
    {
        var nested = Path.Combine(Root, "src", "feature");
        var fileSystem = Project().AddDirectory(nested)
            .AddFile(SkillPath("review"), "Repository review.")
            .AddFile(Path.Combine(nested, ".codai", "skills", "review", "SKILL.md"), "Nearest review.")
            .AddFile(Path.Combine(Root, ".claude", "skills", "audit", "SKILL.md"), "Ancestor audit.")
            .AddFile(Path.Combine(Path.GetDirectoryName(Root)!, ".codai", "skills", "outside", "SKILL.md"), "Outside repository.");
        if (worktree)
        {
            fileSystem.AddFile(Path.Combine(Root, ".git"), "gitdir: ../main/.git/worktrees/feature");
        }
        else
        {
            fileSystem.AddDirectory(Path.Combine(Root, ".git"));
        }

        var skills = await new SkillCatalog(fileSystem).LoadAllAsync(nested);

        skills.Select(skill => skill.Name).Should().Equal("audit", "review");
        skills.Single(skill => skill.Name == "review").Body.Should().Be("Nearest review.");
    }

    [Theory]
    [InlineData(">")]
    [InlineData(">-")]
    [InlineData("|")]
    [InlineData("|-")]
    public async Task Shared_skill_multiline_description_appears_in_management_and_discovery(string marker)
    {
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-multiline-skill"));
        var global = Path.Combine(home, ".codai");
        var fileSystem = Project().AddFile(Path.Combine(home, ".agents", "skills", "caveman", "SKILL.md"),
            $"---\nname: caveman\ndescription: {marker}\n  Ultra-compressed communication mode.\n  Keeps full technical accuracy.\n---\nAll technical substance stays.");
        var catalog = new SkillCatalog(fileSystem, global);

        foreach (var skills in new[] { await catalog.LoadAllAsync(Root), await catalog.LoadAsync(Root) })
        {
            var skill = skills.Should().ContainSingle().Subject;
            skill.Description.Should().Be("Ultra-compressed communication mode. Keeps full technical accuracy.");
            skill.Body.Should().Be("All technical substance stays.");
            skill.IsGlobal.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Management_lists_disabled_skills_and_persists_multiple_enabled_choices()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-skill-selection-global"));
        var settings = Path.Combine(Root, ".codai", "skills.json");
        var userSettings = Path.Combine(global, ".codai.json");
        var fileSystem = Project().AddFile(SkillPath("audit"), "Audit changes.")
            .AddFile(SkillPath("testing"), "Run tests.")
            .AddFile(Path.Combine(global, "skills", "global-skill", "SKILL.md"), "Global skill.")
            .AddFile(Path.Combine(global, "skills.json"), "{\"disabled\":[\"global-skill\"]}")
            .AddFile(settings, "{\"disabled\":[\"testing\",\"missing-skill\"],\"custom\":true}")
            .AddFile(userSettings, "{\"custom\":{\"keep\":\"user\"}}");
        var catalog = new SkillCatalog(fileSystem, global);

        var before = await catalog.LoadAllAsync(Root);
        before.Should().HaveCount(3);
        before.Single(skill => skill.Name == "testing").IsEnabled.Should().BeFalse();
        before.Single(skill => skill.Name == "global-skill").IsEnabled.Should().BeFalse();

        await catalog.SaveEnabledAsync(Root, ["testing", "global-skill"]);

        var reopened = new SkillCatalog(fileSystem, global);
        (await reopened.LoadAsync(Root)).Select(skill => skill.Name).Should().Equal("global-skill", "testing");
        (await reopened.FindAsync(Root, "audit")).Should().BeNull();
        (await reopened.LoadAllAsync(Root)).Single(skill => skill.Name == "audit").IsEnabled.Should().BeFalse();
        fileSystem.FileExists(settings).Should().BeFalse();
        fileSystem.FileExists(Path.Combine(global, "skills.json")).Should().BeFalse();
        using var document = System.Text.Json.JsonDocument.Parse(await fileSystem.ReadAllTextAsync(userSettings));
        document.RootElement.GetProperty("custom").GetProperty("keep").GetString().Should().Be("user");
        document.RootElement.TryGetProperty("customization", out _).Should().BeFalse();
        using var saved = System.Text.Json.JsonDocument.Parse(await fileSystem.ReadAllTextAsync(UserCustomizationSettings.GetFilePath(global)));
        var state = saved.RootElement.GetProperty("skills")
            .GetProperty(UserCustomizationSettings.ProjectKey(fileSystem, Root));
        state.GetProperty("custom").GetBoolean().Should().BeTrue();
        state.GetProperty("disabled").EnumerateArray().Select(value => value.GetString()).Should()
            .Contain("missing-skill").And.Contain("audit");
    }

    [Fact]
    public async Task User_selections_migrate_to_private_state_without_copying_runtime_configuration()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-private-selection-home", ".codai"));
        var settings = Path.Combine(global, ".codai.json");
        var stateFile = Path.Combine(global, "state", "customization.json");
        var fileSystem = Project().AddFile(SkillPath("audit"), "Audit changes.")
                .AddFile(SkillPath("testing"), "Run tests.");
        var key = UserCustomizationSettings.ProjectKey(fileSystem, Root);
        fileSystem.AddFile(settings, $$$$"""
                        {
                            "theme": "graphite",
                            "ai_provider_config": {"api_key": "do-not-copy"},
                            "customization": {
                                "mcp": {"{{{{key}}}}": {"docs": {"fingerprint": "{{{{new string('A', 64)}}}}", "enabled": true}}},
                                "skills": {"{{{{key}}}}": {"enabled": ["testing"], "disabled": ["audit"]}},
                                "skillDefaults": {"disabled": ["unknown-global-skill"]}
                            }
                        }
                        """);

        (await new SkillCatalog(fileSystem, global).LoadAsync(Root)).Should().ContainSingle().Which.Name.Should().Be("testing");

        using var savedSettings = System.Text.Json.JsonDocument.Parse(await fileSystem.ReadAllTextAsync(settings));
        savedSettings.RootElement.TryGetProperty("customization", out _).Should().BeFalse();
        savedSettings.RootElement.GetProperty("theme").GetString().Should().Be("graphite");
        savedSettings.RootElement.GetProperty("ai_provider_config").GetProperty("api_key").GetString().Should().Be("do-not-copy");
        using var state = System.Text.Json.JsonDocument.Parse(await fileSystem.ReadAllTextAsync(stateFile));
        state.RootElement.GetProperty("mcp").GetProperty(key).GetProperty("docs").GetProperty("enabled").GetBoolean().Should().BeTrue();
        state.RootElement.GetProperty("skills").GetProperty(key).GetProperty("disabled")[0].GetString().Should().Be("audit");
        state.RootElement.GetProperty("skillDefaults").GetProperty("disabled")[0].GetString().Should().Be("unknown-global-skill");
        (await fileSystem.ReadAllTextAsync(stateFile)).Should().NotContain("do-not-copy").And.NotContain("graphite");
    }

    [Fact]
    public async Task Private_state_wins_conflicts_without_combining_mcp_consent_or_skill_choices()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-private-selection-conflicts", ".codai"));
        var settings = Path.Combine(global, ".codai.json");
        var stateFile = UserCustomizationSettings.GetFilePath(global);
        var fileSystem = Project();
        fileSystem.AddFile(settings, $$$$"""
            {"customization": {
              "other": {"keep": true},
              "mcp": {"project": {
                "docs": {"fingerprint": "{{{{new string('A', 64)}}}}", "enabled": true},
                "search": {"fingerprint": "{{{{new string('C', 64)}}}}", "enabled": true}
              }},
              "skills": {"project": {"enabled": ["old"], "disabled": ["new"]}},
              "skillDefaults": {"disabled": ["old"]}
            }}
            """);
        fileSystem.AddFile(stateFile, $$$$"""
            {
              "mcp": {"project": {"docs": {"fingerprint": "{{{{new string('B', 64)}}}}", "enabled": false}}},
              "skills": {"project": {"enabled": ["new"], "disabled": ["old"]}},
              "skillDefaults": {"disabled": ["new"]}
            }
            """);

        var state = await UserCustomizationSettings.ReadAsync(fileSystem, global);

        state["mcp"]!["project"]!["docs"]!["fingerprint"]!.GetValue<string>().Should().Be(new string('B', 64));
        state["mcp"]!["project"]!["docs"]!["enabled"]!.GetValue<bool>().Should().BeFalse();
        state["mcp"]!["project"]!["search"]!["enabled"]!.GetValue<bool>().Should().BeTrue();
        state["skills"]!["project"]!["enabled"]![0]!.GetValue<string>().Should().Be("new");
        state["skills"]!["project"]!["disabled"]![0]!.GetValue<string>().Should().Be("old");
        state["skillDefaults"]!["disabled"]![0]!.GetValue<string>().Should().Be("new");
        var remaining = System.Text.Json.Nodes.JsonNode.Parse(await fileSystem.ReadAllTextAsync(settings))!["customization"]!.AsObject();
        remaining.Should().ContainSingle().Which.Key.Should().Be("other");
        remaining["other"]!["keep"]!.GetValue<bool>().Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_migration_preserves_source_and_retries_without_losing_state(bool cleanupFails)
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-private-selection-failure", ".codai"));
        var settings = Path.Combine(global, ".codai.json");
        var stateFile = UserCustomizationSettings.GetFilePath(global);
        const string original = "{\"theme\":\"graphite\",\"customization\":{\"skills\":{\"project\":{\"disabled\":[\"audit\"]}}}}";
        var fileSystem = Project().AddFile(settings, original);
        fileSystem.AtomicWriteFailurePath = cleanupFails ? settings : stateFile;

        Func<Task> migrate = () => UserCustomizationSettings.ReadAsync(fileSystem, global);
        await migrate.Should().ThrowAsync<IOException>();

        (await fileSystem.ReadAllTextAsync(settings)).Should().Be(original);
        fileSystem.FileExists(stateFile).Should().Be(cleanupFails);
        fileSystem.AtomicWriteFailurePath = null;
        var state = await UserCustomizationSettings.ReadAsync(fileSystem, global);
        state["skills"]!["project"]!["disabled"]![0]!.GetValue<string>().Should().Be("audit");
        (await fileSystem.ReadAllTextAsync(settings)).Should().NotContain("customization").And.Contain("graphite");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Linked_selection_files_cannot_launder_workspace_owned_consent(bool linkedState)
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-private-selection-links", ".codai"));
        var settings = Path.Combine(global, ".codai.json");
        var stateFile = UserCustomizationSettings.GetFilePath(global);
        var original = System.Text.Json.JsonSerializer.Serialize(new
        {
            customization = new
            {
                mcp = new { project = new { docs = new { fingerprint = new string('A', 64), enabled = true } } },
                skills = new { project = new { disabled = new[] { "audit" } } },
            },
        });
        var fileSystem = Project().AddFile(settings, original);
        fileSystem.CanonicalPaths[InMemoryFileSystem.Normalize(linkedState ? stateFile : settings)] = Path.Combine(Root, "untrusted.json");

        Func<Task> migrate = () => new SkillCatalog(fileSystem, global).LoadAllAsync(Root);
        await migrate.Should().ThrowAsync<IOException>().WithMessage("*canonical user directory*");

        (await fileSystem.ReadAllTextAsync(settings)).Should().Be(original);
        fileSystem.FileExists(stateFile).Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"mcp\":{\"project\":{\"docs\":{\"enabled\":true}}}}")]
    [InlineData("{\"skills\":{\"project\":{\"disabled\":\"audit\"}}}")]
    [InlineData("{\"skillDefaults\":[]}")]
    public async Task Malformed_legacy_selection_is_not_removed_or_saved_as_private_state(string selection)
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-private-selection-malformed", ".codai"));
        var settings = Path.Combine(global, ".codai.json");
        var original = "{\"customization\":" + selection + "}";
        var fileSystem = Project().AddFile(settings, original);

        Func<Task> migrate = () => UserCustomizationSettings.ReadAsync(fileSystem, global);
        await migrate.Should().ThrowAsync<IOException>();

        (await fileSystem.ReadAllTextAsync(settings)).Should().Be(original);
        fileSystem.FileExists(UserCustomizationSettings.GetFilePath(global)).Should().BeFalse();
    }

    [Fact]
    public async Task Cancelled_migration_keeps_original_settings_and_creates_no_state()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-private-selection-cancelled", ".codai"));
        var settings = Path.Combine(global, ".codai.json");
        const string original = "{\"customization\":{\"skills\":{}}}";
        var fileSystem = Project().AddFile(settings, original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> migrate = () => UserCustomizationSettings.ReadAsync(fileSystem, global, cancellation.Token);
        await migrate.Should().ThrowAsync<OperationCanceledException>();

        (await fileSystem.ReadAllTextAsync(settings)).Should().Be(original);
        fileSystem.FileExists(UserCustomizationSettings.GetFilePath(global)).Should().BeFalse();
    }

    [Fact]
    public async Task Skill_selection_does_not_overwrite_malformed_settings_or_unknown_names()
    {
        var settings = Path.Combine(Root, ".codai", "skills.json");
        var fileSystem = Project().AddFile(SkillPath("audit"), "Audit changes.").AddFile(settings, "{invalid");
        var catalog = new SkillCatalog(fileSystem);

        Func<Task> save = () => catalog.SaveEnabledAsync(Root, []);
        await save.Should().ThrowAsync<IOException>();
        (await fileSystem.ReadAllTextAsync(settings)).Should().Be("{invalid");

        Func<Task> unknown = () => catalog.SaveEnabledAsync(Root, ["missing"]);
        await unknown.Should().ThrowAsync<ArgumentException>();
        (await fileSystem.ReadAllTextAsync(settings)).Should().Be("{invalid");
    }

    [Theory]
    [InlineData(".yml")]
    [InlineData(".yaml")]
    public async Task Private_selection_can_save_without_shadowing_yaml_user_settings(string extension)
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-skill-yaml-settings"));
        var file = Path.Combine(global, ".codai" + extension);
        var fileSystem = Project().AddFile(SkillPath("audit"), "Audit changes.").AddFile(file, "theme: graphite");
        var catalog = new SkillCatalog(fileSystem, global);

        await catalog.SaveEnabledAsync(Root, []);

        (await fileSystem.ReadAllTextAsync(file)).Should().Be("theme: graphite");
        fileSystem.FileExists(Path.Combine(global, ".codai.json")).Should().BeFalse();
        fileSystem.FileExists(Path.Combine(global, "state", "customization.json")).Should().BeTrue();
        (await new SkillCatalog(fileSystem, global).LoadAllAsync(Root)).Single().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Unchanged_or_cancelled_selection_does_not_create_skill_settings()
    {
        var fileSystem = Project().AddFile(SkillPath("audit"), "Audit changes.");
        var catalog = new SkillCatalog(fileSystem);
        await catalog.SaveEnabledAsync(Root, ["audit"]);
        fileSystem.FileExists(Path.Combine(Root, ".codai", "skills.json")).Should().BeFalse();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> save = () => catalog.SaveEnabledAsync(Root, [], cancellation.Token);
        await save.Should().ThrowAsync<OperationCanceledException>();
        fileSystem.FileExists(Path.Combine(Root, ".codai", "skills.json")).Should().BeFalse();
    }

    [Fact]
    public async Task Shared_project_roots_share_total_limit_and_reserve_codai_entries()
    {
        var fileSystem = Project().AddFile(SkillPath("z-project"), "Project skill.");
        for (var index = 0; index < SkillCatalog.MaxSkills; index++)
        {
            fileSystem.AddFile(Path.Combine(Root, ".agents", "skills", $"shared-{index:D3}", "SKILL.md"), "Shared skill.");
        }

        var skills = await new SkillCatalog(fileSystem).LoadAsync(Root);

        skills.Should().HaveCount(SkillCatalog.MaxSkills);
        skills.Should().Contain(skill => skill.Name == "z-project" && !skill.IsGlobal);
        skills.Should().OnlyContain(skill => !skill.IsGlobal);
    }

    [Fact]
    public async Task Explicit_shared_home_remains_independent_of_selected_configuration_directory()
    {
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-shared-home"));
        var selectedConfig = Path.Combine(home, "isolated-config");
        var fileSystem = Project().AddFile(Path.Combine(home, ".agents", "skills", "caveman", "SKILL.md"), "Concise output.");

        (await new SkillCatalog(fileSystem, selectedConfig).LoadAsync(Root)).Should().BeEmpty();
        (await new SkillCatalog(fileSystem, selectedConfig, home).LoadAsync(Root)).Should().ContainSingle()
            .Which.Name.Should().Be("caveman");
        (await new SkillCatalog(fileSystem, userHomeDirectory: home).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Shared_global_agent_skills_are_discovered_with_project_precedence()
    {
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-shared-skills"));
        var global = Path.Combine(home, ".codai");
        var fileSystem = Project().AddFile(SkillPath("testing"), "Project skill.")
            .AddFile(Path.Combine(home, ".agents", "skills", "testing", "SKILL.md"), "Shared fallback.")
            .AddFile(Path.Combine(home, ".agents", "skills", "caveman", "SKILL.md"), "---\ndescription: Concise answers\n---\nBe brief.")
            .AddFile(Path.Combine(home, ".claude", "skills", "review", "SKILL.md"), "Review carefully.");
        var catalog = new SkillCatalog(fileSystem, global);

        var skills = await catalog.LoadAsync(Root);

        skills.Select(skill => skill.Name).Should().Equal("caveman", "review", "testing");
        skills.Single(skill => skill.Name == "caveman").IsGlobal.Should().BeTrue();
        (await catalog.FindAsync(Root, "testing"))!.Body.Should().Be("Project skill.");
        (await catalog.FindAsync(Root, "caveman"))!.Body.Should().Be("Be brief.");
        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().ContainSingle();
    }

    [Theory]
    [InlineData("project")]
    [InlineData("ancestor")]
    [InlineData("nested")]
    [InlineData("additional")]
    public async Task Github_skill_roots_follow_project_discovery_and_native_precedence(string scope)
    {
        var nested = Path.Combine(Root, "src", "package");
        var extra = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-github-extra-skills"));
        var directory = scope == "nested" ? nested : scope == "additional" ? extra : Root;
        var start = scope == "ancestor" ? nested : Root;
        var fileSystem = Project().AddDirectory(Path.Combine(Root, ".git")).AddDirectory(start)
            .AddFile(Path.Combine(directory, ".github", "skills", "review", "SKILL.md"), "Review from GitHub skill root.")
            .AddFile(Path.Combine(directory, ".github", "skills", "shared", "SKILL.md"), "Shared fallback.")
            .AddFile(Path.Combine(directory, ".codai", "skills", "shared", "SKILL.md"), "Native skill wins.");
        if (scope == "additional")
        {
            fileSystem.AddFile(Path.Combine(Root, ".codai.json"),
                System.Text.Json.JsonSerializer.Serialize(new { additionalDirectories = new[] { extra } }));
        }
        var catalog = new SkillCatalog(fileSystem);
        if (scope == "nested")
        {
            (await catalog.LoadAllAsync(Root)).Should().BeEmpty();
            fileSystem.AddFile(Path.Combine(nested, "Code.cs"), "class Code {}");
            await catalog.LoadForFilesAsync(Root, [Path.Combine("src", "package", "Code.cs")]);
        }

        var skills = await catalog.LoadAllAsync(start);

        skills.Select(skill => skill.Name).Should().Equal("review", "shared");
        skills.Should().OnlyContain(skill => !skill.IsGlobal);
        skills.Single(skill => skill.Name == "review").Body.Should().Be("Review from GitHub skill root.");
        skills.Single(skill => skill.Name == "shared").Body.Should().Be("Native skill wins.");
    }

    [Fact]
    public async Task Copilot_personal_skills_preserve_global_opt_in_precedence_and_project_checks()
    {
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-copilot-personal-skills"));
        var global = Path.Combine(home, "isolated-config");
        var fileSystem = Project().AddFile(SkillPath("audit"), "Project audit.")
            .AddFile(Path.Combine(global, "skills", "shared", "SKILL.md"), "Native global skill.")
            .AddFile(Path.Combine(home, ".copilot", "skills", "shared", "SKILL.md"), "Personal fallback.")
            .AddFile(Path.Combine(home, ".copilot", "skills", "audit", "SKILL.md"), "Global audit fallback.")
            .AddFile(Path.Combine(home, ".copilot", "skills", "personal", "SKILL.md"), "Personal workflow.");
        var catalog = new SkillCatalog(fileSystem, global, home);

        var discovered = await catalog.LoadAllAsync(Root);

        discovered.Select(skill => skill.Name).Should().Equal("audit", "personal", "shared");
        discovered.Single(skill => skill.Name == "audit").Body.Should().Be("Project audit.");
        discovered.Single(skill => skill.Name == "shared").Body.Should().Be("Native global skill.");
        discovered.Single(skill => skill.Name == "personal").IsGlobal.Should().BeTrue();
        (await new SkillCatalog(fileSystem, userHomeDirectory: home).LoadAllAsync(Root)).Should().ContainSingle();
        (await new SkillCatalog(fileSystem, global).LoadAllAsync(Root)).Select(skill => skill.Name).Should().Equal("audit", "shared");
        (await new SkillCatalog(fileSystem, Path.Combine(home, ".codai")).FindAsync(Root, "personal"))!.Body.Should().Be("Personal workflow.");

        await catalog.SaveEnabledAsync(Root, ["audit", "shared"]);

        var reopened = new SkillCatalog(fileSystem, global, home);
        (await reopened.LoadAllAsync(Root)).Single(skill => skill.Name == "personal").IsEnabled.Should().BeFalse();
        (await reopened.FindAsync(Root, "personal")).Should().BeNull();
    }

    [Fact]
    public async Task Identical_global_and_project_skill_roots_keep_project_origin()
    {
        var fileSystem = Project().AddFile(SkillPath("review"), "Project skill.");
        var catalog = new SkillCatalog(fileSystem, Path.Combine(Root, ".codai", "."));

        var skills = await catalog.LoadAsync(Root);

        skills.Should().ContainSingle().Which.IsGlobal.Should().BeFalse();
        skills[0].SourcePath.Should().Be(".codai/skills/review/SKILL.md");
        fileSystem.ReadPaths.Should().ContainSingle().Which.Should()
            .Be(InMemoryFileSystem.Normalize(SkillPath("review")));
        (await catalog.FindAsync(Root, "review"))!.Body.Should().Be("Project skill.");
    }

    [Fact]
    public async Task Empty_project_skill_does_not_hide_valid_global_fallback()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-skills"));
        var fileSystem = Project().AddFile(SkillPath("review"), "---\ndescription: Empty\n---\n")
            .AddFile(Path.Combine(global, "skills", "review", "SKILL.md"), "Global skill.");
        var catalog = new SkillCatalog(fileSystem, global);

        var skills = await catalog.LoadAsync(Root);

        skills.Should().ContainSingle().Which.IsGlobal.Should().BeTrue();
        (await catalog.FindAsync(Root, "review"))!.Body.Should().Be("Global skill.");
    }

    [Fact]
    public async Task Combined_skill_limit_reserves_project_entries_and_cancellation_is_not_swallowed()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-skills"));
        var fileSystem = Project().AddFile(SkillPath("project"), "Project.");
        for (var index = 0; index < SkillCatalog.MaxSkills; index++)
        {
            fileSystem.AddFile(Path.Combine(global, "skills", $"global-{index:D3}", "SKILL.md"), "Global.");
        }

        var catalog = new SkillCatalog(fileSystem, global);
        var skills = await catalog.LoadAsync(Root);
        skills.Should().HaveCount(SkillCatalog.MaxSkills);
        skills.Should().Contain(skill => skill.Name == "project" && !skill.IsGlobal);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> load = () => catalog.LoadAsync(Root, cancellation.Token);
        await load.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Global_skills_are_discovered_but_project_names_override_case_insensitively()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-skills"));
        var fileSystem = Project().AddFile(SkillPath("REVIEW"), "Project skill.")
            .AddFile(Path.Combine(global, "skills", "review", "SKILL.md"), "Shadowed skill.")
            .AddFile(Path.Combine(global, "skills", "ship", "SKILL.md"), "Global skill.");
        var catalog = new SkillCatalog(fileSystem, global);

        var skills = await catalog.LoadAsync(Root);

        skills.Select(skill => skill.Name).Should().Equal("review", "ship");
        skills[0].IsGlobal.Should().BeFalse();
        skills[1].IsGlobal.Should().BeTrue();
        (await catalog.FindAsync(Root, "REVIEW"))!.Body.Should().Be("Project skill.");
        (await catalog.FindAsync(Root, "ship"))!.SourcePath.Should()
            .Be(Path.Combine(global, "skills", "ship", "SKILL.md").Replace('\\', '/'));
    }

    [Fact]
    public async Task Disabled_project_and_global_skills_are_not_advertised()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-skills"));
        var fileSystem = Project()
            .AddFile(SkillPath("review"), "Review.")
            .AddFile(SkillPath("ship"), "Ship.")
            .AddFile(Path.Combine(global, "skills", "deploy", "SKILL.md"), "Deploy.")
            .AddFile(Path.Combine(Root, ".codai", "skills.json"), "{\"disabled\":[\"REVIEW\"]}")
            .AddFile(Path.Combine(global, "skills.json"), "{\"disabled\":[\"deploy\"]}");

        var skills = await new SkillCatalog(fileSystem, global).LoadAsync(Root);

        skills.Select(skill => skill.Name).Should().Equal("ship");
    }

    [Fact]
    public async Task Invalid_skill_settings_do_not_hide_valid_skills()
    {
        var fileSystem = Project()
            .AddFile(SkillPath("review"), "Review.")
            .AddFile(Path.Combine(Root, ".codai", "skills.json"), "not json");

        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().ContainSingle()
            .Which.Name.Should().Be("review");
    }

    [Fact]
    public async Task Global_skill_fallback_works_without_project_directory_and_requires_opt_in()
    {
        var global = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codai-global-skills"));
        var fileSystem = Project().AddFile(Path.Combine(global, "skills", "review", "SKILL.md"), "Global.");

        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
        (await new SkillCatalog(fileSystem, global).LoadAsync(Root)).Should().ContainSingle();
    }

    private const string Root = @"C:\project";

    private static InMemoryFileSystem Project() => new InMemoryFileSystem().AddDirectory(Root);

    private static string SkillPath(string folder) =>
        Path.Combine(Root, ".codai", "skills", folder, "SKILL.md");

    [Fact]
    public async Task A_project_without_the_directory_has_no_skills()
    {
        var catalog = new SkillCatalog(Project());

        (await catalog.LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_folder_names_the_skill_and_the_file_holds_the_instructions()
    {
        var fileSystem = Project().AddFile(
            SkillPath("Api-Style"),
            "---\ndescription: How this project writes endpoints\n---\nAlways return ProblemDetails.");

        var skill = (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().ContainSingle().Subject;

        skill.Name.Should().Be("api-style");
        skill.Description.Should().Be("How this project writes endpoints");
        skill.Body.Should().Be("Always return ProblemDetails.");
        skill.SourcePath.Should().EndWith("SKILL.md");
    }

    [Fact]
    public async Task A_folder_without_a_skill_file_is_not_a_skill()
    {
        var fileSystem = Project().AddFile(
            Path.Combine(Root, ".codai", "skills", "not-a-skill", "readme.md"),
            "Nothing here.");

        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_skill_file_with_no_body_is_not_a_skill()
    {
        var fileSystem = Project().AddFile(SkillPath("empty"), "---\ndescription: x\n---\n");

        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_description_the_first_line_of_the_body_is_used()
    {
        var fileSystem = Project().AddFile(SkillPath("testing"), "# Write tests this way\n\nDetails.");

        var skill = (await new SkillCatalog(fileSystem).LoadAsync(Root)).Single();

        skill.Description.Should().Be("Write tests this way");
    }

    [Fact]
    public async Task Skills_are_sorted_by_name()
    {
        var fileSystem = Project()
            .AddFile(SkillPath("zebra"), "Body.")
            .AddFile(SkillPath("alpha"), "Body.");

        (await new SkillCatalog(fileSystem).LoadAsync(Root))
            .Select(skill => skill.Name)
            .Should().Equal("alpha", "zebra");
    }

    [Fact]
    public async Task A_folder_name_that_could_not_be_called_by_the_tool_is_ignored()
    {
        // read_skill resolves by name, so a folder whose name cannot be a name is not
        // exposed at all rather than being advertised and then unfetchable.
        var fileSystem = Project().AddFile(SkillPath("has space"), "Body.");

        (await new SkillCatalog(fileSystem).LoadAsync(Root)).Should().BeEmpty();
    }

    [Fact]
    public async Task Find_is_case_insensitive_but_refuses_a_name_it_would_never_resolve()
    {
        var fileSystem = Project().AddFile(SkillPath("testing"), "Body.");
        var catalog = new SkillCatalog(fileSystem);

        (await catalog.FindAsync(Root, "TESTING")).Should().NotBeNull();
        (await catalog.FindAsync(Root, "not a name")).Should().BeNull();
        (await catalog.FindAsync(Root, "nope")).Should().BeNull();
    }
}

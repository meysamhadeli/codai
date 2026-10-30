# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Kept the active theme unchanged until Save succeeded. Cancel, Escape, and failed
  saves no longer applied an unsaved theme to terminal controls.
- Isolated model-price lookup by provider instead of borrowing another provider's
  unqualified model price. Kept unknown Azure deployments and unpriced models from
  inheriting unrelated tariffs, and preserved context/capability metadata when
  applying reviewed rate overrides.
- Reused saved provider credentials or account references and model selections on bare
  `codai` startup instead of reopening connection setup every time. Kept explicit
  `codai auth`, first-time setup, environment overrides, and selected configuration
  directories working independently.
- Followed Anthropic and Gemini model-list pagination and removed the silent
  2,000-entry truncation. Bounded requests and rejected repeated or missing cursors
  instead of treating partial catalogs as complete. Filtered explicitly retired,
  non-chat, and OpenRouter batch-only/non-text models where metadata allowed.

### Changed

- Reorganized documentation into task-focused guides for terminal use, tools,
  permissions, recovery, providers, configuration, and troubleshooting. Refined the
  landing page and navigation, expanded integration guides, and preserved the
  existing Agent Mode route and section links.
- Renamed Appearance to Theme in settings and setup, and standardized configuration,
  provider/account connection, and integration confirmation actions as Save and Cancel.
- Expanded the theme palette to ten selectable themes — Graphite, Ocean, Azure, Violet,
  Magenta, Forest, Mint, Ember, Sunset, and Coral — from a shared registry, and aligned
  the default theme across runtime, configuration, and inspection.
- Saved fresh first-run defaults under `~/.codai` when no project configuration
  existed, and automatically reused them across otherwise unconfigured projects.
  Preserved explicit configuration scopes, project precedence, and environment
  and command-line overrides.
- Reviewed USD text-token rates on October 30, 2026 and covered every bundled default
  and fallback model choice. Kept OpenRouter rates distinct from direct-provider
  rates, corrected current `o3` pricing, and documented historical-model, regional,
  promotional, cache, fee, and unknown-price estimation limits.
- Refreshed provider defaults and expanded hosted-provider fallback model suggestions.
  Preferred live catalogs and labeled saved models and unverified suggestions
  separately. Kept Azure deployment names manual and Ollama installed models
  authoritative; documented current transport limitations and preserved explicit
  saved model selections.

### Added

- Added theme-first onboarding for an unconfigured bare `codai` invocation. Saved
  appearance before provider/account and model setup, opened chat immediately after
  connection saving, and skipped completed setup on later runs. Resumed interrupted
  setup without repeating a saved appearance; kept explicit `codai auth` connection-only.
- Added provider-qualified pricing overrides with source URLs and a review date,
  plus prompt-length tiers that priced all request tokens at the applicable rate.
  Exposed tier metadata through `ModelPricing.InputTokenTiers` and reviewed overrides
  through `PricingDocument.CurrentModels`.
- Added a Nextra documentation experience with Codai-specific light/dark wordmarks,
  native cards and steps, and a first-session quickstart. Accents, wordmarks, and favicon
  matched the app's default blue (`#00AFFF`). Static Pagefind search worked without a
  hosted documentation account.

- Validated README and deployment-workflow changes on documentation pull requests,
  in addition to documentation content. Patched the documentation build's transitive
  XML parser without downgrading Nextra.

- Added a cyan terminal-style SVG wordmark as the README logo.

- **Compact, clickable terminal steps.** Thinking, reasoning, and tool actions started
  collapsed and opened individually by clicking their headers. Ctrl+O toggled the
  selected step, with the latest step as its fallback. Edit headers displayed actual
  added/removed line counts; expanded steps showed bounded per-file diffs or command
  output. Thinking details exposed status and timing only. Built-in file tools returned
  structured `ToolResult.FileChanges` summaries, including edits that landed before a
  later patch operation failed. Replacing or deleting existing files through these tools
  required a successful read within the 8 MB editing limit instead of guessing counts.

- **Approval is a panel that shows what will run.** A tool that can change files or run a
  command now asks through a bordered overlay that names the action, prints the exact
  command, path, or patch, and offers three numbered choices: run once, run and stop asking
  for that tool this session, or refuse and say what to do instead. Arrows and Enter work,
  as do `Y`/`A`/`N` and `1`/`2`/`3`, and Escape refuses. Long content keeps its first ten
  lines and reports how many it hid, so the thing being approved is never a clipped
  one-line summary. Redirected, non-ANSI, and non-interactive terminals keep the previous
  yes/no confirmation.

- **Persistent MCP/plugin configuration management.** `codai config mcp|plugin
  enable|disable|remove <name-or-path>` updates only an existing Codai YAML/JSON config
  document through atomic persistence. MCP entries toggle their `enabled` field; plugin
  toggles move paths between `plugins` and `disabled_plugins`; remove deletes only the
  configuration entry. Concrete `.dll` validation, offline inspection, lazy startup and
  tests ensure no plugin binary is installed, loaded, executed or deleted. Full ACP/protocol
  parity remains intentionally unsupported as documented in `docs/content/acp-compatibility.mdx`.

- **Lazy foreground rule activation.** Read/write/edit file targets activate matching
  conditional rules before approval, hooks or I/O. Guidance-only results pause the rest
  of a provider batch until a new model round; calls are not replayed automatically.
  Mode restrictions and independent write consent remain enforced. Canonical project
  paths, per-request state and bounded context prevent scope/state leakage; oversized
  guidance stops execution rather than marking unseen rules reviewed. Shell/prose/
  delegation targets and live rule reload remain unsupported.
- **Global workflows and skills.** CLI/global configuration roots now supply commands
  and skills alongside project catalogs; SDK hosts opt in explicitly. Valid project
  names override globals case-insensitively, with fallback for empty project entries,
  same-root deduplication and project-first 200-entry limits. Workflow expansion, skill
  advertisement, `read_skill` and inventory share origins and precedence. Bodies do not
  grant permissions or consent. Project-first skill collisions intentionally differ from
  Cline's global-first policy. Installation/removal/toggles remain incomplete.
- **Global/project rule precedence.** CLI rules combine `~/.codai/rules` (or the selected
  configuration directory's `rules`) with workspace `.codai/rules`. Valid project rules
  replace globals sharing their relative path; other rules combine with explicit project
  priority and project-first count/render budgets. SDK hosts opt in explicitly. Prompts
  and inspection share the catalog and expose origins without granting permissions or
  consent. Global conditional globs remain project-relative; rule discovery is cancellable
  and bounded. Toggles remain open; foreground lazy activation is described above.
- **Safe configuration inspection.** `codai config [target] [--json]` and interactive
  `:config [target]` report resolved paths/model/policy, mode-aware tools, project rule,
  skill and workflow catalogs, bounded hook manifests and MCP metadata. Offline inspection
  never reads stdin, starts models, loads plugins, launches MCP servers or creates state;
  skipped external-tool discovery is explicit. API keys report presence only, endpoint
  origins omit credentials/path/query/fragment, and MCP/hook executable values are omitted.
  JSON returns one selected-section document; cancellation/timeout preserve `130`/`124`.
  Agent/plugin targets and configuration editing remain incomplete.
- **File-scoped (conditional) project rules.** `.codai/rules/**/*.md` rules load with
  optional `applyTo` front matter naming globs. An unconditional rule reaches every
  request; a rule with globs initially applies when the request references a matching file
  (an `@path` mention or what it expands to). Loading, matching and rendering are bounded
  and share the `glob` syntax instead of re-implementing it. Foreground file tools can
  activate additional matching rules as described above.
- **Bounded multi-file text reads.** `read_file` accepts `path` or up to 20 unique `paths`,
  validating the entire bounded path list before I/O. Offset/limit apply per file; results
  share a 60,000-character budget and stop further reads when exhausted. Per-file failures
  remain visible alongside successful content and fail the batch. Tests cover limits,
  cancellation, skipped I/O, and real JSON CLI/model calls across modes and response styles.
- **Live foreground command output.** Command/build/test tools publish bounded serialized
  stdout/stderr progress before returning final capture. JSON exposes typed stream kinds;
  pipe draining shares the command deadline and retains partial timeout output. Observer
  failure stops the owned command. Composed CLI tests cover consent, ordering, user/run-wide
  cancellation and persisted aborted outcomes. Old runners/observers remain compatible;
  no PTY input, detached-command management or host-crash orphan cleanup is implied.
- **Invocation tool exclusions.** Persisted `disabled_tools` and repeatable `--disable-tool`
  flags filter provider advertisement and executor lookup before approval. CLI restrictions
  add to configuration; shell/delegation and mode gates still narrow availability. Unknown
  names fail cleanly, including JSON CLI runs. Config removal enables tools on the next run;
  this is not a sandbox and does not suppress approved prose edits, hooks or MCP startup.
- **Reviewed Plan/Act execution.** Plan requests persist bounded model proposals and mode.
  `:act` / `--execute-plan` restore matching planning context and request explicit transition
  consent before using the shared Agent-mode execution path. Write/command approvals remain
  independent. Execution links to its source proposal; stale, missing, failed, cleared or
  consumed proposals cannot replay automatically. Streaming/plain and JSON headless paths
  are covered; headless transition defaults to denial.
- **CLI compaction strategies.** `--compaction agentic|basic|off` selects summary-model
  compaction, deterministic digest, or explicit automatic disable. Existing toggles obey
  argument order. Off also prevents provider-overflow history mutation; explicit manual
  `:compact` remains available with the selected strategy.
- **Guarded web retrieval.** Read-only `fetch_web` retrieves public HTTP(S) declared
  text/JSON/XML and statically parsed HTML through patched AngleSharp 1.5.0. URL and
  redirect guards reject private destinations, user-info credentials, nonstandard ports,
  and TLS downgrade; connection callbacks pin validated public DNS results. No cookies,
  proxies, scripts, or subresource fetching. Finite deadlines, decompressed-byte and text
  caps, cancellation, source attribution, and untrusted-content notices bound retrieval.
- **Model-driven clarification.** `ask_question` supports bounded questions, numbered
  choices and free-form input in every foreground mode. A host-neutral handler uses the
  existing cancellable input path; answers are not write/command consent. Missing input
  fails closed with a persisted Failed task. Headless/JSON runs report the question and
  never auto-answer, including under auto-approval. Interaction-required tools are excluded
  from read-only delegated/background investigations.
- **Explicit final submission.** Read-only `submit_task` accepts bounded final text,
  stops remaining tool calls and model rounds, and preserves output and completed task
  state in streaming/plain and interactive/headless sessions. Terminal tool outcomes now
  retain success/failure rather than treating every stop as a provider failure. Consent
  policy and post-tool rejection remain authoritative; next requests start cleanly.
- **Blocking lifecycle hooks.** Global/project/injected `hooks.json` manifests support
  task, prompt, tool, and compaction-application stages. `--hooks-dir` overrides
  `CODAI_HOOKS_DIR`; `--no-hooks` skips all discovery/execution. Manifest preflight, Agent
  mode, shell permission, consent, explicit JSON stdin, finite timeout, bounded output,
  fail-closed responses, and no replay protect execution. JSON runs emit `hook_event`.
  Post-hook failure never promises rollback. Cancellation starts no new commands.
  Async/retry/fail-open/model-plugin policies and management remain incomplete.
- **Run-wide timeout and graceful Ctrl+C.** `--timeout` cancels stdin, interactive waits,
  model work, and owned commands through existing cancellation contracts. Timeout exits
  `124`; caller cancellation exits `130`, retaining foreground cancellation state.
- **System-template override.** `--system` replaces the default template while preserving
  project guidance, mode policy, and the allowed tool set.
- **Isolated configuration and state.** `--config-dir` changes discovery and `--data-dir`
  redirects conversation, foreground/background tasks, and transcripts to per-project
  state directories. CLI flags override `CODAI_CONFIG_DIR`/`CODAI_DATA_DIR`; workspace
  scanning/editing roots and external-file boundaries are unchanged.
- **Structured task output.** `--json` emits source-generated JSON Lines content,
  progress, usage, final and aborted results; errors go to structured stderr. Runtime
  exchange counts, duration, model metadata, and partial output survive terminal failures.
- **Explicit CLI auto-approval.** `--auto-approve true|false` applies one consent policy
  to tool calls and prose changes in interactive and headless runs. Defaults remain
  fail-closed; read-only modes, tool budgets, and path safety cannot be bypassed.
- **One-shot and piped task invocation.** `codai "prompt"` and redirected multiline
  stdin execute one request through `ICodaiSession.RunTaskAsync`, preserving conversation
  and foreground task state. `--cwd` selects the project/config root; `--` preserves
  literal flag-like task text. No-argument invocation now starts an interactive session
  when stdin is not redirected. Headless output separates model text from diagnostics,
  denies interactive approvals, and returns completion/error/cancellation codes
  (`0`/`1`/`130`). A completed request does not imply proposed edits were accepted.
  Explicit approval, JSON output, timeout, and blocking hook controls are described above.
  Remaining lifecycle/plugin parity and other CLI workflows are tracked in the roadmap.
- **Bounded tool-failure continuation.** Executed-tool failures and exceptions allow
  model-directed recovery until a configurable consecutive failure limit (default 3).
  `--retries` overrides `budget.max_consecutive_tool_failures`. The shared model loop
  stops at the limit, foreground failure state is persisted, and sub-agents use the same
  policy. Successful tools reset the count; approvals and cancellation remain separate.
- **Cline-only CLI scope.** The roadmap records the attached Cline CLI's documented
  workflows and outstanding parity work without adding Copilot or Claude Code targets.
- **Agent capability checkpoint.** `AGENT_ROADMAP.md` records implementation status for
  reliable loops, coding workflows, agent execution, session intelligence, and protocol
  quality.
- **Background agent controls.** `:task start`, `:task list`, and `:task cancel` manage
  independent sub-agent investigations with completion reports and cancellation state.
- **Phased task checkpoints.** Foreground requests persist separate `Generate response`
  and `Apply changes` checkpoints, preserving the phase that failed or was cancelled.
- **Durable execution plan steps.** Foreground task records now persist named plan steps
  and link each execution checkpoint to its step for clearer recovery state.
- **Task history.** Completed, failed, and cancelled foreground tasks are archived by ID
  under `.codai/tasks/` and can be inspected with `:task history`.
- **Automatic repair workflow.** `:repair` runs bounded approved test/build diagnostics,
  sends failures back to the model, applies approved repairs, and reruns validation.
- **Structured agent progress events.** Background lifecycle, model streaming, and tool
  activity now flow through a host-neutral Core sink and render in the CLI without coupling
  Core to its UI.
- **Persistent background results.** Background task state and reports are stored under
  `.codai/tasks/background/` and restored when a session starts.
- **Interrupted background-task resume.** Persisted running tasks retain their prompt and
  automatically restart once after a new session initializes.
- **Unified task budgets.** Configurable token, estimated-cost, and tool-call limits now
  apply to the shared foreground agent runtime.
- **Crash-safe task persistence.** Session and task JSON is written through atomic
  replacement, and background records carry leases so active runs are not duplicated
  during restore while expired runs can be reclaimed.
- **Process cancellation cleanup.** Explicit session cancellation now kills the owned
  command process tree before propagating cancellation, preventing orphaned builds and
  tests; focused coverage verifies delayed child work does not continue.
- **Agent tools.** The assistant can now read the project instead of reasoning only over
  the scan summary. Four read-only tools ship: `read_file`, `list_files`, `glob` and
  `grep`, surfaced to the model through `Microsoft.Extensions.AI` function calling.
- **`Codai.Tools`**, a package holding the tool set, the glob matcher and the shared
  project file index.
- **Hand-written tool calling for Anthropic and Gemini**, whose wire formats are not
  OpenAI-compatible: `tool_use` / `tool_result` blocks and streamed `input_json_delta`
  fragments on one side, `functionDeclarations` / `functionCall` / `functionResponse` on
  the other, including filtering out schema keywords Gemini rejects.
- **Tool progress and approval.** `IToolObserver` reports every call to the session, and
  `IToolApprover` gates anything that is not read-only. When no user interface is
  available the defaults stand: progress is dropped and state-changing tools are refused.
- A **tools section in the system prompt** describing what can be called, emitted only
  when tools are actually available, so prompts without tools are unchanged.
- **Project instructions.** A repository that ships `AGENTS.md`, `CLAUDE.md`, `CODAI.md`,
  `.codai/instructions.md` or `.github/copilot-instructions.md` is now followed for the
  whole session. The file is capped at 32 KB and the one that was picked is reported at
  start-up.
- **`@` references in a request.** `@src/Foo.cs`, `@src/` and `@**/*.cs` attach file
  contents to that single request. References resolve through the same file index as the
  tools, cap at 20 files / 256 KB per request, and a reference that matches nothing is
  reported instead of silently dropped.
- **`:compact`** replaces the conversation so far with a summary of it, and
  `PromptExtras` carries the optional instruction and attached-file prompt sections.
- **Session persistence.** Every turn is written to `.codai/sessions/latest.json`, and
  `codai code --continue` resumes that conversation. Losing or corrupting the file is
  treated as "nothing to resume" rather than a failure to start.
- **Agent modes.** `plan` and `agent` decide how much freedom a request has, set
  with `--mode` or changed mid-session with `:mode`. The tool set for a mode is derived
  from `ITool.IsReadOnly` and the prompt guidance from the same mode, so the two cannot
  drift when a tool is added.
- **Writing tools.** `write_file` and `edit_file` are offered in `agent` mode and ask
  before every call. `edit_file` refuses an edit whose search text is not unique, which
  turns an ambiguous change into a message the model can act on rather than a silent
  mis-edit. Both adopt the line endings the file already uses.
- **`run_command`**, opt-in through `AddCodaiTools(allowShell: true)`, which runs a
  command line from the project root through the new `IProcessRunner`. It is never
  registered by default, always asks for approval, never inherits the terminal's standard
  input, and bounds and truncates the captured output. There is no sandbox; the approval
  prompt is the boundary.
- **Automatic compaction.** A conversation that would outgrow the model's context window
  is summarised before the request is sent, using a `ContextWindow` table and a
  `TokenEstimator` that errs high, against a threshold that keeps a reserve for the
  response and the tool definitions. A circuit breaker gives up after three consecutive
  failures rather than spending the session's whole budget retrying.
- **Tool budgets.** `ToolCallGovernor` limits a single request to 60 tool calls and to two
  identical calls, and tells the model to answer with what it has when either is reached.
  Repeat detection compares arguments by sorted property name, so re-emitting the same
  arguments in a different order is still the same call.
- **Project skills.** `.codai/skills/<name>/SKILL.md` files reach the prompt as a name and
  a description; the body is fetched by the new `read_skill` tool when the model decides
  the skill applies.
- **Project commands.** `.codai/commands/<name>.md` files become `:commands` in the
  session. `$ARGUMENTS` and `$1`–`$9` are substituted, and a colon-prefixed line that
  matches neither a session command nor a project command is reported instead of being
  sent to the model as if it were a question.
- **`:compact <focus>`** weights the summary towards what the user names, so the summary
  that survives a long session keeps what the next request needs.
- **`--no-shell`** and **`--no-auto-compact`** turn the command tool and automatic
  compaction off for a session, without giving up the rest of the agent.
- **Sub-agents.** The `task` tool hands an investigation to a nested agent loop with a
  context window of its own: it explores with read-only tools and reports back in a few
  hundred words, so the files it read never accumulate in the parent conversation. It is
  never offered the tool that starts one, so a run cannot nest; it gets its own reduced
  tool budget; and what it spends is added to the session totals whether or not it
  produced a report. `--no-sub-agents` withdraws it.
- **`--no-sub-agents`** withdraws the `task` tool, the only way to stop the model
  spending model calls of its own.

### Changed

- Removed the version line below the terminal welcome wordmark and tightened the header
  spacing. The installed version remained available through `codai --version`.

- Moved Agent to the left and Plan to the right in the terminal mode selector. Agent
  remained the default; explicit Plan selection and restored session modes were preserved.

- Changed session context to on-demand file discovery instead of an automatic full-project
  signature scan, removing the startup `Loading Context...` phase. Interactive prompt
  guidance loaded only before the first task that needed it, then stayed available for
  subsequent requests. One-shot and Plan-to-Act execution also stopped automatic scanning;
  instructions, rules, skills, memory, file mentions, permissions, and checkpoints remained
  intact. Explicit SDK scanning and prompt-building APIs remained available.

- Simplified CI to one Ubuntu build job with warnings treated as errors. Removed CI
  tests, test artifacts, Windows matrix, and publish/package verification jobs while
  retaining Release Drafter, automatic release labels, NuGet publishing, and docs deployment.
  Removed scheduled Dependabot updates and the CodeQL analysis workflow.

- Reordered connection setup to offer **Use your own provider** first, followed by
  **Continue with ChatGPT**, and **Hosted provider** before **Local server**.
  Removed the separate **Custom endpoint** setup choice while retaining editable
  provider server addresses. Provider selection displayed names only, without default
  model subtitles or empty description rows; models remained a later selection.

- **Starting a session is quiet.** The `/help` hint and the notes that used to announce
  the project instructions file, the available skills, the loaded rules, and the memory
  documents are no longer printed, so the welcome banner is the first thing on screen.
  The files are still loaded and still steer the prompt.
- **A tool approval can be remembered per tool.** Choosing the second option keeps that
  one tool approved for the rest of the session; approving every `write_file` call says
  nothing about `run_command`, and nothing is remembered once the process exits.
- **Compaction now has three tiers, and the cheapest one runs first.** Before a summary is
  paid for, the bulk of old tool output is cleared without a model call — the start and the
  end of each response are kept, the middle is replaced by a marker, and the conversation is
  archived first so nothing is lost. A conversation that this brings back under its budget
  never reaches the summariser at all.
- **The files a session was working in are restored after a summary.** A summary can carry a
  decision but not a file, so the five most recently touched files, up to a bounded budget,
  come back inside the summary turn; the restore is dropped rather than the summary when it
  would make the replacement no shorter than what it replaced.
- **A summary request that the provider refuses for being too long is sent again.** The
  oldest turns are dropped from it, the prompt says how many were left out and points at the
  archive, and the retry repeats up to three times instead of failing the compaction.
- **The activity row reports time, tokens and how to stop.** While a request runs, the row
  above the composer now names the task and adds `esc to interrupt`, the elapsed time, and
  the output tokens streamed so far. Timing and counts appear after two seconds so a fast
  answer never flashes numbers that are already gone; the token count climbs toward the real
  figure instead of jumping; and a request that stops producing output marks its spinner
  instead of looking frozen. Narrow terminals drop the token count first, then the timer,
  then the interrupt hint, so the name of the task always survives.
- **Streamed output redraws once per frame.** Answer deltas and command output used to
  repaint the transcript on every write; they are now coalesced to the frame cadence the
  animation already runs at, which keeps long answers smooth and stops the redraw cost from
  competing with the work being shown.
- **Releases publish themselves to nuget.org.** Publishing the draft Release Drafter
  prepares now packs every project under `src/`, stamps the release tag as the package
  version, verifies the `Codai` tool installs and pushes to nuget.org with the
  `NUGET_API_KEY` secret. Package and install verification runs during release publishing;
  regular CI is build-only.
- **The documentation site is built and published by GitHub Actions.** Nextra and Next.js
  exported all pages from `docs/content` into `docs/out`, with `/codai` links and assets.
  [docs.yml](.github/workflows/docs.yml) restored GitHub Pages deployment after source,
  static-link, asset, and search-index checks. Native `_meta.js` navigation replaced
  Mintlify configuration; Mintlify hosting and login were no longer required.
- **The documentation lives at https://meysamhadeli.github.io/codai.** `README.md` links
  point there instead of the unpublished `docs.codai.dev`.
- **The CLI package is `Codai`.** `dotnet tool install --global Codai` now installs the
  terminal app; the command it puts on the path is still `codai`. The previous package id
  was `Codai.Tool`.
- **The session starts in `agent` mode.** Being asked before every write is what makes
  that safe, and writing is the point of the tool. `--mode plan` starts a
  read-only session instead.
- **The read-only modes are enforced twice.** A model with no editing tool can still write
  a diff into its answer, so `ChangeApplicator` now checks the mode too and reports the
  changes it refused to apply instead of applying them.

- `Microsoft.Extensions.*` packages moved to 10.0.12, which `Microsoft.Extensions.AI`
  10.10.0 requires.
- `.codai/` is now a built-in ignore pattern, so the stored session is never read back as
  if it were project context.
- `SessionCommandHandler.Handle` became `HandleAsync`, since `:compact` calls the model.
- `SessionCommandHandler.HandleAsync` now takes the command's arguments, so `:mode agent`
  and a bare `:mode` are both handled by one command.
- `ChatExchangeService.SendAsync` takes a `ProjectGuidance` (instructions plus skills)
  instead of an `IProjectInstructionsProvider`-shaped instructions argument, and reads
  the current `AgentModeState` per request.
- `AddCodaiSession` accepts `mode`, `allowShell`, `autoCompact` and `allowSubAgents`, and
  the CLI passes the parsed `--mode`, `--no-shell`, `--no-auto-compact` and
  `--no-sub-agents` because it can ask before every call.
- `PromptExtras.Mode` is nullable: a request with no mode gets no mode guidance and every
  tool the builder holds, which is what keeps the prompt parity tests meaningful.
- `SkillCatalog.FindAsync` normalises the requested name before validating it, so a
  capitalised name resolves instead of being rejected as unusable.
- Flag names are normalised once in `CommandLineParser.SplitFlag` instead of being
  registered twice, so underscores are accepted wherever a dash is on every flag,
  including the ones added since the snake_case spellings were deprecated.

### Fixed

- Ignored local probe workspaces and Python bytecode caches, and removed previously
  tracked generated files from the repository without deleting local copies.

- **Mouse scrolling in the full-screen terminal.** Added wheel scrolling and right-side
  scrollbar clicks/dragging, including while responses were running, without moving the
  footer. Windows console mouse events and VT mouse reports were consumed separately
  from draft text, dialogs retained input focus, and mouse reporting/input modes were
  restored on exit.
- **Repeated terminal footers in scrollback.** Interactive ANSI sessions moved to an
  alternate screen, keeping Plan / Agent, usage, and workspace information in one
  live footer while the transcript scrolls. Exiting or failing during input restored the
  previous shell screen instead of leaving old UI frames in shell history.
- **Discovery ignored canonical path spellings.** `glob`, `grep` and `list_files` validated
  the resolved directory but then scoped the file index with the raw model spelling, so
  `src/nested/./`, `src/../src/nested` or an absolute in-project directory reported no
  matches. Scoping now derives from the validated canonical path, so equivalent spellings
  agree while sibling directories and ignored paths stay excluded.
- **Transient Windows state-file locks.** Atomic replacement retries access/sharing/lock
  violations up to five times with bounded backoff. Cancellation and exhausted retries
  retain the original file and clean the temporary write; permanent errors still surface.
- **A mode restricted nothing.** `ChatRequest.AllowedTools` was computed from the mode and
  then read by nobody, so a read-only session only *told* the model not to write. It is
  now enforced twice: the provider is given only that mode's tools, so a compliant model
  never sees the others, and the executor refuses a tool that was not offered, so a
  provider that accepts a call for a function it was never given is still stopped.
- **`$ARGUMENTS` was never substituted.** `CommandExpander` compared the placeholder
  against the text after the `$`, where it could not match, so a command that used the
  placeholder lost the user's arguments. Covered now by tests.

### Removed

- Removed the standalone security policy file and linked the README directly to private
  vulnerability reporting.

- **Ask mode.** Removed Ask from the terminal footer, settings, CLI/session mode parsing,
  model selection, and ACP mode switching. Tab toggled between Plan and Agent; Agent
  remained the default and Plan stayed read-only. Saved Ask task metadata migrated to
  Plan on read, preserving read-only behavior; existing Plan and Agent numeric mode values
  were preserved.

## [2.0.0] - 2026-10-09

Codai is now a .NET application. The Go implementation has been replaced by a
solution of focused libraries plus a CLI, packaged on NuGet.

### Added

- **`Codai.Tool`** — install the CLI with
  `dotnet tool install --global Codai.Tool`. Every library also packs as a
  standalone NuGet package.
- **`reasoning_effort`** (`low` | `medium` | `high`) now maps onto
  `ChatOptions.Reasoning`, so it changes the request that providers receive
  instead of being ignored.
- **Kebab-case CLI flags** (`--base-url`, `--api-key`, `--api-version`,
  `--max-tokens`, `--reasoning-effort`, …). The previous snake_case spellings
  still work as aliases.
- A single **provider catalog** that both the configuration layer and the client
  factory read, so a provider's base URL, default model and wire format can no
  longer drift apart.
- Symbol packages (`snupkg`), SourceLink, deterministic builds and a repository
  `.editorconfig`.
- `SECURITY.md`, `CHANGELOG.md`, issue forms, a pull request template, Dependabot
  configuration and a CodeQL workflow.

### Changed

- Configuration sections are now `Codai.*` namespaces rather than a flat layout:
  `Codai.Core.Chat`, `Codai.Core.Diffs`, `Codai.Core.Tokens`,
  `Codai.Core.Projects`, and an `Abstractions` folder per project.
- The session orchestrator was split into `ProjectContextLoader`,
  `ChatExchangeService`, `ChangeApplicator` and `SessionCommandHandler`, with
  `CodeSession` reduced to the interactive loop.
- `IUserInterface` is injectable rather than constructing a console directly, so
  the UI can be tested and the input source substituted.
- XML documentation is generated and the build is warning-clean under
  `-warnaserror`.

### Removed

- The `encoding_format` setting, which the request path never forwarded.
- `CodaiDefaults.Model` and `CodaiDefaults.OpenAiBaseUrl`; the provider catalog is
  the single source of truth for defaults.

### Fixed

- **Path traversal.** `DiffApplier` now rejects any change whose target resolves
  outside the project root, including `..` segments and absolute paths, and
  reports it as a rejected change rather than writing the file.
- Prompt generation is now culture-invariant, so a non-English locale can no
  longer change the format of numbers in the prompt.
- The version reported by `--version` and the version in package metadata are the
  same value.

## [1.0.0] - 2025-06-12

Initial public release of the Go implementation.

[Unreleased]: https://github.com/meysamhadeli/codai/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/meysamhadeli/codai/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/meysamhadeli/codai/releases/tag/v1.0.0

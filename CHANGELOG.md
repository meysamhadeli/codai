# Changelog
All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Removed

- Removed the roadmap page from documentation, navigation, and overview links.

### Fixed

- Updated README and documentation to match implemented commands, CLI flags,
  configuration and budgets, workspace trust, approval defaults, model pickers,
  plugin/MCP activation, and terminal behavior. Added native hook and hosting
  guides, JSON output and local MCP server references, and source-backed coverage
  checks to catch documentation drift.

- Prevented the terminal cursor from flashing over session tabs during hover and
  click switching. Batched tab redraws with the cursor hidden, avoided clearing the
  fixed-width strip on hover, and kept session-switch clears inside the redraw frame.

### Changed

- Used white text for selected and hovered terminal session tabs. Kept a shaded
  background only on the active tab, with text-only hover feedback and no animation.
  Preserved stable tab widths, click targets, and inactive-tab styling.

- Automatically selected skills and MCP servers from publisher-approved plugin
  installs, preserving unrelated selections and private definition-bound consent.
  Verified Context7 documentation calls for WSO2 in the same live terminal session
  after installation, without manual MCP selection or a restart.
- Changed Installed plugin entries to open an Uninstall or Cancel action page before removal.

### Added

- Added Nerd Font action icons to tool transcripts, command menus, plugin
  listings, approvals, and notices. Kept readable labels, cell-aware clipping,
  ASCII fallbacks, and standard Unicode markers through `CODAI_ICONS=unicode`.
  Kept Nerd Font glyphs white independently of theme, selection, and status colors.

- Added `/update` and an Update command-menu action to check stable releases on
  nuget.org, report an up-to-date version, and offer Update or Skip. Updated the
  running global or custom-path .NET tool installation only after explicit consent,
  with cancellation, failure reporting, and a restart notice. Kept source, local-tool,
  and standalone installations check-only with manual-update guidance.

- Added browser-style terminal session tabs with clickable titles, new and close
  controls, overflow navigation, and keyboard shortcuts. Preserved independent
  histories, modes, tool transcripts, scroll positions, and drafts. Saved closed
  conversations and opened an empty tab when the last tab was closed.

- Installed public GitHub, local, and HTTPS ZIP Claude/Copilot plugin bundles with
  nested manifests, staged replacement, and bounded link-safe extraction. Loaded
  namespaced skills and commands, imported MCP definitions behind private approval,
  and adapted supported synchronous command hooks to existing execution policies.
  Verified real Context7 and Copilot Microsoft Docs contributions through the Codai
  terminal with a real model and live remote MCP calls.

- Added GitHub's `copilot-plugins` and community `awesome-copilot` catalogs to
  default plugin discovery. Accepted GitHub `owner/repo` shorthand, repository
  URLs, and local repositories for persistent custom marketplace registration.
  Verified `obra/superpowers-marketplace` as an additional live catalog without
  implying runtime compatibility with its Claude/Copilot components.

- Added Claude-style marketplace metadata discovery and bounded plugin bundle
  installation for Codai hosts. Marketplace browsing does not load assemblies;
  HTTPS sources, local bundles, ZIP traversal, and `plugin.json` manifests are
  validated before an installed plugin reaches the existing loader.

- Added the `/plugins` terminal workflow with Discover, Installed, Marketplaces,
  searchable plugin selection, marketplace-source persistence,
  and restart-aware bundle removal.

- Replaced nested plugin pickers with a persistent tabbed browser, shaded search,
  marketplace filters, two-line results, keyboard navigation, and live default
  discovery from Anthropic's official public catalog. Displayed real catalog counts
  and explicit compatibility limits for client-specific contributions.

### Changed

- Put Install first and Cancel second in plugin installation confirmation, and
  shortened the action label to Install while retaining the publisher warning.

- Matched plugin search and result styling to the MCP picker, with shaded fields,
  accented checkmarks and labels, selected-row backgrounds, and right-aligned sources.
  Made Enter start installation with publisher consent, removed Space-to-toggle,
  and replaced the Errors tab with ordinary error notices.

### Fixed

- Strengthened matching-skill guidance to require `read_skill` before direct tool
  use or answering, including when the model already knew the topic. Verified a
  natural ASP.NET Core JWT question loaded the installed Microsoft Docs skill
  before live Microsoft Learn search and fetch calls.

- Suppressed duplicate plugin MCP connections when an enabled directly configured
  or imported server already provided the endpoint. Matched normalized remote URLs
  or exact stdio commands and arguments, preserved the winning configuration and
  separate private approvals, and retained other plugin contributions.

- Required explicit publisher consent even with automatic tool approval enabled.
  Reported malformed bundles without crashing the plugin browser. Normalized known
  Context7 client hints to its public MCP endpoint without borrowing client identities.

- Preserved custom-marketplace registration across browser reopen by reading and
  writing through the same filesystem abstraction. Resolved local bundle paths
  relative to their marketplace repository and retained failed-add diagnostics.
  Kept active marketplace counts complete when the filter row became crowded.

- Reduced the gap between thinking/loading activity or result text and the input
  border by one row, preserving a clear separator and stable cursor alignment.

- Matched submitted-message markers to the input chevron, using a darker shade
  of the active theme accent for history and preserving ASCII fallback. Removed
  `/settings` from slash-command suggestions without disabling the command.

- Unified settings, command, and submenu search fields with the same full-width
  neutral background and text inset. Reduced search fields to a compact single
  row while keeping cursor alignment consistent across viewport sizes.

- Placed Continue first and Keep current conversation second in new-conversation
  and branch confirmations. Preserved save-before-switch and Escape cancellation.

- Expanded multiline input to fit the terminal up to twelve text rows, with a
  cursor-following viewport for longer drafts. Replaced the filled input rail with
  thin horizontal rules and a single prompt marker. Preserved multiline bracketed
  paste without accidental submission and added Windows Ctrl+V and right-click
  clipboard paste, including pending drafts during responses.

- Recorded provider-qualified context windows and independently published input
  and output ceilings for every bundled hosted model choice. Used the active
  model's input ceiling, or recorded context window, for manual and automatic
  compaction instead of broad family estimates. Preserved bounded headroom when
  output caps were unknown and kept context provenance separate from price reviews.

- Scaled manual and automatic compaction tails, requested summary lengths, and
  deterministic fallback to the active model's input allowance and lower selected
  window. Rejected summaries that remained oversized, bounded fallback endpoint
  fields, and preserved complete transcripts for recovery.

- Scaled the default automatic-compaction buffer for small context windows so short
  conversations no longer compacted prematurely, while retaining non-default host
  policy reserves and proactive/urgent thresholds.

- Rendered informational notices, including stopped-request feedback, in white and
  error messages in red across all terminal themes and transcript rendering paths.

- Accounted for prepared project context, mentions, instructions, memory, skill
  metadata, and initialized tools before automatic history compaction. Used bundled
  model input limits and output headroom, with conservative fallbacks.

- Checked context again between streamed and non-streamed tool rounds. Compacted
  older ordinary payloads before large current results while preserving call IDs,
  loaded skills, rule-review guidance, and original results. Honored lower automatic
  windows and explicit compaction disablement; retained cancellation semantics.

- Budgeted summary input and automatic-window displays for the currently selected
  model rather than only the startup model. Kept summary calls tool-free and excluded
  them from recursive automatic compaction.

- Matched Markdown inline code and backtick-delimited paths to the active theme
  accent instead of fixed green, including after live theme changes. Preserved
  code-block syntax highlighting and semantic red/green diff colors.

- Balanced answer code panes' left and right outer margins, keeping the dark
  surface away from the scrollbar while preserving syntax colors, line numbers,
  indentation, and wrapped continuation rows.

- Reloaded MCP definitions and private selections at the first deferred connection,
  honoring checklist changes made after workspace opening but before the first model
  request, including servers discovered after an initially empty catalog. Preserved
  offline startup, disabled-server exclusion, and once-only successful initialization.
  Initialized deferred MCP tools for background or delegated requests started before
  foreground chat, selecting their read-only inventory only after discovery completed.

- Fixed remote MCP startup parsing for SSE responses beginning with event metadata,
  heartbeat comments, or IDs. Used the standard SSE parser for multiline data and
  CRLF framing while retaining JSON response support. Sent required JSON/SSE Accept
  headers on initialization notifications, fixing Microsoft Learn's HTTP 406 response.

- Retained failed and cancelled model exchanges in conversation history, including
  partial streamed text marked as interrupted. Preserved the earlier request in
  subsequent prompts and saved conversations so follow-ups could refer to a lookup
  that stopped at a tool failure limit without repeating the question. Kept failure
  statuses, cancellation exit codes, and approval policy unchanged.

- Replaced legacy stdio MCP reflection with the installed SDK's typed client and
  tool APIs. Fixed connection failures caused by obsolete type names and returned
  actual MCP result content instead of result type names. Reported server-declared
  tool errors as failed calls.

- Discovered native MCP lists from user-wide `.codai.json` even when project runtime
  settings were selected. Restored Global entries in `/mcps` without copying provider
  settings or automatically starting unapproved servers. Preserved selected native
  overrides and project-specific startup review.

### Changed

- Added Claude Fable 5.2 and Gemini 4 Carbon suggestions and dated rates. Changed
  the Mistral default from deprecated Medium 3.5 to Large 4, removed Gemini 2.5
  from new-connection suggestions, and focused Qwen and Mistral fallbacks on
  reviewed text/coding models. Preserved explicitly configured model IDs and
  historical prices; model listing did not guarantee tool-transport compatibility.

- Reworked `/diff` into a compact bordered file overview with aggregate and per-file
  addition/deletion counts, selection highlighting, and overflow indicators. Added
  Enter-to-open inline detail, Escape-to-list navigation, single real line-number
  gutters, and cell-aware wrapped changed rows while preserving source switching,
  reviewed markers, hunk navigation, and read-only Git snapshots.

- Removed the inline `thought for ...` duration from live activity metrics while
  retaining elapsed time, token counts, interruption hints, and completed thought rows.

- Added request-scoped activity metrics for chat, foreground compaction, and
  delegated model calls, including unprinted summary output. Showed zero tokens
  while waiting, replaced marked live estimates with reported provider usage,
  displayed input counts on wide terminals and timed provider-signaled reasoning.
  Restored parent counts after nested requests, preserved counts across tools,
  and retained the original animated 3×3 spinner and themed phase labels.

- Styled live activity and loading labels with solid theme accents, trailing
  ellipses, and muted time-first metrics while retaining the existing 3×3 dot
  spinner. Added approximate streamed-output token counts on wide terminals and
  consistent presentation for caller-reported phases such as considering and
  reviewing. Changed successful tool dots to the active theme accent and made
  inline expansion chevrons and remaining-line counts white.

- Refined the far-right transcript scrollbar into a thin dark-grey rail with a
  lighter proportional thumb instead of full-cell background blocks. Added an
  ASCII fallback and immediate cursor repositioning to prevent right-margin
  wrapping. Kept edge-aligned click targets, wheel scrolling, dragging, and the
  fixed composer and footer.

- Replaced inline tool-output expansion keyboard hints with clickable disclosure
  chevrons. Added an inline collapse control while retaining tool-header clicks,
  Ctrl+O shortcuts, ASCII fallbacks, and scroll anchoring.

- Replaced grouped working boxes with chronological, unframed thought timings,
  public assistant progress, tool calls, and results that remained visible after
  completion. Added green success dots, `Name(arguments)` display labels, inline
  command output, and numbered red/green file previews with expandable overflow.
  Kept live interrupt controls while answers streamed and reduced submitted prompts
  to unpadded strips without timestamps. Kept thought rows timing-only rather than
  inventing reasoning text unavailable from the provider stream.

- Applied the active theme accent to selected command suggestion arrows, names,
  and descriptions and every segment of the left rail. Retained neutral unselected
  text, subtle selected-row shading, aligned columns, and compact keyboard navigation.

- Increased the live activity text highlight's travel speed by approximately one
  third while preserving the dot animation cadence, text layout, and interrupt controls.

- Styled slash-command suggestions as flat, rail-marked rows with aligned names
  and descriptions and subtle shading only on the selected row. Preserved the
  six-row window, keyboard completion, skill commands, and narrow-width clipping.
  Added New to the settings action menu, using the existing `/new` confirmation
  and saved-history workflow.

- Replaced default expanded activity breadcrumbs with one compact live row based
  on public assistant progress or observed operations. Kept earlier narration and
  tool calls in an explicitly opened, flat detail trace and displayed only the
  final assistant segment as the answer. Preserved interrupt controls, animation,
  and tool output inspection. Stopped labeling completed activities as failed
  solely because an earlier tool call failed; retained actual failure outcomes.

- Styled the existing composer footer as a quiet dark strip with bright approval
  and mode labels, muted shortcut hints and model text, and subtle control-group
  separators. Preserved current labels, keyboard shortcuts, right-aligned usage,
  account-plan links, and single-row compact fallbacks on narrow terminals.

- Strengthened research routing to discover relevant MCP tools before falling back
  to URL retrieval, resolve documentation identifiers before querying, and use URLs
  supplied by users or returned by actual sources rather than guessed paths. Guided
  external questions away from unrelated repository browsing and requested concise,
  observable progress instead of fabricated reasoning. Shared routing and checked
  skill metadata with read-only subagents, retaining deferred skill bodies and manual
  opt-outs. Added source-specific HTTP failure feedback recommending safe discovery
  and recovery without retries, access bypasses, or relaxed tool-failure limits.

- Combined `AGENTS.md`, `CLAUDE.md`, and `instructions.md` with existing instruction
  compatibility paths instead of selecting only the first file. Loaded repository
  ancestors in broad-to-specific order, refreshed instructions for every model
  request, and discovered nested guidance only for referenced or file-tool target
  directories. Paused reads, writes, edits, and structured patches before execution
  when applicable instructions changed, preserving approval and mode restrictions.
  Applied the same scoped guidance to read-only subagents and background investigations,
  retained reviewed changes during provider conversation rebuilds, and bounded discovery
  by canonical repository paths and context limits. Stopped discovering `CODAI.md`;
  existing content required renaming to a supported standard filename.

- Opened interactive workspaces without waiting for MCP server connections. Deferred
  enabled-server discovery until the first model exchange and ran stdio and remote
  discovery concurrently. Published complete tool definitions together and allowed
  retries after cancellation or failure. Preserved eager SDK and one-shot defaults,
  private startup consent, disabled tools, workspace trust, and tool-call policy.

- Matched settings action titles to their slash-command names: Session, Models,
  Agents, Diff, and Themes. Preserved descriptions, action order, and command routing.

- Centered the settings search placeholder, entered text, and cursor between equal
  shaded padding rows above and below. Retained the compact single-row field on
  short terminals and the existing neutral background, width, and menu navigation.

- Discovered repository skills in `.github/skills/` and personal skills in
  `~/.copilot/skills/` without explicit discovery configuration. Included shared
  repository roots in bounded ancestor, referenced-file nested, and additional-directory
  discovery. Preserved existing root priority, project-over-global precedence,
  private skill selections, and discovery limits.

- Loaded checked skill instructions on demand in agent sessions offering `read_skill`,
  advertising names and descriptions first. Retained eager fallback for other hosts
  and legacy requests, manual invocation, path metadata, and selection opt-outs.
  Deferred MCP schemas behind bounded request-local `tool_search` discovery, loading
  up to five matching schemas per search without executing tools. Preserved allowed-tool
  filtering, actual-call approval and budgets, startup consent, and saved selections.

- Clarified existing direct `npx` support for stdio MCP servers on Windows. The
  official SDK handled command wrappers internally, so explicit `cmd.exe /d /c`
  configuration was unnecessary. Documented the shorter portable definition.

- Moved private MCP consent and per-project skill selections out of user-wide
  `.codai.json` into Codai-managed `state/customization.json` beneath the user
  configuration directory. Preserved fingerprints, enabled and disabled choices,
  global skill defaults, provider settings, and unrelated fields. Migrated existing
  `customization` selections and valid legacy files during catalog loading, saving
  private state atomically before cleaning sources. Existing private choices won
  conflicts without combining consent records. Serialized selection updates,
  rejected malformed or oversized saves and redirected selection paths, and allowed
  saves alongside YAML settings without creating a runtime JSON file. Retained
  project-only skill storage for SDK hosts without an explicit global directory.

- Removed the second MCP startup confirmation. Saving `/mcps` selections enabled
  selected imports and disabled unselected servers through the existing private,
  project-specific consent store. Showed executable or endpoint information in the
  checklist, preserved definition-change invalidation and tool-call approval, and
  retained restart-required connection rebuilding.

- Guided automatic selection of relevant offered MCP tools and checked skills from
  their capability descriptions. Preferred documentation resolvers and lookup tools
  for library research, search before retrieving external URLs, and repository tools
  for repository questions. Preserved disabled selections, model invocation opt-outs,
  project priority, mode restrictions, and approval.

- Created missing user configuration automatically on interactive bare and `code`
  launches after workspace trust, including when project settings already existed.
  Preserved existing user files and project precedence without copying credentials.
  Kept first-run setup for missing connections and retained the empty JSON file when
  setup was cancelled.

- Replaced the previous configuration basename with `.codai.json` for full runtime
  settings and discovery metadata. Preferred JSON over `.codai.yml` and `.codai.yaml`,
  updated setup and configuration editing to use the new names, and excluded these
  files from default project scans. Removed discovery of the previous filenames;
  existing settings required renaming. Preserved unrelated discovery fields on saves.

- Removed auxiliary toggle/help footer lines from terminal menus and submenus.
  Matched MCP management to Skills with checkmarks, themed checked titles,
  descriptions, source labels, and an enabled count. Enter toggled without closing;
  Escape saved and closed. Used checklist selection as startup consent for imports and
  reported restart requirements after saving instead of inside the menu.

- Replaced the previous agent-mode roadmap with a VS Code extension roadmap covering
  native conversations, contextual edits and review, terminal/browser tools, MCP and
  skills, task recovery, and distribution. Kept the published roadmap URL and clearly
  separated planned editor features from available capabilities.

### Added

- Added `/compact [focus]` and `/autocompact [auto|tokens]` to slash completion,
  help, and the settings action palette. Reused the existing safe summary/archive
  workflow and retained `:compact`. Added a left/right window selector with Enter
  to save/apply and Escape to cancel, plus direct `800k`/`1m` arguments. Persisted
  `auto_compact_window` in JSON/YAML, with `0` preserving automatic defaults.
  Capped selected windows by the active model, retained compaction headroom and
  disable flags, and kept live settings unchanged when saving failed. Displayed
  foreground summarization as `Compacting conversation...` with the existing spinner.

- Accepted VS Code's `type` transport key in native MCP configuration, normalized
  HTTP aliases, and gave explicit `type` precedence over `transport`. Supported
  bounded slash-qualified imported server names such as `microsoftdocs/mcp`, while
  rejecting empty or traversal-like segments. Retained `gallery` and `version` as
  informational metadata without marketplace installation or executable pinning.
  Preserved ordinary startup selection and tool-call approval.

- Added bounded nearest-first skill discovery through Git repository ancestors,
  `.codai.json` discovery roots, canonical-path deduplication, and nested skills
  revealed by referenced workspace files. Supported `user-invocable`,
  `disable-model-invocation`, and `paths` metadata independently of saved checkmarks.

- Added layered MCP manifest discovery for Codai, Claude-style, and VS Code files,
  explicit manifest paths, additional repositories, environment substitutions,
  safe offline provenance, and native configuration overrides. Kept imported servers
  disabled until selected in `/mcps`; stored startup approvals privately
  with project/definition fingerprints and invalidated them when definitions changed.
  Preserved tool-call approval and required restart after connection changes.

- Added all discovered skill names to `/` completion before the first request and refreshed
  suggestions after skill management. Included checked skill instructions in every
  applicable workflow while preserving project constraints, mode restrictions, and tool permissions.
  Omitted unchecked automatic skill instructions while keeping manual slash invocation
  available independently of checks. Manual invocation did not change automatic selection.

- Added multi-skill enablement in `/skills`, with plain checkmarks, theme-colored
  enabled titles, neutral disabled titles, descriptions below names, source labels,
  an enabled count, and the existing search panel. Enter toggled multiple choices
  without closing the manager; Escape closed and saved them atomically.
  Persisted project availability, supported re-enabling globally disabled skills,
  and refreshed model-visible guidance before the next request without restarting.
  Added catalog APIs for listing disabled entries and saving enabled selections.

- Added a workspace-access screen before CLI agent startup loaded project configuration,
  instructions, integrations, or session state. Selected Yes before No;
  required fresh consent on each launch and explicit `--trust-directory` authorization
  for redirected or JSON runs. Kept directory trust separate from tool approval and
  provided a Yes-default confirmation fallback for plain terminals.

### Fixed

- Simplified every terminal search field to a single neutral dark band with muted
  inset text, without borders or search icons. Preserved filtering and aligned
  the typing cursor in settings and all selection menus, including ASCII terminals.

- Parsed folded and literal multiline YAML skill descriptions instead of showing
  `>` or `|` as the description. Preserved scalar lists and tolerant malformed-header
  fallback in the shared skill, command, and rule frontmatter parser.

- Matched loading labels to the compact muted-grey style with a moving theme-colored
  highlight and inline interrupt/elapsed hint. Kept the animated 3-by-3 dot grid
  and removed trailing-period animation from startup and setup loading.

- Matched workspace-access selection colors to the saved chat theme, including
  project, explicit, and user-fallback JSON/YAML configuration. Preserved flag and
  environment precedence with a bounded theme-only read before consent; malformed
  or unreadable settings did not prevent leaving the workspace.

- Removed accidental game-test and temporary probe registrations from the product
  solution so its build and test commands targeted only Codai projects.

- Selected Yes first and No second in approvals and confirmations, including workspace
  access. Kept Escape, EOF, and unattended runs from granting consent. Moved remembered
  tool approval to the third choice, with `Y` / `1`, `N` / `2`, and `A` / `3` shortcuts.
- Used the active theme's foreground accent rather than filled selection bars in
  decision boxes, and standardized selection footers to Enter to confirm and Esc to
  cancel. Applied the configured theme to workspace access without activating other
  project settings before consent.

- Kept approvals, confirmations, questions, and workspace-trust choices in bordered
  decision boxes below the current CLI conversation instead of opening settings
  palettes or clearing the workspace view. Preserved draft input, Escape cancellation,
  custom answers, and tool-preview paging.

- Corrected input and submitted-message alignment within compact prompt strips.
  Centered text and the input cursor between equal top and bottom padding rows,
  removed excess left spacing,
  and used a steady block input cursor with a one-space inset from the rail. Preserved wrapping,
  short-terminal fallback, footer spacing, and cursor restoration on exit.
- Included bounded proposed contents in file-write approvals instead of displaying
  only the target path. Explicitly marked empty writes and truncated previews.
- Hid the terminal cursor while navigating selection menus so it no longer
  covered the theme-colored Settings arrow or appeared below the model list.
  Preserved a visible cursor for search, value entry, and the chat composer.
- Matched settings and model-dialog accents to the active terminal theme instead
  of a fixed lavender color, including headings, rules, search borders, and selection.
- Kept every edge of the settings search box theme-colored, including its vertical
  borders and after moving selection, while retaining muted text and an inset
  search icon. Aligned padding using terminal cell widths.
- Kept stable agent instructions ahead of growing conversation history to avoid
  invalidating the reusable prompt prefix on every turn. Preserved non-agent
  prompt compatibility, mode permissions, and configured reasoning effort.
- Reused one shadow Git repository and index for new tasks in a project instead of
  initializing and indexing a new repository per request. Preserved legacy
  checkpoint repositories, serialized shared-index access across processes, and
  kept later checkpoint revisions reachable when restoring earlier state.
- Preserved ignored local files during Git checkpoint cleanup while still
  removing non-ignored files created after a checkpoint.
- Rejected stale compaction plans after history was cleared or rewritten, while
  preserving turns appended during summarization. Added source snapshots to
  planner-produced `CompactionPlan` instances.
- Fixed background compaction becoming stuck after synchronous completion and
  returning an awaited summary again through the completed-result queue.
- Reaffirmed the active Plan or Agent mode after supplied context so historical
  mode instructions did not select the current mode. Preserved enforced tool
  permissions, project constraints, and approval requirements.
- Validated tool-call arguments against each tool's advertised JSON Schema before
  approval, guidance, hooks, or execution. Returned bounded field-level feedback
  for correction, counted invalid calls toward safety limits, and stopped on
  invalid schemas or unresolved external references without downloading them.
- Closed repeated-tool-call limit bypasses involving nested object key order,
  escaped strings, and tool-name casing. Preserved array order and stored hashed
  argument fingerprints instead of full argument payloads.
- Stopped local slash controls such as `/sessions` and `/models` from leaving
  duplicate user-message blocks above the composer. Kept ordinary requests,
  `/init`, and custom prompt workflows visible in the conversation.
- Discovered shared agent skills from project `.agents`, `.claude`, and `.opencode`
  roots and home `.agents`, `.claude`, and `.config/opencode` roots. Kept project
  precedence, disabled-skill policy, and shared-home discovery when the CLI used
  a separate configuration directory; SDK global discovery remained opt-in.
- Kept the empty-input placeholder on the home screen only, leaving the composer
  blank after the first submission, including after chat redraws and resizing.
- Cleared the newly positioned composer area on terminal resize so blank spacing
  no longer retained duplicate loader text from the previous layout.
- Scoped default prompts and mode workflows to the actual request so greetings and
  simple questions no longer requested repository overviews, capability lists, or
  automatic plans. Required grounded claims, avoided unnecessary missing-file
  requests from abbreviated summaries, and stopped requesting full-file echoes
  after tool edits. Preserved mode permissions and requests for detailed answers.
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

- Replaced running activity and tool icons with a nine-dot 3×3 animation and
  theme-colored text with a moving bright highlight at a fixed-width 160 ms cadence.
  Moved cancel/elapsed hints into each live row and removed the separate bottom
  loader. Removed action totals from running and completed headers while retaining
  failure status. Kept live headers visible during streamed output and restored
  static icons after completion. Preserved grouping, disclosure
  controls, output previews, and narrow-terminal clipping.
- Added breathing room between transcript/status messages and input, including
  when history filled the viewport, with a compact short-terminal fallback.
- Grouped thinking, reasoning, and working under one collapsible activity header
  with nested tool actions. Removed duplicate status/timing panels and the
  confusing private-reasoning warning. Collapsed completed groups to compact
  duration-only summaries, preserved failure status and manual collapse, and
  kept individual tool output/diffs inspectable without exposing internal reasoning.
- Thickened the input rail and colored it with the active terminal theme,
  preserving centered text, the block cursor inset, and an ASCII fallback.
- Combined white approval/mode shortcut labels, muted usage/cost, and the white
  current model into one input footer line. Removed the workspace address and
  used compact labels on narrow terminals without wrapping. Preserved keyboard
  switching, interactive Agent/Auto defaults, and explicit mode/approval options.
  Showed the selected model immediately at startup and bounded long model names.
- Centered single-line input and submitted messages in three-row strips with
  equal shaded padding above and below their text. Preserved text wrapping,
  the blank row before the single-line footer, and collapsed composer padding on
  short terminals.
- Selected automatic approval by default for interactive terminal sessions while
  preserving explicit `--auto-approve false`, Shift+Tab switching, read-only mode
  restrictions, and existing headless and custom-host defaults.
- Restyled the main conversation with shaded timestamped user messages, accent-dot
  assistant replies, a left-railed composer, muted thought durations, and compact
  tool summaries with right-aligned timing and green/red change counts. Kept one
  conversation screen without Current, Sessions, Issues, Pull requests, or Gists tabs.
- Framed tool approvals around command and patch previews, highlighted the selected
  decision with the active theme, and added preview paging without changing consent
  scope. Preserved the single-line composer footer and `/session` switching.

- Consolidated the input footer into one row beneath the composer: workspace on
  the left, with usage/cost and the current model on the right. Preserved
  percentage and cost while clipping secondary text on narrow terminals, and replaced
  stacked status rows with one footer beneath the padded input surface.
- Applied the rounded, theme-colored Settings search field and inset icon to all
  searchable selection submenus. Added accurate current-state ticks for agents,
  themes, enabled MCP/plugin entries, and known configuration values. Preserved
  theme ticks when saving was cancelled or failed, and kept unknown or masked
  values from implying a configured state.
- Unified selection submenus around compact numbered rows, inline descriptions
  and values, and a footer below results. Applied the active theme accent only
  to the selected row; left other numbers, labels, and current-choice markers
  in default text color instead of fixed green. Preserved filtering, ordering,
  keyboard actions, current markers, and workflow guidance.
- Removed the standalone top separator lines from Settings and model menus,
  preserving titles, theme accents, and the rounded Settings search border.
- Removed the Status / Config / Usage / Stats strip from Settings headers and
  standardized selection-menu hints to "Enter to change" and "Esc to close".
  Preserved keyboard navigation, direct settings views, and workflow guidance.
- Simplified model selection to one flat numbered list without Recent/provider
  headings or the authentication/favorite shortcut footer. Preserved filtering,
  ordering, keyboard actions, current-model markers, and the confirmation hint.
  Moved the idle cursor below the dialog instead of onto a list heading.
- Matched settings to the reference layout with a theme-accented heading,
  rounded search border, aligned compact values, overflow counts, and a
  footer directly below results. Added settings-local Status / Config / Usage /
  Stats navigation backed by runtime inspection; kept main chat free of tabs.
  Restyled model selection with numbered inline rows and a current marker,
  preserving session-only selection, filtering, favorites, and authentication.
- Redesigned the terminal around one conversation screen without persistent top
  tabs. Added wide, unframed settings with safe label/value rows, searchable
  feature pickers with secondary descriptions, full-width forms, and exact
  approval previews above numbered decisions. Preserved draft restoration,
  masked credentials, keyboard controls, and session/tool approval scope.
- Advertised `/session` for saved-conversation switching while retaining
  `/sessions` as an accepted alias, and added `/settings` for the local
  configuration editor without a model request.
- Held precomputed summaries until automatic compaction was due, avoiding eager
  history-prefix replacement at every completed response. Replaced transcript-style
  summaries with concise continuation checkpoints, bounded summary inputs including
  prior checkpoints by estimated model context, and retained both ends of clipped
  transcript fields.
- Replaced padded user/input blocks with compact `>` prompt strips and indented
  continuations. Kept editable input lighter than history, aligned text and cursor
  positions, and retained model, usage, mode, workspace, and approval footer data.
- Replaced the settings palette with twelve grouped actions: session, model,
  agents, skills, diff, theme, plugins, MCPs, auth, help, docs, and exit. Removed
  other settings entries, kept slash commands independent, and opened published
  documentation in the browser with a visible URL fallback.
- Reduced the slash menu to thirteen commands. Removed `/debug`,
  `/editor`, `/review`, `/status`, and `/variants`; renamed `/connect` to `/auth`
  without keeping an alias. Changed `/agents` choices to `agent` and `plan`.
- Replaced transcript-only `/diff` output with a full-screen, read-only browser
  with working/staged snapshots, file navigation, colored numbered hunks,
  scrolling, help, and temporary reviewed markers. Retained bounded Git calls
  and plain-text fallback for noninteractive hosts.
- Added model and skill pickers with aligned descriptions, current-model
  markers, session recents/favorites, and connect/favorite keyboard actions.
  Opened empty MCP/skill pickers instead of writing transcript messages. Made
  Space toggle saved MCP availability directly, with restart requirements shown.
- Replaced the slash menu with a command catalog:
  `/agents`, `/auth`, `/diff`, `/exit`, `/help`, `/init`, `/mcps`, `/models`,
  `/new`, `/session`, `/settings`, `/skills`, and `/themes`. Removed previous slash aliases from parsing and
  completion while retaining legacy colon commands for existing callers.
  Preserved connection identity during model changes.
  Extended read-only `git_diff` with staged, commit, and branch comparisons,
  rejecting shell syntax and option-like refs before process execution.
  Documented restart-required connections/MCP changes and remaining OpenCode
  differences, including unsupported `/move` and `/update`.
- Restyled answer code blocks as padded dark panes with muted line numbers,
  ColorCode.Core syntax highlighting, and wrapped-line continuation gutters.
  Preserved literal source and existing tool/diff preview styling, with plain
  fallback for unknown languages and hidden gutters on very narrow panes.
- Replaced the ring-style loading pulse with an animated 3×3 dot grid that filled
  and emptied using randomly varied row, column, spiral, center-out, and scattered
  patterns, with separately selected fill and empty orders each cycle. Kept a
  stable two-cell slot and used the same animation for startup and setup loading,
  retaining themed colors, ASCII fallback, metrics, and narrow-terminal hints.
- Routed interactive yes/no confirmations through modal choices
  with No selected by default, Escape rejection, preserved drafts, and cancellation
  cleanup. Guided blocking agent questions through `ask_question` while keeping
  clarification separate from write/execute approval.
- Normalized conversation gaps to one blank row and placed input below short
  replies, retaining bottom anchoring for overflowing transcripts. Replaced the
  solid busy blocks with a smoother shaded wave and kept themed/ASCII fallbacks.
- Replaced the busy phase label with a themed moving-block loader and compact
  `esc interrupt` hint. Added blank rows above and below activity, aligned it
  with input text, and kept narrow/short-window fallbacks, ASCII animation,
  elapsed/token metrics, and Escape cancellation.
- Restyled footer usage as `19.0K (2%) · $0.01`, with compact token totals,
  parenthesized context percentages, and responsive fallbacks. Requested a
  steady block input cursor on ANSI terminals and reset cursor style on exit.
- Simplified assistant text with neutral headings, green inline code and paths,
  naturally flowing paragraphs and compact lists. Preserved explicit Markdown line breaks and
  existing tool/diff preview styling.
- Placed a single-line footer below padded prompt input with a blank gap, and replaced the
  context progress bar with a percentage beside estimated cost. Preserved
  percentage and cost on narrow terminals by shortening secondary labels.
- Restyled submitted questions and prompt input as padded dark blocks with a
  theme-colored left edge. Kept model, mode, usage, and approval details below
  the input block, with cursor positioning adjusted for the new padding.
- Grouped settings actions into Suggested, Session, Integrations, Workspace, and
  Help sections, with searchable category names and nonselectable headings.
- Restyled interactive menus and tool approvals as compact, borderless modals with
  a dark surface, full-row theme highlights, aligned descriptions, and responsive
  previews. Added Home, End, and PageUp/PageDown navigation to searchable menus.
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

# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

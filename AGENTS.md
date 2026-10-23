# AGENTS.md

Instructions for anyone — human or AI — changing this repository.

## Documentation ships with the code

A change is not done until the docs that describe it are updated in the same commit.

| Changed | Update |
| --- | --- |
| Flag, default, exit code, config key | [README.md](./README.md) · matching [docs/](./docs) page · `CHANGELOG.md` |
| User-visible behaviour or limitation | `CHANGELOG.md` under `## [Unreleased]` |
| New capability, provider, integration, package | [README.md](./README.md) tables · [docs/](./docs) · `CHANGELOG.md` |
| Public API surface | XML doc comments · [docs/](./docs) · `CHANGELOG.md` |
| Layout or package name | [README.md](./README.md) index · `CONTRIBUTION.md` |

- `README.md` is a short landing page: pitch, install, feature summary, links to
  `https://docs.codai.dev`. Detail goes in `docs/`, never in the README.
- `docs/` is the documentation site. Pages are `.mdx` files with `title` and `description`
  frontmatter; `docs/docs.json` declares the navigation, and `node docs/build.mjs` renders
  the static site into `docs/_site`, which
  [docs.yml](./.github/workflows/docs.yml) publishes to GitHub Pages.
- A new `.mdx` page must be registered in the `navigation` of
  [docs/docs.json](./docs/docs.json) or `node docs/check.mjs` fails — the site would
  otherwise not link to it.
- The published base URL is `https://meysamhadeli.github.io/codai`. It is defined in
  `docs/build.mjs` and `docs/check.mjs`; if a custom domain is added later, add a
  `docs/CNAME` file and update those two constants together.
- Links in `README.md` point at published docs pages, not local file paths.
- `CHANGELOG.md` entries are user-facing, past tense, under `Added` / `Changed` / `Fixed` /
  `Removed`.
- Never document behaviour the code does not implement. Docs and code disagreeing is a bug in
  one of them — fix both.

## Verify

```bash
dotnet build Codai.slnx -warnaserror
dotnet test Codai.slnx

cd docs && npm ci && npm run check && npm run build   # only when docs/ changed
```

CI builds with `-warnaserror`. Style rules live in `.editorconfig` and `Directory.Build.props`;
`dotnet format Codai.slnx --no-restore` applies them.

## Release

Pull request labels drive the version: `feat:` adds `minor`, `fix:` adds `patch`, and `major`
is manual. Release Drafter keeps one draft release; publishing it runs
[publish.yml](./.github/workflows/publish.yml), which packs every `src/` project with the tag
version, verifies `Codai` installs, and pushes to nuget.org with the `NUGET_API_KEY` secret.
Never bump `<Version>` for a release by hand — the tag is the version.

## Conventions

- Collaborators are interfaces, in an `Abstractions` folder next to their consumer.
- Provider-specific code stays in `src/Codai.Adapters/Providers`; `Codai.Core` never learns a
  vendor name.
- Every change arrives with a test; a bug fix arrives with a test that fails before it.
- Fix the design rather than suppressing an analyzer; suppress narrowly and say why.
- Commits and PR titles follow [Conventional Commits](https://www.conventionalcommits.org/).

| Path | Contents |
| --- | --- |
| `src/Codai.Cli/` | Terminal app, packaged as the `Codai` .NET tool |
| `src/Codai.Application/` | Host-neutral session runtime |
| `src/Codai.Core/` | Domain types and abstractions |
| `src/Codai.Adapters/` | Providers, configuration, MCP, tools, analysis |
| `tests/` | Test projects, one per area |
| `docs/` | Mintlify documentation site source |
| `assets/` | Images used by `README.md` |

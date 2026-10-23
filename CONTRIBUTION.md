## Contribution

This is great that you'd like to contribute to this project. All change requests should go through the steps described below.

## Pull Requests

**Please, make sure you open an issue before starting with a Pull Request, unless it's a typo or a really obvious error.** Pull requests are the best way to propose changes.

## Conventional commits

Our repository follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/#summary) specification. Releases are drafted from merged pull requests with [release-drafter](https://github.com/release-drafter/release-drafter).

Pull requests should have a title that follows the specification, otherwise, merging is blocked. If you are not familiar with the specification simply ask maintainers to modify. You can also use this cheatsheet if you want:

- `fix: ` prefix in the title indicates that PR is a bug fix and PATCH release must be triggered.
- `feat: ` prefix in the title indicates that PR is a feature and MINOR release must be triggered.
- `docs: ` prefix in the title indicates that PR is only related to the documentation and there is no need to trigger release.
- `chore: ` prefix in the title indicates that PR is only related to cleanup in the project and there is no need to trigger release.
- `test: ` prefix in the title indicates that PR is only related to tests and there is no need to trigger release.
- `refactor: ` prefix in the title indicates that PR is only related to refactoring and there is no need to trigger release.

## Development

Requires the [.NET SDK 10.0](https://dotnet.microsoft.com/download). The repository
pins it in `global.json`.

```bash
dotnet build Codai.slnx
dotnet test Codai.slnx
```

CI currently runs one Ubuntu build job using
`dotnet build Codai.slnx --configuration Release -warnaserror`. It does not run tests,
package/install checks, or a Windows matrix; run relevant tests locally. Release Drafter
and its automatic labels, release publishing, and documentation deployment remain separate
workflows. Scheduled Dependabot updates and the CodeQL workflow are disabled.

CI builds with `-warnaserror`, so a change must leave the build warning-free. Style and
analyzer rules are declared in `.editorconfig` and `Directory.Build.props`, not in the
IDE, so the same rules apply locally and on the build server. `dotnet format
Codai.slnx --no-restore` will apply the mechanical ones.

### Documentation

The docs site in `docs/` uses **Mintlify**, with the `mint` theme, native MDX components,
search, and system-aware light/dark appearance. Pages are `.mdx` files with `title` and
`description` frontmatter; `docs/docs.json` declares branding and navigation. Requires
Node.js 22 or later. Before pushing a docs change:

```bash
cd docs
npm ci
npm test        # documentation checker regression tests
npm run check   # registration, frontmatter, README routes, Mintlify broken links
npm run build   # Mintlify validation; does not export a static site
npm run dev     # local Mintlify preview at http://localhost:3000
```

Documentation accents, wordmarks, and favicon use the app's default
`deepskyblue1` (`#00AFFF`) in both light and dark modes.

[docs.yml](.github/workflows/docs.yml) validates documentation on pull requests and pushes.
It no longer builds or deploys GitHub Pages output. Mintlify is the actual rendering and
hosting engine, not a custom renderer styled to resemble it.

To publish, connect `meysamhadeli/codai` in the Mintlify dashboard, select the production
branch (`main`), and set the documentation directory to `docs`. Authorize its GitHub
integration and confirm the deployment in the dashboard. Pushes to the connected branch
then publish through Mintlify. Account setup, plan availability, GitHub authorization,
and any custom-domain DNS setup are managed outside this repository.

The existing https://meysamhadeli.github.io/codai URL remains in README links temporarily.
That legacy deployment does not receive these Mintlify changes. Mintlify cannot host at
the GitHub-owned `github.io` URL. Once Mintlify assigns a public URL (or a custom domain is
connected), verify it loads and update README links and the temporary `siteUrl` in
`docs/check.mjs` together. Do not advertise a new domain before it is live.

Register every new page in `docs/docs.json`, including nested paths without `.mdx`.
Use root-relative internal routes such as `/getting-started/quickstart` in MDX links and
cards. Keep Codai's branding and content; do not copy another project's screenshots or copy.

### Live provider tests

Tests that call a real provider are opt-in and never run in CI:

```bash
export API_KEY="your_deepseek_key"
export CODAI_RUN_INTEGRATION=1
dotnet test tests/Codai.Providers.Tests --filter FullyQualifiedName~DeepSeekLiveTests
```

### Conventions

- Fix the design, not the warning. If an analyzer fires, prefer changing the code to
  suppressing the rule; if you must suppress, do it narrowly and say why.
- Every collaborator is an interface, and it lives in an `Abstractions` folder next to
  the code that consumes it.
- Keep provider-specific behaviour inside `src/Codai.Adapters/Providers`. `Codai.Core` must not
  learn about a specific vendor.
- Add tests with the change. Bug fixes should arrive with a test that fails before the
  fix.
- Update `CHANGELOG.md`, `README.md` and the pages under `docs/` when behaviour a user
  can see changes. The detail belongs in `docs/`; `README.md` stays a short landing page
  that links to the published documentation.

### Releases and NuGet

Merged pull requests are grouped into a draft release by
[Release Drafter](.github/release-drafter.yml), which also decides the next version from
labels: `major`, `minor` (added automatically by a `feat:` title) or `patch` (added
automatically by a `fix:` title). Anything unlabelled is a patch.

Publishing the draft is the only manual step. [publish.yml](.github/workflows/publish.yml)
then stamps every package with the release tag, packs each project under `src/`, checks
that the `Codai` tool installs, and pushes to nuget.org. It needs the `NUGET_API_KEY`
repository secret; without it the workflow fails before packing.

The packages produced by a release are:

| Package | Contents |
| --- | --- |
| `Codai` | The terminal app, installed with `dotnet tool install --global Codai` |
| `Codai.Application` | Host-neutral session runtime |
| `Codai.Core` | Domain types and abstractions |
| `Codai.Adapters` | Providers, configuration, MCP, tools, analysis |

## Resources

- [How to Contribute to Open Source](https://opensource.guide/how-to-contribute/)
- [Using Pull Requests](https://help.github.com/articles/about-pull-requests/)
- [GitHub Help](https://help.github.com)

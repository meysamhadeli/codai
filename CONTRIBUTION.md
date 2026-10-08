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

CI builds with `-warnaserror`, so a change must leave the build warning-free. Style and
analyzer rules are declared in `.editorconfig` and `Directory.Build.props`, not in the
IDE, so the same rules apply locally and on the build server. `dotnet format
Codai.slnx --no-restore` will apply the mechanical ones.

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
- Keep provider-specific behaviour inside `src/Codai.Providers`. `Codai.Core` must not
  learn about a specific vendor.
- Add tests with the change. Bug fixes should arrive with a test that fails before the
  fix.
- Update `CHANGELOG.md` and `README.md` when behaviour a user can see changes.

## Resources

- [How to Contribute to Open Source](https://opensource.guide/how-to-contribute/)
- [Using Pull Requests](https://help.github.com/articles/about-pull-requests/)
- [GitHub Help](https://help.github.com)

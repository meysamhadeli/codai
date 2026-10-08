<!--
  Keep this short. Reviewers care about what changed and how you know it works.
-->

## What changed

<!-- One or two sentences. Link the issue this closes, if there is one: "Closes #123". -->

## Why

<!-- The problem this solves. Skip if the "what" makes it obvious. -->

## How it was verified

<!--
  Tick what applies and add the details. A reviewer should be able to reproduce
  your verification from what you write here.
-->

- [ ] `dotnet build Codai.slnx -warnaserror` is clean
- [ ] `dotnet test Codai.slnx` passes
- [ ] Added or updated tests for the change
- [ ] Updated `CHANGELOG.md`
- [ ] Updated `README.md` if user-visible behaviour changed

## Checklist

- [ ] Public API changes are documented with XML comments
- [ ] New collaborators are interfaces in an `Abstractions` folder
- [ ] No provider-specific knowledge leaked into `Codai.Core`
- [ ] No secrets, API keys or local paths are included in this diff

## Screenshots or output

<!-- For CLI or UI changes, show before and after. Otherwise delete this section. -->

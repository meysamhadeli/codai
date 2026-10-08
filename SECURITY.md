# Security Policy

## Supported versions

Codai ships as a set of NuGet packages and a CLI. Security fixes are issued for
the latest released minor version; older versions are expected to upgrade.

| Version | Supported |
| ------- | --------- |
| 2.x     | Yes       |
| < 2.0   | No        |

## Reporting a vulnerability

Please do **not** open a public issue for security problems.

Report privately through GitHub's
[private vulnerability reporting](https://github.com/meysamhadeli/codai/security/advisories/new)
form. If that is unavailable to you, contact a maintainer directly rather than
filing a public issue.

Please include:

- the version (`codai --version`) and the operating system you were running;
- a description of the issue and the impact you believe it has;
- the smallest reproduction you can produce;
- whether the issue is already public anywhere.

## What to expect

- We acknowledge a report within **3 business days**.
- We aim to publish a fix, or an explanation of why none is planned, within
  **30 days**.
- We will credit you in the release notes unless you ask us not to.

## Scope

In scope:

- Authentication and credential handling (API keys, configuration files,
  environment variables).
- Path handling: Codai reads and writes files inside your project directory, so
  a path that escapes that root is a security bug.
- Command execution and any code Codai runs on your behalf.
- Dependency vulnerabilities reachable from the shipped packages.

Out of scope:

- Prompt-injection content that a model returns as prose. Codai treats model
  output as untrusted and asks before applying edits; a suggested-but-unapplied
  change is not a vulnerability.
- The cost or quality of a model's answers.
- Vulnerabilities in provider APIs themselves; report those to the provider.

## Handling of your API keys

Codai reads provider credentials from the configuration file or the environment.
It never transmits them anywhere other than the provider endpoint you configure.
Key material is not written to logs or chat history on disk.

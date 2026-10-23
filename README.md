<h1 align="center">Codai</h1>

<p align="center">
The open source coding agent for your terminal.
</p>

<div align="center">
<table>
<tbody>
<td align="center">
<a href="https://meysamhadeli.github.io/codai" target="_blank"><strong>Docs</strong></a>
</td>
<td align="center">
<a href="https://github.com/meysamhadeli/codai/discussions" target="_blank"><strong>Discussions</strong></a>
</td>
<td align="center">
<a href="https://github.com/meysamhadeli/codai/issues" target="_blank"><strong>Issues</strong></a>
</td>
<td align="center">
<a href="CONTRIBUTION.md" target="_blank"><strong>Contributing</strong></a>
</td>
</tbody>
</table>
</div>

<br>

<p align="center">
  <img src="assets/codai-demo.gif" alt="Codai working in a terminal" />
</p>

---

## Install

```bash
dotnet tool install --global Codai
codai auth
codai code
```

Requires the [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later. Builds from
source and standalone binaries are covered in the [docs](https://meysamhadeli.github.io/codai).

## What it does

| | |
| --- | --- |
| **Edits across your project** | A signature map plus read, search and write tools, so changes land in the right files instead of a guess. [Tools →](https://meysamhadeli.github.io/codai/agent-mode) |
| **Runs your commands** | Build, test and lint from the session, with every write and command shown and approved first. [Agent mode →](https://meysamhadeli.github.io/codai/agent-mode) |
| **Plan, then act** | Explore and plan read-only, then execute the plan after consent. [Modes →](https://meysamhadeli.github.io/codai/agent-mode) |
| **Scrollable terminal** | Browse the conversation with the mouse wheel or scrollbar while the input and status footer stay pinned. [Terminal controls →](https://meysamhadeli.github.io/codai/agent-mode) |
| **Compact action steps** | Click to open thinking status, command output, and file diffs; edit summaries show added and removed line counts. [Transcript steps →](https://meysamhadeli.github.io/codai/agent-mode) |
| **Any provider, including local** | OpenAI, Azure OpenAI, Anthropic, Gemini, Mistral, Grok, DeepSeek, Qwen, OpenRouter and Ollama. [Providers →](https://meysamhadeli.github.io/codai) |
| **Learns your conventions** | `AGENTS.md`, rules, skills, commands and memory documents steer every request. [Rules and skills →](https://meysamhadeli.github.io/codai/skills) |
| **Long sessions stay affordable** | Conversations compact before they outgrow the window, and `--continue` resumes where you stopped. [Compaction →](https://meysamhadeli.github.io/codai/agent-mode) |
| **Extensible** | Plugins, MCP servers and lifecycle hooks under the same approval rules. [MCP →](https://meysamhadeli.github.io/codai/mcp) · [Plugins →](https://meysamhadeli.github.io/codai/plugins) |
| **Also a library** | Host the session runtime in your own .NET app. [Build on Codai →](https://meysamhadeli.github.io/codai) |

## Common tasks

```bash
codai --mode plan "Review authentication and plan improvements" # read-only
codai --mode plan "Plan pagination for orders"         # a numbered plan
codai code --continue                                  # resume the session
codai code --provider ollama --model llama3.1          # a local model
codai --json --mode plan "Plan fixes for TODO comments" # JSON Lines for scripts
```

Inside a session, `:help` lists the commands and `@src/Foo.cs` attaches a file to one request.
Every flag is documented in the [CLI reference](https://meysamhadeli.github.io/codai).

## Extend with plugins or MCP servers

Plugins add tools and lifecycle hooks, MCP servers connect databases, APIs and cloud
infrastructure, and hooks run commands around agent steps — all through the same approval
rules as the built-in tools. Manage servers with `codai config mcp` and plugins with
`codai config plugins`, or read the [plugins](https://meysamhadeli.github.io/codai/plugins) and
[MCP](https://meysamhadeli.github.io/codai/mcp) guides.

## Documentation

Everything beyond getting started lives in the [documentation](https://meysamhadeli.github.io/codai):

- [Agent mode](https://meysamhadeli.github.io/codai/agent-mode) — Plan and Agent, plus Plan → Act
- [Skills](https://meysamhadeli.github.io/codai/skills) — project and global skills
- [Plugins](https://meysamhadeli.github.io/codai/plugins) — extend tools and hooks
- [MCP](https://meysamhadeli.github.io/codai/mcp) — external tool servers
- [ACP compatibility](https://meysamhadeli.github.io/codai/acp-compatibility) — editor integration
- [Roadmap](https://meysamhadeli.github.io/codai/agent-mode-roadmap) — what comes next

## Contributing

Bug reports, feature requests and pull requests are welcome. Start with
[CONTRIBUTION.md](CONTRIBUTION.md); security issues should go through [SECURITY.md](SECURITY.md)
rather than a public issue.

## License

[Apache 2.0 © 2026 Codai contributors](./LICENSE)

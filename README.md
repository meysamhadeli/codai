# Codai

> 🤖 **AI coding agent for your terminal.**

Codai is an AI code assistant that works from the **full context of your project**.
It summarises your codebase, holds a session-based conversation, proposes changes and
applies the ones you accept — all from the terminal.

![](./assets/codai-demo.gif)

## ✨ Features

⚡ Supports a wide range of LLMs — OpenAI, Azure OpenAI, Anthropic, Gemini, Mistral, Grok, DeepSeek, Qwen, OpenRouter and Ollama.

🧠 Context-aware assistance built from a summary of your whole project.

📊 Maintains conversational and code context per session.

🌳 Summarises full project context by extracting code signatures.

➕ Adds new features or test cases.

🔄 Refactors code structure and efficiency.

🐛 Describes and suggests fixes for bugs.

✅ Reviews and optimises code quality.

✔️ Accepts and applies AI-generated code changes.

📚 Generates documentation.

🌐 Works with C#, Go, Python, Java, JavaScript and TypeScript.

⚙️ Configurable via file, environment variables or CLI flags.

🗂️ Lets the AI modify several files in one response.

💳 Tracks token consumption and estimated cost per request.

## 🚀 Get started

Requires the [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later.

```bash
dotnet build
dotnet run --project src/Codai.Cli -- code
```

Or publish a standalone binary:

```bash
dotnet publish src/Codai.Cli -c Release -o ./out
./out/codai code
```

### Zero setup

Set your API key once and it just works:

```bash
export API_KEY="your_api_key"
codai code
```

> [!IMPORTANT]
> Codai defaults to a sensible model per provider, so `--provider` alone is usually
> enough. DeepSeek always defaults to `deepseek-flash`. Use `--model` to choose a
> different model for the selected provider.
> *   [OpenAI](https://platform.openai.com/docs/api-reference/introduction)
> *   [Ollama](https://github.com/ollama/ollama/blob/main/docs/api.md)
> *   [Azure OpenAI](https://learn.microsoft.com/en-us/azure/ai-services/openai/reference)
> *   [Anthropic](https://docs.anthropic.com/en/api/getting-started)
> *   [Gemini](https://ai.google.dev/docs)
> *   [Mistral](https://docs.mistral.ai/)
> *   [Grok](https://docs.x.ai/docs)
> *   [Qwen](https://help.aliyun.com/zh/dashscope/developer-reference/overview)
> *   [DeepSeek](https://platform.deepseek.com/docs)
> *   [OpenRouter](https://openrouter.ai/docs/quick-start)

### ▶️ How to run

Navigate to the directory you want to analyse and start a session:

```bash
codai code
```

Each interaction is part of a session, so the assistant keeps the project and
conversation context across your requests.

## 🔧 Configuration

Configuration is resolved in increasing order of precedence:

**defaults → config file → environment variables → CLI flags**

Add a `codai-config.yml` to the root of the project you want to analyse:

```yml
ai_provider_config:
  provider: "deepseek"
  base_url: "https://api.deepseek.com"   # optional, provider default applies
  model: "deepseek-flash"                # optional, provider default applies
  api_key: "sk-..."                      # prefer the API_KEY env var
  api_version: "2024-04-01-preview"      # optional, Azure OpenAI / Anthropic
  temperature: 0.2                       # optional
  reasoning_effort: "low"                # optional
  max_tokens: 4096                       # optional
  stream: true                           # optional, default true
theme: "dracula"
```

If no config file is provided, the defaults are used. `.yml`, `.yaml` and `.json` are
all supported.

You can also point at a config file from anywhere, or override settings inline:

```bash
codai code --config ./codai-config.yml
codai code --provider deepseek --temperature 0.8
```

### Environment variables

`API_KEY`, `PROVIDER`, `BASE_URL`, `MODEL`, `TEMPERATURE`, `REASONING_EFFORT`,
`ENCODING_FORMAT`, `MAX_TOKENS`, `API_VERSION`, `THEME`.

Nested keys can also be supplied with the `__` separator, e.g.
`AI_PROVIDER_CONFIG__MODEL=deepseek-flash`.

### CLI flags

```
codai code [options]

  -c, --config <path>        Configuration file
      --provider <name>      Chat provider
      --base_url <url>       Provider endpoint override
      --model <name>         Model identifier
      --temperature <n>      Sampling temperature
      --reasoning_effort <l> low | medium | high
      --max_tokens <n>       Maximum output tokens
      --stream <bool>        Stream the response
      --api_key <key>        API key (prefer API_KEY)
      --api_version <v>      API version
  -v, --version              Print version
  -h, --help                 Print help
```

### Providers and defaults

| Provider | Default base URL | Default model |
| --- | --- | --- |
| `openai` | `https://api.openai.com/v1` | `gpt-4o` |
| `azure-openai` | `https://{your-resource}.openai.azure.com` | `gpt-4o` |
| `anthropic` | `https://api.anthropic.com/v1` | `claude-3-5-sonnet-latest` |
| `gemini` | `https://generativelanguage.googleapis.com/v1beta` | `gemini-1.5-pro` |
| `ollama` | `http://localhost:11434/api` | `llama3.1` |
| `deepseek` | `https://api.deepseek.com` | `deepseek-flash` |
| `openrouter` | `https://openrouter.ai/api/v1` | `openai/gpt-4o` |
| `qwen` | `https://dashscope.aliyuncs.com/compatible-mode/v1` | `qwen-plus` |
| `mistral` | `https://api.mistral.ai/v1` | `mistral-large-latest` |
| `grok` | `https://api.x.ai/v1` | `grok-2-latest` |

> **DeepSeek always defaults to `deepseek-flash`** unless a model is supplied explicitly.
> Verified against the live API: base URL `https://api.deepseek.com`, model
> `deepseek-flash`. The legacy aliases `deepseek-v4-flash` and
> `deepseek-v4-flash-vision-exp` are also accepted by the platform and billed at the
> Flash price.

### Ignoring files

Create a `.codai-gitignore` in the project root using gitignore-style patterns; matching
files are excluded from the project context.

## ⌨️ Session commands

| Command | Action |
| --- | --- |
| `:help` | List the session commands |
| `:clear` | Clear the screen |
| `:exit` | Leave the session |
| `:token` | Show token usage and estimated cost |
| `:clear-token` | Reset the token counters |
| `:clear-history` | Forget the conversation so far |

## 🏗️ Project layout

| Project | Responsibility |
| --- | --- |
| `src/Codai.Core` | Models, abstractions, prompt builder, chat history, token tracker, diff applier, requested-context resolver. No third-party dependencies beyond DI abstractions. |
| `src/Codai.Configuration` | `codai-config.yml\|yaml\|json` loading, environment variables, CLI flag parsing and provider defaults. |
| `src/Codai.Tokens` | Embedded `model_pricing.json` catalog and cost calculation. |
| `src/Codai.Providers` | `IChatCompletionService` implementations for all providers, built on `Microsoft.Extensions.AI`. |
| `src/Codai.Analysis` | Project scanner, Roslyn C# signature extractor, regex extractors for Go/Python/Java/JS/TS, ignore rules. |
| `src/Codai.Session` | The interactive `CodeSession` orchestrator and slash-command handling. |
| `src/Codai.Cli` | `codai` executable: argument handling, Spectre.Console user interface, DI composition. |
| `tests/*` | xUnit + FluentAssertions test projects, one per component. |

### Design notes

- **Abstractions first.** Every collaborator is an interface in `Codai.Core.Abstractions`
  (`IProjectScanner`, `IPromptBuilder`, `IChatCompletionService`, `IDiffApplier`,
  `IUserInterface`, …) so the whole session can be tested without a network or a disk.
- **Source-generated JSON.** All serialization uses `JsonSerializerContext`, keeping the
  binary trim/AOT friendly.
- **Central package management.** Versions live in `Directory.Packages.props`; projects
  reference packages without versions.

### How the session works

```mermaid
sequenceDiagram
    participant U as User
    participant S as CodeSession
    participant SC as ProjectScanner
    participant AI as IChatCompletionService
    participant D as DiffApplier

    S->>SC: Scan(projectRoot)
    SC-->>S: ProjectContext (signatures)
    U->>S: "add a feature"
    S->>AI: stream(system prompt + user request)
    AI-->>S: response chunks
    opt Response asks for full files
        S->>U: "Add these files to context? (y/n)"
        S->>AI: stream(again, with full contents)
        AI-->>S: response chunks
    end
    S->>S: extract code changes
    U->>S: accept / reject each file
    S->>D: apply accepted changes
    S-->>U: token usage and cost
```

## 🧪 Tests

```bash
dotnet test
```

**252 tests** covering the core, configuration, pricing, providers, analysis, session
and CLI.

### Live integration tests

The DeepSeek integration tests are opt-in and never run by default:

```bash
export API_KEY="your_deepseek_key"
export CODAI_RUN_INTEGRATION=1
dotnet test tests/Codai.Providers.Tests --filter FullyQualifiedName~DeepSeekLiveTests
```

Set `DEEPSEEK_MODEL` to override the model used by that test.

> [!NOTE]
> These assertions read `API_KEY` from the environment. Windows environment variables are
> inherited at process start, so a shell opened *before* the variable changed keeps the
> stale value — open a new terminal after changing it.

### Parity tests

`tests/Codai.Core.Tests/PromptParityTests.cs` re-implements the original Go
`GeneratePrompt` and `AddToHistory` formatting and asserts the prompt builder produces a
byte-identical string. `PricingParityTests` does the same for the pricing catalog.

Both compare against frozen copies of the original Go artifacts, embedded into the test
assembly from `tests/Codai.Core.Tests/Golden/`. The tests fail loudly if a golden is
missing, so prompt and pricing drift can never go unnoticed.

## 📦 Packaging

The CLI publishes as a single framework-dependent binary, and is trim/AOT compatible:

```bash
dotnet publish src/Codai.Cli -c Release -o ./out
```

## 🗺️ Plan

🌀 This project is a work in progress; new features will be added over time. 🌀

Feature requests and bugs are tracked in [Issues](https://github.com/meysamhadeli/codai/issues).

# 🌟 Support

If you like this work, feel free to ⭐ this repository. Thanks a bunch for supporting me!

## 🤝 Contribution

Thanks to all [contributors](https://github.com/meysamhadeli/codai/graphs/contributors) — you're awesome, and this wouldn't be possible without you!

Please follow the [contribution guideline](./CONTRIBUTION.md) to submit a pull request or create an issue.

## 📄 License

[Apache 2.0](./LICENSE)

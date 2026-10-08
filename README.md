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

## 🚀 Implementations

This repository contains a **C# / .NET 10** implementation.

| Implementation | Location | Docs |
| --- | --- | --- |
| C# / .NET 10 | [`dotnet/`](./dotnet) | [dotnet/README.md](./dotnet/README.md) |

### Quick start

Requires the [.NET SDK 10.0](https://dotnet.microsoft.com/download) or later.

```bash
cd dotnet
dotnet build
dotnet run --project src/Codai.Cli -- code
```

**Zero setup** — provide your API key and it just works:

```bash
export API_KEY="your_api_key"
```

> [!NOTE]
> Codai defaults to a sensible model per provider. DeepSeek always defaults to
> `deepseek-flash`. Select a provider with `--provider` and a model with `--model`.

Supported providers:

- [OpenAI](https://platform.openai.com/docs/api-reference/introduction)
- [Azure OpenAI](https://learn.microsoft.com/en-us/azure/ai-services/openai/reference)
- [Anthropic](https://docs.anthropic.com/en/api/getting-started)
- [Gemini](https://ai.google.dev/docs)
- [Mistral](https://docs.mistral.ai/)
- [Grok](https://docs.x.ai/docs)
- [DeepSeek](https://platform.deepseek.com/docs)
- [Qwen](https://help.aliyun.com/zh/dashscope/developer-reference/overview)
- [OpenRouter](https://openrouter.ai/docs/quick-start)
- [Ollama](https://github.com/ollama/ollama/blob/main/docs/api.md)

### Configuration

Add a `codai-config.yml` to the root of the project you want to analyse, set
environment variables, or pass flags on the command line. Precedence runs
**defaults → config file → environment variables → CLI flags**.

```yml
ai_provider_config:
  provider: "deepseek"
  base_url: "https://api.deepseek.com"   # optional, provider default applies
  model: "deepseek-flash"                # optional, provider default applies
  api_key: "sk-..."                      # prefer the API_KEY environment variable
  api_version: "2024-04-01-preview"      # optional, Azure OpenAI / Anthropic
  temperature: 0.2                       # optional
  reasoning_effort: "low"                # optional
  max_tokens: 4096                       # optional
theme: "dracula"
```

You can also point at a config file from any directory, or override settings inline:

```bash
codai code --config ./codai-config.yml
codai code --provider deepseek --temperature 0.8
```

Add a `.codai-gitignore` to the project root to exclude files from the project context.

See [dotnet/README.md](./dotnet/README.md) for the full configuration and provider
default reference.

### Session commands

| Command | Action |
| --- | --- |
| `:help` | List the session commands |
| `:clear` | Clear the screen |
| `:exit` | Leave the session |
| `:token` | Show token usage and estimated cost |
| `:clear-token` | Reset the token counters |
| `:clear-history` | Forget the conversation so far |

### Running

Navigate to the directory you want to analyse and start a session:

```bash
codai code
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

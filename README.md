[![CI](https://github.com/meysamhadeli/codai/actions/workflows/ci.yml/badge.svg?branch=main&style=flat-square)](https://github.com/meysamhadeli/codai/actions/workflows/ci.yml)
![Go Version](https://img.shields.io/badge/go%20version-%3E=1.23-61CFDD.svg?style=flat-square)
[![Apache License](https://img.shields.io/badge/license-Apache_2.0-blue.svg)](https://github.com/meysamhadeli/codai/blob/main/LICENSE)
[![Go Reference](https://pkg.go.dev/badge/github.com/meysamhadeli/codai.svg)](https://pkg.go.dev/github.com/meysamhadeli/codai)

# Codai

> 💡 **Codai is an AI code assistant designed to help developers efficiently manage their daily tasks through a session-based CLI, such as adding new features, refactoring,
and performing detailed code reviews. What makes codai stand out is its deep understanding of the entire context of your project, enabling it to analyze your code base
and suggest improvements or new code based on your context. This AI-powered tool supports multiple LLM providers, such as OpenAI, Azure OpenAI, Ollama, Anthropic, and OpenRouter.**

![](./assets/codai-demo.gif)


> We use **two** main methods to **manage** and **summarize full context**: 

1. **RAG** (Retrieval-Augmented Generation)
   
2. **Summarize Full Context of Code with Tree-sitter**.

Each method has its own benefits and is chosen depending on the specific needs of the request. Below is a description of each method.

## 📚 RAG
The codai uses **RAG** (Retrieval-Augmented Generation) to **improve code suggestions** by **embedding** and **retrieving the most relevant** information based on
**user input**. **RAG generates embeddings for the entire code context**, allowing the AI to **dynamically find the most relevant details**. By **connecting** to an **embedding model**,
codai **retrieves the just necessary context**, which is then sent with the user’s query to the code-suggestion AI model. This approach **reduces token usage** and provides accurate,
helpful responses, making it the recommended method.

## 🌳 Summarize Full Context of Code with Tree-sitter
Another approach involves creating a **summary of the full context of project** with **Tree-sitter** and in this approach we just send the **signature body of our code** without **full implementation of code block** to the AI. When a **user requests a specific part of code**,
the system can **retrieve the full context for just that section**. This approach also **saves tokens** because it just **sends only completed parts**, but
it usually uses **slightly more tokens than the RAG method**. In **RAG**, only the **related context send to the AI** for **saving even more tokens**.


## 🚀 Get Started
To install `codai` globally, you can use the following command:

```bash
go install github.com/meysamhadeli/codai@latest
```

### ⚙️ Set Environment Variables
To use codai, you need to set your environment variable for the API key.

For `Bash`, use:
```bash
export CHAT_API_KEY="your_chat_api_key"
export EMBEDDINGS_API_KEY="your_embeddings_api_key"     #(Optional, If you want use RAG.)
```

For `PowerShell`, use:
```powershell
$env:CHAT_API_KEY="your_chat_api_key"
$env:EMBEDDINGS_API_KEY="your_embeddings_api_key"     #(Optional, If you want use RAG.) 
```
### 🔧 Configuration
`codai` requires a `codai-config.yml` file in the `root of your working directory` or using `environment variables` to set below configs `globally` as a configuration.

The `codai-config` file should be like following example base on your `AI provider`:

**codai-config.yml**
```yml
ai_provider_config:
  chat_provider_name: "openai"     # openai | ollama | azure-openai | anthropic | openrouter
  chat_base_url: "https://api.openai.com"     # "http://localhost:11434" | "https://test,openai.azure.com" | "https://api.anthropic.com" | "https://openrouter.ai"
  chat_model: "gpt-4o"
  chat_api_version: "2024-04-01-preview"     #(Optional, If your AI provider like 'AzureOpenai' or 'Anthropic' has chat api version.)
  embeddings_provider_name: "openai"     # openai | ollama | azure-openai
  embeddings_base_url: "https://api.openai.com"     # "http://localhost:11434" | "https://test,openai.azure.com"
  embeddings_model: "text-embedding-3-small"     #(Optional, If you want use 'RAG'.)
  embeddings_api_version: "2024-01-01-preview"     #(Optional, If your AI provider like 'AzureOpenai' has embeddings api version.)
  temperature: 0.2
  threshold: 0.2     #(Optional, If you want use 'RAG'.)
theme: "dracula"
rag: true     #(Optional, If you want use 'RAG'.)
```

> Note: We used the standard integration of [OpenAI APIs](https://platform.openai.com/docs/api-reference/introduction), [Ollama APIs](https://github.com/ollama/ollama/blob/main/docs/api.md), [Azure Openai](https://learn.microsoft.com/en-us/azure/ai-services/openai/reference), [Anthropic](https://docs.anthropic.com/en/api/getting-started), [OpenRouter](https://openrouter.ai/docs/quick-start) and you can find more details in documentation of each AI provider APIs.

If you wish to customize your configuration, you can create your own `codai-config.yml` file and place it in the `root directory` of `each project` you want to analyze with codai. If `no configuration` file is provided, codai will use the `default settings`.

You can also specify a configuration file from any directory by using the following CLI command:
```bash
codai code --config ./codai-config.yml
```
Additionally, you can pass configuration options directly in the command line. For example:
```bash
codai code --provider_name openapi --temperature 0.8 --chat_api_key test-chat-key --embeddings_api_key test-embeddings-key
```
This flexibility allows you to customize config of codai on the fly.


**.codai-gitignore**

Also, you can use `.codai-gitignore` in the `root of your working directory,` and codai will ignore the files that we specify in our `.codai-gitignore`.

> Note: We used [Chroma](https://github.com/alecthomas/chroma) for `style` of our `text` and `code block`, and you can find more theme here in [Chroma Style Gallery](https://xyproto.github.io/splash/docs/) and use it as a `theme` in `codai`.

## 🔮 LLM Models
### ⚡ Best Models
The codai works well with advanced LLM models specifically designed for code generation, including `GPT-4o`, `GPT-4`, `Claude 3.5 Sonnet` and `Claude 3 Opus`. These models leverage the latest in AI technology, providing powerful capabilities for understanding and generating code, making them ideal for enhancing your development workflow.

### 💻 Local Models
In addition to cloud-based models, codai is compatible with local models such as `Ollama`. To achieve the best results, it is recommended to utilize models like [Phi-3-medium instruct (128k)](https://github.com/marketplace/models/azureml/Phi-3-medium-128k-instruct), [Mistral Large (2407)](https://github.com/marketplace/models/azureml-mistral/Mistral-large-2407) and [Meta-Llama-3.1-70B-Instruct](https://github.com/marketplace/models/azureml-meta/Meta-Llama-3-1-70B-Instruct). These models have been optimized for coding tasks, ensuring that you can maximize the efficiency and effectiveness of your coding projects.

### 🌐 OpenAI Embedding Models
The codai platform uses `OpenAI embedding models` to retrieve `relevant content` with high efficiency. Recommended models include are **text-embedding-3-large**, **text-embedding-3-small**, and **text-embedding-ada-002**, both known for their `cost-effectiveness` and `accuracy` in `capturing semantic relationships`. These models are ideal for applications needing high-quality performance in `code context retrieval`.

### 🦙 Ollama Embedding Models
codai also supports `Ollama embedding models` for `local`, `cost-effective`, and `efficient` embedding generation and `retrieval of relevant content`. Models such as **mxbai-embed-large**, **all-minilm**, and **nomic-embed-text** provide **effective**, **private embedding** creation optimized for high-quality performance. These models are well-suited for `RAG-based retrieval in code contexts`, eliminating the need for external API calls.

## ▶️ How to Run
To use `codai` as your code assistant, navigate to the directory where you want to apply codai and run the following command:

```bash
codai code
```
This command will initiate the codai assistant to help you with your coding tasks with understanding the context of your code.

## ✨ Features

🧠 Context-aware code completions.

➕ Adding new features or test cases.

🔄 Refactoring code structure and efficiency.

🐛 Describe and suggest fixes for bugs.

✅ Code Review Assistance and optimize code quality.

✔️ Accept and apply AI-generated code changes.

📚 Generate comprehensive documentation.

🌐 Works with multiple programming languages such as (C#, Go, Python, Java, Javascript, Typescript).

⚙️ Adjust settings via a config file.

📊 Maintain understanding of the entire project.

🔍 Retrieve relevant context for accurate suggestions using RAG.

🌳 Summarize Full Project Context using Tree-sitter.

⚡ Support variety of LLM models like GPT-4o, GPT-4, and Ollama.

🗂️ Enable the AI to modify several files at the same time.

💳 Track and represent the token consumption for each request.

## 🗺️ Plan
🌀 This project is a work in progress; new features will be added over time. 🌀

I will try to add new features in the [Issues](https://github.com/meysamhadeli/codai/issues) section of this app.

# 🌟 Support

If you like my work, feel free to:

- ⭐ this repository. And we will be happy together :)

Thanks a bunch for supporting me!

## 🤝 Contribution

Thanks to all [contributors](https://github.com/meysamhadeli/codai/graphs/contributors), you're awesome and this wouldn't be possible without you! The goal is to build a categorized, community-driven collection of very well-known resources.

Please follow this [contribution guideline](./CONTRIBUTION.md) to submit a pull request or create the issue.

[![CI](https://github.com/meysamhadeli/codai/actions/workflows/ci.yml/badge.svg?branch=main&style=flat-square)](https://github.com/meysamhadeli/codai/actions/workflows/ci.yml)
![Go Version](https://img.shields.io/badge/go%20version-%3E=1.23-61CFDD.svg?style=flat-square)
[![Apache License](https://img.shields.io/badge/license-Apache_2.0-blue.svg)](https://github.com/meysamhadeli/codai/blob/main/LICENSE)
[![Go Reference](https://pkg.go.dev/badge/github.com/meysamhadeli/codai.svg)](https://pkg.go.dev/github.com/meysamhadeli/codai)

# codai

> 💡 **codai is an AI code assistant designed to help developers efficiently manage their daily tasks through a session-based CLI, such as adding new features, refactoring,
and performing detailed code reviews. What makes codai stand out is its deep understanding of the entire context of your project, enabling it to analyze your code base
and suggest improvements or new code based on your context. This AI-powered tool supports multiple LLM models, including GPT-4, GPT-4o, GPT-4o mini, Ollama, and more.**

We use **two** main methods to manage context: **RAG** (Retrieval-Augmented Generation) and **Summarize Full Context of Code**.
Each method has its own benefits and is chosen depending on the specific needs of the request. Below is a description of each method.

## 📚 RAG
The codai uses **RAG** (Retrieval-Augmented Generation) to **improve code suggestions** by **embedding** and **retrieving the most relevant** information based on
**user input**. **RAG generates embeddings for the entire code context**, allowing the AI to **dynamically find the most relevant details**. By **connecting** to an **embedding model**,
codai **retrieves the just necessary context**, which is then sent with the user’s query to the code-suggestion AI model. This approach **reduces token usage** and provides accurate,
helpful responses, making it the recommended method.

## 🧩 Summarize Full Context of Code
Another approach involves creating a **summary of the full context of project** and sending it to the AI. When a **user requests a specific part of code**,
the system can **retrieve the full context for just that section**. This method also **saves tokens** because it **sends only relevant parts**, but
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
export API_KEY="your_api_key"
```

For `PowerShell`, use:
```powershell
$env:API_KEY="your_api_key""
```
### 🔧 Configuration
`codai` requires a `config.yml` file in the root of your working directory to analyze your project. By default, the `config.yml` contains the following values:
```yml
ai_provider_config:
  provider_name: "openai"
  chat_completion_url: "http://localhost:11434/v1/chat/completions"
  chat_completion_model: "gpt-4o"
  embedding_url: "http://localhost:11434/v1/embeddings" (Optional, If you want use RAG.)
  embedding_model: "text-embedding-ada-002" (Optional, If you want use RAG.)
  temperature: 0.2
  max_tokens: 128000
theme: "dracula"
RAG: true (Optional, if you want, can disable RAG.)
```
If you wish to customize your configuration, you can create your own `config.yml` file and place it in the `root directory` of each project you want to analyze with codai. If no configuration file is provided, codai will use the default settings.

You can also specify a configuration file from any directory by using the following CLI command:
```bash
codai code --config ./config.yml
```
Additionally, you can pass configuration options directly in the command line. For example:
```bash
codai code --provider_name openapi --temperature 0.8
```
This flexibility allows you to customize config of codai on the fly.

> Note: We use [Chroma](https://github.com/alecthomas/chroma) for `style` of our `text` and `code block`, and you can find more theme here in [Chroma Style Gallery](https://xyproto.github.io/splash/docs/) and use it as a `theme` in `codai`.

## 🔮 LLM Models
### ⚡ Best Models
The codai works well with advanced LLM models specifically designed for code generation, including `GPT-4`, `GPT-4o`, and `GPT-4o mini`. These models leverage the latest in AI technology, providing powerful capabilities for understanding and generating code, making them ideal for enhancing your development workflow.

### 💻 Local Models
In addition to cloud-based models, codai is compatible with local models such as `Ollama`. To achieve the best results, it is recommended to utilize models like `DeepSeek-Coder-v2`, `CodeLlama`, and `Mistral`. These models have been optimized for coding tasks, ensuring that you can maximize the efficiency and effectiveness of your coding projects.

### 🌐 OpenAI Embedding Models
The codai can utilize `OpenAI’s embedding models` to retrieve the `most relevant content`. The current recommended model for `code context` is `text-embedding-ada-002`, known for its high performance and capability in capturing semantic relationships, making it an excellent choice for accurate and efficient embedding retrieval.

### 🦙 Ollama Embedding Models
The codai also supports `Ollama embedding models`, allowing `local embedding` generation and retrieval. A suitable option here is the `nomic-embed-text model`, which provides efficient embedding generation locally, aiding in effective RAG-based retrieval `for relevant code context`.

How to Run
To use `codai` as your code assistant, navigate to the directory where you want to apply codai and run the following command:

```bash
codai code
```
This command will initiate the codai assistant to help you with your coding tasks with undrestanding the context of your code.

## ✨ Features

🧠 **Intelligent Code Suggestions:**
Provide context-aware code completion and suggestions as you type.

➕ **Add New Features or Test Cases:**
Suggest new features or generate test cases based on existing code, helping to enhance functionality and ensure code reliability.

🔄 **Code Refactoring:**
Provide specific suggestions to improve the structure and efficiency of existing code, making it cleaner and easier to maintain.

🐛 **Describe a Bug:**
Users can describe bugs in their code, allowing the AI to analyze the issue and provide targeted suggestions for resolution or debugging steps.

✅ **Code Review Assistance:**
Analyze code and identify potential bugs, providing suggestions to refactor for cleaner and better-performing code.

✔️ **Direct Code Acceptance:**
After receiving code suggestions from the AI, users can directly accept changes, which will be applied immediately to the codebase.

📚 **Documentation Generation:**
Automatically generate documentation based on the codebase, including function descriptions, parameter details, and usage examples.

🌐 **Multi-Language Support:**
Support for various programming languages `(C#, Go, Python, Java, Javascript, Typescript)` to cater to a wider range of developers.

⚙️ **Customizable Config:**
Allow users to customize settings through a config file (e.g., changing AI provider, tuning temperature settings).

📊 **Project Context Awareness:**
Maintain awareness of the entire project context to provide more accurate suggestions.

🌳 **Full Project Context Summarization:** 
Summarize the full context of your codebase using Tree-sitter for accurate and efficient code analysis.

🔍 **RAG System Implementation:**
Implement a Retrieval-Augmented Generation system to improve the relevance and accuracy of code suggestions by retrieving relevant context from the project.

⚡ **Support variety of LLM models:**
Work with advanced LLM models like `GPT-4, GPT-4o, GPT-4o mini and Ollama` to get high-quality code suggestions and interactions.

🗂️ **Edit Multiple Files at Once:**
Enable the AI to modify several files at the same time, making it easier to handle complex requests that need changes in different areas of the code.

💳 **Token Management:**
Track and represent the token consumption for each request, providing transparency on how many tokens are used, which helps in managing API costs effectively.

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

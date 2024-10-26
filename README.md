# codai

> **codai is a powerful AI code assistant designed to help developers efficiently manage their daily tasks through a session-based CLI, such as adding new features, refactoring,
and performing detailed code reviews. What makes codai stand out is its deep understanding of the entire context of your project, 
enabling it to analyze your code base and suggest improvements or new code based on that context. This AI-powered tool supports multiple
LLM models, including GPT-3.5, GPT-4, Ollama, and more.**

## 🚀 Get Started
To install **codai** globally, you can use the following command:

```bash
go install github.com/meysamhadeli/codai@latest
```

### ⚙️ Set Environment Variables
To use codai, you need to set your environment variable for the API key.

For **Bash**, use:
```bash
export API_KEY="your_api_key"
```

For **PowerShell**, use:
```powershell
$env:API_KEY="your_api_key""
```
### 🔧 Configuration
**codai** requires a `config.yml` file in the root of your working directory to analyze your project. By default, the `config.yml` contains the following values:
```yml
ai_provider_config:
  provider_name: "ollama"
  embedding_url: "http://localhost:11434/v1/embeddings"
  embedding_model: "nomic-embed-text"
  chat_completion_url: "http://localhost:11434/v1/chat/completions"
  chat_completion_model: "deepseek-coder-v2"
  temperature: 0.2
  buffering_theme: "dracula"
```
If you wish to customize your configuration, you can create your own config.yml file and place it in the root directory of each project you want to analyze with codai. If no configuration file is provided, codai will use the default settings.

You can also specify a configuration file from any directory by using the following CLI command:
```bash
codai code --config ./config.yml
```
Additionally, you can pass configuration options directly in the command line. For example:
```bash
codai code --provider_name openapi --temperature 0.8
```
This flexibility allows you to customize config of codai on the fly.

How to Run
To use **codai** as your code assistant, navigate to the directory where you want to apply codai and run the following command:

```bash
codai code
```
This will initiate the AI assistant to help you with your coding tasks with undrestanding the context of your code.

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
Support for various programming languages `(C#, Go)` to cater to a wider range of developers. (We will support other languages soon...)

⚙️ **Customizable Config:**
Allow users to customize settings through a config file (e.g., changing AI provider, tuning temperature settings).

📊 **Project Context Awareness:**
Maintain awareness of the entire project context to provide more accurate suggestions.

🌳 **Full Project Context Summarization:** 
Summarize the full context of your codebase using Tree-sitter for accurate and efficient code analysis.

🔍 **RAG System Implementation:**
Implement a Retrieval-Augmented Generation system to improve the relevance and accuracy of code suggestions by retrieving relevant context from the project.

⚡ **Support variety of LLM models:**
Work with advanced LLM models like `GPT-3.5, GPT-4, and Ollama`, ensuring high-quality suggestions and interactions.

🗂️ **Edit Multiple Files at Once:**
Enable the AI to modify several files at the same time, making it easier to handle complex requests that need changes in different areas of the code.

💳 **Token Management:**
Track and represent the token consumption for each request, providing transparency on how many tokens are used, which helps in managing API costs effectively.

## 🗺️ Plan
🌀 This project is a work in progress; new features will be added over time. 🌀

I will try to add new features in the [Issues](https://github.com/meysamhadeli/codai/issues) section of this app.

# Support

If you like my work, feel free to:

- ⭐ this repository. And we will be happy together :)

Thanks a bunch for supporting me!

## Contribution

Thanks to all [contributors](https://github.com/meysamhadeli/codai/graphs/contributors), you're awesome and this wouldn't be possible without you! The goal is to build a categorized, community-driven collection of very well-known resources.

Please follow this [contribution guideline](./CONTRIBUTION.md) to submit a pull request or create the issue.

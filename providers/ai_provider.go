package providers

import (
	"errors"
	"github.com/meysamhadeli/codai/providers/anthropic"
	"github.com/meysamhadeli/codai/providers/azure_openai"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/deepseek"
	"github.com/meysamhadeli/codai/providers/gemini"
	"github.com/meysamhadeli/codai/providers/grok"
	"github.com/meysamhadeli/codai/providers/mistral"
	"github.com/meysamhadeli/codai/providers/ollama"
	"github.com/meysamhadeli/codai/providers/openai"
	"github.com/meysamhadeli/codai/providers/openrouter"
	"github.com/meysamhadeli/codai/providers/qwen"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
)

type AIProviderConfig struct {
	ProviderName    string   `mapstructure:"provider_name"`
	BaseURL         string   `mapstructure:"base_url"`
	Model           string   `mapstructure:"model"`
	Stream          bool     `mapstructure:"stream"`
	Temperature     *float32 `mapstructure:"temperature"`
	ReasoningEffort *string  `mapstructure:"reasoning_effort"`
	EncodingFormat  string   `mapstructure:"encoding_format"`
	MaxTokens       int      `mapstructure:"max_tokens"`
	ApiKey          string   `mapstructure:"api_key"`
	ApiVersion      string   `mapstructure:"api_version"`
}

// ChatProviderFactory creates a Provider based on the given provider config.
func ChatProviderFactory(config *AIProviderConfig, tokenManagement contracts2.ITokenManagement) (contracts.IChatAIProvider, error) {
	switch config.ProviderName {
	case "ollama":
		return ollama.NewOllamaChatProvider(&ollama.OllamaConfig{
			Temperature:     config.Temperature,
			ReasoningEffort: config.ReasoningEffort,
			EncodingFormat:  config.EncodingFormat,
			Model:           config.Model,
			BaseURL:         config.BaseURL,
			MaxTokens:       config.MaxTokens,
			TokenManagement: tokenManagement,
		}), nil
	case "deepseek":
		return deepseek.NewDeepSeekChatProvider(&deepseek.DeepSeekConfig{
			Temperature:     config.Temperature,
			ReasoningEffort: config.ReasoningEffort,
			EncodingFormat:  config.EncodingFormat,
			Model:           config.Model,
			BaseURL:         config.BaseURL,
			ApiKey:          config.ApiKey,
			MaxTokens:       config.MaxTokens,
			TokenManagement: tokenManagement,
		}), nil
	case "openai":
		return openai.NewOpenAIChatProvider(&openai.OpenAIConfig{
			Temperature:     config.Temperature,
			ReasoningEffort: config.ReasoningEffort,
			EncodingFormat:  config.EncodingFormat,
			Model:           config.Model,
			BaseURL:         config.BaseURL,
			ApiKey:          config.ApiKey,
			MaxTokens:       config.MaxTokens,
			TokenManagement: tokenManagement,
			ApiVersion:      config.ApiVersion,
		}), nil
	case "azure-openai":
		return azure_openai.NewAzureOpenAIChatProvider(&azure_openai.AzureOpenAIConfig{
			Temperature:     config.Temperature,
			ReasoningEffort: config.ReasoningEffort,
			EncodingFormat:  config.EncodingFormat,
			Model:           config.Model,
			BaseURL:         config.BaseURL,
			ApiKey:          config.ApiKey,
			MaxTokens:       config.MaxTokens,
			TokenManagement: tokenManagement,
			ApiVersion:      config.ApiVersion,
		}), nil

	case "openrouter":
		return openrouter.NewOpenRouterChatProvider(&openrouter.OpenRouterConfig{
			Temperature:     config.Temperature,
			ReasoningEffort: config.ReasoningEffort,
			EncodingFormat:  config.EncodingFormat,
			Model:           config.Model,
			BaseURL:         config.BaseURL,
			ApiKey:          config.ApiKey,
			MaxTokens:       config.MaxTokens,
			TokenManagement: tokenManagement,
			ApiVersion:      config.ApiVersion,
		}), nil

	case "anthropic":
		return anthropic.NewAnthropicMessageProvider(&anthropic.AnthropicConfig{
			Temperature:     config.Temperature,
			EncodingFormat:  config.EncodingFormat,
			Model:           config.Model,
			BaseURL:         config.BaseURL,
			ApiKey:          config.ApiKey,
			MaxTokens:       config.MaxTokens,
			TokenManagement: tokenManagement,
			ApiVersion:      config.ApiVersion,
		}), nil

	case "gemini":
		return gemini.NewGeminiChatProvider(&gemini.GeminiConfig{
			Temperature:     config.Temperature,
			Model:           config.Model,
			BaseURL:         config.BaseURL,
			ApiKey:          config.ApiKey,
			MaxTokens:       config.MaxTokens,
			TokenManagement: tokenManagement,
		}), nil

	case "qwen":
		return qwen.NewQwenChatProvider(&qwen.QwenConfig{
			Temperature:     config.Temperature,
			Model:           config.Model,
			BaseURL:         config.BaseURL,
			ApiKey:          config.ApiKey,
			MaxTokens:       config.MaxTokens,
			TokenManagement: tokenManagement,
		}), nil

	case "mistral":
		return mistral.NewMistralChatProvider(&mistral.MistralConfig{
			Temperature:     config.Temperature,
			Model:           config.Model,
			BaseURL:         config.BaseURL,
			ApiKey:          config.ApiKey,
			MaxTokens:       config.MaxTokens,
			TokenManagement: tokenManagement,
		}), nil

	case "grok":
		return grok.NewGrokChatProvider(&grok.GrokConfig{
			Temperature:     config.Temperature,
			Model:           config.Model,
			BaseURL:         config.BaseURL,
			ApiKey:          config.ApiKey,
			MaxTokens:       config.MaxTokens,
			ApiVersion:      config.ApiVersion,
			TokenManagement: tokenManagement,
		}), nil
	default:

		return nil, errors.New("unsupported provider")
	}
}

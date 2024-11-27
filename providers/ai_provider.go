package providers

import (
	"errors"
	azure_openai "github.com/meysamhadeli/codai/providers/azure-openai"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/ollama"
	"github.com/meysamhadeli/codai/providers/openai"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
)

type AIProviderConfig struct {
	ChatProviderName       string  `mapstructure:"chat_provider_name"`
	EmbeddingsProviderName string  `mapstructure:"embeddings_provider_name"`
	ChatBaseURL            string  `mapstructure:"chat_base_url"`
	EmbeddingsBaseURL      string  `mapstructure:"embeddings_base_url"`
	EmbeddingsModel        string  `mapstructure:"embeddings_model"`
	ChatModel              string  `mapstructure:"chat_model"`
	Stream                 bool    `mapstructure:"stream"`
	Temperature            float32 `mapstructure:"temperature"`
	EncodingFormat         string  `mapstructure:"encoding_format"`
	MaxTokens              int     `mapstructure:"max_tokens"`
	Threshold              float64 `mapstructure:"threshold"`
	ChatApiKey             string  `mapstructure:"chat_api_key"`
	EmbeddingsApiKey       string  `mapstructure:"embeddings_api_key"`
	ChatApiVersion         string  `mapstructure:"chat_api_version"`
	EmbeddingsApiVersion   string  `mapstructure:"embeddings_api_version"`
}

// ChatProviderFactory creates a Provider based on the given provider config.
func ChatProviderFactory(config *AIProviderConfig, tokenManagement contracts2.ITokenManagement) (contracts.IChatAIProvider, error) {
	switch config.ChatProviderName {
	case "ollama":
		return ollama.NewOllamaChatProvider(&ollama.OllamaConfig{
			Temperature:     config.Temperature,
			EncodingFormat:  config.EncodingFormat,
			ChatModel:       config.ChatModel,
			EmbeddingsModel: config.EmbeddingsModel,
			ChatBaseURL:     config.ChatBaseURL,
			MaxTokens:       config.MaxTokens,
			Threshold:       config.Threshold,
			TokenManagement: tokenManagement,
		}), nil
	case "openai":
		return openai.NewOpenAIChatProvider(&openai.OpenAIConfig{
			Temperature:          config.Temperature,
			EncodingFormat:       config.EncodingFormat,
			ChatModel:            config.ChatModel,
			EmbeddingsModel:      config.EmbeddingsModel,
			ChatBaseURL:          config.ChatBaseURL,
			ChatApiKey:           config.ChatApiKey,
			EmbeddingsApiKey:     config.EmbeddingsApiKey,
			MaxTokens:            config.MaxTokens,
			Threshold:            config.Threshold,
			TokenManagement:      tokenManagement,
			ChatApiVersion:       config.ChatApiVersion,
			EmbeddingsApiVersion: config.EmbeddingsApiVersion,
		}), nil
	case "azure-openai", "azure_openai":
		return azure_openai.NewAzureOpenAIChatProvider(&azure_openai.AzureOpenAIConfig{
			Temperature:          config.Temperature,
			EncodingFormat:       config.EncodingFormat,
			ChatModel:            config.ChatModel,
			EmbeddingsModel:      config.EmbeddingsModel,
			ChatBaseURL:          config.ChatBaseURL,
			ChatApiKey:           config.ChatApiKey,
			EmbeddingsApiKey:     config.EmbeddingsApiKey,
			MaxTokens:            config.MaxTokens,
			Threshold:            config.Threshold,
			TokenManagement:      tokenManagement,
			ChatApiVersion:       config.ChatApiVersion,
			EmbeddingsApiVersion: config.EmbeddingsApiVersion,
		}), nil
	default:

		return nil, errors.New("unsupported provider")
	}
}

// EmbeddingsProviderFactory creates a Provider based on the given provider config.
func EmbeddingsProviderFactory(config *AIProviderConfig, tokenManagement contracts2.ITokenManagement) (contracts.IEmbeddingAIProvider, error) {
	switch config.EmbeddingsProviderName {
	case "ollama":
		return ollama.NewOllamaEmbeddingsProvider(&ollama.OllamaConfig{
			Temperature:       config.Temperature,
			EncodingFormat:    config.EncodingFormat,
			ChatModel:         config.ChatModel,
			EmbeddingsModel:   config.EmbeddingsModel,
			EmbeddingsBaseURL: config.EmbeddingsBaseURL,
			MaxTokens:         config.MaxTokens,
			Threshold:         config.Threshold,
			TokenManagement:   tokenManagement,
		}), nil
	case "openai":
		return openai.NewOpenAIEmbeddingsProvider(&openai.OpenAIConfig{
			Temperature:          config.Temperature,
			EncodingFormat:       config.EncodingFormat,
			ChatModel:            config.ChatModel,
			EmbeddingsModel:      config.EmbeddingsModel,
			EmbeddingsBaseURL:    config.EmbeddingsBaseURL,
			ChatApiKey:           config.ChatApiKey,
			EmbeddingsApiKey:     config.EmbeddingsApiKey,
			MaxTokens:            config.MaxTokens,
			Threshold:            config.Threshold,
			TokenManagement:      tokenManagement,
			ChatApiVersion:       config.ChatApiVersion,
			EmbeddingsApiVersion: config.EmbeddingsApiVersion,
		}), nil
	case "azure-openai", "azure_openai":
		return azure_openai.NewAzureOpenAIEmbeddingsProvider(&azure_openai.AzureOpenAIConfig{
			Temperature:          config.Temperature,
			EncodingFormat:       config.EncodingFormat,
			ChatModel:            config.ChatModel,
			EmbeddingsModel:      config.EmbeddingsModel,
			EmbeddingsBaseURL:    config.EmbeddingsBaseURL,
			ChatApiKey:           config.ChatApiKey,
			EmbeddingsApiKey:     config.EmbeddingsApiKey,
			MaxTokens:            config.MaxTokens,
			Threshold:            config.Threshold,
			TokenManagement:      tokenManagement,
			ChatApiVersion:       config.ChatApiVersion,
			EmbeddingsApiVersion: config.EmbeddingsApiVersion,
		}), nil
	default:

		return nil, errors.New("unsupported provider")
	}
}

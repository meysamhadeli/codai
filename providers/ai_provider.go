package providers

import (
	"errors"
	azure_openai "github.com/meysamhadeli/codai/providers/azure-openai"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/ollama"
	"github.com/meysamhadeli/codai/providers/openai"
)

type AIProviderConfig struct {
	ProviderName         string  `mapstructure:"provider_name"`
	BaseURL              string  `mapstructure:"base_url"`
	EmbeddingModel       string  `mapstructure:"embedding_model"`
	ChatCompletionModel  string  `mapstructure:"chat_completion_model"`
	Stream               bool    `mapstructure:"stream"`
	Temperature          float32 `mapstructure:"temperature"`
	EncodingFormat       string  `mapstructure:"encoding_format"`
	MaxTokens            int     `mapstructure:"max_tokens"`
	Threshold            float64 `mapstructure:"threshold"`
	ChatApiKey           string  `mapstructure:"chat_api_key"`
	EmbeddingsApiKey     string  `mapstructure:"embeddings_api_key"`
	ChatApiVersion       string  `mapstructure:"chat_api_version"`
	EmbeddingsApiVersion string  `mapstructure:"embeddings_api_version"`
}

// ProviderFactory creates a Provider based on the given provider config.
func ProviderFactory(config *AIProviderConfig, tokenManagement contracts.ITokenManagement) (contracts.IAIProvider, error) {
	switch config.ProviderName {
	case "ollama":
		return ollama.NewOllamaProvider(&ollama.OllamaConfig{
			Temperature:         config.Temperature,
			EncodingFormat:      config.EncodingFormat,
			ChatCompletionModel: config.ChatCompletionModel,
			EmbeddingModel:      config.EmbeddingModel,
			BaseURL:             config.BaseURL,
			MaxTokens:           config.MaxTokens,
			Threshold:           config.Threshold,
			TokenManagement:     tokenManagement,
		}), nil
	case "openai":
		return openai.NewOpenAIProvider(&openai.OpenAIConfig{
			Temperature:          config.Temperature,
			EncodingFormat:       config.EncodingFormat,
			ChatCompletionModel:  config.ChatCompletionModel,
			EmbeddingModel:       config.EmbeddingModel,
			BaseURL:              config.BaseURL,
			ChatApiKey:           config.ChatApiKey,
			EmbeddingsApiKey:     config.EmbeddingsApiKey,
			MaxTokens:            config.MaxTokens,
			Threshold:            config.Threshold,
			TokenManagement:      tokenManagement,
			ChatApiVersion:       config.ChatApiVersion,
			EmbeddingsApiVersion: config.EmbeddingsApiVersion,
		}), nil
	case "azure-openai", "azure_openai":
		return azure_openai.NewAzureOpenAIProvider(&azure_openai.AzureOpenAIConfig{
			Temperature:          config.Temperature,
			EncodingFormat:       config.EncodingFormat,
			ChatCompletionModel:  config.ChatCompletionModel,
			EmbeddingModel:       config.EmbeddingModel,
			BaseURL:              config.BaseURL,
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

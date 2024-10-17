package providers

import (
	"errors"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/ollama"
	"github.com/meysamhadeli/codai/providers/openai"
)

type AIProviderConfig struct {
	ProviderName        string
	EmbeddingURL        string
	ChatCompletionURL   string
	EmbeddingModel      string
	ChatCompletionModel string
	Stream              bool
	MaxCompletionTokens int
	Temperature         float32
	EncodingFormat      string
	ApiKey              string
}

// ProviderFactory creates a Provider based on the given provider config.
func ProviderFactory(config *AIProviderConfig) (contracts.IAIProvider, error) {
	switch config.ProviderName {
	case "ollama":
		return ollama.NewOllamaProvider(&ollama.OllamaConfig{
			Stream:              config.Stream,
			Temperature:         config.Temperature,
			EncodingFormat:      config.EncodingFormat,
			MaxCompletionTokens: config.MaxCompletionTokens,
			ChatCompletionModel: config.ChatCompletionModel,
			EmbeddingModel:      config.EmbeddingModel,
			ChatCompletionURL:   config.ChatCompletionURL,
			EmbeddingURL:        config.EmbeddingURL,
		}), nil
	case "openapi":

		return openai.NewOpenAPIProvider(&openai.OpenAPIConfig{
			Stream:              config.Stream,
			Temperature:         config.Temperature,
			EncodingFormat:      config.EncodingFormat,
			MaxCompletionTokens: config.MaxCompletionTokens,
			ChatCompletionModel: config.ChatCompletionModel,
			EmbeddingModel:      config.EmbeddingModel,
			ChatCompletionURL:   config.ChatCompletionURL,
			EmbeddingURL:        config.EmbeddingURL,
			ApiKey:              config.ApiKey,
		}), nil
	default:

		return nil, errors.New("unsupported provider")
	}
}

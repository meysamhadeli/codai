package providers

import (
	"errors"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/ollama"
	"github.com/meysamhadeli/codai/providers/openai"
)

type AIProviderConfig struct {
	ProviderName        string  `mapstructure:"provider_name"`
	EmbeddingURL        string  `mapstructure:"embedding_url"`
	ChatCompletionURL   string  `mapstructure:"chat_completion_url"`
	EmbeddingModel      string  `mapstructure:"embedding_model"`
	ChatCompletionModel string  `mapstructure:"chat_completion_model"`
	Stream              bool    `mapstructure:"stream"`
	Temperature         float32 `mapstructure:"temperature"`
	EncodingFormat      string  `mapstructure:"encoding_format"`
	MaxTokens           int     `mapstructure:"max_tokens"`
	Threshold           float64 `mapstructure:"threshold"`
	ApiKey              string  `mapstructure:"api_key"`
	BufferingTheme      string  `mapstructure:"buffering_theme"`
}

// ProviderFactory creates a Provider based on the given provider config.
func ProviderFactory(config *AIProviderConfig) (contracts.IAIProvider, error) {
	switch config.ProviderName {
	case "ollama":
		return ollama.NewOllamaProvider(&ollama.OllamaConfig{
			Temperature:         config.Temperature,
			EncodingFormat:      config.EncodingFormat,
			ChatCompletionModel: config.ChatCompletionModel,
			EmbeddingModel:      config.EmbeddingModel,
			ChatCompletionURL:   config.ChatCompletionURL,
			EmbeddingURL:        config.EmbeddingURL,
			MaxTokens:           config.MaxTokens,
			Threshold:           config.Threshold,
			BufferingTheme:      config.BufferingTheme,
		}), nil
	case "openai":

		return openai.NewOpenAIProvider(&openai.OpenAIConfig{
			Temperature:         config.Temperature,
			EncodingFormat:      config.EncodingFormat,
			ChatCompletionModel: config.ChatCompletionModel,
			EmbeddingModel:      config.EmbeddingModel,
			ChatCompletionURL:   config.ChatCompletionURL,
			EmbeddingURL:        config.EmbeddingURL,
			ApiKey:              config.ApiKey,
			MaxTokens:           config.MaxTokens,
			Threshold:           config.Threshold,
			BufferingTheme:      config.BufferingTheme,
		}), nil
	default:

		return nil, errors.New("unsupported provider")
	}
}

package providers

import (
	"errors"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/ollama"
	"github.com/meysamhadeli/codai/providers/openai"
)

// ProviderFactory creates a Provider based on the given provider name.
func ProviderFactory(providerName string) (contracts.IAIProvider, error) {
	switch providerName {
	case "ollama":
		return ollama.NewOllamaProvider(), nil
	case "openapi":

		return openai.NewOpenAPIProvider(), nil
	default:

		return nil, errors.New("unsupported provider")
	}
}

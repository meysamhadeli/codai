package contracts

import (
	"context"
	"github.com/meysamhadeli/codai/providers/models"
)

type IEmbeddingAIProvider interface {
	EmbeddingRequest(ctx context.Context, prompt []string) ([][]float64, error)
}

type IChatAIProvider interface {
	ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan models.StreamResponse
}

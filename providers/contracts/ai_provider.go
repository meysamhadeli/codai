package contracts

import (
	"context"
	"github.com/meysamhadeli/codai/providers/models"
)

type IAIProvider interface {
	ChatCompletionRequest(ctx context.Context, userInput string, prompt string) (*models.ChatCompletionResponse, error)
	EmbeddingRequest(ctx context.Context, prompt string) (*models.EmbeddingResponse, error)
}

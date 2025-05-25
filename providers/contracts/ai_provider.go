package contracts

import (
	"context"
	"github.com/meysamhadeli/codai/providers/models"
)

type IChatAIProvider interface {
	ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan models.StreamResponse
}

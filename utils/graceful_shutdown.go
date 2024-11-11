package utils

import (
	"context"
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss"
)

func GracefulShutdown(ctx context.Context, cleanup func()) {
	// Defer the recovery function to handle any panics during cleanup
	defer func() {
		if r := recover(); r != nil {
			fmt.Println(lipgloss.Red.Render(fmt.Sprintf("Recovered from panic: %v", r)))
			cleanup()
		}
	}()

	// Wait for the context to be canceled by an external signal (e.g., SIGINT or SIGTERM)
	<-ctx.Done()

	// When the context is canceled, perform cleanup
	cleanup()
}

package utils

import (
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"os"
)

func GracefulShutdown(done chan bool, sigs chan os.Signal, chatHistoryCleanUp func()) {
	go func() {
		for {
			select {
			case <-sigs:
				chatHistoryCleanUp()
				done <- true // Signal the application to exit
			}
		}
	}()

	// Defer the recovery function to handle panics
	defer func() {
		if r := recover(); r != nil {
			fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("recovered from panic: %v", r)))
			chatHistoryCleanUp()
			done <- true // Signal the application to exit
		}
	}()
}

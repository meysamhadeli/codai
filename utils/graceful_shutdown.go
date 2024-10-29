package utils

import (
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
	"os"
	"os/signal"
	"syscall"
)

func GracefulShutdown(done chan bool, TempFilesCleanup func(), chatHistoryCleanUp func()) {
	// Set up a channel to listen for shutdown signals
	sigs := make(chan os.Signal, 1)
	signal.Notify(sigs, syscall.SIGINT, syscall.SIGTERM)

	go func() {
		for {
			select {
			case <-sigs:
				fmt.Println("Received shutdown signal")
				done <- true // Signal the application to exit
				TempFilesCleanup()
				chatHistoryCleanUp()
				return
			}
		}
	}()

	// Defer the recovery function to handle panics
	defer func() {
		if r := recover(); r != nil {
			fmt.Println(lipgloss_color.Red.Render(fmt.Sprintf("Recovered from panic: %v", r)))
			TempFilesCleanup()
			chatHistoryCleanUp()
			done <- true // Signal the application to exit
		}
	}()
}

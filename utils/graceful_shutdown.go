package utils

import (
	"context"
	"fmt"
	"os"
	"os/signal"
	"syscall"
)

func GracefulShutdown(cancel context.CancelFunc, done chan bool, cleanup func()) {
	// Set up a channel to listen for shutdown signals
	sigs := make(chan os.Signal, 1)
	signal.Notify(sigs, syscall.SIGINT, syscall.SIGTERM)

	// Start a goroutine to handle the signals
	go func() {
		<-sigs // Block until a signal is received
		fmt.Println("Received shutdown signal")
		cancel()
		cleanup()
		done <- true // Signal the application to exit
	}()

	// Defer the recovery function to handle panics
	defer func() {
		if r := recover(); r != nil {
			fmt.Println("Recovered from panic:", r)
			cancel()
			cleanup()
			done <- true // Signal the application to exit
		}
	}()
}

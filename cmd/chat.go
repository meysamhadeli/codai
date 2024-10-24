package cmd

import (
	"fmt"
	"github.com/spf13/cobra"
)

// ChatCmd: codai chat
var chatCmd = &cobra.Command{
	Use:   "chat",
	Short: "Start a stateless AI-powered chat for single request-response interactions. (Not implemented yet!)",
	Long: `The 'chat' command initiates a stateless AI-powered session where each query is processed independently. 
This mode allows users to send a request and receive a response without any memory of previous interactions, 
providing isolated, context-free answers to each individual query.`,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("Stateless AI-powered chat session started")
	},
}

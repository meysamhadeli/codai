package cmd

import (
	"fmt"
	"github.com/spf13/cobra"
)

// ChatCmd: codai chat
var chatCmd = &cobra.Command{
	Use:   "chat",
	Short: "Run the chat subcommand",
	Long:  `The 'chat' subcommand starts an AI-powered chat session.`,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("Chat subcommand executed")
	},
}

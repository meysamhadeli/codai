package cmd

import (
	"fmt"
	"github.com/spf13/cobra"
)

// ChatCmd: codai chat
var chatCmd = &cobra.Command{
	Use:   "chat",
	Short: "Start a session-based AI-powered chat for interactive conversations. (Not implemented yet!)",
	Long: `The 'chat' command initiates a session-based AI-powered chat, allowing users to engage in interactive conversations 
with the ai assistant. In this mode, the context of previous interactions is maintained throughout the session, enabling 
more relevant and coherent responses. Users can send multiple queries in a single session and receive contextually 
aware answers, enhancing the overall chat experience.`,
	Run: func(cmd *cobra.Command, args []string) {
		fmt.Println("AI-powered chat session is started")
	},
}

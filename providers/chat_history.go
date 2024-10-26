package providers

import (
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
)

// ChatHistory Define a struct for the chat session to keep the history
type chatHistory struct {
	History []string // Store each prompt-response as a string
}

func (ch *chatHistory) GetHistory() []string {
	return ch.History
}

// AddToHistory Method to add conversation to the session history
func (ch *chatHistory) AddToHistory(prompt, response string) {
	entry := fmt.Sprintf("User: %s\nAI: %s", prompt, response)
	ch.History = append(ch.History, entry)
}

// ClearHistory Method to clear the chat session history
func (ch *chatHistory) ClearHistory() {
	ch.History = []string{}
}

func NewChatHistory() contracts.IChatHistory {
	return &chatHistory{}
}

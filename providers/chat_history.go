package providers

import (
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	"time"
)

// ChatHistory Define a struct for the chat session to keep the history
type chatHistory struct {
	History []string // Store each prompt-response as a string
}

func (ch *chatHistory) GetHistory() []string {
	return ch.History
}

// AddToHistory Method to add conversation to the session history
func (ch *chatHistory) AddToHistory(userInputPrompt string, aiResponse string) {

	history := fmt.Sprintf(fmt.Sprintf("### History time:\n\n%v\n---------\n\n", time.Now())+"### Here is user request:\n\n%s\n---------\n\n### Here is the the response from using AI:\n\n%s", userInputPrompt, aiResponse)

	ch.History = append(ch.History, history)
}

// ClearHistory Method to clear the chat session history
func (ch *chatHistory) ClearHistory() {
	ch.History = []string{}
}

func NewChatHistory() contracts.IChatHistory {
	return &chatHistory{}
}

package contracts

type IChatHistory interface {
	AddToHistory(userInputPrompt string, aiResponse string)
	ClearHistory()
	GetHistory() []string
}

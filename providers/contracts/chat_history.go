package contracts

type IChatHistory interface {
	AddToHistory(prompt string)
	ClearHistory()
	GetHistory() []string
}

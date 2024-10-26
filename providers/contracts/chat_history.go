package contracts

type IChatHistory interface {
	AddToHistory(prompt, response string)
	ClearHistory()
	GetHistory() []string
}

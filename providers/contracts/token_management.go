package contracts

type ITokenManagement interface {
	CountTokens(text string) int
	AvailableTokens() int
	UseTokens(count int) error
	GetTokenDetails() (int, int, int) // Returns used, available, and total tokens
	DisplayTokens(model string)
}

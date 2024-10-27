package contracts

type ITokenManagement interface {
	CountTokens(text string, model string) (int, error)
	AvailableTokens() int
	UseTokens(count int) error
	UseEmbeddingTokens(count int) error
	DisplayTokens(model string)
}

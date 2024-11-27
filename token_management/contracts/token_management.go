package contracts

type ITokenManagement interface {
	TokenCount(content string) (int, error)
	SplitTokenIntoChunks(content string, maxTokens int) ([]string, error)
	UsedTokens(inputToken int, outputToken int)
	UsedEmbeddingTokens(inputToken int, outputToken int)
	CalculateCost(providerName string, modelName string, inputToken int, outputToken int) float64
	DisplayTokens(chatProviderName string, embeddingProviderName string, chatModel string, embeddingModel string, isRag bool)
	ClearToken()
}

package contracts

type ITokenManagement interface {
	UsedTokens(inputToken int, outputToken int)
	UsedEmbeddingTokens(inputToken int, outputToken int)
	CalculateCost(providerName string, modelName string, inputToken int, outputToken int) float64
	DisplayTokens(providerName string, model string, embeddingModel string, isRag bool)
	ClearToken()
}

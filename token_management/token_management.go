package token_management

import (
	"encoding/json"
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss"
	"github.com/meysamhadeli/codai/embed_data"
	"github.com/meysamhadeli/codai/token_management/contracts"
	"github.com/pkoukk/tiktoken-go"
	"strings"
)

// TokenManager implementation
type tokenManager struct {
	usedToken       int
	usedInputToken  int
	usedOutputToken int

	usedEmbeddingToken       int
	usedEmbeddingInputToken  int
	usedEmbeddingOutputToken int
}

type details struct {
	MaxTokens               int     `json:"max_tokens"`
	MaxInputTokens          int     `json:"max_input_tokens"`
	MaxOutputTokens         int     `json:"max_output_tokens"`
	InputCostPerToken       float64 `json:"input_cost_per_token,omitempty"`
	OutputCostPerToken      float64 `json:"output_cost_per_token,omitempty"`
	CacheReadInputTokenCost float64 `json:"cache_read_input_token_cost,omitempty"`
	Mode                    string  `json:"mode"`
	SupportsFunctionCalling bool    `json:"supports_function_calling,omitempty"`
}

type Models struct {
	ModelDetails map[string]details `json:"models"`
}

// NewTokenManager creates a new token manager
func NewTokenManager() contracts.ITokenManagement {
	return &tokenManager{
		usedToken:                0,
		usedInputToken:           0,
		usedOutputToken:          0,
		usedEmbeddingToken:       0,
		usedEmbeddingInputToken:  0,
		usedEmbeddingOutputToken: 0,
	}
}

// TokenCount calculates the number of tokens in a given string.
func (tm *tokenManager) TokenCount(content string) (int, error) {
	encoder, err := tiktoken.EncodingForModel("gpt-4o")
	if err != nil {
		return 0, err
	}

	// Encode the content to calculate tokens
	tokens := encoder.Encode(content, nil, nil)
	return len(tokens), nil
}

// SplitTokenIntoChunks splits the content into chunks that each have up to maxTokens tokens.
func (tm *tokenManager) SplitTokenIntoChunks(content string, maxTokens int) ([]string, error) {
	// Load the tokenizer for the target OpenAI model
	encoder, err := tiktoken.EncodingForModel("gpt-4")
	if err != nil {
		return nil, err
	}

	// Tokenize the content
	tokens := encoder.Encode(content, nil, nil)

	// Split tokens into chunks
	var chunks []string
	for start := 0; start < len(tokens); start += maxTokens {
		end := start + maxTokens
		if end > len(tokens) {
			end = len(tokens)
		}

		// Decode the chunk of tokens back into a string
		chunk := encoder.Decode(tokens[start:end])

		chunks = append(chunks, chunk)
	}

	return chunks, nil
}

// UsedTokens deducts the token count from the available tokens.
func (tm *tokenManager) UsedTokens(inputToken int, outputToken int) {
	tm.usedInputToken = inputToken
	tm.usedOutputToken = outputToken

	tm.usedToken += inputToken + outputToken
}

// UsedEmbeddingTokens deducts the token count from the available tokens.
func (tm *tokenManager) UsedEmbeddingTokens(inputToken int, outputToken int) {
	tm.usedEmbeddingInputToken = inputToken
	tm.usedEmbeddingOutputToken = outputToken

	tm.usedEmbeddingToken += inputToken + outputToken
}

func (tm *tokenManager) DisplayTokens(chatProviderName string, embeddingProviderName string, chatModel string, embeddingModel string, isRag bool) {
	// We'll update this with the actual cost after token counts are set
	var tokenInfo string
	var cost float64
	var costEmbedding float64

	// Calculate costs after token counts have been set
	cost = tm.CalculateCost(chatProviderName, chatModel, tm.usedInputToken, tm.usedOutputToken)

	// Only calculate embedding costs if RAG is enabled
	// Fix bug where cost is not calculated right when RAG is disabled
	if isRag {
		costEmbedding = tm.CalculateCost(embeddingProviderName, embeddingModel, tm.usedEmbeddingInputToken, tm.usedEmbeddingOutputToken)
	}

	// Format display text
	tokenInfo = fmt.Sprintf("Token Used: %s - Cost: %.8f $ - Chat Model: %s", fmt.Sprint(tm.usedToken), cost, chatModel)

	if isRag {
		embeddingTokenDetails := fmt.Sprintf("Token Used: %s - Cost: %s $ - Embedding Model: %s", fmt.Sprint(tm.usedEmbeddingToken), fmt.Sprintf("%.6f", costEmbedding), embeddingModel)
		tokenInfo = tokenInfo + "\n" + embeddingTokenDetails
	}

	tokenBox := lipgloss.BoxStyle.Render(tokenInfo)
	fmt.Println(tokenBox)
}

func (tm *tokenManager) ClearToken() {
	tm.usedToken = 0
	tm.usedInputToken = 0
	tm.usedOutputToken = 0
	tm.usedEmbeddingToken = 0
	tm.usedEmbeddingInputToken = 0
	tm.usedEmbeddingOutputToken = 0
}

func (tm *tokenManager) CalculateCost(providerName string, modelName string, inputToken int, outputToken int) float64 {
	modelDetails, err := getModelDetails(providerName, modelName)
	if err != nil {
		return 0
	}
	// Calculate cost for input tokens
	inputCost := float64(inputToken) * modelDetails.InputCostPerToken

	// Calculate cost for output tokens
	outputCost := float64(outputToken) * modelDetails.OutputCostPerToken

	// Total cost
	totalCost := inputCost + outputCost

	return totalCost
}

func getModelDetails(providerName string, modelName string) (details, error) {

	providerName = strings.ToLower(providerName)
	modelName = strings.ToLower(modelName)

	if strings.HasPrefix(providerName, "azure") {
		modelName = "azure/" + modelName
	}

	// Initialize the Models struct to hold parsed JSON data
	models := Models{
		ModelDetails: make(map[string]details),
	}

	// Unmarshal the JSON data from the embedded file
	err := json.Unmarshal(embed_data.ModelDetails, &models)
	if err != nil {
		return details{}, err
	}

	// Look up the model by name
	model, exists := models.ModelDetails[modelName]
	if !exists {
		return details{}, fmt.Errorf("model details price with name '%s' not found for provider '%s'", modelName, providerName)
	}

	return model, nil
}

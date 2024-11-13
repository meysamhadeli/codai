package providers

import (
	"encoding/json"
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss"
	"github.com/meysamhadeli/codai/embed_data"
	"github.com/meysamhadeli/codai/providers/contracts"
	"log"
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

func (tm *tokenManager) DisplayTokens(providerName string, model string, embeddingModel string, isRag bool) {

	cost := tm.CalculateCost(providerName, model, tm.usedInputToken, tm.usedOutputToken)
	costEmbedding := tm.CalculateCost(providerName, embeddingModel, tm.usedEmbeddingInputToken, tm.usedEmbeddingOutputToken)

	tokenInfo := fmt.Sprintf("Token Used: %s - Cost: %s $ - Chat Model: %s", fmt.Sprint(tm.usedToken), fmt.Sprintf("%.6f", cost), model)

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
		log.Fatalf("Error unmarshaling JSON: %v", err)
		return details{}, err
	}

	// Look up the model by name
	model, exists := models.ModelDetails[modelName]
	if !exists {
		return details{}, fmt.Errorf("model details price with name '%s' not found for provider '%s'", modelName, providerName)
	}

	return model, nil
}

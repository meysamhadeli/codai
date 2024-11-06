package providers

import (
	"fmt"
	"github.com/charmbracelet/lipgloss"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/pkoukk/tiktoken-go"
	"log"
	"strings"
)

// Define styles for the box
var (
	boxStyle = lipgloss.NewStyle().Border(lipgloss.NormalBorder()).PaddingLeft(1).PaddingRight(1).BorderLeft(false).BorderRight(false).Align(lipgloss.Center)
)

// TokenManager implementation
type tokenManager struct {
	maxTokens           int
	usedTokens          int
	usedEmbeddingTokens int
}

// NewTokenManager creates a new token manager
func NewTokenManager(maxTokens int) contracts.ITokenManagement {
	return &tokenManager{
		maxTokens:           maxTokens,
		usedTokens:          0,
		usedEmbeddingTokens: 0,
	}
}

// CountTokens counts the number of tokens in the input text.
func (tm *tokenManager) CountTokens(text string, model string) (int, error) {

	model = strings.ToLower(model)

	var modelName string
	switch {
	case strings.HasPrefix(model, "gpt-4o"):
		modelName = "gpt-4o"
	case strings.HasPrefix(model, "gpt-4"):
		modelName = "gpt-4"
	case strings.HasPrefix(model, "gpt-3"):
		modelName = "gpt-3.5-turbo"
	case model == "text-embedding-3-small":
		modelName = "text-embedding-3-small"
	case model == "text-embedding-3-large":
		modelName = "text-embedding-3-large"
	case model == "text-embedding-ada-002":
		modelName = "text-embedding-ada-002"
	default:
		modelName = "gpt-4"
	}

	tkm, err := tiktoken.EncodingForModel(modelName)
	if err != nil {
		err = fmt.Errorf("encoding for model: %v", err)
		log.Println(err)
		return 0, err
	}

	// encode
	token := tkm.Encode(text, nil, nil)

	return len(token), nil
}

// AvailableTokens returns the number of available tokens.
func (tm *tokenManager) AvailableTokens() int {
	return tm.maxTokens - tm.usedTokens
}

// UseTokens deducts the token count from the available tokens.
func (tm *tokenManager) UseTokens(count int) error {
	if count > tm.AvailableTokens() {
		return fmt.Errorf("not enough tokens available: requested %d, available %d", count, tm.AvailableTokens())
	}
	tm.usedTokens += count
	return nil
}

// UseEmbeddingTokens deducts the token count from the available tokens.
func (tm *tokenManager) UseEmbeddingTokens(count int) error {
	tm.usedEmbeddingTokens += count
	return nil
}

func (tm *tokenManager) DisplayTokens(model string, embeddingModel string) {
	used, available, total := tm.usedTokens, tm.AvailableTokens(), tm.maxTokens
	tokenInfo := fmt.Sprintf("Used Tokens: %d | Available Tokens: %d | Total Tokens (%s): %d | Used Embedding Tokens (%s): %d", used, available, model, total, embeddingModel, tm.usedEmbeddingTokens)

	tokenBox := boxStyle.Render(tokenInfo)
	fmt.Println(tokenBox)
}

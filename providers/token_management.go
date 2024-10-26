package providers

import (
	"fmt"
	"github.com/charmbracelet/lipgloss"
	"github.com/meysamhadeli/codai/providers/contracts"
)

// Define styles for the box
var (
	boxStyle = lipgloss.NewStyle().Border(lipgloss.NormalBorder()).Padding(1, 2).Align(lipgloss.Center)
)

// TokenManager implementation
type tokenManager struct {
	maxTokens  int
	usedTokens int
	tokenizer  *Tokenizer // Use the custom tokenizer
}

// NewTokenManager creates a new token manager
func NewTokenManager(maxTokens int) contracts.ITokenManagement {
	return &tokenManager{
		maxTokens:  maxTokens,
		usedTokens: 0,
		tokenizer:  &Tokenizer{},
	}
}

// CountTokens counts the number of tokens in the input text.
func (tm *tokenManager) CountTokens(text string) int {
	return len(tm.tokenizer.Tokenize(text))
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

func (tm *tokenManager) DisplayTokens(model string) {
	used, available, total := tm.GetTokenDetails()
	tokenInfo := fmt.Sprintf("Used Tokens: %d | Available Tokens: %d | Total Tokens (%s): %d", used, available, model, total)
	tokenBox := boxStyle.Render(tokenInfo)
	fmt.Println(tokenBox)
}

// GetTokenDetails returns details about token usage.
func (tm *tokenManager) GetTokenDetails() (int, int, int) {
	return tm.usedTokens, tm.AvailableTokens(), tm.maxTokens
}

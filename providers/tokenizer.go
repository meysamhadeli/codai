package providers

import (
	"strings"
	"unicode"
)

// Tokenizer is a custom tokenizer implementation
type Tokenizer struct{}

// Tokenize splits input text into tokens based on spaces and punctuation.
func (t *Tokenizer) Tokenize(text string) []string {
	var tokens []string
	var currentToken strings.Builder

	for _, r := range text {
		if unicode.IsSpace(r) || unicode.IsPunct(r) {
			if currentToken.Len() > 0 {
				tokens = append(tokens, currentToken.String())
				currentToken.Reset()
			}
		} else {
			currentToken.WriteRune(r)
		}
	}

	// Add the last token if there is any
	if currentToken.Len() > 0 {
		tokens = append(tokens, currentToken.String())
	}

	return tokens
}

// CountTokens returns the number of tokens in the input text.
func (t *Tokenizer) CountTokens(text string) int {
	tokens := t.Tokenize(text)
	return len(tokens)
}

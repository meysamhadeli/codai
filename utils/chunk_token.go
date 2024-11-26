package utils

import (
	"github.com/pkoukk/tiktoken-go"
)

// TokenCount calculates the number of tokens in a given string.
func TokenCount(content string) (int, error) {
	encoder, err := tiktoken.EncodingForModel("gpt-4o")
	if err != nil {
		return 0, err
	}

	// Encode the content to calculate tokens
	tokens := encoder.Encode(content, nil, nil)
	return len(tokens), nil
}

// SplitIntoChunks splits the content into chunks that each have up to maxTokens tokens.
func SplitIntoChunks(content string, maxTokens int) ([]string, error) {
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

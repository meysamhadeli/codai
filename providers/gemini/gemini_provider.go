package gemini

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	gemini_models "github.com/meysamhadeli/codai/providers/gemini/models"
	"github.com/meysamhadeli/codai/providers/models"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
	"io"
	"net/http"
	"strings"
)

// GeminiConfig implements the Provider interface for Google's Gemini.
type GeminiConfig struct {
	BaseURL         string
	Model           string
	Temperature     *float32
	MaxTokens       int
	ApiKey          string
	TokenManagement contracts2.ITokenManagement
}

const (
	defaultBaseURL = "https://generativelanguage.googleapis.com/v1beta"
)

// NewGeminiChatProvider initializes a new GeminiProvider.
func NewGeminiChatProvider(config *GeminiConfig) contracts.IChatAIProvider {
	// Set default BaseURL if empty
	baseURL := config.BaseURL
	if baseURL == "" {
		baseURL = defaultBaseURL
	}
	return &GeminiConfig{
		BaseURL:         config.BaseURL,
		Model:           config.Model,
		Temperature:     config.Temperature,
		MaxTokens:       config.MaxTokens,
		ApiKey:          config.ApiKey,
		TokenManagement: config.TokenManagement,
	}
}

func (geminiProvider *GeminiConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan models.StreamResponse {
	responseChan := make(chan models.StreamResponse)
	var markdownBuffer strings.Builder

	go func() {
		defer close(responseChan)

		// Combine system prompt and user input into a single user message
		fullContent := fmt.Sprintf("%s\n\n%s", prompt, userInput)

		reqBody := gemini_models.GeminiChatCompletionRequest{
			Contents: []gemini_models.Content{
				{
					Role: "user",
					Parts: []gemini_models.Part{
						{Text: fullContent},
					},
				},
			},
			GenerationConfig: &gemini_models.GenerationConfig{
				Temperature:     geminiProvider.Temperature,
				MaxOutputTokens: geminiProvider.MaxTokens,
			},
		}

		jsonData, err := json.Marshal(reqBody)
		if err != nil {
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error marshalling request body: %v", err)}
			return
		}

		req, err := http.NewRequestWithContext(
			ctx,
			"POST",
			fmt.Sprintf("%s/models/%s:generateContent", geminiProvider.BaseURL, geminiProvider.Model),
			bytes.NewBuffer(jsonData),
		)
		if err != nil {
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error creating request: %v", err)}
			return
		}

		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", geminiProvider.ApiKey))

		client := &http.Client{}
		resp, err := client.Do(req)
		if err != nil {
			if errors.Is(ctx.Err(), context.Canceled) {
				responseChan <- models.StreamResponse{Err: fmt.Errorf("request canceled: %v", err)}
				return
			}
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error sending request: %v", err)}
			return
		}
		defer resp.Body.Close()

		if resp.StatusCode != http.StatusOK {
			body, _ := io.ReadAll(resp.Body)
			var apiError models.AIError
			if err := json.Unmarshal(body, &apiError); err != nil {
				responseChan <- models.StreamResponse{Err: fmt.Errorf("error parsing error response: %v", err)}
				return
			}
			responseChan <- models.StreamResponse{Err: fmt.Errorf("API request failed with status code '%d' - %s\n", resp.StatusCode, apiError.Error.Message)}
			return
		}

		var fullResponse gemini_models.GeminiChatCompletionResponse
		if err := json.NewDecoder(resp.Body).Decode(&fullResponse); err != nil {
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error decoding response: %v", err)}
			return
		}

		if len(fullResponse.Candidates) > 0 && len(fullResponse.Candidates[0].Content.Parts) > 0 {
			content := fullResponse.Candidates[0].Content.Parts[0].Text
			markdownBuffer.WriteString(content)
			responseChan <- models.StreamResponse{Content: markdownBuffer.String()}
		}

		if fullResponse.UsageMetadata != nil {
			geminiProvider.TokenManagement.UsedTokens(
				fullResponse.UsageMetadata.PromptTokenCount,
				fullResponse.UsageMetadata.CandidatesTokenCount,
			)
		}

		responseChan <- models.StreamResponse{Done: true}
	}()

	return responseChan
}

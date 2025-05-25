package mistral

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	mistral_models "github.com/meysamhadeli/codai/providers/mistral/models"
	"github.com/meysamhadeli/codai/providers/models"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
	"io"
	"net/http"
	"strings"
)

// MistralConfig implements the Provider interface for Mistral.
type MistralConfig struct {
	BaseURL         string
	Model           string
	Temperature     *float32
	MaxTokens       int
	ApiKey          string
	TokenManagement contracts2.ITokenManagement
}

const (
	defaultBaseURL = "https://api.mistral.ai/v1"
)

// NewMistralChatProvider initializes a new MistralProvider.
func NewMistralChatProvider(config *MistralConfig) contracts.IChatAIProvider {
	// Set default BaseURL if empty
	baseURL := config.BaseURL
	if baseURL == "" {
		baseURL = defaultBaseURL
	}
	return &MistralConfig{
		BaseURL:         config.BaseURL,
		Model:           config.Model,
		Temperature:     config.Temperature,
		MaxTokens:       config.MaxTokens,
		ApiKey:          config.ApiKey,
		TokenManagement: config.TokenManagement,
	}
}

func (mistralProvider *MistralConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan models.StreamResponse {
	responseChan := make(chan models.StreamResponse)
	var markdownBuffer strings.Builder
	var usage mistral_models.Usage

	go func() {
		defer close(responseChan)

		reqBody := mistral_models.MistralChatCompletionRequest{
			Model: mistralProvider.Model,
			Messages: []mistral_models.Message{
				{Role: "system", Content: prompt},
				{Role: "user", Content: userInput},
			},
			Temperature: mistralProvider.Temperature,
			MaxTokens:   mistralProvider.MaxTokens,
			Stream:      true,
		}

		jsonData, err := json.Marshal(reqBody)
		if err != nil {
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error marshalling request body: %v", err)}
			return
		}

		req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/chat/completions", mistralProvider.BaseURL), bytes.NewBuffer(jsonData))
		if err != nil {
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error creating request: %v", err)}
			return
		}

		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", mistralProvider.ApiKey))

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

		reader := bufio.NewReader(resp.Body)

		for {
			line, err := reader.ReadString('\n')
			if err != nil {
				if err == io.EOF {
					break
				}
				responseChan <- models.StreamResponse{Err: fmt.Errorf("error reading stream: %v", err)}
				return
			}

			if strings.HasPrefix(line, "data:") {
				jsonPart := strings.TrimPrefix(line, "data:")
				var response mistral_models.MistralChatCompletionResponse
				if err := json.Unmarshal([]byte(jsonPart), &response); err != nil {
					responseChan <- models.StreamResponse{Err: fmt.Errorf("error unmarshalling chunk: %v", err)}
					return
				}

				if response.Usage.TotalTokens > 0 {
					usage = response.Usage
				}

				if len(response.Choices) > 0 {
					content := response.Choices[0].Delta.Content
					markdownBuffer.WriteString(content)

					if strings.Contains(content, "\n") {
						responseChan <- models.StreamResponse{Content: markdownBuffer.String()}
						markdownBuffer.Reset()
					}
				}
			}
		}

		if markdownBuffer.Len() > 0 {
			responseChan <- models.StreamResponse{Content: markdownBuffer.String()}
		}

		responseChan <- models.StreamResponse{Done: true}
		if usage.TotalTokens > 0 {
			mistralProvider.TokenManagement.UsedTokens(usage.PromptTokens, usage.CompletionTokens)
		}
	}()

	return responseChan
}

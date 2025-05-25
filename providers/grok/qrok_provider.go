package grok

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	grok_models "github.com/meysamhadeli/codai/providers/grok/models"
	"github.com/meysamhadeli/codai/providers/models"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
	"io"
	"net/http"
	"strings"
)

// GrokConfig implements the Provider interface for xAI's Grok.
type GrokConfig struct {
	BaseURL         string
	Model           string
	Temperature     *float32
	MaxTokens       int
	ApiVersion      string
	ApiKey          string
	TokenManagement contracts2.ITokenManagement
}

const (
	defaultBaseURL = "https://api.x.ai/v1"
)

// NewGrokChatProvider initializes a new GrokProvider.
func NewGrokChatProvider(config *GrokConfig) contracts.IChatAIProvider {
	// Set default BaseURL if empty
	baseURL := config.BaseURL
	if baseURL == "" {
		baseURL = defaultBaseURL
	}
	return &GrokConfig{
		BaseURL:         config.BaseURL,
		Model:           config.Model,
		Temperature:     config.Temperature,
		MaxTokens:       config.MaxTokens,
		ApiVersion:      config.ApiVersion,
		ApiKey:          config.ApiKey,
		TokenManagement: config.TokenManagement,
	}
}

func (grokProvider *GrokConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan models.StreamResponse {
	responseChan := make(chan models.StreamResponse)
	var markdownBuffer strings.Builder
	var usage grok_models.Usage

	go func() {
		defer close(responseChan)

		reqBody := grok_models.GrokChatCompletionRequest{
			Model: grokProvider.Model,
			Messages: []grok_models.Message{
				{Role: "system", Content: prompt},
				{Role: "user", Content: userInput},
			},
			Temperature: grokProvider.Temperature,
			MaxTokens:   grokProvider.MaxTokens,
			Stream:      true,
		}

		jsonData, err := json.Marshal(reqBody)
		if err != nil {
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error marshalling request body: %v", err)}
			return
		}

		req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/chat/completions", grokProvider.BaseURL), bytes.NewBuffer(jsonData))
		if err != nil {
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error creating request: %v", err)}
			return
		}

		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", grokProvider.ApiKey))
		req.Header.Set("x-api-version", grokProvider.ApiVersion)

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
				var response grok_models.GrokChatCompletionResponse
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
			grokProvider.TokenManagement.UsedTokens(usage.PromptTokens, usage.CompletionTokens)
		}
	}()

	return responseChan
}

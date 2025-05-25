package openrouter

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	general_models "github.com/meysamhadeli/codai/providers/models"
	"github.com/meysamhadeli/codai/providers/openrouter/models"

	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
	"io"
	"io/ioutil"
	"net/http"
	"strings"
)

// OpenRouterConfig implements the Provider interface for OpenAPI.
type OpenRouterConfig struct {
	BaseURL         string
	Model           string
	Temperature     *float32
	ReasoningEffort *string
	EncodingFormat  string
	ApiKey          string
	MaxTokens       int
	TokenManagement contracts2.ITokenManagement
	ApiVersion      string
}

const (
	defaultBaseURL = "https://openrouter.ai/api/v1"
)

// NewOpenRouterChatProvider initializes a new OpenAPIProvider.
func NewOpenRouterChatProvider(config *OpenRouterConfig) contracts.IChatAIProvider {
	// Set default BaseURL if empty
	baseURL := config.BaseURL
	if baseURL == "" {
		baseURL = defaultBaseURL
	}
	return &OpenRouterConfig{
		BaseURL:         baseURL,
		Model:           config.Model,
		Temperature:     config.Temperature,
		ReasoningEffort: config.ReasoningEffort,
		EncodingFormat:  config.EncodingFormat,
		MaxTokens:       config.MaxTokens,
		ApiKey:          config.ApiKey,
		ApiVersion:      config.ApiVersion,
		TokenManagement: config.TokenManagement,
	}
}

func (openRouterProvider *OpenRouterConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan general_models.StreamResponse {
	responseChan := make(chan general_models.StreamResponse)
	var markdownBuffer strings.Builder // Buffer to accumulate content until newline
	var usage models.Usage             // Variable to hold usage data

	go func() {
		defer close(responseChan)

		// Prepare the request body
		reqBody := models.OpenRouterChatCompletionRequest{
			Model: openRouterProvider.Model,
			Messages: []models.Message{
				{Role: "system", Content: prompt},
				{Role: "user", Content: userInput},
			},
			Stream:          true,
			Temperature:     openRouterProvider.Temperature,
			ReasoningEffort: openRouterProvider.ReasoningEffort,
		}

		jsonData, err := json.Marshal(reqBody)
		if err != nil {
			markdownBuffer.Reset()
			responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error marshalling request body: %v", err)}
			return
		}

		// Create a new HTTP request
		req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/chat/completions", openRouterProvider.BaseURL), bytes.NewBuffer(jsonData))
		if err != nil {
			markdownBuffer.Reset()
			responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error creating request: %v", err)}
			return
		}

		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", openRouterProvider.ApiKey))

		client := &http.Client{}
		resp, err := client.Do(req)
		if err != nil {
			markdownBuffer.Reset()
			if errors.Is(ctx.Err(), context.Canceled) {
				responseChan <- general_models.StreamResponse{Err: fmt.Errorf("request canceled: %v", err)}
				return
			}
			responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error sending request: %v", err)}
			return
		}
		defer resp.Body.Close()

		if resp.StatusCode != http.StatusOK {
			markdownBuffer.Reset()
			body, _ := ioutil.ReadAll(resp.Body)
			var apiError general_models.AIError
			if err := json.Unmarshal(body, &apiError); err != nil {
				responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error parsing error response: %v", err)}
				return
			}

			responseChan <- general_models.StreamResponse{Err: fmt.Errorf("API request failed with status code '%d' - %s\n", resp.StatusCode, apiError.Error.Message)}
			return
		}

		reader := bufio.NewReader(resp.Body)

		// Stream processing
		for {
			line, err := reader.ReadString('\n')
			if err != nil {
				markdownBuffer.Reset()
				if err == io.EOF {
					break
				}
				responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error reading stream: %v", err)}
				return
			}

			// Skip the [DONE] marker completely
			if line == "data: [DONE]\n" {
				continue
			}

			if strings.HasPrefix(line, "data: ") {
				jsonPart := strings.TrimPrefix(line, "data: ")
				var response models.OpenRouterChatCompletionResponse
				if err := json.Unmarshal([]byte(jsonPart), &response); err != nil {
					markdownBuffer.Reset()

					responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error unmarshalling chunk: %v", err)}
					return
				}

				// Check if the response has usage information
				if response.Usage.TotalTokens > 0 {
					usage = response.Usage // Capture the usage data for later use
				}

				// Accumulate and send response content
				if len(response.Choices) > 0 {
					choice := response.Choices[0]
					content := choice.Delta.Content
					markdownBuffer.WriteString(content)

					// Send chunk if it contains a newline, and then reset the buffer
					if strings.Contains(content, "\n") {
						responseChan <- general_models.StreamResponse{Content: markdownBuffer.String()}
						markdownBuffer.Reset()
					}

					// Check for completion using FinishReason
					if choice.FinishReason == "stop" {
						responseChan <- general_models.StreamResponse{Content: markdownBuffer.String()}

						responseChan <- general_models.StreamResponse{Done: true}

						// Count total tokens usage
						if usage.TotalTokens > 0 {
							openRouterProvider.TokenManagement.UsedTokens(usage.PromptTokens, usage.CompletionTokens)
						}

						break
					}
				}
			}
		}

		// Send any remaining content in the buffer
		if markdownBuffer.Len() > 0 {
			responseChan <- general_models.StreamResponse{Content: markdownBuffer.String()}
		}
	}()

	return responseChan
}

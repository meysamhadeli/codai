package anthropic

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/anthropic/models"
	"github.com/meysamhadeli/codai/providers/contracts"
	general_models "github.com/meysamhadeli/codai/providers/models"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
	"io"
	"io/ioutil"
	"net/http"
	"strings"
)

// AnthropicConfig implements the Provider interface for OpenAPI.
type AnthropicConfig struct {
	MessageBaseURL    string
	MessageModel      string
	Temperature       *float32
	EncodingFormat    string
	MessageApiKey     string
	MaxTokens         int
	Threshold         float64
	TokenManagement   contracts2.ITokenManagement
	MessageApiVersion string
}

// NewAnthropicMessageProvider initializes a new OpenAPIProvider.
func NewAnthropicMessageProvider(config *AnthropicConfig) contracts.IChatAIProvider {
	return &AnthropicConfig{
		MessageBaseURL:    config.MessageBaseURL,
		MessageModel:      config.MessageModel,
		Temperature:       config.Temperature,
		EncodingFormat:    config.EncodingFormat,
		MaxTokens:         config.MaxTokens,
		Threshold:         config.Threshold,
		MessageApiKey:     config.MessageApiKey,
		MessageApiVersion: config.MessageApiVersion,
		TokenManagement:   config.TokenManagement,
	}
}

func (anthropicProvider *AnthropicConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan general_models.StreamResponse {
	responseChan := make(chan general_models.StreamResponse)
	var markdownBuffer strings.Builder // Accumulate content for streaming responses
	var usage models.Usage             // To track token usage

	go func() {
		defer close(responseChan)

		// Prepare the request body
		reqBody := models.AnthropicMessageRequest{
			Messages: []models.Message{
				{Role: "system", Content: prompt},
				{Role: "user", Content: userInput},
			},
			Model:       anthropicProvider.MessageModel,
			Temperature: &anthropicProvider.Temperature,
			Stream:      true,
		}

		jsonData, err := json.Marshal(reqBody)
		if err != nil {
			responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error marshalling request body: %v", err)}
			return
		}

		// Create the HTTP request
		req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/v1/messages", anthropicProvider.MessageBaseURL), bytes.NewBuffer(jsonData))
		if err != nil {
			responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error creating HTTP request: %v", err)}
			return
		}

		// Set required headers
		req.Header.Set("content-type", "application/json")                       // Required content type
		req.Header.Set("anthropic-version", anthropicProvider.MessageApiVersion) // Required API version
		req.Header.Set("x-api-key", anthropicProvider.MessageApiKey)             // API key for authentication

		// Send the HTTP request
		client := &http.Client{}
		resp, err := client.Do(req)
		if err != nil {
			if errors.Is(ctx.Err(), context.Canceled) {
				responseChan <- general_models.StreamResponse{Err: fmt.Errorf("request canceled: %v", err)}
			} else {
				responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error sending request: %v", err)}
			}
			return
		}
		defer resp.Body.Close()

		// Handle non-200 status codes
		if resp.StatusCode != http.StatusOK {
			body, _ := ioutil.ReadAll(resp.Body)
			var apiError models.AnthropicError
			if err := json.Unmarshal(body, &apiError); err != nil {
				responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error parsing error response: %v", err)}
			} else {
				responseChan <- general_models.StreamResponse{Err: fmt.Errorf("API request failed: %s", apiError.Error.Message)}
			}
			return
		}

		// Process the streaming response
		reader := bufio.NewReader(resp.Body)
		for {
			line, err := reader.ReadString('\n')
			if err != nil {
				if err == io.EOF {
					break
				}
				responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error reading stream: %v", err)}
				return
			}

			// Skip ping events or irrelevant data
			if strings.HasPrefix(line, "event: ping") || strings.TrimSpace(line) == "" {
				continue
			}

			// Parse response events
			if strings.HasPrefix(line, "data: ") {
				data := strings.TrimPrefix(line, "data: ")
				var response models.AnthropicMessageResponse
				if err := json.Unmarshal([]byte(data), &response); err != nil {
					responseChan <- general_models.StreamResponse{Err: fmt.Errorf("error unmarshalling chunk: %v", err)}
					return
				}

				// Handle content and final message updates
				switch response.Type {
				case "content_block_delta":
					if response.Delta.Type == "text_delta" {
						markdownBuffer.WriteString(response.Delta.Text)
						if strings.Contains(response.Delta.Text, "\n") {
							responseChan <- general_models.StreamResponse{Content: markdownBuffer.String()}
							markdownBuffer.Reset()
						}
					}
				case "message_delta":
					if response.Usage != nil {
						usage = *response.Usage // Capture usage details
					}
				case "message_stop":
					responseChan <- general_models.StreamResponse{Content: markdownBuffer.String(), Done: true}
					if usage.TotalTokens > 0 {
						anthropicProvider.TokenManagement.UsedTokens(usage.InputTokens, usage.OutputTokens)
					}
					return
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

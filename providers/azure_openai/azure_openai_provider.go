package azure_openai

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	azure_openai_models "github.com/meysamhadeli/codai/providers/azure_openai/models"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/models"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
	"io"
	"io/ioutil"
	"net/http"
	"strings"
)

// AzureOpenAIConfig implements the Provider interface for OpenAPI.
type AzureOpenAIConfig struct {
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

// NewAzureOpenAIChatProvider initializes a new OpenAPIProvider.
func NewAzureOpenAIChatProvider(config *AzureOpenAIConfig) contracts.IChatAIProvider {
	return &AzureOpenAIConfig{
		BaseURL:         config.BaseURL,
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

func (azureOpenAIProvider *AzureOpenAIConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan models.StreamResponse {
	responseChan := make(chan models.StreamResponse)
	var markdownBuffer strings.Builder  // Buffer to accumulate content until newline
	var usage azure_openai_models.Usage // Variable to hold usage data

	go func() {
		defer close(responseChan)

		// Prepare the request body
		reqBody := azure_openai_models.OpenAIChatCompletionRequest{
			Model: azureOpenAIProvider.Model,
			Messages: []azure_openai_models.Message{
				{Role: "system", Content: prompt},
				{Role: "user", Content: userInput},
			},
			Stream:          true,
			Temperature:     azureOpenAIProvider.Temperature,
			ReasoningEffort: azureOpenAIProvider.ReasoningEffort,
			StreamOptions: azure_openai_models.StreamOptions{
				IncludeUsage: true,
			},
		}

		jsonData, err := json.Marshal(reqBody)
		if err != nil {
			markdownBuffer.Reset()
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error marshalling request body: %v", err)}
			return
		}

		// Create a new HTTP request
		req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/openai/deployments/%s/chat/completions?api-version=%s", azureOpenAIProvider.BaseURL, azureOpenAIProvider.Model, azureOpenAIProvider.ApiVersion), bytes.NewBuffer(jsonData))
		if err != nil {
			markdownBuffer.Reset()
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error creating request: %v", err)}
			return
		}

		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("api-key", azureOpenAIProvider.ApiKey)

		client := &http.Client{}
		resp, err := client.Do(req)
		if err != nil {
			markdownBuffer.Reset()
			if errors.Is(ctx.Err(), context.Canceled) {
				responseChan <- models.StreamResponse{Err: fmt.Errorf("request canceled: %v", err)}
				return
			}
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error sending request: %v", err)}
			return
		}
		defer resp.Body.Close()

		if resp.StatusCode != http.StatusOK {
			markdownBuffer.Reset()
			body, _ := ioutil.ReadAll(resp.Body)
			var apiError models.AIError
			if err := json.Unmarshal(body, &apiError); err != nil {
				responseChan <- models.StreamResponse{Err: fmt.Errorf("error parsing error response: %v", err)}
				return
			}

			responseChan <- models.StreamResponse{Err: fmt.Errorf("API request failed with status code '%d' - %s\n", resp.StatusCode, apiError.Error.Message)}
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
				responseChan <- models.StreamResponse{Err: fmt.Errorf("error reading stream: %v", err)}
				return
			}

			if line == "data: [DONE]\n" {
				// Send the final content
				responseChan <- models.StreamResponse{Content: markdownBuffer.String()}

				responseChan <- models.StreamResponse{Done: true}

				// Count total tokens usage
				if usage.TotalTokens > 0 {
					azureOpenAIProvider.TokenManagement.UsedTokens(usage.PromptTokens, usage.CompletionTokens)
				}

				break
			}

			if strings.HasPrefix(line, "data: ") {
				jsonPart := strings.TrimPrefix(line, "data: ")
				var response azure_openai_models.OpenAIChatCompletionResponse
				if err := json.Unmarshal([]byte(jsonPart), &response); err != nil {
					markdownBuffer.Reset()

					responseChan <- models.StreamResponse{Err: fmt.Errorf("error unmarshalling chunk: %v", err)}
					return
				}

				// Check if the response has usage information
				if response.Usage.TotalTokens > 0 {
					usage = response.Usage // Capture the usage data for later use
				}

				// Accumulate and send response content
				if len(response.Choices) > 0 {
					content := response.Choices[0].Delta.Content
					markdownBuffer.WriteString(content)

					// Send chunk if it contains a newline, and then reset the buffer
					if strings.Contains(content, "\n") {
						responseChan <- models.StreamResponse{Content: markdownBuffer.String()}
						markdownBuffer.Reset()
					}
				}
			}
		}

		// Send any remaining content in the buffer
		if markdownBuffer.Len() > 0 {
			responseChan <- models.StreamResponse{Content: markdownBuffer.String()}
		}
	}()

	return responseChan
}

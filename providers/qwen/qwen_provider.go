package qwen

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/models"
	qwen_models "github.com/meysamhadeli/codai/providers/qwen/models"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
	"io"
	"net/http"
	"strings"
)

// QwenConfig implements the Provider interface for Alibaba's Qwen.
type QwenConfig struct {
	BaseURL         string
	Model           string
	Temperature     *float32
	MaxTokens       int
	ApiKey          string
	TokenManagement contracts2.ITokenManagement
}

const (
	defaultBaseURL = "https://dashscope-intl.aliyuncs.com/compatible-mode"
)

// NewQwenChatProvider initializes a new QwenProvider.
func NewQwenChatProvider(config *QwenConfig) contracts.IChatAIProvider {
	// Set default BaseURL if empty
	baseURL := config.BaseURL
	if baseURL == "" {
		baseURL = defaultBaseURL
	}

	return &QwenConfig{
		BaseURL:         baseURL,
		Model:           config.Model,
		Temperature:     config.Temperature,
		MaxTokens:       config.MaxTokens,
		ApiKey:          config.ApiKey,
		TokenManagement: config.TokenManagement,
	}
}

func (qwenProvider *QwenConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan models.StreamResponse {
	responseChan := make(chan models.StreamResponse)
	var markdownBuffer strings.Builder
	var usage qwen_models.Usage

	go func() {
		defer close(responseChan)

		reqBody := qwen_models.QwenChatCompletionRequest{
			Model: qwenProvider.Model,
			Messages: []qwen_models.Message{
				{Role: "system", Content: prompt},
				{Role: "user", Content: userInput},
			},
			Temperature: qwenProvider.Temperature,
			MaxTokens:   qwenProvider.MaxTokens,
			Stream:      true,
		}

		jsonData, err := json.Marshal(reqBody)
		if err != nil {
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error marshalling request body: %v", err)}
			return
		}

		req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/v1/chat/completions", qwenProvider.BaseURL), bytes.NewBuffer(jsonData))
		if err != nil {
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error creating request: %v", err)}
			return
		}

		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", qwenProvider.ApiKey))

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
				var response qwen_models.QwenChatCompletionResponse
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
			qwenProvider.TokenManagement.UsedTokens(usage.PromptTokens, usage.CompletionTokens)
		}
	}()

	return responseChan
}

package deepseek

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	deepseek_models "github.com/meysamhadeli/codai/providers/deepseek/models"
	"github.com/meysamhadeli/codai/providers/models"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
	"io"
	"io/ioutil"
	"net/http"
	"strings"
)

// DeepSeekConfig implements the Provider interface for DeepSeek.
type DeepSeekConfig struct {
	ChatBaseURL     string
	ChatModel       string
	Temperature     float32
	EncodingFormat  string
	ChatApiKey      string
	MaxTokens       int
	Threshold       float64
	TokenManagement contracts2.ITokenManagement
	ChatApiVersion  string
}

// NewDeepSeekChatProvider initializes a new DeepSeekAPIProvider.
func NewDeepSeekChatProvider(config *DeepSeekConfig) contracts.IChatAIProvider {
	return &DeepSeekConfig{
		ChatBaseURL:     config.ChatBaseURL,
		ChatModel:       config.ChatModel,
		Temperature:     config.Temperature,
		EncodingFormat:  config.EncodingFormat,
		MaxTokens:       config.MaxTokens,
		Threshold:       config.Threshold,
		ChatApiKey:      config.ChatApiKey,
		ChatApiVersion:  config.ChatApiVersion,
		TokenManagement: config.TokenManagement,
	}
}
func (deepSeekProvider *DeepSeekConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan models.StreamResponse {
	responseChan := make(chan models.StreamResponse)
	var markdownBuffer strings.Builder // Buffer to accumulate content until newline
	var usage deepseek_models.Usage    // Variable to hold usage data

	go func() {
		defer close(responseChan)

		// Prepare the request body
		reqBody := deepseek_models.DeepSeekChatCompletionRequest{
			Model: deepSeekProvider.ChatModel,
			Messages: []deepseek_models.Message{
				{Role: "system", Content: prompt},
				{Role: "user", Content: userInput},
			},
			Stream:      true,
			Temperature: &deepSeekProvider.Temperature,
		}

		jsonData, err := json.Marshal(reqBody)
		if err != nil {
			markdownBuffer.Reset()
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error marshalling request body: %v", err)}
			return
		}

		// Create a new HTTP request
		req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/chat/completions", deepSeekProvider.ChatBaseURL), bytes.NewBuffer(jsonData))
		if err != nil {
			markdownBuffer.Reset()
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error creating request: %v", err)}
			return
		}

		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", deepSeekProvider.ChatApiKey))

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
					// Stream ended, send any remaining content
					if markdownBuffer.Len() > 0 {
						responseChan <- models.StreamResponse{Content: markdownBuffer.String()}
					}

					// Notify that the stream is done
					responseChan <- models.StreamResponse{Done: true}

					// Count total tokens usage
					if usage.TotalTokens > 0 {
						deepSeekProvider.TokenManagement.UsedTokens(usage.PromptTokens, usage.CompletionTokens)
					}

					break
				}
				responseChan <- models.StreamResponse{Err: fmt.Errorf("error reading stream: %v", err)}
				return
			}

			if strings.HasPrefix(line, "data: ") {
				jsonPart := strings.TrimPrefix(line, "data: ")
				var response deepseek_models.DeepSeekChatCompletionResponse
				if err := json.Unmarshal([]byte(jsonPart), &response); err != nil {
					markdownBuffer.Reset()
					responseChan <- models.StreamResponse{Err: fmt.Errorf("error unmarshalling chunk: %v", err)}
					return
				}

				// Check if the response has usage information
				if response.Usage.TotalTokens > 0 {
					usage = response.Usage // Capture the usage data for later use
				}

				// Check for finish_reason
				if len(response.Choices) > 0 && response.Choices[0].FinishReason != "" {
					// Stream completed for this choice
					responseChan <- models.StreamResponse{Content: markdownBuffer.String()}
					responseChan <- models.StreamResponse{Done: true}

					// Count total tokens usage
					if usage.TotalTokens > 0 {
						deepSeekProvider.TokenManagement.UsedTokens(usage.PromptTokens, usage.CompletionTokens)
					}

					break
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
	}()

	return responseChan
}

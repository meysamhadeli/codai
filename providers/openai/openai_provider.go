package openai

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/models"
	openai_models "github.com/meysamhadeli/codai/providers/openai/models"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
	"io"
	"io/ioutil"
	"net/http"
	"strings"
)

// OpenAIConfig implements the Provider interface for OpenAPI.
type OpenAIConfig struct {
	ChatBaseURL          string
	EmbeddingsBaseURL    string
	EmbeddingsModel      string
	ChatModel            string
	Temperature          float32
	EncodingFormat       string
	ChatApiKey           string
	EmbeddingsApiKey     string
	MaxTokens            int
	Threshold            float64
	TokenManagement      contracts2.ITokenManagement
	ChatApiVersion       string
	EmbeddingsApiVersion string
}

// NewOpenAIChatProvider initializes a new OpenAPIProvider.
func NewOpenAIChatProvider(config *OpenAIConfig) contracts.IChatAIProvider {
	return &OpenAIConfig{
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

// NewOpenAIEmbeddingsProvider initializes a new OpenAPIProvider.
func NewOpenAIEmbeddingsProvider(config *OpenAIConfig) contracts.IEmbeddingAIProvider {
	return &OpenAIConfig{
		EmbeddingsBaseURL:    config.EmbeddingsBaseURL,
		EmbeddingsModel:      config.EmbeddingsModel,
		Temperature:          config.Temperature,
		EncodingFormat:       config.EncodingFormat,
		MaxTokens:            config.MaxTokens,
		Threshold:            config.Threshold,
		EmbeddingsApiKey:     config.EmbeddingsApiKey,
		EmbeddingsApiVersion: config.EmbeddingsApiVersion,
		TokenManagement:      config.TokenManagement,
	}
}

func (openAIProvider *OpenAIConfig) EmbeddingRequest(ctx context.Context, prompt []string) ([][]float64, error) {

	// Create the request payload
	requestBody := openai_models.OpenAIEmbeddingRequest{
		Input:          prompt,
		Model:          openAIProvider.EmbeddingsModel,
		EncodingFormat: openAIProvider.EncodingFormat,
	}

	// Convert the request payload to JSON
	jsonData, err := json.Marshal(requestBody)
	if err != nil {
		return nil, fmt.Errorf("error encoding JSON: %v", err)
	}

	// Create a new HTTP POST request
	req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/v1/embeddings", openAIProvider.EmbeddingsBaseURL), bytes.NewBuffer(jsonData))
	if err != nil {
		return nil, fmt.Errorf("error creating request: %v", err)
	}

	// Set required headers
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", openAIProvider.EmbeddingsApiKey))

	// Make the HTTP request
	client := &http.Client{}
	resp, err := client.Do(req)
	if err != nil {
		// Check if the context was canceled
		if errors.Is(ctx.Err(), context.Canceled) {
			return nil, fmt.Errorf("request canceled: %v", err)
		}
		return nil, fmt.Errorf("error sending request: %v", err)
	}
	defer resp.Body.Close()

	// Read the response body
	body, err := ioutil.ReadAll(resp.Body)
	if err != nil {
		return nil, fmt.Errorf("error reading response: %v", err)
	}

	// Check for error status code
	if resp.StatusCode != http.StatusOK {
		var apiError models.AIError
		if err := json.Unmarshal(body, &apiError); err != nil {
			return nil, fmt.Errorf("error parsing error response: %v", err)
		}

		return nil, fmt.Errorf("embedding request failed with status code '%d' - %s\n", resp.StatusCode, apiError.Error.Message)
	}

	// Unmarshal the response JSON into the struct
	var embeddingResponse openai_models.OpenAIEmbeddingResponse
	err = json.Unmarshal(body, &embeddingResponse)
	if err != nil {
		return nil, fmt.Errorf("error decoding JSON response: %v", err)
	}

	// Count total tokens usage
	if embeddingResponse.UsageEmbedding.TotalTokens > 0 {
		openAIProvider.TokenManagement.UsedEmbeddingTokens(embeddingResponse.UsageEmbedding.TotalTokens, 0)
	}

	// Extract all embeddings
	var embeddings [][]float64
	for _, data := range embeddingResponse.Data {
		embeddings = append(embeddings, data.Embedding)
	}

	return embeddings, nil
}

func (openAIProvider *OpenAIConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan models.StreamResponse {
	responseChan := make(chan models.StreamResponse)
	var markdownBuffer strings.Builder // Buffer to accumulate content until newline
	var usage openai_models.Usage      // Variable to hold usage data

	go func() {
		defer close(responseChan)

		// Prepare the request body
		reqBody := openai_models.OpenAIChatCompletionRequest{
			Model: openAIProvider.ChatModel,
			Messages: []openai_models.Message{
				{Role: "system", Content: prompt},
				{Role: "user", Content: userInput},
			},
			Stream:      true,
			Temperature: &openAIProvider.Temperature,
			StreamOptions: openai_models.StreamOptions{
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
		req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/v1/chat/completions", openAIProvider.ChatBaseURL), bytes.NewBuffer(jsonData))
		if err != nil {
			markdownBuffer.Reset()
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error creating request: %v", err)}
			return
		}

		req.Header.Set("Content-Type", "application/json")
		req.Header.Set("Authorization", fmt.Sprintf("Bearer %s", openAIProvider.ChatApiKey))

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
					openAIProvider.TokenManagement.UsedTokens(usage.PromptTokens, usage.CompletionTokens)
				}

				break
			}

			if strings.HasPrefix(line, "data: ") {
				jsonPart := strings.TrimPrefix(line, "data: ")
				var response openai_models.OpenAIChatCompletionResponse
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

package ollama

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/models"
	ollama_models "github.com/meysamhadeli/codai/providers/ollama/models"
	contracts2 "github.com/meysamhadeli/codai/token_management/contracts"
	"io"
	"io/ioutil"
	"net/http"
	"strings"
)

// OllamaConfig implements the Provider interface for OpenAPI.
type OllamaConfig struct {
	ChatBaseURL       string
	EmbeddingsBaseURL string
	EmbeddingsModel   string
	ChatModel         string
	Temperature       *float32
	ReasoningEffort   *string
	EncodingFormat    string
	MaxTokens         int
	Threshold         float64
	TokenManagement   contracts2.ITokenManagement
}

// NewOllamaChatProvider initializes a new OpenAPIProvider.
func NewOllamaChatProvider(config *OllamaConfig) contracts.IChatAIProvider {
	return &OllamaConfig{
		ChatBaseURL:     config.ChatBaseURL,
		ChatModel:       config.ChatModel,
		Temperature:     config.Temperature,
		ReasoningEffort: config.ReasoningEffort,
		EncodingFormat:  config.EncodingFormat,
		MaxTokens:       config.MaxTokens,
		Threshold:       config.Threshold,
		TokenManagement: config.TokenManagement,
	}
}

// NewOllamaEmbeddingsProvider initializes a new OpenAPIProvider.
func NewOllamaEmbeddingsProvider(config *OllamaConfig) contracts.IEmbeddingAIProvider {
	return &OllamaConfig{
		EmbeddingsBaseURL: config.EmbeddingsBaseURL,
		EmbeddingsModel:   config.EmbeddingsModel,
		Temperature:       config.Temperature,
		EncodingFormat:    config.EncodingFormat,
		MaxTokens:         config.MaxTokens,
		Threshold:         config.Threshold,
		TokenManagement:   config.TokenManagement,
	}
}

func (ollamaProvider *OllamaConfig) EmbeddingRequest(ctx context.Context, prompt []string) ([][]float64, error) {
	// Create the request payload
	requestBody := ollama_models.OllamaEmbeddingRequest{
		Input: prompt,
		Model: ollamaProvider.EmbeddingsModel,
	}

	// Convert the request payload to JSON
	jsonData, err := json.Marshal(requestBody)
	if err != nil {
		return nil, fmt.Errorf("error encoding JSON: %v", err)
	}

	// Create a new HTTP POST request
	req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/api/embed", ollamaProvider.EmbeddingsBaseURL), bytes.NewBuffer(jsonData))
	if err != nil {
		return nil, fmt.Errorf("error creating request: %v", err)
	}

	// Set required headers
	req.Header.Set("Content-Type", "application/json")

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

	// Check for non-200 status codes
	if resp.StatusCode != http.StatusOK {
		var apiError models.AIError
		if err := json.Unmarshal(body, &apiError); err != nil {
			return nil, fmt.Errorf("error parsing error response: %v", err)
		}

		return nil, fmt.Errorf("embedding request failed with status code '%d' - %s\n", resp.StatusCode, apiError.Error.Message)
	}

	// Unmarshal the response JSON into the struct
	var embeddingResponse ollama_models.OllamaEmbeddingResponse
	err = json.Unmarshal(body, &embeddingResponse)
	if err != nil {
		return nil, fmt.Errorf("error decoding JSON response: %v", err)
	}

	// Count total tokens usage
	if embeddingResponse.PromptEvalCount > 0 {
		ollamaProvider.TokenManagement.UsedEmbeddingTokens(embeddingResponse.PromptEvalCount, 0)
	}

	// Return the parsed response
	return embeddingResponse.Embeddings, nil
}

func (ollamaProvider *OllamaConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) <-chan models.StreamResponse {
	responseChan := make(chan models.StreamResponse)
	var markdownBuffer strings.Builder // Buffer to accumulate content until newline

	go func() {
		defer close(responseChan)

		// Prepare the request body
		reqBody := ollama_models.OllamaChatCompletionRequest{
			Model: ollamaProvider.ChatModel,
			Messages: []ollama_models.Message{
				{Role: "system", Content: prompt},
				{Role: "user", Content: userInput},
			},
			Stream:      true,
			Temperature: &ollamaProvider.Temperature,
		}

		jsonData, err := json.Marshal(reqBody)
		if err != nil {
			markdownBuffer.Reset()
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error marshalling request body: %v", err)}
			return
		}

		// Create a new HTTP request
		req, err := http.NewRequestWithContext(ctx, "POST", fmt.Sprintf("%s/api/chat", ollamaProvider.ChatBaseURL), bytes.NewBuffer(jsonData))
		if err != nil {
			markdownBuffer.Reset()
			responseChan <- models.StreamResponse{Err: fmt.Errorf("error creating request: %v", err)}
			return
		}

		req.Header.Set("Content-Type", "application/json")

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

			var response ollama_models.OllamaChatCompletionResponse
			if err := json.Unmarshal([]byte(line), &response); err != nil {
				markdownBuffer.Reset()

				responseChan <- models.StreamResponse{Err: fmt.Errorf("error unmarshalling chunk: %v", err)}
				return
			}

			if len(response.Message.Content) > 0 {
				content := response.Message.Content
				markdownBuffer.WriteString(content)

				// Send chunk if it contains a newline, and then reset the buffer
				if strings.Contains(content, "\n") {
					responseChan <- models.StreamResponse{Content: markdownBuffer.String()}
					markdownBuffer.Reset()
				}
			}

			// Check if the response is marked as done
			if response.Done {
				//	// Signal end of stream
				responseChan <- models.StreamResponse{Content: markdownBuffer.String()}
				responseChan <- models.StreamResponse{Done: true}

				// Count total tokens usage
				if response.PromptEvalCount > 0 {
					ollamaProvider.TokenManagement.UsedTokens(response.PromptEvalCount, response.EvalCount)
				}

				break
			}
		}

		// Send any remaining content in the buffer
		if markdownBuffer.Len() > 0 {
			responseChan <- models.StreamResponse{Content: markdownBuffer.String()}
		}
	}()

	return responseChan
}

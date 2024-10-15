package ollama

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/providers/contracts"
	"github.com/meysamhadeli/codai/providers/models"
	"io/ioutil"
	"net/http"
)

// OllamaProvider implements the Provider interface for Ollama.
type OllamaProvider struct {
	embeddingURL        string
	chatURL             string
	embeddingModel      string
	chatModel           string
	stream              bool
	maxCompletionTokens int
	temperature         float32
	encodingFormat      string
}

// NewOllamaProvider initializes a new OllamaProvider.
func NewOllamaProvider() contracts.IAIProvider {
	return &OllamaProvider{
		embeddingURL:        "http://localhost:11434/v1/embeddings",
		chatURL:             "http://localhost:11434/v1/chat/completions",
		maxCompletionTokens: 4096,
		chatModel:           "llama3.1",
		embeddingModel:      "all-minilm:l6-v2",
		stream:              false,
		encodingFormat:      "float",
	}
}

func (ollamaProvider *OllamaProvider) EmbeddingRequest(ctx context.Context, prompt string) (*models.EmbeddingResponse, error) {

	// Create the request payload
	requestBody := models.EmbeddingRequest{
		Input:          prompt,
		Model:          ollamaProvider.embeddingModel,
		EncodingFormat: ollamaProvider.encodingFormat,
	}

	// Convert the request payload to JSON
	jsonData, err := json.Marshal(requestBody)
	if err != nil {
		return nil, fmt.Errorf("error encoding JSON: %v", err)
	}

	// Create a new HTTP POST request
	req, err := http.NewRequestWithContext(ctx, "POST", ollamaProvider.embeddingURL, bytes.NewBuffer(jsonData))
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
		return nil, fmt.Errorf("API request failed with status %d: %s", resp.StatusCode, string(body))
	}

	// Unmarshal the response JSON into the struct
	var embeddingResponse models.EmbeddingResponse
	err = json.Unmarshal(body, &embeddingResponse)
	if err != nil {
		return nil, fmt.Errorf("error decoding JSON response: %v", err)
	}

	// Return the parsed response
	return &embeddingResponse, nil
}

func (ollamaProvider *OllamaProvider) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) (*models.ChatCompletionResponse, error) {

	// Prepare the request body
	reqBody := models.ChatCompletionRequest{
		Model: ollamaProvider.chatModel,
		Messages: []models.Message{
			{Role: "system", Content: prompt},
			{Role: "user", Content: userInput},
		},
		StreamOptions: models.StreamOptions{
			Stream: ollamaProvider.stream,
		},
		MaxCompletionTokens: ollamaProvider.maxCompletionTokens,
		Temperature:         ollamaProvider.temperature,
	}

	jsonData, err := json.Marshal(reqBody)
	if err != nil {
		return nil, err
	}

	// Create a new HTTP request
	req, err := http.NewRequestWithContext(ctx, "POST", ollamaProvider.chatURL, bytes.NewBuffer(jsonData))
	if err != nil {
		return nil, fmt.Errorf("error creating request: %v", err)
	}

	// Set headers
	req.Header.Set("Content-Type", "application/json")

	// Create an HTTP client and send the request
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
		return nil, fmt.Errorf("error reading response body: %v", err)
	}

	// Unmarshal the response into the response struct
	var chatResponse models.ChatCompletionResponse
	err = json.Unmarshal(body, &chatResponse)
	if err != nil {
		return nil, fmt.Errorf("error unmarshalling response: %v", err)
	}

	return &chatResponse, nil
}

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
	"github.com/meysamhadeli/codai/utils"
	"io"
	"io/ioutil"
	"net/http"
	"strings"
)

// OllamaConfig implements the Provider interface for Ollama.
type OllamaConfig struct {
	EmbeddingURL        string
	ChatCompletionURL   string
	EmbeddingModel      string
	ChatCompletionModel string
	Temperature         float32
	EncodingFormat      string
	Threshold           float64
}

// NewOllamaProvider initializes a new OllamaProvider.
func NewOllamaProvider(config *OllamaConfig) contracts.IAIProvider {
	return &OllamaConfig{
		EmbeddingURL:        config.EmbeddingURL,
		ChatCompletionURL:   config.ChatCompletionURL,
		ChatCompletionModel: config.ChatCompletionModel,
		EmbeddingModel:      config.EmbeddingModel,
		EncodingFormat:      config.EncodingFormat,
		Temperature:         config.Temperature,
		Threshold:           config.Threshold,
	}
}

func (ollamaProvider *OllamaConfig) EmbeddingRequest(ctx context.Context, prompt string) (*models.EmbeddingResponse, error) {

	// Create the request payload
	requestBody := models.EmbeddingRequest{
		Input:          prompt,
		Model:          ollamaProvider.EmbeddingModel,
		EncodingFormat: ollamaProvider.EncodingFormat,
	}

	// Convert the request payload to JSON
	jsonData, err := json.Marshal(requestBody)
	if err != nil {
		return nil, fmt.Errorf("error encoding JSON: %v", err)
	}

	// Create a new HTTP POST request
	req, err := http.NewRequestWithContext(ctx, "POST", ollamaProvider.EmbeddingURL, bytes.NewBuffer(jsonData))
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

func (ollamaProvider *OllamaConfig) ChatCompletionRequest(ctx context.Context, userInput string, prompt string) (string, error) {

	// Prepare the request body
	reqBody := models.ChatCompletionRequest{
		Model: ollamaProvider.ChatCompletionModel,
		Messages: []models.Message{
			{
				Role:    "system",
				Content: prompt,
			},
			{
				Role:    "user",
				Content: userInput,
			},
		},
		Stream:      true, // Enable streaming
		Temperature: &ollamaProvider.Temperature,
	}

	jsonData, err := json.Marshal(reqBody)
	if err != nil {
		return "", err
	}

	// Create a new HTTP request
	req, err := http.NewRequestWithContext(ctx, "POST", ollamaProvider.ChatCompletionURL, bytes.NewBuffer(jsonData))
	if err != nil {
		return "", fmt.Errorf("error creating request: %v", err)
	}

	// Set headers
	req.Header.Set("Content-Type", "application/json")

	// Create an HTTP client and send the request
	client := &http.Client{}
	resp, err := client.Do(req)
	if err != nil {
		// Check if the context was canceled
		if errors.Is(ctx.Err(), context.Canceled) {
			return "", fmt.Errorf("request canceled: %v", err)
		}
		return "", fmt.Errorf("error sending request: %v", err)
	}
	defer resp.Body.Close()

	var buffer strings.Builder
	inCodeBlock := false

	// Create a buffered reader for reading the response stream
	reader := bufio.NewReader(resp.Body)

	var resultBuilder strings.Builder

	for {
		// Read the response line by line
		line, err := reader.ReadString('\n')
		if err != nil {
			if err == io.EOF {
				break
			}
			return "", fmt.Errorf("error reading stream: %v", err)
		}

		// Check for the end of the stream
		if line == "data: [DONE]\n" {
			break // Stop processing if we hit the end signal
		}

		if strings.HasPrefix(line, "data: ") {
			// Trim the "data: " prefix to get the actual JSON part
			jsonPart := strings.TrimPrefix(line, "data: ")

			// Parse the JSON to extract the response structure
			var response models.ChatCompletionResponse
			if err := json.Unmarshal([]byte(jsonPart), &response); err != nil {
				return "", fmt.Errorf("error unmarshalling chunk: %v", err)
			}

			// Safely extract the content from the response
			if len(response.Choices) > 0 {
				content := response.Choices[0].Delta.Content
				resultBuilder.WriteString(content)
				utils.RenderAndPrintMarkdown(content, &inCodeBlock, &buffer)
			}
		}
	}

	fmt.Println()

	// Return the final result or suggestions
	return resultBuilder.String(), nil
}

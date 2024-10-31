package ollama

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/meysamhadeli/codai/constants/lipgloss_color"
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
	MaxTokens           int
	Threshold           float64
	BufferingTheme      string
	TokenManagement     contracts.ITokenManagement
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
		MaxTokens:           config.MaxTokens,
		Threshold:           config.Threshold,
		BufferingTheme:      config.BufferingTheme,
		TokenManagement:     config.TokenManagement,
	}
}

func (ollamaProvider *OllamaConfig) EmbeddingRequest(ctx context.Context, prompt string) (*models.EmbeddingResponse, error) {

	// Count tokens for the user input and prompt
	totalChatTokens, err := ollamaProvider.TokenManagement.CountTokens(prompt, ollamaProvider.ChatCompletionModel)
	if err != nil {
		return nil, fmt.Errorf(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
	}

	// Check if enough tokens are available
	if err := ollamaProvider.TokenManagement.UseTokens(totalChatTokens); err != nil {
		return nil, fmt.Errorf(lipgloss_color.Red.Render(fmt.Sprintf("Error: %v", err)))
	}

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

	// Count tokens for the user input and prompt
	totalChatTokens, err := ollamaProvider.TokenManagement.CountTokens(fmt.Sprintf("%s%s", prompt, userInput), ollamaProvider.ChatCompletionModel)
	if err != nil {
		return "", fmt.Errorf(lipgloss_color.Red.Render(fmt.Sprintf("%v", err)))
	}

	// Check if enough tokens are available
	if err := ollamaProvider.TokenManagement.UseTokens(totalChatTokens); err != nil {
		return "", fmt.Errorf(lipgloss_color.Red.Render(fmt.Sprintf("Error: %v", err)))
	}

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

	// Check if the response status is not 200
	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("API request failed with status: %d", resp.StatusCode)
	}

	reader := bufio.NewReader(resp.Body)
	var resultBuilder strings.Builder
	var markdownBuffer strings.Builder // Buffer for accumulating chunks

	// Process each chunk as it arrives
	for {
		line, err := reader.ReadString('\n')
		if err != nil {
			if err == io.EOF {
				break
			}
			return "", fmt.Errorf("error reading stream: %v", err)
		}

		if line == "data: [DONE]\n" {
			break
		}

		if strings.HasPrefix(line, "data: ") {
			// Trim the "data: " prefix and parse JSON
			jsonPart := strings.TrimPrefix(line, "data: ")
			var response models.ChatCompletionResponse
			if err := json.Unmarshal([]byte(jsonPart), &response); err != nil {
				return "", fmt.Errorf("error unmarshalling chunk: %v", err)
			}

			if len(response.Choices) > 0 {
				content := response.Choices[0].Delta.Content
				resultBuilder.WriteString(content) // Gather full result for final return

				// Accumulate content in the markdown buffer
				markdownBuffer.WriteString(content)

				// Process complete Markdown blocks (indicated by double newlines)
				if strings.Contains(content, "\n") {
					blockContent := markdownBuffer.String()
					markdownBuffer.Reset()

					language := utils.DetectLanguageFromCodeBlock(blockContent)
					err := utils.RenderAndPrintMarkdown(blockContent, language, ollamaProvider.BufferingTheme)
					if err != nil {
						return "", err
					}
				}
			}
		}
	}

	// Flush remaining content in the buffer
	if markdownBuffer.Len() > 0 {
		blockContent := markdownBuffer.String()
		language := utils.DetectLanguageFromCodeBlock(blockContent)
		err := utils.RenderAndPrintMarkdown(blockContent, language, ollamaProvider.BufferingTheme)
		if err != nil {
			return "", err
		}
	}

	return resultBuilder.String(), nil
}

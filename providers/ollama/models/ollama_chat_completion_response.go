package models

import "time"

// OllamaChatCompletionResponse defines the structure of the combined Ollama API response.
type OllamaChatCompletionResponse struct {
	Model           string        `json:"model"`             // Name of the model used
	CreatedAt       time.Time     `json:"created_at"`        // Timestamp of when the response was created
	Message         OllamaMessage `json:"message"`           // Message details
	Done            bool          `json:"done"`              // Indicates if the response is complete
	PromptEvalCount int           `json:"prompt_eval_count"` // Number of prompt evaluations
	EvalCount       int           `json:"eval_count"`        // Number of evaluations
}

// OllamaMessage represents the content of the message from the assistant.
type OllamaMessage struct {
	Role    string `json:"role"`    // Role of the message sender (e.g., "assistant")
	Content string `json:"content"` // The content of the message
}

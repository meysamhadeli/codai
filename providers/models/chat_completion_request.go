package models

// ChatCompletionRequest Define the request body structure
type ChatCompletionRequest struct {
	Model       string    `json:"model"`
	Messages    []Message `json:"messages"`
	Temperature *float32  `json:"temperature,omitempty"` // Optional field (pointer to float32)
	Stream      bool      `json:"stream"`
}

package models

// DeepSeekChatCompletionRequest Define the request body structure
type DeepSeekChatCompletionRequest struct {
	Model       string    `json:"model"`
	Messages    []Message `json:"messages"`
	Temperature *float32  `json:"temperature,omitempty"` // Optional field (pointer to float32)
	Stream      bool      `json:"stream"`
}

// Message Define the request body structure
type Message struct {
	Role    string `json:"role"`
	Content string `json:"content"`
}

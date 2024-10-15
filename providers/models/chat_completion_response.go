package models

// ChatCompletionResponse Define the response structure
type ChatCompletionResponse struct {
	ID                string   `json:"id"`
	Object            string   `json:"object"`
	Created           int64    `json:"created"`
	Model             string   `json:"model"`
	SystemFingerprint string   `json:"system_fingerprint"`
	Choices           []Choice `json:"choices"`
	Usage             Usage    `json:"usage"`
}

// Choice Define the response structure
type Choice struct {
	Index        int     `json:"index"`
	Message      Message `json:"message"`
	Logprobs     *int    `json:"logprobs"` // Logprobs can be null, so we use a pointer
	FinishReason string  `json:"finish_reason"`
}

// Usage Define the response structure
type Usage struct {
	PromptTokens            int                     `json:"prompt_tokens"`
	CompletionTokens        int                     `json:"completion_tokens"`
	TotalTokens             int                     `json:"total_tokens"`
	CompletionTokensDetails CompletionTokensDetails `json:"completion_tokens_details"`
}

// Message Define the request body structure
type Message struct {
	Role    string `json:"role"`
	Content string `json:"content"`
}

// CompletionTokensDetails Define the response structure
type CompletionTokensDetails struct {
	ReasoningTokens int `json:"reasoning_tokens"`
}

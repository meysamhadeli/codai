package models

// EmbeddingResponse represents the entire response from the embedding API
type EmbeddingResponse struct {
	Object string    `json:"object"`
	Data   []Data    `json:"data"`
	Model  string    `json:"model"`
	Usage  UsageInfo `json:"usage"`
}

// Data represents each individual embedding
type Data struct {
	Object    string    `json:"object"`
	Embedding []float64 `json:"embedding"` // Embedding is an array of 1536 floats
	Index     int       `json:"index"`
}

// UsageInfo represents the token usage details in the API response
type UsageInfo struct {
	PromptTokens int `json:"prompt_tokens"`
	TotalTokens  int `json:"total_tokens"`
}

package models

// OpenAIEmbeddingResponse represents the entire response from the embedding API
type OpenAIEmbeddingResponse struct {
	Object         string         `json:"object"` // Object type, typically "list"
	Data           []Data         `json:"data"`   // Array of embedding data
	Model          string         `json:"model"`  // Model used for the embedding
	UsageEmbedding UsageEmbedding `json:"usage"`  // Token usage details
}

// Data represents each individual embedding
type Data struct {
	Object    string    `json:"object"`
	Embedding []float64 `json:"embedding"` // Embedding is an array of 1536 floats
	Index     int       `json:"index"`
}

// UsageEmbedding defines the token usage information for the embedding request.
type UsageEmbedding struct {
	PromptTokens int `json:"prompt_tokens"` // Number of tokens in the prompt
	TotalTokens  int `json:"total_tokens"`  // Total number of tokens used
}

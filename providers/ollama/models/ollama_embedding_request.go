package models

// OllamaEmbeddingRequest represents the request structure for the OpenAI embedding API
type OllamaEmbeddingRequest struct {
	Input []string `json:"input"` // The input text to be embedded
	Model string   `json:"model"` // The model used for generating embeddings
}

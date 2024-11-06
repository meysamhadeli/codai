package models

// OllamaEmbeddingResponse defines the structure of an embedding response from the Ollama API.
type OllamaEmbeddingResponse struct {
	Model           string      `json:"model"`             // The embedding model used (e.g., "all-minilm")
	Embeddings      [][]float64 `json:"embeddings"`        // Embedding vectors, where each embedding is a slice of float64 values
	PromptEvalCount int         `json:"prompt_eval_count"` // Count of prompt evaluations
}

package models

// OpenAIEmbeddingRequest represents the request structure for the OpenAI embedding API
type OpenAIEmbeddingRequest struct {
	Input          string `json:"input"`           // The input text to be embedded
	Model          string `json:"model"`           // The model used for generating embeddings
	EncodingFormat string `json:"encoding_format"` // The encoding format (in this case, "float")
}

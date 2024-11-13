package contracts

// IEmbeddingStore defines the interface for managing code and embeddings.
type IEmbeddingStore interface {
	Save(key string, code string, embeddings []float64)
	FindRelevantChunks(queryEmbedding []float64, topN int, embeddingModel string, threshold float64) []string
	CosineSimilarity(vec1, vec2 []float64) float64
}

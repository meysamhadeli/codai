package embedding_store

import (
	"fmt"
	"github.com/meysamhadeli/codai/embedding_store/contracts"
	"math"
	"sort"
)

// EmbeddingStore holds the embeddings and their corresponding code chunks.
type EmbeddingStore struct {
	EmbeddingsStore map[string][]float64
	CodeStore       map[string]string
}

// NewEmbeddingStoreModel initializes a new CodeEmbeddingStoreModel.
func NewEmbeddingStoreModel() contracts.IEmbeddingStore {
	return &EmbeddingStore{
		EmbeddingsStore: make(map[string][]float64),
		CodeStore:       make(map[string]string),
	}
}

// Save the embeddings and the corresponding code to the in-memory store.
func (store *EmbeddingStore) Save(key string, code string, embeddings []float64) {
	if len(embeddings) > 0 {
		store.EmbeddingsStore[key] = embeddings
		store.CodeStore[key] = code
	} else {
		fmt.Printf("No embeddings found for %s\n", key)
	}
}

func (store *EmbeddingStore) CosineSimilarity(vec1, vec2 []float64) float64 {
	var dotProduct, normA, normB float64
	for i := range vec1 {
		dotProduct += vec1[i] * vec2[i]
		normA += vec1[i] * vec1[i]
		normB += vec2[i] * vec2[i]
	}
	if normA == 0 || normB == 0 {
		return 0.0 // Avoid division by zero
	}
	return dotProduct / (math.Sqrt(normA) * math.Sqrt(normB))
}

// FindRelevantChunks retrieves the relevant code chunks from the embedding store based on a similarity threshold.
func (store *EmbeddingStore) FindRelevantChunks(queryEmbedding []float64, topN int, threshold float64) []string {
	type similarityResult struct {
		FileName   string
		Similarity float64
	}

	var results []similarityResult

	// Calculate similarity for each stored embedding
	for fileName, storedEmbedding := range store.EmbeddingsStore {
		similarity := store.CosineSimilarity(queryEmbedding, storedEmbedding)
		if similarity >= threshold { // Only consider embeddings with similarity above the threshold
			results = append(results, similarityResult{FileName: fileName, Similarity: similarity})
		}
	}

	// Sort results by similarity in descending order
	sort.Slice(results, func(i, j int) bool {
		return results[i].Similarity > results[j].Similarity
	})

	// Retrieve relevant code chunks, respecting the topN limit if provided
	var relevantCode []string
	for i := 0; i < len(results) && (topN == -1 || i < topN); i++ {
		fileName := results[i].FileName
		relevantCode = append(relevantCode, fmt.Sprintf("File: %s\nSimilarity: %.4f\n%s", fileName, results[i].Similarity, store.CodeStore[fileName]))
	}

	return relevantCode
}

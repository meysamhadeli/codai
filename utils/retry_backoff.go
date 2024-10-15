package utils

import (
	"github.com/cenkalti/backoff/v4"
	"time"
)

// RetryWithBackoff Function to encapsulate the retry logic with backoff
func RetryWithBackoff(operation func() error, maxRetries uint64) error {
	// Create a new exponential backoff configuration
	expBackoff := backoff.NewExponentialBackOff()

	// Set a max interval between retries (optional)
	expBackoff.MaxInterval = 5 * time.Second

	// Wrap the backoff with a fixed number of retries
	backoffWithRetries := backoff.WithMaxRetries(expBackoff, maxRetries)

	// Retry the operation using the backoff strategy with the retry limit
	return backoff.Retry(operation, backoffWithRetries)
}

package cmd

import (
	"bytes"
	"os"
	"testing"
)

func TestCodeCommand(t *testing.T) {
	// Save original stdin and args
	originalStdin := os.Stdin
	originalArgs := os.Args

	// Create a pipe to simulate user input
	reader, writer, _ := os.Pipe()
	os.Stdin = reader // Redirect stdin to the pipe

	// Prepare the simulated user input
	userInput := "please remove all of comments from Foo.cs and Foo1.cs\n"
	writer.Write([]byte(userInput)) // Write to the pipe
	writer.Close()                  // Close the writer to signal EOF

	// Create a buffer to capture the output
	var buf bytes.Buffer
	codeCmd.SetOut(&buf)
	codeCmd.SetErr(&buf)

	// Simulate command-line arguments
	os.Args = []string{"codai", "code"} // Simulate calling 'codai code'

	// Execute the command
	err := codeCmd.Execute()
	if err != nil {
		t.Fatalf("codeCmd.Execute() failed: %v", err)
	}

	// Restore original stdin and args
	os.Stdin = originalStdin
	os.Args = originalArgs

	output := buf.String()
	if output == "" {
		t.Fatal("Expected output, got nothing")
	}

	// Validate the output if necessary
	expectedOutput := "You entered: print hello world\n" // Expected output
	if !bytes.Contains([]byte(output), []byte(expectedOutput)) {
		t.Fatalf("Expected output to contain '%s', got: %s", expectedOutput, output)
	}
}

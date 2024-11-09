package lipgloss

import "github.com/charmbracelet/lipgloss"

var (
	BoxStyle = lipgloss.NewStyle().Foreground(lipgloss.Color("#FFFFFF")).Bold(true).Border(lipgloss.NormalBorder()).PaddingLeft(1).PaddingRight(1).Align(lipgloss.Left)
)

package lipgloss

import "github.com/charmbracelet/lipgloss"

var (
	BlueSky    = lipgloss.NewStyle().Foreground(lipgloss.Color("#00BFFF"))
	LightBlue  = lipgloss.NewStyle().Foreground(lipgloss.Color("#2b7fec")).Bold(true)
	LightBlueB = lipgloss.NewStyle().Background(lipgloss.Color("#E5E7E9")).Foreground(lipgloss.Color("#2b7fec")).Bold(true)
	Red        = lipgloss.NewStyle().Foreground(lipgloss.Color("197"))
	Green      = lipgloss.NewStyle().Foreground(lipgloss.Color("76"))
	Yellow     = lipgloss.NewStyle().Foreground(lipgloss.Color("#FCF55F"))
	Violet     = lipgloss.NewStyle().Foreground(lipgloss.Color("#7F00FF"))
	Charm      = lipgloss.NewStyle().Foreground(lipgloss.Color("205")).Bold(true)
	CharmB     = lipgloss.NewStyle().Background(lipgloss.Color("#E5E7E9")).Foreground(lipgloss.Color("205")).Bold(true)
	Gray       = lipgloss.NewStyle().Foreground(lipgloss.Color("#bcbcbc"))
)

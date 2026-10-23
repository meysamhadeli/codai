using Codai.Core.Abstractions;

namespace Codai.Tools;

/// <summary>Builds the repository and returns compiler diagnostics.</summary>
public sealed class DotnetBuildTool : DotnetWorkflowToolBase
{
    public DotnetBuildTool(IProcessRunner runner) : base(runner) { }

    public override string Name => "dotnet_build";

    public override string Description =>
        "Run dotnet build from the project root and return compiler diagnostics. This may write build artifacts and requires approval.";

    protected override string CommandLine => "dotnet build --nologo";
}
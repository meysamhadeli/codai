using Codai.Core.Abstractions;

namespace Codai.Tools;

/// <summary>Runs the repository's .NET test suite and returns diagnostics.</summary>
public sealed class DotnetTestTool : DotnetWorkflowToolBase
{
    public DotnetTestTool(IProcessRunner runner) : base(runner) { }

    public override string Name => "dotnet_test";

    public override string Description =>
        "Run dotnet test from the project root and return its diagnostics. This may write build artifacts and requires approval.";

    protected override string CommandLine => "dotnet test --nologo";
}
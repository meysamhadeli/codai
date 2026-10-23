using Codai.Core.Abstractions;
using Codai.Core.Agents;

namespace Codai.Application.Acp;

/// <summary>Session settings supplied by an ACP host.</summary>
public sealed record AcpSessionRequest(
    string ProjectRoot,
    string? Provider = null,
    string? Model = null,
    AgentMode Mode = AgentModeParser.Default,
    bool Resume = false,
    string? TaskId = null,
    string? DataDirectory = null);

/// <summary>The services an ACP protocol session can control.</summary>
public sealed record AcpSessionHandle(
    ICodaiSession Session,
    SessionOptions Options,
    AgentModeState Mode,
    IDisposable? Lifetime = null);

/// <summary>Creates application sessions without coupling the protocol to a host's DI container.</summary>
public interface IAcpSessionFactory
{
    AcpSessionHandle Create(AcpSessionRequest request, IUserInterface userInterface);
}
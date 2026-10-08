namespace Codai.Core.Chat;

/// <summary>
/// One user request and the model response that answered it.
/// </summary>
public sealed record ChatTurn(DateTimeOffset Timestamp, string UserRequest, string AiResponse);

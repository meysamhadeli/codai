using System.Runtime.CompilerServices;
using Codai.Core.Abstractions;
using Codai.Core.Chat;

namespace Codai.Application;

internal static class ChatCompletionActivity
{
    internal static async IAsyncEnumerable<ChatStreamUpdate> StreamWithActivityAsync(
        this IChatCompletionService service,
        ChatRequest request,
        IUserInterface? ui,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();
        ui?.ModelRequestStarted(requestId);
        try
        {
            await foreach (var update in service.StreamAsync(request, cancellationToken)
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                ui?.ModelRequestUpdated(requestId, update);
                yield return update;
            }
        }
        finally
        {
            ui?.ModelRequestCompleted(requestId);
        }
    }
}

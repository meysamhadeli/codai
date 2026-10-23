using Codai.Cli;
using Codai.Mcp.Server;

if (!Console.IsOutputRedirected)
{
    Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler onCancel = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += onCancel;
try
{
    if (args.Length > 0 && string.Equals(args[0], "mcp", StringComparison.OrdinalIgnoreCase))
    {
        return await CodaiMcpServerApplication
            .RunAsync(args[1..], cancellation.Token)
            .ConfigureAwait(false);
    }

    return await new CliApplication().RunAsync(args, cancellation.Token).ConfigureAwait(false);
}
finally
{
    Console.CancelKeyPress -= onCancel;
}

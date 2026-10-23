using System.Net;
using System.Net.Sockets;

namespace Codai.Tools;

internal sealed class PublicWebTransport : IDisposable
{
    public PublicWebTransport()
    {
        Client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            MaxResponseHeadersLength = 32,
            ConnectCallback = (context, cancellationToken) => ConnectAsync(
                context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellationToken),
        })
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    public HttpClient Client { get; }

    public void Dispose() => Client.Dispose();

    internal static async ValueTask<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var addresses = await (resolver is null
            ? Dns.GetHostAddressesAsync(host, cancellationToken)
            : resolver(host, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            throw new HttpRequestException("Web retrieval requires exclusively public destination addresses.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublicAddress(address.MapToIPv4());
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.ScopeId == 0 && (bytes[0] & 0xe0) == 0x20 &&
                !(bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] & 0xfe) == 0) &&
                !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) &&
                !(bytes[0] == 0x3f && bytes[1] == 0xff && (bytes[2] & 0xf0) == 0) &&
                !(bytes[0] == 0x20 && bytes[1] == 0x02);
        }

        return address.AddressFamily == AddressFamily.InterNetwork &&
            bytes[0] is not (0 or 10 or 127) && bytes[0] < 224 &&
            !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) &&
            !(bytes[0] == 169 && bytes[1] == 254) &&
            !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) &&
            !(bytes[0] == 192 && bytes[1] == 168) &&
            !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2) &&
            !(bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99) &&
            !(bytes[0] == 198 && bytes[1] is 18 or 19) &&
            !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) &&
            !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
    }
}

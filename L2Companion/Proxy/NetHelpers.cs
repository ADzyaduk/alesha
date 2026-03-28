using System.IO;
using System.Net.Sockets;

namespace L2Companion.Proxy;

internal static class NetHelpers
{
    public static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
            if (n == 0)
            {
                throw new IOException("Socket closed");
            }

            read += n;
        }
    }

    public static bool IsExpectedDisconnect(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return true;
        }

        var baseEx = ex.GetBaseException();
        return baseEx is OperationCanceledException
            or IOException
            or SocketException
            or ObjectDisposedException;
    }
}


using System.Net;
using System.Net.Sockets;
using L2Companion.Core;
using L2Companion.Protocol;

namespace L2Companion.Proxy;

internal sealed class LoginProxySession
{
    private readonly TcpClient _client;
    private readonly ProxySettings _settings;
    private readonly LogService _log;
    private readonly ProxyDiagnostics _diag;
    private readonly Action<string, int> _onGameDiscovered;

    private L2Blowfish? _blowfish;

    public LoginProxySession(
        TcpClient client,
        ProxySettings settings,
        LogService log,
        ProxyDiagnostics diag,
        Action<string, int> onGameDiscovered)
    {
        _client = client;
        _settings = settings;
        _log = log;
        _diag = diag;
        _onGameDiscovered = onGameDiscovered;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _diag.LoginClientConnected = true;
        using var server = new TcpClient();

        try
        {
            await server.ConnectAsync(_settings.LoginHost, _settings.LoginPort, ct);
            _diag.LoginServerConnected = true;
        }
        catch (Exception ex)
        {
            _diag.LastError = $"Login connect failed: {ex.Message}";
            throw;
        }

        using var clientStream = _client.GetStream();
        using var serverStream = server.GetStream();

        var s2c = RelayAsync(serverStream, clientStream, isServerToClient: true, ct);
        var c2s = RelayAsync(clientStream, serverStream, isServerToClient: false, ct);

        await Task.WhenAny(s2c, c2s);

        _diag.LoginClientConnected = false;
        _diag.LoginServerConnected = false;
    }

    private async Task RelayAsync(NetworkStream from, NetworkStream to, bool isServerToClient, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var header = new byte[2];
                await NetHelpers.ReadExactAsync(from, header, ct);
                var totalLen = BitConverter.ToUInt16(header, 0);
                var bodyLen = totalLen - 2;

                if (bodyLen <= 0)
                {
                    continue;
                }

                var body = new byte[bodyLen];
                await NetHelpers.ReadExactAsync(from, body, ct);

                if (isServerToClient)
                {
                    ProcessServerToClient(body);
                }

                await to.WriteAsync(header, ct);
                await to.WriteAsync(body, ct);
            }
            catch (Exception ex) when (NetHelpers.IsExpectedDisconnect(ex, ct))
            {
                return;
            }
        }
    }

    private void ProcessServerToClient(byte[] body)
    {
        if (_blowfish is null)
        {
            if (body.Length > 0 && body[0] == 0x00 && body.Length >= 1 + 4 + 4 + 128 + 16 + 16)
            {
                var keyStart = 1 + 4 + 4 + 128 + 16;
                var key = body.Skip(keyStart).Take(16).ToArray();
                _blowfish = new L2Blowfish(key);
                _diag.LoginBlowfishReady = true;
                _diag.SessionStage = "login-blowfish-ready";
                _log.Info("Login blowfish key initialized.");
            }

            return;
        }

        if (body.Length % 8 != 0)
        {
            return;
        }

        var decrypted = (byte[])body.Clone();
        _blowfish.DecryptInPlace(decrypted);
        if (decrypted.Length == 0 || decrypted[0] != 0x04)
        {
            return;
        }

        try
        {
            if (TryPatchServerListInPlace(decrypted, out var patchedCount))
            {
                _blowfish.EncryptInPlace(decrypted);
                Buffer.BlockCopy(decrypted, 0, body, 0, body.Length);
                _diag.MarkServerListPatched();
                _diag.SessionStage = "server-list-patched";
                _log.Info($"Login ServerList patched, entries={patchedCount}.");
            }
        }
        catch (Exception ex)
        {
            _diag.LastError = $"ServerList patch skipped: {ex.Message}";
            _log.Info($"ServerList patch skipped: {ex.Message}");
        }
    }

    private bool TryPatchServerListInPlace(byte[] packet, out int patchedCount)
    {
        patchedCount = 0;
        if (packet.Length < 4)
        {
            return false;
        }

        var logicalLen = FindLogicalLength(packet);
        var checksumStart = logicalLen - 4;

        var count = packet[1];
        var pos = 3;
        if (count <= 0 || checksumStart <= pos)
        {
            return false;
        }

        var payloadForStride = checksumStart - pos;
        var stride = 16;
        if (payloadForStride > 0 && payloadForStride % count == 0)
        {
            stride = payloadForStride / count;
        }

        if (stride < 9)
        {
            stride = 16;
        }

        var discovered = false;
        for (var i = 0; i < count; i++)
        {
            if (pos + stride > checksumStart)
            {
                break;
            }

            var serverId = packet[pos];
            var ipBytes = packet.Skip(pos + 1).Take(4).ToArray();
            var ip = string.Join('.', ipBytes);
            var port = BitConverter.ToInt32(packet, pos + 5);

            if (port is < 1 or > 65535)
            {
                pos += stride;
                continue;
            }

            if (!discovered)
            {
                discovered = true;
                _diag.RealGameEndpoint = $"{ip}:{port}";
                _onGameDiscovered(ip, port);
                _log.Info($"Discovered real game endpoint from ServerList: {ip}:{port}");
            }

            var localhost = IPAddress.Loopback.GetAddressBytes();
            Buffer.BlockCopy(localhost, 0, packet, pos + 1, 4);
            BitConverter.GetBytes(_settings.LocalGamePort).CopyTo(packet, pos + 5);

            patchedCount++;
            _log.Info($"ServerList entry id={serverId} patched to 127.0.0.1:{_settings.LocalGamePort}");
            pos += stride;
        }

        if (patchedCount == 0)
        {
            return false;
        }

        AppendChecksum(packet, checksumStart);
        return true;
    }

    private static int FindLogicalLength(byte[] body)
    {
        var n = body.Length;
        for (var end = 8; end <= n; end += 4)
        {
            if (!VerifyChecksum(body, end))
            {
                continue;
            }

            var tailAllZero = true;
            for (var i = end; i < n; i++)
            {
                if (body[i] != 0)
                {
                    tailAllZero = false;
                    break;
                }
            }

            if (tailAllZero)
            {
                return end;
            }
        }

        return n;
    }

    private static bool VerifyChecksum(byte[] body, int size)
    {
        if (size <= 4 || (size & 3) != 0)
        {
            return false;
        }

        uint checksum = 0;
        for (var i = 0; i < size - 4; i += 4)
        {
            checksum ^= BitConverter.ToUInt32(body, i);
        }

        var stored = BitConverter.ToUInt32(body, size - 4);
        return stored == checksum;
    }

    private static void AppendChecksum(byte[] body, int checksumStart)
    {
        uint checksum = 0;
        for (var i = 0; i < checksumStart; i += 4)
        {
            checksum ^= BitConverter.ToUInt32(body, i);
        }

        BitConverter.GetBytes(checksum).CopyTo(body, checksumStart);
    }
}




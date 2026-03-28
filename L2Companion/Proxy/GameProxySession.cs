using System.Collections.Concurrent;
using System.Net.Sockets;
using L2Companion.Core;
using L2Companion.Protocol;
using L2Companion.World;

namespace L2Companion.Proxy;

internal sealed class GameProxySession
{
    private readonly TcpClient _client;
    private readonly string _realGameHost;
    private readonly int _realGamePort;
    private readonly LogService _log;
    private readonly GamePacketParser _parser;
    private readonly ProxyDiagnostics _diag;
    private readonly ServerProfileResolver _profileResolver;
    private readonly Action<GameProxySession>? _onDisconnected;

    private readonly ConcurrentQueue<byte[]> _injectQueue = new();
    private readonly SemaphoreSlim _injectSignal = new(0);
    private readonly SemaphoreSlim _serverSendGate = new(1, 1);

    private NetworkStream? _serverStream;

    private L2GameCrypt? _s2cShadowXor;
    private L2GameCrypt? _c2sClientXor;
    private L2GameCrypt? _c2sServerXor;

    public GameProxySession(
        TcpClient client,
        string realGameHost,
        int realGamePort,
        LogService log,
        GamePacketParser parser,
        ProxyDiagnostics diag,
        ServerProfileResolver profileResolver,
        Action<GameProxySession>? onDisconnected = null)
    {
        _client = client;
        _realGameHost = realGameHost;
        _realGamePort = realGamePort;
        _log = log;
        _parser = parser;
        _diag = diag;
        _profileResolver = profileResolver;
        _onDisconnected = onDisconnected;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _diag.GameClientConnected = true;
        _diag.GameServerConnected = false;
        _diag.GameCryptoReady = false;
        _diag.SessionStage = "game-connecting-server";

        try
        {
            using var server = new TcpClient();
            try
            {
                await server.ConnectAsync(_realGameHost, _realGamePort, ct);
                _diag.GameServerConnected = true;
                _diag.SessionStage = "game-relay-online";
            }
            catch (Exception ex)
            {
                _diag.LastError = $"Game connect failed: {ex.Message}";
                _diag.SessionStage = "game-connect-failed";
                throw;
            }

            using var clientStream = _client.GetStream();
            _serverStream = server.GetStream();

            var s2c = RelayAsync(_serverStream, clientStream, isServerToClient: true, ct);
            var c2s = RelayAsync(clientStream, _serverStream, isServerToClient: false, ct);
            _ = ProcessInjectQueueAsync(ct);

            var done = await Task.WhenAny(s2c, c2s);
            if (done.IsFaulted && done.Exception is not null)
            {
                var ex = done.Exception.GetBaseException();
                _diag.LastError = $"Game relay terminated: {ex.Message}";
                _log.Info($"Game relay terminated: {ex.Message}");
            }
            else if (done.IsCanceled)
            {
                _diag.LastError = "-";
                _log.Info("Game relay canceled (expected shutdown).");
            }
            else
            {
                _diag.LastError = "-";
                _log.Info("Game relay ended (socket closed, expected)." );
            }

        }
        finally
        {
            _onDisconnected?.Invoke(this);
        }
    }

    public void EnqueueInjectPacket(byte[] plainBody)
    {
        if (plainBody.Length == 0)
        {
            _diag.MarkDrop("inject-empty-payload");
            return;
        }

        _injectQueue.Enqueue((byte[])plainBody.Clone());
        _diag.SetPendingInjectPackets(_injectQueue.Count);
        _injectSignal.Release();
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
                    HandleServerPacket(body);
                    await to.WriteAsync(header, ct);
                    await to.WriteAsync(body, ct);
                    continue;
                }

                await RelayClientToServerAsync(to, header, body, ct);
            }
            catch (Exception ex) when (NetHelpers.IsExpectedDisconnect(ex, ct))
            {
                return;
            }
        }
    }

    private async Task RelayClientToServerAsync(NetworkStream to, byte[] header, byte[] encryptedBody, CancellationToken ct)
    {
        byte[] outboundBody = encryptedBody;
        byte c2sOpcode = 0xFF;

        await _serverSendGate.WaitAsync(ct);
        try
        {
            if (_c2sClientXor is not null && _c2sServerXor is not null)
            {
                try
                {
                    var plain = _c2sClientXor.DecryptCopy(encryptedBody);
                    c2sOpcode = plain.Length > 0 ? plain[0] : (byte)0xFF;
                    outboundBody = _c2sServerXor.EncryptCopy(plain);
                }
                catch (Exception ex)
                {
                    _diag.LastError = $"C2S relay crypto error: {ex.Message}";
                    _diag.MarkDrop("c2s-crypto-error-raw-forward");
                    _log.Info($"C2S relay crypto error, forwarding raw packet: {ex.Message}");
                    outboundBody = encryptedBody;
                }
            }

            await to.WriteAsync(header, ct);
            await to.WriteAsync(outboundBody, ct);
        }
        finally
        {
            _serverSendGate.Release();
        }

        _diag.MarkC2S(c2sOpcode);
    }

    private void HandleServerPacket(byte[] encryptedBody)
    {
        if (_s2cShadowXor is null)
        {
            if (encryptedBody.Length < 10 || encryptedBody[0] != 0x00)
            {
                return;
            }

            var dynamicKey = encryptedBody.Skip(2).Take(8).ToArray();
            var fullKey = dynamicKey.Concat(L2Constants.BlowfishDynamicSuffix).ToArray();

            _s2cShadowXor = new L2GameCrypt();
            _s2cShadowXor.Init(fullKey);

            _c2sClientXor = new L2GameCrypt();
            _c2sClientXor.Init(fullKey);

            _c2sServerXor = new L2GameCrypt();
            _c2sServerXor.Init(fullKey);

            _diag.GameCryptoReady = true;
            _diag.SessionStage = "game-crypto-ready";
            _diag.MarkInjectResult("crypto-ready");
            _log.Info("Game session XOR crypto initialized from BlowfishInit.");
            return;
        }

        var plain = _s2cShadowXor.DecryptCopy(encryptedBody);
        if (plain.Length <= 1)
        {
            return;
        }

        var opcode = plain[0];
        _profileResolver.FeedServerOpcode(opcode);
        _diag.ServerProfile = _profileResolver.ResolvedProfile;

        _diag.MarkS2C(opcode);

        var payload = ExtractPayload(plain);
        try
        {
            _parser.ParseServerPacket(opcode, payload);
        }
        catch (Exception ex)
        {
            _diag.LastError = $"S2C parse error op=0x{opcode:X2}: {ex.Message}";
            _diag.MarkDrop($"s2c-parse-error:0x{opcode:X2}");
            _log.Info($"S2C parse error op=0x{opcode:X2}: {ex.Message}");
        }
    }

    private static ReadOnlySpan<byte> ExtractPayload(byte[] plain)
    {
        if (plain.Length <= 1)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        if (TryFindChecksumLogicalLength(plain.AsSpan(1), out var payloadLen))
        {
            return plain.AsSpan(1, payloadLen);
        }

        return plain.AsSpan(1);
    }

    private static bool TryFindChecksumLogicalLength(ReadOnlySpan<byte> bodyWithoutOpcode, out int payloadLen)
    {
        payloadLen = 0;
        var n = bodyWithoutOpcode.Length;
        if (n < 8)
        {
            return false;
        }

        var max = (n / 4) * 4;
        for (var size = 8; size <= max; size += 4)
        {
            if (!VerifyChecksum(bodyWithoutOpcode.Slice(0, size)))
            {
                continue;
            }

            var tailAllZero = true;
            for (var i = size; i < n; i++)
            {
                if (bodyWithoutOpcode[i] != 0)
                {
                    tailAllZero = false;
                    break;
                }
            }

            if (!tailAllZero)
            {
                continue;
            }

            payloadLen = Math.Max(0, size - 4);
            return true;
        }

        return false;
    }

    private static bool VerifyChecksum(ReadOnlySpan<byte> body)
    {
        if (body.Length <= 4 || (body.Length & 3) != 0)
        {
            return false;
        }

        uint checksum = 0;
        for (var i = 0; i < body.Length - 4; i += 4)
        {
            checksum ^= BitConverter.ToUInt32(body.Slice(i, 4));
        }

        var stored = BitConverter.ToUInt32(body.Slice(body.Length - 4, 4));
        return checksum == stored;
    }

    private async Task ProcessInjectQueueAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _injectSignal.WaitAsync(ct);

                while (_injectQueue.TryDequeue(out var plainBody))
                {
                    _diag.SetPendingInjectPackets(_injectQueue.Count);
                    var sent = false;

                    await _serverSendGate.WaitAsync(ct);
                    try
                    {
                        if (_serverStream is not null && _c2sServerXor is not null)
                        {
                            var body = _c2sServerXor.EncryptCopy(plainBody);
                            var framed = new byte[2 + body.Length];
                            BitConverter.GetBytes((ushort)framed.Length).CopyTo(framed, 0);
                            Buffer.BlockCopy(body, 0, framed, 2, body.Length);

                            await _serverStream.WriteAsync(framed, ct);
                            _diag.MarkInject(plainBody[0]);
                            _diag.MarkInjectResult($"sent:0x{plainBody[0]:X2}");
                            sent = true;
                        }
                    }
                    finally
                    {
                        _serverSendGate.Release();
                    }

                    if (sent)
                    {
                        continue;
                    }

                    _diag.MarkDrop("inject-deferred-crypto-not-ready");

                    // Keep packet until game crypto is ready; do not lose early bot commands.
                    _injectQueue.Enqueue(plainBody);
                    _diag.SetPendingInjectPackets(_injectQueue.Count);
                    _injectSignal.Release();
                    await Task.Delay(40, ct);
                    break;
                }
            }
        }
        catch (Exception ex) when (NetHelpers.IsExpectedDisconnect(ex, ct))
        {
            // expected on normal session shutdown
        }
    }
}

internal static class GameCryptExt
{
    public static byte[] DecryptCopy(this L2GameCrypt crypt, byte[] data)
    {
        var copy = (byte[])data.Clone();
        crypt.DecryptInPlace(copy);
        return copy;
    }

    public static byte[] EncryptCopy(this L2GameCrypt crypt, byte[] data)
    {
        var copy = (byte[])data.Clone();
        crypt.EncryptInPlace(copy);
        return copy;
    }
}










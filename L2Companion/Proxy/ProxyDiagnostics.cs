using System.Threading;

namespace L2Companion.Proxy;

public sealed class ProxyDiagnostics
{
    private long _s2cPackets;
    private long _c2sPackets;
    private long _injectPackets;
    private long _serverListPatched;
    private long _pendingInjectPackets;
    private long _unknownServerOpcodes;

    public bool LoginClientConnected { get; set; }
    public bool LoginServerConnected { get; set; }
    public bool LoginBlowfishReady { get; set; }
    public bool GameClientConnected { get; set; }
    public bool GameServerConnected { get; set; }
    public bool GameCryptoReady { get; set; }

    public string RealGameEndpoint { get; set; } = "-";
    public string LastS2COpcode { get; set; } = "-";
    public string LastC2SOpcode { get; set; } = "-";
    public string LastInjectOpcode { get; set; } = "-";
    public string LastError { get; set; } = "-";

    public string SessionStage { get; set; } = "idle";
    public string ServerProfile { get; set; } = "AutoDetect";
    public string LastInjectResult { get; set; } = "-";
    public string LastDropReason { get; set; } = "-";
    public DateTime LastParsedAtUtc { get; set; } = DateTime.MinValue;

    public long S2CPackets => Interlocked.Read(ref _s2cPackets);
    public long C2SPackets => Interlocked.Read(ref _c2sPackets);
    public long InjectPackets => Interlocked.Read(ref _injectPackets);
    public long ServerListPatched => Interlocked.Read(ref _serverListPatched);
    public long PendingInjectPackets => Interlocked.Read(ref _pendingInjectPackets);
    public long UnknownServerOpcodes => Interlocked.Read(ref _unknownServerOpcodes);

    public void MarkS2C(byte opcode)
    {
        Interlocked.Increment(ref _s2cPackets);
        LastS2COpcode = $"0x{opcode:X2}";
        LastParsedAtUtc = DateTime.UtcNow;
    }

    public void MarkC2S(byte opcode)
    {
        Interlocked.Increment(ref _c2sPackets);
        LastC2SOpcode = $"0x{opcode:X2}";
    }

    public void MarkInject(byte opcode)
    {
        Interlocked.Increment(ref _injectPackets);
        LastInjectOpcode = $"0x{opcode:X2}";
    }

    public void MarkInjectResult(string result)
    {
        LastInjectResult = string.IsNullOrWhiteSpace(result) ? "-" : result;
    }

    public void MarkDrop(string reason)
    {
        LastDropReason = string.IsNullOrWhiteSpace(reason) ? "drop-unknown" : reason;
    }

    public void MarkUnknownServerOpcode(byte opcode)
    {
        Interlocked.Increment(ref _unknownServerOpcodes);
        LastDropReason = $"unknown-opcode:0x{opcode:X2}";
    }

    public void SetPendingInjectPackets(int count)
    {
        Interlocked.Exchange(ref _pendingInjectPackets, Math.Max(0, count));
    }

    public void MarkServerListPatched()
    {
        Interlocked.Increment(ref _serverListPatched);
    }
}

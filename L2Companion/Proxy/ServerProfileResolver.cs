namespace L2Companion.Proxy;

public sealed class ServerProfileResolver
{
    private int _teonHints;
    private int _classicHints;

    public ServerProfileMode Mode { get; set; } = ServerProfileMode.AutoDetect;
    public string ResolvedProfile { get; private set; } = "AutoDetect";

    public void Reset(ServerProfileMode mode)
    {
        Mode = mode;
        _teonHints = 0;
        _classicHints = 0;
        ResolvedProfile = mode switch
        {
            ServerProfileMode.TeonLike => "TeonLike",
            ServerProfileMode.ClassicL2J => "ClassicL2J",
            _ => "AutoDetect"
        };
    }

    public void FeedServerOpcode(byte opcode)
    {
        if (Mode != ServerProfileMode.AutoDetect)
        {
            return;
        }

        if (opcode is 0x6D or 0x72 or 0x47 or 0x06)
        {
            _teonHints++;
        }

        if (opcode is 0x0E or 0x0B or 0x24 or 0x12)
        {
            _classicHints++;
        }

        if (_teonHints >= 3 && _teonHints >= _classicHints)
        {
            ResolvedProfile = "TeonLike";
        }
        else if (_classicHints >= 3 && _classicHints > _teonHints)
        {
            ResolvedProfile = "ClassicL2J";
        }
    }
}

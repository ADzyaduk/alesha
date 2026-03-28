using L2Companion.Core;

namespace L2Companion.World;

public sealed class TeonOpcodeDetector
{
    private readonly LogService _log;
    private readonly Dictionary<byte, List<int>> _sizeCounts = [];
    private readonly Dictionary<byte, int> _rangeHits = [];
    private Dictionary<string, byte> _opcodes = [];

    public const int NpcInfoPayloadSize = 187;
    public const int NpcInfoPayloadMin = 180;
    public const int NpcInfoPayloadMax = 210;

    private const int MinNpcInfoCountStrict = 3;
    private const int MinNpcInfoCountRange = 8;

    private static readonly IReadOnlyDictionary<string, byte> BaseOpcodes = new Dictionary<string, byte>
    {
        ["CharList"] = 0x13,
        ["CharSelected"] = 0x15,
        ["UserInfo"] = 0x04,
        ["NpcInfo"] = 0x16,
        ["MoveToPoint"] = 0x01,
        ["StatusUpdate"] = 0x6D,
        ["SkillList"] = 0x58,
        ["ItemList"] = 0x1B,
        ["InventoryUpdate"] = 0x21,
        ["SystemMessage"] = 0x62,
        ["LogoutOk"] = 0x7F,
        ["LeaveWorld"] = 0x6E,
        ["Say2"] = 0x54,
        ["StopMove"] = 0x2D,
        ["ChangeWaitType"] = 0x25,
        ["Die"] = 0x06,
        ["Die2"] = 0x12,
        ["DeleteObject"] = 0x72,
        ["TargetSelected"] = 0x47,
        ["SpawnItem"] = 0x0C,
        ["Attack"] = 0x60,
        ["SkillCoolTime"] = 0x6A,
        ["MagicSkillLaunched"] = 0x48,
        ["AbnormalStatusUpdate"] = 0x7F,
        ["PartySmallWindowAll"] = 0x4E,
        ["PartySmallWindowAdd"] = 0x4F,
        ["PartySmallWindowDelete"] = 0x50,
        ["PartySmallWindowUpdate"] = 0x52,
        ["PartySpelled"] = 0xEE,
        ["ShortBuffStatusUpdate"] = 0x91,
        ["StatusUpdate2"] = 0x0E
    };

    public TeonOpcodeDetector(LogService log)
    {
        _log = log;
    }

    public bool Ready { get; private set; }
    public byte? XorKey { get; private set; }

    public bool TryGetOpcode(string name, out byte opcode)
        => _opcodes.TryGetValue(name, out opcode);

    public void Reset()
    {
        _sizeCounts.Clear();
        _rangeHits.Clear();
        _opcodes.Clear();
        XorKey = null;
        Ready = false;
    }

    public void Feed(byte wireOpcode, int payloadLen)
    {
        if (Ready)
        {
            return;
        }

        if (!_sizeCounts.TryGetValue(wireOpcode, out var list))
        {
            list = [];
            _sizeCounts[wireOpcode] = list;
        }

        list.Add(payloadLen);

        if (payloadLen is >= NpcInfoPayloadMin and <= NpcInfoPayloadMax)
        {
            _rangeHits.TryGetValue(wireOpcode, out var hits);
            _rangeHits[wireOpcode] = hits + 1;
        }

        TryDetect();
    }

    private void TryDetect()
    {
        foreach (var (wireOpcode, sizes) in _sizeCounts)
        {
            var count187 = sizes.Count(x => x == NpcInfoPayloadSize);
            if (count187 >= MinNpcInfoCountStrict)
            {
                Finalize(wireOpcode, "strict-187");
                return;
            }
        }

        foreach (var (wireOpcode, rangeCount) in _rangeHits)
        {
            if (rangeCount < MinNpcInfoCountRange)
            {
                continue;
            }

            var key = (byte)(wireOpcode ^ BaseOpcodes["NpcInfo"]);
            if (!LooksPlausibleKey(key, out var scoreDetails))
            {
                continue;
            }

            Finalize(wireOpcode, $"range-heuristic {scoreDetails}");
            return;
        }
    }

    private bool LooksPlausibleKey(byte key, out string details)
    {
        var moveOpcode = (byte)(BaseOpcodes["MoveToPoint"] ^ key);
        var userInfoOpcode = (byte)(BaseOpcodes["UserInfo"] ^ key);
        var statusOpcode = (byte)(BaseOpcodes["StatusUpdate"] ^ key);
        var status2Opcode = (byte)(BaseOpcodes["StatusUpdate2"] ^ key);

        var moveHit = CountSizeRange(moveOpcode, 24, 28);
        var userInfoHit = CountSizeRange(userInfoOpcode, 100, int.MaxValue);
        var statusHit = CountSizeRange(statusOpcode, 8, 96) + CountSizeRange(status2Opcode, 8, 96);

        details = $"Move={moveHit} UserInfo={userInfoHit} Status={statusHit}";
        return moveHit >= 1 && (userInfoHit >= 1 || statusHit >= 2);
    }

    private int CountSizeRange(byte opcode, int min, int max)
    {
        if (!_sizeCounts.TryGetValue(opcode, out var sizes))
        {
            return 0;
        }

        return sizes.Count(x => x >= min && x <= max);
    }

    private void Finalize(byte npcInfoWireOpcode, string reason)
    {
        var key = (byte)(npcInfoWireOpcode ^ BaseOpcodes["NpcInfo"]);
        XorKey = key;

        _opcodes = BaseOpcodes.ToDictionary(kv => kv.Key, kv => (byte)(kv.Value ^ key));
        Ready = true;

        var moveOpcode = _opcodes["MoveToPoint"];
        var move24 = _sizeCounts.TryGetValue(moveOpcode, out var sz)
            ? sz.Count(x => x is >= 24 and <= 28)
            : 0;

        _log.Info(
            $"OpcodeDetector ready ({reason}): key=0x{key:X2} NpcInfo=0x{_opcodes["NpcInfo"]:X2} Move=0x{moveOpcode:X2} UserInfo=0x{_opcodes["UserInfo"]:X2} Status=0x{_opcodes["StatusUpdate"]:X2} Status2=0x{_opcodes["StatusUpdate2"]:X2} Move24-28={move24}");
    }
}
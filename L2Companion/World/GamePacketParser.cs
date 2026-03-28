using System.Text;
using L2Companion.Core;
using L2Companion.Protocol;

namespace L2Companion.World;

public sealed class GamePacketParser
{
    private readonly GameWorldState _world;
    private readonly LogService _log;
    private readonly TeonOpcodeDetector _detector;
    private readonly List<(byte opcode, byte[] payload)> _preDetectPackets = [];

    private DateTime _lastSelfStatusLogAt = DateTime.MinValue;

    private const int MaxPreDetectPackets = 1200;
    private const int ItemListEntryStride = 36;
    private const int InventoryUpdateRowTail = 22;
    private const int AdenaItemId = 57;
    private const double KillCreditWindowSec = 18.0;
    private const int SysMsgSpoilSuccess = 612;
    private const int SysMsgAlreadySpoiled = 357;
    private static readonly HashSet<int> SpoilAbnormalSkillIds = [254, 302];

    public GamePacketParser(GameWorldState world, LogService log)
    {
        _world = world;
        _log = log;
        _detector = new TeonOpcodeDetector(log);
    }

    public bool ParseServerPacket(byte wireOpcode, ReadOnlySpan<byte> payload)
    {
        if (!_detector.Ready && _preDetectPackets.Count < MaxPreDetectPackets)
        {
            _preDetectPackets.Add((wireOpcode, payload.ToArray()));
        }

        var wasReady = _detector.Ready;
        _detector.Feed(wireOpcode, payload.Length);

        if (!wasReady && _detector.Ready)
        {
            _world.SessionOpcodeXorKey = _detector.XorKey;
            ReplayPreDetectedPackets();
            return true;
        }

        if (_detector.Ready)
        {
            return ParseBySessionOpcode(wireOpcode, payload);
        }

        if (TryHeuristicParseBeforeDetect(payload))
        {
            return true;
        }

        return ParseByBaseFallback(wireOpcode, payload);
    }

    private void ReplayPreDetectedPackets()
    {
        var packets = _preDetectPackets.ToArray();
        _preDetectPackets.Clear();

        var replayed = 0;
        foreach (var (wireOpcode, payload) in packets)
        {
            ParseBySessionOpcode(wireOpcode, payload);
            replayed++;
        }

        _log.Info($"Replayed pre-detect packets: {replayed}");
    }

    private bool Is(string name, byte wireOpcode)
        => _detector.TryGetOpcode(name, out var code) && code == wireOpcode;

    private bool ParseBySessionOpcode(byte wireOpcode, ReadOnlySpan<byte> payload)
    {
        if (Is("UserInfo", wireOpcode))
        {
            ParseUserInfo(payload);
            return true;
        }

        if (Is("NpcInfo", wireOpcode))
        {
            ParseNpcInfo(payload);
            return true;
        }

        if (Is("StatusUpdate", wireOpcode) || Is("StatusUpdate2", wireOpcode))
        {
            ParseStatusUpdate(payload);
            return true;
        }

        if (Is("MoveToPoint", wireOpcode))
        {
            ParseMoveToPoint(payload);
            return true;
        }

        if (Is("SystemMessage", wireOpcode) || wireOpcode is 0x22 or 0x62 or 0x64)
        {
            ParseSystemMessage(payload);
            return true;
        }

        if (Is("SkillCoolTime", wireOpcode))
        {
            ParseSkillCoolTime(payload);
            return true;
        }

        if (Is("MagicSkillLaunched", wireOpcode))
        {
            ParseMagicSkillLaunched(payload);
            return true;
        }

        if (Is("SpawnItem", wireOpcode))
        {
            ParseSpawnItem(payload);
            return true;
        }

        if (Is("DeleteObject", wireOpcode))
        {
            ParseDeleteObject(payload);
            return true;
        }

        if (Is("Die", wireOpcode) || Is("Die2", wireOpcode))
        {
            ParseDie(payload);
            return true;
        }

        if (Is("SkillList", wireOpcode))
        {
            ParseSkillList(payload);
            return true;
        }

        if (Is("TargetSelected", wireOpcode))
        {
            ParseTargetSelected(payload);
            return true;
        }

        if (Is("Attack", wireOpcode))
        {
            ParseAttack(payload);
            return true;
        }

        if (Is("ItemList", wireOpcode))
        {
            ParseItemList(payload);
            return true;
        }

        if (Is("InventoryUpdate", wireOpcode))
        {
            ParseInventoryUpdate(payload);
            return true;
        }

        if (Is("ChangeWaitType", wireOpcode))
        {
            ParseChangeWaitType(payload);
            return true;
        }

        if (Is("PartySmallWindowAll", wireOpcode))
        {
            ParsePartyAll(payload);
            return true;
        }

        if (Is("PartySmallWindowAdd", wireOpcode))
        {
            ParsePartyAdd(payload);
            return true;
        }

        if (Is("PartySmallWindowDelete", wireOpcode))
        {
            ParsePartyDelete(payload);
            return true;
        }

        if (Is("PartySmallWindowUpdate", wireOpcode))
        {
            ParsePartyUpdate(payload);
            return true;
        }

        if (Is("PartySpelled", wireOpcode))
        {
            ParsePartySpelled(payload);
            return true;
        }

        if (Is("ShortBuffStatusUpdate", wireOpcode))
        {
            ParseShortBuffStatusUpdate(payload);
            return true;
        }

        if (Is("AbnormalStatusUpdate", wireOpcode))
        {
            ParseAbnormal(payload);
            return true;
        }

        if (_world.Me.ObjectId == 0 && LooksLikeUserInfo(payload))
        {
            _log.Info($"Heuristic UserInfo parse on wire opcode 0x{wireOpcode:X2}");
            ParseUserInfo(payload);
            return true;
        }

        return false;
    }

    private bool ParseByBaseFallback(byte wireOpcode, ReadOnlySpan<byte> payload)
    {
        switch (wireOpcode)
        {
            case 0x04:
                ParseUserInfo(payload);
                return true;
            case 0x16:
            case 0x6C:
                ParseNpcInfo(payload);
                return true;
            case 0x0E:
            case 0x17:
            case 0x6D:
                ParseStatusUpdate(payload);
                return true;
            case 0x01:
            case 0x7B:
                ParseMoveToPoint(payload);
                return true;
            case 0x22:
            case 0x62:
            case 0x64:
                ParseSystemMessage(payload);
                return true;
            case 0x6A:
                ParseSkillCoolTime(payload);
                return true;
            case 0x48:
                ParseMagicSkillLaunched(payload);
                return true;
            case 0x7F:
                ParseAbnormal(payload);
                return true;
            case 0x0C:
                ParseSpawnItem(payload);
                return true;
            case 0x08:
            case 0x0B:
            case 0x72:
                ParseDeleteObject(payload);
                return true;
            case 0x06:
            case 0x12:
            case 0x68:
                ParseDie(payload);
                return true;
            case 0x58:
                ParseSkillList(payload);
                return true;
            case 0x24:
            case 0x47:
                ParseTargetSelected(payload);
                return true;
            case 0x60:
                ParseAttack(payload);
                return true;
            case 0x1B:
                ParseItemList(payload);
                return true;
            case 0x21:
                ParseInventoryUpdate(payload);
                return true;
            case 0x25:
                ParseChangeWaitType(payload);
                return true;
            case 0xEE:
                ParsePartySpelled(payload);
                return true;
            case 0x91:
                ParseShortBuffStatusUpdate(payload);
                return true;
            default:
                if (_world.Me.ObjectId == 0 && LooksLikeUserInfo(payload))
                {
                    _log.Info($"Fallback heuristic UserInfo parse on wire opcode 0x{wireOpcode:X2}");
                    ParseUserInfo(payload);
                    return true;
                }

                return false;
        }
    }

    private bool TryHeuristicParseBeforeDetect(ReadOnlySpan<byte> payload)
    {
        if (LooksLikeNpcInfo(payload))
        {
            ParseNpcInfo(payload);
            return true;
        }

        if (LooksLikeStatusUpdate(payload))
        {
            ParseStatusUpdate(payload);
            return true;
        }

        if (_world.Me.ObjectId == 0 && LooksLikeUserInfo(payload))
        {
            ParseUserInfo(payload);
            return true;
        }

        return false;
    }
    private static bool LooksLikeNpcInfo(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < TeonOpcodeDetector.NpcInfoPayloadMin || payload.Length > TeonOpcodeDetector.NpcInfoPayloadMax + 20)
        {
            return false;
        }

        try
        {
            var objectId = BitConverter.ToInt32(payload.Slice(0, 4));
            var npcTypeId = BitConverter.ToInt32(payload.Slice(4, 4));
            var x = BitConverter.ToInt32(payload.Slice(12, 4));
            var y = BitConverter.ToInt32(payload.Slice(16, 4));
            var z = BitConverter.ToInt32(payload.Slice(20, 4));

            return objectId != 0
                && npcTypeId is > 1_000_000 and < 3_000_000
                && Math.Abs(x) < 3_000_000
                && Math.Abs(y) < 3_000_000
                && Math.Abs(z) < 700_000;
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeStatusUpdate(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return false;
        }

        try
        {
            var count = BitConverter.ToInt32(payload.Slice(4, 4));
            if (count is > 0 and <= 10 && payload.Length >= 8 + count * 8)
            {
                var attrId = BitConverter.ToInt32(payload.Slice(8, 4));
                return attrId is >= 0x01 and <= 0x22;
            }

            if (count is >= 0x01 and <= 0x22 && payload.Length >= 12)
            {
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
    private static bool LooksLikeUserInfo(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 100)
        {
            return false;
        }

        var objectId = BitConverter.ToInt32(payload.Slice(16, 4));
        if (objectId == 0)
        {
            return false;
        }

        var x = BitConverter.ToInt32(payload.Slice(0, 4));
        var y = BitConverter.ToInt32(payload.Slice(4, 4));
        var z = BitConverter.ToInt32(payload.Slice(8, 4));
        if (Math.Abs(x) > 2_000_000 || Math.Abs(y) > 2_000_000 || Math.Abs(z) > 500_000)
        {
            return false;
        }

        var strStart = 20;
        var strEnd = -1;
        var maxScan = Math.Min(payload.Length - 2, strStart + 80);
        for (var i = strStart; i + 1 < maxScan; i += 2)
        {
            if (payload[i] == 0 && payload[i + 1] == 0)
            {
                strEnd = i;
                break;
            }
        }

        if (strEnd <= strStart)
        {
            return false;
        }

        try
        {
            var name = Encoding.Unicode.GetString(payload.Slice(strStart, strEnd - strStart));
            return !string.IsNullOrWhiteSpace(name) && name.Length <= 24;
        }
        catch
        {
            return false;
        }
    }

    private void ParseUserInfo(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 60)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);

            var x = r.ReadInt32();
            var y = r.ReadInt32();
            var z = r.ReadInt32();
            var heading = r.ReadInt32();
            var objectId = r.ReadInt32();
            var name = r.ReadUnicodeString();

            if (objectId == 0 || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (r.Remaining < (4 * 4 + 8 + 6 * 4 + 5 * 4))
            {
                return;
            }

            var race = r.ReadInt32();
            var sex = r.ReadInt32();
            var classId = r.ReadInt32();
            var level = r.ReadInt32();
            _ = r.ReadInt64();

            _ = r.ReadInt32();
            _ = r.ReadInt32();
            _ = r.ReadInt32();
            _ = r.ReadInt32();
            _ = r.ReadInt32();
            _ = r.ReadInt32();

            var maxHp = r.ReadInt32();
            var curHp = r.ReadInt32();
            var maxMp = r.ReadInt32();
            var curMp = r.ReadInt32();
            _ = r.ReadInt32();

            _world.WithLock(() =>
            {
                var identityChanged = _world.Me.ObjectId != 0
                    && (_world.Me.ObjectId != objectId || !string.Equals(_world.Me.Name, name, StringComparison.Ordinal));

                if (identityChanged)
                {
                    _world.SessionStats.Reset();
                    _world.InventoryByItemId.Clear();
                    _world.Items.Clear();
                    _world.Npcs.Clear();
                    _world.Party.Clear();
                    _world.SkillCooldownReadyAtUtc.Clear();
                    _world.Me.AbnormalEffectSkillIds.Clear();
                    _world.Me.AbnormalUpdatedAtUtc = DateTime.MinValue;
                }

                _world.Me.X = x;
                _world.Me.Y = y;
                _world.Me.Z = z;
                _world.Me.Heading = heading;
                _world.Me.ObjectId = objectId;
                _world.Me.Name = name;
                _world.Me.ClassId = classId;
                _world.Me.Level = level;
                _world.Me.CurHp = curHp;
                _world.Me.MaxHp = maxHp;
                _world.Me.CurMp = curMp;
                _world.Me.MaxMp = maxMp;
                if (maxHp > 0) _world.Me.MaxHpBaseline = maxHp;
                if (maxMp > 0) _world.Me.MaxMpBaseline = maxMp;
                _world.EnteredWorld = true;
            });

            _world.MarkMutation(WorldMutationType.UserInfo);
            _log.Info($"UserInfo parsed: {name} oid=0x{objectId:X} class={classId} lvl={level} HP={curHp}/{maxHp} MP={curMp}/{maxMp} race={race} sex={sex}");
        }
        catch
        {
            // Ignore malformed payloads from unknown shards.
        }
    }

    private static bool AcceptMaxPool(int cur, int candidate, int baseline)
    {
        if (candidate <= 0)
        {
            return false;
        }

        if (candidate < cur)
        {
            return false;
        }

        if (candidate > 2_000_000)
        {
            return false;
        }

        if (baseline > 0 && candidate < Math.Max(1, (int)(baseline * 0.10f)))
        {
            return false;
        }

        if (baseline > 0 && candidate > (int)(baseline * 25.0f))
        {
            return false;
        }

        if (baseline > 0 && cur > 0 && candidate > Math.Max(cur * 100, baseline * 3))
        {
            return false;
        }

        return true;
    }

    private void ParseNpcInfo(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 36)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            var npc = new NpcState
            {
                ObjectId = r.ReadInt32(),
                NpcTypeId = r.ReadInt32(),
                IsAttackable = r.ReadInt32() != 0,
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Heading = r.ReadInt32()
            };

            if (r.Remaining >= (11 * 4 + 4 * 8 + 3 * 4 + 5))
            {
                r.Skip(11 * 4);
                r.Skip(4 * 8);
                r.Skip(3 * 4);
                if (r.Remaining >= 1) _ = r.ReadByte(); // nameAboveChar
                if (r.Remaining >= 1) _ = r.ReadByte(); // isRunning
                if (r.Remaining >= 1) _ = r.ReadByte(); // inCombat
                if (r.Remaining >= 1) npc.IsDead = r.ReadByte() != 0;
                if (r.Remaining >= 1) npc.IsSummoned = r.ReadByte() != 0;

                npc.Name = r.ReadUnicodeString();
                npc.Title = r.ReadUnicodeString();

                if (r.Remaining >= 28)
                {
                    r.Skip(28);
                }

                if (r.Remaining >= 18)
                {
                    r.Skip(2);
                    r.Skip(16);
                }

                if (r.Remaining >= 4)
                {
                    r.Skip(4);
                }

                if (r.Remaining >= 4)
                {
                    var hpPct = r.ReadInt32();
                    if (hpPct is >= 0 and <= 100)
                    {
                        npc.HpPct = hpPct;
                    }
                }

                if (r.Remaining >= 4)
                {
                    _ = r.ReadInt32(); // curMpPercent
                }
            }

            if (_world.Npcs.TryGetValue(npc.ObjectId, out var prev))
            {
                if (string.IsNullOrWhiteSpace(npc.Name)) npc.Name = prev.Name;
                if (string.IsNullOrWhiteSpace(npc.Title)) npc.Title = prev.Title;
                if (npc.HpPct <= 0 && prev.HpPct > 0) npc.HpPct = prev.HpPct;
                if (!npc.IsDead && prev.IsDead && prev.HpPct > 0) npc.IsDead = false;
                npc.LastAggroHitAtUtc = prev.LastAggroHitAtUtc;
                npc.LastHitByMeAtUtc = prev.LastHitByMeAtUtc;
                npc.KillCredited = prev.KillCredited;
                npc.SpoilSucceeded = prev.SpoilSucceeded;
                npc.SpoilAttempted = prev.SpoilAttempted;
                npc.SpoilAtUtc = prev.SpoilAtUtc;
                npc.SweepDone = prev.SweepDone;
                npc.SweepRetryUntilUtc = prev.SweepRetryUntilUtc;
                npc.AbnormalEffectSkillIds = prev.AbnormalEffectSkillIds;
            }

            // Some shards expose transient 0 here for alive NPCs; treat as unknown unless dead.
            if (!npc.IsDead && npc.HpPct <= 0f)
            {
                npc.HpPct = 100f;
            }

            if (string.IsNullOrWhiteSpace(npc.Name))
            {
                var npcId = npc.NpcTypeId > 1_000_000 ? npc.NpcTypeId - 1_000_000 : npc.NpcTypeId;
                npc.Name = $"NPC {npcId}";
            }

            _world.Npcs[npc.ObjectId] = npc;
            _world.MarkMutation(WorldMutationType.Npc);
        }
        catch
        {
            // ignored
        }
    }

    private void ParseMoveToPoint(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 24)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            var objectId = r.ReadInt32();
            var destX = r.ReadInt32();
            var destY = r.ReadInt32();
            var destZ = r.ReadInt32();
            var origX = r.ReadInt32();
            var origY = r.ReadInt32();
            var hasOrigZ = r.Remaining >= 4;
            var origZ = hasOrigZ ? r.ReadInt32() : destZ;

            if (objectId == 0)
            {
                return;
            }

            var meMoved = false;
            _world.WithLock(() =>
            {
                if (objectId == _world.Me.ObjectId)
                {
                    _world.Me.X = origX;
                    _world.Me.Y = origY;
                    _world.Me.Z = origZ;

                    var movedFar = Math.Abs(destX - origX) >= 12
                        || Math.Abs(destY - origY) >= 12
                        || Math.Abs(destZ - origZ) >= 12;
                    if (movedFar)
                    {
                        _world.Me.IsSitting = false;
                    }

                    meMoved = true;
                    return;
                }

                if (_world.Npcs.TryGetValue(objectId, out var npc))
                {
                    npc.X = origX;
                    npc.Y = origY;
                    npc.Z = origZ;
                    if (npc.IsDead)
                    {
                        npc.IsDead = false;
                    }

                    _world.Npcs[objectId] = npc;
                }
            });

            _world.MarkMutation(meMoved ? WorldMutationType.Target : WorldMutationType.Npc);
        }
        catch
        {
            // ignored
        }
    }
    private void ParseTargetSelected(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return;
        }

        var targetId = BitConverter.ToInt32(payload.Slice(0, 4));
        if (targetId == 0)
        {
            return;
        }

        _world.WithLock(() => _world.Me.TargetId = targetId);
        _world.MarkMutation(WorldMutationType.Target);
    }

    private void ParseAttack(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            var attackerId = r.ReadInt32();
            var targetId = r.ReadInt32();
            var now = DateTime.UtcNow;

            if (attackerId == 0 || targetId == 0)
            {
                return;
            }

            var meId = _world.Me.ObjectId;
            if (targetId == meId && _world.Npcs.TryGetValue(attackerId, out var aggroNpc))
            {
                aggroNpc.LastAggroHitAtUtc = now;
                _world.Npcs[attackerId] = aggroNpc;
                _world.MarkMutation(WorldMutationType.Npc);
            }

            if (attackerId == meId && _world.Npcs.TryGetValue(targetId, out var hitNpc))
            {
                hitNpc.LastHitByMeAtUtc = now;
                hitNpc.KillCredited = false;
                _world.Npcs[targetId] = hitNpc;
                _world.MarkMutation(WorldMutationType.Npc);
            }
        }
        catch
        {
            // ignored
        }
    }

    private void ParseSystemMessage(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return;
        }

        try
        {
            var msgId = BitConverter.ToInt32(payload.Slice(0, 4));
            if (msgId is not (SysMsgSpoilSuccess or SysMsgAlreadySpoiled))
            {
                return;
            }

            TryMarkSpoilSuccess(msgId);
        }
        catch
        {
            // ignored
        }
    }

    private void ParseSkillCoolTime(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            var count = r.ReadInt32();
            if (count <= 0 || count > 1024)
            {
                return;
            }

            var remaining = r.Remaining;
            var stride = 8;
            foreach (var candidate in new[] { 8, 12, 16, 20 })
            {
                if (remaining < count * candidate)
                {
                    continue;
                }

                if ((remaining - count * candidate) is >= 0 and < 4)
                {
                    stride = candidate;
                    break;
                }
            }

            var now = DateTime.UtcNow;
            var updates = 0;
            var intsPerRow = stride / 4;
            for (var i = 0; i < count && r.Remaining >= stride; i++)
            {
                var row = new int[intsPerRow];
                for (var j = 0; j < intsPerRow; j++)
                {
                    row[j] = r.ReadInt32();
                }

                var skillId = row[0];
                if (skillId <= 0 || skillId > 200000)
                {
                    continue;
                }

                var cooldownMs = row.Skip(1)
                    .Where(x => x > 0 && x <= 86_400_000)
                    .DefaultIfEmpty(0)
                    .Last();

                if (cooldownMs <= 0)
                {
                    continue;
                }

                var readyAt = now.AddMilliseconds(Math.Max(250, cooldownMs));
                _world.SkillCooldownReadyAtUtc[skillId] = readyAt;
                updates++;
            }

            PruneExpiredSkillCooldowns(now);

            if (updates > 0)
            {
                _world.MarkMutation(WorldMutationType.Status);
            }
        }
        catch
        {
            // ignored
        }
    }

    private void ParseMagicSkillLaunched(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            var casterId = r.ReadInt32();
            if (casterId == 0)
            {
                return;
            }

            var ints = new List<int>();
            while (r.Remaining >= 4)
            {
                ints.Add(r.ReadInt32());
            }

            var now = DateTime.UtcNow;
            var meId = _world.Me.ObjectId;
            if (casterId == meId)
            {
                var targetId = _world.Me.TargetId;
                if (targetId != 0 && _world.Npcs.TryGetValue(targetId, out var targetNpc))
                {
                    targetNpc.LastHitByMeAtUtc = now;
                    targetNpc.KillCredited = false;
                    _world.Npcs[targetId] = targetNpc;
                    _world.MarkMutation(WorldMutationType.Npc);
                }

                return;
            }

            if (ints.Contains(meId) && _world.Npcs.TryGetValue(casterId, out var aggroNpc))
            {
                aggroNpc.LastAggroHitAtUtc = now;
                _world.Npcs[casterId] = aggroNpc;
                _world.MarkMutation(WorldMutationType.Npc);
            }
        }
        catch
        {
            // ignored
        }
    }

    private void ParsePartySpelled(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return;
        }

        try
        {
            if (!TryParseAbnormal(payload, out var objectId, out var effects, out var explicitEmpty))
            {
                return;
            }

            ApplyAbnormalEffects(objectId, effects, explicitEmpty);
        }
        catch
        {
            // ignored
        }
    }

    private void ParseShortBuffStatusUpdate(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            var first = r.ReadInt32();
            var objectId = 0;
            var skillId = 0;

            if (first == _world.Me.ObjectId && first != 0 && r.Remaining >= 4)
            {
                objectId = first;
                skillId = r.ReadInt32();
            }
            else
            {
                objectId = _world.Me.ObjectId;
                skillId = first;
            }

            if (skillId <= 0 || skillId > 200000)
            {
                return;
            }

            var duration = r.Remaining >= 4 ? r.ReadInt32() : 1;
            var now = DateTime.UtcNow;

            _world.WithLock(() =>
            {
                if (objectId == _world.Me.ObjectId && objectId != 0)
                {
                    if (duration <= 0)
                    {
                        _world.Me.AbnormalEffectSkillIds.Remove(skillId);
                    }
                    else
                    {
                        _world.Me.AbnormalEffectSkillIds.Add(skillId);
                    }

                    _world.Me.AbnormalUpdatedAtUtc = now;
                }
            });

            _world.MarkMutation(WorldMutationType.Status);
        }
        catch
        {
            // ignored
        }
    }

    private void PruneExpiredSkillCooldowns(DateTime now)
    {
        foreach (var kv in _world.SkillCooldownReadyAtUtc.Where(x => x.Value <= now.AddMilliseconds(-600)).ToArray())
        {
            _world.SkillCooldownReadyAtUtc.TryRemove(kv.Key, out _);
        }
    }

    private void TryMarkSpoilSuccess(int msgId)
    {
        var now = DateTime.UtcNow;
        var targetId = _world.Me.TargetId;
        if (targetId != 0 && _world.Npcs.TryGetValue(targetId, out var targetNpc))
        {
            var targetRecentlySpoiled = targetNpc.SpoilAttempted
                && targetNpc.SpoilAtUtc != DateTime.MinValue
                && (now - targetNpc.SpoilAtUtc).TotalSeconds <= 8;
            var targetHasSpoilAbnormal = targetNpc.AbnormalEffectSkillIds.Count > 0
                && targetNpc.AbnormalEffectSkillIds.Overlaps(SpoilAbnormalSkillIds);

            if (targetRecentlySpoiled || targetHasSpoilAbnormal)
            {
                targetNpc.SpoilSucceeded = true;
                targetNpc.SpoilAttempted = true;
                targetNpc.SpoilAtUtc = now;
                _world.Npcs[targetId] = targetNpc;
                _world.MarkMutation(WorldMutationType.Npc);
                _log.Info($"[Spoil] success via SystemMessage id={msgId} target=0x{targetId:X}");
                return;
            }
        }

        var me = _world.Me;
        var candidate = _world.Npcs.Values
            .Where(x => x.SpoilAttempted && !x.SpoilSucceeded)
            .Where(x => (now - x.SpoilAtUtc).TotalSeconds <= 8)
            .OrderBy(x => x.ObjectId == me.TargetId ? 0 : 1)
            .ThenBy(x => x.LastHitByMeAtUtc != DateTime.MinValue && (now - x.LastHitByMeAtUtc).TotalSeconds <= 6 ? 0 : 1)
            .ThenBy(x => DistanceSq(me.X, me.Y, x.X, x.Y))
            .ThenByDescending(x => x.SpoilAtUtc)
            .FirstOrDefault();

        if (candidate is null)
        {
            return;
        }

        candidate.SpoilSucceeded = true;
        candidate.SpoilAtUtc = now;
        _world.Npcs[candidate.ObjectId] = candidate;
        _world.MarkMutation(WorldMutationType.Npc);
        _log.Info($"[Spoil] success via SystemMessage id={msgId} fallback=0x{candidate.ObjectId:X}");
    }
    private void ParseStatusUpdate(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            var objectId = r.ReadInt32();
            var count = r.ReadInt32();

            var attrs = new Dictionary<int, int>();
            if (count < 0 || count > 10 || r.Remaining < count * 8)
            {
                var attrId = count;
                if (r.Remaining >= 4)
                {
                    attrs[attrId] = r.ReadInt32();
                }

                while (r.Remaining >= 8)
                {
                    attrs[r.ReadInt32()] = r.ReadInt32();
                }
            }
            else
            {
                for (var i = 0; i < count && r.Remaining >= 8; i++)
                {
                    attrs[r.ReadInt32()] = r.ReadInt32();
                }
            }

            _world.WithLock(() =>
            {
                if (_world.Me.ObjectId == 0 && (attrs.ContainsKey(0x09) || attrs.ContainsKey(0x0B)))
                {
                    _world.Me.ObjectId = objectId;
                    _log.Info($"Self object detected from StatusUpdate: oid=0x{objectId:X}");
                }

                if (objectId == _world.Me.ObjectId && objectId != 0)
                {
                    if (attrs.TryGetValue(0x09, out var curHp)) _world.Me.CurHp = curHp;
                    if (attrs.TryGetValue(0x0B, out var curMp)) _world.Me.CurMp = curMp;
                    if (attrs.TryGetValue(0x21, out var curCp)) _world.Me.CurCp = curCp;

                    if (attrs.TryGetValue(0x0A, out var maxHp) && AcceptMaxPool(_world.Me.CurHp, maxHp, _world.Me.MaxHpBaseline))
                    {
                        _world.Me.MaxHp = maxHp;
                    }

                    if (attrs.TryGetValue(0x0C, out var maxMp) && AcceptMaxPool(_world.Me.CurMp, maxMp, _world.Me.MaxMpBaseline))
                    {
                        _world.Me.MaxMp = maxMp;
                    }

                    if (attrs.TryGetValue(0x22, out var maxCp) && maxCp > 0)
                    {
                        _world.Me.MaxCp = maxCp;
                    }

                    _world.EnteredWorld = true;
                    _world.MarkMutation(WorldMutationType.Status);

                    if ((DateTime.UtcNow - _lastSelfStatusLogAt).TotalSeconds >= 3)
                    {
                        _lastSelfStatusLogAt = DateTime.UtcNow;
                        _log.Info($"Status self: HP={_world.Me.CurHp}/{_world.Me.MaxHp} MP={_world.Me.CurMp}/{_world.Me.MaxMp} attrs={string.Join(',', attrs.Keys.Select(k => $"0x{k:X}"))}");
                    }

                    return;
                }

                if (_world.Npcs.TryGetValue(objectId, out var npc) && attrs.TryGetValue(0x09, out var npcCurHp) && attrs.TryGetValue(0x0A, out var npcMaxHp) && npcMaxHp > 0)
                {
                    npc.HpPct = npcCurHp * 100f / npcMaxHp;
                    _world.Npcs[objectId] = npc;
                    _world.MarkMutation(WorldMutationType.Status);
                    return;
                }

                if (_world.Party.TryGetValue(objectId, out var member))
                {
                    if (attrs.TryGetValue(0x09, out var curHpPm)) member.CurHp = curHpPm;
                    if (attrs.TryGetValue(0x0A, out var maxHpPm) && maxHpPm > 0) member.MaxHp = maxHpPm;
                    if (attrs.TryGetValue(0x0B, out var curMpPm)) member.CurMp = curMpPm;
                    if (attrs.TryGetValue(0x0C, out var maxMpPm) && maxMpPm > 0) member.MaxMp = maxMpPm;
                    if (attrs.TryGetValue(0x21, out var curCpPm)) member.CurCp = curCpPm;
                    if (attrs.TryGetValue(0x22, out var maxCpPm) && maxCpPm > 0) member.MaxCp = maxCpPm;
                    _world.Party[objectId] = member;
                    _world.MarkMutation(WorldMutationType.Status);
                }
            });
        }
        catch
        {
            // ignored
        }
    }

    private void ParseSpawnItem(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 24)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            _ = r.ReadInt32();
            var objectId = r.ReadInt32();
            var itemId = r.ReadInt32();

            var item = new GroundItemState
            {
                ObjectId = objectId,
                ItemId = itemId,
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Count = 1
            };

            if (r.Remaining >= 4)
            {
                _ = r.ReadInt32();
            }

            if (r.Remaining >= 4)
            {
                item.Count = Math.Max(1, r.ReadInt32());
            }
            else if (r.Remaining >= 8)
            {
                item.Count = Math.Max(1, r.ReadInt64());
            }

            _world.Items[item.ObjectId] = item;
            _world.MarkMutation(WorldMutationType.Loot);
        }
        catch
        {
            // ignored
        }
    }

    private void ParseDeleteObject(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return;
        }

        var objectId = BitConverter.ToInt32(payload.Slice(0, 4));
        _world.Npcs.TryRemove(objectId, out _);
        _world.Items.TryRemove(objectId, out _);
        _world.Party.TryRemove(objectId, out _);
        if (_world.Me.TargetId == objectId) _world.Me.TargetId = 0;
        _world.MarkMutation(WorldMutationType.Target);
    }

    private void ParseDie(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return;
        }

        var objectId = BitConverter.ToInt32(payload.Slice(0, 4));
        if (!_world.Npcs.TryGetValue(objectId, out var npc))
        {
            return;
        }

        npc.IsDead = true;
        npc.HpPct = 0;

        if (!npc.KillCredited && npc.LastHitByMeAtUtc != DateTime.MinValue && (DateTime.UtcNow - npc.LastHitByMeAtUtc).TotalSeconds <= KillCreditWindowSec)
        {
            npc.KillCredited = true;
            _world.WithLock(() => _world.SessionStats.AddKill());
        }

        if (!npc.SpoilSucceeded && npc.SpoilAttempted && npc.SpoilAtUtc != DateTime.MinValue && (DateTime.UtcNow - npc.SpoilAtUtc).TotalSeconds <= 10)
        {
            npc.SpoilSucceeded = true;
            _log.Info($"[Spoil] success via recent-attempt fallback target=0x{objectId:X}");
        }

        _world.Npcs[objectId] = npc;
        _world.MarkMutation(WorldMutationType.Npc);
    }

    private void ParseSkillList(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            var count = r.ReadInt32();
            if (count < 0 || count > 5000)
            {
                return;
            }

            _world.Skills.Clear();
            var parsed = 0;
            for (var i = 0; i < count && r.Remaining >= 12; i++)
            {
                _ = r.ReadInt32();
                var level = r.ReadInt32();
                var skillId = r.ReadInt32();
                if (r.Remaining >= 1)
                {
                    _ = r.ReadByte();
                }

                _world.Skills[skillId] = level;
                parsed++;
            }

            _world.MarkMutation(WorldMutationType.Generic);
            if (parsed < count)
            {
                _log.Info($"SkillList truncated: header={count} parsed={parsed} payload={payload.Length}B rem={r.Remaining}B");
            }
        }
        catch
        {
            // ignored
        }
    }

    private void ParseItemList(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            _ = r.ReadInt16();
            var count = (ushort)r.ReadInt16();
            if (count > 5000)
            {
                return;
            }

            var totals = new Dictionary<int, long>();
            for (var i = 0; i < count; i++)
            {
                if (r.Remaining < ItemListEntryStride)
                {
                    break;
                }

                _ = r.ReadInt16();
                _ = r.ReadInt32();
                var itemId = r.ReadInt32();
                var itemCount = Math.Max(0, r.ReadInt32());
                r.Skip(ItemListEntryStride - 14);

                totals[itemId] = totals.GetValueOrDefault(itemId) + itemCount;
            }

            _world.WithLock(() =>
            {
                _world.InventoryByItemId.Clear();
                foreach (var kv in totals)
                {
                    _world.InventoryByItemId[kv.Key] = kv.Value;
                }

                _world.SessionStats.SetAdenaSnapshot(totals.GetValueOrDefault(AdenaItemId));
            });

            _world.MarkMutation(WorldMutationType.Generic);
        }
        catch
        {
            // ignored
        }
    }

    private void ParseInventoryUpdate(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            return;
        }

        try
        {
            if (payload.Length >= 4)
            {
                var show = BitConverter.ToUInt16(payload.Slice(0, 2));
                var ilCount = BitConverter.ToUInt16(payload.Slice(2, 2));
                var need = 4 + ilCount * ItemListEntryStride;
                if (show is 0 or 1 && ilCount <= 5000 && payload.Length >= need)
                {
                    ParseItemList(payload);
                    return;
                }
            }

            var r = new PacketReader(payload);
            var count = (ushort)r.ReadInt16();
            if (count == 0 || count > 500)
            {
                return;
            }

            long pickedItems = 0;
            for (var i = 0; i < count; i++)
            {
                if (r.Remaining < 2)
                {
                    break;
                }

                var mod = (ushort)r.ReadInt16();
                if (mod == 3)
                {
                    if (r.Remaining >= 4)
                    {
                        _ = r.ReadInt32();
                    }

                    continue;
                }

                if (r.Remaining < ItemListEntryStride)
                {
                    break;
                }

                _ = r.ReadInt16();
                _ = r.ReadInt32();
                var itemId = r.ReadInt32();
                var itemCount = Math.Max(0, r.ReadInt32());
                r.Skip(InventoryUpdateRowTail);

                _world.WithLock(() =>
                {
                    var previous = _world.InventoryByItemId.GetValueOrDefault(itemId);
                    _world.InventoryByItemId[itemId] = itemCount;

                    if (itemId == AdenaItemId)
                    {
                        _world.SessionStats.ApplyAdenaUpdate(itemCount);
                        return;
                    }

                    if (itemCount > previous)
                    {
                        pickedItems += itemCount - previous;
                    }
                });
            }

            if (pickedItems > 0)
            {
                _world.WithLock(() => _world.SessionStats.AddLootPickups((int)Math.Min(int.MaxValue, pickedItems)));
            }

            _world.MarkMutation(WorldMutationType.Generic);
        }
        catch
        {
            // ignored
        }
    }

    private void ParseChangeWaitType(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8)
        {
            return;
        }

        try
        {
            var objectId = BitConverter.ToInt32(payload.Slice(0, 4));
            var waitType = BitConverter.ToInt32(payload.Slice(4, 4));

            _world.WithLock(() =>
            {
                if (objectId != _world.Me.ObjectId || objectId == 0)
                {
                    return;
                }

                var sitRaw = _world.ChangeWaitTypeSitRaw == 1 ? 1 : 0;
                var standRaw = sitRaw == 0 ? 1 : 0;

                var isSitting = waitType switch
                {
                    var v when v == sitRaw => true,
                    var v when v == standRaw => false,
                    >= 7 => true,
                    _ => sitRaw == 0 && waitType == 0
                };

                _world.Me.IsSitting = isSitting;
            });

            _world.MarkMutation(WorldMutationType.Status);
        }
        catch
        {
            // ignored
        }
    }

    private void ParsePartyAll(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            var count = r.ReadInt32();
            if (count < 0 || count > 16)
            {
                return;
            }

            _world.Party.Clear();
            for (var i = 0; i < count; i++)
            {
                if (!TryParsePartyMemberRow(r, out var member) || member.ObjectId == 0)
                {
                    break;
                }

                _world.Party[member.ObjectId] = member;
            }

            _world.MarkMutation(WorldMutationType.Status);
        }
        catch
        {
            // ignored
        }
    }

    private void ParsePartyAdd(ReadOnlySpan<byte> payload)
    {
        try
        {
            var r = new PacketReader(payload);
            if (!TryParsePartyMemberRow(r, out var member) || member.ObjectId == 0)
            {
                return;
            }

            _world.Party[member.ObjectId] = member;
            _world.MarkMutation(WorldMutationType.Status);
        }
        catch
        {
            // ignored
        }
    }

    private void ParsePartyDelete(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return;
        }

        var objectId = BitConverter.ToInt32(payload.Slice(0, 4));
        _world.Party.TryRemove(objectId, out _);
        if (_world.Me.TargetId == objectId) _world.Me.TargetId = 0;
        _world.MarkMutation(WorldMutationType.Status);
    }

    private void ParsePartyUpdate(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 28)
        {
            return;
        }

        try
        {
            var r = new PacketReader(payload);
            var objectId = r.ReadInt32();
            if (!_world.Party.TryGetValue(objectId, out var member))
            {
                member = new PartyMemberState { ObjectId = objectId, Name = "PartyMember" };
            }

            member.CurHp = r.ReadInt32();
            member.MaxHp = r.ReadInt32();
            member.CurMp = r.ReadInt32();
            member.MaxMp = r.ReadInt32();
            member.CurCp = r.ReadInt32();
            member.MaxCp = r.ReadInt32();
            _world.Party[objectId] = member;
            _world.MarkMutation(WorldMutationType.Status);
        }
        catch
        {
            // ignored
        }
    }

    private static bool TryParsePartyMemberRow(PacketReader r, out PartyMemberState member)
    {
        member = new PartyMemberState();

        if (r.Remaining < 4)
        {
            return false;
        }

        var name = r.ReadUnicodeString();
        if (r.Remaining < (7 * 4))
        {
            return false;
        }

        member.Name = string.IsNullOrWhiteSpace(name) ? "PartyMember" : name;
        member.ObjectId = r.ReadInt32();
        member.ClassId = r.ReadInt32();
        member.CurHp = r.ReadInt32();
        member.MaxHp = r.ReadInt32();
        member.CurMp = r.ReadInt32();
        member.MaxMp = r.ReadInt32();
        member.CurCp = r.ReadInt32();

        if (r.Remaining >= 8)
        {
            member.MaxCp = r.ReadInt32();
            member.Level = r.ReadInt32();
            return true;
        }

        if (r.Remaining >= 4)
        {
            member.Level = r.ReadInt32();
            return true;
        }

        return false;
    }

    private void ParseAbnormal(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            return;
        }

        try
        {
            if (!TryParseAbnormal(payload, out var objectId, out var effects, out var explicitEmpty))
            {
                return;
            }

            ApplyAbnormalEffects(objectId, effects, explicitEmpty);
        }
        catch
        {
            // ignored
        }
    }

    private void ApplyAbnormalEffects(int objectId, HashSet<int> effects, bool explicitEmpty)
    {
        var now = DateTime.UtcNow;
        var updatedMe = false;
        var updatedNpc = false;

        _world.WithLock(() =>
        {
            if (objectId == _world.Me.ObjectId && objectId != 0)
            {
                if (explicitEmpty)
                {
                    _world.Me.AbnormalEffectSkillIds.Clear();
                }
                else if (effects.Count > 0)
                {
                    _world.Me.AbnormalEffectSkillIds = effects;
                }

                _world.Me.AbnormalUpdatedAtUtc = now;
                updatedMe = true;
                return;
            }

            var resolvedObjectId = objectId == 0 ? _world.Me.TargetId : objectId;
            if (resolvedObjectId == 0 || !_world.Npcs.TryGetValue(resolvedObjectId, out var npc))
            {
                return;
            }

            if (explicitEmpty)
            {
                npc.AbnormalEffectSkillIds.Clear();
            }
            else if (effects.Count > 0)
            {
                npc.AbnormalEffectSkillIds = effects;
            }

            if (npc.AbnormalEffectSkillIds.Count > 0 && npc.SpoilAttempted && npc.AbnormalEffectSkillIds.Overlaps(SpoilAbnormalSkillIds))
            {
                npc.SpoilSucceeded = true;
                npc.SpoilAtUtc = now;
            }

            _world.Npcs[resolvedObjectId] = npc;
            updatedNpc = true;
        });

        if (updatedNpc)
        {
            _world.MarkMutation(WorldMutationType.Npc);
            return;
        }

        if (updatedMe)
        {
            _world.MarkMutation(WorldMutationType.Status);
        }
    }

    private static bool TryParseAbnormal(ReadOnlySpan<byte> payload, out int objectId, out HashSet<int> effects, out bool explicitEmpty)
    {
        objectId = 0;
        explicitEmpty = false;
        effects = [];

        if (payload.Length >= 8)
        {
            var oid = BitConverter.ToInt32(payload.Slice(0, 4));
            var count = BitConverter.ToInt32(payload.Slice(4, 4));
            if (oid != 0 && count >= 0 && count <= 64)
            {
                if (count == 0)
                {
                    objectId = oid;
                    explicitEmpty = true;
                    return true;
                }

                var need = 8 + count * 10;
                if (payload.Length >= need)
                {
                    var off = 8;
                    for (var i = 0; i < count; i++)
                    {
                        var sid = BitConverter.ToInt32(payload.Slice(off, 4));
                        if (sid > 0 && sid <= 200000)
                        {
                            effects.Add(sid);
                        }

                        off += 10;
                    }

                    objectId = oid;
                    return effects.Count > 0;
                }
            }
        }

        if (payload.Length >= 6)
        {
            var oid = BitConverter.ToInt32(payload.Slice(0, 4));
            var count = BitConverter.ToInt16(payload.Slice(4, 2));
            if (oid != 0 && count >= 0 && count <= 64)
            {
                if (count == 0)
                {
                    objectId = oid;
                    explicitEmpty = true;
                    return true;
                }

                var need = 6 + count * 10;
                if (payload.Length >= need)
                {
                    var off = 6;
                    for (var i = 0; i < count; i++)
                    {
                        var sid = BitConverter.ToInt32(payload.Slice(off, 4));
                        if (sid > 0 && sid <= 200000)
                        {
                            effects.Add(sid);
                        }

                        off += 10;
                    }

                    objectId = oid;
                    return effects.Count > 0;
                }
            }
        }

        if (payload.Length >= 4)
        {
            var count = BitConverter.ToInt32(payload.Slice(0, 4));
            if (count > 0 && count <= 64 && payload.Length >= 4 + count * 10)
            {
                var off = 4;
                for (var i = 0; i < count; i++)
                {
                    var sid = BitConverter.ToInt32(payload.Slice(off, 4));
                    if (sid > 0 && sid <= 200000)
                    {
                        effects.Add(sid);
                    }

                    off += 10;
                }

                return effects.Count > 0;
            }
        }

        return false;
    }

    private static long DistanceSq(int x1, int y1, int x2, int y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return (long)dx * dx + (long)dy * dy;
    }
}









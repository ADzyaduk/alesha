namespace L2Companion.World;

public sealed class CharacterState
{
    public int ObjectId { get; set; }
    public int Level { get; set; }
    public int ClassId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }
    public int CurHp { get; set; }
    public int MaxHp { get; set; }
    public int CurMp { get; set; }
    public int MaxMp { get; set; }
    public int MaxHpBaseline { get; set; }
    public int MaxMpBaseline { get; set; }
    public int CurCp { get; set; }
    public int MaxCp { get; set; }
    public int TargetId { get; set; }
    public bool IsSitting { get; set; }
    public string Name { get; set; } = string.Empty;
    public HashSet<int> AbnormalEffectSkillIds { get; set; } = [];
    public DateTime AbnormalUpdatedAtUtc { get; set; } = DateTime.MinValue;

    public float HpPct => EffectiveMaxHp > 0 ? CurHp * 100f / EffectiveMaxHp : 0;
    public float MpPct => EffectiveMaxMp > 0 ? CurMp * 100f / EffectiveMaxMp : 0;

    public int EffectiveMaxHp => EffectiveMax(CurHp, MaxHp, MaxHpBaseline);
    public int EffectiveMaxMp => EffectiveMax(CurMp, MaxMp, MaxMpBaseline);

    public CharacterSnapshot ToSnapshot()
        => new()
        {
            ObjectId = ObjectId,
            Level = Level,
            ClassId = ClassId,
            X = X,
            Y = Y,
            Z = Z,
            Heading = Heading,
            CurHp = CurHp,
            MaxHp = MaxHp,
            CurMp = CurMp,
            MaxMp = MaxMp,
            MaxHpBaseline = MaxHpBaseline,
            MaxMpBaseline = MaxMpBaseline,
            CurCp = CurCp,
            MaxCp = MaxCp,
            TargetId = TargetId,
            IsSitting = IsSitting,
            Name = Name,
            AbnormalEffectSkillIds = AbnormalEffectSkillIds.ToArray(),
            AbnormalUpdatedAtUtc = AbnormalUpdatedAtUtc
        };

    private static int EffectiveMax(int cur, int max, int baseline)
    {
        if (max > 0 && max >= cur)
        {
            if (baseline == 0 || max >= Math.Max(1, (int)(baseline * 0.10f)))
            {
                return max;
            }
        }

        if (baseline > 0)
        {
            return Math.Max(baseline, cur);
        }

        return Math.Max(max, Math.Max(cur, 1));
    }
}

public sealed class NpcState
{
    public int ObjectId { get; set; }
    public int NpcTypeId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int Heading { get; set; }
    public bool IsAttackable { get; set; }
    public bool IsDead { get; set; }
    public bool IsSummoned { get; set; }
    public bool KillCredited { get; set; }
    public float HpPct { get; set; } = 100;
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime LastAggroHitAtUtc { get; set; } = DateTime.MinValue;
    public DateTime LastHitByMeAtUtc { get; set; } = DateTime.MinValue;
    public bool SpoilSucceeded { get; set; }
    public bool SpoilAttempted { get; set; }
    public DateTime SpoilAtUtc { get; set; } = DateTime.MinValue;
    public bool SweepDone { get; set; }
    public DateTime SweepRetryUntilUtc { get; set; } = DateTime.MinValue;
    public HashSet<int> AbnormalEffectSkillIds { get; set; } = [];

    public NpcSnapshot ToSnapshot()
        => new()
        {
            ObjectId = ObjectId,
            NpcTypeId = NpcTypeId,
            X = X,
            Y = Y,
            Z = Z,
            Heading = Heading,
            IsAttackable = IsAttackable,
            IsDead = IsDead,
            IsSummoned = IsSummoned,
            HpPct = HpPct,
            Name = Name,
            Title = Title,
            LastAggroHitAtUtc = LastAggroHitAtUtc,
            LastHitByMeAtUtc = LastHitByMeAtUtc,
            SpoilSucceeded = SpoilSucceeded,
            SpoilAttempted = SpoilAttempted,
            SpoilAtUtc = SpoilAtUtc,
            SweepDone = SweepDone,
            SweepRetryUntilUtc = SweepRetryUntilUtc,
            AbnormalEffectSkillIds = AbnormalEffectSkillIds.ToArray()
        };
}

public sealed class GroundItemState
{
    public int ObjectId { get; set; }
    public int ItemId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public long Count { get; set; }

    public GroundItemSnapshot ToSnapshot()
        => new()
        {
            ObjectId = ObjectId,
            ItemId = ItemId,
            X = X,
            Y = Y,
            Z = Z,
            Count = Count
        };
}

public sealed class PartyMemberState
{
    public int ObjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ClassId { get; set; }
    public int Level { get; set; }
    public int CurHp { get; set; }
    public int MaxHp { get; set; }
    public int CurMp { get; set; }
    public int MaxMp { get; set; }
    public int CurCp { get; set; }
    public int MaxCp { get; set; }

    public float HpPct => MaxHp > 0 ? CurHp * 100f / MaxHp : 0;
    public float MpPct => MaxMp > 0 ? CurMp * 100f / MaxMp : 0;
    public float CpPct => MaxCp > 0 ? CurCp * 100f / MaxCp : 0;

    public PartyMemberSnapshot ToSnapshot()
        => new()
        {
            ObjectId = ObjectId,
            Name = Name,
            ClassId = ClassId,
            Level = Level,
            CurHp = CurHp,
            MaxHp = MaxHp,
            CurMp = CurMp,
            MaxMp = MaxMp,
            CurCp = CurCp,
            MaxCp = MaxCp
        };
}

public sealed class SessionStatsState
{
    public DateTime SessionStartUtc { get; private set; } = DateTime.UtcNow;
    public int Kills { get; private set; }
    public long LootPickedCount { get; private set; }
    public long AdenaGained { get; private set; }
    public long CurrentAdena { get; private set; }

    private bool _hasAdenaBaseline;

    public void Reset()
    {
        SessionStartUtc = DateTime.UtcNow;
        Kills = 0;
        LootPickedCount = 0;
        AdenaGained = 0;
        CurrentAdena = 0;
        _hasAdenaBaseline = false;
    }

    public void AddKill() => Kills++;

    public void AddLootPickups(int count)
    {
        if (count > 0)
        {
            LootPickedCount += count;
        }
    }

    public void SetAdenaSnapshot(long current)
    {
        CurrentAdena = Math.Max(0, current);
        _hasAdenaBaseline = true;
    }

    public void ApplyAdenaUpdate(long current)
    {
        current = Math.Max(0, current);
        if (!_hasAdenaBaseline)
        {
            CurrentAdena = current;
            _hasAdenaBaseline = true;
            return;
        }

        if (current > CurrentAdena)
        {
            AdenaGained += current - CurrentAdena;
        }

        CurrentAdena = current;
    }

    public SessionStatsSnapshot ToSnapshot()
        => new()
        {
            SessionStartUtc = SessionStartUtc,
            Kills = Kills,
            LootPickedCount = LootPickedCount,
            AdenaGained = AdenaGained,
            CurrentAdena = CurrentAdena
        };
}

public sealed class CharacterSnapshot
{
    public int ObjectId { get; init; }
    public int Level { get; init; }
    public int ClassId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public int Heading { get; init; }
    public int CurHp { get; init; }
    public int MaxHp { get; init; }
    public int CurMp { get; init; }
    public int MaxMp { get; init; }
    public int MaxHpBaseline { get; init; }
    public int MaxMpBaseline { get; init; }
    public int CurCp { get; init; }
    public int MaxCp { get; init; }
    public int TargetId { get; init; }
    public bool IsSitting { get; init; }
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<int> AbnormalEffectSkillIds { get; init; } = [];
    public DateTime AbnormalUpdatedAtUtc { get; init; }

    public int EffectiveMaxHp => EffectiveMax(CurHp, MaxHp, MaxHpBaseline);
    public int EffectiveMaxMp => EffectiveMax(CurMp, MaxMp, MaxMpBaseline);
    public float HpPct => EffectiveMaxHp > 0 ? CurHp * 100f / EffectiveMaxHp : 0;
    public float MpPct => EffectiveMaxMp > 0 ? CurMp * 100f / EffectiveMaxMp : 0;

    private static int EffectiveMax(int cur, int max, int baseline)
    {
        if (max > 0 && max >= cur)
        {
            if (baseline == 0 || max >= Math.Max(1, (int)(baseline * 0.10f)))
            {
                return max;
            }
        }

        if (baseline > 0)
        {
            return Math.Max(baseline, cur);
        }

        return Math.Max(max, Math.Max(cur, 1));
    }
}

public sealed class NpcSnapshot
{
    public int ObjectId { get; init; }
    public int NpcTypeId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public int Heading { get; init; }
    public bool IsAttackable { get; init; }
    public bool IsDead { get; init; }
    public bool IsSummoned { get; init; }
    public float HpPct { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public DateTime LastAggroHitAtUtc { get; init; }
    public DateTime LastHitByMeAtUtc { get; init; }
    public bool SpoilSucceeded { get; init; }
    public bool SpoilAttempted { get; init; }
    public DateTime SpoilAtUtc { get; init; }
    public bool SweepDone { get; init; }
    public DateTime SweepRetryUntilUtc { get; init; }
    public IReadOnlyList<int> AbnormalEffectSkillIds { get; init; } = [];
}

public sealed class GroundItemSnapshot
{
    public int ObjectId { get; init; }
    public int ItemId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public long Count { get; init; }
}

public sealed class PartyMemberSnapshot
{
    public int ObjectId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int ClassId { get; init; }
    public int Level { get; init; }
    public int CurHp { get; init; }
    public int MaxHp { get; init; }
    public int CurMp { get; init; }
    public int MaxMp { get; init; }
    public int CurCp { get; init; }
    public int MaxCp { get; init; }

    public float HpPct => MaxHp > 0 ? CurHp * 100f / MaxHp : 0;
}

public sealed class SessionStatsSnapshot
{
    public DateTime SessionStartUtc { get; init; }
    public int Kills { get; init; }
    public long LootPickedCount { get; init; }
    public long AdenaGained { get; init; }
    public long CurrentAdena { get; init; }
}

public sealed class WorldSnapshot
{
    public static WorldSnapshot Empty { get; } = new();

    public CharacterSnapshot Me { get; init; } = new();
    public IReadOnlyList<NpcSnapshot> Npcs { get; init; } = [];
    public IReadOnlyList<GroundItemSnapshot> Items { get; init; } = [];
    public IReadOnlyList<PartyMemberSnapshot> Party { get; init; } = [];
    public IReadOnlyDictionary<int, int> Skills { get; init; } = new Dictionary<int, int>();
    public SessionStatsSnapshot SessionStats { get; init; } = new();

    public DateTime LastMutationUtc { get; init; }
    public DateTime LastUserInfoAtUtc { get; init; }
    public DateTime LastStatusAtUtc { get; init; }
    public DateTime LastNpcUpdateAtUtc { get; init; }
    public DateTime LastLootUpdateAtUtc { get; init; }
    public DateTime LastTargetUpdateAtUtc { get; init; }

    public long UserInfoPackets { get; init; }
    public long StatusPackets { get; init; }
    public long NpcPackets { get; init; }
    public byte? SessionOpcodeXorKey { get; init; }
}

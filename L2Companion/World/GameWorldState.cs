using System.Collections.Concurrent;
using System.Threading;

namespace L2Companion.World;

public sealed class GameWorldState
{
    private readonly object _gate = new();
    private long _lastMutationTicks = DateTime.UtcNow.Ticks;
    private long _lastUserInfoTicks;
    private long _lastStatusTicks;
    private long _lastNpcUpdateTicks;
    private long _lastLootUpdateTicks;
    private long _lastTargetUpdateTicks;

    private long _userInfoPackets;
    private long _statusPackets;
    private long _npcPackets;

    public CharacterState Me { get; } = new();
    public ConcurrentDictionary<int, NpcState> Npcs { get; } = new();
    public ConcurrentDictionary<int, GroundItemState> Items { get; } = new();
    public ConcurrentDictionary<int, int> Skills { get; } = new();
    public ConcurrentDictionary<int, PartyMemberState> Party { get; } = new();
    public ConcurrentDictionary<int, DateTime> SkillCooldownReadyAtUtc { get; } = [];

    public Dictionary<int, long> InventoryByItemId { get; } = [];
    public SessionStatsState SessionStats { get; } = new();

    public byte? SessionOpcodeXorKey { get; set; }
    public bool EnteredWorld { get; set; }
    public int ChangeWaitTypeSitRaw { get; set; } = 0;

    public DateTime LastMutationUtc => new(Interlocked.Read(ref _lastMutationTicks), DateTimeKind.Utc);
    public DateTime LastUserInfoAtUtc => ReadUtc(_lastUserInfoTicks);
    public DateTime LastStatusAtUtc => ReadUtc(_lastStatusTicks);
    public DateTime LastNpcUpdateAtUtc => ReadUtc(_lastNpcUpdateTicks);
    public DateTime LastLootUpdateAtUtc => ReadUtc(_lastLootUpdateTicks);
    public DateTime LastTargetUpdateAtUtc => ReadUtc(_lastTargetUpdateTicks);

    public long UserInfoPackets => Interlocked.Read(ref _userInfoPackets);
    public long StatusPackets => Interlocked.Read(ref _statusPackets);
    public long NpcPackets => Interlocked.Read(ref _npcPackets);

    public void WithLock(Action action)
    {
        lock (_gate)
        {
            action();
        }
    }

    public T WithLock<T>(Func<T> action)
    {
        lock (_gate)
        {
            return action();
        }
    }

    public void ResetSessionStats()
    {
        WithLock(() =>
        {
            SessionStats.Reset();
            InventoryByItemId.Clear();
            SkillCooldownReadyAtUtc.Clear();
            Me.AbnormalEffectSkillIds.Clear();
            Me.AbnormalUpdatedAtUtc = DateTime.MinValue;
        });
    }

    public void MarkMutation(WorldMutationType type)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        Interlocked.Exchange(ref _lastMutationTicks, nowTicks);

        switch (type)
        {
            case WorldMutationType.UserInfo:
                Interlocked.Increment(ref _userInfoPackets);
                Interlocked.Exchange(ref _lastUserInfoTicks, nowTicks);
                break;
            case WorldMutationType.Status:
                Interlocked.Increment(ref _statusPackets);
                Interlocked.Exchange(ref _lastStatusTicks, nowTicks);
                break;
            case WorldMutationType.Npc:
                Interlocked.Increment(ref _npcPackets);
                Interlocked.Exchange(ref _lastNpcUpdateTicks, nowTicks);
                break;
            case WorldMutationType.Loot:
                Interlocked.Exchange(ref _lastLootUpdateTicks, nowTicks);
                break;
            case WorldMutationType.Target:
                Interlocked.Exchange(ref _lastTargetUpdateTicks, nowTicks);
                break;
        }
    }

    public WorldSnapshot CreateSnapshot()
    {
        return WithLock(() =>
        {
            var me = Me.ToSnapshot();
            var npcList = Npcs.Values.Select(x => x.ToSnapshot()).ToArray();
            var itemList = Items.Values.Select(x => x.ToSnapshot()).ToArray();
            var partyList = Party.Values.Select(x => x.ToSnapshot()).ToArray();
            var skillsMap = Skills.ToDictionary(x => x.Key, x => x.Value);
            var stats = SessionStats.ToSnapshot();

            return new WorldSnapshot
            {
                Me = me,
                Npcs = npcList,
                Items = itemList,
                Party = partyList,
                Skills = skillsMap,
                SessionStats = stats,
                LastMutationUtc = LastMutationUtc,
                LastUserInfoAtUtc = LastUserInfoAtUtc,
                LastStatusAtUtc = LastStatusAtUtc,
                LastNpcUpdateAtUtc = LastNpcUpdateAtUtc,
                LastLootUpdateAtUtc = LastLootUpdateAtUtc,
                LastTargetUpdateAtUtc = LastTargetUpdateAtUtc,
                UserInfoPackets = UserInfoPackets,
                StatusPackets = StatusPackets,
                NpcPackets = NpcPackets,
                SessionOpcodeXorKey = SessionOpcodeXorKey
            };
        });
    }

    private static DateTime ReadUtc(long ticks)
    {
        if (ticks <= 0)
        {
            return DateTime.MinValue;
        }

        return new DateTime(ticks, DateTimeKind.Utc);
    }
}

public enum WorldMutationType
{
    Generic,
    UserInfo,
    Status,
    Npc,
    Loot,
    Target
}



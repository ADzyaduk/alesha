using L2Companion.World;

namespace L2Companion.Bot.Services;

public sealed class TargetingService
{
    private const double AggroRecentWindowSec = 18.0;

    private int _anchorX;
    private int _anchorY;
    private int _anchorZ;

    public (int x, int y, int z) GetHuntCenter(BotContext ctx)
    {
        var me = ctx.World.Me;
        if (ctx.Settings.HuntCenterMode != HuntCenterMode.Anchor)
            return (me.X, me.Y, me.Z);

        return (_anchorX, _anchorY, _anchorZ);
    }

    public void EnsureHuntCenterAnchor(BotContext ctx)
    {
        if (ctx.Settings.HuntCenterMode != HuntCenterMode.Anchor)
            return;

        if (_anchorX != 0 || _anchorY != 0 || _anchorZ != 0)
            return;

        var me = ctx.World.Me;
        if (me.ObjectId == 0)
            return;

        _anchorX = me.X;
        _anchorY = me.Y;
        _anchorZ = me.Z;
        ctx.Log.Info($"[AutoFight] hunt anchor set to ({_anchorX},{_anchorY},{_anchorZ})");
    }

    public void SetAnchor(int x, int y, int z)
    {
        _anchorX = x;
        _anchorY = y;
        _anchorZ = z;
    }

    public NpcState? SelectFightTarget(BotContext ctx, CharacterState me, int centerX, int centerY, int centerZ,
        HashSet<int>? temporarilyIgnoredTargets = null, int lastAutoTargetObjectId = 0)
    {
        var settings = ctx.Settings;
        var fightRangeSq = (long)settings.FightRange * settings.FightRange;
        var retain = Math.Max(0, settings.RetainCurrentTargetMaxDist);
        var retainRangeSq = (long)retain * retain;
        var targetZ = Math.Max(0, settings.TargetZRangeMax);

        var whitelist = settings.NpcWhitelistIds;
        var blacklist = settings.NpcBlacklistIds;
        var whitelistOn = settings.AttackOnlyWhitelistMobs && whitelist.Count > 0;
        var now = ctx.Now;

        bool IsCandidate(NpcState npc, long distSq)
        {
            if (!npc.IsAttackable || npc.IsDead)
                return false;

            if (temporarilyIgnoredTargets is not null
                && temporarilyIgnoredTargets.Contains(npc.ObjectId))
                return false;

            if (settings.SkipSummonedNpcs && npc.IsSummoned)
                return false;

            if (targetZ > 0 && Math.Abs(npc.Z - centerZ) > targetZ)
                return false;

            if (distSq > fightRangeSq)
                return false;

            var npcId = npc.NpcTypeId > 1_000_000 ? npc.NpcTypeId - 1_000_000 : npc.NpcTypeId;
            if (blacklist.Contains(npcId))
                return false;

            if (whitelistOn && !whitelist.Contains(npcId))
                return false;

            var whitelisted = whitelist.Contains(npcId);
            if (!whitelisted)
            {
                var isRecentAggro = npc.LastAggroHitAtUtc != DateTime.MinValue
                    && (now - npc.LastAggroHitAtUtc).TotalSeconds <= AggroRecentWindowSec;
                if (!isRecentAggro && (IsNonCombatServiceName(npc.Name) || IsNonCombatServiceName(npc.Title)))
                    return false;

                var likelyMobId = IsLikelyMobByNpcId(npcId);
                if (!likelyMobId && !isRecentAggro)
                    return false;
            }

            return true;
        }

        var retainOid = me.TargetId != 0 ? me.TargetId : lastAutoTargetObjectId;
        if (retainOid != 0 && retainRangeSq > 0 && ctx.World.Npcs.TryGetValue(retainOid, out var retained))
        {
            var retainedDistSq = CombatService.DistanceSq(centerX, centerY, retained.X, retained.Y);
            if (retainedDistSq <= retainRangeSq && IsCandidate(retained, retainedDistSq))
                return retained;
        }

        NpcState? best = null;
        long bestDistSq = long.MaxValue;
        var bestAggroBucket = int.MaxValue;

        foreach (var npc in ctx.World.Npcs.Values)
        {
            var distSq = CombatService.DistanceSq(centerX, centerY, npc.X, npc.Y);
            if (!IsCandidate(npc, distSq))
                continue;

            if (!settings.PreferAggroMobs)
            {
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = npc;
                }
                continue;
            }

            var isRecentAggro = npc.LastAggroHitAtUtc != DateTime.MinValue
                && (now - npc.LastAggroHitAtUtc).TotalSeconds <= AggroRecentWindowSec;
            var aggroBucket = isRecentAggro ? 0 : 1;

            if (aggroBucket < bestAggroBucket || (aggroBucket == bestAggroBucket && distSq < bestDistSq))
            {
                bestAggroBucket = aggroBucket;
                bestDistSq = distSq;
                best = npc;
            }
        }

        return best;
    }

    public NpcState? SelectEmergencyAggroTarget(BotContext ctx, CharacterState me)
    {
        var settings = ctx.Settings;
        var fightRange = Math.Max(900, settings.FightRange);
        var emergencyRange = Math.Max(fightRange, settings.RetainCurrentTargetMaxDist + 500);
        var emergencyRangeSq = (long)emergencyRange * emergencyRange;
        var targetZ = Math.Max(0, settings.TargetZRangeMax);
        var now = ctx.Now;

        NpcState? best = null;
        var bestDistSq = long.MaxValue;
        var bestAggroAgeMs = double.MaxValue;

        foreach (var npc in ctx.World.Npcs.Values)
        {
            if (!npc.IsAttackable || npc.IsDead)
                continue;

            if (settings.SkipSummonedNpcs && npc.IsSummoned)
                continue;

            if (targetZ > 0 && Math.Abs(npc.Z - me.Z) > targetZ)
                continue;

            var aggroAgeMs = npc.LastAggroHitAtUtc == DateTime.MinValue
                ? double.MaxValue
                : (now - npc.LastAggroHitAtUtc).TotalMilliseconds;
            if (aggroAgeMs > 4200)
                continue;

            var distSq = CombatService.DistanceSq(me.X, me.Y, npc.X, npc.Y);
            if (distSq > emergencyRangeSq)
                continue;

            if (aggroAgeMs < bestAggroAgeMs || (Math.Abs(aggroAgeMs - bestAggroAgeMs) < 1 && distSq < bestDistSq))
            {
                best = npc;
                bestAggroAgeMs = aggroAgeMs;
                bestDistSq = distSq;
            }
        }

        return best;
    }

    public static int CountFreshAggroAttackers(BotContext ctx, CharacterState me)
    {
        var range = Math.Max(900, ctx.Settings.FightRange);
        var rangeSq = (long)range * range;
        var targetZ = Math.Max(0, ctx.Settings.TargetZRangeMax);
        var now = ctx.Now;
        var count = 0;

        foreach (var npc in ctx.World.Npcs.Values)
        {
            if (!npc.IsAttackable || npc.IsDead)
                continue;

            if (npc.LastAggroHitAtUtc == DateTime.MinValue || (now - npc.LastAggroHitAtUtc).TotalMilliseconds > 2600)
                continue;

            if (targetZ > 0 && Math.Abs(npc.Z - me.Z) > targetZ)
                continue;

            if (CombatService.DistanceSq(me.X, me.Y, npc.X, npc.Y) > rangeSq)
                continue;

            count++;
        }

        return count;
    }

    public static bool IsLikelyMobByNpcId(int npcId)
        => npcId is >= 20400 and < 30000;

    public static bool IsLikelyHerbItemId(int itemId)
        => itemId is >= 8600 and <= 8625;

    public static bool IsNonCombatServiceName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var v = text.ToLowerInvariant();
        return v.Contains("gatekeeper", StringComparison.Ordinal)
            || v.Contains("warehouse", StringComparison.Ordinal)
            || v.Contains("trader", StringComparison.Ordinal)
            || v.Contains("blacksmith", StringComparison.Ordinal)
            || v.Contains("grocery", StringComparison.Ordinal)
            || v.Contains("fisher", StringComparison.Ordinal)
            || v.Contains("teleporter", StringComparison.Ordinal)
            || v.Contains("pet manager", StringComparison.Ordinal)
            || v.Contains("buffer", StringComparison.Ordinal)
            || v.Contains("manager", StringComparison.Ordinal)
            || v.Contains("supplier", StringComparison.Ordinal);
    }

    public static bool HasCombatEvidence(NpcState npc, DateTime now)
    {
        if (npc.IsDead)
            return true;

        if (npc.HpPct is > 0 and < 99.9f)
            return true;

        if (npc.LastHitByMeAtUtc != DateTime.MinValue && (now - npc.LastHitByMeAtUtc).TotalSeconds <= 60)
            return true;

        if (npc.LastAggroHitAtUtc != DateTime.MinValue && (now - npc.LastAggroHitAtUtc).TotalSeconds <= 25)
            return true;

        return false;
    }
}

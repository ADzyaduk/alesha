using L2Companion.Bot.Services;
using L2Companion.Protocol;
using L2Companion.World;

namespace L2Companion.Bot.Roles;

public sealed class MeleeDdRole : IBotRole
{
    private readonly CombatService _combat;
    private readonly TargetingService _targeting;
    private readonly PostKillService _postKill;

    private int _combatTargetObjectId;
    private int _lastAutoTargetObjectId;
    private DateTime _combatTargetSinceUtc = DateTime.MinValue;
    private readonly HashSet<int> _temporarilyIgnoredTargets = [];
    private DateTime _nextIgnoreCleanupAt = DateTime.MinValue;

    public BotRole RoleType => BotRole.LeaderDD;

    public MeleeDdRole(CombatService combat, TargetingService targeting, PostKillService postKill)
    {
        _combat = combat;
        _targeting = targeting;
        _postKill = postKill;
    }

    public TickResult Tick(BotContext ctx)
    {
        if (!ctx.Settings.AutoFight)
            return TickResult.Continue;

        var me = ctx.World.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
            return TickResult.Continue;

        if (me.IsSitting)
        {
            _combat.SetDecision("melee-paused-sitting");
            return TickResult.Continue;
        }

        var now = ctx.Now;
        _targeting.EnsureHuntCenterAnchor(ctx);
        CleanupIgnoredTargets(now);

        if (_postKill.IsActive)
        {
            if (!_postKill.RunSweepAndLoot(ctx, me))
                _postKill.Reset();

            return TickResult.Yielded;
        }

        if (_combatTargetObjectId != 0)
        {
            if (!ctx.World.Npcs.TryGetValue(_combatTargetObjectId, out var currentTarget) || currentTarget.IsDead)
            {
                if (currentTarget is not null && currentTarget.IsDead)
                {
                    _postKill.Begin(ctx, currentTarget, currentTarget.SpoilSucceeded, "target-dead");
                    _combatTargetObjectId = 0;
                    return TickResult.Yielded;
                }

                _combatTargetObjectId = 0;
            }
            else
            {
                var killTimeout = Math.Max(8000, ctx.Settings.KillTimeoutMs);
                if ((now - _combatTargetSinceUtc).TotalMilliseconds > killTimeout)
                {
                    _temporarilyIgnoredTargets.Add(_combatTargetObjectId);
                    _combatTargetObjectId = 0;
                    _combat.SetDecision("melee-kill-timeout");
                }
            }
        }

        if (_combatTargetObjectId == 0)
        {
            if (ctx.Settings.RestEnabled && me.MpPct <= ctx.Settings.SitMpPct)
            {
                _combat.SetDecision($"melee-mp-low mp={me.MpPct:0.#}%");
                return TickResult.Continue;
            }

            var (cx, cy, cz) = _targeting.GetHuntCenter(ctx);

            var aggroTarget = _targeting.SelectEmergencyAggroTarget(ctx, me);
            if (aggroTarget is not null)
            {
                _combatTargetObjectId = aggroTarget.ObjectId;
                _combatTargetSinceUtc = now;
                _lastAutoTargetObjectId = aggroTarget.ObjectId;
                _combat.SetDecision($"melee-aggro-target:0x{aggroTarget.ObjectId:X}");
            }
            else
            {
                var target = _targeting.SelectFightTarget(ctx, me, cx, cy, cz,
                    _temporarilyIgnoredTargets, _lastAutoTargetObjectId);
                if (target is not null)
                {
                    _combatTargetObjectId = target.ObjectId;
                    _combatTargetSinceUtc = now;
                    _lastAutoTargetObjectId = target.ObjectId;
                }
                else
                {
                    _combat.SetDecision("melee-no-target");
                    return TickResult.Continue;
                }
            }
        }

        if (_combatTargetObjectId != 0 && ctx.World.Npcs.TryGetValue(_combatTargetObjectId, out var npc) && !npc.IsDead)
        {
            var distSq = CombatService.DistanceSq(me.X, me.Y, npc.X, npc.Y);
            var engageRange = Math.Max(80, ctx.Settings.MeleeEngageRange);
            var engageRangeSq = (long)engageRange * engageRange;

            if (me.TargetId != _combatTargetObjectId)
            {
                _combat.TryInject(ctx, PacketBuilder.BuildAction(_combatTargetObjectId, me.X, me.Y, me.Z, 0), "melee-target");
                _combat.BumpAssumedCombatTarget(_combatTargetObjectId, now, 9000);
            }

            if (distSq > engageRangeSq && ctx.Settings.MoveToTarget)
                _combat.TryMoveTo(ctx, npc.X, npc.Y, npc.Z);

            _combat.TryUseCombatRotation(ctx, _combatTargetObjectId);
            _combat.TrySendAttack(ctx, me, _combatTargetObjectId, "melee-fight");
            return TickResult.Yielded;
        }

        return TickResult.Continue;
    }

    public void Reset()
    {
        _combatTargetObjectId = 0;
        _lastAutoTargetObjectId = 0;
        _combatTargetSinceUtc = DateTime.MinValue;
        _temporarilyIgnoredTargets.Clear();
        _nextIgnoreCleanupAt = DateTime.MinValue;
    }

    private void CleanupIgnoredTargets(DateTime now)
    {
        if (now < _nextIgnoreCleanupAt || _temporarilyIgnoredTargets.Count == 0)
            return;

        _temporarilyIgnoredTargets.Clear();
        _nextIgnoreCleanupAt = now.AddSeconds(30);
    }
}

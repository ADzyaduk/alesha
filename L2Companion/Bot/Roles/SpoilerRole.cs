using L2Companion.Bot.Services;
using L2Companion.Protocol;
using L2Companion.World;

namespace L2Companion.Bot.Roles;

public sealed class SpoilerRole : IBotRole
{
    private enum SpoilPhase
    {
        Idle,
        AcquireTarget,
        ConfirmTarget,
        Spoil,
        Attack,
        PostKill
    }

    private readonly CombatService _combat;
    private readonly TargetingService _targeting;
    private readonly PostKillService _postKill;

    private SpoilPhase _phase = SpoilPhase.Idle;
    private int _combatTargetObjectId;
    private int _lastAutoTargetObjectId;
    private DateTime _combatTargetSinceUtc = DateTime.MinValue;
    private int _spoilAttempts;
    private DateTime _nextSpoilAt = DateTime.MinValue;
    private readonly HashSet<int> _temporarilyIgnoredTargets = [];
    private DateTime _nextIgnoreCleanupAt = DateTime.MinValue;

    public BotRole RoleType => BotRole.Spoiler;

    public SpoilerRole(CombatService combat, TargetingService targeting, PostKillService postKill)
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
            _combat.SetDecision("spoiler-paused-sitting");
            return TickResult.Continue;
        }

        var now = ctx.Now;
        _targeting.EnsureHuntCenterAnchor(ctx);
        CleanupIgnoredTargets(now);

        if (_phase == SpoilPhase.PostKill || _postKill.IsActive)
        {
            if (!_postKill.RunSweepAndLoot(ctx, me))
            {
                _postKill.Reset();
                _phase = SpoilPhase.Idle;
            }

            return TickResult.Yielded;
        }

        if (_combatTargetObjectId != 0)
        {
            if (!ctx.World.Npcs.TryGetValue(_combatTargetObjectId, out var currentTarget) || currentTarget.IsDead)
            {
                if (currentTarget is not null && currentTarget.IsDead)
                {
                    _postKill.Begin(ctx, currentTarget, currentTarget.SpoilSucceeded, "spoiler-target-dead");
                    _phase = SpoilPhase.PostKill;
                    _combatTargetObjectId = 0;
                    return TickResult.Yielded;
                }

                _combatTargetObjectId = 0;
                _phase = SpoilPhase.Idle;
            }
            else
            {
                var killTimeout = Math.Max(8000, ctx.Settings.KillTimeoutMs);
                if ((now - _combatTargetSinceUtc).TotalMilliseconds > killTimeout)
                {
                    _temporarilyIgnoredTargets.Add(_combatTargetObjectId);
                    _combatTargetObjectId = 0;
                    _phase = SpoilPhase.Idle;
                    _combat.SetDecision("spoiler-kill-timeout");
                }
            }
        }

        switch (_phase)
        {
            case SpoilPhase.Idle:
            case SpoilPhase.AcquireTarget:
                return TickAcquireTarget(ctx, me, now);

            case SpoilPhase.ConfirmTarget:
                return TickConfirmTarget(ctx, me, now);

            case SpoilPhase.Spoil:
                return TickSpoil(ctx, me, now);

            case SpoilPhase.Attack:
                return TickAttack(ctx, me, now);

            default:
                return TickResult.Continue;
        }
    }

    private TickResult TickAcquireTarget(BotContext ctx, CharacterState me, DateTime now)
    {
        var (cx, cy, cz) = _targeting.GetHuntCenter(ctx);

        var aggroTarget = _targeting.SelectEmergencyAggroTarget(ctx, me);
        if (aggroTarget is not null)
        {
            SetTarget(aggroTarget.ObjectId, now);
            _phase = SpoilPhase.ConfirmTarget;
            return TickResult.Yielded;
        }

        var target = _targeting.SelectFightTarget(ctx, me, cx, cy, cz,
            _temporarilyIgnoredTargets, _lastAutoTargetObjectId);
        if (target is null)
        {
            _combat.SetDecision("spoiler-no-target");
            return TickResult.Continue;
        }

        SetTarget(target.ObjectId, now);
        _phase = SpoilPhase.ConfirmTarget;
        return TickResult.Yielded;
    }

    private TickResult TickConfirmTarget(BotContext ctx, CharacterState me, DateTime now)
    {
        if (_combatTargetObjectId == 0)
        {
            _phase = SpoilPhase.Idle;
            return TickResult.Continue;
        }

        if (!ctx.World.Npcs.TryGetValue(_combatTargetObjectId, out var npc) || npc.IsDead)
        {
            _combatTargetObjectId = 0;
            _phase = SpoilPhase.Idle;
            return TickResult.Continue;
        }

        if (me.TargetId != _combatTargetObjectId)
        {
            _combat.TryInject(ctx, PacketBuilder.BuildAction(_combatTargetObjectId, me.X, me.Y, me.Z, 0), "spoiler-target-confirm");
            _combat.BumpAssumedCombatTarget(_combatTargetObjectId, now, 9000);
        }

        var distSq = CombatService.DistanceSq(me.X, me.Y, npc.X, npc.Y);
        var engageRange = Math.Max(80, ctx.Settings.MeleeEngageRange);
        if (distSq > (long)engageRange * engageRange && ctx.Settings.MoveToTarget)
        {
            _combat.TryMoveTo(ctx, npc.X, npc.Y, npc.Z);
            _combat.TrySendAttack(ctx, me, _combatTargetObjectId, "spoiler-approach");
            return TickResult.Yielded;
        }

        if (ctx.Settings.SpoilEnabled && ctx.Settings.SpoilSkillId > 0)
        {
            _spoilAttempts = 0;
            _nextSpoilAt = DateTime.MinValue;
            _phase = SpoilPhase.Spoil;
        }
        else
        {
            _phase = SpoilPhase.Attack;
        }

        return TickResult.Yielded;
    }

    private TickResult TickSpoil(BotContext ctx, CharacterState me, DateTime now)
    {
        if (_combatTargetObjectId == 0)
        {
            _phase = SpoilPhase.Idle;
            return TickResult.Continue;
        }

        if (!ctx.World.Npcs.TryGetValue(_combatTargetObjectId, out var npc) || npc.IsDead)
        {
            if (npc is not null && npc.IsDead)
            {
                _postKill.Begin(ctx, npc, npc.SpoilSucceeded, "spoiler-target-died-during-spoil");
                _phase = SpoilPhase.PostKill;
                _combatTargetObjectId = 0;
                return TickResult.Yielded;
            }

            _combatTargetObjectId = 0;
            _phase = SpoilPhase.Idle;
            return TickResult.Continue;
        }

        if (npc.SpoilSucceeded)
        {
            _combat.SetDecision("spoil-success");
            _phase = SpoilPhase.Attack;
            return TickResult.Yielded;
        }

        var maxAttempts = Math.Max(1, ctx.Settings.SpoilMaxAttemptsPerTarget);
        if (ctx.Settings.SpoilOncePerTarget && _spoilAttempts >= maxAttempts)
        {
            _combat.SetDecision($"spoil-max-attempts:{_spoilAttempts}");
            _phase = SpoilPhase.Attack;
            return TickResult.Yielded;
        }

        if (now < _nextSpoilAt)
            return TickResult.Yielded;

        if (!CombatService.IsWithinSpoilCastRange(ctx, me, _combatTargetObjectId))
            return TickResult.Yielded;

        var spoilInterval = Math.Max(500, ctx.Settings.SpoilRetryIntervalMs);
        if (_combat.TryCastSkill(ctx, ctx.Settings.SpoilSkillId, forBuff: false, cooldownOverrideMs: spoilInterval, allowReservedSkill: true, bypassSelfCastLock: true))
        {
            _spoilAttempts++;
            _nextSpoilAt = now.AddMilliseconds(spoilInterval);
            _combat.SetDecision($"spoil-cast:{_spoilAttempts}/{maxAttempts}");
        }

        return TickResult.Yielded;
    }

    private TickResult TickAttack(BotContext ctx, CharacterState me, DateTime now)
    {
        if (_combatTargetObjectId == 0)
        {
            _phase = SpoilPhase.Idle;
            return TickResult.Continue;
        }

        if (!ctx.World.Npcs.TryGetValue(_combatTargetObjectId, out var npc) || npc.IsDead)
        {
            if (npc is not null && npc.IsDead)
            {
                _postKill.Begin(ctx, npc, npc.SpoilSucceeded, "spoiler-target-killed");
                _phase = SpoilPhase.PostKill;
                _combatTargetObjectId = 0;
                return TickResult.Yielded;
            }

            _combatTargetObjectId = 0;
            _phase = SpoilPhase.Idle;
            return TickResult.Continue;
        }

        var distSq = CombatService.DistanceSq(me.X, me.Y, npc.X, npc.Y);
        var engageRange = Math.Max(80, ctx.Settings.MeleeEngageRange);
        if (distSq > (long)engageRange * engageRange && ctx.Settings.MoveToTarget)
            _combat.TryMoveTo(ctx, npc.X, npc.Y, npc.Z);

        _combat.TryUseCombatRotation(ctx, _combatTargetObjectId);
        _combat.TrySendAttack(ctx, me, _combatTargetObjectId, "spoiler-fight");
        return TickResult.Yielded;
    }

    private void SetTarget(int objectId, DateTime now)
    {
        _combatTargetObjectId = objectId;
        _combatTargetSinceUtc = now;
        _lastAutoTargetObjectId = objectId;
        _spoilAttempts = 0;
    }

    public void Reset()
    {
        _phase = SpoilPhase.Idle;
        _combatTargetObjectId = 0;
        _lastAutoTargetObjectId = 0;
        _combatTargetSinceUtc = DateTime.MinValue;
        _spoilAttempts = 0;
        _nextSpoilAt = DateTime.MinValue;
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

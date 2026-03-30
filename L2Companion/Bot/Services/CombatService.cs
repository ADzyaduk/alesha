using L2Companion.Bot;
using L2Companion.Core;
using L2Companion.Protocol;
using L2Companion.Proxy;
using L2Companion.World;

namespace L2Companion.Bot.Services;

public sealed class CombatService
{
    private readonly Dictionary<int, DateTime> _nextSkillById = [];
    private DateTime _nextMoveAt = DateTime.MinValue;
    private DateTime _nextForceAttackAt = DateTime.MinValue;
    private DateTime _nextCompatAttackRequestAt = DateTime.MinValue;
    private DateTime _preferAttackRequestUntilUtc = DateTime.MinValue;
    private DateTime _nextTransportSwitchAtUtc = DateTime.MinValue;
    private DateTime _nextTargetActionAt = DateTime.MinValue;
    private int _assumedTargetObjectId;
    private DateTime _assumedTargetUntilUtc = DateTime.MinValue;
    private int _forceAttackIssuedForCombatOid;
    private DateTime _meleeCommandProgressUntilUtc = DateTime.MinValue;
    private BotCommandResult _lastCommand = BotCommandResult.Deferred("init", "not-started");
    private DateTime _nextCommandDiagLogAt = DateTime.MinValue;
    private DateTime _nextSentCommandDiagLogAt = DateTime.MinValue;
    private string _lastDecision = "idle";
    private DateTime _lastDecisionAtUtc = DateTime.UtcNow;

    public string LastDecision => _lastDecision;
    public DateTime LastDecisionAtUtc => _lastDecisionAtUtc;
    public BotCommandResult LastCommand => _lastCommand;
    public int AssumedTargetObjectId => _assumedTargetObjectId;
    public DateTime AssumedTargetUntilUtc => _assumedTargetUntilUtc;
    public int ForceAttackIssuedForCombatOid => _forceAttackIssuedForCombatOid;
    public DateTime MeleeCommandProgressUntilUtc => _meleeCommandProgressUntilUtc;
    public DateTime NextForceAttackAt { get => _nextForceAttackAt; set => _nextForceAttackAt = value; }
    public DateTime NextTargetActionAt { get => _nextTargetActionAt; set => _nextTargetActionAt = value; }

    public void SetDecision(string decision)
    {
        _lastDecision = decision;
        _lastDecisionAtUtc = DateTime.UtcNow;
    }

    public bool TryInject(BotContext ctx, byte[] plainBody, string action)
        => SendCommand(ctx, plainBody, action).Status == BotCommandStatus.Sent;

    public BotCommandResult SendCommand(BotContext ctx, byte[] plainBody, string action)
    {
        if (plainBody.Length == 0)
        {
            _lastCommand = BotCommandResult.Rejected(action, "empty-payload");
            MaybeLogCommandOutcome(ctx, _lastCommand);
            return _lastCommand;
        }

        var d = ctx.Proxy.Diagnostics;
        if (!IsProxyReady(ctx))
        {
            SetDecision($"wait-proxy run={ctx.Proxy.IsRunning} gc={d.GameClientConnected} gs={d.GameServerConnected} cry={d.GameCryptoReady}");
            _lastCommand = BotCommandResult.Deferred(action, "proxy-not-ready", plainBody[0]);
            MaybeLogCommandOutcome(ctx, _lastCommand);
            return _lastCommand;
        }

        try
        {
            ctx.Proxy.InjectToServer(plainBody);
            SetDecision($"{action} 0x{plainBody[0]:X2}");
            _lastCommand = BotCommandResult.Sent(action, plainBody[0]);
            MaybeLogCommandOutcome(ctx, _lastCommand);
            return _lastCommand;
        }
        catch (Exception ex)
        {
            _lastCommand = BotCommandResult.Error(action, ex.Message, plainBody[0]);
            MaybeLogCommandOutcome(ctx, _lastCommand);
            return _lastCommand;
        }
    }

    public bool IsProxyReady(BotContext ctx)
    {
        var d = ctx.Proxy.Diagnostics;
        return ctx.Proxy.IsRunning
            && d.GameClientConnected
            && d.GameServerConnected
            && d.GameCryptoReady;
    }

    public bool TryCastSkill(BotContext ctx, int skillId, bool forBuff, int? cooldownOverrideMs = null, bool allowReservedSkill = false, bool bypassSelfCastLock = false)
    {
        if (skillId <= 0)
            return false;

        var now = ctx.Now;
        if (!bypassSelfCastLock && !forBuff && now < ctx.World.Me.CastingUntilUtc)
        {
            SetDecision($"skill-cast-lock:{skillId}");
            LogVerboseCombatSkill(ctx, skillId, "reject cast-lock");
            return false;
        }

        if (!forBuff && !allowReservedSkill && IsSpecialCombatSkill(ctx.Settings, skillId))
        {
            SetDecision($"skill-reserved:{skillId}");
            LogVerboseCombatSkill(ctx, skillId, "reject reserved");
            return false;
        }

        var hasKnownSkill = ctx.World.Skills.ContainsKey(skillId);
        if (!hasKnownSkill)
        {
            var canOptimisticSupportCast = forBuff && !allowReservedSkill;
            if (!canOptimisticSupportCast && !allowReservedSkill)
            {
                SetDecision($"skill-missing:{skillId}");
                LogVerboseCombatSkill(ctx, skillId, "reject missing-skill");
                return false;
            }

            SetDecision(canOptimisticSupportCast
                ? $"skill-missing-optimistic:{skillId}"
                : $"skill-missing-allow:{skillId}");
        }

        if (ctx.World.SkillCooldownReadyAtUtc.TryGetValue(skillId, out var serverReadyAt))
        {
            if (now < serverReadyAt)
            {
                _nextSkillById[skillId] = serverReadyAt;
                SetDecision($"skill-cd-server:{skillId}");
                LogVerboseCombatSkill(ctx, skillId, "reject server-cd");
                return false;
            }

            if (serverReadyAt <= now.AddMilliseconds(-600))
                ctx.World.SkillCooldownReadyAtUtc.TryRemove(skillId, out _);
        }

        if (_nextSkillById.TryGetValue(skillId, out var nextAllowed) && now < nextAllowed)
        {
            SetDecision($"skill-cd:{skillId}");
            LogVerboseCombatSkill(ctx, skillId, "reject local-cd");
            return false;
        }

        var teonPipeline = ctx.Settings.AttackPipelineMode == AttackPipelineMode.TeonActionPlus2F;
        var use2F = forBuff
            ? teonPipeline || string.Equals(ctx.Settings.BuffSkillPacket, "2f", StringComparison.OrdinalIgnoreCase)
            : teonPipeline || string.Equals(ctx.Settings.CombatSkillPacket, "2f", StringComparison.OrdinalIgnoreCase);

        if (use2F)
        {
            if (!TryInject(ctx, PacketBuilder.BuildShortcutSkillUse(skillId), $"skill-2f:{skillId}"))
            {
                LogVerboseCombatSkill(ctx, skillId, "reject inject-failed 2f");
                return false;
            }
        }
        else
        {
            if (!TryInject(ctx, PacketBuilder.BuildMagicSkillUse(skillId, payloadStyle: ctx.Settings.MagicSkillPayload), $"skill-39:{skillId}"))
            {
                LogVerboseCombatSkill(ctx, skillId, "reject inject-failed 39");
                return false;
            }
        }

        MarkSpoilAttemptOnCurrentTarget(ctx, skillId, forBuff, now);

        var cd = Math.Max(250, cooldownOverrideMs ?? ctx.Settings.CombatSkillCooldownMs);
        _nextSkillById[skillId] = now.AddMilliseconds(cd);
        if (!forBuff)
        {
            MarkMeleeCombatCommandSent(ctx);
            var bumpOid = ctx.World.Me.TargetId;
            if (bumpOid != 0)
                BumpAssumedCombatTarget(bumpOid, now, 9500);
        }

        return true;
    }

    public bool TrySendAttack(BotContext ctx, CharacterState me, int targetObjectId, string actionPrefix)
    {
        if (targetObjectId == 0)
            return false;

        var (tx, ty, tz) = ResolveTargetPosition(ctx, me, targetObjectId);
        var now = ctx.Now;
        if (now < me.CastingUntilUtc)
        {
            SetDecision($"attack-cast-lock until={me.CastingUntilUtc:O}");
            if (ctx.Settings.VerboseCombatSkillLog)
                ctx.Log.Info($"[CombatAttack] blocked cast-lock oid=0x{targetObjectId:X}");
            return false;
        }

        if (ctx.Settings.AttackPipelineMode == AttackPipelineMode.LegacyAttackRequest)
        {
            if (TryInject(ctx, PacketBuilder.BuildAttackRequest(targetObjectId, tx, ty, tz, shift: (byte)(ctx.Settings.UseForceAttack ? 1 : 0)), $"{actionPrefix}-0a"))
            {
                MarkMeleeCombatCommandSent(ctx);
                BumpAssumedCombatTarget(targetObjectId, now, 9000);
                _forceAttackIssuedForCombatOid = targetObjectId;
                return true;
            }

            if (TryInject(ctx, PacketBuilder.BuildForceAttack(targetObjectId, tx, ty, tz), $"{actionPrefix}-2f16"))
            {
                MarkMeleeCombatCommandSent(ctx);
                BumpAssumedCombatTarget(targetObjectId, now, 9000);
                _forceAttackIssuedForCombatOid = targetObjectId;
                return true;
            }

            return false;
        }

        var sent = false;
        var preferAttackRequest = ctx.Settings.AttackTransportMode == AttackTransportMode.AutoPrimary04Plus2F && now < _preferAttackRequestUntilUtc;
        var assumedOk = _assumedTargetObjectId == targetObjectId && now <= _assumedTargetUntilUtc;

        if (!preferAttackRequest)
        {
            if (me.TargetId != targetObjectId)
            {
                if (now >= _nextTargetActionAt
                    && TryInject(ctx, PacketBuilder.BuildAction(targetObjectId, me.X, me.Y, me.Z, 0), $"{actionPrefix}-04"))
                {
                    sent = true;
                    BumpAssumedCombatTarget(targetObjectId, now, 9000);
                    _nextTargetActionAt = now.AddMilliseconds(820);
                }
                else if (assumedOk
                    && TryInject(ctx, PacketBuilder.BuildForceAttack(targetObjectId, tx, ty, tz), $"{actionPrefix}-2f16"))
                {
                    sent = true;
                    _forceAttackIssuedForCombatOid = targetObjectId;
                    if (_nextTargetActionAt < now.AddMilliseconds(620))
                        _nextTargetActionAt = now.AddMilliseconds(620);
                }
            }
            else if (TryInject(ctx, PacketBuilder.BuildForceAttack(targetObjectId, tx, ty, tz), $"{actionPrefix}-2f16"))
            {
                sent = true;
                _forceAttackIssuedForCombatOid = targetObjectId;
                if (_nextTargetActionAt < now.AddMilliseconds(620))
                    _nextTargetActionAt = now.AddMilliseconds(620);
            }
        }

        if (preferAttackRequest || ctx.Settings.UseAttackRequestFallback || ctx.Settings.PreferAttackRequest)
        {
            if (now >= _nextCompatAttackRequestAt
                && TryInject(ctx, PacketBuilder.BuildAttackRequest(targetObjectId, tx, ty, tz, shift: (byte)(ctx.Settings.UseForceAttack ? 1 : 0)), $"{actionPrefix}-0a-compat"))
            {
                sent = true;
                _nextCompatAttackRequestAt = now.AddMilliseconds(preferAttackRequest ? 650 : 900);
                _forceAttackIssuedForCombatOid = targetObjectId;
            }
        }

        if (sent)
        {
            MarkMeleeCombatCommandSent(ctx);
            BumpAssumedCombatTarget(targetObjectId, now, 9000);
        }

        return sent;
    }

    public bool TryMoveTo(BotContext ctx, int destX, int destY, int destZ)
    {
        var now = ctx.Now;
        if (now < _nextMoveAt)
            return false;

        var me = ctx.World.Me;
        var distSq = DistanceSq(me.X, me.Y, destX, destY);
        if (distSq <= 64)
            return false;

        if (!TryInject(ctx, PacketBuilder.BuildMoveToLocation(destX, destY, destZ, me.X, me.Y, me.Z), "move"))
            return false;

        _nextMoveAt = now.AddMilliseconds(280);
        return true;
    }

    public void BumpAssumedCombatTarget(int objectId, DateTime now, int minExtendMs)
    {
        if (objectId == 0)
            return;

        var extendMs = Math.Clamp(minExtendMs, 2800, 30000);
        var until = now.AddMilliseconds(extendMs);
        if (_assumedTargetObjectId != objectId)
        {
            _assumedTargetObjectId = objectId;
            _assumedTargetUntilUtc = until;
            return;
        }

        if (until > _assumedTargetUntilUtc)
            _assumedTargetUntilUtc = until;
    }

    public void MarkMeleeCombatCommandSent(BotContext ctx)
    {
        if (IsCasterRole(ctx.Settings))
            return;

        var now = DateTime.UtcNow;
        var extendMs = Math.Max(8500, ctx.Settings.AttackNoProgressWindowMs + 4000);
        _meleeCommandProgressUntilUtc = now.AddMilliseconds(extendMs);
    }

    public void ActivateAttackRequestFallback(BotContext ctx, DateTime now, string reason)
    {
        if (ctx.Settings.AttackTransportMode != AttackTransportMode.AutoPrimary04Plus2F)
            return;

        if (now < _nextTransportSwitchAtUtc)
            return;

        _preferAttackRequestUntilUtc = now.AddMilliseconds(6500);
        _nextTransportSwitchAtUtc = now.AddMilliseconds(8000);
        ctx.Log.Info($"[AutoFight] transport-fallback => 0x0A reason={reason} until={_preferAttackRequestUntilUtc:HH:mm:ss.fff}");
    }

    public bool TryUseCombatRotation(BotContext ctx, int combatTargetObjectId)
    {
        var me = ctx.World.Me;
        NpcState? target = null;
        long targetDistSq = 0;
        var oid = combatTargetObjectId != 0 ? combatTargetObjectId : me.TargetId;
        if (oid != 0 && ctx.World.Npcs.TryGetValue(oid, out var npc) && !npc.IsDead && npc.HpPct > 0.01f)
        {
            target = npc;
            targetDistSq = DistanceSq(me.X, me.Y, npc.X, npc.Y);
        }

        var useFsm = ctx.Settings.EnableCombatFsmV2;
        foreach (var rule in CombatSkillRotation.OrderRules(ctx.Settings.AttackSkills, useFsm))
        {
            if (rule.SkillId <= 0 || IsSpecialCombatSkill(ctx.Settings, rule.SkillId))
                continue;

            if (!CombatSkillRotation.RuleConditionsAllow(rule, me, target, targetDistSq, useFsm))
                continue;

            if (!TryCastSkill(ctx, rule.SkillId, forBuff: false, cooldownOverrideMs: Math.Max(250, rule.CooldownMs)))
                continue;

            return true;
        }

        var fallback = new[] { ctx.Settings.CombatSkill1Id, ctx.Settings.CombatSkill2Id, ctx.Settings.CombatSkill3Id }
            .Where(x => x > 0 && !IsSpecialCombatSkill(ctx.Settings, x));

        foreach (var skillId in fallback)
        {
            if (!TryCastSkill(ctx, skillId, forBuff: false, cooldownOverrideMs: ctx.Settings.CombatSkillCooldownMs))
                continue;

            return true;
        }

        return false;
    }

    public void ResetTimers()
    {
        _nextMoveAt = DateTime.MinValue;
        _nextForceAttackAt = DateTime.MinValue;
        _nextCompatAttackRequestAt = DateTime.MinValue;
        _nextTargetActionAt = DateTime.MinValue;
        _preferAttackRequestUntilUtc = DateTime.MinValue;
        _nextTransportSwitchAtUtc = DateTime.MinValue;
        _assumedTargetObjectId = 0;
        _assumedTargetUntilUtc = DateTime.MinValue;
        _forceAttackIssuedForCombatOid = 0;
        _meleeCommandProgressUntilUtc = DateTime.MinValue;
    }

    public static bool IsSpecialCombatSkill(BotSettings settings, int skillId)
    {
        if (skillId <= 0)
            return false;

        if (settings.SpoilEnabled && settings.SpoilSkillId > 0 && skillId == settings.SpoilSkillId)
            return true;

        if (settings.SweepEnabled && settings.SweepSkillId > 0 && skillId == settings.SweepSkillId)
            return true;

        return false;
    }

    public static bool IsCasterRole(BotSettings settings)
        => settings.Role == BotRole.CasterDD || settings.BattleMode == BotBattleMode.StrictCaster;

    public static bool IsSupportRole(BotSettings settings)
        => settings.Role is BotRole.Healer or BotRole.Buffer;

    public static long DistanceSq(int x1, int y1, int x2, int y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return (long)dx * dx + (long)dy * dy;
    }

    public static bool IsWithinSpoilCastRange(BotContext ctx, CharacterState me, int targetObjectId)
    {
        var maxD = Math.Max(80, ctx.Settings.SpoilMaxCastDistance);
        if (targetObjectId == 0 || !ctx.World.Npcs.TryGetValue(targetObjectId, out var npc) || npc.IsDead)
            return false;

        return DistanceSq(me.X, me.Y, npc.X, npc.Y) <= (long)maxD * maxD;
    }

    public static (int x, int y, int z) ResolveTargetPosition(BotContext ctx, CharacterState me, int targetObjectId)
    {
        if (ctx.World.Npcs.TryGetValue(targetObjectId, out var npc))
            return (npc.X, npc.Y, npc.Z);

        return (me.X, me.Y, me.Z);
    }

    // Lets TryMarkSpoilSuccess(SystemMessage 612/357) correlate with our last spoil inject on this target.
    private static void MarkSpoilAttemptOnCurrentTarget(BotContext ctx, int skillId, bool forBuff, DateTime nowUtc)
    {
        if (forBuff || !ctx.Settings.SpoilEnabled || ctx.Settings.SpoilSkillId <= 0 || skillId != ctx.Settings.SpoilSkillId)
            return;

        var tid = ctx.World.Me.TargetId;
        if (tid == 0 || !ctx.World.Npcs.TryGetValue(tid, out var npc))
            return;

        npc.SpoilAttempted = true;
        npc.SpoilAtUtc = nowUtc;
        ctx.World.Npcs[tid] = npc;
        ctx.World.MarkMutation(WorldMutationType.Npc);
    }

    private void LogVerboseCombatSkill(BotContext ctx, int skillId, string detail)
    {
        if (!ctx.Settings.VerboseCombatSkillLog)
            return;

        ctx.Log.Info($"[CombatSkill] id={skillId} {detail}");
    }

    private void MaybeLogCommandOutcome(BotContext ctx, BotCommandResult result)
    {
        var now = DateTime.UtcNow;
        if (result.Status == BotCommandStatus.Sent)
        {
            if (result.Opcode is 0x01 or 0x04 or 0x0A or 0x2F or 0x39 or 0x45 or 0x48 && now >= _nextSentCommandDiagLogAt)
            {
                _nextSentCommandDiagLogAt = now.AddMilliseconds(420);
                ctx.Log.Info($"[BotCmd] Sent {result.Action} op=0x{result.Opcode:X2}");
            }

            return;
        }

        if (now < _nextCommandDiagLogAt)
            return;

        _nextCommandDiagLogAt = now.AddMilliseconds(900);
        var d = ctx.Proxy.Diagnostics;
        ctx.Log.Info($"[BotCmd] {result.Status} {result.Action} op=0x{result.Opcode:X2} reason={result.Reason} stage={d.SessionStage} gc={d.GameClientConnected} gs={d.GameServerConnected} cry={d.GameCryptoReady}");
    }
}

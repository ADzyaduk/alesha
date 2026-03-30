using L2Companion.Bot.Roles;
using L2Companion.Bot.Services;
using L2Companion.Core;
using L2Companion.Protocol;
using L2Companion.Proxy;
using L2Companion.World;

namespace L2Companion.Bot;

public enum BotRuntimeState
{
    Stopped,
    Running,
    PausedNoSession
}

public sealed class BotEngine
{
    private readonly ProxyService _proxy;
    private readonly GameWorldState _world;
    private readonly LogService _log;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private BotRuntimeState _runtimeState = BotRuntimeState.Stopped;
    private string _runtimeReason = "stopped";
    private DateTime _lastRuntimeTransitionAtUtc = DateTime.UtcNow;

    private string _combatPhase = "idle";
    private DateTime _nextPhaseDiagLogAt = DateTime.MinValue;
    private bool _criticalHoldActive;
    private int _lastObservedSelfHp = -1;
    private DateTime _lastIncomingDamageAtUtc = DateTime.MinValue;

    private int _lastCombatTargetOid;

    private readonly CombatCoordinator _coordinator;
    private DateTime _nextCoordinatorFollowAt = DateTime.MinValue;
    private long _coordinatorSequence;

    private readonly CombatService _combat;
    private readonly TargetingService _targeting;
    private readonly HealService _heal;
    private readonly BuffService _buff;
    private readonly RechargeService _recharge;
    private readonly LootService _loot;
    private readonly PostKillService _postKill;
    private readonly RestService _rest;

    private IBotRole _activeRole;
    private readonly MeleeDdRole _meleeDdRole;
    private readonly CasterDdRole _casterDdRole;
    private readonly SpoilerRole _spoilerRole;
    private readonly HealerRole _healerRole;
    private readonly BufferRole _bufferRole;

    public BotEngine(ProxyService proxy, GameWorldState world, LogService log)
    {
        _proxy = proxy;
        _world = world;
        _log = log;
        _coordinator = new CombatCoordinator(log);

        _combat = new CombatService();
        _targeting = new TargetingService();
        _heal = new HealService(_combat);
        _buff = new BuffService(_combat);
        _recharge = new RechargeService(_combat);
        _loot = new LootService(_combat);
        _postKill = new PostKillService(_combat);
        _rest = new RestService(_combat);

        _meleeDdRole = new MeleeDdRole(_combat, _targeting, _postKill);
        _casterDdRole = new CasterDdRole(_combat, _targeting, _postKill);
        _spoilerRole = new SpoilerRole(_combat, _targeting, _postKill);
        _healerRole = new HealerRole(_combat, _heal, _buff, _recharge);
        _bufferRole = new BufferRole(_combat, _buff, _heal, _recharge);

        _activeRole = _meleeDdRole;
    }

    public bool IsRunning => _runtimeState != BotRuntimeState.Stopped;
    public BotRuntimeState RuntimeState => _runtimeState;
    public BotSettings Settings { get; } = new();

    public string GetDecisionSummary()
    {
        var age = (DateTime.UtcNow - _combat.LastDecisionAtUtc).TotalSeconds;
        var d = _proxy.Diagnostics;
        return $"{_combat.LastDecision}  ({age:0.0}s)  Phase:{_combatPhase}  Cmd:{_combat.LastCommand.Status}/{_combat.LastCommand.Action}  Inject:{d.InjectPackets} Pending:{d.PendingInjectPackets} Target:0x{_world.Me.TargetId:X}";
    }

    public string GetLastCommandTrace() => _combat.LastCommand.ToString();

    public string GetRuntimeSummary()
    {
        var age = (DateTime.UtcNow - _lastRuntimeTransitionAtUtc).TotalSeconds;
        var d = _proxy.Diagnostics;
        return $"{_runtimeState} ({age:0.0}s) reason={_runtimeReason} stage={d.SessionStage}";
    }

    public string GetCombatSummary()
    {
        return $"phase={_combatPhase} role={_activeRole.RoleType} target=0x{_lastCombatTargetOid:X}";
    }

    public void Start()
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        SyncActiveRole();
        ResetAllServices();
        EnsureCoordinatorMode();
        TransitionRuntime(IsProxyReady() ? BotRuntimeState.Running : BotRuntimeState.PausedNoSession,
            IsProxyReady() ? "start-ready" : "start-no-session", logTransition: false);
        _loopTask = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
        _log.Info("Bot engine started.");
        _log.Info($"[AutoFight] config AutoFight={Settings.AutoFight} Role={Settings.Role} Mode={Settings.BattleMode} Coord={Settings.CoordMode} Spoil={Settings.SpoilEnabled} SpoilSkillId={Settings.SpoilSkillId} Sweep={Settings.SweepEnabled} SweepSkillId={Settings.SweepSkillId}");
    }

    public void Stop()
    {
        if (_cts is null)
            return;

        var cts = _cts;
        var loopTask = _loopTask;
        _cts = null;
        _loopTask = null;

        try { cts.Cancel(); } catch { }
        try { loopTask?.Wait(600); } catch { }

        cts.Dispose();
        ResetAllServices();

        try { _coordinator.Configure(false, CoordMode.Standalone, Settings.CoordinatorChannel); } catch { }

        TransitionRuntime(BotRuntimeState.Stopped, "manual-stop", logTransition: false);
        _log.Info("Bot engine stopped.");
    }

    // ------------------------------------------------------------------ //
    // Main loop
    // ------------------------------------------------------------------ //

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                RefreshRuntimeState();
                if (_runtimeState != BotRuntimeState.Running)
                {
                    SetCombatPhase("paused-no-session");
                    _combat.SetDecision("paused-no-session");
                    await Task.Delay(220, ct);
                    continue;
                }

                if (!HasWorldContext())
                {
                    await Task.Delay(220, ct);
                    continue;
                }

                SyncActiveRole();
                var ctx = BuildContext();
                var me = ctx.World.Me;

                if (me.CurHp <= 0 && me.ObjectId != 0)
                {
                    SetCombatPhase("dead-wait-res");
                    _combat.SetDecision("dead-wait-res");
                    _lastCombatTargetOid = 0;
                    await Task.Delay(2000, ct);
                    continue;
                }

                TrackIncomingDamage(ctx);

                if (_heal.TickSelfHeal(ctx, inFight: _combatPhase == "in_kill_loop"))
                {
                    await Task.Delay(120, ct);
                    continue;
                }

                _heal.TickPartyHeal(ctx, inFight: _combatPhase == "in_kill_loop", criticalHoldActive: _criticalHoldActive);
                _recharge.Tick(ctx, criticalHoldActive: _criticalHoldActive);
                _buff.Tick(ctx, inFight: _combatPhase == "in_kill_loop", criticalHoldActive: _criticalHoldActive,
                    postKillActive: false, combatPhase: _combatPhase);

                TickCoordinatorFollower(ctx);

                if (_criticalHoldActive && !CombatService.IsSupportRole(Settings))
                {
                    SetCombatPhase("critical-hold");
                    _combat.SetDecision($"critical-hold hp={me.HpPct:0.#}%");
                    _rest.Tick(BuildContext(), inCombatFlow: false);
                    await Task.Delay(200, ct);
                    continue;
                }

                if (CombatService.IsSupportRole(Settings))
                {
                    var result = _activeRole.Tick(ctx);
                    UpdateCombatPhaseFromRole(result);
                    TickCoordinatorLeader(ctx);
                    await Task.Delay(180, ct);
                    continue;
                }

                if (!Settings.AutoFight)
                {
                    _rest.Tick(ctx, inCombatFlow: false);
                    _loot.Tick(ctx, inCombat: false, postKillActive: false, hasRecentDamage: false);
                    SetCombatPhase("idle");
                    await Task.Delay(200, ct);
                    continue;
                }

                await RunCombatCycleAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.Info($"Bot loop error: {ex.Message}");
                try { await Task.Delay(1000, ct); } catch { break; }
            }
        }
    }

    // ------------------------------------------------------------------ //
    // Sequential combat (ported from Python _auto_combat_loop)
    // ------------------------------------------------------------------ //

    private async Task RunCombatCycleAsync(CancellationToken ct)
    {
        // Loot any nearby items (from previous kills)
        if (Settings.AutoLoot)
            await LootNearbyAsync(ct);
        if (ct.IsCancellationRequested) return;

        await AwaitRestMpIfNeededAsync(ct);
        if (ct.IsCancellationRequested) return;

        _targeting.EnsureHuntCenterAnchor(BuildContext());
        var ctx = BuildContext();
        var me = ctx.World.Me;
        var (cx, cy, cz) = _targeting.GetHuntCenter(ctx);
        var mob = _targeting.SelectFightTarget(ctx, me, cx, cy, cz, lastAutoTargetObjectId: _lastCombatTargetOid);

        if (mob is null)
        {
            _lastCombatTargetOid = 0;
            SetCombatPhase("idle");
            _combat.SetDecision("no-mobs-in-range");

            if (Settings.AutoLoot)
                await LootNearbyAsync(ct);

            await AwaitRestMpIfNeededAsync(ct);

            if (Settings.IdleSitEnabled && AllowRecoverySit())
            {
                me = _world.Me;
                var needHpSit = me.EffectiveMaxHp > 0 && me.HpPct < Settings.PostKillSitHpBelowPct;
                var needMpSit = Settings.RecoverySitMpBelowPct > 0 && me.EffectiveMaxMp > 0 && me.MpPct < Settings.RecoverySitMpBelowPct;
                if (needHpSit || needMpSit)
                    await RecoverAsync(Settings.PostKillStandHpPct, ct);
            }

            TickCoordinatorLeader(BuildContext());
            await Task.Delay(Math.Max(200, Settings.IdleNoMobsSleepMs), ct);
            return;
        }

        var targetOid = mob.ObjectId;
        _lastCombatTargetOid = targetOid;
        _log.Info($"[AutoCombat] Target: npcId={mob.NpcTypeId} oid=0x{targetOid:X} dist={Math.Sqrt(CombatService.DistanceSq(me.X, me.Y, mob.X, mob.Y)):0}");

        await EnsureStandingAsync(ct);
        if (ct.IsCancellationRequested) return;

        // Target the mob via Action (0x04)
        ctx = BuildContext();
        me = ctx.World.Me;
        _combat.TryInject(ctx, PacketBuilder.BuildAction(targetOid, me.X, me.Y, me.Z, 0), "target-action");
        SetCombatPhase("targeting");
        await Task.Delay(Math.Max(50, Settings.PostTargetDelayMs), ct);
        if (ct.IsCancellationRequested) return;

        // Spoil BEFORE attack rotation: rotation skills trigger MagicSkillLaunched → SelfCastLock and block Spoil otherwise.
        var spoilAttempts = 0;
        var spoilIntervalMs = Math.Max(500, Settings.SpoilRetryIntervalMs);
        if (Settings.SpoilEnabled && Settings.SpoilSkillId > 0)
        {
            await Task.Delay(120, ct);
            if (ct.IsCancellationRequested) return;
            ctx = BuildContext();
            me = ctx.World.Me;
            if (CombatService.IsWithinSpoilCastRange(ctx, me, targetOid))
            {
                if (_combat.TryCastSkill(ctx, Settings.SpoilSkillId, forBuff: false,
                        cooldownOverrideMs: spoilIntervalMs, allowReservedSkill: true, bypassSelfCastLock: true))
                {
                    spoilAttempts++;
                    _log.Info($"[AutoCombat] Spoil attempt #{spoilAttempts} on 0x{targetOid:X}");
                }
                else
                    _log.Info($"[AutoCombat] Spoil opening blocked skillId={Settings.SpoilSkillId} decision={_combat.LastDecision}");
            }
            else
                _log.Info($"[AutoCombat] Spoil opening skipped (out of range > {Settings.SpoilMaxCastDistance}), retry in kill loop");

            await Task.Delay(350, ct);
            if (ct.IsCancellationRequested) return;
        }

        // Combat rotation
        ctx = BuildContext();
        _combat.TryUseCombatRotation(ctx, targetOid);
        await Task.Delay(120, ct);
        if (ct.IsCancellationRequested) return;

        // Force attack (melee/spoiler only — casters rely on skills)
        if (!IsCasterMode())
        {
            ctx = BuildContext();
            _combat.TrySendAttack(ctx, ctx.World.Me, targetOid, "engage");
        }

        // === KILL LOOP ===
        await KillLoopAsync(targetOid, spoilAttempts, ct);
        if (ct.IsCancellationRequested) return;

        // Post-kill: Sweep
        if (Settings.SpoilEnabled && Settings.SweepEnabled && Settings.SweepSkillId > 0)
        {
            ctx = BuildContext();
            me = ctx.World.Me;
            _combat.TryInject(ctx, PacketBuilder.BuildAction(targetOid, me.X, me.Y, me.Z, 0), "sweep-target");
            await Task.Delay(Math.Max(50, Settings.PostKillSweepDelayMs), ct);
            ctx = BuildContext();
            _combat.TryCastSkill(ctx, Settings.SweepSkillId, forBuff: false,
                cooldownOverrideMs: 350, allowReservedSkill: true, bypassSelfCastLock: true);
            await Task.Delay(350, ct);
        }

        // Cancel target
        ctx = BuildContext();
        _combat.TryInject(ctx, PacketBuilder.BuildTargetCancel(), "post-kill-cancel");

        // Post-kill loot (casters skip walking to distant items — collect during idle/pre-combat)
        if (Settings.AutoLoot)
        {
            await Task.Delay(Math.Max(50, Settings.PostKillSpawnWaitMs), ct);
            await LootNearbyAsync(ct, maxAttempts: 80, walkToItems: !IsCasterMode());
        }

        // Post-kill recovery
        if (Settings.PostKillSitEnabled && AllowRecoverySit())
        {
            me = _world.Me;
            var needHpSit = me.EffectiveMaxHp > 0 && me.HpPct < Settings.PostKillSitHpBelowPct;
            var needMpSit = Settings.RecoverySitMpBelowPct > 0 && me.EffectiveMaxMp > 0 && me.MpPct < Settings.RecoverySitMpBelowPct;
            if (needHpSit || needMpSit)
            {
                _log.Info($"[AutoCombat] HP={me.HpPct:0}% MP={me.MpPct:0}% after kill — sitting to recover");
                await RecoverAsync(Settings.PostKillStandHpPct, ct);
            }
        }

        await AwaitRestMpIfNeededAsync(ct);

        TickCoordinatorLeader(BuildContext());
        await Task.Delay(Math.Max(50, Settings.BetweenTargetsSleepMs), ct);
    }

    private async Task KillLoopAsync(int targetOid, int spoilAttemptsSoFar, CancellationToken ct)
    {
        SetCombatPhase("in_kill_loop");
        var killStart = DateTime.UtcNow;
        var lastReattack = killStart;
        var lastRulesTick = killStart;
        var killTimeoutMs = Math.Max(5000, Settings.KillTimeoutMs);
        var reattackMs = Math.Max(200, Settings.ReattackIntervalMs);
        var killTickMs = Math.Max(50, Settings.KillPollTickMs);
        var rulesTickMs = Math.Max(0, Settings.CombatRulesTickMs);
        var spoilAttempts = spoilAttemptsSoFar;
        var spoilDone = !Settings.SpoilEnabled || Settings.SpoilSkillId <= 0;
        var spoilIntervalMs = Math.Max(500, Settings.SpoilRetryIntervalMs);
        var nextSpoilAllowedAt = spoilAttemptsSoFar > 0
            ? DateTime.UtcNow.AddMilliseconds(spoilIntervalMs)
            : DateTime.UtcNow;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var ctx = BuildContext();
                var me = ctx.World.Me;

                // Rest/sit is intentionally not started mid-fight (see AwaitRestMpIfNeededAsync + AllowRecoverySit).
                // If we are sitting anyway (sync glitch, manual sit, or client state), stand before attacking/healing.
                if (me.IsSitting)
                {
                    if (_rest.TryForceStand(ctx))
                        _log.Info($"[AutoCombat] Forcing stand — sitting during kill loop (target 0x{targetOid:X})");
                    await Task.Delay(killTickMs, ct);
                    continue;
                }

                if (_heal.TickSelfHeal(ctx, inFight: true))
                {
                    await Task.Delay(120, ct);
                    continue;
                }

                TrackIncomingDamage(ctx);

                if (IsTargetEliminated(ctx, targetOid))
                {
                    _log.Info($"[AutoCombat] Mob dead after {(DateTime.UtcNow - killStart).TotalSeconds:0.#}s");
                    break;
                }

                if ((DateTime.UtcNow - killStart).TotalMilliseconds >= killTimeoutMs)
                {
                    _log.Info($"[AutoCombat] Kill timeout ({killTimeoutMs}ms)");
                    _lastCombatTargetOid = 0;
                    break;
                }

                var now = DateTime.UtcNow;

                // Spoil retry: decoupled from CombatRulesTickMs — use SpoilRetryIntervalMs; skip injects while out of cast range (no attempt count).
                if (!spoilDone && now >= nextSpoilAllowedAt)
                {
                    if (ctx.World.Npcs.TryGetValue(targetOid, out var targetNpc) && targetNpc.SpoilSucceeded)
                    {
                        spoilDone = true;
                        _log.Info($"[AutoCombat] Spoil SUCCESS on 0x{targetOid:X} after {spoilAttempts} attempt(s)");
                    }
                    else
                    {
                        var maxSpoilAttempts = Math.Max(1, Settings.SpoilMaxAttemptsPerTarget);
                        if (Settings.SpoilOncePerTarget && spoilAttempts >= maxSpoilAttempts)
                        {
                            spoilDone = true;
                            _log.Info($"[AutoCombat] Spoil max attempts ({maxSpoilAttempts}) reached on 0x{targetOid:X}");
                        }
                        else if (!CombatService.IsWithinSpoilCastRange(ctx, me, targetOid))
                        {
                            // Still walking in — do not consume tries or spam packets.
                        }
                        else
                        {
                            nextSpoilAllowedAt = now.AddMilliseconds(spoilIntervalMs);
                            ctx = BuildContext();
                            me = ctx.World.Me;
                            if (_combat.TryCastSkill(ctx, Settings.SpoilSkillId, forBuff: false,
                                    cooldownOverrideMs: spoilIntervalMs, allowReservedSkill: true, bypassSelfCastLock: true))
                            {
                                spoilAttempts++;
                                if (spoilAttempts <= 3 || spoilAttempts % 6 == 0)
                                    _log.Info($"[AutoCombat] Spoil #{spoilAttempts} on 0x{targetOid:X}");
                            }
                        }
                    }
                }

                // Aggro retarget
                if (Settings.PreferAggroMobs)
                {
                    var aggroMob = _targeting.SelectEmergencyAggroTarget(ctx, me);
                    if (aggroMob is not null && aggroMob.ObjectId != targetOid)
                    {
                        _log.Info($"[AutoCombat] Retarget to aggro 0x{aggroMob.ObjectId:X}");
                        targetOid = aggroMob.ObjectId;
                        _lastCombatTargetOid = targetOid;
                        killStart = DateTime.UtcNow;
                        spoilAttempts = 0;
                        spoilDone = !Settings.SpoilEnabled || Settings.SpoilSkillId <= 0;
                        nextSpoilAllowedAt = DateTime.UtcNow;

                        _combat.TryInject(ctx, PacketBuilder.BuildAction(targetOid, me.X, me.Y, me.Z, 0), "retarget");
                        await Task.Delay(120, ct);

                        if (Settings.SpoilEnabled && Settings.SpoilSkillId > 0)
                        {
                            ctx = BuildContext();
                            me = ctx.World.Me;
                            if (CombatService.IsWithinSpoilCastRange(ctx, me, targetOid)
                                && _combat.TryCastSkill(ctx, Settings.SpoilSkillId, forBuff: false,
                                    cooldownOverrideMs: spoilIntervalMs, allowReservedSkill: true, bypassSelfCastLock: true))
                            {
                                spoilAttempts++;
                                nextSpoilAllowedAt = DateTime.UtcNow.AddMilliseconds(spoilIntervalMs);
                                _log.Info($"[AutoCombat] Spoil attempt #{spoilAttempts} on retarget 0x{targetOid:X}");
                            }

                            await Task.Delay(400, ct);
                        }

                        ctx = BuildContext();
                        _combat.TryUseCombatRotation(ctx, targetOid);
                        if (!IsCasterMode())
                        {
                            ctx = BuildContext();
                            _combat.TrySendAttack(ctx, ctx.World.Me, targetOid, "retarget");
                        }

                        lastReattack = DateTime.UtcNow;
                        lastRulesTick = DateTime.UtcNow;
                    }
                }

                // Periodic combat rules
                if (rulesTickMs > 0 && (now - lastRulesTick).TotalMilliseconds >= rulesTickMs)
                {
                    ctx = BuildContext();
                    _combat.TryUseCombatRotation(ctx, targetOid);
                    lastRulesTick = DateTime.UtcNow;
                }

                // Periodic re-attack
                if ((now - lastReattack).TotalMilliseconds >= reattackMs)
                {
                    if (!IsCasterMode())
                    {
                        ctx = BuildContext();
                        me = ctx.World.Me;
                        _combat.TryInject(ctx, PacketBuilder.BuildAction(targetOid, me.X, me.Y, me.Z, 0), "reattack-target");
                        await Task.Delay(120, ct);
                    }
                    ctx = BuildContext();
                    _combat.TryUseCombatRotation(ctx, targetOid);
                    if (!IsCasterMode())
                    {
                        ctx = BuildContext();
                        _combat.TrySendAttack(ctx, ctx.World.Me, targetOid, "reattack");
                    }
                    lastReattack = DateTime.UtcNow;
                }

                await Task.Delay(killTickMs, ct);
            }
        }
        finally
        {
            SetCombatPhase("post-kill");
        }
    }

    // ------------------------------------------------------------------ //
    // Recovery (ported from Python _wait_recovery / _ensure_sitting/standing)
    // ------------------------------------------------------------------ //

    /// <param name="standMpPctOverride">When set (1–100), stand up once MP% reaches this; otherwise uses <see cref="BotSettings.RecoveryStandMpPct"/> (0 = ignore MP).</param>
    private async Task RecoverAsync(int standHpPct, CancellationToken ct, int? standMpPctOverride = null)
    {
        SetCombatPhase("recovering");
        _combat.SetDecision("recovering");
        _rest.Reset();

        if (!_world.Me.IsSitting)
        {
            var ctx = BuildContext();
            _combat.TryInject(ctx, PacketBuilder.BuildActionUse(0), "recovery-sit");
            await Task.Delay(800, ct);
        }

        var maxWaitMs = Settings.RecoveryMaxWaitSec * 1000;
        var start = DateTime.UtcNow;
        var lastHp = _world.Me.CurHp;
        var mpTarget = standMpPctOverride ?? Settings.RecoveryStandMpPct;

        while (!ct.IsCancellationRequested && (DateTime.UtcNow - start).TotalMilliseconds < maxWaitMs)
        {
            await Task.Delay(1000, ct);
            var me = _world.Me;

            if (me.CurHp <= 0)
            {
                _log.Info("[AutoCombat] Recovery interrupted — died");
                break;
            }

            if (me.CurHp < lastHp)
            {
                _log.Info($"[AutoCombat] Recovery interrupted — HP dropped {lastHp}->{me.CurHp}");
                break;
            }
            lastHp = me.CurHp;

            var aggroCutoff = DateTime.UtcNow.AddMilliseconds(-Settings.IncomingDamageSitBlockMs);
            foreach (var n in _world.Npcs.Values)
            {
                if (n.IsAttackable && !n.IsDead && n.LastAggroHitAtUtc >= aggroCutoff)
                {
                    _log.Info($"[AutoCombat] Recovery interrupted — aggro mob 0x{n.ObjectId:X} detected");
                    goto exitRecovery;
                }
            }

            var hpOk = me.EffectiveMaxHp <= 0 || me.HpPct >= standHpPct;
            var mpOk = mpTarget <= 0 || me.EffectiveMaxMp <= 0 || me.MpPct >= mpTarget;
            if (hpOk && mpOk)
            {
                _log.Info($"[AutoCombat] Recovery done — HP={me.HpPct:0}% MP={me.MpPct:0}%");
                break;
            }
        }
        exitRecovery:

        await EnsureStandingAsync(ct, afterRecovery: true);
        SetCombatPhase("idle");
        await Task.Delay(500, ct);
    }

    /// <summary>
    /// Sit until MP reaches Stand MP% when Rest is enabled (Combat tab). AutoFight previously skipped <see cref="RestService"/> entirely.
    /// </summary>
    private async Task AwaitRestMpIfNeededAsync(CancellationToken ct)
    {
        if (!Settings.RestEnabled)
            return;

        var me = _world.Me;
        if (me.CurHp <= 0 || me.EffectiveMaxMp <= 0)
            return;

        var sitAt = Math.Clamp(Settings.SitMpPct, 1, 99);
        var standAt = Math.Max(sitAt + 1, Math.Clamp(Settings.StandMpPct, sitAt + 1, 100));
        if (me.MpPct > sitAt)
            return;

        if (!AllowRecoverySit())
        {
            _log.Info($"[AutoCombat] MP rest skipped (combat target alive or recent damage) mp={me.MpPct:0.#}%");
            return;
        }

        var hpStandPct = Math.Max(1, Settings.PostKillStandHpPct);
        _log.Info($"[AutoCombat] MP rest: sitting until MP>={standAt}% HP>={hpStandPct}% (now MP={me.MpPct:0.#}% HP={me.HpPct:0.#}%)");
        await RecoverAsync(hpStandPct, ct, standAt);
    }

    /// <summary>
    /// Sit/stand is toggled via ActionUse(0). If <see cref="CharacterState.IsSitting"/> is wrong (ChangeWaitType not decoded),
    /// normal stand logic never sends packets — <paramref name="afterRecovery"/> sends a short pulse sequence anyway.
    /// </summary>
    private async Task EnsureStandingAsync(CancellationToken ct, bool afterRecovery = false)
    {
        var maxAttempts = afterRecovery
            ? Math.Max(6, Settings.RecoveryStandToggleAttempts)
            : Math.Max(1, Math.Min(4, Settings.RecoveryStandToggleAttempts));

        for (var i = 0; i < maxAttempts && !ct.IsCancellationRequested; i++)
        {
            var sitting = _world.Me.IsSitting;
            if (!afterRecovery)
            {
                if (!sitting)
                    return;
            }
            else if (!sitting && i >= 2)
            {
                return;
            }

            if (!afterRecovery)
                _log.Info("[AutoCombat] Standing up");
            else if (i == 0 || sitting)
                _log.Info($"[AutoCombat] Recovery exit: stand toggle {i + 1}/{maxAttempts} (parsed sitting={sitting})");

            var ctx = BuildContext();
            _combat.TryInject(ctx, PacketBuilder.BuildActionUse(0), afterRecovery ? $"recovery-stand-{i}" : $"stand-{i}");
            await Task.Delay(afterRecovery ? 950 : 800, ct);

            if (!_world.Me.IsSitting)
                return;
        }
    }

    private bool AllowRecoverySit()
    {
        if (_lastIncomingDamageAtUtc != DateTime.MinValue
            && (DateTime.UtcNow - _lastIncomingDamageAtUtc).TotalMilliseconds < Settings.IncomingDamageSitBlockMs)
            return false;

        var tid = _world.Me.TargetId;
        if (tid != 0 && _world.Npcs.TryGetValue(tid, out var npc) && npc.IsAttackable && !npc.IsDead)
            return false;

        var aggroCutoff = DateTime.UtcNow.AddMilliseconds(-Settings.IncomingDamageSitBlockMs);
        var fightRangeSq = (long)Settings.FightRange * Settings.FightRange;
        foreach (var n in _world.Npcs.Values)
        {
            if (n.IsAttackable && !n.IsDead
                && n.LastAggroHitAtUtc >= aggroCutoff
                && CombatService.DistanceSq(_world.Me.X, _world.Me.Y, n.X, n.Y) <= fightRangeSq)
                return false;
        }

        return true;
    }

    // ------------------------------------------------------------------ //
    // Loot (sequential async burst)
    // ------------------------------------------------------------------ //

    private async Task LootNearbyAsync(CancellationToken ct, int maxAttempts = 56, bool walkToItems = true)
    {
        var me = _world.Me;
        if (me.IsSitting || me.CurHp <= 0 || !Settings.AutoLoot) return;

        var searchRange = Math.Max(Settings.LootRange, Settings.LootPickupRange);
        var searchRangeSq = (long)searchRange * searchRange;
        var pickupRange = Math.Max(70, Settings.LootPickupRange);
        var pickupRangeSq = (long)pickupRange * pickupRange;
        var corpseLinkRadius = Math.Max(Math.Max(searchRange, 380), Settings.LootPickupRange) + 440;
        var corpseLinkRadiusSq = (long)corpseLinkRadius * corpseLinkRadius;

        var attempts = 0;
        var emptyPolls = 0;
        var picked = 0;
        var perOidTries = new Dictionary<int, int>();
        var loosenCorpseAnchor = false;
        var loggedCorpseStacks = false;

        while (attempts < maxAttempts && !ct.IsCancellationRequested)
        {
            me = _world.Me;
            var ax = 0;
            var ay = 0;
            var hasCorpseAnchor = !loosenCorpseAnchor
                && _world.TryGetLootCorpseAnchor(TimeSpan.FromSeconds(14), out ax, out ay, out _);
            var maxAnchorPlayer = Math.Max(Settings.LootRange, Settings.FightRange);
            var maxAnchorPlayerSq = (long)maxAnchorPlayer * maxAnchorPlayer;
            var anchorTooFarForWalk = walkToItems && hasCorpseAnchor
                && CombatService.DistanceSq(me.X, me.Y, ax, ay) > maxAnchorPlayerSq;
            var useCorpseAnchor = hasCorpseAnchor && !anchorTooFarForWalk;

            List<GroundItemState> pool;
            if (useCorpseAnchor)
            {
                pool = _world.Items.Values
                    .Where(x => CombatService.DistanceSq(ax, ay, x.X, x.Y) <= corpseLinkRadiusSq)
                    .ToList();
                if (!loggedCorpseStacks && pool.Count > 0)
                {
                    loggedCorpseStacks = true;
                    _log.Info($"[AutoCombat] Loot: {pool.Count} ground stack(s) near last kill (~{corpseLinkRadius}u)");
                }
            }
            else
            {
                pool = _world.Items.Values
                    .Where(x => CombatService.DistanceSq(me.X, me.Y, x.X, x.Y) <= searchRangeSq)
                    .ToList();
            }

            if (!walkToItems)
            {
                var beforeFilter = pool.Count;
                pool = pool
                    .Where(x => CombatService.DistanceSq(me.X, me.Y, x.X, x.Y) <= pickupRangeSq)
                    .ToList();
                if (beforeFilter > 0 && pool.Count == 0)
                    break;
            }

            var item = pool.Count == 0
                ? null
                : pool.MinBy(x => CombatService.DistanceSq(me.X, me.Y, x.X, x.Y));

            var emptyLimit = useCorpseAnchor ? 18 : 6;
            if (item is null)
            {
                emptyPolls++;
                if (useCorpseAnchor && picked == 0 && emptyPolls >= 16)
                {
                    loosenCorpseAnchor = true;
                    emptyPolls = 0;
                }
                if (emptyPolls >= emptyLimit)
                    break;
                await Task.Delay(useCorpseAnchor && picked == 0 ? 42 : 55, ct);
                continue;
            }

            if (!useCorpseAnchor && CombatService.DistanceSq(me.X, me.Y, item.X, item.Y) > searchRangeSq)
            {
                emptyPolls++;
                if (emptyPolls >= emptyLimit)
                    break;
                await Task.Delay(55, ct);
                continue;
            }

            emptyPolls = 0;
            var oid = item.ObjectId;
            var tries = perOidTries.GetValueOrDefault(oid);
            if (tries >= 2)
            {
                _world.Items.TryRemove(oid, out _);
                perOidTries.Remove(oid);
                continue;
            }

            var distSq = CombatService.DistanceSq(me.X, me.Y, item.X, item.Y);
            if (distSq > pickupRangeSq)
            {
                if (!walkToItems || !Settings.MoveToLoot)
                {
                    attempts++;
                    if (!walkToItems)
                        continue;
                    _world.Items.TryRemove(oid, out _);
                    continue;
                }

                var ctx = BuildContext();
                _combat.TryMoveTo(ctx, item.X, item.Y, item.Z);
                var dist = Math.Sqrt(distSq);
                var walkDelay = Math.Min(800, Math.Max(200, (int)(dist / 2.6)));
                await Task.Delay(walkDelay, ct);
                attempts++;
                continue;
            }

            var ctx2 = BuildContext();
            var sent = _combat.TryInject(ctx2, PacketBuilder.BuildAction(oid, me.X, me.Y, me.Z, 0), "loot-pickup");
            picked++;
            attempts++;

            if (sent)
                await WaitForGroundItemRemovedAsync(oid, maxWaitMs: 520, ct);
            else
                await Task.Delay(65, ct);

            if (!_world.Items.ContainsKey(oid))
            {
                perOidTries.Remove(oid);
                await Task.Delay(22, ct);
                continue;
            }

            perOidTries[oid] = tries + 1;
            await Task.Delay(85, ct);
        }

        if (picked > 0)
            _log.Info($"[AutoCombat] Loot: {picked} pickup injects");
    }

    /// <summary>
    /// After a pickup action, the client usually receives DeleteObject for the ground item.
    /// Without this wait we spammed the same stack up to 4× when removal was slower than the fixed 250ms delay.
    /// </summary>
    private async Task WaitForGroundItemRemovedAsync(int objectId, int maxWaitMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(80, maxWaitMs));
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (!_world.Items.ContainsKey(objectId))
                return;
            await Task.Delay(18, ct);
        }
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private bool IsCasterMode()
        => Settings.BattleMode == BotBattleMode.StrictCaster || Settings.Role == BotRole.CasterDD;

    private bool HasWorldContext()
        => _world.EnteredWorld || _world.Me.ObjectId != 0 || !_world.Npcs.IsEmpty || !_world.Items.IsEmpty;

    private static bool IsTargetEliminated(BotContext ctx, int objectId)
    {
        if (objectId == 0) return true;
        if (!ctx.World.Npcs.TryGetValue(objectId, out var npc)) return true;
        return npc.IsDead || npc.HpPct <= 0.01f;
    }

    private bool IsInCombatFlow()
        => _combatPhase is "in_kill_loop" or "targeting" or "post-kill";

    private BotContext BuildContext()
        => new(_world, Settings, DateTime.UtcNow, _proxy, _log);

    private void TrackIncomingDamage(BotContext ctx)
    {
        var me = ctx.World.Me;
        if (_lastObservedSelfHp >= 0 && me.CurHp < _lastObservedSelfHp)
        {
            _lastIncomingDamageAtUtc = ctx.Now;
            if (!_criticalHoldActive)
            {
                var enterPct = Math.Max(5, Settings.CriticalHoldEnterHpPct);
                if (me.HpPct <= enterPct)
                {
                    _criticalHoldActive = true;
                    _log.Info($"[Bot] critical hold ENTER hp={me.HpPct:0.#}%");
                }
            }
        }

        if (_criticalHoldActive)
        {
            var resumePct = Math.Max(Settings.CriticalHoldEnterHpPct + 5, Settings.CriticalHoldResumeHpPct);
            if (me.HpPct >= resumePct)
            {
                _criticalHoldActive = false;
                _log.Info($"[Bot] critical hold RESUME hp={me.HpPct:0.#}%");
            }
        }

        _lastObservedSelfHp = me.CurHp;
    }

    private void UpdateCombatPhaseFromRole(TickResult result)
    {
        switch (result)
        {
            case TickResult.Yielded:  SetCombatPhase("role-active"); break;
            case TickResult.Blocked:  SetCombatPhase("role-blocked"); break;
            case TickResult.Continue: SetCombatPhase("idle"); break;
        }
    }

    private void SyncActiveRole()
    {
        var targetRole = Settings.Role switch
        {
            BotRole.CasterDD => (IBotRole)_casterDdRole,
            BotRole.Spoiler => _spoilerRole,
            BotRole.Healer => _healerRole,
            BotRole.Buffer => _bufferRole,
            _ => _meleeDdRole
        };

        if (ReferenceEquals(_activeRole, targetRole))
            return;

        _activeRole.Reset();
        _activeRole = targetRole;
        _activeRole.Reset();
        _log.Info($"[Bot] role switched to {Settings.Role}");
    }

    // ------------------------------------------------------------------ //
    // Coordinator
    // ------------------------------------------------------------------ //

    private void TickCoordinatorLeader(BotContext ctx)
    {
        if (!Settings.EnableRoleCoordinator || Settings.CoordMode != CoordMode.CoordinatorLeader)
            return;

        var me = ctx.World.Me;
        _coordinatorSequence++;
        _coordinator.Publish(new CombatIntent
        {
            LeaderObjectId = me.ObjectId,
            LeaderTargetOid = me.TargetId,
            LeaderX = me.X,
            LeaderY = me.Y,
            LeaderZ = me.Z,
            CombatState = _combatPhase,
            PullTimestampUnixMs = new DateTimeOffset(ctx.Now).ToUnixTimeMilliseconds(),
            Sequence = _coordinatorSequence
        });
    }

    private void TickCoordinatorFollower(BotContext ctx)
    {
        if (!Settings.EnableRoleCoordinator || Settings.CoordMode != CoordMode.CoordinatorFollower)
            return;

        if (!_coordinator.TryGetLatestIntent(Settings.CoordinatorStaleMs, out var intent))
            return;

        var me = ctx.World.Me;
        var now = ctx.Now;
        if (now < _nextCoordinatorFollowAt)
            return;

        var followDist = Math.Max(120, Settings.FollowDistance);
        var tolerance = Math.Max(30, Settings.FollowTolerance);
        var keepDist = followDist + tolerance;
        var keepDistSq = (long)keepDist * keepDist;
        var distSq = CombatService.DistanceSq(me.X, me.Y, intent.LeaderX, intent.LeaderY);

        if (distSq <= keepDistSq)
            return;

        if (_combat.TryMoveTo(ctx, intent.LeaderX, intent.LeaderY, intent.LeaderZ))
        {
            _nextCoordinatorFollowAt = now.AddMilliseconds(Math.Max(160, Settings.FollowRepathIntervalMs));
            SetCombatPhase("follower-follow-leader");
            _combat.SetDecision("follower-follow-leader");
        }
    }

    // ------------------------------------------------------------------ //
    // Infrastructure
    // ------------------------------------------------------------------ //

    private void ResetAllServices()
    {
        _combat.ResetTimers();
        _heal.Reset();
        _buff.Reset();
        _recharge.Reset();
        _loot.Reset();
        _postKill.Reset();
        _rest.Reset();
        _activeRole.Reset();
        _criticalHoldActive = false;
        _lastObservedSelfHp = -1;
        _lastIncomingDamageAtUtc = DateTime.MinValue;
        _lastCombatTargetOid = 0;
        _combatPhase = "idle";
    }

    private void SetCombatPhase(string phase)
    {
        if (string.Equals(_combatPhase, phase, StringComparison.Ordinal))
            return;

        _combatPhase = phase;
        var now = DateTime.UtcNow;
        if (now < _nextPhaseDiagLogAt)
            return;

        _nextPhaseDiagLogAt = now.AddMilliseconds(650);
        _log.Info($"[AutoFight] phase => {phase}");
    }

    private void EnsureCoordinatorMode()
    {
        var enabled = Settings.EnableRoleCoordinator;
        var mode = enabled ? Settings.CoordMode : CoordMode.Standalone;
        _coordinator.Configure(enabled, mode, Settings.CoordinatorChannel);
    }

    private bool IsProxyReady()
    {
        var d = _proxy.Diagnostics;
        return _proxy.IsRunning
            && d.GameClientConnected
            && d.GameServerConnected
            && d.GameCryptoReady;
    }

    private void RefreshRuntimeState()
    {
        if (_cts is null)
        {
            if (_runtimeState != BotRuntimeState.Stopped)
                TransitionRuntime(BotRuntimeState.Stopped, "loop-without-cts");
            return;
        }

        if (IsProxyReady())
        {
            if (_runtimeState != BotRuntimeState.Running)
                TransitionRuntime(BotRuntimeState.Running, "game-crypto-ready");
            return;
        }

        if (_runtimeState != BotRuntimeState.PausedNoSession)
            TransitionRuntime(BotRuntimeState.PausedNoSession, "game-disconnected");
    }

    private void TransitionRuntime(BotRuntimeState next, string reason, bool logTransition = true)
    {
        if (_runtimeState == next && string.Equals(_runtimeReason, reason, StringComparison.Ordinal))
            return;

        _runtimeState = next;
        _runtimeReason = reason;
        _lastRuntimeTransitionAtUtc = DateTime.UtcNow;

        if (logTransition)
            _log.Info($"Bot runtime => {next} ({reason})");
    }
}

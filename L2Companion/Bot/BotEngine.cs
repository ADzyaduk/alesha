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

    private DateTime _nextBuffAt = DateTime.MinValue;
    private DateTime _nextSelfHealAt = DateTime.MinValue;
    private DateTime _nextGroupHealAt = DateTime.MinValue;
    private DateTime _nextPartyHealAt = DateTime.MinValue;
    private DateTime _nextFightActionAt = DateTime.MinValue;
    private DateTime _nextLootAt = DateTime.MinValue;
    private DateTime _nextMoveAt = DateTime.MinValue;
    private DateTime _nextRestToggleAt = DateTime.MinValue;
    private DateTime _restExpectedStateUntilUtc = DateTime.MinValue;
    private bool? _restExpectedIsSitting;
    private DateTime _nextForceAttackAt = DateTime.MinValue;
    private DateTime _nextTargetActionAt = DateTime.MinValue;
    private DateTime _nextCompatAttackRequestAt = DateTime.MinValue;
    private DateTime _preferAttackRequestUntilUtc = DateTime.MinValue;
    private DateTime _nextTransportSwitchAtUtc = DateTime.MinValue;
    private DateTime _nextSpoilAt = DateTime.MinValue;
    private DateTime _nextSweepAt = DateTime.MinValue;
    private DateTime _nextEmergencyRetaliateAt = DateTime.MinValue;

    private readonly Dictionary<int, DateTime> _nextSkillById = [];
    private readonly Dictionary<string, DateTime> _nextPartyHealRuleAt = [];
    private readonly Dictionary<string, DateTime> _nextBuffRuleAt = [];
    private int _lastAutoTargetObjectId;
    private string _lastDecision = "idle";
    private DateTime _lastDecisionAtUtc = DateTime.UtcNow;
    private BotCommandResult _lastCommand = BotCommandResult.Deferred("init", "not-started");
    private DateTime _nextCommandDiagLogAt = DateTime.MinValue;
    private DateTime _nextSentCommandDiagLogAt = DateTime.MinValue;
    private DateTime _nextPhaseDiagLogAt = DateTime.MinValue;

    private BotRuntimeState _runtimeState = BotRuntimeState.Stopped;
    private string _runtimeReason = "stopped";
    private DateTime _lastRuntimeTransitionAtUtc = DateTime.UtcNow;

    private int _combatTargetObjectId;
    private float _combatTargetLastHpPct = -1f;
    private DateTime _combatTargetAssignedAtUtc = DateTime.MinValue;
    private DateTime _combatLastProgressAtUtc = DateTime.MinValue;
    private string _combatPhase = "idle";
    private int _lastObservedSelfHp = -1;
    private DateTime _lastIncomingDamageAtUtc = DateTime.MinValue;
    private int _assumedTargetObjectId;
    private DateTime _assumedTargetUntilUtc = DateTime.MinValue;
    private int _spoilPendingTargetObjectId;
    private DateTime _spoilPendingUntilUtc = DateTime.MinValue;

    private DateTime _combatTargetLastSeenAtUtc = DateTime.MinValue;
    private int _combatTargetLastX;
    private int _combatTargetLastY;
    private int _combatTargetLastZ;
    private int _combatTargetNpcTypeId;
    private bool _combatTargetSpoilSucceeded;
    private DateTime _combatTargetLastHitByMeAtUtc = DateTime.MinValue;
    private bool _combatTargetHadCombatSignal;

    private bool _postKillActive;
    private int _postKillTargetObjectId;
    private int _postKillNpcTypeId;
    private int _postKillX;
    private int _postKillY;
    private int _postKillZ;
    private bool _postKillSpoilSucceeded;
    private DateTime _postKillStartedAtUtc = DateTime.MinValue;
    private DateTime _postKillSweepUntilUtc = DateTime.MinValue;
    private DateTime _postKillSpawnWaitUntilUtc = DateTime.MinValue;
    private DateTime _postKillMinWaitForLootUntilUtc = DateTime.MinValue;
    private DateTime _nextPostKillActionAt = DateTime.MinValue;
    private int _postKillLootActions;
    private int _postKillEmptyPolls;
    private int _postKillItemsSeen;
    private int _postKillItemsPicked;
    private int _postKillItemsSkipped;
    private bool _postKillTargetCanceled;
    private int _postKillSweepAttempts;
    private int _postKillMoveToCorpseAttempts;
    private bool _postKillReachedCorpseZone;
    private readonly HashSet<int> _postKillSeenItemIds = [];
    private readonly Dictionary<int, int> _postKillLootItemAttempts = [];
    private readonly Dictionary<int, DateTime> _spoilAttemptLocalByTarget = [];
    private readonly Dictionary<int, int> _spoilAttemptCountByTarget = [];
    private readonly CombatCoordinator _coordinator;
    private DateTime _nextCoordinatorFollowAt = DateTime.MinValue;
    private long _coordinatorSequence;
    private readonly Dictionary<int, DateTime> _recentPostKillByTarget = [];
    private readonly Dictionary<int, DateTime> _temporarilyIgnoredTargets = [];
    private int _combatNoProgressStrikes;
    private int _killTimeoutFireCount;
    private DateTime _casterChaseStartedAtUtc = DateTime.MinValue;
    private int _casterStuckStrikes;
    private readonly Dictionary<int, int> _stallRetargetCountByTarget = [];
    private DateTime _combatNoServerTargetSinceUtc = DateTime.MinValue;
    private int _targetConfirmRecoveryStage;
    private DateTime _targetConfirmRecoveryStartedAtUtc = DateTime.MinValue;
    private DateTime _nextAutoFightDisabledLogAt = DateTime.MinValue;
    private bool _criticalHoldActive;
    private DateTime _nextIdleNoTargetLogAt = DateTime.MinValue;
    private bool _deadStopActive;
    private const double AggroRecentWindowSec = 18.0;

    public BotEngine(ProxyService proxy, GameWorldState world, LogService log)
    {
        _proxy = proxy;
        _world = world;
        _log = log;
        _coordinator = new CombatCoordinator(log);
    }

    public bool IsRunning => _runtimeState != BotRuntimeState.Stopped;
    public BotRuntimeState RuntimeState => _runtimeState;
    public BotSettings Settings { get; } = new();

    public string GetDecisionSummary()
    {
        var age = (DateTime.UtcNow - _lastDecisionAtUtc).TotalSeconds;

        var d = _proxy.Diagnostics;
        return $"{_lastDecision}  ({age:0.0}s)  Phase:{_combatPhase}  Cmd:{_lastCommand.Status}/{_lastCommand.Action}  Inject:{d.InjectPackets} Pending:{d.PendingInjectPackets} Target:0x{_world.Me.TargetId:X}";
    }

    public string GetLastCommandTrace() => _lastCommand.ToString();

    public string GetRuntimeSummary()
    {
        var age = (DateTime.UtcNow - _lastRuntimeTransitionAtUtc).TotalSeconds;
        var d = _proxy.Diagnostics;
        return $"{_runtimeState} ({age:0.0}s) reason={_runtimeReason} stage={d.SessionStage}";
    }

    public string GetCombatSummary()
    {
        var progressAge = _combatLastProgressAtUtc == DateTime.MinValue
            ? -1
            : (DateTime.UtcNow - _combatLastProgressAtUtc).TotalSeconds;

        var assignedAge = _combatTargetAssignedAtUtc == DateTime.MinValue
            ? -1
            : (DateTime.UtcNow - _combatTargetAssignedAtUtc).TotalSeconds;

        return $"phase={_combatPhase} target=0x{_combatTargetObjectId:X} assigned={assignedAge:0.0}s progress={progressAge:0.0}s";
    }

    public void Start()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        ClearSpoilPendingTarget();
        ResetPostKillState();
        ResetCombatState();
        EnsureCoordinatorMode();
        TransitionRuntime(IsProxyReady() ? BotRuntimeState.Running : BotRuntimeState.PausedNoSession, IsProxyReady() ? "start-ready" : "start-no-session", logTransition: false);
        _loopTask = Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
        _log.Info("Bot engine started.");
        _log.Info($"[AutoFight] config AutoFight={Settings.AutoFight} Role={Settings.Role} Mode={Settings.BattleMode} Coord={Settings.CoordMode} Spoil={Settings.SpoilEnabled} Sweep={Settings.SweepEnabled}");
    }
    public void Stop()
    {
        if (_cts is null)
        {
            return;
        }

        var cts = _cts;
        var loopTask = _loopTask;
        _cts = null;
        _loopTask = null;

        try
        {
            cts.Cancel();
        }
        catch
        {
            // ignored
        }

        try
        {
            loopTask?.Wait(600);
        }
        catch
        {
            // loop is cancellation-driven; no error for Stop path
        }

        cts.Dispose();
        _lastAutoTargetObjectId = 0;
        ClearSpoilPendingTarget();
        ResetPostKillState();
        ResetCombatState();
        try
        {
            _coordinator.Configure(false, CoordMode.Standalone, Settings.CoordinatorChannel);
        }
        catch
        {
            // ignored
        }
        TransitionRuntime(BotRuntimeState.Stopped, "manual-stop", logTransition: false);
        _log.Info("Bot engine stopped.");
    }
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
                    SetDecision("paused-no-session");
                    await Task.Delay(220, ct);
                    continue;
                }

                var hasWorldContext = _world.EnteredWorld
                    || _world.Me.ObjectId != 0
                    || !_world.Npcs.IsEmpty
                    || !_world.Items.IsEmpty;

                if (hasWorldContext)
                {
                    if (TickDeadStop())
                    {
                        await Task.Delay(220, ct);
                        continue;
                    }

                    var selfHealTriggered = TickAutoHeal();
                    TickPartyHeal();

                    if (selfHealTriggered)
                    {
                        await Task.Delay(120, ct);
                        continue;
                    }

                    if (TickMpRest())
                    {
                        await Task.Delay(140, ct);
                        continue;
                    }

                    TickAutoBuff();
                    TickAutoFight();
                    TickAutoLoot();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Info($"Bot tick error: {ex.Message}");
            }

            try
            {
                await Task.Delay(180, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private bool TickDeadStop()
    {
        var me = _world.Me;
        var isDead = me.CurHp <= 0 || me.MaxHp <= 0;
        var now = DateTime.UtcNow;

        if (isDead)
        {
            if (!_deadStopActive)
            {
                _deadStopActive = true;
                ResetPostKillState();
                ResetCombatState();
                _nextBuffAt = DateTime.MinValue;
                _nextSelfHealAt = DateTime.MinValue;
                _nextGroupHealAt = DateTime.MinValue;
                _nextPartyHealAt = DateTime.MinValue;
                _nextLootAt = DateTime.MinValue;
                _nextMoveAt = DateTime.MinValue;
                _nextFightActionAt = DateTime.MinValue;
                SetDecision("dead-stop-enter");
            }

            SetCombatPhase("dead-wait-res");
            SetDecision("dead-wait-res");
            return true;
        }

        if (!_deadStopActive)
        {
            return false;
        }

        var resumeHpPct = Math.Max(1, Math.Min(99, Settings.DeadStopResumeHpPct));
        if (me.HpPct < resumeHpPct)
        {
            SetCombatPhase("dead-wait-res");
            SetDecision($"dead-wait-res hp={me.HpPct:0}% need={resumeHpPct}%");
            return true;
        }

        if (!IsProxyReady())
        {
            SetCombatPhase("dead-wait-res");
            SetDecision("dead-wait-res proxy-not-ready");
            return true;
        }

        _deadStopActive = false;
        _nextFightActionAt = now;
        _nextLootAt = now;
        _nextBuffAt = now;
        _nextPartyHealAt = now;
        _nextSelfHealAt = now;
        SetCombatPhase("acquire");
        SetDecision($"dead-resume hp={me.HpPct:0}%");
        return false;
    }

    private bool TickAutoHeal()
    {
        if (!Settings.AutoHeal)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (now < _nextSelfHealAt)
        {
            return false;
        }

        var me = _world.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
        {
            return false;
        }

        var hp = me.HpPct;
        var inFight = _combatTargetObjectId != 0 || _postKillActive || HasRecentIncomingDamage(now, 2200);

        var dynamicRules = Settings.HealRules
            .Where(x => x.Enabled && x.SkillId > 0 && x.HpBelowPct > 0)
            .OrderBy(x => x.HpBelowPct)
            .ToList();

        foreach (var rule in dynamicRules)
        {
            if (hp > rule.HpBelowPct)
            {
                continue;
            }

            if (rule.MinMpPct > 0 && me.MpPct < rule.MinMpPct)
            {
                continue;
            }

            if (!rule.InFight && inFight)
            {
                continue;
            }

            if (!TryCastSkill(rule.SkillId, forBuff: true, cooldownOverrideMs: Math.Max(250, rule.CooldownMs)))
            {
                continue;
            }

            _nextSelfHealAt = now.AddMilliseconds(Math.Max(320, rule.CooldownMs / 3));
            SetDecision($"self-heal-rule:{rule.SkillId}");
            return true;
        }

        if (hp > Settings.HealHpThreshold)
        {
            return false;
        }

        var fallbackSelfHealSkillId = ResolveSelfPreservationSkillId();
        if (fallbackSelfHealSkillId <= 0)
        {
            return false;
        }

        if (!TryCastSkill(fallbackSelfHealSkillId, forBuff: true, cooldownOverrideMs: 900))
        {
            return false;
        }

        _nextSelfHealAt = now.AddMilliseconds(900);
        SetDecision($"self-heal-fallback:{fallbackSelfHealSkillId}");
        return true;
    }

    private void TickPartyHeal()
    {
        if (!Settings.AutoHeal || !Settings.PartySupportEnabled || _criticalHoldActive)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now < _nextPartyHealAt)
        {
            return;
        }

        var me = _world.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
        {
            return;
        }

        var inFight = _combatTargetObjectId != 0 || _postKillActive || HasRecentIncomingDamage(now, 2200);
        var rules = GetEffectivePartyHealRules();
        if (rules.Count == 0)
        {
            return;
        }

        var party = _world.Party.Values.ToList();
        if (party.Count == 0)
        {
            return;
        }

        foreach (var rule in rules)
        {
            if (!rule.Enabled || rule.SkillId <= 0)
            {
                continue;
            }

            if (!rule.InFight && inFight)
            {
                continue;
            }

            if (rule.MinMpPct > 0 && me.MpPct < rule.MinMpPct)
            {
                continue;
            }

            var threshold = Math.Max(1, Math.Min(99, rule.HpBelowPct));
            var lowMembers = party.Where(x => x.HpPct > 0 && x.HpPct < threshold).OrderBy(x => x.HpPct).ToList();
            if (lowMembers.Count == 0)
            {
                continue;
            }

            var ruleKey = $"{rule.Mode}:{rule.SkillId}:{threshold}";
            if (_nextPartyHealRuleAt.TryGetValue(ruleKey, out var nextRuleAt) && now < nextRuleAt)
            {
                continue;
            }

            if (rule.Mode == PartyHealMode.Target)
            {
                var targetMember = lowMembers[0];
                if (targetMember.ObjectId == 0)
                {
                    continue;
                }

                if (me.TargetId != targetMember.ObjectId)
                {
                    if (now < _nextTargetActionAt)
                    {
                        continue;
                    }

                    if (!TryInject(PacketBuilder.BuildAction(targetMember.ObjectId, me.X, me.Y, me.Z, 0), "party-heal-target-confirm"))
                    {
                        continue;
                    }

                    _nextTargetActionAt = now.AddMilliseconds(520);
                    _nextPartyHealAt = now.AddMilliseconds(180);
                    SetDecision($"party-heal-target-confirm:0x{targetMember.ObjectId:X}");
                    return;
                }
            }

            var ruleCooldown = Math.Max(320, rule.CooldownMs);
            if (!TryCastSkill(rule.SkillId, forBuff: true, cooldownOverrideMs: ruleCooldown))
            {
                continue;
            }

            _nextPartyHealRuleAt[ruleKey] = now.AddMilliseconds(ruleCooldown);
            _nextPartyHealAt = now.AddMilliseconds(220);
            _nextGroupHealAt = _nextPartyHealAt;
            SetDecision($"party-heal:{rule.Mode}:{rule.SkillId}");
            return;
        }
    }

    private List<PartyHealRuleSetting> GetEffectivePartyHealRules()
    {
        var configured = Settings.PartyHealRules
            .Where(x => x.SkillId > 0)
            .ToList();

        if (configured.Count > 0)
        {
            return configured;
        }

        if (Settings.GroupHealSkillId <= 0)
        {
            return [];
        }

        return
        [
            new PartyHealRuleSetting
            {
                SkillId = Settings.GroupHealSkillId,
                Mode = PartyHealMode.Group,
                HpBelowPct = Math.Max(1, Math.Min(99, Settings.PartyHealHpThreshold)),
                MinMpPct = 0,
                CooldownMs = 1200,
                InFight = true,
                Enabled = true
            }
        ];
    }

    private int ResolveSelfPreservationSkillId()
    {
        if (Settings.SelfHealSkillId > 0 && _world.Skills.ContainsKey(Settings.SelfHealSkillId))
        {
            return Settings.SelfHealSkillId;
        }

        if (Settings.SelfHealSkillId > 0)
        {
            // Keep explicit self-heal as fallback even if skill list parsing is incomplete.
            return Settings.SelfHealSkillId;
        }

        var healRuleSkillId = Settings.HealRules
            .Where(x => x.Enabled && x.SkillId > 0 && _world.Skills.ContainsKey(x.SkillId))
            .OrderBy(x => x.HpBelowPct)
            .Select(x => x.SkillId)
            .FirstOrDefault();
        if (healRuleSkillId > 0)
        {
            return healRuleSkillId;
        }

        var fallbackHealRuleSkillId = Settings.HealRules
            .Where(x => x.Enabled && x.SkillId > 0)
            .OrderBy(x => x.HpBelowPct)
            .Select(x => x.SkillId)
            .FirstOrDefault();
        if (fallbackHealRuleSkillId > 0)
        {
            return fallbackHealRuleSkillId;
        }

        var partyRuleSkillId = GetEffectivePartyHealRules()
            .Where(x => x.Enabled && x.SkillId > 0 && _world.Skills.ContainsKey(x.SkillId))
            .Select(x => x.SkillId)
            .FirstOrDefault();
        if (partyRuleSkillId > 0)
        {
            return partyRuleSkillId;
        }

        var fallbackPartyRuleSkillId = GetEffectivePartyHealRules()
            .Where(x => x.Enabled && x.SkillId > 0)
            .Select(x => x.SkillId)
            .FirstOrDefault();
        // Teon mage fallback when profile has no explicit heal rule yet.
        if (_world.Me.ClassId == 10)
        {
            return 1015;
        }

        return fallbackPartyRuleSkillId;
    }
    private List<BuffRuleSetting> GetEffectiveBuffRules()
    {
        var configured = Settings.BuffRules
            .Where(x => x.SkillId > 0)
            .ToList();

        if (configured.Count > 0)
        {
            return configured;
        }

        if (Settings.BuffSkillId <= 0)
        {
            return [];
        }

        return
        [
            new BuffRuleSetting
            {
                SkillId = Settings.BuffSkillId,
                Scope = Settings.GroupBuff ? BuffTargetScope.Both : BuffTargetScope.Self,
                AutoDetect = true,
                DelaySec = 18,
                MinMpPct = 0,
                InFight = true,
                Enabled = true
            }
        ];
    }

    private void TickAutoBuff()
    {
        if (!Settings.AutoBuff)
        {
            return;
        }

        var me = _world.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
        {
            return;
        }

        if (me.IsSitting)
        {
            SetDecision("buff-paused-sitting");
            return;
        }

        if (_criticalHoldActive
            || _postKillActive
            || string.Equals(_combatPhase, "target-confirm", StringComparison.Ordinal)
            || string.Equals(_combatPhase, "post-kill-sweep", StringComparison.Ordinal))
        {
            SetDecision("buff-paused-combat-phase");
            return;
        }

        var now = DateTime.UtcNow;
        if (now < _nextBuffAt)
        {
            return;
        }

        var inFight = _combatTargetObjectId != 0 || _postKillActive || HasRecentIncomingDamage(now, 2200);
        var hasFreshAbnormal = me.AbnormalUpdatedAtUtc != DateTime.MinValue && (now - me.AbnormalUpdatedAtUtc).TotalSeconds <= 90;
        var rules = GetEffectiveBuffRules();
        if (rules.Count == 0)
        {
            return;
        }

        foreach (var rule in rules)
        {
            if (!rule.Enabled || rule.SkillId <= 0)
            {
                continue;
            }

            if (!rule.InFight && inFight)
            {
                continue;
            }

            if (rule.MinMpPct > 0 && me.MpPct < rule.MinMpPct)
            {
                continue;
            }

            var needsParty = rule.Scope is BuffTargetScope.Party or BuffTargetScope.Both;
            if (needsParty && _world.Party.IsEmpty)
            {
                if (rule.Scope == BuffTargetScope.Party)
                {
                    continue;
                }
            }

            var delaySec = Math.Max(6, rule.DelaySec);
            var ruleKey = $"{rule.SkillId}:{rule.Scope}:{rule.AutoDetect}:{delaySec}";
            if (_nextBuffRuleAt.TryGetValue(ruleKey, out var nextRuleAt) && now < nextRuleAt)
            {
                continue;
            }

            var canDetectSelf = hasFreshAbnormal && rule.Scope is BuffTargetScope.Self or BuffTargetScope.Both;
            if (rule.AutoDetect && canDetectSelf && me.AbnormalEffectSkillIds.Contains(rule.SkillId))
            {
                _nextBuffRuleAt[ruleKey] = now.AddSeconds(delaySec);
                _nextBuffAt = now.AddMilliseconds(220);
                SetDecision($"buff-active:{rule.SkillId}");
                continue;
            }

            // Self-buff: target yourself first so the server applies the buff to self, not to a mob
            if (rule.Scope is BuffTargetScope.Self or BuffTargetScope.Both && me.ObjectId != 0 && me.TargetId != me.ObjectId)
            {
                TryInject(PacketBuilder.BuildAction(me.ObjectId, me.X, me.Y, me.Z, 0), $"self-target-buff:{rule.SkillId}");
            }

            if (!TryCastSkill(rule.SkillId, forBuff: true, cooldownOverrideMs: 1200))
            {
                _nextBuffRuleAt[ruleKey] = now.AddMilliseconds(1000);
                _nextBuffAt = now.AddMilliseconds(420);
                continue;
            }

            _nextBuffRuleAt[ruleKey] = now.AddSeconds(delaySec);
            _nextBuffAt = now.AddMilliseconds(320);
            SetDecision($"buff-cast:{rule.SkillId}:{rule.Scope}");
            return;
        }
    }

    private void TickAutoFight()
    {
        var now = DateTime.UtcNow;
        if (!Settings.AutoFight)
        {
            if (now >= _nextAutoFightDisabledLogAt)
            {
                _nextAutoFightDisabledLogAt = now.AddSeconds(4);
                SetCombatPhase("autofight-disabled");
                SetDecision("autofight-disabled");
                _log.Info("[AutoFight] skipped: AutoFight=false");
            }

            _nextFightActionAt = now.AddMilliseconds(280);
            return;
        }

        var me = _world.Me;
        var criticalEnterHpPct = Math.Max(10, Math.Min(95, Settings.CriticalHoldEnterHpPct));
        var isCriticalHp = me.MaxHp > 0 && me.HpPct > 0 && me.HpPct <= criticalEnterHpPct;

        if (now < _nextFightActionAt && !isCriticalHp)
        {
            return;
        }

        EnsureCoordinatorMode();
        TrackIncomingDamage(me, now);

        var strictCaster = IsCasterRole();
        if (me.IsSitting)
        {
            var shouldForceStand = _combatTargetObjectId != 0 || me.TargetId != 0 || HasRecentIncomingDamage(now, 1600);
            if (shouldForceStand
                && now >= _nextRestToggleAt
                && TryInject(PacketBuilder.BuildActionUse(0), "force-stand-before-engage"))
            {
                _nextRestToggleAt = now.AddMilliseconds(950);
                _nextFightActionAt = now.AddMilliseconds(170);
                SetCombatPhase("force-stand-before-engage");
                SetDecision("force-stand-before-engage");
                return;
            }

            SetCombatPhase("paused-sitting");
            SetDecision("fight-paused-sitting");
            _nextFightActionAt = now.AddMilliseconds(250);
            return;
        }
        if (Settings.EnableSupportV2 && IsSupportRole() && !Settings.SupportAllowDamage)
        {
            if (Settings.EnableRoleCoordinator
                && Settings.CoordMode == CoordMode.CoordinatorFollower
                && _coordinator.TryGetLatestIntent(Settings.CoordinatorStaleMs, out var supportIntent)
                && TryFollowLeaderIntent(me, supportIntent, now))
            {
                SetCombatPhase("support-follow-leader");
                _nextFightActionAt = now.AddMilliseconds(220);
                return;
            }
            SetCombatPhase("support-hold");
            SetDecision("support-hold");
            _nextFightActionAt = now.AddMilliseconds(280);
            return;
        }
        TrackIncomingDamage(me, now);
        PublishLeaderIntent(me, _combatTargetObjectId, _combatPhase, now);

        var criticalResumeHpPct = Math.Max(criticalEnterHpPct + 1, Math.Min(99, Settings.CriticalHoldResumeHpPct));

        if (_criticalHoldActive)
        {
            var canResume = me.MaxHp > 0 && me.HpPct >= criticalResumeHpPct;
            if (canResume)
            {
                _criticalHoldActive = false;
                _nextFightActionAt = DateTime.MinValue;
                _nextRestToggleAt = DateTime.MinValue;
                SetDecision($"critical-hp-resume hp={me.HpPct:0}%");
            }
        }
        else if (isCriticalHp)
        {
            _criticalHoldActive = true;
        }

        if (_criticalHoldActive)
        {
            var emergencySelfHealSkillId = ResolveSelfPreservationSkillId();
            if (emergencySelfHealSkillId > 0 && now >= _nextSelfHealAt)
            {
                if (TryCastSkill(emergencySelfHealSkillId, forBuff: true, cooldownOverrideMs: 900))
                {
                    _nextSelfHealAt = now.AddMilliseconds(900);
                }
            }

            if (_combatTargetObjectId != 0)
            {
                _temporarilyIgnoredTargets[_combatTargetObjectId] = now.AddSeconds(12);
                TryInject(PacketBuilder.BuildTargetCancel(), "critical-hp-cancel-target");
                ClearSpoilPendingTarget(_combatTargetObjectId);
                ResetCombatState();
                _lastAutoTargetObjectId = 0;
            }

            SetCombatPhase("critical-hp-hold");
            SetDecision($"critical-hp-hold hp={me.HpPct:0}% resume={criticalResumeHpPct}%");
            _nextFightActionAt = now.AddMilliseconds(620);
            return;
        }
        var overpullHoldHpPct = Math.Max(50, Settings.CriticalHoldResumeHpPct);
        if (_combatTargetObjectId == 0 && me.MaxHp > 0 && me.HpPct < overpullHoldHpPct)
        {
            var freshAggroCount = CountFreshAggroAttackers(me, now);
            if (freshAggroCount >= 2)
            {
                var overpullSelfHealSkillId = ResolveSelfPreservationSkillId();
                if (overpullSelfHealSkillId > 0 && now >= _nextSelfHealAt)
                {
                    if (TryCastSkill(overpullSelfHealSkillId, forBuff: true, cooldownOverrideMs: 900))
                    {
                        _nextSelfHealAt = now.AddMilliseconds(900);
                    }
                }

                SetCombatPhase("overpull-hold");
                SetDecision($"overpull-hold hp={me.HpPct:0}% aggro={freshAggroCount}");
                _nextFightActionAt = now.AddMilliseconds(420);
                return;
            }
        }

        var mpSitAt = Math.Max(1, Math.Min(99, Settings.SitMpPct));
        var mpStandAt = Math.Max(mpSitAt + 1, Math.Min(100, Settings.StandMpPct));
        if (!_postKillActive && _combatTargetObjectId == 0 && !HasRecentIncomingDamage(now, 2500))
        {
            if (me.MpPct <= mpSitAt)
            {
                if (!me.IsSitting && now >= _nextRestToggleAt && TryInject(PacketBuilder.BuildActionUse(0), "rest-sit-low-mp-hold"))
                {
                    _nextRestToggleAt = now.AddMilliseconds(950);
                    _restExpectedIsSitting = true;
                    _restExpectedStateUntilUtc = now.AddSeconds(4);
                }

                SetCombatPhase("rest-mp-hold");
                SetDecision($"rest-mp-hold mp={me.MpPct:0.#}%<= {mpSitAt}%");
                _nextFightActionAt = now.AddMilliseconds(350);
                return;
            }

            if (me.IsSitting && me.MpPct < mpStandAt)
            {
                SetCombatPhase("rest-mp-hold");
                SetDecision($"rest-mp-hold mp={me.MpPct:0.#}%< {mpStandAt}%");
                _nextFightActionAt = now.AddMilliseconds(350);
                return;
            }
        }

        if (_postKillActive)
        {
            if (RunPostKillSweepAndLoot(me, now))
            {
                _nextFightActionAt = now.AddMilliseconds(180);
                return;
            }

            // If post-kill has just completed this tick, do not instantly reacquire.
            // Yield one tick so low-mp/rest/defensive guards can run first.
            _nextFightActionAt = now.AddMilliseconds(140);
            return;
        }

        EnsureHuntCenterAnchor(me);
        var huntCenter = GetHuntCenter(me);
        var fightRange = Math.Max(100, Settings.FightRange);
        var fightRangeSq = (long)fightRange * fightRange;

        NpcState? target = null;

        if (_combatTargetObjectId != 0)
        {
            var killTimeoutMs = Math.Max(6000, Settings.KillTimeoutMs);
            var timedOut = _combatTargetAssignedAtUtc != DateTime.MinValue
                && (now - _combatTargetAssignedAtUtc).TotalMilliseconds >= killTimeoutMs;

            if (_world.Npcs.TryGetValue(_combatTargetObjectId, out var lockedTarget))
            {
                UpdateCombatTargetSnapshot(lockedTarget, now);

                if (lockedTarget.IsDead || lockedTarget.HpPct <= 0.01f)
                {
                    BeginPostKill(lockedTarget, now, "target-eliminated");
                    _nextFightActionAt = now.AddMilliseconds(220);
                    return;
                }

                if (timedOut)
                {
                    _killTimeoutFireCount++;

                    // Hard-abandon takes priority after 3 consecutive kill-timeouts.
                    // Must be checked BEFORE the reengage block, which does an early return
                    // and would prevent this from ever being reached on the 3rd timeout.
                    if (_killTimeoutFireCount >= 3)
                    {
                        _log.Info($"[AutoFight] hard-abandon after {_killTimeoutFireCount} timeouts oid=0x{lockedTarget.ObjectId:X}; ignoring 30s");
                        _temporarilyIgnoredTargets[lockedTarget.ObjectId] = now.AddSeconds(30);
                        ClearSpoilPendingTarget(lockedTarget.ObjectId);
                        ResetCombatState();
                        _killTimeoutFireCount = 0;
                        _lastAutoTargetObjectId = 0;
                        _nextFightActionAt = now.AddMilliseconds(260);
                        return;
                    }

                    var staleProgress = _combatLastProgressAtUtc != DateTime.MinValue
                        && (now - _combatLastProgressAtUtc).TotalMilliseconds >= Math.Max(5200, Settings.AttackNoProgressWindowMs + 1200);
                    var noRecentIncoming = _lastIncomingDamageAtUtc == DateTime.MinValue
                        || (now - _lastIncomingDamageAtUtc).TotalMilliseconds >= 2200;
                    var noRecentOutgoing = lockedTarget.LastHitByMeAtUtc == DateTime.MinValue
                        || (now - lockedTarget.LastHitByMeAtUtc).TotalMilliseconds >= 2200;

                    if (staleProgress && noRecentIncoming && noRecentOutgoing)
                    {
                        _log.Info($"[AutoFight] kill-timeout target=0x{lockedTarget.ObjectId:X} elapsed={(now - _combatTargetAssignedAtUtc).TotalSeconds:0.0}s attempt={_killTimeoutFireCount}");
                        SetCombatPhase("kill-timeout-reengage");
                        if (now >= _nextTargetActionAt
                            && TryInject(PacketBuilder.BuildAction(lockedTarget.ObjectId, me.X, me.Y, me.Z, 0), "kill-timeout-target-action"))
                        {
                            _nextTargetActionAt = now.AddMilliseconds(1300);
                        }

                        if (strictCaster)
                        {
                            _ = TryUseCombatRotation();
                        }
                        else if (now >= _nextForceAttackAt && TrySendAttack(me, lockedTarget.ObjectId, "kill-timeout-attack"))
                        {
                            _nextForceAttackAt = now.AddMilliseconds(980);
                        }

                        _combatTargetAssignedAtUtc = now;
                        _combatLastProgressAtUtc = now;
                        _nextFightActionAt = now.AddMilliseconds(680);
                        return;
                    }
                }

                if (Settings.FinishCurrentTargetBeforeAggroRetarget)
                {
                    target = lockedTarget;
                }
                else
                {
                    var stickyDistSq = DistanceSq(huntCenter.x, huntCenter.y, lockedTarget.X, lockedTarget.Y);
                    if (stickyDistSq <= fightRangeSq)
                    {
                        target = lockedTarget;
                    }
                }
            }
            else
            {
                var hadTime = _combatTargetAssignedAtUtc != DateTime.MinValue
                    && (now - _combatTargetAssignedAtUtc).TotalMilliseconds >= 1200;

                var likelyEliminated = _combatTargetLastHpPct >= 0 && _combatTargetLastHpPct <= 1f;
                var recentlyEngaged = _combatTargetLastHitByMeAtUtc != DateTime.MinValue
                    && (now - _combatTargetLastHitByMeAtUtc).TotalMilliseconds <= 4500;
                var missingLongEnough = _combatTargetLastSeenAtUtc != DateTime.MinValue
                    && (now - _combatTargetLastSeenAtUtc).TotalMilliseconds >= 700;

                if (hadTime && missingLongEnough && (likelyEliminated || (_combatTargetSpoilSucceeded && recentlyEngaged)))
                {
                    BeginPostKillFromSnapshot(now, "target-disappeared");
                    _nextFightActionAt = now.AddMilliseconds(220);
                    return;
                }

                ResetCombatState();
                _lastAutoTargetObjectId = 0;
            }
        }

        if (target is null && _spoilPendingTargetObjectId != 0)
        {
            if (now > _spoilPendingUntilUtc
                || !_world.Npcs.TryGetValue(_spoilPendingTargetObjectId, out var spoilPendingNpc)
                || spoilPendingNpc.IsDead)
            {
                ClearSpoilPendingTarget();
            }
            else
            {
                var pendingDistSq = DistanceSq(huntCenter.x, huntCenter.y, spoilPendingNpc.X, spoilPendingNpc.Y);
                if (spoilPendingNpc.IsAttackable && pendingDistSq <= fightRangeSq)
                {
                    target = spoilPendingNpc;
                }
            }
        }

        if (target is null && HasRecentIncomingDamage(now, 2200) && now >= _nextEmergencyRetaliateAt)
        {
            target = SelectEmergencyAggroTarget(me, now);
            if (target is not null)
            {
                _nextEmergencyRetaliateAt = now.AddMilliseconds(850);
                SetDecision($"retaliate-aggro oid=0x{target.ObjectId:X}");
            }
        }

                if (target is null && TryResolveCoordinatorFollowerTarget(me, now, out var followerTarget))
        {
            if (followerTarget is not null)
            {
                target = followerTarget;
            }
            else if (Settings.EnableRoleCoordinator
                && Settings.CoordMode == CoordMode.CoordinatorFollower
                && !Settings.FollowerFallbackToStandalone)
            {
                SetCombatPhase("follower-hold-no-target");
                SetDecision("follower-hold-no-target");
                _nextFightActionAt = now.AddMilliseconds(Math.Max(260, Settings.CasterCastIntervalMs));
                return;
            }
        }
        target ??= SelectFightTarget(me, huntCenter.x, huntCenter.y, huntCenter.z);
        if (target is null)
        {
            if (me.TargetId == 0)
            {
                if (_temporarilyIgnoredTargets.Count > 0)
                {
                    _temporarilyIgnoredTargets.Clear();
                    SetCombatPhase("idle-reacquire");
                    _nextFightActionAt = now.AddMilliseconds(120);
                    return;
                }

                                _lastAutoTargetObjectId = 0;
                ResetCombatState();
                SetCombatPhase("idle-no-target");
                SetDecision("idle-no-target");
                if (now >= _nextIdleNoTargetLogAt)
                {
                    _nextIdleNoTargetLogAt = now.AddSeconds(4);
                    _log.Info("[AutoFight] no eligible targets in range; waiting");
                }
                _nextFightActionAt = now.AddMilliseconds(180);
                return;
            }

            var hasValidExistingTarget = _world.Npcs.TryGetValue(me.TargetId, out var existingTarget)
                && existingTarget.IsAttackable
                && !existingTarget.IsDead
                && existingTarget.HpPct > 0.01f;

            if (!hasValidExistingTarget)
            {
                TryInject(PacketBuilder.BuildTargetCancel(), "clear-stale-target");
                _nextFightActionAt = now.AddMilliseconds(300);
                return;
            }

            if (Settings.UseCombatSkills && TryUseCombatRotation())
            {
                SetCombatPhase("active-existing-target-skill");
                _nextFightActionAt = now.AddMilliseconds(350);
                return;
            }
            if (strictCaster
                && Settings.EnableCasterV2
                && Settings.CasterFallbackToAttack
                && now >= _nextForceAttackAt
                && TrySendAttack(me, me.TargetId, "caster-existing-fallback"))
            {
                SetCombatPhase("caster-fallback-attack");
                _nextForceAttackAt = now.AddMilliseconds(980);
                _nextFightActionAt = now.AddMilliseconds(Math.Max(320, Settings.CasterCastIntervalMs));
                return;
            }
            if (strictCaster)
            {
                SetCombatPhase("strict-caster-wait");
                SetDecision("strict-caster-wait-no-skill");
                _nextFightActionAt = now.AddMilliseconds(Math.Max(260, Settings.CasterCastIntervalMs));
                return;
            }

            if (now < _nextForceAttackAt)
            {
                _nextFightActionAt = now.AddMilliseconds(320);
                return;
            }

            if (TrySendAttack(me, me.TargetId, "target-attack"))
            {
                SetCombatPhase("active-existing-target-attack");
                _nextForceAttackAt = now.AddMilliseconds(980);
            }

            _nextFightActionAt = now.AddMilliseconds(900);
            return;
        }

        if (_combatTargetObjectId != target.ObjectId)
        {
            ResetCombatState();
            _combatTargetObjectId = target.ObjectId;
            _combatTargetAssignedAtUtc = now;
            _combatLastProgressAtUtc = now;
            _combatTargetLastHpPct = target.HpPct;
            _nextSpoilAt = now;
                        _combatNoProgressStrikes = 0;
            _combatNoServerTargetSinceUtc = DateTime.MinValue;
            _nextTargetActionAt = DateTime.MinValue;
        _preferAttackRequestUntilUtc = DateTime.MinValue;
        _nextTransportSwitchAtUtc = DateTime.MinValue;
            UpdateCombatTargetSnapshot(target, now);
            SetCombatPhase("acquire");
            _log.Info($"[AutoFight] target {target.Name} oid=0x{target.ObjectId:X} hp={target.HpPct:0.#}%");
        }

        _lastAutoTargetObjectId = target.ObjectId;

        if (target.IsDead || target.HpPct <= 0.01f)
        {
            BeginPostKill(target, now, "target-eliminated");
            _nextFightActionAt = now.AddMilliseconds(220);
            return;
        }

        var targetDistSq = DistanceSq(me.X, me.Y, target.X, target.Y);
        var hasServerTarget = me.TargetId == target.ObjectId;
        var canUseAssumedTarget = _assumedTargetObjectId == target.ObjectId
            && now <= _assumedTargetUntilUtc;

        if (!hasServerTarget && !canUseAssumedTarget)
        {
            if (_combatNoServerTargetSinceUtc == DateTime.MinValue)
            {
                _combatNoServerTargetSinceUtc = now;
                _targetConfirmRecoveryStage = 0;
                _targetConfirmRecoveryStartedAtUtc = now;
            }

            SetCombatPhase("target-confirm");

            var noProgress = _combatLastProgressAtUtc == DateTime.MinValue
                || (now - _combatLastProgressAtUtc).TotalMilliseconds >= Math.Max(1200, Settings.CasterCastIntervalMs);

            if (noProgress)
            {
                if (_targetConfirmRecoveryStage == 0
                    && now >= _nextTargetActionAt
                    && TryInject(PacketBuilder.BuildAction(target.ObjectId, me.X, me.Y, me.Z, 0), "target-confirm-action"))
                {
                    _assumedTargetObjectId = target.ObjectId;
                    _assumedTargetUntilUtc = now.AddMilliseconds(1800);
                    _nextTargetActionAt = now.AddMilliseconds(760);
                    _targetConfirmRecoveryStage = 1;
                    _targetConfirmRecoveryStartedAtUtc = now;
                    SetDecision($"target-confirm-action:0x{target.ObjectId:X}");
                }

                if (_targetConfirmRecoveryStage <= 1 && now >= _nextForceAttackAt)
                {
                    var (tx, ty, tz) = ResolveTargetPosition(me, target.ObjectId);
                    if (TryInject(PacketBuilder.BuildForceAttack(target.ObjectId, tx, ty, tz), "target-confirm-force-2f16"))
                    {
                        _assumedTargetObjectId = target.ObjectId;
                        _assumedTargetUntilUtc = now.AddMilliseconds(2400);
                        _nextForceAttackAt = now.AddMilliseconds(920);
                        _targetConfirmRecoveryStage = 2;
                        _targetConfirmRecoveryStartedAtUtc = now;
                        SetDecision($"target-confirm-force:0x{target.ObjectId:X}");
                        _nextFightActionAt = now.AddMilliseconds(170);
                        return;
                    }
                }

                var noServerTargetTimeoutMs = Math.Max(3800, Settings.AttackNoProgressWindowMs);
                var recoveryAgeMs = _targetConfirmRecoveryStartedAtUtc == DateTime.MinValue
                    ? 0
                    : (now - _targetConfirmRecoveryStartedAtUtc).TotalMilliseconds;
                if ((now - _combatNoServerTargetSinceUtc).TotalMilliseconds >= noServerTargetTimeoutMs)
                {
                    var staleCount = _stallRetargetCountByTarget.GetValueOrDefault(target.ObjectId);
                    var ignoreSeconds = staleCount >= 2 ? 35 : 18;
                    _stallRetargetCountByTarget[target.ObjectId] = staleCount + 1;
                    _temporarilyIgnoredTargets[target.ObjectId] = now.AddSeconds(ignoreSeconds);
                    _log.Info($"[AutoFight] no-server-target-timeout oid=0x{target.ObjectId:X}; recovery=action->force age={recoveryAgeMs:0}ms; ignore={ignoreSeconds}s; retarget");
                    ActivateAttackRequestFallback(now, "no-server-target");
                    ClearSpoilPendingTarget(target.ObjectId);
                    ResetCombatState();
                    _lastAutoTargetObjectId = 0;
                    SetCombatPhase("retarget-no-server-target");
                    SetDecision("retarget-no-server-target");
                    _nextFightActionAt = now.AddMilliseconds(220);
                    return;
                }
            }

            _nextFightActionAt = now.AddMilliseconds(strictCaster ? 220 : 170);
            return;
        }

        _combatNoServerTargetSinceUtc = DateTime.MinValue;
        _targetConfirmRecoveryStage = 0;
        _targetConfirmRecoveryStartedAtUtc = DateTime.MinValue;
        _criticalHoldActive = false;

        if (hasServerTarget && _assumedTargetObjectId == target.ObjectId)
        {
            _assumedTargetObjectId = 0;
            _assumedTargetUntilUtc = DateTime.MinValue;
        }

        ObserveCombatProgress(target, now);
        UpdateCombatTargetSnapshot(target, now);

        if (target.SpoilSucceeded && _spoilPendingTargetObjectId == target.ObjectId)
        {
            ClearSpoilPendingTarget(target.ObjectId);
        }

        var spoilCombatSignal = hasServerTarget
            || canUseAssumedTarget
            || (target.LastHitByMeAtUtc != DateTime.MinValue
                && (now - target.LastHitByMeAtUtc).TotalMilliseconds <= 2600)
            || (_lastIncomingDamageAtUtc != DateTime.MinValue
                && (now - _lastIncomingDamageAtUtc).TotalMilliseconds <= 1800);

        if (TryRunSpoilOpening(target, now, targetDistSq, spoilCombatSignal))
        {
            return;
        }

        var meleeRange = Math.Max(70, Settings.MeleeEngageRange);
        var meleeRangeSq = (long)meleeRange * meleeRange;
        if (strictCaster && Settings.EnableCasterV2)
        {
            var casterChaseRange = Math.Max(220, Settings.CasterChaseRange);
            var casterChaseRangeSq = (long)casterChaseRange * casterChaseRange;
            if (targetDistSq > casterChaseRangeSq)
            {
                // Caster stuck detection: if chasing the same target >8s, treat as no-progress strike
                if (_casterChaseStartedAtUtc == DateTime.MinValue)
                    _casterChaseStartedAtUtc = now;

                if ((now - _casterChaseStartedAtUtc).TotalMilliseconds >= 8000)
                {
                    _casterChaseStartedAtUtc = DateTime.MinValue;
                    _casterStuckStrikes++;
                    _combatLastProgressAtUtc = now;
                    _log.Info($"[AutoFight] caster-stuck oid=0x{target.ObjectId:X} strike={_casterStuckStrikes}; chase >8s without reaching cast range");

                    if (_casterStuckStrikes >= 3)
                    {
                        var staleCount = _stallRetargetCountByTarget.GetValueOrDefault(target.ObjectId);
                        var ignoreSeconds = staleCount >= 2 ? 35 : 15;
                        _stallRetargetCountByTarget[target.ObjectId] = staleCount + 1;
                        _temporarilyIgnoredTargets[target.ObjectId] = now.AddSeconds(ignoreSeconds);
                        _log.Info($"[AutoFight] target-stalled oid=0x{target.ObjectId:X}; ignore={ignoreSeconds}s; retargeting");
                        ActivateAttackRequestFallback(now, "progress-stalled");
                        ClearSpoilPendingTarget(target.ObjectId);
                        ResetCombatState();
                        _lastAutoTargetObjectId = 0;
                        SetCombatPhase("target-stalled-retarget");
                        _nextFightActionAt = now.AddMilliseconds(260);
                        return;
                    }
                }

                // Out of cast range — cast a skill (server moves char to skill range + fires it).
                // No ForceAttack: it drags the caster to melee range (~40 units) causing unnecessary movement.
                if (Settings.UseCombatSkills && TryUseCombatRotation())
                {
                    SetCombatPhase("caster-chase");
                }
                else
                {
                    // All skills on cooldown — stand still and wait; do NOT send ForceAttack.
                    SetCombatPhase("caster-chase-wait");
                }
                _nextFightActionAt = now.AddMilliseconds(Math.Max(400, Settings.CasterCastIntervalMs));
                return;
            }

            // Reached cast range — reset chase timer
            _casterChaseStartedAtUtc = DateTime.MinValue;
        }


        if (Settings.UseCombatSkills && TryUseCombatRotation())
        {
            SetCombatPhase("attack-rotation");
            _nextFightActionAt = now.AddMilliseconds(420);
            return;
        }
        if (strictCaster
            && Settings.EnableCasterV2
            && Settings.CasterFallbackToAttack
            && now >= _nextForceAttackAt
            && TrySendAttack(me, target.ObjectId, "caster-fallback-loop"))
        {
            SetCombatPhase("caster-fallback-attack");
            _nextForceAttackAt = now.AddMilliseconds(980);
            _nextFightActionAt = now.AddMilliseconds(Math.Max(320, Settings.CasterCastIntervalMs));
            return;
        }
        if (strictCaster)
        {
            SetCombatPhase("strict-caster-wait");
            SetDecision("strict-caster-wait-no-skill");
            _nextFightActionAt = now.AddMilliseconds(Math.Max(260, Settings.CasterCastIntervalMs));
            return;
        }

        var progressWindowMs = Math.Max(1600, Settings.AttackNoProgressWindowMs);
        if (_combatLastProgressAtUtc != DateTime.MinValue && (now - _combatLastProgressAtUtc).TotalMilliseconds >= progressWindowMs)
        {
            var noRecentIncomingDamage = _lastIncomingDamageAtUtc == DateTime.MinValue
                || (now - _lastIncomingDamageAtUtc).TotalMilliseconds >= 2400;
            var noRecentOutgoingHit = target.LastHitByMeAtUtc == DateTime.MinValue
                || (now - target.LastHitByMeAtUtc).TotalMilliseconds >= 2400;

            if (noRecentIncomingDamage && noRecentOutgoingHit)
            {
                _combatNoProgressStrikes++;
                SetCombatPhase("progress-guard");
                _log.Info($"[AutoFight] no progress for {(now - _combatLastProgressAtUtc).TotalSeconds:0.0}s target=0x{target.ObjectId:X}; strike={_combatNoProgressStrikes}; forcing engage");

                if (_combatNoProgressStrikes >= 3)
                {
                    var staleCount = _stallRetargetCountByTarget.GetValueOrDefault(target.ObjectId);
                    var ignoreSeconds = staleCount >= 2 ? 35 : 15;
                    _stallRetargetCountByTarget[target.ObjectId] = staleCount + 1;
                    _temporarilyIgnoredTargets[target.ObjectId] = now.AddSeconds(ignoreSeconds);
                    _log.Info($"[AutoFight] target-stalled oid=0x{target.ObjectId:X}; ignore={ignoreSeconds}s; retargeting");
                    ActivateAttackRequestFallback(now, "progress-stalled");
                    ClearSpoilPendingTarget(target.ObjectId);
                    ResetCombatState();
                    _lastAutoTargetObjectId = 0;
                    SetCombatPhase("target-stalled-retarget");
                    _nextFightActionAt = now.AddMilliseconds(260);
                    return;
                }

                if (now >= _nextTargetActionAt)
                {
                    TryInject(PacketBuilder.BuildAction(target.ObjectId, me.X, me.Y, me.Z, 0), "progress-guard-target-action");
                    _nextTargetActionAt = now.AddMilliseconds(900);
                }

                TrySendAttack(me, target.ObjectId, "progress-guard");
                _combatLastProgressAtUtc = now;
                _nextFightActionAt = now.AddMilliseconds(620);
                return;
            }

            _combatLastProgressAtUtc = now;
        }

        if (now < _nextForceAttackAt)
        {
            _nextFightActionAt = now.AddMilliseconds(280);
            return;
        }

        if (TrySendAttack(me, target.ObjectId, "attack-loop"))
        {
            SetCombatPhase("engage");
            _nextForceAttackAt = now.AddMilliseconds(980);
        }

        _nextFightActionAt = now.AddMilliseconds(520);
    }
    private bool TryRunSpoilOpening(NpcState target, DateTime now, long targetDistSq, bool allowOutOfRangeFromCombatSignal)
    {
        if (Settings.Role != BotRole.Spoiler || !Settings.SpoilEnabled || Settings.SpoilSkillId <= 0)
        {
            return false;
        }

        if (now < _nextSpoilAt)
        {
            return false;
        }

        if (target.SpoilSucceeded)
        {
            return false;
        }

        var maxSpoilAttempts = Math.Max(1, Settings.CombatMode == CombatMode.HybridFsmPriority
            ? Settings.SpoilMaxAttemptsPerTarget
            : (Settings.SpoilOncePerTarget ? 1 : Settings.SpoilMaxAttemptsPerTarget));
        if (_spoilAttemptCountByTarget.GetValueOrDefault(target.ObjectId) >= maxSpoilAttempts)
        {
            return false;
        }

        if (_spoilPendingTargetObjectId == target.ObjectId && now < _spoilPendingUntilUtc)
        {
            return false;
        }

        var spoilRetryMs = Math.Max(1100, Settings.CombatSkillCooldownMs);
        var spoilRetryTimeoutMs = maxSpoilAttempts <= 1
            ? Math.Max(15000, Settings.AttackNoProgressWindowMs + 6000)
            : Math.Max(Math.Max(spoilRetryMs * 2, Settings.AttackNoProgressWindowMs), 7000);

        if (_spoilAttemptLocalByTarget.TryGetValue(target.ObjectId, out var localAttemptAt)
            && (now - localAttemptAt).TotalMilliseconds < spoilRetryTimeoutMs)
        {
            return false;
        }

        if (target.SpoilAttempted && target.SpoilAtUtc != DateTime.MinValue)
        {
            var elapsed = (now - target.SpoilAtUtc).TotalMilliseconds;
            if (maxSpoilAttempts <= 1)
            {
                if (elapsed < spoilRetryTimeoutMs)
                {
                    return false;
                }
            }
            else if (elapsed < spoilRetryMs)
            {
                return false;
            }
        }

        var spoilCastRange = Math.Max(80, Settings.MeleeEngageRange + 40);
        var spoilCastRangeSq = (long)spoilCastRange * spoilCastRange;
        if (targetDistSq > spoilCastRangeSq && !allowOutOfRangeFromCombatSignal)
        {
            var spoilChaseRange = Math.Max(spoilCastRange + 240, Settings.MeleeEngageRange + 320);
            var spoilChaseRangeSq = (long)spoilChaseRange * spoilChaseRange;
            if (!Settings.MoveToTarget || targetDistSq > spoilChaseRangeSq)
            {
                return false;
            }
        }

        if (!TryCastSkill(Settings.SpoilSkillId, forBuff: false, cooldownOverrideMs: spoilRetryMs, allowReservedSkill: true))
        {
            return false;
        }

        var retryWindowMs = Math.Max(500, Settings.PostKillSweepRetryWindowMs);
        _world.WithLock(() =>
        {
            if (_world.Npcs.TryGetValue(target.ObjectId, out var tracked))
            {
                tracked.SpoilAttempted = true;
                tracked.SpoilAtUtc = now;
                tracked.SweepDone = false;
                tracked.SweepRetryUntilUtc = now.AddMilliseconds(retryWindowMs);
                _world.Npcs[target.ObjectId] = tracked;
            }
        });

        _spoilAttemptLocalByTarget[target.ObjectId] = now;
        _spoilAttemptCountByTarget[target.ObjectId] = _spoilAttemptCountByTarget.GetValueOrDefault(target.ObjectId) + 1;
        var pruneBefore = now.AddMinutes(-2);
        foreach (var kv in _spoilAttemptLocalByTarget.Where(x => x.Value < pruneBefore).ToArray())
        {
            _spoilAttemptLocalByTarget.Remove(kv.Key);
        }

        SetSpoilPendingTarget(target.ObjectId, now);

        SetCombatPhase("opening-spoil");
        _nextSpoilAt = now.AddMilliseconds(Math.Max(1400, Settings.CombatSkillCooldownMs));
        _nextForceAttackAt = now.AddMilliseconds(320);
        _nextFightActionAt = now.AddMilliseconds(160);
        return true;
    }
    private bool HandleDeadTarget(NpcState target, DateTime now)
    {
        BeginPostKill(target, now, "dead-target");
        return true;
    }

    private void UpdateCombatTargetSnapshot(NpcState target, DateTime now)
    {
        _combatTargetLastSeenAtUtc = now;
        _combatTargetLastX = target.X;
        _combatTargetLastY = target.Y;
        _combatTargetLastZ = target.Z;
        _combatTargetNpcTypeId = target.NpcTypeId;
        _combatTargetSpoilSucceeded = ResolveSpoilSucceeded(target, now);

        if (target.LastHitByMeAtUtc != DateTime.MinValue)
        {
            _combatTargetLastHitByMeAtUtc = target.LastHitByMeAtUtc;
        }

        if (HasCombatEvidence(target, now))
        {
            _combatTargetHadCombatSignal = true;
        }
    }
    private bool ResolveSpoilSucceeded(NpcState target, DateTime now)
    {
        if (target.SpoilSucceeded)
        {
            return true;
        }

        if (target.AbnormalEffectSkillIds.Count > 0 && target.AbnormalEffectSkillIds.Overlaps(new HashSet<int> { Settings.SpoilSkillId, 254, 302 }))
        {
            return true;
        }

        if (target.SpoilAttempted
            && target.SpoilAtUtc != DateTime.MinValue
            && (now - target.SpoilAtUtc).TotalSeconds <= 10)
        {
            return true;
        }

        if (_spoilAttemptLocalByTarget.TryGetValue(target.ObjectId, out var localAttemptAt)
            && (now - localAttemptAt).TotalSeconds <= 10)
        {
            return true;
        }

        return false;
    }
    private void BeginPostKill(NpcState target, DateTime now, string reason)
    {
        _postKillTargetObjectId = target.ObjectId;
        _postKillNpcTypeId = target.NpcTypeId;
        _postKillX = target.X;
        _postKillY = target.Y;
        _postKillZ = target.Z;
        _postKillSpoilSucceeded = Settings.Role == BotRole.Spoiler && ResolveSpoilSucceeded(target, now);
        BeginPostKillCore(now, reason);
    }

    private void BeginPostKillFromSnapshot(DateTime now, string reason)
    {
        if (_combatTargetObjectId == 0)
        {
            return;
        }

        _postKillTargetObjectId = _combatTargetObjectId;
        _postKillNpcTypeId = _combatTargetNpcTypeId;
        _postKillX = _combatTargetLastX;
        _postKillY = _combatTargetLastY;
        _postKillZ = _combatTargetLastZ;
        _postKillSpoilSucceeded = Settings.Role == BotRole.Spoiler && _combatTargetSpoilSucceeded;
        BeginPostKillCore(now, reason);
    }

    private void BeginPostKillCore(DateTime now, string reason)
    {
        if (_postKillActive
            && _postKillTargetObjectId != 0
            && (now - _postKillStartedAtUtc).TotalMilliseconds < 7000)
        {
            return;
        }
        if (_postKillTargetObjectId != 0
            && _recentPostKillByTarget.TryGetValue(_postKillTargetObjectId, out var recentAt)
            && (now - recentAt).TotalMilliseconds < 4500)
        {
            return;
        }

        _postKillActive = true;
        _postKillStartedAtUtc = now;
        _postKillSweepUntilUtc = now.AddMilliseconds(Math.Max(800, Settings.PostKillSweepRetryWindowMs));
        var spawnWaitMs = Math.Max(0, Settings.PostKillSpawnWaitMs);
        if (Settings.CombatMode == CombatMode.HybridFsmPriority)
        {
            spawnWaitMs = Math.Max(spawnWaitMs, 220);
        }

        _postKillSpawnWaitUntilUtc = now.AddMilliseconds(spawnWaitMs);
        _postKillMinWaitForLootUntilUtc = now.AddMilliseconds(Math.Max(900, spawnWaitMs + 900));
        _nextPostKillActionAt = now;
        _postKillLootActions = 0;
        _postKillEmptyPolls = 0;
        _postKillItemsSeen = 0;
        _postKillItemsPicked = 0;
        _postKillItemsSkipped = 0;
        _postKillTargetCanceled = false;
        _postKillSweepAttempts = 0;
        _postKillMoveToCorpseAttempts = 0;
        _postKillReachedCorpseZone = false;
        var me = _world.Me;
        var pickupRange = Math.Max(70, Settings.LootPickupRange);
        var pickupRangeSq = (long)pickupRange * pickupRange;
        if (DistanceSq(me.X, me.Y, _postKillX, _postKillY) <= pickupRangeSq)
        {
            _postKillReachedCorpseZone = true;
        }
        _postKillLootItemAttempts.Clear();
        _postKillSeenItemIds.Clear();
        _spoilAttemptCountByTarget.Clear();

        if (_postKillTargetObjectId != 0)
        {
            ClearSpoilPendingTarget(_postKillTargetObjectId);
            _stallRetargetCountByTarget.Remove(_postKillTargetObjectId);
            _log.Info($"[AutoFight] post-kill start oid=0x{_postKillTargetObjectId:X} reason={reason} spoil={_postKillSpoilSucceeded}");
        }
        else
        {
            ClearSpoilPendingTarget();
        }

        ResetCombatState();
        _lastAutoTargetObjectId = 0;
        SetCombatPhase("post-kill-start");
        SetDecision($"post-kill-start:{reason}");
    }
    private bool RunPostKillSweepAndLoot(CharacterState me, DateTime now)
    {
        if (!_postKillActive)
        {
            return false;
        }

        var postKillTimeboxMs = 6200;
        if (Settings.MoveToLoot)
        {
            var distToKill = Math.Sqrt(DistanceSq(me.X, me.Y, _postKillX, _postKillY));
            var travelBudgetMs = (int)Math.Min(7000, distToKill * 9.0);
            postKillTimeboxMs = Math.Min(14500, postKillTimeboxMs + travelBudgetMs);
        }

        if ((now - _postKillStartedAtUtc).TotalMilliseconds >= postKillTimeboxMs)
        {
            EndPostKill("post-kill-timebox");
            return false;
        }

        if (HasRecentIncomingDamage(now, 900) && _postKillLootActions > 0)
        {
            var lowHpUnderPressure = me.MaxHp > 0 && me.HpPct <= Math.Max(24, Settings.HealHpThreshold - 8);
            if (lowHpUnderPressure)
            {
                EndPostKill("post-kill-under-attack");
                return false;
            }
        }

        if (now < _nextPostKillActionAt)
        {
            return true;
        }

        var maxSweepAttempts = Math.Max(1, Settings.SweepAttemptsPostKill);
        var canSweepSpoilCorpse = Settings.Role == BotRole.Spoiler;
        if (canSweepSpoilCorpse && Settings.PostKillSweepEnabled
            && Settings.SweepEnabled
            && Settings.SweepSkillId > 0
            && _postKillSpoilSucceeded
            && _postKillSweepAttempts < maxSweepAttempts
            && now <= _postKillSweepUntilUtc && (_postKillTargetObjectId == 0 || _world.Npcs.ContainsKey(_postKillTargetObjectId)))
        {
            var sweepRetryMs = Math.Max(120, Settings.PostKillSweepRetryIntervalMs);
            if (now >= _nextSweepAt)
            {
                if (TryCastSkill(Settings.SweepSkillId, forBuff: false, cooldownOverrideMs: sweepRetryMs, allowReservedSkill: true))
                {
                    _postKillSweepAttempts++;
                }

                _nextSweepAt = now.AddMilliseconds(sweepRetryMs);
                SetCombatPhase("post-kill-sweep");
            }

            _nextPostKillActionAt = now.AddMilliseconds(140);
            return true;
        }

        if (now < _postKillSpawnWaitUntilUtc)
        {
            SetCombatPhase("post-kill-wait-drop");
            _nextPostKillActionAt = _postKillSpawnWaitUntilUtc;
            return true;
        }

        if (!_postKillTargetCanceled)
        {
            TryInject(PacketBuilder.BuildTargetCancel(), "post-kill-cancel-target");
            _postKillTargetCanceled = true;
            _nextPostKillActionAt = now.AddMilliseconds(100);
            return true;
        }

        if (TryRunPostKillLootBurst(me, now))
        {
            EndPostKill("loot-complete");
            return false;
        }

        return true;
    }
    private bool TryRunPostKillLootBurst(CharacterState me, DateTime now)
    {
        var maxAttempts = Math.Max(1, Settings.PostKillLootMaxAttempts);
        if (Settings.CombatMode == CombatMode.HybridFsmPriority)
        {
            maxAttempts = Math.Max(maxAttempts, 14);
        }

        if (Settings.MoveToLoot)
        {
            var farCorpseRange = Math.Max(160, Settings.LootPickupRange + 90);
            var farCorpseRangeSq = (long)farCorpseRange * farCorpseRange;
            var distToCorpseSq = DistanceSq(me.X, me.Y, _postKillX, _postKillY);
            if (distToCorpseSq > farCorpseRangeSq)
            {
                maxAttempts += 6;
            }
        }

        if (_postKillLootActions >= maxAttempts)
        {
            var hardAttempts = maxAttempts + (Settings.BattleMode == BotBattleMode.StrictCaster ? 10 : 8);
            var shouldExtend = _postKillItemsSeen > _postKillItemsPicked
                && _postKillLootActions < hardAttempts;
            if (shouldExtend)
            {
                _nextPostKillActionAt = now.AddMilliseconds(180);
                return false;
            }

            return true;
        }

        if (!TryGetPostKillLootItem(me, out var item, out var distFromMeSq))
        {
            var corpsePickupRange = Math.Max(70, Settings.LootPickupRange);
            var corpsePickupRangeSq = (long)corpsePickupRange * corpsePickupRange;
            var maxMoveToLoot = Settings.BattleMode == BotBattleMode.StrictCaster
                ? Math.Max(1600, Math.Max(Settings.LootRange, corpsePickupRange) + 620)
                : Math.Max(700, Math.Max(Settings.LootRange, corpsePickupRange) + 320);
            var maxMoveToLootSq = (long)maxMoveToLoot * maxMoveToLoot;
            var distToKillSq = DistanceSq(me.X, me.Y, _postKillX, _postKillY);

            if (distToKillSq <= corpsePickupRangeSq)
            {
                _postKillReachedCorpseZone = true;
            }

            if (Settings.MoveToLoot
                && distToKillSq > corpsePickupRangeSq
                && distToKillSq <= maxMoveToLootSq
                && TryMoveTo(_postKillX, _postKillY, _postKillZ))
            {
                _postKillMoveToCorpseAttempts++;
                // Movement doesn't burn pickup attempts — only actual pickups/polls at corpse count
                SetCombatPhase("post-kill-move-to-corpse");
                _nextPostKillActionAt = now.AddMilliseconds(Settings.BattleMode == BotBattleMode.StrictCaster ? 300 : 240);
                return false;
            }

            _postKillEmptyPolls++;
            // Empty polls only count toward maxAttempts once we've reached the corpse zone
            if (_postKillReachedCorpseZone)
            {
                _postKillLootActions++;
            }
            SetCombatPhase("post-kill-loot-poll");
            // At corpse zone: poll faster (80ms) and stop sooner (3 empty polls = ~240ms)
            _nextPostKillActionAt = now.AddMilliseconds(_postKillReachedCorpseZone ? 80 : 180);

            var emptyPollLimit = _postKillReachedCorpseZone ? 3 : (Settings.BattleMode == BotBattleMode.StrictCaster ? 10 : 5);
            var hadCorpseAttempt = _postKillReachedCorpseZone || _postKillMoveToCorpseAttempts > 0 || !Settings.MoveToLoot;
            var hasSeenButNotResolved = _postKillItemsSeen > 0 && _postKillItemsPicked == 0;
            if (hasSeenButNotResolved)
            {
                return false;
            }

            return _postKillEmptyPolls >= emptyPollLimit && hadCorpseAttempt && now >= _postKillMinWaitForLootUntilUtc;
        }

        _postKillEmptyPolls = 0;
        if (_postKillSeenItemIds.Add(item.ObjectId))
        {
            _postKillItemsSeen++;
        }

        var perItemRetry = Math.Max(1, Settings.PostKillLootItemRetry);
        if (Settings.CombatMode == CombatMode.HybridFsmPriority)
        {
            perItemRetry = Math.Max(perItemRetry, 3);
        }

        var usedAttempts = _postKillLootItemAttempts.GetValueOrDefault(item.ObjectId);
        if (usedAttempts >= perItemRetry)
        {
            _postKillItemsSkipped++;
            _postKillLootActions++;
            _nextPostKillActionAt = now.AddMilliseconds(120);
            return false;
        }

        var pickupRangeNear = Math.Max(70, Settings.LootPickupRange);
        var pickupRangeNearSq = (long)pickupRangeNear * pickupRangeNear;

        if (distFromMeSq > pickupRangeNearSq)
        {
            // For caster: don't chase scattered loot — stand still, TickAutoLoot picks up the rest.
            // For melee: move up to a short radius (pickupRange + 200) so we collect loot nearby.
            var maxMoveToLoot = Settings.BattleMode == BotBattleMode.StrictCaster
                ? Math.Max(300, pickupRangeNear + 150)  // caster: short hop only, don't chase far scattered items
                : Math.Max(400, pickupRangeNear + 200);
            var maxMoveToLootSq = (long)maxMoveToLoot * maxMoveToLoot;
            if (distFromMeSq <= maxMoveToLootSq && Settings.MoveToLoot && TryMoveTo(item.X, item.Y, item.Z))
            {
                SetCombatPhase("post-kill-move-to-loot");
                _nextPostKillActionAt = now.AddMilliseconds(220);
                return false;
            }

            _postKillLootItemAttempts[item.ObjectId] = perItemRetry;
            _postKillItemsSkipped++;
            _postKillLootActions++;
            SetCombatPhase("post-kill-skip-far-loot");
            _nextPostKillActionAt = now.AddMilliseconds(180);
            return false;
        }

        _postKillLootItemAttempts[item.ObjectId] = usedAttempts + 1;
        _postKillItemsPicked++;
        _postKillLootActions++;

        if (!TryInject(PacketBuilder.BuildAction(item.ObjectId, me.X, me.Y, me.Z, 0), "post-kill-loot-pickup-action"))
        {
            TryInject(PacketBuilder.BuildGetItem(item.X, item.Y, item.Z, item.ObjectId), "post-kill-loot-pickup-48");
        }

        SetCombatPhase("post-kill-loot");
        _nextPostKillActionAt = now.AddMilliseconds(_postKillReachedCorpseZone ? 80 : 180);
        return false;
    }
    private bool TryGetPostKillLootItem(CharacterState me, out GroundItemState item, out long distFromMeSq)
    {
        item = null!;
        distFromMeSq = 0;

        var perItemRetry = Math.Max(1, Settings.PostKillLootItemRetry);
        if (Settings.CombatMode == CombatMode.HybridFsmPriority)
        {
            perItemRetry = Math.Max(perItemRetry, 3);
        }
        var corpsePickupRange = Math.Max(70, Settings.LootPickupRange);
        var maxMoveToLoot = Settings.BattleMode == BotBattleMode.StrictCaster
                ? Math.Max(300, corpsePickupRange + 150)  // caster: short hop only
                : Math.Max(400, corpsePickupRange + 200);
        var maxMoveToLootSq = (long)maxMoveToLoot * maxMoveToLoot;
        var baseRange = Math.Max(Math.Max(Settings.LootRange, Settings.LootPickupRange), 450);
        var killRange = baseRange + (Settings.BattleMode == BotBattleMode.StrictCaster ? 460 : 220);
        var killRangeSq = (long)killRange * killRange;

        GroundItemState? best = null;
        var bestDistFromMe = long.MaxValue;
        var bestScore = long.MaxValue;

        foreach (var cur in _world.Items.Values)
        {
            if (_postKillLootItemAttempts.GetValueOrDefault(cur.ObjectId) >= perItemRetry)
            {
                continue;
            }

            var distKillSq = DistanceSq(_postKillX, _postKillY, cur.X, cur.Y);
            var distMeSq = DistanceSq(me.X, me.Y, cur.X, cur.Y);
            if (distKillSq > killRangeSq)
            {
                continue;
            }

            if (distMeSq > maxMoveToLootSq)
            {
                continue;
            }

            var herbBoost = IsLikelyHerbItemId(cur.ItemId) ? -220L : 0L;
            var score = distMeSq + distKillSq / 2 + herbBoost;
            if (score < bestScore)
            {
                best = cur;
                bestScore = score;
                bestDistFromMe = distMeSq;
            }
        }

        if (best is null)
        {
            return false;
        }

        item = best;
        distFromMeSq = bestDistFromMe;
        return true;
    }

    private void EndPostKill(string reason)
    {
        if (_postKillTargetObjectId != 0)
        {
            _recentPostKillByTarget[_postKillTargetObjectId] = DateTime.UtcNow;
            _spoilAttemptLocalByTarget.Remove(_postKillTargetObjectId);
            _spoilAttemptCountByTarget.Remove(_postKillTargetObjectId);
            _stallRetargetCountByTarget.Remove(_postKillTargetObjectId);
        }

        var pruneBefore = DateTime.UtcNow.AddSeconds(-20);
        foreach (var kv in _recentPostKillByTarget.Where(x => x.Value < pruneBefore).ToArray())
        {
            _recentPostKillByTarget.Remove(kv.Key);
        }

        var spoilPruneBefore = DateTime.UtcNow.AddMinutes(-2);
        foreach (var kv in _spoilAttemptLocalByTarget.Where(x => x.Value < spoilPruneBefore).ToArray())
        {
            _spoilAttemptLocalByTarget.Remove(kv.Key);
        }

        _log.Info($"[AutoFight] post-kill summary oid=0x{_postKillTargetObjectId:X} reason={reason} seen={_postKillItemsSeen} picked={_postKillItemsPicked} skipped={_postKillItemsSkipped} polls={_postKillEmptyPolls} actions={_postKillLootActions}");
        SetDecision($"post-kill:{reason}");
        SetCombatPhase("post-kill-complete");
        ResetPostKillState();
    }
    private void ObserveCombatProgress(NpcState target, DateTime now)
    {
        var progressed = false;

        if (_combatTargetLastHpPct >= 0 && target.HpPct >= 0 && target.HpPct + 0.05f < _combatTargetLastHpPct)
        {
            progressed = true;
        }

        if (target.LastHitByMeAtUtc != DateTime.MinValue && (_combatLastProgressAtUtc == DateTime.MinValue || target.LastHitByMeAtUtc > _combatLastProgressAtUtc))
        {
            progressed = true;
        }

        if (_lastIncomingDamageAtUtc != DateTime.MinValue
            && (_combatLastProgressAtUtc == DateTime.MinValue || _lastIncomingDamageAtUtc > _combatLastProgressAtUtc))
        {
            progressed = true;
        }

        if (target.IsDead || target.HpPct <= 0.01f)
        {
            if (_spoilPendingTargetObjectId == target.ObjectId)
            {
                ClearSpoilPendingTarget(target.ObjectId);
            }
            progressed = true;
        }

        if (progressed || _combatLastProgressAtUtc == DateTime.MinValue)
        {
            _combatLastProgressAtUtc = now;
            _combatNoProgressStrikes = 0;
            // _killTimeoutFireCount is intentionally NOT reset here — it should only
            // reset when a new target is assigned (ResetCombatState), not on any combat progress.
            // Otherwise hard-abandon never fires on invincible/unkillable mobs.
        }

        _combatTargetLastHpPct = target.HpPct;
    }

    private void TrackIncomingDamage(CharacterState me, DateTime now)
    {
        var curHp = me.CurHp;
        if (curHp <= 0)
        {
            _lastObservedSelfHp = -1;
            return;
        }

        if (_lastObservedSelfHp > 0 && curHp < _lastObservedSelfHp)
        {
            _lastIncomingDamageAtUtc = now;
        }

        _lastObservedSelfHp = curHp;
    }

    private bool TrySendAttack(CharacterState me, int targetObjectId, string actionPrefix)
    {
        if (targetObjectId == 0)
        {
            return false;
        }

        var (tx, ty, tz) = ResolveTargetPosition(me, targetObjectId);
        var now = DateTime.UtcNow;

        if (Settings.AttackPipelineMode == AttackPipelineMode.LegacyAttackRequest)
        {
            if (TryInject(PacketBuilder.BuildAttackRequest(targetObjectId, tx, ty, tz, shift: (byte)(Settings.UseForceAttack ? 1 : 0)), $"{actionPrefix}-0a"))
            {
                _assumedTargetObjectId = targetObjectId;
                _assumedTargetUntilUtc = now.AddMilliseconds(2600);
                return true;
            }

            if (TryInject(PacketBuilder.BuildForceAttack(targetObjectId, tx, ty, tz), $"{actionPrefix}-2f16"))
            {
                _assumedTargetObjectId = targetObjectId;
                _assumedTargetUntilUtc = now.AddMilliseconds(2600);
                return true;
            }

            return false;
        }

        var sent = false;
        var preferAttackRequest = Settings.AttackTransportMode == AttackTransportMode.AutoPrimary04Plus2F && now < _preferAttackRequestUntilUtc;
        var assumedActive = _assumedTargetObjectId == targetObjectId && now <= _assumedTargetUntilUtc;

        if (!preferAttackRequest)
        {
            if (me.TargetId != targetObjectId && !assumedActive && now >= _nextTargetActionAt)
            {
                if (TryInject(PacketBuilder.BuildAction(targetObjectId, me.X, me.Y, me.Z, 0), $"{actionPrefix}-04"))
                {
                    sent = true;
                    _assumedTargetObjectId = targetObjectId;
                    _assumedTargetUntilUtc = now.AddMilliseconds(3000);
                    _nextTargetActionAt = now.AddMilliseconds(820);
                }
            }

            if (TryInject(PacketBuilder.BuildForceAttack(targetObjectId, tx, ty, tz), $"{actionPrefix}-2f16"))
            {
                sent = true;
                _assumedTargetObjectId = targetObjectId;
                _assumedTargetUntilUtc = now.AddMilliseconds(2600);
                if (_nextTargetActionAt < now.AddMilliseconds(620))
                {
                    _nextTargetActionAt = now.AddMilliseconds(620);
                }
            }
        }

        if (preferAttackRequest || Settings.UseAttackRequestFallback || Settings.PreferAttackRequest)
        {
            if (now >= _nextCompatAttackRequestAt
                && TryInject(PacketBuilder.BuildAttackRequest(targetObjectId, tx, ty, tz, shift: (byte)(Settings.UseForceAttack ? 1 : 0)), $"{actionPrefix}-0a-compat"))
            {
                sent = true;
                _nextCompatAttackRequestAt = now.AddMilliseconds(preferAttackRequest ? 650 : 900);
                _assumedTargetObjectId = targetObjectId;
                _assumedTargetUntilUtc = now.AddMilliseconds(2600);
            }
        }

        return sent;
    }

    private void ActivateAttackRequestFallback(DateTime now, string reason)
    {
        if (Settings.AttackTransportMode != AttackTransportMode.AutoPrimary04Plus2F)
        {
            return;
        }

        if (now < _nextTransportSwitchAtUtc)
        {
            return;
        }

        _preferAttackRequestUntilUtc = now.AddMilliseconds(6500);
        _nextTransportSwitchAtUtc = now.AddMilliseconds(8000);
        _log.Info($"[AutoFight] transport-fallback => 0x0A reason={reason} until={_preferAttackRequestUntilUtc:HH:mm:ss.fff}");
    }
    private (int x, int y, int z) ResolveTargetPosition(CharacterState me, int targetObjectId)
    {
        if (_world.Npcs.TryGetValue(targetObjectId, out var npc))
        {
            return (npc.X, npc.Y, npc.Z);
        }

        return (me.X, me.Y, me.Z);
    }

    private static bool IsLikelyMobByNpcId(int npcId)
        => npcId is >= 20400 and < 30000;

    private static bool IsLikelyHerbItemId(int itemId)
        => itemId is >= 8600 and <= 8625;

    private static bool IsNonCombatServiceName(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

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

    private static bool HasCombatEvidence(NpcState npc, DateTime now)
    {
        if (npc.IsDead)
        {
            return true;
        }

        if (npc.HpPct is > 0 and < 99.9f)
        {
            return true;
        }

        if (npc.LastHitByMeAtUtc != DateTime.MinValue && (now - npc.LastHitByMeAtUtc).TotalSeconds <= 60)
        {
            return true;
        }

        if (npc.LastAggroHitAtUtc != DateTime.MinValue && (now - npc.LastAggroHitAtUtc).TotalSeconds <= 25)
        {
            return true;
        }

        return false;
    }

    private bool HasRecentIncomingDamage(DateTime now, int windowMs)
    {
        if (_lastIncomingDamageAtUtc == DateTime.MinValue)
        {
            return false;
        }

        var window = Math.Max(200, windowMs);
        return (now - _lastIncomingDamageAtUtc).TotalMilliseconds <= window;
    }

    private int CountFreshAggroAttackers(CharacterState me, DateTime now)
    {
        var range = Math.Max(900, Settings.FightRange);
        var rangeSq = (long)range * range;
        var targetZ = Math.Max(0, Settings.TargetZRangeMax);
        var count = 0;

        foreach (var npc in _world.Npcs.Values)
        {
            if (!npc.IsAttackable || npc.IsDead)
            {
                continue;
            }

            if (npc.LastAggroHitAtUtc == DateTime.MinValue || (now - npc.LastAggroHitAtUtc).TotalMilliseconds > 2600)
            {
                continue;
            }

            if (targetZ > 0 && Math.Abs(npc.Z - me.Z) > targetZ)
            {
                continue;
            }

            if (DistanceSq(me.X, me.Y, npc.X, npc.Y) > rangeSq)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private NpcState? SelectEmergencyAggroTarget(CharacterState me, DateTime now)
    {
        var fightRange = Math.Max(900, Settings.FightRange);
        var emergencyRange = Math.Max(fightRange, Settings.RetainCurrentTargetMaxDist + 500);
        var emergencyRangeSq = (long)emergencyRange * emergencyRange;
        var targetZ = Math.Max(0, Settings.TargetZRangeMax);

        NpcState? best = null;
        var bestDistSq = long.MaxValue;
        var bestAggroAgeMs = double.MaxValue;

        foreach (var npc in _world.Npcs.Values)
        {
            if (!npc.IsAttackable || npc.IsDead)
            {
                continue;
            }

            if (Settings.SkipSummonedNpcs && npc.IsSummoned)
            {
                continue;
            }

            if (targetZ > 0 && Math.Abs(npc.Z - me.Z) > targetZ)
            {
                continue;
            }

            var aggroAgeMs = npc.LastAggroHitAtUtc == DateTime.MinValue
                ? double.MaxValue
                : (now - npc.LastAggroHitAtUtc).TotalMilliseconds;
            if (aggroAgeMs > 4200)
            {
                continue;
            }

            var distSq = DistanceSq(me.X, me.Y, npc.X, npc.Y);
            if (distSq > emergencyRangeSq)
            {
                continue;
            }

            if (aggroAgeMs < bestAggroAgeMs || (Math.Abs(aggroAgeMs - bestAggroAgeMs) < 1 && distSq < bestDistSq))
            {
                best = npc;
                bestAggroAgeMs = aggroAgeMs;
                bestDistSq = distSq;
            }
        }

        return best;
    }
    private void EnsureHuntCenterAnchor(CharacterState me)
    {
        if (Settings.HuntCenterMode != HuntCenterMode.Anchor)
        {
            return;
        }

        if (Settings.AnchorX != 0 || Settings.AnchorY != 0 || Settings.AnchorZ != 0)
        {
            return;
        }

        if (me.ObjectId == 0)
        {
            return;
        }

        Settings.AnchorX = me.X;
        Settings.AnchorY = me.Y;
        Settings.AnchorZ = me.Z;
        _log.Info($"[AutoFight] hunt anchor set to ({Settings.AnchorX},{Settings.AnchorY},{Settings.AnchorZ})");
    }

    private (int x, int y, int z) GetHuntCenter(CharacterState me)
    {
        if (Settings.HuntCenterMode != HuntCenterMode.Anchor)
        {
            return (me.X, me.Y, me.Z);
        }

        return (Settings.AnchorX, Settings.AnchorY, Settings.AnchorZ);
    }
    private NpcState? SelectFightTarget(CharacterState me, int centerX, int centerY, int centerZ)
    {
        var fightRangeSq = (long)Settings.FightRange * Settings.FightRange;
        var retain = Math.Max(0, Settings.RetainCurrentTargetMaxDist);
        var retainRangeSq = (long)retain * retain;
        var targetZ = Math.Max(0, Settings.TargetZRangeMax);

        var whitelist = Settings.NpcWhitelistIds;
        var blacklist = Settings.NpcBlacklistIds;
        var whitelistOn = Settings.AttackOnlyWhitelistMobs && whitelist.Count > 0;
        var now = DateTime.UtcNow;

        foreach (var kv in _temporarilyIgnoredTargets.Where(x => x.Value <= now).ToArray())
        {
            _temporarilyIgnoredTargets.Remove(kv.Key);
        }

        bool IsCandidate(NpcState npc, long distSq)
        {
            if (!npc.IsAttackable || npc.IsDead)
            {
                return false;
            }

            if (_temporarilyIgnoredTargets.TryGetValue(npc.ObjectId, out var ignoreUntil) && now < ignoreUntil)
            {
                return false;
            }

            if (Settings.SkipSummonedNpcs && npc.IsSummoned)
            {
                return false;
            }

            if (targetZ > 0 && Math.Abs(npc.Z - centerZ) > targetZ)
            {
                return false;
            }

            if (distSq > fightRangeSq)
            {
                return false;
            }

            var npcId = npc.NpcTypeId > 1_000_000 ? npc.NpcTypeId - 1_000_000 : npc.NpcTypeId;
            if (blacklist.Contains(npcId))
            {
                return false;
            }

            if (whitelistOn && !whitelist.Contains(npcId))
            {
                return false;
            }

            var whitelisted = whitelist.Contains(npcId);
            if (!whitelisted)
            {
                var isRecentAggro = npc.LastAggroHitAtUtc != DateTime.MinValue
                    && (now - npc.LastAggroHitAtUtc).TotalSeconds <= AggroRecentWindowSec;
                if (!isRecentAggro && (IsNonCombatServiceName(npc.Name) || IsNonCombatServiceName(npc.Title)))
                {
                    return false;
                }

                var likelyMobId = IsLikelyMobByNpcId(npcId);
                if (!likelyMobId && !isRecentAggro)
                {
                    return false;
                }
            }

            return true;
        }

        var retainOid = me.TargetId != 0 ? me.TargetId : _lastAutoTargetObjectId;
        if (retainOid != 0 && retainRangeSq > 0 && _world.Npcs.TryGetValue(retainOid, out var retained))
        {
            var retainedDistSq = DistanceSq(centerX, centerY, retained.X, retained.Y);
            if (retainedDistSq <= retainRangeSq && IsCandidate(retained, retainedDistSq))
            {
                return retained;
            }
        }

        NpcState? best = null;
        long bestDistSq = long.MaxValue;
        var bestAggroBucket = int.MaxValue;

        foreach (var npc in _world.Npcs.Values)
        {
            var distSq = DistanceSq(centerX, centerY, npc.X, npc.Y);
            if (!IsCandidate(npc, distSq))
            {
                continue;
            }

            if (!Settings.PreferAggroMobs)
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
    private void TickAutoLoot()
    {
        var now = DateTime.UtcNow;
        if (!Settings.AutoLoot || now < _nextLootAt)
        {
            return;
        }

        // AutoFight has dedicated post-kill loot logic; avoid parallel global loot pulses.
        // However, allow fallback loot when fully idle to catch items missed by post-kill.
        if (Settings.AutoFight)
        {
            var idleForLoot = _combatTargetObjectId == 0
                && !_postKillActive
                && !HasRecentIncomingDamage(now, 3500);
            if (!idleForLoot)
                return;
        }

        if (_postKillActive)
        {
            SetDecision("loot-paused-post-kill");
            return;
        }

        var me = _world.Me;
        if (me.IsSitting)
        {
            SetDecision("loot-paused-sitting");
            _nextLootAt = now.AddMilliseconds(800);
            return;
        }

        // Suppress loot during MP recovery to prevent sit/stand spam:
        // picking up items forces the character to stand, which immediately
        // triggers another sit attempt from TickAutoFight/TickMpRest.
        if (Settings.RestEnabled && _combatTargetObjectId == 0 && !_postKillActive && !HasRecentIncomingDamage(now, 2500))
        {
            var mpStandAt = Math.Max(2, Math.Min(100, Settings.StandMpPct));
            if (me.MpPct < mpStandAt)
            {
                SetDecision($"loot-paused-mp-rest mp={me.MpPct:0.#}%<{mpStandAt}%");
                _nextLootAt = now.AddMilliseconds(800);
                return;
            }
        }

        if (_combatTargetObjectId != 0
            && _world.Npcs.TryGetValue(_combatTargetObjectId, out var combatTarget)
            && !combatTarget.IsDead
            && combatTarget.HpPct > 0.01f)
        {
            SetDecision($"loot-paused-combat target=0x{_combatTargetObjectId:X}");
            return;
        }

        var searchRange = Math.Max(Settings.LootRange, Settings.LootPickupRange);
        var searchRangeSq = (long)searchRange * searchRange;

        var item = _world.Items.Values.MinBy(x => DistanceSq(me.X, me.Y, x.X, x.Y));
        if (item is null || DistanceSq(me.X, me.Y, item.X, item.Y) > searchRangeSq)
        {
            _nextLootAt = now.AddMilliseconds(620);
            return;
        }

        var corpsePickupRange = Math.Max(70, Settings.LootPickupRange);
        var corpsePickupRangeSq = (long)corpsePickupRange * corpsePickupRange;
        var itemDistSq = DistanceSq(me.X, me.Y, item.X, item.Y);

        if (itemDistSq > corpsePickupRangeSq)
        {
            if (Settings.MoveToLoot && TryMoveTo(item.X, item.Y, item.Z))
                _nextLootAt = now.AddMilliseconds(320);
            else
                _nextLootAt = now.AddMilliseconds(620);

            return;
        }

        TryInject(PacketBuilder.BuildAction(item.ObjectId, me.X, me.Y, me.Z, 0), "loot-pickup-action");
        _nextLootAt = now.AddMilliseconds(620);
    }
    private bool TickMpRest()
    {
        var now = DateTime.UtcNow;
        if (!Settings.RestEnabled || now < _nextRestToggleAt)
        {
            return false;
        }

        var me = _world.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
        {
            return false;
        }

        // Never toggle sit/stand while combat flow is active.
        if (Settings.AutoFight)
        {
            var inCombatFlow = _combatTargetObjectId != 0
                || _postKillActive
                || HasRecentIncomingDamage(now, 3000)
                || _combatPhase is "target-confirm" or "attack-rotation" or "engage" or "progress-guard";
            if (inCombatFlow)
            {
                if (me.IsSitting && now >= _nextRestToggleAt && TryInject(PacketBuilder.BuildActionUse(0), "rest-stand-combat"))
                {
                    _nextRestToggleAt = now.AddMilliseconds(950);
                    _restExpectedIsSitting = false;
                    _restExpectedStateUntilUtc = now.AddSeconds(4);
                    SetDecision("rest-stand-combat");
                    return true;
                }

                return false;
            }
        }

        if (_restExpectedIsSitting.HasValue)
        {
            if (me.IsSitting == _restExpectedIsSitting.Value)
            {
                _restExpectedIsSitting = null;
                _restExpectedStateUntilUtc = DateTime.MinValue;
            }
            else if (now < _restExpectedStateUntilUtc)
            {
                return false;
            }
            else
            {
                _restExpectedIsSitting = null;
                _restExpectedStateUntilUtc = DateTime.MinValue;
            }
        }

        var sitAt = Math.Max(1, Math.Min(99, Settings.SitMpPct));
        var standAt = Math.Max(sitAt + 1, Math.Min(100, Settings.StandMpPct));
        var mp = me.MpPct;

        if (!me.IsSitting && mp <= sitAt)
        {
            if (!TryInject(PacketBuilder.BuildActionUse(0), "rest-sit"))
            {
                return false;
            }

            _nextRestToggleAt = now.AddMilliseconds(950);
            _nextFightActionAt = now.AddMilliseconds(350);
            _nextLootAt = now.AddMilliseconds(350);
            _restExpectedIsSitting = true;
            _restExpectedStateUntilUtc = now.AddSeconds(4);
            SetDecision($"rest-sit mp={mp:0.#}%");
            return true;
        }

        if (!me.IsSitting || mp < standAt)
        {
            return false;
        }

        if (!TryInject(PacketBuilder.BuildActionUse(0), "rest-stand"))
        {
            return false;
        }

        _nextRestToggleAt = now.AddMilliseconds(950);
        _restExpectedIsSitting = false;
        _restExpectedStateUntilUtc = now.AddSeconds(4);
        SetDecision($"rest-stand mp={mp:0.#}%");
        return true;
    }
    private bool IsSpecialCombatSkill(int skillId)
    {
        if (skillId <= 0)
        {
            return false;
        }

        if (Settings.SpoilEnabled && Settings.SpoilSkillId > 0 && skillId == Settings.SpoilSkillId)
        {
            return true;
        }

        if (Settings.SweepEnabled && Settings.SweepSkillId > 0 && skillId == Settings.SweepSkillId)
        {
            return true;
        }

        return false;
    }
    private bool TryMoveTo(int destX, int destY, int destZ)
    {
        var now = DateTime.UtcNow;
        if (now < _nextMoveAt)
        {
            return false;
        }

        var me = _world.Me;
        var distSq = DistanceSq(me.X, me.Y, destX, destY);
        if (distSq <= 64)
        {
            return false;
        }

        if (!TryInject(PacketBuilder.BuildMoveToLocation(destX, destY, destZ, me.X, me.Y, me.Z), "move"))
        {
            return false;
        }

        _nextMoveAt = now.AddMilliseconds(280);
        return true;
    }

    private bool TryUseCombatRotation()
    {
        var dynamicRules = Settings.AttackSkills
            .Where(x => x.SkillId > 0 && !IsSpecialCombatSkill(x.SkillId))
            .ToList();

        foreach (var rule in dynamicRules)
        {
            if (!TryCastSkill(rule.SkillId, forBuff: false, cooldownOverrideMs: Math.Max(250, rule.CooldownMs)))
            {
                continue;
            }

            return true;
        }

        var fallback = new[] { Settings.CombatSkill1Id, Settings.CombatSkill2Id, Settings.CombatSkill3Id }
            .Where(x => x > 0 && !IsSpecialCombatSkill(x));

        foreach (var skillId in fallback)
        {
            if (!TryCastSkill(skillId, forBuff: false, cooldownOverrideMs: Settings.CombatSkillCooldownMs))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool TryCastSkill(int skillId, bool forBuff, int? cooldownOverrideMs = null, bool allowReservedSkill = false)
    {
        if (skillId <= 0)
        {
            return false;
        }

        if (!forBuff && !allowReservedSkill && IsSpecialCombatSkill(skillId))
        {
            SetDecision($"skill-reserved:{skillId}");
            return false;
        }

        var hasKnownSkill = _world.Skills.ContainsKey(skillId);
        if (!hasKnownSkill)
        {
            var canOptimisticSupportCast = forBuff && !allowReservedSkill;
            if (!canOptimisticSupportCast && !allowReservedSkill)
            {
                SetDecision($"skill-missing:{skillId}");
                return false;
            }

            SetDecision(canOptimisticSupportCast
                ? $"skill-missing-optimistic:{skillId}"
                : $"skill-missing-allow:{skillId}");
        }

        var now = DateTime.UtcNow;
        if (_world.SkillCooldownReadyAtUtc.TryGetValue(skillId, out var serverReadyAt))
        {
            if (now < serverReadyAt)
            {
                _nextSkillById[skillId] = serverReadyAt;
                SetDecision($"skill-cd-server:{skillId}");
                return false;
            }

            if (serverReadyAt <= now.AddMilliseconds(-600))
            {
                _world.SkillCooldownReadyAtUtc.TryRemove(skillId, out _);
            }
        }

        if (_nextSkillById.TryGetValue(skillId, out var nextAllowed) && now < nextAllowed)
        {
            SetDecision($"skill-cd:{skillId}");
            return false;
        }

        var teonPipeline = Settings.AttackPipelineMode == AttackPipelineMode.TeonActionPlus2F;
        var use2F = forBuff
            ? teonPipeline || string.Equals(Settings.BuffSkillPacket, "2f", StringComparison.OrdinalIgnoreCase)
            : teonPipeline || string.Equals(Settings.CombatSkillPacket, "2f", StringComparison.OrdinalIgnoreCase);

        if (use2F)
        {
            if (!TryInject(PacketBuilder.BuildShortcutSkillUse(skillId), $"skill-2f:{skillId}"))
            {
                return false;
            }
        }
        else
        {
            var payloadStyle = Settings.MagicSkillPayload;

            if (!TryInject(PacketBuilder.BuildMagicSkillUse(skillId, payloadStyle: payloadStyle), $"skill-39:{skillId}"))
            {
                return false;
            }
        }

        var cd = Math.Max(250, cooldownOverrideMs ?? Settings.CombatSkillCooldownMs);
        _nextSkillById[skillId] = now.AddMilliseconds(cd);
        return true;
    }

    private void ResetCombatState()
    {
        _combatTargetObjectId = 0;
        _combatTargetLastHpPct = -1f;
        _combatTargetAssignedAtUtc = DateTime.MinValue;
        _combatLastProgressAtUtc = DateTime.MinValue;
        _combatPhase = "idle";
        _nextForceAttackAt = DateTime.MinValue;
        _nextCompatAttackRequestAt = DateTime.MinValue;
        _nextSpoilAt = DateTime.MinValue;
        _nextSweepAt = DateTime.MinValue;
        _nextTargetActionAt = DateTime.MinValue;
        _preferAttackRequestUntilUtc = DateTime.MinValue;
        _nextTransportSwitchAtUtc = DateTime.MinValue;
        _assumedTargetObjectId = 0;
        _assumedTargetUntilUtc = DateTime.MinValue;
        _lastObservedSelfHp = -1;
        _lastIncomingDamageAtUtc = DateTime.MinValue;
        _nextEmergencyRetaliateAt = DateTime.MinValue;
        _combatTargetLastSeenAtUtc = DateTime.MinValue;
        _combatTargetLastX = 0;
        _combatTargetLastY = 0;
        _combatTargetLastZ = 0;
        _combatTargetNpcTypeId = 0;
        _combatTargetSpoilSucceeded = false;
        _combatTargetLastHitByMeAtUtc = DateTime.MinValue;
        _combatTargetHadCombatSignal = false;
        _combatNoProgressStrikes = 0;
        _killTimeoutFireCount = 0;
        _casterChaseStartedAtUtc = DateTime.MinValue;
        _casterStuckStrikes = 0;
        _combatNoServerTargetSinceUtc = DateTime.MinValue;
        _targetConfirmRecoveryStage = 0;
        _targetConfirmRecoveryStartedAtUtc = DateTime.MinValue;
        _criticalHoldActive = false;
        _nextFightActionAt = DateTime.MinValue;
        _nextRestToggleAt = DateTime.MinValue;
        _restExpectedIsSitting = null;
        _restExpectedStateUntilUtc = DateTime.MinValue;
    }

    private void ResetPostKillState()
    {
        _postKillActive = false;
        _postKillTargetObjectId = 0;
        _postKillNpcTypeId = 0;
        _postKillX = 0;
        _postKillY = 0;
        _postKillZ = 0;
        _postKillSpoilSucceeded = false;
        _postKillStartedAtUtc = DateTime.MinValue;
        _postKillSweepUntilUtc = DateTime.MinValue;
        _postKillSpawnWaitUntilUtc = DateTime.MinValue;
        _postKillMinWaitForLootUntilUtc = DateTime.MinValue;
        _nextPostKillActionAt = DateTime.MinValue;
        _postKillLootActions = 0;
        _postKillEmptyPolls = 0;
        _postKillItemsSeen = 0;
        _postKillItemsPicked = 0;
        _postKillItemsSkipped = 0;
        _postKillTargetCanceled = false;
        _postKillSweepAttempts = 0;
        _postKillMoveToCorpseAttempts = 0;
        _postKillReachedCorpseZone = false;
        _postKillLootItemAttempts.Clear();
        _postKillSeenItemIds.Clear();
        _spoilAttemptCountByTarget.Clear();
    }
    private void SetSpoilPendingTarget(int objectId, DateTime now)
    {
        _spoilPendingTargetObjectId = objectId;
        var pendingMs = Settings.SpoilOncePerTarget
            ? Math.Max(12000, Settings.AttackNoProgressWindowMs + 3200)
            : Math.Max(4200, Settings.AttackNoProgressWindowMs + 600);
        _spoilPendingUntilUtc = now.AddMilliseconds(pendingMs);
    }

    private void ClearSpoilPendingTarget(int? objectId = null)
    {
        if (objectId is { } onlyForObjectId && _spoilPendingTargetObjectId != onlyForObjectId)
        {
            return;
        }

        _spoilPendingTargetObjectId = 0;
        _spoilPendingUntilUtc = DateTime.MinValue;
    }
    private void EnsureCoordinatorMode()
    {
        var enabled = Settings.EnableRoleCoordinator;
        var mode = enabled ? Settings.CoordMode : CoordMode.Standalone;
        _coordinator.Configure(enabled, mode, Settings.CoordinatorChannel);
    }
    private bool IsSupportRole()
        => Settings.Role is BotRole.Healer or BotRole.Buffer;
    private bool IsCasterRole()
        => Settings.Role == BotRole.CasterDD || Settings.BattleMode == BotBattleMode.StrictCaster;
    private void PublishLeaderIntent(CharacterState me, int targetOid, string state, DateTime now)
    {
        if (!Settings.EnableRoleCoordinator || Settings.CoordMode != CoordMode.CoordinatorLeader)
        {
            return;
        }
        _coordinatorSequence++;
        _coordinator.Publish(new CombatIntent
        {
            LeaderObjectId = me.ObjectId,
            LeaderTargetOid = targetOid,
            LeaderX = me.X,
            LeaderY = me.Y,
            LeaderZ = me.Z,
            CombatState = state,
            PullTimestampUnixMs = new DateTimeOffset(now).ToUnixTimeMilliseconds(),
            Sequence = _coordinatorSequence
        });
    }
    private bool TryResolveCoordinatorFollowerTarget(CharacterState me, DateTime now, out NpcState? target)
    {
        target = null;
        if (!Settings.EnableRoleCoordinator || Settings.CoordMode != CoordMode.CoordinatorFollower)
        {
            return false;
        }
        if (!_coordinator.TryGetLatestIntent(Settings.CoordinatorStaleMs, out var intent))
        {
            return false;
        }
        if (intent.LeaderTargetOid != 0
            && _world.Npcs.TryGetValue(intent.LeaderTargetOid, out var npc)
            && npc.IsAttackable
            && !npc.IsDead)
        {
            target = npc;
            return true;
        }
        if (TryFollowLeaderIntent(me, intent, now))
        {
            SetCombatPhase("follower-follow-leader");
            SetDecision("follower-follow-leader");
        }
        return true;
    }
    private bool TryFollowLeaderIntent(CharacterState me, CombatIntent intent, DateTime now)
    {
        if (now < _nextCoordinatorFollowAt)
        {
            return false;
        }
        var followDist = Math.Max(120, Settings.FollowDistance);
        var tolerance = Math.Max(30, Settings.FollowTolerance);
        var keepDist = followDist + tolerance;
        var keepDistSq = (long)keepDist * keepDist;
        var distSq = DistanceSq(me.X, me.Y, intent.LeaderX, intent.LeaderY);
        if (distSq <= keepDistSq)
        {
            return false;
        }
        if (!TryMoveTo(intent.LeaderX, intent.LeaderY, intent.LeaderZ))
        {
            return false;
        }
        _nextCoordinatorFollowAt = now.AddMilliseconds(Math.Max(160, Settings.FollowRepathIntervalMs));
        return true;
    }

    private void SetCombatPhase(string phase)
    {
        if (string.Equals(_combatPhase, phase, StringComparison.Ordinal))
        {
            return;
        }

        _combatPhase = phase;
        var now = DateTime.UtcNow;
        if (now < _nextPhaseDiagLogAt)
        {
            return;
        }

        _nextPhaseDiagLogAt = now.AddMilliseconds(650);
        _log.Info($"[AutoFight] phase => {phase}");
    }

    private void SetDecision(string decision)
    {
        _lastDecision = decision;
        _lastDecisionAtUtc = DateTime.UtcNow;
    }

    private bool TryInject(byte[] plainBody, string action)
        => SendCommand(plainBody, action).Status == BotCommandStatus.Sent;

    private BotCommandResult SendCommand(byte[] plainBody, string action)
    {
        if (plainBody.Length == 0)
        {
            _lastCommand = BotCommandResult.Rejected(action, "empty-payload");
            MaybeLogCommandOutcome(_lastCommand);
            return _lastCommand;
        }



        if (_deadStopActive)
        {
            _lastCommand = BotCommandResult.Deferred(action, "dead-stop", plainBody[0]);
            MaybeLogCommandOutcome(_lastCommand);
            return _lastCommand;
        }

        var d = _proxy.Diagnostics;
        if (!IsProxyReady())
        {
            SetDecision($"wait-proxy run={_proxy.IsRunning} gc={d.GameClientConnected} gs={d.GameServerConnected} cry={d.GameCryptoReady}");
            _lastCommand = BotCommandResult.Deferred(action, "proxy-not-ready", plainBody[0]);
            MaybeLogCommandOutcome(_lastCommand);
            return _lastCommand;
        }

        try
        {
            _proxy.InjectToServer(plainBody);
            SetDecision($"{action} 0x{plainBody[0]:X2}");
            _lastCommand = BotCommandResult.Sent(action, plainBody[0]);
            MaybeLogCommandOutcome(_lastCommand);
            return _lastCommand;
        }
        catch (Exception ex)
        {
            _lastCommand = BotCommandResult.Error(action, ex.Message, plainBody[0]);
            MaybeLogCommandOutcome(_lastCommand);
            return _lastCommand;
        }
    }

    private void MaybeLogCommandOutcome(BotCommandResult result)
    {
        var now = DateTime.UtcNow;
        if (result.Status == BotCommandStatus.Sent)
        {
            if (result.Opcode is 0x01 or 0x04 or 0x0A or 0x2F or 0x39 or 0x45 or 0x48 && now >= _nextSentCommandDiagLogAt)
            {
                _nextSentCommandDiagLogAt = now.AddMilliseconds(420);
                _log.Info($"[BotCmd] Sent {result.Action} op=0x{result.Opcode:X2}");
            }

            return;
        }

        if (now < _nextCommandDiagLogAt)
        {
            return;
        }

        _nextCommandDiagLogAt = now.AddMilliseconds(900);
        var d = _proxy.Diagnostics;
        _log.Info($"[BotCmd] {result.Status} {result.Action} op=0x{result.Opcode:X2} reason={result.Reason} stage={d.SessionStage} gc={d.GameClientConnected} gs={d.GameServerConnected} cry={d.GameCryptoReady}");
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
            {
                TransitionRuntime(BotRuntimeState.Stopped, "loop-without-cts");
            }

            return;
        }

        if (IsProxyReady())
        {
            if (_runtimeState != BotRuntimeState.Running)
            {
                TransitionRuntime(BotRuntimeState.Running, "game-crypto-ready");
            }

            return;
        }

        if (_runtimeState != BotRuntimeState.PausedNoSession)
        {
            TransitionRuntime(BotRuntimeState.PausedNoSession, "game-disconnected");
        }
    }

    private void TransitionRuntime(BotRuntimeState next, string reason, bool logTransition = true)
    {
        if (_runtimeState == next && string.Equals(_runtimeReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        _runtimeState = next;
        _runtimeReason = reason;
        _lastRuntimeTransitionAtUtc = DateTime.UtcNow;

        if (logTransition)
        {
            _log.Info($"Bot runtime => {next} ({reason})");
        }
    }

    private static long DistanceSq(int x1, int y1, int x2, int y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return (long)dx * dx + (long)dy * dy;
    }
}










































































































































































































































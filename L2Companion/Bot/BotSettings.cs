using System.Collections.Generic;

namespace L2Companion.Bot;

public enum BotBattleMode
{
    Melee,
    StrictCaster
}

public enum HuntCenterMode
{
    Player,
    Anchor
}

public enum AttackPipelineMode
{
    TeonActionPlus2F,
    LegacyAttackRequest
}

public enum CombatMode
{
    Legacy,
    /// <summary>
    /// Tunes spoil retry policy and post-kill sweep/loot timing — not a full combat state machine.
    /// </summary>
    HybridFsmPriority
}

public enum AttackTransportMode
{
    Manual,
    AutoPrimary04Plus2F
}

public enum BuffTargetScope
{
    Self,
    Party,
    Both
}

public enum PartyHealMode
{
    Group,
    Target
}

public enum BotRole
{
    LeaderDD,
    Spoiler,
    CasterDD,
    Healer,
    Buffer
}

public enum CoordMode
{
    Standalone,
    CoordinatorLeader,
    CoordinatorFollower
}

public sealed class BotSettings
{
    public CombatMode CombatMode { get; set; } = CombatMode.HybridFsmPriority;

    public bool AutoFight { get; set; }
    public bool AutoBuff { get; set; }
    public bool AutoLoot { get; set; } = true;
    public bool GroupBuff { get; set; }
    public bool AutoHeal { get; set; }
    public bool PartySupportEnabled { get; set; } = true;

    public int SelfHealSkillId { get; set; }
    public int GroupHealSkillId { get; set; }
    public int BuffSkillId { get; set; }

    public int HealHpThreshold { get; set; } = 55;
    public int PartyHealHpThreshold { get; set; } = 55;
    public int LootRange { get; set; } = 400;
    public int FightRange { get; set; } = 1200;
    public HuntCenterMode HuntCenterMode { get; set; } = HuntCenterMode.Player;
    public int AnchorX { get; set; }
    public int AnchorY { get; set; }
    public int AnchorZ { get; set; }

    public BotBattleMode BattleMode { get; set; } = BotBattleMode.Melee;

    // Backward-compat bridge for older bindings/state.
    public bool CasterMode
    {
        get => BattleMode == BotBattleMode.StrictCaster;
        set => BattleMode = value ? BotBattleMode.StrictCaster : BotBattleMode.Melee;
    }

    public bool MoveToTarget { get; set; } = true;
    public int MeleeEngageRange { get; set; } = 130;
    public bool MoveToLoot { get; set; } = true;
    public int LootPickupRange { get; set; } = 250;

    public bool RestEnabled { get; set; } = true;
    public int SitMpPct { get; set; } = 15;
    public int StandMpPct { get; set; } = 45;
    /// <summary>Sit when HP% drops below this (0 = HP does not trigger sit).</summary>
    public int RestSitHpPct { get; set; }
    /// <summary>Stand only when HP% reaches this AND MP is recovered (0 = ignore HP for stand decision).</summary>
    public int RestStandHpPct { get; set; }
    public int ChangeWaitTypeSitRaw { get; set; } = 0;

    public bool UseCombatSkills { get; set; }
    public int CombatSkill1Id { get; set; }
    public int CombatSkill2Id { get; set; }
    public int CombatSkill3Id { get; set; }
    public int CombatSkillCooldownMs { get; set; } = 1200;
    public string CombatSkillPacket { get; set; } = "2f";
    public string BuffSkillPacket { get; set; } = "2f";
    public string MagicSkillPayload { get; set; } = "ddd";
    public bool UseForceAttack { get; set; } = true;
    public bool PreferAttackRequest { get; set; }
    public bool CasterFallbackToAttack { get; set; } = true;

    public AttackPipelineMode AttackPipelineMode { get; set; } = AttackPipelineMode.TeonActionPlus2F;
    public AttackTransportMode AttackTransportMode { get; set; } = AttackTransportMode.AutoPrimary04Plus2F;
    public bool UseAttackRequestFallback { get; set; }
    public int AttackNoProgressWindowMs { get; set; } = 4200;
    public int CasterChaseRange { get; set; } = 650;
    public int CasterCastIntervalMs { get; set; } = 520;

    public bool SpoilEnabled { get; set; }
    public int SpoilSkillId { get; set; }
    /// <summary>When false, Spoil retries until success or mob dies (like having Spoil in attack rotation). When true, cap at <see cref="SpoilMaxAttemptsPerTarget"/>.</summary>
    public bool SpoilOncePerTarget { get; set; } = true;
    public int SpoilMaxAttemptsPerTarget { get; set; } = 12;
    /// <summary>Minimum time between Spoil inject attempts in kill loop (opening cast schedules the first window).</summary>
    public int SpoilRetryIntervalMs { get; set; } = 1500;
    /// <summary>Planar distance (same units as FightRange) — no Spoil inject while farther; saves max tries while walking.</summary>
    public int SpoilMaxCastDistance { get; set; } = 600;
    public bool SweepEnabled { get; set; } = true;
    public int SweepSkillId { get; set; } = 42;
    public int SweepRetryWindowMs { get; set; } = 3000;
    public int SweepRetryIntervalMs { get; set; } = 350;
    public bool FinishCurrentTargetBeforeAggroRetarget { get; set; } = true;
    public int KillTimeoutMs { get; set; } = 32000;
    public bool PostKillSweepEnabled { get; set; } = true;
    public int PostKillSweepRetryWindowMs { get; set; } = 3000;
    public int PostKillSweepRetryIntervalMs { get; set; } = 240;
    public int PostKillSweepMaxAttempts { get; set; } = 1;
    public int SweepAttemptsPostKill { get; set; } = 1;
    public int PostKillLootMaxAttempts { get; set; } = 10;
    public int PostKillLootItemRetry { get; set; } = 2;
    public int PostKillSpawnWaitMs { get; set; } = 250;

    public bool PreferAggroMobs { get; set; } = true;
    public int RetainCurrentTargetMaxDist { get; set; } = 650;
    public bool AttackOnlyWhitelistMobs { get; set; }
    public int TargetZRangeMax { get; set; }
    public bool SkipSummonedNpcs { get; set; }

    public HashSet<int> NpcWhitelistIds { get; set; } = [];
    public HashSet<int> NpcBlacklistIds { get; set; } = [];
    public BotRole Role { get; set; } = BotRole.LeaderDD;
    public CoordMode CoordMode { get; set; } = CoordMode.Standalone;
    public bool EnableRoleCoordinator { get; set; }
    /// <summary>
    /// When true, attack rotation uses rule priority ordering and per-skill MP/HP/range/abnormal conditions.
    /// </summary>
    public bool EnableCombatFsmV2 { get; set; } = true;
    public bool VerboseCombatSkillLog { get; set; }
    /// <summary>
    /// Milliseconds to suppress overlapping combat actions after our own MagicSkillLaunched (server-dependent).
    /// </summary>
    public int SelfCastLockDurationMs { get; set; } = 900;
    public bool EnableCasterV2 { get; set; } = true;
    public bool EnableSupportV2 { get; set; } = true;
    public string CoordinatorChannel { get; set; } = "l2companion_combat_v2";
    public int CoordinatorStaleMs { get; set; } = 2600;
    public int FollowDistance { get; set; } = 300;
    public int FollowTolerance { get; set; } = 100;
    public int FollowRepathIntervalMs { get; set; } = 320;
    public bool FollowerFallbackToStandalone { get; set; } = true;
    public bool SupportAllowDamage { get; set; }

    public int CriticalHoldEnterHpPct { get; set; } = 30;
    public int CriticalHoldResumeHpPct { get; set; } = 50;
    public int DeadStopResumeHpPct { get; set; } = 30;

    public bool AutoRecharge { get; set; }

    // Sequential combat timing (ported from Python CombatProfile)
    public int PostTargetDelayMs { get; set; } = 300;
    public int KillPollTickMs { get; set; } = 200;
    public int ReattackIntervalMs { get; set; } = 1500;
    public int BetweenTargetsSleepMs { get; set; } = 45;
    public int IdleNoMobsSleepMs { get; set; } = 1000;
    public int CombatRulesTickMs { get; set; } = 450;
    public int PostKillSweepDelayMs { get; set; } = 120;

    // Post-kill & idle recovery
    public bool PostKillSitEnabled { get; set; } = true;
    public int PostKillSitHpBelowPct { get; set; } = 50;
    public int PostKillStandHpPct { get; set; } = 80;
    public int RecoverySitMpBelowPct { get; set; }
    public int RecoveryStandMpPct { get; set; }
    public int RecoveryMaxWaitSec { get; set; } = 60;
    public int RecoveryStandToggleAttempts { get; set; } = 5;
    public bool IdleSitEnabled { get; set; }
    public int IncomingDamageSitBlockMs { get; set; } = 2500;

    public List<HealRuleSetting> HealRules { get; set; } = [];
    public List<BuffRuleSetting> BuffRules { get; set; } = [];
    public List<PartyHealRuleSetting> PartyHealRules { get; set; } = [];
    public List<AttackSkillSetting> AttackSkills { get; set; } = [];
    public List<RechargeRuleSetting> RechargeRules { get; set; } = [];
}

public sealed class HealRuleSetting
{
    public int SkillId { get; set; }
    public int HpBelowPct { get; set; }
    public int CooldownMs { get; set; } = 900;
    public int MinMpPct { get; set; }
    public bool InFight { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public sealed class AttackSkillSetting
{
    public int SkillId { get; set; }
    public int CooldownMs { get; set; } = 1200;
    public bool Enabled { get; set; } = true;
    /// <summary>Lower runs first when combat FSM v2 is enabled in bot settings.</summary>
    public int Priority { get; set; }
    public int MinMpPct { get; set; }
    public int MaxMpPct { get; set; }
    /// <summary>When &gt; 0, skill is used only if target HP% is at or below this value.</summary>
    public int TargetHpBelowPct { get; set; }
    /// <summary>When &gt; 0, skill is used only if target HP% is at or above this value.</summary>
    public int TargetHpAbovePct { get; set; }
    /// <summary>When &gt; 0, skip if target already has this abnormal (debuff) skill id.</summary>
    public int SkipIfTargetHasAbnormalSkillId { get; set; }
    /// <summary>When &gt; 0, skip if distance to target exceeds this (XY plane, same units as engage range).</summary>
    public int MaxCastRange { get; set; }
}

public sealed class BuffRuleSetting
{
    public int SkillId { get; set; }
    public BuffTargetScope Scope { get; set; } = BuffTargetScope.Self;
    public bool AutoDetect { get; set; } = true;
    public int DelaySec { get; set; } = 18;
    public int MinMpPct { get; set; }
    public bool InFight { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public sealed class PartyHealRuleSetting
{
    public int SkillId { get; set; }
    public PartyHealMode Mode { get; set; } = PartyHealMode.Group;
    public int HpBelowPct { get; set; } = 55;
    public int MinMpPct { get; set; }
    public int CooldownMs { get; set; } = 1200;
    public bool InFight { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public sealed class RechargeRuleSetting
{
    public int SkillId { get; set; }
    public int MpBelowPct { get; set; } = 40;
    public int CooldownMs { get; set; } = 1200;
    public int MinSelfMpPct { get; set; } = 20;
    public bool Enabled { get; set; } = true;
}

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
    public bool AutoLoot { get; set; }
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
    public int LootPickupRange { get; set; } = 150;

    public bool RestEnabled { get; set; } = true;
    public int SitMpPct { get; set; } = 15;
    public int StandMpPct { get; set; } = 45;
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
    public bool CasterFallbackToAttack { get; set; }

    public AttackPipelineMode AttackPipelineMode { get; set; } = AttackPipelineMode.TeonActionPlus2F;
    public AttackTransportMode AttackTransportMode { get; set; } = AttackTransportMode.AutoPrimary04Plus2F;
    public bool UseAttackRequestFallback { get; set; }
    public int AttackNoProgressWindowMs { get; set; } = 4200;
    public int CasterChaseRange { get; set; } = 650;
    public int CasterCastIntervalMs { get; set; } = 520;

    public bool SpoilEnabled { get; set; }
    public int SpoilSkillId { get; set; }
    public bool SpoilOncePerTarget { get; set; } = true;
    public int SpoilMaxAttemptsPerTarget { get; set; } = 2;
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
    public int PostKillSpawnWaitMs { get; set; } = 140;

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
    public bool EnableCombatFsmV2 { get; set; } = true;
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

    public List<HealRuleSetting> HealRules { get; set; } = [];
    public List<BuffRuleSetting> BuffRules { get; set; } = [];
    public List<PartyHealRuleSetting> PartyHealRules { get; set; } = [];
    public List<AttackSkillSetting> AttackSkills { get; set; } = [];
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

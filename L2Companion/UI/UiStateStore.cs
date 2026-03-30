using L2Companion.Bot;
using L2Companion.Proxy;
using System.IO;
using System.Text;
using System.Text.Json;

namespace L2Companion.UI;

// Legacy combined state (kept for one-way migration from old ui_state.json).
public sealed class UiAppState
{
    public string LoginHost { get; set; } = "51.38.238.76";
    public int LoginPort { get; set; } = 2106;
    public string GameHost { get; set; } = "51.38.238.76";
    public int GamePort { get; set; } = 7777;
    public int LocalLoginPort { get; set; } = 2106;
    public int LocalGamePort { get; set; } = 7777;

    public bool AutoFight { get; set; }
    public bool AutoBuff { get; set; }
    public bool AutoLoot { get; set; }
    public bool GroupBuff { get; set; }
    public bool AutoHeal { get; set; }

    public int SelfHealSkillId { get; set; }
    public int GroupHealSkillId { get; set; }
    public int BuffSkillId { get; set; }
    public int HealThreshold { get; set; } = 55;
    public int FightRange { get; set; } = 1200;
    public int LootRange { get; set; } = 400;

    public bool CasterMode { get; set; }
    public BotBattleMode BattleMode { get; set; } = BotBattleMode.Melee;
    public bool RestEnabled { get; set; } = true;
    public int SitMpPct { get; set; } = 15;
    public int StandMpPct { get; set; } = 45;
    public int RestSitHpPct { get; set; }
    public int RestStandHpPct { get; set; }
    public int ChangeWaitTypeSitRaw { get; set; } = 0;

    public HuntCenterMode HuntCenterMode { get; set; } = HuntCenterMode.Player;
    public int AnchorX { get; set; }
    public int AnchorY { get; set; }
    public int AnchorZ { get; set; }

    public bool MoveToTarget { get; set; } = true;
    public int MeleeEngageRange { get; set; } = 130;
    public bool MoveToLoot { get; set; } = true;
    public int LootPickupRange { get; set; } = 150;

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
    public CombatMode CombatMode { get; set; } = CombatMode.HybridFsmPriority;
    public AttackTransportMode AttackTransportMode { get; set; } = AttackTransportMode.AutoPrimary04Plus2F;
    public bool UseAttackRequestFallback { get; set; }
    public int AttackNoProgressWindowMs { get; set; } = 4200;
    public int CasterChaseRange { get; set; } = 650;
    public int CasterCastIntervalMs { get; set; } = 520;

    public BotRole Role { get; set; } = BotRole.LeaderDD;
    public CoordMode CoordMode { get; set; } = CoordMode.Standalone;
    public bool EnableRoleCoordinator { get; set; }
    public bool EnableCombatFsmV2 { get; set; } = true;
    public bool VerboseCombatSkillLog { get; set; }
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

    public bool SpoilEnabled { get; set; }
    public int SpoilSkillId { get; set; }
    public bool SpoilOncePerTarget { get; set; } = true;
    public int SpoilMaxAttemptsPerTarget { get; set; } = 12;
    public int SpoilRetryIntervalMs { get; set; } = 1500;
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
    public int PostKillSpawnWaitMs { get; set; } = 140;

    public bool PreferAggroMobs { get; set; } = true;
    public int RetainCurrentTargetMaxDist { get; set; } = 650;
    public bool AttackOnlyWhitelistMobs { get; set; }
    public int TargetZRangeMax { get; set; }
    public bool SkipSummonedNpcs { get; set; }
    public string NpcWhitelistCsv { get; set; } = string.Empty;
    public string NpcBlacklistCsv { get; set; } = string.Empty;

    public bool PartySupportEnabled { get; set; } = true;
    public int PartyHealHpThreshold { get; set; } = 55;

    public bool OnlyKnownSkills { get; set; } = true;
    public ServerProfileMode ServerProfileMode { get; set; } = ServerProfileMode.AutoDetect;

    public List<HealRuleState> HealRules { get; set; } = [];
    public List<BuffRuleState> BuffRules { get; set; } = [];
    public List<PartyHealRuleState> PartyHealRules { get; set; } = [];
    public List<AttackRuleState> AttackSkills { get; set; } = [];
}

public sealed class GlobalUiState
{
    public string LoginHost { get; set; } = "51.38.238.76";
    public int LoginPort { get; set; } = 2106;
    public string GameHost { get; set; } = "51.38.238.76";
    public int GamePort { get; set; } = 7777;
    public int LocalLoginPort { get; set; } = 2106;
    public int LocalGamePort { get; set; } = 7777;
    public bool OnlyKnownSkills { get; set; } = true;
    public ServerProfileMode ServerProfileMode { get; set; } = ServerProfileMode.AutoDetect;
}

public sealed class CharacterBotState
{
    public bool AutoFight { get; set; }
    public bool AutoBuff { get; set; }
    public bool AutoLoot { get; set; }
    public bool GroupBuff { get; set; }
    public bool AutoHeal { get; set; }
    public bool AutoRecharge { get; set; }

    public int SelfHealSkillId { get; set; }
    public int GroupHealSkillId { get; set; }
    public int BuffSkillId { get; set; }
    public int HealThreshold { get; set; } = 55;
    public int FightRange { get; set; } = 1200;
    public int LootRange { get; set; } = 400;

    public bool CasterMode { get; set; }
    public BotBattleMode BattleMode { get; set; } = BotBattleMode.Melee;
    public bool RestEnabled { get; set; } = true;
    public int SitMpPct { get; set; } = 15;
    public int StandMpPct { get; set; } = 45;
    public int RestSitHpPct { get; set; }
    public int RestStandHpPct { get; set; }
    public int ChangeWaitTypeSitRaw { get; set; } = 0;

    public HuntCenterMode HuntCenterMode { get; set; } = HuntCenterMode.Player;
    public int AnchorX { get; set; }
    public int AnchorY { get; set; }
    public int AnchorZ { get; set; }

    public bool MoveToTarget { get; set; } = true;
    public int MeleeEngageRange { get; set; } = 130;
    public bool MoveToLoot { get; set; } = true;
    public int LootPickupRange { get; set; } = 150;

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
    public CombatMode CombatMode { get; set; } = CombatMode.HybridFsmPriority;
    public AttackTransportMode AttackTransportMode { get; set; } = AttackTransportMode.AutoPrimary04Plus2F;
    public bool UseAttackRequestFallback { get; set; }
    public int AttackNoProgressWindowMs { get; set; } = 4200;
    public int CasterChaseRange { get; set; } = 650;
    public int CasterCastIntervalMs { get; set; } = 520;

    public BotRole Role { get; set; } = BotRole.LeaderDD;
    public CoordMode CoordMode { get; set; } = CoordMode.Standalone;
    public bool EnableRoleCoordinator { get; set; }
    public bool EnableCombatFsmV2 { get; set; } = true;
    public bool VerboseCombatSkillLog { get; set; }
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

    public bool SpoilEnabled { get; set; }
    public int SpoilSkillId { get; set; }
    public bool SpoilOncePerTarget { get; set; } = true;
    public int SpoilMaxAttemptsPerTarget { get; set; } = 12;
    public int SpoilRetryIntervalMs { get; set; } = 1500;
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
    public int PostKillSpawnWaitMs { get; set; } = 140;

    public bool PreferAggroMobs { get; set; } = true;
    public int RetainCurrentTargetMaxDist { get; set; } = 650;
    public bool AttackOnlyWhitelistMobs { get; set; }
    public int TargetZRangeMax { get; set; }
    public bool SkipSummonedNpcs { get; set; }
    public string NpcWhitelistCsv { get; set; } = string.Empty;
    public string NpcBlacklistCsv { get; set; } = string.Empty;

    public bool PartySupportEnabled { get; set; } = true;
    public int PartyHealHpThreshold { get; set; } = 55;

    public List<HealRuleState> HealRules { get; set; } = [];
    public List<BuffRuleState> BuffRules { get; set; } = [];
    public List<PartyHealRuleState> PartyHealRules { get; set; } = [];
    public List<AttackRuleState> AttackSkills { get; set; } = [];
    public List<RechargeRuleState> RechargeRules { get; set; } = [];
}

public sealed class HealRuleState
{
    public int SkillId { get; set; }
    public int HpBelowPct { get; set; }
    public int CooldownMs { get; set; } = 900;
    public int MinMpPct { get; set; }
    public bool InFight { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public sealed class AttackRuleState
{
    public int SkillId { get; set; }
    public int CooldownMs { get; set; } = 1200;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; }
    public int MinMpPct { get; set; }
    public int MaxMpPct { get; set; }
    public int TargetHpBelowPct { get; set; }
    public int TargetHpAbovePct { get; set; }
    public int SkipIfTargetHasAbnormalSkillId { get; set; }
    public int MaxCastRange { get; set; }
}

public sealed class BuffRuleState
{
    public int SkillId { get; set; }
    public BuffTargetScope Scope { get; set; } = BuffTargetScope.Self;
    public bool AutoDetect { get; set; } = true;
    public int DelaySec { get; set; } = 18;
    public int MinMpPct { get; set; }
    public bool InFight { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public sealed class PartyHealRuleState
{
    public int SkillId { get; set; }
    public PartyHealMode Mode { get; set; } = PartyHealMode.Group;
    public int HpBelowPct { get; set; } = 55;
    public int MinMpPct { get; set; }
    public int CooldownMs { get; set; } = 1200;
    public bool InFight { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public sealed class RechargeRuleState
{
    public int SkillId { get; set; }
    public int MpBelowPct { get; set; } = 40;
    public int CooldownMs { get; set; } = 1200;
    public int MinSelfMpPct { get; set; } = 20;
    public bool Enabled { get; set; } = true;
}

public sealed class UiStateStore
{
    private readonly string _configDir;
    private readonly string _legacyPath;
    private readonly string _globalPath;
    private readonly string _charactersDir;

    public UiStateStore(string baseDir)
    {
        _configDir = Path.Combine(baseDir, "Config");
        _legacyPath = Path.Combine(_configDir, "ui_state.json");
        _globalPath = Path.Combine(_configDir, "global_ui_state.json");
        _charactersDir = Path.Combine(_configDir, "characters");
    }

    public GlobalUiState LoadGlobal()
    {
        try
        {
            if (File.Exists(_globalPath))
            {
                var json = File.ReadAllText(_globalPath);
                var state = JsonSerializer.Deserialize<GlobalUiState>(json);
                return state ?? new GlobalUiState();
            }

            var legacy = LoadLegacy();
            return legacy is null ? new GlobalUiState() : MapLegacyGlobal(legacy);
        }
        catch
        {
            return new GlobalUiState();
        }
    }

    public void SaveGlobal(GlobalUiState state)
    {
        try
        {
            Directory.CreateDirectory(_configDir);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_globalPath, json);
        }
        catch
        {
            // ignore
        }
    }

    public CharacterBotState LoadCharacterState(string profileKey)
    {
        try
        {
            var path = GetCharacterPath(profileKey);
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var state = JsonSerializer.Deserialize<CharacterBotState>(json);
                return state ?? new CharacterBotState();
            }

            var legacy = LoadLegacy();
            return legacy is null ? new CharacterBotState() : MapLegacyCharacter(legacy);
        }
        catch
        {
            return new CharacterBotState();
        }
    }

    public void SaveCharacterState(string profileKey, CharacterBotState state)
    {
        try
        {
            Directory.CreateDirectory(_charactersDir);
            var path = GetCharacterPath(profileKey);
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore
        }
    }

    public string BuildProfileKey(string server, string characterName)
    {
        var host = SanitizeSegment(server);
        var name = SanitizeSegment(characterName);
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "unknown-server";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "unknown-character";
        }

        return $"{host}__{name}";
    }

    private string GetCharacterPath(string profileKey)
    {
        var safe = SanitizeSegment(profileKey);
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "unknown-server__unknown-character";
        }

        return Path.Combine(_charactersDir, safe + ".json");
    }

    private UiAppState? LoadLegacy()
    {
        try
        {
            if (!File.Exists(_legacyPath))
            {
                return null;
            }

            var json = File.ReadAllText(_legacyPath);
            return JsonSerializer.Deserialize<UiAppState>(json);
        }
        catch
        {
            return null;
        }
    }

    private static GlobalUiState MapLegacyGlobal(UiAppState legacy)
        => new()
        {
            LoginHost = legacy.LoginHost,
            LoginPort = legacy.LoginPort,
            GameHost = legacy.GameHost,
            GamePort = legacy.GamePort,
            LocalLoginPort = legacy.LocalLoginPort,
            LocalGamePort = legacy.LocalGamePort,
            OnlyKnownSkills = legacy.OnlyKnownSkills,
            ServerProfileMode = legacy.ServerProfileMode,
        };

    private static CharacterBotState MapLegacyCharacter(UiAppState legacy)
        => new()
        {
            AutoFight = legacy.AutoFight,
            AutoBuff = legacy.AutoBuff,
            AutoLoot = legacy.AutoLoot,
            GroupBuff = legacy.GroupBuff,
            AutoHeal = legacy.AutoHeal,
            SelfHealSkillId = legacy.SelfHealSkillId,
            GroupHealSkillId = legacy.GroupHealSkillId,
            BuffSkillId = legacy.BuffSkillId,
            HealThreshold = legacy.HealThreshold,
            FightRange = legacy.FightRange,
            LootRange = legacy.LootRange,
            CasterMode = legacy.CasterMode,
            BattleMode = legacy.BattleMode,
            RestEnabled = legacy.RestEnabled,
            SitMpPct = legacy.SitMpPct,
            StandMpPct = legacy.StandMpPct,
            RestSitHpPct = legacy.RestSitHpPct,
            RestStandHpPct = legacy.RestStandHpPct,
            ChangeWaitTypeSitRaw = legacy.ChangeWaitTypeSitRaw,
            HuntCenterMode = legacy.HuntCenterMode,
            AnchorX = legacy.AnchorX,
            AnchorY = legacy.AnchorY,
            AnchorZ = legacy.AnchorZ,
            MoveToTarget = legacy.MoveToTarget,
            MeleeEngageRange = legacy.MeleeEngageRange,
            MoveToLoot = legacy.MoveToLoot,
            LootPickupRange = legacy.LootPickupRange,
            UseCombatSkills = legacy.UseCombatSkills,
            CombatSkill1Id = legacy.CombatSkill1Id,
            CombatSkill2Id = legacy.CombatSkill2Id,
            CombatSkill3Id = legacy.CombatSkill3Id,
            CombatSkillCooldownMs = legacy.CombatSkillCooldownMs,
            CombatSkillPacket = legacy.CombatSkillPacket,
            BuffSkillPacket = legacy.BuffSkillPacket,
            MagicSkillPayload = legacy.MagicSkillPayload,
            UseForceAttack = legacy.UseForceAttack,
            PreferAttackRequest = legacy.PreferAttackRequest,
            CasterFallbackToAttack = legacy.CasterFallbackToAttack,
            AttackPipelineMode = legacy.AttackPipelineMode,
            CombatMode = legacy.CombatMode,
            AttackTransportMode = legacy.AttackTransportMode,
            UseAttackRequestFallback = legacy.UseAttackRequestFallback,
            AttackNoProgressWindowMs = legacy.AttackNoProgressWindowMs,
            CasterChaseRange = legacy.CasterChaseRange,
            CasterCastIntervalMs = legacy.CasterCastIntervalMs,
            Role = legacy.Role,
            CoordMode = legacy.CoordMode,
            EnableRoleCoordinator = legacy.EnableRoleCoordinator,
            EnableCombatFsmV2 = legacy.EnableCombatFsmV2,
            VerboseCombatSkillLog = legacy.VerboseCombatSkillLog,
            SelfCastLockDurationMs = legacy.SelfCastLockDurationMs > 0 ? legacy.SelfCastLockDurationMs : 900,
            EnableCasterV2 = legacy.EnableCasterV2,
            EnableSupportV2 = legacy.EnableSupportV2,
            CoordinatorChannel = legacy.CoordinatorChannel,
            CoordinatorStaleMs = legacy.CoordinatorStaleMs,
            FollowDistance = legacy.FollowDistance,
            FollowTolerance = legacy.FollowTolerance,
            FollowRepathIntervalMs = legacy.FollowRepathIntervalMs,
            FollowerFallbackToStandalone = legacy.FollowerFallbackToStandalone,
            SupportAllowDamage = legacy.SupportAllowDamage,
            CriticalHoldEnterHpPct = legacy.CriticalHoldEnterHpPct,
            CriticalHoldResumeHpPct = legacy.CriticalHoldResumeHpPct,
            DeadStopResumeHpPct = legacy.DeadStopResumeHpPct,
            SpoilEnabled = legacy.SpoilEnabled,
            SpoilSkillId = legacy.SpoilSkillId,
            SpoilOncePerTarget = legacy.SpoilOncePerTarget,
            SpoilMaxAttemptsPerTarget = legacy.SpoilMaxAttemptsPerTarget,
            SpoilRetryIntervalMs = legacy.SpoilRetryIntervalMs >= 500 ? legacy.SpoilRetryIntervalMs : 1500,
            SpoilMaxCastDistance = legacy.SpoilMaxCastDistance >= 80 ? legacy.SpoilMaxCastDistance : 600,
            SweepEnabled = legacy.SweepEnabled,
            SweepSkillId = legacy.SweepSkillId,
            SweepRetryWindowMs = legacy.SweepRetryWindowMs,
            SweepRetryIntervalMs = legacy.SweepRetryIntervalMs,
            FinishCurrentTargetBeforeAggroRetarget = legacy.FinishCurrentTargetBeforeAggroRetarget,
            KillTimeoutMs = legacy.KillTimeoutMs,
            PostKillSweepEnabled = legacy.PostKillSweepEnabled,
            PostKillSweepRetryWindowMs = legacy.PostKillSweepRetryWindowMs,
            PostKillSweepRetryIntervalMs = legacy.PostKillSweepRetryIntervalMs,
            PostKillSweepMaxAttempts = legacy.PostKillSweepMaxAttempts,
            SweepAttemptsPostKill = legacy.SweepAttemptsPostKill,
            PostKillLootMaxAttempts = legacy.PostKillLootMaxAttempts,
            PostKillLootItemRetry = legacy.PostKillLootItemRetry,
            PostKillSpawnWaitMs = legacy.PostKillSpawnWaitMs,
            PreferAggroMobs = legacy.PreferAggroMobs,
            RetainCurrentTargetMaxDist = legacy.RetainCurrentTargetMaxDist,
            AttackOnlyWhitelistMobs = legacy.AttackOnlyWhitelistMobs,
            TargetZRangeMax = legacy.TargetZRangeMax,
            SkipSummonedNpcs = legacy.SkipSummonedNpcs,
            NpcWhitelistCsv = legacy.NpcWhitelistCsv,
            NpcBlacklistCsv = legacy.NpcBlacklistCsv,
            PartySupportEnabled = legacy.PartySupportEnabled,
            PartyHealHpThreshold = legacy.PartyHealHpThreshold,
            HealRules = legacy.HealRules,
            BuffRules = BuildLegacyBuffRules(legacy),
            PartyHealRules = BuildLegacyPartyHealRules(legacy),
            AttackSkills = legacy.AttackSkills
        };

    private static List<BuffRuleState> BuildLegacyBuffRules(UiAppState legacy)
        => legacy.BuffSkillId > 0
            ? [new BuffRuleState
            {
                SkillId = legacy.BuffSkillId,
                Scope = legacy.GroupBuff ? BuffTargetScope.Both : BuffTargetScope.Self,
                AutoDetect = true,
                DelaySec = 18,
                MinMpPct = 0,
                InFight = true,
                Enabled = true
            }]
            : [];

    private static List<PartyHealRuleState> BuildLegacyPartyHealRules(UiAppState legacy)
        => legacy.GroupHealSkillId > 0
            ? [new PartyHealRuleState
            {
                SkillId = legacy.GroupHealSkillId,
                Mode = PartyHealMode.Group,
                HpBelowPct = Math.Max(1, Math.Min(99, legacy.PartyHealHpThreshold)),
                MinMpPct = 0,
                CooldownMs = 1200,
                InFight = true,
                Enabled = true
            }]
            : [];

    private static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
            }
        }

        var normalized = sb.ToString().Trim('_');
        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return normalized;
    }
}











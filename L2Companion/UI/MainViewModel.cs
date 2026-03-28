
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using L2Companion.Bot;
using L2Companion.Core;
using L2Companion.Protocol;
using L2Companion.Proxy;
using L2Companion.World;

namespace L2Companion.UI;

public sealed class MainViewModel : ObservableObject
{
    private readonly LogService _log;
    private readonly ProxyService _proxy;
    private readonly BotEngine _bot;
    private readonly GameWorldState _world;
    private readonly UiStateStore _stateStore;
    private readonly GameReferenceData _referenceData;

    private OverlayWidgetWindow? _overlay;
    private bool _loadingState;

    private string _loginHost = "51.38.238.76";
    private int _loginPort = 2106;
    private string _gameHost = "51.38.238.76";
    private int _gamePort = 7777;
    private int _localLoginPort = 2106;
    private int _localGamePort = 7777;

    private bool _autoFight;
    private bool _autoBuff;
    private bool _autoLoot;
    private bool _groupBuff;
    private bool _autoHeal;

    private int _selfHealSkillId;
    private int _groupHealSkillId;
    private int _buffSkillId;
    private int _healThreshold = 55;
    private int _fightRange = 1200;
    private int _lootRange = 500;
    private HuntCenterMode _huntCenterMode = HuntCenterMode.Player;
    private int _anchorX;
    private int _anchorY;
    private int _anchorZ;
    private bool _casterMode;
    private BotBattleMode _battleMode = BotBattleMode.Melee;
    private bool _restEnabled = true;
    private int _sitMpPct = 15;
    private int _standMpPct = 45;
    private int _changeWaitTypeSitRaw;
    private bool _partySupportEnabled = true;
    private int _partyHealHpThreshold = 55;
    private bool _moveToTarget = true;
    private int _meleeEngageRange = 130;
    private bool _moveToLoot = true;
    private int _lootPickupRange = 150;

    private bool _useCombatSkills = true;
    private int _combatSkill1Id;
    private int _combatSkill2Id;
    private int _combatSkill3Id;
    private int _combatSkillCooldownMs = 1200;
    private string _combatSkillPacket = "39";
    private string _buffSkillPacket = "2f";
    private string _magicSkillPayload = "ddd";
    private bool _useForceAttack = true;
    private bool _preferAttackRequest;
    private bool _casterFallbackToAttack = true;
    private AttackPipelineMode _attackPipelineMode = AttackPipelineMode.TeonActionPlus2F;
    private CombatMode _combatMode = CombatMode.HybridFsmPriority;
    private AttackTransportMode _attackTransportMode = AttackTransportMode.AutoPrimary04Plus2F;
    private bool _useAttackRequestFallback;
    private int _attackNoProgressWindowMs = 4200;
    private int _casterChaseRange = 650;
    private int _casterCastIntervalMs = 520;
    private BotRole _role = BotRole.LeaderDD;
    private CoordMode _coordMode = CoordMode.Standalone;
    private bool _enableRoleCoordinator;
    private bool _enableCombatFsmV2 = true;
    private bool _enableCasterV2 = true;
    private bool _enableSupportV2 = true;
    private string _coordinatorChannel = "l2companion_combat_v2";
    private int _coordinatorStaleMs = 2600;
    private int _followDistance = 300;
    private int _followTolerance = 100;
    private int _followRepathIntervalMs = 320;
    private bool _followerFallbackToStandalone = true;
    private bool _supportAllowDamage;

    private bool _spoilEnabled;
    private int _spoilSkillId;
    private bool _spoilOncePerTarget = true;
    private bool _sweepEnabled = true;
    private int _sweepSkillId = 42;
    private int _sweepRetryWindowMs = 3000;
    private int _sweepRetryIntervalMs = 350;
    private bool _finishCurrentTargetBeforeAggroRetarget = true;
    private int _killTimeoutMs = 32000;
    private bool _postKillSweepEnabled = true;
    private int _sweepAttemptsPostKill = 1;
    private int _postKillSweepRetryWindowMs = 3000;
    private int _postKillSweepRetryIntervalMs = 240;
    private int _postKillSweepMaxAttempts = 1;
    private int _postKillLootMaxAttempts = 10;
    private int _postKillLootItemRetry = 2;
    private int _postKillSpawnWaitMs = 140;
    private int _spoilMaxAttemptsPerTarget = 2;
    private int _criticalHoldEnterHpPct = 30;
    private int _criticalHoldResumeHpPct = 50;
    private int _deadStopResumeHpPct = 30;

    private int _newBuffSkillId;
    private BuffTargetScope _newBuffScope = BuffTargetScope.Self;
    private int _newBuffDelaySec = 18;
    private int _newBuffMinMpPct;
    private bool _newBuffAutoDetect = true;
    private bool _newBuffInFight = true;
    private BuffRuleRow? _selectedBuffRule;

    private int _newPartyHealSkillId;
    private PartyHealMode _newPartyHealMode = PartyHealMode.Group;
    private int _newPartyHealHpBelowPct = 55;
    private int _newPartyHealMinMpPct;
    private int _newPartyHealCooldownMs = 1200;
    private bool _newPartyHealInFight = true;
    private PartyHealRuleRow? _selectedPartyHealRule;

    private bool _preferAggroMobs = true;
    private int _retainCurrentTargetMaxDist = 650;
    private bool _attackOnlyWhitelistMobs;
    private int _targetZRangeMax;
    private bool _skipSummonedNpcs;
    private string _npcWhitelistCsv = string.Empty;
    private string _npcBlacklistCsv = string.Empty;

    private int _newHealSkillId;
    private int _newHealHpBelowPct = 50;
    private int _newHealCooldownMs = 900;

    private int _newAttackSkillId;
    private int _newAttackCooldownMs = 1200;

    private HealRuleRow? _selectedHealRule;
    private AttackRuleRow? _selectedAttackRule;

    private string _status = "Stopped";
    private DateTime _nextCharacterTablesRefreshAt = DateTime.MinValue;
    private DateTime _nextWorldTablesRefreshAt = DateTime.MinValue;
    private const int CharacterTablesRefreshMs = 1700;
    private const int WorldTablesRefreshMs = 4200;
    private const int SkillPickerCatalogLimit = 280;
    private const int SkillSearchResultLimit = 220;
    private string _lastSkillPickerStamp = string.Empty;
    private string _lastWorldRowsStamp = string.Empty;
    private bool _isStartingProxy;
    private DateTime _lastBotToggleAtUtc = DateTime.MinValue;
    private int _selectedTabIndex;
    private DateTime _lastStateSaveUtc = DateTime.MinValue;
    private bool _stateSaveQueued;
    private const int StateSaveMinIntervalMs = 320;
    private string _skillFilterText = string.Empty;
    private bool _onlyKnownSkills = true;
    private WorldSnapshot _snapshot = WorldSnapshot.Empty;
    private ServerProfileMode _serverProfileMode = ServerProfileMode.AutoDetect;
    private string _activeCharacterProfileKey = string.Empty;
    private string _activeCharacterProfileLabel = "Character profile: pending (wait UserInfo)";
    private string _activeCharacterIdentity = string.Empty;

    public MainViewModel(LogService log, ProxyService proxy, BotEngine bot, GameWorldState world, UiStateStore stateStore)
    {
        _log = log;
        _proxy = proxy;
        _bot = bot;
        _world = world;
        _stateStore = stateStore;
        _referenceData = new GameReferenceData(AppDomain.CurrentDomain.BaseDirectory, log);

        Logs = log.Logs;
        NpcRows = [];
        ItemRows = [];
        PartyRows = [];
        SkillRows = [];
        SkillPickerOptions = [];
        FilteredSkillPickerOptions = [];
        HealRuleRows = [];
        BuffRuleRows = [];
        PartyHealRuleRows = [];
        AttackRuleRows = [];

        CombatSkillPacketOptions = ["39", "2f"];
        BuffSkillPacketOptions = ["39", "2f"];
        ChangeWaitTypeSitRawOptions = [0, 1];
        MagicPayloadOptions = ["ddd", "dcb", "dcc"];
        BuffScopeOptions = [BuffTargetScope.Self, BuffTargetScope.Party, BuffTargetScope.Both];
        PartyHealModeOptions = [PartyHealMode.Group, PartyHealMode.Target];

        StartProxyCommand = new RelayCommand(_ => _ = StartProxyAsync(), _ => !_proxy.IsRunning && !_isStartingProxy);
        StopProxyCommand = new RelayCommand(_ => StopProxy(), _ => _proxy.IsRunning);
        StartBotCommand = new RelayCommand(_ => StartBot(), _ => !_bot.IsRunning);
        StopBotCommand = new RelayCommand(_ => StopBot(), _ => _bot.IsRunning);
        ToggleOverlayCommand = new RelayCommand(_ => ToggleOverlay());
        CopyLogsCommand = new RelayCommand(_ => CopyLogs());
        ExportLogsCommand = new RelayCommand(_ => ExportLogs());
        ClearLogsCommand = new RelayCommand(_ => ClearLogs());
        ProbePacketCommand = new RelayCommand(_ => SendProbePacket());
        ProbeAttackCommand = new RelayCommand(_ => SendAttackProbePacket());

        AddHealRuleCommand = new RelayCommand(_ => AddHealRule(), _ => NewHealSkillId > 0 && NewHealHpBelowPct > 0);
        RemoveHealRuleCommand = new RelayCommand(_ => RemoveSelectedHealRule(), _ => SelectedHealRule is not null);

        AddBuffRuleCommand = new RelayCommand(_ => AddBuffRule(), _ => NewBuffSkillId > 0);
        RemoveBuffRuleCommand = new RelayCommand(_ => RemoveSelectedBuffRule(), _ => SelectedBuffRule is not null);
        MoveBuffUpCommand = new RelayCommand(_ => MoveSelectedBuffRule(-1), _ => CanMoveBuffRule(-1));
        MoveBuffDownCommand = new RelayCommand(_ => MoveSelectedBuffRule(1), _ => CanMoveBuffRule(1));

        AddPartyHealRuleCommand = new RelayCommand(_ => AddPartyHealRule(), _ => NewPartyHealSkillId > 0 && NewPartyHealHpBelowPct > 0);
        RemovePartyHealRuleCommand = new RelayCommand(_ => RemoveSelectedPartyHealRule(), _ => SelectedPartyHealRule is not null);

        AddAttackRuleCommand = new RelayCommand(_ => AddAttackRule(), _ => NewAttackSkillId > 0);
        RemoveAttackRuleCommand = new RelayCommand(_ => RemoveSelectedAttackRule(), _ => SelectedAttackRule is not null);
        MoveAttackUpCommand = new RelayCommand(_ => MoveSelectedAttack(-1), _ => CanMoveAttack(-1));
        MoveAttackDownCommand = new RelayCommand(_ => MoveSelectedAttack(1), _ => CanMoveAttack(1));

        LoadState();
        RefreshSkillPickerOptions([]);
        EnsureRuleNames();
        ApplyBotSettings();
    }

    public ObservableCollection<UiLog> Logs { get; }
    public ObservableCollection<NpcRow> NpcRows { get; }
    public ObservableCollection<ItemRow> ItemRows { get; }
    public ObservableCollection<PartyRow> PartyRows { get; }
    public ObservableCollection<SkillRow> SkillRows { get; }
    public ObservableCollection<SkillOption> SkillPickerOptions { get; }
    public ObservableCollection<SkillOption> FilteredSkillPickerOptions { get; }
    public ObservableCollection<HealRuleRow> HealRuleRows { get; }
    public ObservableCollection<BuffRuleRow> BuffRuleRows { get; }
    public ObservableCollection<PartyHealRuleRow> PartyHealRuleRows { get; }
    public ObservableCollection<AttackRuleRow> AttackRuleRows { get; }

    public IReadOnlyList<string> CombatSkillPacketOptions { get; }
    public IReadOnlyList<string> BuffSkillPacketOptions { get; }
    public IReadOnlyList<int> ChangeWaitTypeSitRawOptions { get; }
    public IReadOnlyList<string> MagicPayloadOptions { get; }
    public IReadOnlyList<BuffTargetScope> BuffScopeOptions { get; }
    public IReadOnlyList<PartyHealMode> PartyHealModeOptions { get; }
    public IReadOnlyList<BotBattleMode> BattleModeOptions { get; } =
    [
        BotBattleMode.Melee,
        BotBattleMode.StrictCaster
    ];
    public IReadOnlyList<HuntCenterMode> HuntCenterModeOptions { get; } =
    [
        HuntCenterMode.Player,
        HuntCenterMode.Anchor
    ];
    public IReadOnlyList<AttackPipelineMode> AttackPipelineModeOptions { get; } =
    [
        AttackPipelineMode.TeonActionPlus2F,
        AttackPipelineMode.LegacyAttackRequest
    ];
    public IReadOnlyList<BotRole> RoleOptions { get; } =
    [
        BotRole.LeaderDD,
        BotRole.Spoiler,
        BotRole.CasterDD,
        BotRole.Healer,
        BotRole.Buffer
    ];
    public IReadOnlyList<CoordMode> CoordModeOptions { get; } =
    [
        CoordMode.Standalone,
        CoordMode.CoordinatorLeader,
        CoordMode.CoordinatorFollower
    ];
    public IReadOnlyList<ServerProfileMode> ServerProfileModeOptions { get; } =
    [
        ServerProfileMode.AutoDetect,
        ServerProfileMode.TeonLike,
        ServerProfileMode.ClassicL2J
    ];

    public ServerProfileMode ServerProfileMode
    {
        get => _serverProfileMode;
        set
        {
            if (!SetProperty(ref _serverProfileMode, value))
            {
                return;
            }

            SaveState();
        }
    }

    public RelayCommand StartProxyCommand { get; }
    public RelayCommand StopProxyCommand { get; }
    public RelayCommand StartBotCommand { get; }
    public RelayCommand StopBotCommand { get; }
    public RelayCommand ToggleOverlayCommand { get; }
    public RelayCommand CopyLogsCommand { get; }
    public RelayCommand ExportLogsCommand { get; }
    public RelayCommand ClearLogsCommand { get; }
    public RelayCommand ProbePacketCommand { get; }
    public RelayCommand ProbeAttackCommand { get; }

    public RelayCommand AddHealRuleCommand { get; }
    public RelayCommand RemoveHealRuleCommand { get; }
    public RelayCommand AddBuffRuleCommand { get; }
    public RelayCommand RemoveBuffRuleCommand { get; }
    public RelayCommand MoveBuffUpCommand { get; }
    public RelayCommand MoveBuffDownCommand { get; }
    public RelayCommand AddPartyHealRuleCommand { get; }
    public RelayCommand RemovePartyHealRuleCommand { get; }
    public RelayCommand AddAttackRuleCommand { get; }
    public RelayCommand RemoveAttackRuleCommand { get; }
    public RelayCommand MoveAttackUpCommand { get; }
    public RelayCommand MoveAttackDownCommand { get; }
    public bool IsSpoilerRole => Role == BotRole.Spoiler;
    public bool SpoilControlsEnabled => IsSpoilerRole;

    private void EnforceRoleBasedSpoilPolicy(bool logChange = true)
    {
        if (IsSpoilerRole)
        {
            return;
        }

        var changed = false;
        if (_spoilEnabled)
        {
            _spoilEnabled = false;
            changed = true;
        }

        if (_sweepEnabled)
        {
            _sweepEnabled = false;
            changed = true;
        }

        if (_postKillSweepEnabled)
        {
            _postKillSweepEnabled = false;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        NotifyPropertyChanged(nameof(SpoilEnabled));
        NotifyPropertyChanged(nameof(SweepEnabled));
        NotifyPropertyChanged(nameof(PostKillSweepEnabled));

        if (logChange && !_loadingState)
        {
            _log.Info("[BotUI] Spoil/Sweep auto-disabled for non-spoiler role.");
        }
    }

    public string LoginHost { get => _loginHost; set { if (SetProperty(ref _loginHost, value)) SaveState(); } }
    public int LoginPort { get => _loginPort; set { if (SetProperty(ref _loginPort, value)) SaveState(); } }
    public string GameHost { get => _gameHost; set { if (SetProperty(ref _gameHost, value)) SaveState(); } }
    public int GamePort { get => _gamePort; set { if (SetProperty(ref _gamePort, value)) SaveState(); } }
    public int LocalLoginPort { get => _localLoginPort; set { if (SetProperty(ref _localLoginPort, value)) SaveState(); } }
    public int LocalGamePort { get => _localGamePort; set { if (SetProperty(ref _localGamePort, value)) SaveState(); } }

    public bool AutoFight { get => _autoFight; set { if (SetProperty(ref _autoFight, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool AutoBuff { get => _autoBuff; set { if (SetProperty(ref _autoBuff, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool AutoLoot { get => _autoLoot; set { if (SetProperty(ref _autoLoot, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool GroupBuff { get => _groupBuff; set { if (SetProperty(ref _groupBuff, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool AutoHeal { get => _autoHeal; set { if (SetProperty(ref _autoHeal, value)) { ApplyBotSettings(); SaveState(); } } }

    public int SelfHealSkillId { get => _selfHealSkillId; set { if (SetProperty(ref _selfHealSkillId, value)) { ApplyBotSettings(); SaveState(); } } }
    public int GroupHealSkillId { get => _groupHealSkillId; set { if (SetProperty(ref _groupHealSkillId, value)) { ApplyBotSettings(); SaveState(); } } }
    public int BuffSkillId { get => _buffSkillId; set { if (SetProperty(ref _buffSkillId, value)) { ApplyBotSettings(); SaveState(); } } }
    public int HealThreshold { get => _healThreshold; set { if (SetProperty(ref _healThreshold, value)) { ApplyBotSettings(); SaveState(); } } }
    public int FightRange { get => _fightRange; set { if (SetProperty(ref _fightRange, value)) { ApplyBotSettings(); SaveState(); } } }
    public int LootRange { get => _lootRange; set { if (SetProperty(ref _lootRange, value)) { ApplyBotSettings(); SaveState(); } } }
    public HuntCenterMode HuntCenterMode
    {
        get => _huntCenterMode;
        set
        {
            if (!SetProperty(ref _huntCenterMode, value))
            {
                return;
            }

            if (value == HuntCenterMode.Player)
            {
                AnchorX = 0;
                AnchorY = 0;
                AnchorZ = 0;
            }
            else if (_snapshot.Me.ObjectId != 0 && AnchorX == 0 && AnchorY == 0 && AnchorZ == 0)
            {
                AnchorX = _snapshot.Me.X;
                AnchorY = _snapshot.Me.Y;
                AnchorZ = _snapshot.Me.Z;
            }

            ApplyBotSettings();
            SaveState();
        }
    }

    public int AnchorX { get => _anchorX; set { if (SetProperty(ref _anchorX, value)) { ApplyBotSettings(); SaveState(); } } }
    public int AnchorY { get => _anchorY; set { if (SetProperty(ref _anchorY, value)) { ApplyBotSettings(); SaveState(); } } }
    public int AnchorZ { get => _anchorZ; set { if (SetProperty(ref _anchorZ, value)) { ApplyBotSettings(); SaveState(); } } }

    public BotBattleMode BattleMode
    {
        get => _battleMode;
        set
        {
            if (!SetProperty(ref _battleMode, value))
            {
                return;
            }

            _casterMode = _battleMode == BotBattleMode.StrictCaster;
            NotifyPropertyChanged(nameof(CasterMode));
            ApplyBotSettings();
            SaveState();
        }
    }

    public bool CasterMode
    {
        get => _casterMode;
        set
        {
            if (!SetProperty(ref _casterMode, value))
            {
                return;
            }

            _battleMode = value ? BotBattleMode.StrictCaster : BotBattleMode.Melee;
            NotifyPropertyChanged(nameof(BattleMode));
            ApplyBotSettings();
            SaveState();
        }
    }

    public bool RestEnabled { get => _restEnabled; set { if (SetProperty(ref _restEnabled, value)) { ApplyBotSettings(); SaveState(); } } }
    public int SitMpPct { get => _sitMpPct; set { if (SetProperty(ref _sitMpPct, value)) { ApplyBotSettings(); SaveState(); } } }
    public int StandMpPct { get => _standMpPct; set { if (SetProperty(ref _standMpPct, value)) { ApplyBotSettings(); SaveState(); } } }
    public int ChangeWaitTypeSitRaw { get => _changeWaitTypeSitRaw; set { if (SetProperty(ref _changeWaitTypeSitRaw, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool PartySupportEnabled { get => _partySupportEnabled; set { if (SetProperty(ref _partySupportEnabled, value)) { ApplyBotSettings(); SaveState(); } } }
    public int PartyHealHpThreshold { get => _partyHealHpThreshold; set { if (SetProperty(ref _partyHealHpThreshold, value)) { ApplyBotSettings(); SaveState(); } } }
    public int DeadStopResumeHpPct { get => _deadStopResumeHpPct; set { if (SetProperty(ref _deadStopResumeHpPct, value)) { ApplyBotSettings(); SaveState(); } } }

    public int NewBuffSkillId
    {
        get => _newBuffSkillId;
        set
        {
            if (!SetProperty(ref _newBuffSkillId, value))
            {
                return;
            }

            AddBuffRuleCommand.RaiseCanExecuteChanged();
        }
    }

    public BuffTargetScope NewBuffScope { get => _newBuffScope; set => SetProperty(ref _newBuffScope, value); }
    public int NewBuffDelaySec { get => _newBuffDelaySec; set => SetProperty(ref _newBuffDelaySec, value); }
    public int NewBuffMinMpPct { get => _newBuffMinMpPct; set => SetProperty(ref _newBuffMinMpPct, value); }
    public bool NewBuffAutoDetect { get => _newBuffAutoDetect; set => SetProperty(ref _newBuffAutoDetect, value); }
    public bool NewBuffInFight { get => _newBuffInFight; set => SetProperty(ref _newBuffInFight, value); }

    public int NewPartyHealSkillId
    {
        get => _newPartyHealSkillId;
        set
        {
            if (!SetProperty(ref _newPartyHealSkillId, value))
            {
                return;
            }

            AddPartyHealRuleCommand.RaiseCanExecuteChanged();
        }
    }

    public PartyHealMode NewPartyHealMode { get => _newPartyHealMode; set => SetProperty(ref _newPartyHealMode, value); }

    public int NewPartyHealHpBelowPct
    {
        get => _newPartyHealHpBelowPct;
        set
        {
            if (!SetProperty(ref _newPartyHealHpBelowPct, value))
            {
                return;
            }

            AddPartyHealRuleCommand.RaiseCanExecuteChanged();
        }
    }

    public int NewPartyHealMinMpPct { get => _newPartyHealMinMpPct; set => SetProperty(ref _newPartyHealMinMpPct, value); }
    public int NewPartyHealCooldownMs { get => _newPartyHealCooldownMs; set => SetProperty(ref _newPartyHealCooldownMs, value); }
    public bool NewPartyHealInFight { get => _newPartyHealInFight; set => SetProperty(ref _newPartyHealInFight, value); }

    public bool MoveToTarget { get => _moveToTarget; set { if (SetProperty(ref _moveToTarget, value)) { ApplyBotSettings(); SaveState(); } } }
    public int MeleeEngageRange { get => _meleeEngageRange; set { if (SetProperty(ref _meleeEngageRange, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool MoveToLoot { get => _moveToLoot; set { if (SetProperty(ref _moveToLoot, value)) { ApplyBotSettings(); SaveState(); } } }
    public int LootPickupRange { get => _lootPickupRange; set { if (SetProperty(ref _lootPickupRange, value)) { ApplyBotSettings(); SaveState(); } } }

    public bool UseCombatSkills { get => _useCombatSkills; set { if (SetProperty(ref _useCombatSkills, value)) { ApplyBotSettings(); SaveState(); } } }
    public int CombatSkill1Id { get => _combatSkill1Id; set { if (SetProperty(ref _combatSkill1Id, value)) { ApplyBotSettings(); SaveState(); } } }
    public int CombatSkill2Id { get => _combatSkill2Id; set { if (SetProperty(ref _combatSkill2Id, value)) { ApplyBotSettings(); SaveState(); } } }
    public int CombatSkill3Id { get => _combatSkill3Id; set { if (SetProperty(ref _combatSkill3Id, value)) { ApplyBotSettings(); SaveState(); } } }
    public int CombatSkillCooldownMs { get => _combatSkillCooldownMs; set { if (SetProperty(ref _combatSkillCooldownMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public string CombatSkillPacket { get => _combatSkillPacket; set { if (SetProperty(ref _combatSkillPacket, value)) { ApplyBotSettings(); SaveState(); } } }
    public string BuffSkillPacket { get => _buffSkillPacket; set { if (SetProperty(ref _buffSkillPacket, value)) { ApplyBotSettings(); SaveState(); } } }
    public string MagicSkillPayload { get => _magicSkillPayload; set { if (SetProperty(ref _magicSkillPayload, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool UseForceAttack { get => _useForceAttack; set { if (SetProperty(ref _useForceAttack, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool PreferAttackRequest { get => _preferAttackRequest; set { if (SetProperty(ref _preferAttackRequest, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool CasterFallbackToAttack { get => _casterFallbackToAttack; set { if (SetProperty(ref _casterFallbackToAttack, value)) { ApplyBotSettings(); SaveState(); } } }
    public AttackPipelineMode AttackPipelineMode { get => _attackPipelineMode; set { if (SetProperty(ref _attackPipelineMode, value)) { ApplyBotSettings(); SaveState(); } } }
    public CombatMode CombatMode { get => _combatMode; set { if (SetProperty(ref _combatMode, value)) { ApplyBotSettings(); SaveState(); } } }
    public AttackTransportMode AttackTransportMode { get => _attackTransportMode; set { if (SetProperty(ref _attackTransportMode, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool UseAttackRequestFallback { get => _useAttackRequestFallback; set { if (SetProperty(ref _useAttackRequestFallback, value)) { ApplyBotSettings(); SaveState(); } } }
    public int AttackNoProgressWindowMs { get => _attackNoProgressWindowMs; set { if (SetProperty(ref _attackNoProgressWindowMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public int CasterChaseRange { get => _casterChaseRange; set { if (SetProperty(ref _casterChaseRange, value)) { ApplyBotSettings(); SaveState(); } } }
    public int CasterCastIntervalMs { get => _casterCastIntervalMs; set { if (SetProperty(ref _casterCastIntervalMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public BotRole Role { get => _role; set { if (SetProperty(ref _role, value)) { EnforceRoleBasedSpoilPolicy(); NotifyPropertyChanged(nameof(IsSpoilerRole)); NotifyPropertyChanged(nameof(SpoilControlsEnabled)); ApplyBotSettings(); SaveState(); } } }
    public CoordMode CoordMode
    {
        get => _coordMode;
        set
        {
            if (!SetProperty(ref _coordMode, value))
            {
                return;
            }

            NotifyPropertyChanged(nameof(AssistFollowEnabled));
            NotifyPropertyChanged(nameof(LeaderBroadcastEnabled));
            ApplyBotSettings();
            SaveState();
        }
    }
    public bool EnableRoleCoordinator
    {
        get => _enableRoleCoordinator;
        set
        {
            if (!SetProperty(ref _enableRoleCoordinator, value))
            {
                return;
            }

            NotifyPropertyChanged(nameof(AssistFollowEnabled));
            NotifyPropertyChanged(nameof(LeaderBroadcastEnabled));
            ApplyBotSettings();
            SaveState();
        }
    }
    public bool EnableCombatFsmV2 { get => _enableCombatFsmV2; set { if (SetProperty(ref _enableCombatFsmV2, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool EnableCasterV2 { get => _enableCasterV2; set { if (SetProperty(ref _enableCasterV2, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool EnableSupportV2 { get => _enableSupportV2; set { if (SetProperty(ref _enableSupportV2, value)) { ApplyBotSettings(); SaveState(); } } }
    public string CoordinatorChannel { get => _coordinatorChannel; set { if (SetProperty(ref _coordinatorChannel, value)) { ApplyBotSettings(); SaveState(); } } }
    public int CoordinatorStaleMs { get => _coordinatorStaleMs; set { if (SetProperty(ref _coordinatorStaleMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public int FollowDistance { get => _followDistance; set { if (SetProperty(ref _followDistance, value)) { ApplyBotSettings(); SaveState(); } } }
    public int FollowTolerance { get => _followTolerance; set { if (SetProperty(ref _followTolerance, value)) { ApplyBotSettings(); SaveState(); } } }
    public int FollowRepathIntervalMs { get => _followRepathIntervalMs; set { if (SetProperty(ref _followRepathIntervalMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool FollowerFallbackToStandalone { get => _followerFallbackToStandalone; set { if (SetProperty(ref _followerFallbackToStandalone, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool SupportAllowDamage { get => _supportAllowDamage; set { if (SetProperty(ref _supportAllowDamage, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool AssistFollowEnabled
    {
        get => EnableRoleCoordinator && CoordMode == CoordMode.CoordinatorFollower;
        set
        {
            if (value)
            {
                var changed = false;
                if (!EnableRoleCoordinator)
                {
                    _enableRoleCoordinator = true;
                    NotifyPropertyChanged(nameof(EnableRoleCoordinator));
                    changed = true;
                }

                if (CoordMode != CoordMode.CoordinatorFollower)
                {
                    _coordMode = CoordMode.CoordinatorFollower;
                    NotifyPropertyChanged(nameof(CoordMode));
                    changed = true;
                }

                if (changed)
                {
                    NotifyPropertyChanged(nameof(AssistFollowEnabled));
                    NotifyPropertyChanged(nameof(LeaderBroadcastEnabled));
                    ApplyBotSettings();
                    SaveState();
                }

                return;
            }

            if (!AssistFollowEnabled)
            {
                return;
            }

            _enableRoleCoordinator = false;
            _coordMode = CoordMode.Standalone;
            NotifyPropertyChanged(nameof(EnableRoleCoordinator));
            NotifyPropertyChanged(nameof(CoordMode));
            NotifyPropertyChanged(nameof(AssistFollowEnabled));
            NotifyPropertyChanged(nameof(LeaderBroadcastEnabled));
            ApplyBotSettings();
            SaveState();
        }
    }

    public bool LeaderBroadcastEnabled
    {
        get => EnableRoleCoordinator && CoordMode == CoordMode.CoordinatorLeader;
        set
        {
            if (value)
            {
                var changed = false;
                if (!EnableRoleCoordinator)
                {
                    _enableRoleCoordinator = true;
                    NotifyPropertyChanged(nameof(EnableRoleCoordinator));
                    changed = true;
                }

                if (CoordMode != CoordMode.CoordinatorLeader)
                {
                    _coordMode = CoordMode.CoordinatorLeader;
                    NotifyPropertyChanged(nameof(CoordMode));
                    changed = true;
                }

                if (changed)
                {
                    NotifyPropertyChanged(nameof(AssistFollowEnabled));
                    NotifyPropertyChanged(nameof(LeaderBroadcastEnabled));
                    ApplyBotSettings();
                    SaveState();
                }

                return;
            }

            if (!LeaderBroadcastEnabled)
            {
                return;
            }

            _enableRoleCoordinator = false;
            _coordMode = CoordMode.Standalone;
            NotifyPropertyChanged(nameof(EnableRoleCoordinator));
            NotifyPropertyChanged(nameof(CoordMode));
            NotifyPropertyChanged(nameof(AssistFollowEnabled));
            NotifyPropertyChanged(nameof(LeaderBroadcastEnabled));
            ApplyBotSettings();
            SaveState();
        }
    }

    public bool SpoilEnabled { get => _spoilEnabled; set { var normalized = IsSpoilerRole && value; if (SetProperty(ref _spoilEnabled, normalized)) { ApplyBotSettings(); SaveState(); } } }
    public int SpoilSkillId { get => _spoilSkillId; set { if (SetProperty(ref _spoilSkillId, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool SpoilOncePerTarget { get => _spoilOncePerTarget; set { if (SetProperty(ref _spoilOncePerTarget, value)) { ApplyBotSettings(); SaveState(); } } }
    public int SpoilMaxAttemptsPerTarget { get => _spoilMaxAttemptsPerTarget; set { if (SetProperty(ref _spoilMaxAttemptsPerTarget, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool SweepEnabled { get => _sweepEnabled; set { var normalized = IsSpoilerRole && value; if (SetProperty(ref _sweepEnabled, normalized)) { ApplyBotSettings(); SaveState(); } } }
    public int SweepSkillId { get => _sweepSkillId; set { if (SetProperty(ref _sweepSkillId, value)) { ApplyBotSettings(); SaveState(); } } }
    public int SweepRetryWindowMs { get => _sweepRetryWindowMs; set { if (SetProperty(ref _sweepRetryWindowMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public int SweepRetryIntervalMs { get => _sweepRetryIntervalMs; set { if (SetProperty(ref _sweepRetryIntervalMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool FinishCurrentTargetBeforeAggroRetarget { get => _finishCurrentTargetBeforeAggroRetarget; set { if (SetProperty(ref _finishCurrentTargetBeforeAggroRetarget, value)) { ApplyBotSettings(); SaveState(); } } }
    public int KillTimeoutMs { get => _killTimeoutMs; set { if (SetProperty(ref _killTimeoutMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool PostKillSweepEnabled { get => _postKillSweepEnabled; set { var normalized = IsSpoilerRole && value; if (SetProperty(ref _postKillSweepEnabled, normalized)) { ApplyBotSettings(); SaveState(); } } }
    public int PostKillSweepRetryWindowMs { get => _postKillSweepRetryWindowMs; set { if (SetProperty(ref _postKillSweepRetryWindowMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public int PostKillSweepRetryIntervalMs { get => _postKillSweepRetryIntervalMs; set { if (SetProperty(ref _postKillSweepRetryIntervalMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public int PostKillSweepMaxAttempts { get => _postKillSweepMaxAttempts; set { if (SetProperty(ref _postKillSweepMaxAttempts, value)) { ApplyBotSettings(); SaveState(); } } }
    public int SweepAttemptsPostKill { get => _sweepAttemptsPostKill; set { if (SetProperty(ref _sweepAttemptsPostKill, value)) { ApplyBotSettings(); SaveState(); } } }
    public int PostKillLootMaxAttempts { get => _postKillLootMaxAttempts; set { if (SetProperty(ref _postKillLootMaxAttempts, value)) { ApplyBotSettings(); SaveState(); } } }
    public int PostKillLootItemRetry { get => _postKillLootItemRetry; set { if (SetProperty(ref _postKillLootItemRetry, value)) { ApplyBotSettings(); SaveState(); } } }
    public int PostKillSpawnWaitMs { get => _postKillSpawnWaitMs; set { if (SetProperty(ref _postKillSpawnWaitMs, value)) { ApplyBotSettings(); SaveState(); } } }
    public int CriticalHoldEnterHpPct { get => _criticalHoldEnterHpPct; set { if (SetProperty(ref _criticalHoldEnterHpPct, value)) { ApplyBotSettings(); SaveState(); } } }
    public int CriticalHoldResumeHpPct { get => _criticalHoldResumeHpPct; set { if (SetProperty(ref _criticalHoldResumeHpPct, value)) { ApplyBotSettings(); SaveState(); } } }

    public bool PreferAggroMobs { get => _preferAggroMobs; set { if (SetProperty(ref _preferAggroMobs, value)) { ApplyBotSettings(); SaveState(); } } }
    public int RetainCurrentTargetMaxDist { get => _retainCurrentTargetMaxDist; set { if (SetProperty(ref _retainCurrentTargetMaxDist, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool AttackOnlyWhitelistMobs { get => _attackOnlyWhitelistMobs; set { if (SetProperty(ref _attackOnlyWhitelistMobs, value)) { ApplyBotSettings(); SaveState(); } } }
    public int TargetZRangeMax { get => _targetZRangeMax; set { if (SetProperty(ref _targetZRangeMax, value)) { ApplyBotSettings(); SaveState(); } } }
    public bool SkipSummonedNpcs { get => _skipSummonedNpcs; set { if (SetProperty(ref _skipSummonedNpcs, value)) { ApplyBotSettings(); SaveState(); } } }
    public string NpcWhitelistCsv { get => _npcWhitelistCsv; set { if (SetProperty(ref _npcWhitelistCsv, value)) { ApplyBotSettings(); SaveState(); } } }
    public string NpcBlacklistCsv { get => _npcBlacklistCsv; set { if (SetProperty(ref _npcBlacklistCsv, value)) { ApplyBotSettings(); SaveState(); } } }

    public string SkillFilterText
    {
        get => _skillFilterText;
        set
        {
            if (!SetProperty(ref _skillFilterText, value))
            {
                return;
            }

            if (OnlyKnownSkills)
            {
                RefreshSkillPickerFilter();
            }
            else
            {
                RefreshSkillPickerOptions(SkillRows.ToList());
            }
        }
    }

    public bool OnlyKnownSkills
    {
        get => _onlyKnownSkills;
        set
        {
            if (!SetProperty(ref _onlyKnownSkills, value))
            {
                return;
            }

            RefreshSkillPickerOptions(SkillRows.ToList());
            SaveState();
        }
    }

    public string SkillPickerSummary
    {
        get
        {
            var total = SkillPickerOptions.Count;
            var shown = FilteredSkillPickerOptions.Count;
            if (OnlyKnownSkills && total == 0)
            {
                return "Skills: waiting for SkillList";
            }

            return $"Skill list: {shown}/{total}";
        }
    }
    public int NewHealSkillId
    {
        get => _newHealSkillId;
        set
        {
            if (!SetProperty(ref _newHealSkillId, value))
            {
                return;
            }

            AddHealRuleCommand.RaiseCanExecuteChanged();
        }
    }

    public int NewHealHpBelowPct
    {
        get => _newHealHpBelowPct;
        set
        {
            if (!SetProperty(ref _newHealHpBelowPct, value))
            {
                return;
            }

            AddHealRuleCommand.RaiseCanExecuteChanged();
        }
    }

    public int NewHealCooldownMs { get => _newHealCooldownMs; set => SetProperty(ref _newHealCooldownMs, value); }

    public int NewAttackSkillId
    {
        get => _newAttackSkillId;
        set
        {
            if (!SetProperty(ref _newAttackSkillId, value))
            {
                return;
            }

            AddAttackRuleCommand.RaiseCanExecuteChanged();
        }
    }

    public int NewAttackCooldownMs { get => _newAttackCooldownMs; set => SetProperty(ref _newAttackCooldownMs, value); }

    public HealRuleRow? SelectedHealRule
    {
        get => _selectedHealRule;
        set
        {
            if (!SetProperty(ref _selectedHealRule, value))
            {
                return;
            }

            RemoveHealRuleCommand.RaiseCanExecuteChanged();
        }
    }

    public BuffRuleRow? SelectedBuffRule
    {
        get => _selectedBuffRule;
        set
        {
            if (!SetProperty(ref _selectedBuffRule, value))
            {
                return;
            }

            RemoveBuffRuleCommand.RaiseCanExecuteChanged();
            MoveBuffUpCommand.RaiseCanExecuteChanged();
            MoveBuffDownCommand.RaiseCanExecuteChanged();
        }
    }

    public PartyHealRuleRow? SelectedPartyHealRule
    {
        get => _selectedPartyHealRule;
        set
        {
            if (!SetProperty(ref _selectedPartyHealRule, value))
            {
                return;
            }

            RemovePartyHealRuleCommand.RaiseCanExecuteChanged();
        }
    }
    public AttackRuleRow? SelectedAttackRule
    {
        get => _selectedAttackRule;
        set
        {
            if (!SetProperty(ref _selectedAttackRule, value))
            {
                return;
            }

            RemoveAttackRuleCommand.RaiseCanExecuteChanged();
            MoveAttackUpCommand.RaiseCanExecuteChanged();
            MoveAttackDownCommand.RaiseCanExecuteChanged();
        }
    }

    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value))
            {
                return;
            }

            var now = DateTime.UtcNow;
            _nextCharacterTablesRefreshAt = now.AddMilliseconds(120);
            _nextWorldTablesRefreshAt = now.AddMilliseconds(120);
        }
    }

    public string CharacterSummary
    {
        get
        {
            var me = _snapshot.Me;
            var name = string.IsNullOrWhiteSpace(me.Name) ? "-" : me.Name;
            return $"{name}  Lvl {me.Level}  Class {me.ClassId}  OID 0x{me.ObjectId:X}";
        }
    }

    public string ResourceSummary
    {
        get
        {
            var me = _snapshot.Me;
            return $"HP {me.CurHp}/{me.EffectiveMaxHp} ({me.HpPct:0.#}%)   MP {me.CurMp}/{me.EffectiveMaxMp} ({me.MpPct:0.#}%)   CP {me.CurCp}/{Math.Max(1, me.MaxCp)}";
        }
    }

    public string PositionSummary
    {
        get
        {
            var me = _snapshot.Me;
            return $"X:{me.X}  Y:{me.Y}  Z:{me.Z}  Heading:{me.Heading}";
        }
    }

    public string WorldSummary =>
        $"NPC: {_snapshot.Npcs.Count}  Items: {_snapshot.Items.Count}  Party: {_snapshot.Party.Count}  Skills: {_snapshot.Skills.Count}";

    public string ParserSummary
    {
        get
        {
            var age = (DateTime.UtcNow - _snapshot.LastMutationUtc).TotalSeconds;
            var stale = age > 5 ? "STALE" : "LIVE";
            var xor = _snapshot.SessionOpcodeXorKey.HasValue ? $"0x{_snapshot.SessionOpcodeXorKey:X2}" : "-";
            return $"World stream: {stale} ({age:0.0}s)  XOR key: {xor}  UserInfo:{_snapshot.UserInfoPackets} Status:{_snapshot.StatusPackets} NpcInfo:{_snapshot.NpcPackets}";
        }
    }

    public string ProxyStateSummary
    {
        get
        {
            var d = _proxy.Diagnostics;
            return $"Stage:{d.SessionStage}  Profile:{d.ServerProfile}  Login C:{BoolDot(d.LoginClientConnected)} S:{BoolDot(d.LoginServerConnected)} BF:{BoolDot(d.LoginBlowfishReady)}  |  Game C:{BoolDot(d.GameClientConnected)} S:{BoolDot(d.GameServerConnected)} CR:{BoolDot(d.GameCryptoReady)}";
        }
    }

    public string ProxyTrafficSummary
    {
        get
        {
            var d = _proxy.Diagnostics;
            return $"S2C:{d.S2CPackets} ({d.LastS2COpcode})  C2S:{d.C2SPackets} ({d.LastC2SOpcode})  Inject:{d.InjectPackets} ({d.LastInjectOpcode})  Pending:{d.PendingInjectPackets}  InjectRes:{d.LastInjectResult}  Drop:{d.LastDropReason}";
        }
    }

    public string ProxyEndpointSummary => $"Real endpoint: {_proxy.Diagnostics.RealGameEndpoint}";
    public string LastErrorSummary => $"Last error: {_proxy.Diagnostics.LastError}";
    public string ReferenceSummary => $"Skills: {_referenceData.SkillSourcePath}   Items: {_referenceData.ItemSourcePath}   Npcs: {_referenceData.NpcSourcePath}";
    public string ActiveCharacterProfileSummary => _activeCharacterProfileLabel;
    public string BotRuntimeSummary => _bot.GetRuntimeSummary();
    public string BotCombatSummary => _bot.GetCombatSummary();
    public string BotDecisionSummary => _bot.GetDecisionSummary();
    public string BotTraceSummary => _bot.GetLastCommandTrace();
    public string SessionSummary
    {
        get
        {
            var stats = _snapshot.SessionStats;
            var elapsed = DateTime.UtcNow - stats.SessionStartUtc;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            var sitState = _snapshot.Me.IsSitting ? "SIT" : "STAND";
            return $"Session {elapsed:hh\\:mm\\:ss}  Kills:{stats.Kills}  Loot:{stats.LootPickedCount}  Adena+:{stats.AdenaGained:N0}  State:{sitState}";
        }
    }

    public void RefreshUiTick()
    {
        if (_stateSaveQueued && (DateTime.UtcNow - _lastStateSaveUtc).TotalMilliseconds >= StateSaveMinIntervalMs)
        {
            SaveState();
        }

        _snapshot = _world.CreateSnapshot();
        TryActivateCharacterProfile(_snapshot);

        NotifyPropertyChanged(nameof(CharacterSummary));
        NotifyPropertyChanged(nameof(ResourceSummary));
        NotifyPropertyChanged(nameof(PositionSummary));
        NotifyPropertyChanged(nameof(WorldSummary));
        NotifyPropertyChanged(nameof(ParserSummary));
        NotifyPropertyChanged(nameof(ProxyStateSummary));
        NotifyPropertyChanged(nameof(ProxyTrafficSummary));
        NotifyPropertyChanged(nameof(ProxyEndpointSummary));
        NotifyPropertyChanged(nameof(LastErrorSummary));
        NotifyPropertyChanged(nameof(ReferenceSummary));
        NotifyPropertyChanged(nameof(ActiveCharacterProfileSummary));
        NotifyPropertyChanged(nameof(BotRuntimeSummary));
        NotifyPropertyChanged(nameof(BotCombatSummary));
        NotifyPropertyChanged(nameof(BotDecisionSummary));
        NotifyPropertyChanged(nameof(BotTraceSummary));
        NotifyPropertyChanged(nameof(SessionSummary));

        var now = DateTime.UtcNow;
        if (SelectedTabIndex == 0)
        {
            if (now >= _nextCharacterTablesRefreshAt)
            {
                _nextCharacterTablesRefreshAt = now.AddMilliseconds(CharacterTablesRefreshMs);
                RefreshCharacterTables(_snapshot);
            }

            if (now >= _nextWorldTablesRefreshAt)
            {
                _nextWorldTablesRefreshAt = now.AddMilliseconds(WorldTablesRefreshMs);
                RefreshWorldTables(_snapshot);
            }
        }
        else if (SelectedTabIndex == 1 && now >= _nextCharacterTablesRefreshAt)
        {
            _nextCharacterTablesRefreshAt = now.AddMilliseconds(CharacterTablesRefreshMs);
            RefreshCharacterTables(_snapshot);
        }

        _overlay?.SetStats(_snapshot, _proxy.Diagnostics, _bot.IsRunning);
    }
    public void OnWindowClosing()
    {
        SaveState(force: true);
        _overlay?.Close();
    }

    private void RefreshCharacterTables(WorldSnapshot snapshot)
    {
        var party = snapshot.Party
            .OrderBy(x => x.Name)
            .Take(48)
            .Select(x => new PartyRow
            {
                Name = string.IsNullOrWhiteSpace(x.Name) ? "PartyMember" : x.Name,
                Level = x.Level,
                Hp = x.MaxHp > 0 ? $"{x.CurHp}/{x.MaxHp}" : x.CurHp.ToString(),
                Mp = x.MaxMp > 0 ? $"{x.CurMp}/{x.MaxMp}" : x.CurMp.ToString(),
                Cp = x.MaxCp > 0 ? $"{x.CurCp}/{x.MaxCp}" : x.CurCp.ToString(),
                ObjectId = $"0x{x.ObjectId:X}"
            })
            .ToList();

        var skills = snapshot.Skills
            .OrderBy(x => x.Key)
            .Take(1500)
            .Select(x => new SkillRow
            {
                SkillId = x.Key,
                Name = _referenceData.ResolveSkillName(x.Key),
                Level = x.Value
            })
            .ToList();

        ReplaceCollection(PartyRows, party);
        ReplaceCollection(SkillRows, skills);

        var stamp = BuildSkillStamp(skills);
        if (!string.Equals(stamp, _lastSkillPickerStamp, StringComparison.Ordinal))
        {
            _lastSkillPickerStamp = stamp;
            RefreshSkillPickerOptions(skills);
            EnsureRuleNames();
        }
    }

    private void RefreshWorldTables(WorldSnapshot snapshot)
    {
        var me = snapshot.Me;

        var npcs = snapshot.Npcs
            .Select(x => new
            {
                Npc = x,
                DistSq = DistanceSq(me.X, me.Y, x.X, x.Y)
            })
            .OrderBy(x => x.DistSq)
            .ThenBy(x => x.Npc.ObjectId)
            .Take(35)
            .Select(x =>
            {
                var npcId = x.Npc.NpcTypeId > 1_000_000 ? x.Npc.NpcTypeId - 1_000_000 : x.Npc.NpcTypeId;
                var resolvedName = _referenceData.ResolveNpcName(npcId);
                var name = string.IsNullOrWhiteSpace(x.Npc.Name) || x.Npc.Name.StartsWith("NPC ", StringComparison.Ordinal)
                    ? resolvedName
                    : x.Npc.Name;

                return new NpcRow
                {
                    ObjectId = $"0x{x.Npc.ObjectId:X}",
                    NpcId = npcId,
                    Name = name,
                    Hp = $"{x.Npc.HpPct:0.#}%",
                    Dist = Math.Sqrt(x.DistSq).ToString("0"),
                    State = BuildNpcStateLabel(x.Npc)
                };
            })
            .ToList();

        var items = snapshot.Items
            .Select(x => new
            {
                Item = x,
                DistSq = DistanceSq(me.X, me.Y, x.X, x.Y)
            })
            .OrderBy(x => x.DistSq)
            .ThenBy(x => x.Item.ObjectId)
            .Take(35)
            .Select(x => new ItemRow
            {
                ObjectId = $"0x{x.Item.ObjectId:X}",
                ItemId = x.Item.ItemId,
                Name = _referenceData.ResolveItemName(x.Item.ItemId),
                Count = x.Item.Count,
                Dist = Math.Sqrt(x.DistSq).ToString("0")
            })
            .ToList();

        var worldStamp = $"{snapshot.LastMutationUtc.Ticks}:{snapshot.Npcs.Count}:{snapshot.Items.Count}:{string.Join('|', npcs.Take(6).Select(x => x.ObjectId))}:{string.Join('|', items.Take(6).Select(x => x.ObjectId))}";
        if (string.Equals(worldStamp, _lastWorldRowsStamp, StringComparison.Ordinal))
        {
            return;
        }

        _lastWorldRowsStamp = worldStamp;
        ReplaceCollection(NpcRows, npcs);
        ReplaceCollection(ItemRows, items);
    }
    private void RefreshSkillPickerOptions(IReadOnlyCollection<SkillRow> liveSkills)
    {
        var map = new Dictionary<int, string>();
        var knownIds = liveSkills.Where(x => x.SkillId > 0).Select(x => x.SkillId).ToHashSet();
        var query = (SkillFilterText ?? string.Empty).Trim();

        bool IsAllowed(int skillId)
            => skillId > 0 && (!OnlyKnownSkills || knownIds.Contains(skillId));

        void AddIfAllowed(int skillId)
        {
            if (!IsAllowed(skillId) || map.ContainsKey(skillId))
            {
                return;
            }

            map[skillId] = _referenceData.ResolveSkillName(skillId);
        }

        if (!OnlyKnownSkills)
        {
            var catalog = query.Length > 0
                ? _referenceData.SearchSkills(query, SkillSearchResultLimit)
                : _referenceData.GetSkillCatalog(SkillPickerCatalogLimit);

            foreach (var (sid, name) in catalog)
            {
                if (sid > 0)
                {
                    map[sid] = name;
                }
            }
        }

        foreach (var s in liveSkills.Where(x => x.SkillId > 0))
        {
            map[s.SkillId] = s.Name;
        }

        foreach (var rule in HealRuleRows)
        {
            AddIfAllowed(rule.SkillId);
        }

        foreach (var rule in AttackRuleRows)
        {
            AddIfAllowed(rule.SkillId);
        }

        AddIfAllowed(SelfHealSkillId);
        AddIfAllowed(GroupHealSkillId);
        AddIfAllowed(BuffSkillId);
        AddIfAllowed(CombatSkill1Id);
        AddIfAllowed(CombatSkill2Id);
        AddIfAllowed(CombatSkill3Id);
        AddIfAllowed(SpoilSkillId);
        AddIfAllowed(SweepSkillId);

        var list = map
            .OrderBy(x => x.Key)
            .Select(x => new SkillOption { SkillId = x.Key, Name = x.Value })
            .ToList();

        ReplaceCollection(SkillPickerOptions, list);
        RefreshSkillPickerFilter();

        if (NewHealSkillId == 0 && SkillPickerOptions.Count > 0)
        {
            NewHealSkillId = SkillPickerOptions[0].SkillId;
        }

        if (NewAttackSkillId == 0 && SkillPickerOptions.Count > 0)
        {
            NewAttackSkillId = SkillPickerOptions[0].SkillId;
        }

        NotifyPropertyChanged(nameof(SkillPickerSummary));
    }

    private void RefreshSkillPickerFilter()
    {
        var filtered = SkillPickerOptions
            .Where(SkillMatchesFilter)
            .ToList();

        ReplaceCollection(FilteredSkillPickerOptions, filtered);
        NotifyPropertyChanged(nameof(SkillPickerSummary));
    }

    private bool SkillMatchesFilter(SkillOption option)
    {
        if (option is null)
        {
            return false;
        }

        var query = (SkillFilterText ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return true;
        }

        if (option.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return option.SkillId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureRuleNames()
    {
        foreach (var row in HealRuleRows)
        {
            row.SkillName = ResolveSkillName(row.SkillId);
        }

        foreach (var row in AttackRuleRows)
        {
            row.SkillName = ResolveSkillName(row.SkillId);
        }
    }

    private void AddHealRule()
    {
        if (NewHealSkillId <= 0 || NewHealHpBelowPct <= 0)
        {
            return;
        }

        HealRuleRows.Add(new HealRuleRow
        {
            SkillId = NewHealSkillId,
            SkillName = ResolveSkillName(NewHealSkillId),
            HpBelowPct = Math.Max(1, Math.Min(100, NewHealHpBelowPct)),
            CooldownMs = Math.Max(250, NewHealCooldownMs)
        });

        SortHealRules();
        ApplyBotSettings();
        SaveState();
    }

    private void RemoveSelectedHealRule()
    {
        if (SelectedHealRule is null)
        {
            return;
        }

        HealRuleRows.Remove(SelectedHealRule);
        SelectedHealRule = null;
        ApplyBotSettings();
        SaveState();
    }

    private void SortHealRules()
    {
        var sorted = HealRuleRows.OrderBy(x => x.HpBelowPct).ToList();
        ReplaceCollection(HealRuleRows, sorted);
    }

    private void AddBuffRule()
    {
        if (NewBuffSkillId <= 0)
        {
            return;
        }

        BuffRuleRows.Add(new BuffRuleRow
        {
            SkillId = NewBuffSkillId,
            SkillName = ResolveSkillName(NewBuffSkillId),
            Scope = NewBuffScope,
            AutoDetect = NewBuffAutoDetect,
            DelaySec = Math.Max(3, NewBuffDelaySec),
            MinMpPct = Math.Max(0, Math.Min(99, NewBuffMinMpPct)),
            InFight = NewBuffInFight,
            Enabled = true
        });

        ApplyBotSettings();
        SaveState();
    }

    private void RemoveSelectedBuffRule()
    {
        if (SelectedBuffRule is null)
        {
            return;
        }

        BuffRuleRows.Remove(SelectedBuffRule);
        SelectedBuffRule = null;
        ApplyBotSettings();
        SaveState();
    }

    private bool CanMoveBuffRule(int delta)
    {
        if (SelectedBuffRule is null)
        {
            return false;
        }

        var idx = BuffRuleRows.IndexOf(SelectedBuffRule);
        if (idx < 0)
        {
            return false;
        }

        var target = idx + delta;
        return target >= 0 && target < BuffRuleRows.Count;
    }

    private void MoveSelectedBuffRule(int delta)
    {
        if (SelectedBuffRule is null)
        {
            return;
        }

        var idx = BuffRuleRows.IndexOf(SelectedBuffRule);
        if (idx < 0)
        {
            return;
        }

        var next = idx + delta;
        if (next < 0 || next >= BuffRuleRows.Count)
        {
            return;
        }

        BuffRuleRows.Move(idx, next);
        ApplyBotSettings();
        SaveState();
        MoveBuffUpCommand.RaiseCanExecuteChanged();
        MoveBuffDownCommand.RaiseCanExecuteChanged();
    }

    private void AddPartyHealRule()
    {
        if (NewPartyHealSkillId <= 0 || NewPartyHealHpBelowPct <= 0)
        {
            return;
        }

        PartyHealRuleRows.Add(new PartyHealRuleRow
        {
            SkillId = NewPartyHealSkillId,
            SkillName = ResolveSkillName(NewPartyHealSkillId),
            Mode = NewPartyHealMode,
            HpBelowPct = Math.Max(1, Math.Min(99, NewPartyHealHpBelowPct)),
            MinMpPct = Math.Max(0, Math.Min(99, NewPartyHealMinMpPct)),
            CooldownMs = Math.Max(250, NewPartyHealCooldownMs),
            InFight = NewPartyHealInFight,
            Enabled = true
        });

        ApplyBotSettings();
        SaveState();
    }

    private void RemoveSelectedPartyHealRule()
    {
        if (SelectedPartyHealRule is null)
        {
            return;
        }

        PartyHealRuleRows.Remove(SelectedPartyHealRule);
        SelectedPartyHealRule = null;
        ApplyBotSettings();
        SaveState();
    }
    private void AddAttackRule()
    {
        if (NewAttackSkillId <= 0)
        {
            return;
        }

        AttackRuleRows.Add(new AttackRuleRow
        {
            Order = AttackRuleRows.Count + 1,
            SkillId = NewAttackSkillId,
            SkillName = ResolveSkillName(NewAttackSkillId),
            CooldownMs = Math.Max(250, NewAttackCooldownMs)
        });

        ReindexAttackRules();
        ApplyBotSettings();
        SaveState();
    }

    private void RemoveSelectedAttackRule()
    {
        if (SelectedAttackRule is null)
        {
            return;
        }

        AttackRuleRows.Remove(SelectedAttackRule);
        SelectedAttackRule = null;
        ReindexAttackRules();
        ApplyBotSettings();
        SaveState();
    }

    private bool CanMoveAttack(int delta)
    {
        if (SelectedAttackRule is null)
        {
            return false;
        }

        var idx = AttackRuleRows.IndexOf(SelectedAttackRule);
        if (idx < 0)
        {
            return false;
        }

        var target = idx + delta;
        return target >= 0 && target < AttackRuleRows.Count;
    }

    private void MoveSelectedAttack(int delta)
    {
        if (SelectedAttackRule is null)
        {
            return;
        }

        var idx = AttackRuleRows.IndexOf(SelectedAttackRule);
        if (idx < 0)
        {
            return;
        }

        var next = idx + delta;
        if (next < 0 || next >= AttackRuleRows.Count)
        {
            return;
        }

        AttackRuleRows.Move(idx, next);
        ReindexAttackRules();
        ApplyBotSettings();
        SaveState();
    }

    private void ReindexAttackRules()
    {
        for (var i = 0; i < AttackRuleRows.Count; i++)
        {
            AttackRuleRows[i].Order = i + 1;
        }

        MoveAttackUpCommand.RaiseCanExecuteChanged();
        MoveAttackDownCommand.RaiseCanExecuteChanged();
    }

    private string ResolveSkillName(int skillId)
    {
        var live = SkillRows.FirstOrDefault(x => x.SkillId == skillId);
        return live is not null ? live.Name : _referenceData.ResolveSkillName(skillId);
    }


    private static string BuildNpcStateLabel(NpcSnapshot npc)
    {
        if (npc.IsDead)
        {
            var spoil = npc.SpoilSucceeded ? "SpoilOK" : (npc.SpoilAttempted ? "SpoilTry" : "NoSpoil");
            var sweep = npc.SweepDone ? "SweepDone" : "SweepPending";
            return $"Dead {spoil} {sweep}";
        }

        if (!npc.IsAttackable)
        {
            return "Peace";
        }

        return npc.IsSummoned ? "Summon" : "Attackable";
    }
    private static string BuildSkillStamp(IReadOnlyList<SkillRow> skills)
    {
        if (skills.Count == 0)
        {
            return "0";
        }

        var first = skills[0];
        var last = skills[^1];
        return $"{skills.Count}:{first.SkillId}:{first.Level}:{last.SkillId}:{last.Level}";
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyCollection<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private static string BoolDot(bool ok) => ok ? "OK" : "--";
    public void TryAutoStartProxyOnLaunch()
    {
        if (_proxy.IsRunning || _isStartingProxy)
        {
            return;
        }

        _ = StartProxyAsync();
    }

    private async Task StartProxyAsync()
    {
        if (_isStartingProxy || _proxy.IsRunning)
        {
            return;
        }

        _isStartingProxy = true;
        RaiseCommands();

        try
        {
            await _proxy.StartAsync(new ProxySettings
            {
                LoginHost = LoginHost,
                LoginPort = LoginPort,
                GameHost = GameHost,
                GamePort = GamePort,
                LocalLoginPort = LocalLoginPort,
                LocalGamePort = LocalGamePort,
                ServerProfileMode = ServerProfileMode,
            });

            Status = "Proxy running";
            SaveState();
        }
        catch (Exception ex)
        {
            _log.Info($"Start proxy failed: {ex.Message}");
            Status = "Proxy error";
        }
        finally
        {
            _isStartingProxy = false;
            RaiseCommands();
        }
    }

    private void StopProxy()
    {
        _proxy.Stop();
        Status = "Proxy stopped";
        RaiseCommands();
    }

    private void StartBot()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastBotToggleAtUtc).TotalMilliseconds < 450)
        {
            _log.Info("StartBot ignored: debounce");
            return;
        }

        _lastBotToggleAtUtc = now;
        ApplyBotSettings();
        _log.Info($"StartBot requested: AutoFight={AutoFight} Role={Role} BattleMode={BattleMode} Coord={CoordMode} Spoil={SpoilEnabled} Sweep={SweepEnabled}");
        _bot.Start();
        Status = "Bot running";
        RaiseCommands();
    }

    private void StopBot()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastBotToggleAtUtc).TotalMilliseconds < 450)
        {
            _log.Info("StopBot ignored: debounce");
            return;
        }

        _lastBotToggleAtUtc = now;
        _log.Info("StopBot requested");
        _bot.Stop();
        Status = "Bot stopped";
        RaiseCommands();
    }

    private void ApplyBotSettings()
    {
        _bot.Settings.AutoFight = AutoFight;
        _bot.Settings.AutoBuff = AutoBuff;
        _bot.Settings.AutoLoot = AutoLoot;
        _bot.Settings.GroupBuff = GroupBuff;
        _bot.Settings.AutoHeal = AutoHeal;
        _bot.Settings.SelfHealSkillId = SelfHealSkillId;
        _bot.Settings.GroupHealSkillId = GroupHealSkillId;
        _bot.Settings.BuffSkillId = BuffSkillId;
        _bot.Settings.HealHpThreshold = HealThreshold;
        _bot.Settings.FightRange = Math.Max(100, FightRange);
        _bot.Settings.LootRange = Math.Max(120, LootRange);
        _bot.Settings.HuntCenterMode = HuntCenterMode;
        _bot.Settings.AnchorX = AnchorX;
        _bot.Settings.AnchorY = AnchorY;
        _bot.Settings.AnchorZ = AnchorZ;
        _bot.Settings.BattleMode = BattleMode;
        _bot.Settings.CasterMode = CasterMode;
        _bot.Settings.RestEnabled = RestEnabled;
        _bot.Settings.SitMpPct = Math.Max(1, Math.Min(95, SitMpPct));
        _bot.Settings.StandMpPct = Math.Max(_bot.Settings.SitMpPct + 1, Math.Min(100, StandMpPct));
        _bot.Settings.ChangeWaitTypeSitRaw = NormalizeChangeWaitTypeSitRaw(ChangeWaitTypeSitRaw);
        _world.ChangeWaitTypeSitRaw = _bot.Settings.ChangeWaitTypeSitRaw;
        _bot.Settings.PartySupportEnabled = PartySupportEnabled;
        _bot.Settings.PartyHealHpThreshold = Math.Max(1, Math.Min(99, PartyHealHpThreshold));
        _bot.Settings.MoveToTarget = MoveToTarget;
        _bot.Settings.MeleeEngageRange = Math.Max(70, MeleeEngageRange);
        _bot.Settings.MoveToLoot = MoveToLoot;
        _bot.Settings.LootPickupRange = Math.Max(70, LootPickupRange);

        _bot.Settings.UseCombatSkills = UseCombatSkills;
        _bot.Settings.CombatSkill1Id = CombatSkill1Id;
        _bot.Settings.CombatSkill2Id = CombatSkill2Id;
        _bot.Settings.CombatSkill3Id = CombatSkill3Id;
        _bot.Settings.CombatSkillCooldownMs = Math.Max(250, CombatSkillCooldownMs);
        var normalizedCombatPacket = NormalizeCombatSkillPacket(CombatSkillPacket, AttackPipelineMode);
        var normalizedBuffPacket = NormalizeSkillPacket(BuffSkillPacket, AttackPipelineMode == AttackPipelineMode.TeonActionPlus2F ? "2f" : "39");
        var normalizedMagicPayload = NormalizeMagicSkillPayload(MagicSkillPayload, AttackPipelineMode);
        _bot.Settings.CombatSkillPacket = normalizedCombatPacket;
        _bot.Settings.BuffSkillPacket = normalizedBuffPacket;
        _bot.Settings.MagicSkillPayload = normalizedMagicPayload;
        _bot.Settings.UseForceAttack = UseForceAttack;
        _bot.Settings.PreferAttackRequest = PreferAttackRequest;
        _bot.Settings.CasterFallbackToAttack = CasterFallbackToAttack;
        _bot.Settings.AttackPipelineMode = AttackPipelineMode;
        _bot.Settings.CombatMode = CombatMode;
        _bot.Settings.AttackTransportMode = AttackTransportMode;
        _bot.Settings.UseAttackRequestFallback = UseAttackRequestFallback;
        _bot.Settings.AttackNoProgressWindowMs = Math.Max(3500, AttackNoProgressWindowMs);
        _bot.Settings.CasterChaseRange = Math.Max(220, CasterChaseRange);
        _bot.Settings.CasterCastIntervalMs = Math.Max(280, CasterCastIntervalMs);
        _bot.Settings.Role = Role;
        _bot.Settings.CoordMode = CoordMode;
        _bot.Settings.EnableRoleCoordinator = EnableRoleCoordinator;
        _bot.Settings.EnableCombatFsmV2 = EnableCombatFsmV2;
        _bot.Settings.EnableCasterV2 = EnableCasterV2;
        _bot.Settings.EnableSupportV2 = EnableSupportV2;
        _bot.Settings.CoordinatorChannel = string.IsNullOrWhiteSpace(CoordinatorChannel) ? "l2companion_combat_v2" : CoordinatorChannel.Trim();
        _bot.Settings.CoordinatorStaleMs = Math.Max(500, CoordinatorStaleMs);
        _bot.Settings.FollowDistance = Math.Max(120, FollowDistance);
        _bot.Settings.FollowTolerance = Math.Max(40, FollowTolerance);
        _bot.Settings.FollowRepathIntervalMs = Math.Max(160, FollowRepathIntervalMs);
        _bot.Settings.FollowerFallbackToStandalone = FollowerFallbackToStandalone;
        _bot.Settings.SupportAllowDamage = SupportAllowDamage;

        var roleAllowsSpoil = Role == BotRole.Spoiler;
        _bot.Settings.SpoilEnabled = roleAllowsSpoil && SpoilEnabled;
        _bot.Settings.SpoilSkillId = SpoilSkillId;
        _bot.Settings.SpoilOncePerTarget = SpoilOncePerTarget;
        _bot.Settings.SpoilMaxAttemptsPerTarget = Math.Max(1, SpoilMaxAttemptsPerTarget);
        _bot.Settings.SweepEnabled = roleAllowsSpoil && SweepEnabled;
        _bot.Settings.SweepSkillId = SweepSkillId;
        _bot.Settings.SweepRetryWindowMs = Math.Max(500, SweepRetryWindowMs);
        _bot.Settings.SweepRetryIntervalMs = Math.Max(120, SweepRetryIntervalMs);
        _bot.Settings.FinishCurrentTargetBeforeAggroRetarget = FinishCurrentTargetBeforeAggroRetarget;
        _bot.Settings.KillTimeoutMs = Math.Max(25000, KillTimeoutMs);
        _bot.Settings.PostKillSweepEnabled = roleAllowsSpoil && PostKillSweepEnabled;
        _bot.Settings.PostKillSweepRetryWindowMs = Math.Max(800, PostKillSweepRetryWindowMs);
        _bot.Settings.PostKillSweepRetryIntervalMs = Math.Max(120, PostKillSweepRetryIntervalMs);
        _bot.Settings.PostKillSweepMaxAttempts = Math.Max(1, PostKillSweepMaxAttempts);
        _bot.Settings.SweepAttemptsPostKill = Math.Max(1, SweepAttemptsPostKill);
        _bot.Settings.PostKillLootMaxAttempts = Math.Max(1, PostKillLootMaxAttempts);
        _bot.Settings.PostKillLootItemRetry = Math.Max(1, PostKillLootItemRetry);
        _bot.Settings.PostKillSpawnWaitMs = Math.Max(0, PostKillSpawnWaitMs);
        _bot.Settings.CriticalHoldEnterHpPct = Math.Max(5, Math.Min(95, CriticalHoldEnterHpPct));
        _bot.Settings.CriticalHoldResumeHpPct = Math.Max(_bot.Settings.CriticalHoldEnterHpPct + 1, Math.Min(99, CriticalHoldResumeHpPct));
        _bot.Settings.DeadStopResumeHpPct = Math.Max(1, Math.Min(99, DeadStopResumeHpPct));

        _bot.Settings.PreferAggroMobs = PreferAggroMobs;
        _bot.Settings.RetainCurrentTargetMaxDist = Math.Max(0, RetainCurrentTargetMaxDist);
        _bot.Settings.AttackOnlyWhitelistMobs = AttackOnlyWhitelistMobs;
        _bot.Settings.TargetZRangeMax = Math.Max(0, TargetZRangeMax);
        _bot.Settings.SkipSummonedNpcs = SkipSummonedNpcs;
        _bot.Settings.NpcWhitelistIds = ParseIdSet(NpcWhitelistCsv);
        _bot.Settings.NpcBlacklistIds = ParseIdSet(NpcBlacklistCsv);

        _bot.Settings.HealRules = HealRuleRows
            .Select(x => new HealRuleSetting
            {
                SkillId = x.SkillId,
                HpBelowPct = x.HpBelowPct,
                CooldownMs = Math.Max(250, x.CooldownMs),
                MinMpPct = Math.Max(0, Math.Min(99, x.MinMpPct)),
                InFight = x.InFight,
                Enabled = x.Enabled
            })
            .ToList();

        _bot.Settings.BuffRules = BuffRuleRows
            .Select(x => new BuffRuleSetting
            {
                SkillId = x.SkillId,
                Scope = x.Scope,
                AutoDetect = x.AutoDetect,
                DelaySec = Math.Max(3, x.DelaySec),
                MinMpPct = Math.Max(0, Math.Min(99, x.MinMpPct)),
                InFight = x.InFight,
                Enabled = x.Enabled
            })
            .ToList();

        _bot.Settings.PartyHealRules = PartyHealRuleRows
            .Select(x => new PartyHealRuleSetting
            {
                SkillId = x.SkillId,
                Mode = x.Mode,
                HpBelowPct = Math.Max(1, Math.Min(99, x.HpBelowPct)),
                MinMpPct = Math.Max(0, Math.Min(99, x.MinMpPct)),
                CooldownMs = Math.Max(250, x.CooldownMs),
                InFight = x.InFight,
                Enabled = x.Enabled
            })
            .ToList();

        _bot.Settings.AttackSkills = AttackRuleRows
            .OrderBy(x => x.Order)
            .Select(x => new AttackSkillSetting
            {
                SkillId = x.SkillId,
                CooldownMs = Math.Max(250, x.CooldownMs)
            })
            .ToList();
    }

    private void ToggleOverlay()
    {
        if (_overlay is null)
        {
            _overlay = new OverlayWidgetWindow();
            _overlay.Closed += (_, _) => _overlay = null;
            _overlay.Show();
            _log.Info("Overlay widget opened.");
            return;
        }

        _overlay.Close();
    }

    private void CopyLogs()
    {
        try
        {
            var text = string.Join(Environment.NewLine, Logs.Select(x => $"{x.Time:HH:mm:ss}  {x.Message}"));
            Clipboard.SetText(text);
            _log.Info("Logs copied to clipboard.");
        }
        catch (Exception ex)
        {
            _log.Info($"Copy logs failed: {ex.Message}");
        }
    }

    private void ExportLogs()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dir = Path.Combine(baseDir, "Config");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"logs-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var lines = Logs.Select(x => $"{x.Time:yyyy-MM-dd HH:mm:ss}  {x.Message}").ToArray();
            File.WriteAllLines(path, lines);
            _log.Info($"Logs exported: {path}");
        }
        catch (Exception ex)
        {
            _log.Info($"Export logs failed: {ex.Message}");
        }
    }

    private void SendProbePacket()
    {
        var d = _proxy.Diagnostics;
        if (!_proxy.IsRunning || !d.GameClientConnected || !d.GameServerConnected || !d.GameCryptoReady)
        {
            _log.Info($"Probe skipped: proxy not ready (run={_proxy.IsRunning} gc={d.GameClientConnected} gs={d.GameServerConnected} cry={d.GameCryptoReady})");
            return;
        }

        _proxy.InjectToServer(PacketBuilder.BuildTargetCancel());
        _log.Info("Probe packet sent: RequestTargetCancel (0x37)");
    }

    private void SendAttackProbePacket()
    {
        var d = _proxy.Diagnostics;
        if (!_proxy.IsRunning || !d.GameClientConnected || !d.GameServerConnected || !d.GameCryptoReady)
        {
            _log.Info($"Probe attack skipped: proxy not ready (run={_proxy.IsRunning} gc={d.GameClientConnected} gs={d.GameServerConnected} cry={d.GameCryptoReady})");
            return;
        }

        var me = _snapshot.Me;
        var targetId = me.TargetId;
        var tx = me.X;
        var ty = me.Y;
        var tz = me.Z;

        if (targetId == 0)
        {
            var nearest = _snapshot.Npcs
                .Where(x => x.IsAttackable && !x.IsDead)
                .OrderBy(x => DistanceSq(me.X, me.Y, x.X, x.Y))
                .FirstOrDefault();

            if (nearest is null)
            {
                _log.Info("Probe attack skipped: no target and no attackable NPC nearby.");
                return;
            }

            targetId = nearest.ObjectId;
            tx = nearest.X;
            ty = nearest.Y;
            tz = nearest.Z;
        }
        else
        {
            var target = _snapshot.Npcs.FirstOrDefault(x => x.ObjectId == targetId);
            if (target is not null)
            {
                tx = target.X;
                ty = target.Y;
                tz = target.Z;
            }
        }

        _proxy.InjectToServer(PacketBuilder.BuildAction(targetId, me.X, me.Y, me.Z, 0));
        _proxy.InjectToServer(PacketBuilder.BuildForceAttack(targetId, tx, ty, tz));
        _log.Info($"Probe attack sent: target=0x{targetId:X} packets=0x04+0x2F(16)");
    }
    private void ClearLogs()
    {
        Logs.Clear();
        _log.Info("Logs cleared.");
    }

    private void RaiseCommands()
    {
        StartProxyCommand.RaiseCanExecuteChanged();
        StopProxyCommand.RaiseCanExecuteChanged();
        StartBotCommand.RaiseCanExecuteChanged();
        StopBotCommand.RaiseCanExecuteChanged();
    }

    private void LoadState()
    {
        _loadingState = true;
        try
        {
            var global = _stateStore.LoadGlobal();
            _loginHost = global.LoginHost;
            _loginPort = global.LoginPort;
            _gameHost = global.GameHost;
            _gamePort = global.GamePort;
            _localLoginPort = global.LocalLoginPort;
            _localGamePort = global.LocalGamePort;
            _onlyKnownSkills = global.OnlyKnownSkills;
            _serverProfileMode = global.ServerProfileMode;

            _activeCharacterProfileKey = string.Empty;
            _activeCharacterIdentity = string.Empty;
            _activeCharacterProfileLabel = "Character profile: pending (wait UserInfo)";
            ApplyCharacterState(new CharacterBotState());

            NotifyPropertyChanged(nameof(LoginHost));
            NotifyPropertyChanged(nameof(LoginPort));
            NotifyPropertyChanged(nameof(GameHost));
            NotifyPropertyChanged(nameof(GamePort));
            NotifyPropertyChanged(nameof(LocalLoginPort));
            NotifyPropertyChanged(nameof(LocalGamePort));
            NotifyPropertyChanged(nameof(ServerProfileMode));
            NotifyPropertyChanged(nameof(OnlyKnownSkills));
            NotifyPropertyChanged(nameof(ActiveCharacterProfileSummary));
            NotifyPropertyChanged(nameof(SkillPickerSummary));
        }
        finally
        {
            _loadingState = false;
        }
    }

    private bool TryActivateCharacterProfile(WorldSnapshot snapshot)
    {
        var me = snapshot.Me;
        if (me.ObjectId == 0 || string.IsNullOrWhiteSpace(me.Name))
        {
            return false;
        }

        var server = ResolveCurrentServerHost();
        var identity = $"{server}|{me.Name}";
        if (string.Equals(identity, _activeCharacterIdentity, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_activeCharacterProfileKey))
        {
            _stateStore.SaveCharacterState(_activeCharacterProfileKey, BuildCharacterState());
        }

        _activeCharacterIdentity = identity;
        _activeCharacterProfileKey = _stateStore.BuildProfileKey(server, me.Name);
        _activeCharacterProfileLabel = $"Character profile: {server}/{me.Name}";

        var state = _stateStore.LoadCharacterState(_activeCharacterProfileKey);
        ApplyCharacterState(state);
        RefreshSkillPickerOptions(SkillRows.ToList());
        _stateStore.SaveCharacterState(_activeCharacterProfileKey, BuildCharacterState());

        _log.Info($"Character profile loaded: key={_activeCharacterProfileKey}");
        NotifyPropertyChanged(nameof(ActiveCharacterProfileSummary));
        return true;
    }

    private string ResolveCurrentServerHost()
    {
        var endpoint = _proxy.Diagnostics.RealGameEndpoint;
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.Equals(endpoint, "-", StringComparison.Ordinal))
        {
            var host = endpoint.Split(':', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(host))
            {
                return host.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(GameHost))
        {
            return GameHost.Trim();
        }

        return string.IsNullOrWhiteSpace(LoginHost) ? "unknown-server" : LoginHost.Trim();
    }

    private void ApplyCharacterState(CharacterBotState state)
    {
        var prevLoading = _loadingState;
        _loadingState = true;
        try
        {
            _autoFight = state.AutoFight;
            _autoBuff = state.AutoBuff;
            _autoLoot = state.AutoLoot;
            _groupBuff = state.GroupBuff;
            _autoHeal = state.AutoHeal;

            _selfHealSkillId = state.SelfHealSkillId;
            _groupHealSkillId = state.GroupHealSkillId;
            _buffSkillId = state.BuffSkillId;
            _healThreshold = state.HealThreshold;
            _fightRange = state.FightRange;
            _lootRange = state.LootRange;

            _battleMode = state.BattleMode;
            if (_battleMode == BotBattleMode.Melee && state.CasterMode)
            {
                _battleMode = BotBattleMode.StrictCaster;
            }

            _casterMode = _battleMode == BotBattleMode.StrictCaster;
            _restEnabled = state.RestEnabled;
            _sitMpPct = Math.Max(1, Math.Min(95, state.SitMpPct));
            _standMpPct = Math.Max(_sitMpPct + 1, Math.Min(100, state.StandMpPct));
            _changeWaitTypeSitRaw = NormalizeChangeWaitTypeSitRaw(state.ChangeWaitTypeSitRaw);
            _huntCenterMode = state.HuntCenterMode;
            _anchorX = state.AnchorX;
            _anchorY = state.AnchorY;
            _anchorZ = state.AnchorZ;

            _moveToTarget = state.MoveToTarget;
            _meleeEngageRange = state.MeleeEngageRange;
            _moveToLoot = state.MoveToLoot;
            _lootPickupRange = state.LootPickupRange;

            _useCombatSkills = state.UseCombatSkills;
            _combatSkill1Id = state.CombatSkill1Id;
            _combatSkill2Id = state.CombatSkill2Id;
            _combatSkill3Id = state.CombatSkill3Id;
            _combatSkillCooldownMs = state.CombatSkillCooldownMs;
            var normalizedPacket = NormalizeCombatSkillPacket(state.CombatSkillPacket, state.AttackPipelineMode);
            var normalizedBuffPacket = NormalizeSkillPacket(state.BuffSkillPacket, state.AttackPipelineMode == AttackPipelineMode.TeonActionPlus2F ? "2f" : "39");
            var normalizedPayload = NormalizeMagicSkillPayload(state.MagicSkillPayload, state.AttackPipelineMode);
            _combatSkillPacket = normalizedPacket;
            _buffSkillPacket = normalizedBuffPacket;
            _magicSkillPayload = normalizedPayload;
            _useForceAttack = state.UseForceAttack;
            _preferAttackRequest = state.PreferAttackRequest;
            _casterFallbackToAttack = state.CasterFallbackToAttack;
            _attackPipelineMode = state.AttackPipelineMode;
            _combatMode = state.CombatMode;
            _attackTransportMode = state.AttackTransportMode;
            _useAttackRequestFallback = state.UseAttackRequestFallback;
            _attackNoProgressWindowMs = Math.Max(3500, state.AttackNoProgressWindowMs);
            _casterChaseRange = Math.Max(220, state.CasterChaseRange);
            _casterCastIntervalMs = Math.Max(280, state.CasterCastIntervalMs);
            _role = state.Role;
            _coordMode = state.CoordMode;
            _enableRoleCoordinator = state.EnableRoleCoordinator;
            _enableCombatFsmV2 = state.EnableCombatFsmV2;
            _enableCasterV2 = state.EnableCasterV2;
            _enableSupportV2 = state.EnableSupportV2;
            _coordinatorChannel = string.IsNullOrWhiteSpace(state.CoordinatorChannel) ? "l2companion_combat_v2" : state.CoordinatorChannel;
            _coordinatorStaleMs = Math.Max(500, state.CoordinatorStaleMs);
            _followDistance = Math.Max(120, state.FollowDistance);
            _followTolerance = Math.Max(40, state.FollowTolerance);
            _followRepathIntervalMs = Math.Max(160, state.FollowRepathIntervalMs);
            _followerFallbackToStandalone = state.FollowerFallbackToStandalone;
            _supportAllowDamage = state.SupportAllowDamage;

            EnforceRoleBasedSpoilPolicy(logChange: false);

            _spoilEnabled = state.SpoilEnabled;
            _spoilSkillId = state.SpoilSkillId;
            _spoilOncePerTarget = state.SpoilOncePerTarget;
            _spoilMaxAttemptsPerTarget = Math.Max(1, state.SpoilMaxAttemptsPerTarget);
            _sweepEnabled = state.SweepEnabled;
            _sweepSkillId = state.SweepSkillId;
            _sweepRetryWindowMs = Math.Max(500, state.SweepRetryWindowMs);
            _sweepRetryIntervalMs = Math.Max(120, state.SweepRetryIntervalMs);
            _finishCurrentTargetBeforeAggroRetarget = state.FinishCurrentTargetBeforeAggroRetarget;
            _killTimeoutMs = Math.Max(25000, state.KillTimeoutMs);
            _postKillSweepEnabled = state.PostKillSweepEnabled;
            _postKillSweepRetryWindowMs = Math.Max(800, state.PostKillSweepRetryWindowMs);
            _postKillSweepRetryIntervalMs = Math.Max(120, state.PostKillSweepRetryIntervalMs);
            _postKillSweepMaxAttempts = Math.Max(1, state.PostKillSweepMaxAttempts);
            _sweepAttemptsPostKill = Math.Max(1, state.SweepAttemptsPostKill);
            _postKillLootMaxAttempts = Math.Max(1, state.PostKillLootMaxAttempts);
            _postKillLootItemRetry = Math.Max(1, state.PostKillLootItemRetry);
            _postKillSpawnWaitMs = Math.Max(0, state.PostKillSpawnWaitMs);
            _criticalHoldEnterHpPct = Math.Max(5, Math.Min(95, state.CriticalHoldEnterHpPct));
            _criticalHoldResumeHpPct = Math.Max(_criticalHoldEnterHpPct + 1, Math.Min(99, state.CriticalHoldResumeHpPct));
            _deadStopResumeHpPct = Math.Max(1, Math.Min(99, state.DeadStopResumeHpPct));

            _preferAggroMobs = state.PreferAggroMobs;
            _retainCurrentTargetMaxDist = state.RetainCurrentTargetMaxDist;
            _attackOnlyWhitelistMobs = state.AttackOnlyWhitelistMobs;
            _targetZRangeMax = state.TargetZRangeMax;
            _skipSummonedNpcs = state.SkipSummonedNpcs;
            _npcWhitelistCsv = state.NpcWhitelistCsv;
            _npcBlacklistCsv = state.NpcBlacklistCsv;
            _partySupportEnabled = state.PartySupportEnabled;
            _partyHealHpThreshold = state.PartyHealHpThreshold;

            HealRuleRows.Clear();
            foreach (var r in state.HealRules)
            {
                if (r.SkillId <= 0 || r.HpBelowPct <= 0)
                {
                    continue;
                }

                HealRuleRows.Add(new HealRuleRow
                {
                    SkillId = r.SkillId,
                    SkillName = _referenceData.ResolveSkillName(r.SkillId),
                    HpBelowPct = r.HpBelowPct,
                    CooldownMs = Math.Max(250, r.CooldownMs),
                    MinMpPct = Math.Max(0, Math.Min(99, r.MinMpPct)),
                    InFight = r.InFight,
                    Enabled = r.Enabled
                });
            }

            SortHealRules();

            BuffRuleRows.Clear();
            var buffRules = state.BuffRules;
            if (buffRules.Count == 0 && state.BuffSkillId > 0)
            {
                buffRules =
                [
                    new BuffRuleState
                    {
                        SkillId = state.BuffSkillId,
                        Scope = state.GroupBuff ? BuffTargetScope.Both : BuffTargetScope.Self,
                        AutoDetect = true,
                        DelaySec = 18,
                        MinMpPct = 0,
                        InFight = true,
                        Enabled = true
                    }
                ];
            }

            foreach (var r in buffRules)
            {
                if (r.SkillId <= 0)
                {
                    continue;
                }

                BuffRuleRows.Add(new BuffRuleRow
                {
                    SkillId = r.SkillId,
                    SkillName = _referenceData.ResolveSkillName(r.SkillId),
                    Scope = r.Scope,
                    AutoDetect = r.AutoDetect,
                    DelaySec = Math.Max(3, r.DelaySec),
                    MinMpPct = Math.Max(0, Math.Min(99, r.MinMpPct)),
                    InFight = r.InFight,
                    Enabled = r.Enabled
                });
            }

            PartyHealRuleRows.Clear();
            var partyHealRules = state.PartyHealRules;
            if (partyHealRules.Count == 0 && state.GroupHealSkillId > 0)
            {
                partyHealRules =
                [
                    new PartyHealRuleState
                    {
                        SkillId = state.GroupHealSkillId,
                        Mode = PartyHealMode.Group,
                        HpBelowPct = Math.Max(1, Math.Min(99, state.PartyHealHpThreshold)),
                        MinMpPct = 0,
                        CooldownMs = 1200,
                        InFight = true,
                        Enabled = true
                    }
                ];
            }

            foreach (var r in partyHealRules)
            {
                if (r.SkillId <= 0)
                {
                    continue;
                }

                PartyHealRuleRows.Add(new PartyHealRuleRow
                {
                    SkillId = r.SkillId,
                    SkillName = _referenceData.ResolveSkillName(r.SkillId),
                    Mode = r.Mode,
                    HpBelowPct = Math.Max(1, Math.Min(99, r.HpBelowPct)),
                    MinMpPct = Math.Max(0, Math.Min(99, r.MinMpPct)),
                    CooldownMs = Math.Max(250, r.CooldownMs),
                    InFight = r.InFight,
                    Enabled = r.Enabled
                });
            }

            AttackRuleRows.Clear();
            foreach (var r in state.AttackSkills)
            {
                if (r.SkillId <= 0)
                {
                    continue;
                }

                AttackRuleRows.Add(new AttackRuleRow
                {
                    SkillId = r.SkillId,
                    SkillName = _referenceData.ResolveSkillName(r.SkillId),
                    CooldownMs = Math.Max(250, r.CooldownMs)
                });
            }

            ReindexAttackRules();
            EnsureRuleNames();
        }
        finally
        {
            _loadingState = prevLoading;
        }

        ApplyBotSettings();
        NotifyCharacterPropertiesChanged();
    }

    private void NotifyCharacterPropertiesChanged()
    {
        NotifyPropertyChanged(nameof(AutoFight));
        NotifyPropertyChanged(nameof(AutoBuff));
        NotifyPropertyChanged(nameof(AutoLoot));
        NotifyPropertyChanged(nameof(GroupBuff));
        NotifyPropertyChanged(nameof(AutoHeal));
        NotifyPropertyChanged(nameof(SelfHealSkillId));
        NotifyPropertyChanged(nameof(GroupHealSkillId));
        NotifyPropertyChanged(nameof(BuffSkillId));
        NotifyPropertyChanged(nameof(HealThreshold));
        NotifyPropertyChanged(nameof(FightRange));
        NotifyPropertyChanged(nameof(LootRange));
        NotifyPropertyChanged(nameof(HuntCenterMode));
        NotifyPropertyChanged(nameof(AnchorX));
        NotifyPropertyChanged(nameof(AnchorY));
        NotifyPropertyChanged(nameof(AnchorZ));
        NotifyPropertyChanged(nameof(BattleMode));
        NotifyPropertyChanged(nameof(CasterMode));
        NotifyPropertyChanged(nameof(RestEnabled));
        NotifyPropertyChanged(nameof(SitMpPct));
        NotifyPropertyChanged(nameof(StandMpPct));
        NotifyPropertyChanged(nameof(ChangeWaitTypeSitRaw));
        NotifyPropertyChanged(nameof(PartySupportEnabled));
        NotifyPropertyChanged(nameof(PartyHealHpThreshold));
        NotifyPropertyChanged(nameof(MoveToTarget));
        NotifyPropertyChanged(nameof(MeleeEngageRange));
        NotifyPropertyChanged(nameof(MoveToLoot));
        NotifyPropertyChanged(nameof(LootPickupRange));
        NotifyPropertyChanged(nameof(UseCombatSkills));
        NotifyPropertyChanged(nameof(CombatSkill1Id));
        NotifyPropertyChanged(nameof(CombatSkill2Id));
        NotifyPropertyChanged(nameof(CombatSkill3Id));
        NotifyPropertyChanged(nameof(CombatSkillCooldownMs));
        NotifyPropertyChanged(nameof(CombatSkillPacket));
        NotifyPropertyChanged(nameof(BuffSkillPacket));
        NotifyPropertyChanged(nameof(MagicSkillPayload));
        NotifyPropertyChanged(nameof(UseForceAttack));
        NotifyPropertyChanged(nameof(PreferAttackRequest));
        NotifyPropertyChanged(nameof(CasterFallbackToAttack));
        NotifyPropertyChanged(nameof(AttackPipelineMode));
        NotifyPropertyChanged(nameof(CombatMode));
        NotifyPropertyChanged(nameof(AttackTransportMode));
        NotifyPropertyChanged(nameof(UseAttackRequestFallback));
        NotifyPropertyChanged(nameof(AttackNoProgressWindowMs));
        NotifyPropertyChanged(nameof(CasterChaseRange));
        NotifyPropertyChanged(nameof(CasterCastIntervalMs));
        NotifyPropertyChanged(nameof(Role));
        NotifyPropertyChanged(nameof(IsSpoilerRole));
        NotifyPropertyChanged(nameof(SpoilControlsEnabled));
        NotifyPropertyChanged(nameof(CoordMode));
        NotifyPropertyChanged(nameof(EnableRoleCoordinator));
        NotifyPropertyChanged(nameof(AssistFollowEnabled));
        NotifyPropertyChanged(nameof(LeaderBroadcastEnabled));
        NotifyPropertyChanged(nameof(EnableCombatFsmV2));
        NotifyPropertyChanged(nameof(EnableCasterV2));
        NotifyPropertyChanged(nameof(EnableSupportV2));
        NotifyPropertyChanged(nameof(CoordinatorChannel));
        NotifyPropertyChanged(nameof(CoordinatorStaleMs));
        NotifyPropertyChanged(nameof(FollowDistance));
        NotifyPropertyChanged(nameof(FollowTolerance));
        NotifyPropertyChanged(nameof(FollowRepathIntervalMs));
        NotifyPropertyChanged(nameof(FollowerFallbackToStandalone));
        NotifyPropertyChanged(nameof(SupportAllowDamage));
        NotifyPropertyChanged(nameof(SpoilEnabled));
        NotifyPropertyChanged(nameof(SpoilSkillId));
        NotifyPropertyChanged(nameof(SpoilOncePerTarget));
        NotifyPropertyChanged(nameof(SpoilMaxAttemptsPerTarget));
        NotifyPropertyChanged(nameof(SweepEnabled));
        NotifyPropertyChanged(nameof(SweepSkillId));
                NotifyPropertyChanged(nameof(FinishCurrentTargetBeforeAggroRetarget));
        NotifyPropertyChanged(nameof(KillTimeoutMs));
        NotifyPropertyChanged(nameof(PostKillSweepEnabled));
        NotifyPropertyChanged(nameof(PostKillSweepRetryWindowMs));
        NotifyPropertyChanged(nameof(PostKillSweepRetryIntervalMs));
        NotifyPropertyChanged(nameof(PostKillSweepMaxAttempts));
        NotifyPropertyChanged(nameof(SweepAttemptsPostKill));
        NotifyPropertyChanged(nameof(PostKillLootMaxAttempts));
        NotifyPropertyChanged(nameof(PostKillLootItemRetry));
        NotifyPropertyChanged(nameof(PostKillSpawnWaitMs));
        NotifyPropertyChanged(nameof(CriticalHoldEnterHpPct));
        NotifyPropertyChanged(nameof(CriticalHoldResumeHpPct));
        NotifyPropertyChanged(nameof(DeadStopResumeHpPct));
        NotifyPropertyChanged(nameof(PreferAggroMobs));
        NotifyPropertyChanged(nameof(RetainCurrentTargetMaxDist));
        NotifyPropertyChanged(nameof(AttackOnlyWhitelistMobs));
        NotifyPropertyChanged(nameof(TargetZRangeMax));
        NotifyPropertyChanged(nameof(SkipSummonedNpcs));
        NotifyPropertyChanged(nameof(NpcWhitelistCsv));
        NotifyPropertyChanged(nameof(NpcBlacklistCsv));
        NotifyPropertyChanged(nameof(ActiveCharacterProfileSummary));
    }

    private CharacterBotState BuildCharacterState()
        => new()
        {
            AutoFight = AutoFight,
            AutoBuff = AutoBuff,
            AutoLoot = AutoLoot,
            GroupBuff = GroupBuff,
            AutoHeal = AutoHeal,
            SelfHealSkillId = SelfHealSkillId,
            GroupHealSkillId = GroupHealSkillId,
            BuffSkillId = BuffSkillId,
            HealThreshold = HealThreshold,
            FightRange = FightRange,
            LootRange = LootRange,
            CasterMode = CasterMode,
            BattleMode = BattleMode,
            RestEnabled = RestEnabled,
            SitMpPct = SitMpPct,
            StandMpPct = StandMpPct,
            ChangeWaitTypeSitRaw = ChangeWaitTypeSitRaw,
            HuntCenterMode = HuntCenterMode,
            AnchorX = AnchorX,
            AnchorY = AnchorY,
            AnchorZ = AnchorZ,
            MoveToTarget = MoveToTarget,
            MeleeEngageRange = MeleeEngageRange,
            MoveToLoot = MoveToLoot,
            LootPickupRange = LootPickupRange,
            UseCombatSkills = UseCombatSkills,
            CombatSkill1Id = CombatSkill1Id,
            CombatSkill2Id = CombatSkill2Id,
            CombatSkill3Id = CombatSkill3Id,
            CombatSkillCooldownMs = CombatSkillCooldownMs,
            CombatSkillPacket = CombatSkillPacket,
            BuffSkillPacket = BuffSkillPacket,
            MagicSkillPayload = MagicSkillPayload,
            UseForceAttack = UseForceAttack,
            PreferAttackRequest = PreferAttackRequest,
            CasterFallbackToAttack = CasterFallbackToAttack,
            AttackPipelineMode = AttackPipelineMode,
            CombatMode = CombatMode,
            AttackTransportMode = AttackTransportMode,
            UseAttackRequestFallback = UseAttackRequestFallback,
            AttackNoProgressWindowMs = AttackNoProgressWindowMs,
            CasterChaseRange = CasterChaseRange,
            CasterCastIntervalMs = CasterCastIntervalMs,
            Role = Role,
            CoordMode = CoordMode,
            EnableRoleCoordinator = EnableRoleCoordinator,
            EnableCombatFsmV2 = EnableCombatFsmV2,
            EnableCasterV2 = EnableCasterV2,
            EnableSupportV2 = EnableSupportV2,
            CoordinatorChannel = CoordinatorChannel,
            CoordinatorStaleMs = CoordinatorStaleMs,
            FollowDistance = FollowDistance,
            FollowTolerance = FollowTolerance,
            FollowRepathIntervalMs = FollowRepathIntervalMs,
            FollowerFallbackToStandalone = FollowerFallbackToStandalone,
            SupportAllowDamage = SupportAllowDamage,
            SpoilEnabled = SpoilEnabled,
            SpoilSkillId = SpoilSkillId,
            SpoilOncePerTarget = SpoilOncePerTarget,
            SpoilMaxAttemptsPerTarget = SpoilMaxAttemptsPerTarget,
            SweepEnabled = SweepEnabled,
            SweepSkillId = SweepSkillId,
            SweepRetryWindowMs = SweepRetryWindowMs,
            SweepRetryIntervalMs = SweepRetryIntervalMs,
            FinishCurrentTargetBeforeAggroRetarget = FinishCurrentTargetBeforeAggroRetarget,
            KillTimeoutMs = KillTimeoutMs,
            PostKillSweepEnabled = PostKillSweepEnabled,
            PostKillSweepRetryWindowMs = PostKillSweepRetryWindowMs,
            PostKillSweepRetryIntervalMs = PostKillSweepRetryIntervalMs,
            PostKillSweepMaxAttempts = PostKillSweepMaxAttempts,
            SweepAttemptsPostKill = SweepAttemptsPostKill,
            PostKillLootMaxAttempts = PostKillLootMaxAttempts,
            PostKillLootItemRetry = PostKillLootItemRetry,
            PostKillSpawnWaitMs = PostKillSpawnWaitMs,
            CriticalHoldEnterHpPct = CriticalHoldEnterHpPct,
            CriticalHoldResumeHpPct = CriticalHoldResumeHpPct,
            PreferAggroMobs = PreferAggroMobs,
            RetainCurrentTargetMaxDist = RetainCurrentTargetMaxDist,
            AttackOnlyWhitelistMobs = AttackOnlyWhitelistMobs,
            TargetZRangeMax = TargetZRangeMax,
            SkipSummonedNpcs = SkipSummonedNpcs,
            NpcWhitelistCsv = NpcWhitelistCsv,
            NpcBlacklistCsv = NpcBlacklistCsv,
            PartySupportEnabled = PartySupportEnabled,
            PartyHealHpThreshold = PartyHealHpThreshold,
            DeadStopResumeHpPct = DeadStopResumeHpPct,
            HealRules = HealRuleRows
                .Select(x => new HealRuleState
                {
                    SkillId = x.SkillId,
                    HpBelowPct = x.HpBelowPct,
                    CooldownMs = x.CooldownMs,
                    MinMpPct = x.MinMpPct,
                    InFight = x.InFight,
                    Enabled = x.Enabled
                })
                .ToList(),
            BuffRules = BuffRuleRows
                .Select(x => new BuffRuleState
                {
                    SkillId = x.SkillId,
                    Scope = x.Scope,
                    AutoDetect = x.AutoDetect,
                    DelaySec = x.DelaySec,
                    MinMpPct = x.MinMpPct,
                    InFight = x.InFight,
                    Enabled = x.Enabled
                })
                .ToList(),
            PartyHealRules = PartyHealRuleRows
                .Select(x => new PartyHealRuleState
                {
                    SkillId = x.SkillId,
                    Mode = x.Mode,
                    HpBelowPct = x.HpBelowPct,
                    MinMpPct = x.MinMpPct,
                    CooldownMs = x.CooldownMs,
                    InFight = x.InFight,
                    Enabled = x.Enabled
                })
                .ToList(),
            AttackSkills = AttackRuleRows
                .OrderBy(x => x.Order)
                .Select(x => new AttackRuleState
                {
                    SkillId = x.SkillId,
                    CooldownMs = x.CooldownMs
                })
                .ToList()
        };

    private void SaveState(bool force = false)
    {
        if (_loadingState && !force)
        {
            return;
        }

        if (!force)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastStateSaveUtc).TotalMilliseconds < StateSaveMinIntervalMs)
            {
                _stateSaveQueued = true;
                return;
            }
        }

        _lastStateSaveUtc = DateTime.UtcNow;
        _stateSaveQueued = false;

        _stateStore.SaveGlobal(new GlobalUiState
        {
            LoginHost = LoginHost,
            LoginPort = LoginPort,
            GameHost = GameHost,
            GamePort = GamePort,
            LocalLoginPort = LocalLoginPort,
            LocalGamePort = LocalGamePort,
            OnlyKnownSkills = OnlyKnownSkills,
            ServerProfileMode = ServerProfileMode
        });

        if (!string.IsNullOrWhiteSpace(_activeCharacterProfileKey))
        {
            _stateStore.SaveCharacterState(_activeCharacterProfileKey, BuildCharacterState());
        }
    }

    private static string NormalizeCombatSkillPacket(string? value, AttackPipelineMode mode)
    {
        var packet = string.IsNullOrWhiteSpace(value)
            ? (mode == AttackPipelineMode.TeonActionPlus2F ? "2f" : "39")
            : value.Trim().ToLowerInvariant();

        if (packet is not ("39" or "2f"))
        {
            packet = mode == AttackPipelineMode.TeonActionPlus2F ? "2f" : "39";
        }

        if (mode == AttackPipelineMode.TeonActionPlus2F)
        {
            packet = "2f";
        }

        return packet;
    }

    private static string NormalizeMagicSkillPayload(string? value, AttackPipelineMode mode)
    {
        var payload = string.IsNullOrWhiteSpace(value) ? "ddd" : value.Trim().ToLowerInvariant();
        if (payload is not ("dcb" or "ddd" or "dcc"))
        {
            payload = "ddd";
        }


        return payload;
    }

    private static string NormalizeSkillPacket(string? value, string fallback)
    {
        var packet = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();
        return packet is "39" or "2f" ? packet : fallback;
    }

    private static int NormalizeChangeWaitTypeSitRaw(int value)
        => value == 1 ? 1 : 0;
    private static HashSet<int> ParseIdSet(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        var parts = csv.Split([',', ';', ' ', '\t', '\r', '\n', '|'], StringSplitOptions.RemoveEmptyEntries);
        var set = new HashSet<int>();
        foreach (var part in parts)
        {
            if (int.TryParse(part.Trim(), out var id) && id != 0)
            {
                set.Add(id);
            }
        }

        return set;
    }

    private static long DistanceSq(int x1, int y1, int x2, int y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return (long)dx * dx + (long)dy * dy;
    }
}

public sealed class NpcRow
{
    public string ObjectId { get; init; } = string.Empty;
    public int NpcId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Hp { get; init; } = string.Empty;
    public string Dist { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
}

public sealed class ItemRow
{
    public string ObjectId { get; init; } = string.Empty;
    public int ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public long Count { get; init; }
    public string Dist { get; init; } = string.Empty;
}

public sealed class PartyRow
{
    public string Name { get; init; } = string.Empty;
    public int Level { get; init; }
    public string Hp { get; init; } = string.Empty;
    public string Mp { get; init; } = string.Empty;
    public string Cp { get; init; } = string.Empty;
    public string ObjectId { get; init; } = string.Empty;
}

public sealed class SkillRow
{
    public int SkillId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Level { get; init; }
}

public sealed class SkillOption
{
    public int SkillId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Label => $"{SkillId} - {Name}";
    public override string ToString() => Label;
}

public sealed class HealRuleRow : ObservableObject
{
    private int _skillId;
    private string _skillName = string.Empty;
    private int _hpBelowPct;
    private int _cooldownMs = 900;
    private int _minMpPct;
    private bool _inFight = true;
    private bool _enabled = true;

    public int SkillId { get => _skillId; set => SetProperty(ref _skillId, value); }
    public string SkillName { get => _skillName; set => SetProperty(ref _skillName, value); }
    public int HpBelowPct { get => _hpBelowPct; set => SetProperty(ref _hpBelowPct, value); }
    public int CooldownMs { get => _cooldownMs; set => SetProperty(ref _cooldownMs, value); }
    public int MinMpPct { get => _minMpPct; set => SetProperty(ref _minMpPct, value); }
    public bool InFight { get => _inFight; set => SetProperty(ref _inFight, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
}

public sealed class BuffRuleRow : ObservableObject
{
    private int _skillId;
    private string _skillName = string.Empty;
    private BuffTargetScope _scope = BuffTargetScope.Self;
    private bool _autoDetect = true;
    private int _delaySec = 18;
    private int _minMpPct;
    private bool _inFight = true;
    private bool _enabled = true;

    public int SkillId { get => _skillId; set => SetProperty(ref _skillId, value); }
    public string SkillName { get => _skillName; set => SetProperty(ref _skillName, value); }
    public BuffTargetScope Scope { get => _scope; set => SetProperty(ref _scope, value); }
    public bool AutoDetect { get => _autoDetect; set => SetProperty(ref _autoDetect, value); }
    public int DelaySec { get => _delaySec; set => SetProperty(ref _delaySec, value); }
    public int MinMpPct { get => _minMpPct; set => SetProperty(ref _minMpPct, value); }
    public bool InFight { get => _inFight; set => SetProperty(ref _inFight, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
}

public sealed class PartyHealRuleRow : ObservableObject
{
    private int _skillId;
    private string _skillName = string.Empty;
    private PartyHealMode _mode = PartyHealMode.Group;
    private int _hpBelowPct = 55;
    private int _minMpPct;
    private int _cooldownMs = 1200;
    private bool _inFight = true;
    private bool _enabled = true;

    public int SkillId { get => _skillId; set => SetProperty(ref _skillId, value); }
    public string SkillName { get => _skillName; set => SetProperty(ref _skillName, value); }
    public PartyHealMode Mode { get => _mode; set => SetProperty(ref _mode, value); }
    public int HpBelowPct { get => _hpBelowPct; set => SetProperty(ref _hpBelowPct, value); }
    public int MinMpPct { get => _minMpPct; set => SetProperty(ref _minMpPct, value); }
    public int CooldownMs { get => _cooldownMs; set => SetProperty(ref _cooldownMs, value); }
    public bool InFight { get => _inFight; set => SetProperty(ref _inFight, value); }
    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }
}

public sealed class AttackRuleRow : ObservableObject
{
    private int _order;
    private int _skillId;
    private string _skillName = string.Empty;
    private int _cooldownMs = 1200;

    public int Order { get => _order; set => SetProperty(ref _order, value); }
    public int SkillId { get => _skillId; set => SetProperty(ref _skillId, value); }
    public string SkillName { get => _skillName; set => SetProperty(ref _skillName, value); }
    public int CooldownMs { get => _cooldownMs; set => SetProperty(ref _cooldownMs, value); }
}




















































































































































































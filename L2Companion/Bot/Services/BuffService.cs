using L2Companion.Protocol;
using L2Companion.World;

namespace L2Companion.Bot.Services;

public sealed class BuffService
{
    private readonly CombatService _combat;

    private DateTime _nextBuffAt = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _nextBuffRuleAt = [];

    public BuffService(CombatService combat)
    {
        _combat = combat;
    }

    public void Tick(BotContext ctx, bool inFight, bool criticalHoldActive, bool postKillActive, string combatPhase)
    {
        if (!ctx.Settings.AutoBuff)
            return;

        var me = ctx.World.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
            return;

        if (me.IsSitting)
        {
            _combat.SetDecision("buff-paused-sitting");
            return;
        }

        if (criticalHoldActive
            || postKillActive
            || string.Equals(combatPhase, "target-confirm", StringComparison.Ordinal)
            || string.Equals(combatPhase, "post-kill-sweep", StringComparison.Ordinal))
        {
            _combat.SetDecision("buff-paused-combat-phase");
            return;
        }

        var now = ctx.Now;
        if (now < _nextBuffAt)
            return;

        var hasFreshAbnormal = me.AbnormalUpdatedAtUtc != DateTime.MinValue && (now - me.AbnormalUpdatedAtUtc).TotalSeconds <= 90;
        var rules = GetEffectiveBuffRules(ctx.Settings);
        if (rules.Count == 0)
            return;

        foreach (var rule in rules)
        {
            if (!rule.Enabled || rule.SkillId <= 0)
                continue;

            if (!rule.InFight && inFight)
                continue;

            if (rule.MinMpPct > 0 && me.MpPct < rule.MinMpPct)
                continue;

            var needsParty = rule.Scope is BuffTargetScope.Party or BuffTargetScope.Both;
            if (needsParty && ctx.World.Party.IsEmpty)
            {
                if (rule.Scope == BuffTargetScope.Party)
                    continue;
            }

            var delaySec = Math.Max(6, rule.DelaySec);
            var ruleKey = $"{rule.SkillId}:{rule.Scope}:{rule.AutoDetect}:{delaySec}";
            if (_nextBuffRuleAt.TryGetValue(ruleKey, out var nextRuleAt) && now < nextRuleAt)
                continue;

            var canDetectSelf = hasFreshAbnormal && rule.Scope is BuffTargetScope.Self or BuffTargetScope.Both;
            if (rule.AutoDetect && canDetectSelf && me.AbnormalEffectSkillIds.Contains(rule.SkillId))
            {
                _nextBuffRuleAt[ruleKey] = now.AddSeconds(delaySec);
                _nextBuffAt = now.AddMilliseconds(220);
                _combat.SetDecision($"buff-active:{rule.SkillId}");
                continue;
            }

            if (rule.Scope is BuffTargetScope.Self or BuffTargetScope.Both && me.ObjectId != 0 && me.TargetId != me.ObjectId)
                _combat.TryInject(ctx, PacketBuilder.BuildAction(me.ObjectId, me.X, me.Y, me.Z, 0), $"self-target-buff:{rule.SkillId}");

            if (!_combat.TryCastSkill(ctx, rule.SkillId, forBuff: true, cooldownOverrideMs: 1200))
            {
                _nextBuffRuleAt[ruleKey] = now.AddMilliseconds(1000);
                _nextBuffAt = now.AddMilliseconds(420);
                continue;
            }

            _nextBuffRuleAt[ruleKey] = now.AddSeconds(delaySec);
            _nextBuffAt = now.AddMilliseconds(320);
            _combat.SetDecision($"buff-cast:{rule.SkillId}:{rule.Scope}");
            return;
        }
    }

    public static List<BuffRuleSetting> GetEffectiveBuffRules(BotSettings settings)
    {
        var configured = settings.BuffRules
            .Where(x => x.SkillId > 0)
            .ToList();

        if (configured.Count > 0)
            return configured;

        if (settings.BuffSkillId <= 0)
            return [];

        return
        [
            new BuffRuleSetting
            {
                SkillId = settings.BuffSkillId,
                Scope = settings.GroupBuff ? BuffTargetScope.Both : BuffTargetScope.Self,
                AutoDetect = true,
                DelaySec = 18,
                MinMpPct = 0,
                InFight = true,
                Enabled = true
            }
        ];
    }

    public void Reset()
    {
        _nextBuffAt = DateTime.MinValue;
        _nextBuffRuleAt.Clear();
    }
}

using L2Companion.Protocol;
using L2Companion.World;

namespace L2Companion.Bot.Services;

public sealed class HealService
{
    private readonly CombatService _combat;

    private DateTime _nextSelfHealAt = DateTime.MinValue;
    private DateTime _nextPartyHealAt = DateTime.MinValue;
    private DateTime _nextGroupHealAt = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _nextPartyHealRuleAt = [];

    public HealService(CombatService combat)
    {
        _combat = combat;
    }

    public bool TickSelfHeal(BotContext ctx, bool inFight)
    {
        if (!ctx.Settings.AutoHeal)
            return false;

        var now = ctx.Now;
        if (now < _nextSelfHealAt)
            return false;

        var me = ctx.World.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
            return false;

        var hp = me.HpPct;

        var dynamicRules = ctx.Settings.HealRules
            .Where(x => x.Enabled && x.SkillId > 0 && x.HpBelowPct > 0)
            .OrderBy(x => x.HpBelowPct)
            .ToList();

        foreach (var rule in dynamicRules)
        {
            if (hp > rule.HpBelowPct)
                continue;

            if (rule.MinMpPct > 0 && me.MpPct < rule.MinMpPct)
                continue;

            if (!rule.InFight && inFight)
                continue;

            if (inFight && !TrySelectSelfForSelfHeal(ctx, now))
                return true;

            if (!_combat.TryCastSkill(ctx, rule.SkillId, forBuff: true, cooldownOverrideMs: Math.Max(250, rule.CooldownMs)))
                continue;

            _nextSelfHealAt = now.AddMilliseconds(Math.Max(320, rule.CooldownMs / 3));
            _combat.SetDecision($"self-heal-rule:{rule.SkillId}");
            return true;
        }

        if (hp > ctx.Settings.HealHpThreshold)
            return false;

        var fallbackSkillId = ResolveSelfPreservationSkillId(ctx);
        if (fallbackSkillId <= 0)
            return false;

        if (inFight && !TrySelectSelfForSelfHeal(ctx, now))
            return true;

        if (!_combat.TryCastSkill(ctx, fallbackSkillId, forBuff: true, cooldownOverrideMs: 900))
            return false;

        _nextSelfHealAt = now.AddMilliseconds(900);
        _combat.SetDecision($"self-heal-fallback:{fallbackSkillId}");
        return true;
    }

    /// <summary>
    /// Skill packets (0x2F/0x39) carry no target id — the client uses <see cref="CharacterState.TargetId"/>.
    /// While fighting, target is usually the mob; self-heals must target self first.
    /// Returns true when ready to cast; false when this tick only sent target-self (caller should yield combat).
    /// </summary>
    private bool TrySelectSelfForSelfHeal(BotContext ctx, DateTime now)
    {
        var me = ctx.World.Me;
        if (me.ObjectId == 0)
            return true;

        if (me.TargetId == me.ObjectId)
            return true;

        if (now < _combat.NextTargetActionAt)
            return false;

        if (!_combat.TryInject(ctx, PacketBuilder.BuildAction(me.ObjectId, me.X, me.Y, me.Z, 0), "self-heal-target-self"))
            return false;

        _combat.NextTargetActionAt = now.AddMilliseconds(520);
        _combat.SetDecision("self-heal-target-self");
        return false;
    }

    public void TickPartyHeal(BotContext ctx, bool inFight, bool criticalHoldActive)
    {
        if (!ctx.Settings.AutoHeal || !ctx.Settings.PartySupportEnabled || criticalHoldActive)
            return;

        var now = ctx.Now;
        if (now < _nextPartyHealAt)
            return;

        var me = ctx.World.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
            return;

        var rules = GetEffectivePartyHealRules(ctx.Settings);
        if (rules.Count == 0)
            return;

        var party = ctx.World.Party.Values.ToList();
        if (party.Count == 0)
            return;

        foreach (var rule in rules)
        {
            if (!rule.Enabled || rule.SkillId <= 0)
                continue;

            if (!rule.InFight && inFight)
                continue;

            if (rule.MinMpPct > 0 && me.MpPct < rule.MinMpPct)
                continue;

            var threshold = Math.Max(1, Math.Min(99, rule.HpBelowPct));
            var lowMembers = party.Where(x => x.HpPct > 0 && x.HpPct < threshold).OrderBy(x => x.HpPct).ToList();
            if (lowMembers.Count == 0)
                continue;

            var ruleKey = $"{rule.Mode}:{rule.SkillId}:{threshold}";
            if (_nextPartyHealRuleAt.TryGetValue(ruleKey, out var nextRuleAt) && now < nextRuleAt)
                continue;

            if (rule.Mode == PartyHealMode.Target)
            {
                var targetMember = lowMembers[0];
                if (targetMember.ObjectId == 0)
                    continue;

                if (me.TargetId != targetMember.ObjectId)
                {
                    if (now < _combat.NextTargetActionAt)
                        continue;

                    if (!_combat.TryInject(ctx, PacketBuilder.BuildAction(targetMember.ObjectId, me.X, me.Y, me.Z, 0), "party-heal-target-confirm"))
                        continue;

                    _combat.NextTargetActionAt = now.AddMilliseconds(520);
                    _nextPartyHealAt = now.AddMilliseconds(180);
                    _combat.SetDecision($"party-heal-target-confirm:0x{targetMember.ObjectId:X}");
                    return;
                }
            }

            var ruleCooldown = Math.Max(320, rule.CooldownMs);
            if (!_combat.TryCastSkill(ctx, rule.SkillId, forBuff: true, cooldownOverrideMs: ruleCooldown))
                continue;

            _nextPartyHealRuleAt[ruleKey] = now.AddMilliseconds(ruleCooldown);
            _nextPartyHealAt = now.AddMilliseconds(220);
            _nextGroupHealAt = _nextPartyHealAt;
            _combat.SetDecision($"party-heal:{rule.Mode}:{rule.SkillId}");
            return;
        }
    }

    public int ResolveSelfPreservationSkillId(BotContext ctx)
    {
        var settings = ctx.Settings;
        if (settings.SelfHealSkillId > 0)
            return settings.SelfHealSkillId;

        var healRuleSkillId = settings.HealRules
            .Where(x => x.Enabled && x.SkillId > 0 && ctx.World.Skills.ContainsKey(x.SkillId))
            .OrderBy(x => x.HpBelowPct)
            .Select(x => x.SkillId)
            .FirstOrDefault();
        if (healRuleSkillId > 0)
            return healRuleSkillId;

        var fallbackHealRuleSkillId = settings.HealRules
            .Where(x => x.Enabled && x.SkillId > 0)
            .OrderBy(x => x.HpBelowPct)
            .Select(x => x.SkillId)
            .FirstOrDefault();
        if (fallbackHealRuleSkillId > 0)
            return fallbackHealRuleSkillId;

        var partyRules = GetEffectivePartyHealRules(settings);
        var partyRuleSkillId = partyRules
            .Where(x => x.Enabled && x.SkillId > 0)
            .Select(x => x.SkillId)
            .FirstOrDefault();

        if (ctx.World.Me.ClassId == 10)
            return 1015;

        return partyRuleSkillId;
    }

    public static List<PartyHealRuleSetting> GetEffectivePartyHealRules(BotSettings settings)
    {
        var configured = settings.PartyHealRules
            .Where(x => x.SkillId > 0)
            .ToList();

        if (configured.Count > 0)
            return configured;

        if (settings.GroupHealSkillId <= 0)
            return [];

        return
        [
            new PartyHealRuleSetting
            {
                SkillId = settings.GroupHealSkillId,
                Mode = PartyHealMode.Group,
                HpBelowPct = Math.Max(1, Math.Min(99, settings.PartyHealHpThreshold)),
                MinMpPct = 0,
                CooldownMs = 1200,
                InFight = true,
                Enabled = true
            }
        ];
    }

    public void Reset()
    {
        _nextSelfHealAt = DateTime.MinValue;
        _nextPartyHealAt = DateTime.MinValue;
        _nextGroupHealAt = DateTime.MinValue;
        _nextPartyHealRuleAt.Clear();
    }
}

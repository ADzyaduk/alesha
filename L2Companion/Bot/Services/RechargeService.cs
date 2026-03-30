using L2Companion.Protocol;
using L2Companion.World;

namespace L2Companion.Bot.Services;

public sealed class RechargeService
{
    private readonly CombatService _combat;

    private DateTime _nextRechargeAt = DateTime.MinValue;
    private readonly Dictionary<string, DateTime> _nextRechargeRuleAt = [];

    public RechargeService(CombatService combat)
    {
        _combat = combat;
    }

    public void Tick(BotContext ctx, bool criticalHoldActive)
    {
        if (!ctx.Settings.AutoRecharge || !ctx.Settings.PartySupportEnabled || criticalHoldActive)
            return;

        var now = ctx.Now;
        if (now < _nextRechargeAt)
            return;

        var me = ctx.World.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
            return;

        var rules = ctx.Settings.RechargeRules;
        if (rules.Count == 0)
            return;

        var party = ctx.World.Party.Values.ToList();
        if (party.Count == 0)
            return;

        foreach (var rule in rules)
        {
            if (!rule.Enabled || rule.SkillId <= 0)
                continue;

            if (rule.MinSelfMpPct > 0 && me.MpPct < rule.MinSelfMpPct)
                continue;

            var threshold = Math.Max(1, Math.Min(99, rule.MpBelowPct));
            var lowMembers = party
                .Where(x => x.MpPct > 0 && x.MpPct < threshold)
                .OrderBy(x => x.MpPct)
                .ToList();

            if (lowMembers.Count == 0)
                continue;

            var ruleKey = $"recharge:{rule.SkillId}:{threshold}";
            if (_nextRechargeRuleAt.TryGetValue(ruleKey, out var nextRuleAt) && now < nextRuleAt)
                continue;

            var targetMember = lowMembers[0];
            if (targetMember.ObjectId == 0)
                continue;

            if (me.TargetId != targetMember.ObjectId)
            {
                if (now < _combat.NextTargetActionAt)
                    continue;

                if (!_combat.TryInject(ctx, PacketBuilder.BuildAction(targetMember.ObjectId, me.X, me.Y, me.Z, 0), "recharge-target"))
                    continue;

                _combat.NextTargetActionAt = now.AddMilliseconds(520);
                _nextRechargeAt = now.AddMilliseconds(180);
                _combat.SetDecision($"recharge-target:0x{targetMember.ObjectId:X}");
                return;
            }

            var ruleCooldown = Math.Max(320, rule.CooldownMs);
            if (!_combat.TryCastSkill(ctx, rule.SkillId, forBuff: true, cooldownOverrideMs: ruleCooldown))
                continue;

            _nextRechargeRuleAt[ruleKey] = now.AddMilliseconds(ruleCooldown);
            _nextRechargeAt = now.AddMilliseconds(220);
            _combat.SetDecision($"recharge-cast:{rule.SkillId}:0x{targetMember.ObjectId:X}");
            return;
        }
    }

    public void Reset()
    {
        _nextRechargeAt = DateTime.MinValue;
        _nextRechargeRuleAt.Clear();
    }
}

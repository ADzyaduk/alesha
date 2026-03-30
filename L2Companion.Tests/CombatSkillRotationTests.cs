using L2Companion.Bot;
using L2Companion.World;
using Xunit;

namespace L2Companion.Tests;

public sealed class CombatSkillRotationTests
{
    [Fact]
    public void RuleConditionsAllow_WhenFsmOff_IgnoresMpAndTargetRules()
    {
        var rule = new AttackSkillSetting
        {
            Enabled = true,
            MinMpPct = 90,
            TargetHpBelowPct = 30
        };
        var me = new CharacterState { CurMp = 5, MaxMp = 100, CurHp = 50, MaxHp = 100 };
        var target = new NpcState { HpPct = 80 };

        Assert.True(CombatSkillRotation.RuleConditionsAllow(rule, me, target, targetDistSq: 1, enableExtendedConditions: false));
    }

    [Fact]
    public void RuleConditionsAllow_MinMpBlocksWhenExtendedOn()
    {
        var rule = new AttackSkillSetting { Enabled = true, MinMpPct = 50 };
        var me = new CharacterState { CurMp = 40, MaxMp = 100, CurHp = 50, MaxHp = 100 };
        var target = new NpcState { HpPct = 50 };

        Assert.False(CombatSkillRotation.RuleConditionsAllow(rule, me, target, 100, enableExtendedConditions: true));
    }

    [Fact]
    public void RuleConditionsAllow_TargetHpBelow_allowsOnlyWhenLow()
    {
        var rule = new AttackSkillSetting { Enabled = true, TargetHpBelowPct = 35 };
        var me = new CharacterState { CurMp = 80, MaxMp = 100, CurHp = 80, MaxHp = 100 };
        var high = new NpcState { HpPct = 80 };
        var low = new NpcState { HpPct = 30 };

        Assert.False(CombatSkillRotation.RuleConditionsAllow(rule, me, high, 100, true));
        Assert.True(CombatSkillRotation.RuleConditionsAllow(rule, me, low, 100, true));
    }

    [Fact]
    public void RuleConditionsAllow_SkipAbnormal_WhenPresent()
    {
        var rule = new AttackSkillSetting { Enabled = true, SkipIfTargetHasAbnormalSkillId = 99 };
        var me = new CharacterState { CurMp = 80, MaxMp = 100, CurHp = 80, MaxHp = 100 };
        var withFx = new NpcState { HpPct = 50, AbnormalEffectSkillIds = [99] };
        var clean = new NpcState { HpPct = 50, AbnormalEffectSkillIds = [] };

        Assert.False(CombatSkillRotation.RuleConditionsAllow(rule, me, withFx, 100, true));
        Assert.True(CombatSkillRotation.RuleConditionsAllow(rule, me, clean, 100, true));
    }

    [Fact]
    public void OrderRules_PrioritySortsBeforeStableIndex()
    {
        var rules = new List<AttackSkillSetting>
        {
            new() { SkillId = 1, Priority = 10 },
            new() { SkillId = 2, Priority = 0 },
            new() { SkillId = 3, Priority = 0 }
        };

        var ordered = CombatSkillRotation.OrderRules(rules, usePrioritySort: true);
        Assert.Equal(2, ordered[0].SkillId);
        Assert.Equal(3, ordered[1].SkillId);
        Assert.Equal(1, ordered[2].SkillId);
    }

    [Fact]
    public void OrderRules_WhenFsmOff_PreservesListOrder()
    {
        var rules = new List<AttackSkillSetting>
        {
            new() { SkillId = 1, Priority = 10 },
            new() { SkillId = 2, Priority = 0 }
        };

        var ordered = CombatSkillRotation.OrderRules(rules, usePrioritySort: false);
        Assert.Equal(1, ordered[0].SkillId);
        Assert.Equal(2, ordered[1].SkillId);
    }
}

using L2Companion.Bot;
using L2Companion.Bot.Services;
using Xunit;

namespace L2Companion.Tests;

public sealed class TargetingServiceTests
{
    [Theory]
    [InlineData("Gatekeeper", true)]
    [InlineData("Warehouse Keeper", true)]
    [InlineData("Dark Trader", true)]
    [InlineData("Goblin", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsNonCombatServiceName_DetectsCorrectly(string? text, bool expected)
    {
        Assert.Equal(expected, TargetingService.IsNonCombatServiceName(text));
    }

    [Theory]
    [InlineData(20400, true)]
    [InlineData(25000, true)]
    [InlineData(29999, true)]
    [InlineData(20399, false)]
    [InlineData(30000, false)]
    [InlineData(100, false)]
    public void IsLikelyMobByNpcId_ValidRange(int npcId, bool expected)
    {
        Assert.Equal(expected, TargetingService.IsLikelyMobByNpcId(npcId));
    }

    [Theory]
    [InlineData(8600, true)]
    [InlineData(8612, true)]
    [InlineData(8625, true)]
    [InlineData(8599, false)]
    [InlineData(8626, false)]
    public void IsLikelyHerbItemId_ValidRange(int itemId, bool expected)
    {
        Assert.Equal(expected, TargetingService.IsLikelyHerbItemId(itemId));
    }
}

public sealed class CombatServiceTests
{
    [Fact]
    public void DistanceSq_CalculatesCorrectly()
    {
        Assert.Equal(0L, CombatService.DistanceSq(0, 0, 0, 0));
        Assert.Equal(25L, CombatService.DistanceSq(0, 0, 3, 4));
        Assert.Equal(180000L, CombatService.DistanceSq(100, 200, 400, 500));
    }

    [Fact]
    public void IsSpecialCombatSkill_DetectsSpoilAndSweep()
    {
        var settings = new BotSettings
        {
            SpoilEnabled = true,
            SpoilSkillId = 254,
            SweepEnabled = true,
            SweepSkillId = 42
        };

        Assert.True(CombatService.IsSpecialCombatSkill(settings, 254));
        Assert.True(CombatService.IsSpecialCombatSkill(settings, 42));
        Assert.False(CombatService.IsSpecialCombatSkill(settings, 100));
        Assert.False(CombatService.IsSpecialCombatSkill(settings, 0));
    }

    [Theory]
    [InlineData(BotRole.CasterDD, BotBattleMode.Melee, true)]
    [InlineData(BotRole.LeaderDD, BotBattleMode.StrictCaster, true)]
    [InlineData(BotRole.LeaderDD, BotBattleMode.Melee, false)]
    [InlineData(BotRole.Spoiler, BotBattleMode.Melee, false)]
    public void IsCasterRole_MatchesExpected(BotRole role, BotBattleMode mode, bool expected)
    {
        var settings = new BotSettings { Role = role, BattleMode = mode };
        Assert.Equal(expected, CombatService.IsCasterRole(settings));
    }

    [Theory]
    [InlineData(BotRole.Healer, true)]
    [InlineData(BotRole.Buffer, true)]
    [InlineData(BotRole.LeaderDD, false)]
    [InlineData(BotRole.CasterDD, false)]
    [InlineData(BotRole.Spoiler, false)]
    public void IsSupportRole_MatchesExpected(BotRole role, bool expected)
    {
        var settings = new BotSettings { Role = role };
        Assert.Equal(expected, CombatService.IsSupportRole(settings));
    }
}

public sealed class HealServiceTests
{
    [Fact]
    public void GetEffectivePartyHealRules_ReturnsConfiguredWhenPresent()
    {
        var settings = new BotSettings
        {
            PartyHealRules =
            [
                new PartyHealRuleSetting { SkillId = 1015, HpBelowPct = 40, Mode = PartyHealMode.Target, Enabled = true }
            ]
        };

        var result = HealService.GetEffectivePartyHealRules(settings);
        Assert.Single(result);
        Assert.Equal(1015, result[0].SkillId);
    }

    [Fact]
    public void GetEffectivePartyHealRules_FallsBackToGroupHealSkillId()
    {
        var settings = new BotSettings
        {
            GroupHealSkillId = 1217,
            PartyHealHpThreshold = 55,
            PartyHealRules = []
        };

        var result = HealService.GetEffectivePartyHealRules(settings);
        Assert.Single(result);
        Assert.Equal(1217, result[0].SkillId);
        Assert.Equal(PartyHealMode.Group, result[0].Mode);
    }

    [Fact]
    public void GetEffectivePartyHealRules_ReturnsEmptyWhenNoConfig()
    {
        var settings = new BotSettings { GroupHealSkillId = 0, PartyHealRules = [] };
        Assert.Empty(HealService.GetEffectivePartyHealRules(settings));
    }
}

public sealed class BuffServiceTests
{
    [Fact]
    public void GetEffectiveBuffRules_ReturnsConfiguredWhenPresent()
    {
        var settings = new BotSettings
        {
            BuffRules =
            [
                new BuffRuleSetting { SkillId = 1204, Scope = BuffTargetScope.Self, Enabled = true }
            ]
        };

        var result = BuffService.GetEffectiveBuffRules(settings);
        Assert.Single(result);
        Assert.Equal(1204, result[0].SkillId);
    }

    [Fact]
    public void GetEffectiveBuffRules_FallsBackToBuffSkillId()
    {
        var settings = new BotSettings { BuffSkillId = 1068, GroupBuff = true, BuffRules = [] };

        var result = BuffService.GetEffectiveBuffRules(settings);
        Assert.Single(result);
        Assert.Equal(1068, result[0].SkillId);
        Assert.Equal(BuffTargetScope.Both, result[0].Scope);
    }

    [Fact]
    public void GetEffectiveBuffRules_ReturnsEmptyWhenNoConfig()
    {
        var settings = new BotSettings { BuffSkillId = 0, BuffRules = [] };
        Assert.Empty(BuffService.GetEffectiveBuffRules(settings));
    }
}

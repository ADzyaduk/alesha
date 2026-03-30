using L2Companion.World;

namespace L2Companion.Bot;

/// <summary>
/// Pure selection logic for attack rotation rules (testable without proxy/game loop).
/// </summary>
public static class CombatSkillRotation
{
    public static List<AttackSkillSetting> OrderRules(IReadOnlyList<AttackSkillSetting> rules, bool usePrioritySort)
    {
        if (rules.Count == 0)
        {
            return [];
        }

        if (!usePrioritySort)
        {
            return rules.ToList();
        }

        return rules
            .Select((rule, index) => (rule, index))
            .OrderBy(x => x.rule.Priority)
            .ThenBy(x => x.index)
            .Select(x => x.rule)
            .ToList();
    }

    /// <summary>
    /// When <paramref name="enableExtendedConditions"/> is false (legacy rotation), only <see cref="AttackSkillSetting.Enabled"/> is checked.
    /// </summary>
    public static bool RuleConditionsAllow(
        AttackSkillSetting rule,
        CharacterState me,
        NpcState? target,
        long targetDistSq,
        bool enableExtendedConditions)
    {
        if (!rule.Enabled)
        {
            return false;
        }

        if (!enableExtendedConditions)
        {
            return true;
        }

        if (rule.MinMpPct > 0 && me.MpPct < rule.MinMpPct)
        {
            return false;
        }

        if (rule.MaxMpPct > 0 && me.MpPct > rule.MaxMpPct)
        {
            return false;
        }

        var needsTarget = rule.TargetHpBelowPct > 0
            || rule.TargetHpAbovePct > 0
            || rule.SkipIfTargetHasAbnormalSkillId > 0
            || rule.MaxCastRange > 0;

        if (needsTarget && target is null)
        {
            return false;
        }

        if (target is not null)
        {
            if (rule.TargetHpBelowPct > 0 && target.HpPct > rule.TargetHpBelowPct + 0.01f)
            {
                return false;
            }

            if (rule.TargetHpAbovePct > 0 && target.HpPct < rule.TargetHpAbovePct - 0.01f)
            {
                return false;
            }

            if (rule.SkipIfTargetHasAbnormalSkillId > 0
                && target.AbnormalEffectSkillIds.Contains(rule.SkipIfTargetHasAbnormalSkillId))
            {
                return false;
            }

            if (rule.MaxCastRange > 0)
            {
                var rangeSq = (long)rule.MaxCastRange * rule.MaxCastRange;
                if (targetDistSq > rangeSq)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

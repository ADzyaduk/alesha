using L2Companion.Bot.Services;
using L2Companion.World;

namespace L2Companion.Bot.Roles;

public sealed class BufferRole : IBotRole
{
    private readonly CombatService _combat;
    private readonly BuffService _buff;
    private readonly HealService _heal;
    private readonly RechargeService _recharge;

    private bool _criticalHoldActive;

    public BotRole RoleType => BotRole.Buffer;

    public BufferRole(CombatService combat, BuffService buff, HealService heal, RechargeService recharge)
    {
        _combat = combat;
        _buff = buff;
        _heal = heal;
        _recharge = recharge;
    }

    public TickResult Tick(BotContext ctx)
    {
        var me = ctx.World.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
            return TickResult.Continue;

        if (me.IsSitting)
        {
            _combat.SetDecision("buffer-paused-sitting");
            return TickResult.Continue;
        }

        UpdateCriticalHold(ctx, me);

        if (_heal.TickSelfHeal(ctx, inFight: false))
            return TickResult.Yielded;

        _buff.Tick(ctx, inFight: false, criticalHoldActive: _criticalHoldActive, postKillActive: false, combatPhase: "idle");
        _heal.TickPartyHeal(ctx, inFight: false, criticalHoldActive: _criticalHoldActive);
        _recharge.Tick(ctx, criticalHoldActive: _criticalHoldActive);

        if (ctx.Settings.SupportAllowDamage && ctx.Settings.AutoFight && !_criticalHoldActive)
        {
            _combat.SetDecision("buffer-support-damage");
        }
        else
        {
            _combat.SetDecision("buffer-monitoring");
        }

        return TickResult.Continue;
    }

    private void UpdateCriticalHold(BotContext ctx, CharacterState me)
    {
        var enterPct = Math.Max(5, ctx.Settings.CriticalHoldEnterHpPct);
        var resumePct = Math.Max(enterPct + 5, ctx.Settings.CriticalHoldResumeHpPct);

        if (!_criticalHoldActive && me.HpPct <= enterPct)
        {
            _criticalHoldActive = true;
            ctx.Log.Info($"[Buffer] critical hold ENTER hp={me.HpPct:0.#}%");
        }
        else if (_criticalHoldActive && me.HpPct >= resumePct)
        {
            _criticalHoldActive = false;
            ctx.Log.Info($"[Buffer] critical hold RESUME hp={me.HpPct:0.#}%");
        }
    }

    public void Reset()
    {
        _criticalHoldActive = false;
    }
}

using L2Companion.Protocol;
using L2Companion.World;

namespace L2Companion.Bot.Services;

public sealed class RestService
{
    private readonly CombatService _combat;

    private DateTime _nextRestToggleAt = DateTime.MinValue;
    private DateTime _restExpectedStateUntilUtc = DateTime.MinValue;
    private bool? _restExpectedIsSitting;

    public DateTime NextRestToggleAt => _nextRestToggleAt;

    public RestService(CombatService combat)
    {
        _combat = combat;
    }

    public bool Tick(BotContext ctx, bool inCombatFlow)
    {
        var now = ctx.Now;
        if (!ctx.Settings.RestEnabled || now < _nextRestToggleAt)
            return false;

        var me = ctx.World.Me;
        if (me.CurHp <= 0 || me.MaxHp <= 0)
            return false;

        if (ctx.Settings.AutoFight && inCombatFlow)
        {
            if (me.IsSitting && now >= _nextRestToggleAt && _combat.TryInject(ctx, PacketBuilder.BuildActionUse(0), "rest-stand-combat"))
            {
                _nextRestToggleAt = now.AddMilliseconds(950);
                _restExpectedIsSitting = false;
                _restExpectedStateUntilUtc = now.AddSeconds(4);
                _combat.SetDecision("rest-stand-combat");
                return true;
            }

            return false;
        }

        if (_restExpectedIsSitting.HasValue)
        {
            if (me.IsSitting == _restExpectedIsSitting.Value)
            {
                _restExpectedIsSitting = null;
                _restExpectedStateUntilUtc = DateTime.MinValue;
            }
            else if (now < _restExpectedStateUntilUtc)
            {
                return false;
            }
            else
            {
                _restExpectedIsSitting = null;
                _restExpectedStateUntilUtc = DateTime.MinValue;
            }
        }

        var sitAt = Math.Max(1, Math.Min(99, ctx.Settings.SitMpPct));
        var standAt = Math.Max(sitAt + 1, Math.Min(100, ctx.Settings.StandMpPct));
        var hpSitAt = ctx.Settings.RestSitHpPct;
        var hpStandAt = ctx.Settings.RestStandHpPct;
        var mp = me.MpPct;
        var hp = me.HpPct;

        var needMpSit = mp <= sitAt;
        var needHpSit = hpSitAt > 0 && me.EffectiveMaxHp > 0 && hp < hpSitAt;
        if (!me.IsSitting && (needMpSit || needHpSit))
        {
            if (!_combat.TryInject(ctx, PacketBuilder.BuildActionUse(0), "rest-sit"))
                return false;

            _nextRestToggleAt = now.AddMilliseconds(950);
            _restExpectedIsSitting = true;
            _restExpectedStateUntilUtc = now.AddSeconds(4);
            _combat.SetDecision($"rest-sit hp={hp:0.#}% mp={mp:0.#}%");
            return true;
        }

        if (!me.IsSitting)
            return false;

        var mpReady = mp >= standAt;
        var hpReady = hpStandAt <= 0 || me.EffectiveMaxHp <= 0 || hp >= hpStandAt;
        if (!mpReady || !hpReady)
            return false;

        if (!_combat.TryInject(ctx, PacketBuilder.BuildActionUse(0), "rest-stand"))
            return false;

        _nextRestToggleAt = now.AddMilliseconds(950);
        _restExpectedIsSitting = false;
        _restExpectedStateUntilUtc = now.AddSeconds(4);
        _combat.SetDecision($"rest-stand hp={hp:0.#}% mp={mp:0.#}%");
        return true;
    }

    public bool TryForceStand(BotContext ctx)
    {
        var now = ctx.Now;
        if (now < _nextRestToggleAt)
            return false;

        if (!_combat.TryInject(ctx, PacketBuilder.BuildActionUse(0), "force-stand"))
            return false;

        _nextRestToggleAt = now.AddMilliseconds(950);
        _restExpectedIsSitting = false;
        _restExpectedStateUntilUtc = now.AddSeconds(4);
        return true;
    }

    public void Reset()
    {
        _nextRestToggleAt = DateTime.MinValue;
        _restExpectedStateUntilUtc = DateTime.MinValue;
        _restExpectedIsSitting = null;
    }
}

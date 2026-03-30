using L2Companion.Bot;
using L2Companion.Protocol;
using L2Companion.World;

namespace L2Companion.Bot.Services;

public sealed class LootService
{
    private readonly CombatService _combat;

    private DateTime _nextLootAt = DateTime.MinValue;

    public LootService(CombatService combat)
    {
        _combat = combat;
    }

    public void Tick(BotContext ctx, bool inCombat, bool postKillActive, bool hasRecentDamage)
    {
        var now = ctx.Now;
        if (!ctx.Settings.AutoLoot || now < _nextLootAt)
            return;

        if (ctx.Settings.AutoFight)
        {
            var idleForLoot = !inCombat && !postKillActive && !hasRecentDamage;
            if (!idleForLoot)
                return;
        }

        if (postKillActive)
        {
            _combat.SetDecision("loot-paused-post-kill");
            return;
        }

        var me = ctx.World.Me;
        if (me.IsSitting)
        {
            _combat.SetDecision("loot-paused-sitting");
            _nextLootAt = now.AddMilliseconds(380);
            return;
        }

        if (ctx.Settings.RestEnabled && !inCombat && !postKillActive && !hasRecentDamage)
        {
            var mpStandAt = Math.Max(2, Math.Min(100, ctx.Settings.StandMpPct));
            if (me.MpPct < mpStandAt)
            {
                _combat.SetDecision($"loot-paused-mp-rest mp={me.MpPct:0.#}%<{mpStandAt}%");
                _nextLootAt = now.AddMilliseconds(380);
                return;
            }
        }

        var searchRange = Math.Max(ctx.Settings.LootRange, ctx.Settings.LootPickupRange);
        var searchRangeSq = (long)searchRange * searchRange;

        var item = ctx.World.Items.Values.MinBy(x => CombatService.DistanceSq(me.X, me.Y, x.X, x.Y));
        if (item is null || CombatService.DistanceSq(me.X, me.Y, item.X, item.Y) > searchRangeSq)
        {
            _nextLootAt = now.AddMilliseconds(280);
            return;
        }

        var corpsePickupRange = Math.Max(70, ctx.Settings.LootPickupRange);
        var corpsePickupRangeSq = (long)corpsePickupRange * corpsePickupRange;
        var itemDistSq = CombatService.DistanceSq(me.X, me.Y, item.X, item.Y);

        if (itemDistSq > corpsePickupRangeSq)
        {
            if (ctx.Settings.MoveToLoot && _combat.TryMoveTo(ctx, item.X, item.Y, item.Z))
                _nextLootAt = now.AddMilliseconds(160);
            else
                _nextLootAt = now.AddMilliseconds(280);

            return;
        }

        _combat.TryInject(ctx, PacketBuilder.BuildAction(item.ObjectId, me.X, me.Y, me.Z, 0), "loot-pickup-action");
        _nextLootAt = now.AddMilliseconds(280);
    }

    public void Reset()
    {
        _nextLootAt = DateTime.MinValue;
    }
}

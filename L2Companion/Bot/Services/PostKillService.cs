using L2Companion.Bot;
using L2Companion.Protocol;
using L2Companion.World;

namespace L2Companion.Bot.Services;

public sealed class PostKillService
{
    private readonly CombatService _combat;

    public bool IsActive { get; private set; }
    public int TargetObjectId { get; private set; }

    private int _npcTypeId;
    private int _x, _y, _z;
    private bool _spoilSucceeded;
    private DateTime _startedAtUtc = DateTime.MinValue;
    private DateTime _sweepUntilUtc = DateTime.MinValue;
    private DateTime _spawnWaitUntilUtc = DateTime.MinValue;
    private DateTime _minWaitForLootUntilUtc = DateTime.MinValue;
    private DateTime _nextActionAt = DateTime.MinValue;
    private DateTime _nextSweepAt = DateTime.MinValue;
    private int _lootActions;
    private int _emptyPolls;
    private int _itemsSeen;
    private int _itemsPicked;
    private int _itemsSkipped;
    private bool _targetCanceled;
    private int _sweepAttempts;
    private int _moveToCorpseAttempts;
    private bool _reachedCorpseZone;
    private readonly HashSet<int> _seenItemIds = [];
    private readonly Dictionary<int, int> _lootItemAttempts = [];
    private readonly Dictionary<int, DateTime> _recentByTarget = [];

    public PostKillService(CombatService combat)
    {
        _combat = combat;
    }

    public void Begin(BotContext ctx, NpcState target, bool spoilSucceeded, string reason)
    {
        var now = ctx.Now;
        if (IsActive && TargetObjectId != 0 && (now - _startedAtUtc).TotalMilliseconds < 7000)
            return;

        if (target.ObjectId != 0
            && _recentByTarget.TryGetValue(target.ObjectId, out var recentAt)
            && (now - recentAt).TotalMilliseconds < 4500)
            return;

        TargetObjectId = target.ObjectId;
        _npcTypeId = target.NpcTypeId;
        _x = target.X;
        _y = target.Y;
        _z = target.Z;
        _spoilSucceeded = spoilSucceeded;
        BeginCore(ctx, reason);
    }

    public void BeginFromSnapshot(BotContext ctx, int objectId, int npcTypeId, int x, int y, int z, bool spoilSucceeded,
        string reason)
    {
        if (objectId == 0)
            return;

        var now = ctx.Now;
        if (IsActive && TargetObjectId != 0 && (now - _startedAtUtc).TotalMilliseconds < 7000)
            return;

        if (_recentByTarget.TryGetValue(objectId, out var recentAt)
            && (now - recentAt).TotalMilliseconds < 4500)
            return;

        TargetObjectId = objectId;
        _npcTypeId = npcTypeId;
        _x = x;
        _y = y;
        _z = z;
        _spoilSucceeded = spoilSucceeded;
        BeginCore(ctx, reason);
    }

    private void BeginCore(BotContext ctx, string reason)
    {
        var settings = ctx.Settings;
        var now = ctx.Now;
        var me = ctx.World.Me;

        IsActive = true;
        _startedAtUtc = now;
        _sweepUntilUtc = now.AddMilliseconds(Math.Max(800, settings.PostKillSweepRetryWindowMs));
        var spawnWaitMs = Math.Max(0, settings.PostKillSpawnWaitMs);
        if (settings.CombatMode == CombatMode.HybridFsmPriority)
            spawnWaitMs = Math.Max(spawnWaitMs, 220);

        _spawnWaitUntilUtc = now.AddMilliseconds(spawnWaitMs);
        _minWaitForLootUntilUtc = now.AddMilliseconds(Math.Max(900, spawnWaitMs + 900));
        _nextActionAt = now;
        _lootActions = 0;
        _emptyPolls = 0;
        _itemsSeen = 0;
        _itemsPicked = 0;
        _itemsSkipped = 0;
        _targetCanceled = false;
        _sweepAttempts = 0;
        _moveToCorpseAttempts = 0;
        _reachedCorpseZone = false;
        var pickupRange = Math.Max(70, settings.LootPickupRange);
        var pickupRangeSq = (long)pickupRange * pickupRange;
        if (CombatService.DistanceSq(me.X, me.Y, _x, _y) <= pickupRangeSq)
            _reachedCorpseZone = true;

        _lootItemAttempts.Clear();
        _seenItemIds.Clear();

        if (TargetObjectId != 0)
            ctx.Log.Info($"[AutoFight] post-kill start oid=0x{TargetObjectId:X} reason={reason} spoil={_spoilSucceeded}");
    }

    public bool RunSweepAndLoot(BotContext ctx, CharacterState me)
    {
        if (!IsActive)
            return false;

        var now = ctx.Now;
        var settings = ctx.Settings;

        var postKillTimeboxMs = 6200;
        if (settings.MoveToLoot)
        {
            var distToKill = Math.Sqrt(CombatService.DistanceSq(me.X, me.Y, _x, _y));
            var travelBudgetMs = (int)Math.Min(7000, distToKill * 9.0);
            postKillTimeboxMs = Math.Min(14500, postKillTimeboxMs + travelBudgetMs);
        }

        if ((now - _startedAtUtc).TotalMilliseconds >= postKillTimeboxMs)
        {
            End(ctx, "post-kill-timebox");
            return false;
        }

        if (now < _nextActionAt)
            return true;

        var maxSweepAttempts = Math.Max(1, settings.SweepAttemptsPostKill);
        if (settings.SpoilEnabled && settings.PostKillSweepEnabled
            && settings.SweepEnabled && settings.SweepSkillId > 0
            && _spoilSucceeded && _sweepAttempts < maxSweepAttempts
            && now <= _sweepUntilUtc
            && (TargetObjectId == 0 || ctx.World.Npcs.ContainsKey(TargetObjectId)))
        {
            var sweepRetryMs = Math.Max(120, settings.PostKillSweepRetryIntervalMs);
            if (now >= _nextSweepAt)
            {
                if (_combat.TryCastSkill(ctx, settings.SweepSkillId, forBuff: false, cooldownOverrideMs: sweepRetryMs, allowReservedSkill: true, bypassSelfCastLock: true))
                    _sweepAttempts++;

                _nextSweepAt = now.AddMilliseconds(sweepRetryMs);
                _combat.SetDecision("post-kill-sweep");
            }

            _nextActionAt = now.AddMilliseconds(140);
            return true;
        }

        if (now < _spawnWaitUntilUtc)
        {
            _combat.SetDecision("post-kill-wait-drop");
            _nextActionAt = _spawnWaitUntilUtc;
            return true;
        }

        if (!_targetCanceled)
        {
            _combat.TryInject(ctx, PacketBuilder.BuildTargetCancel(), "post-kill-cancel-target");
            _targetCanceled = true;
            _nextActionAt = now.AddMilliseconds(100);
            return true;
        }

        if (TryRunLootBurst(ctx, me, now))
        {
            End(ctx, "loot-complete");
            return false;
        }

        return true;
    }

    private bool TryRunLootBurst(BotContext ctx, CharacterState me, DateTime now)
    {
        var settings = ctx.Settings;
        var maxAttempts = Math.Max(1, settings.PostKillLootMaxAttempts);
        if (settings.CombatMode == CombatMode.HybridFsmPriority)
            maxAttempts = Math.Max(maxAttempts, 14);

        if (settings.MoveToLoot)
        {
            var farCorpseRange = Math.Max(160, settings.LootPickupRange + 90);
            var farCorpseRangeSq = (long)farCorpseRange * farCorpseRange;
            if (CombatService.DistanceSq(me.X, me.Y, _x, _y) > farCorpseRangeSq)
                maxAttempts += 6;
        }

        var slackLootNearKill = HasUncollectedLootNearKill(ctx, me, extraWalkRadius: 480, extraKillRadius: 220);
        var effectiveMaxAttempts = maxAttempts + (slackLootNearKill ? 22 : 0);

        if (_lootActions >= effectiveMaxAttempts)
        {
            var hardAttempts = effectiveMaxAttempts + (settings.BattleMode == BotBattleMode.StrictCaster ? 10 : 8);
            var shouldExtend = _itemsSeen > _itemsPicked && _lootActions < hardAttempts;
            if (shouldExtend)
            {
                _nextActionAt = now.AddMilliseconds(200);
                return false;
            }

            return true;
        }

        if (!TryGetLootItem(ctx, me, out var item, out var distFromMeSq))
        {
            var corpsePickupRange = Math.Max(70, settings.LootPickupRange);
            var corpsePickupRangeSq = (long)corpsePickupRange * corpsePickupRange;
            if (CombatService.DistanceSq(me.X, me.Y, _x, _y) <= corpsePickupRangeSq)
                _reachedCorpseZone = true;

            if (settings.MoveToLoot
                && CombatService.DistanceSq(me.X, me.Y, _x, _y) > corpsePickupRangeSq
                && _combat.TryMoveTo(ctx, _x, _y, _z))
            {
                _moveToCorpseAttempts++;
                _nextActionAt = now.AddMilliseconds(settings.BattleMode == BotBattleMode.StrictCaster ? 300 : 240);
                return false;
            }

            _emptyPolls++;
            // Do not burn PostKillLootMaxAttempts on empty polls — that made the bot "freeze" near corpse then quit early.

            _nextActionAt = now.AddMilliseconds(_reachedCorpseZone ? 95 : 180);
            var emptyPollLimit = _reachedCorpseZone ? 16 : (settings.BattleMode == BotBattleMode.StrictCaster ? 18 : 10);
            var hadCorpseAttempt = _reachedCorpseZone || _moveToCorpseAttempts > 0 || !settings.MoveToLoot;
            if (_itemsSeen > 0 && _itemsPicked == 0)
                return false;

            var doneEmpty = _emptyPolls >= emptyPollLimit && hadCorpseAttempt && now >= _minWaitForLootUntilUtc;
            if (doneEmpty && HasUncollectedLootNearKill(ctx, me, extraWalkRadius: 520, extraKillRadius: 280))
            {
                _emptyPolls = Math.Max(0, _emptyPolls - 8);
                _nextActionAt = now.AddMilliseconds(220);
                return false;
            }

            return doneEmpty;
        }

        _emptyPolls = 0;
        if (_seenItemIds.Add(item.ObjectId))
            _itemsSeen++;

        var perItemRetry = Math.Max(1, settings.PostKillLootItemRetry);
        if (settings.CombatMode == CombatMode.HybridFsmPriority)
            perItemRetry = Math.Max(perItemRetry, 3);

        var usedAttempts = _lootItemAttempts.GetValueOrDefault(item.ObjectId);
        if (usedAttempts >= perItemRetry)
        {
            _itemsSkipped++;
            _lootActions++;
            _nextActionAt = now.AddMilliseconds(120);
            return false;
        }

        var pickupRangeNear = Math.Max(70, settings.LootPickupRange);
        var pickupRangeNearSq = (long)pickupRangeNear * pickupRangeNear;

        if (distFromMeSq > pickupRangeNearSq)
        {
            var maxMoveToLoot = settings.BattleMode == BotBattleMode.StrictCaster
                ? Math.Max(420, pickupRangeNear + 300)
                : Math.Max(560, pickupRangeNear + 380);
            var maxMoveToLootSq = (long)maxMoveToLoot * maxMoveToLoot;
            if (distFromMeSq <= maxMoveToLootSq && settings.MoveToLoot && _combat.TryMoveTo(ctx, item.X, item.Y, item.Z))
            {
                _nextActionAt = now.AddMilliseconds(220);
                return false;
            }

            _lootItemAttempts[item.ObjectId] = perItemRetry;
            _itemsSkipped++;
            _lootActions++;
            _nextActionAt = now.AddMilliseconds(180);
            return false;
        }

        _lootItemAttempts[item.ObjectId] = usedAttempts + 1;
        _itemsPicked++;
        _lootActions++;

        if (!_combat.TryInject(ctx, PacketBuilder.BuildAction(item.ObjectId, me.X, me.Y, me.Z, 0), "post-kill-loot-pickup-action"))
            _combat.TryInject(ctx, PacketBuilder.BuildGetItem(item.X, item.Y, item.Z, item.ObjectId), "post-kill-loot-pickup-48");

        _nextActionAt = now.AddMilliseconds(_reachedCorpseZone ? 80 : 180);
        return false;
    }

    /// <summary>True if a ground stack near this kill is still eligible (not exhausted per-item retries).</summary>
    private bool HasUncollectedLootNearKill(BotContext ctx, CharacterState me, int extraWalkRadius, int extraKillRadius)
    {
        var settings = ctx.Settings;
        var perItemRetry = Math.Max(1, settings.PostKillLootItemRetry);
        if (settings.CombatMode == CombatMode.HybridFsmPriority)
            perItemRetry = Math.Max(perItemRetry, 3);

        var corpsePickupRange = Math.Max(70, settings.LootPickupRange);
        var maxMoveToLoot = settings.BattleMode == BotBattleMode.StrictCaster
            ? Math.Max(420, corpsePickupRange + 300)
            : Math.Max(560, corpsePickupRange + 380);
        maxMoveToLoot += extraWalkRadius;
        var maxMoveToLootSq = (long)maxMoveToLoot * maxMoveToLoot;

        var baseRange = Math.Max(Math.Max(settings.LootRange, settings.LootPickupRange), 450);
        var killRange = baseRange + (settings.BattleMode == BotBattleMode.StrictCaster ? 460 : 220) + 520 + extraKillRadius;
        var killRangeSq = (long)killRange * killRange;

        foreach (var cur in ctx.World.Items.Values)
        {
            if (_lootItemAttempts.GetValueOrDefault(cur.ObjectId) >= perItemRetry)
                continue;

            if (CombatService.DistanceSq(_x, _y, cur.X, cur.Y) > killRangeSq)
                continue;

            if (CombatService.DistanceSq(me.X, me.Y, cur.X, cur.Y) > maxMoveToLootSq)
                continue;

            return true;
        }

        return false;
    }

    private bool TryGetLootItem(BotContext ctx, CharacterState me, out GroundItemState item, out long distFromMeSq)
    {
        item = null!;
        distFromMeSq = 0;
        var settings = ctx.Settings;

        var perItemRetry = Math.Max(1, settings.PostKillLootItemRetry);
        if (settings.CombatMode == CombatMode.HybridFsmPriority)
            perItemRetry = Math.Max(perItemRetry, 3);

        var corpsePickupRange = Math.Max(70, settings.LootPickupRange);
        var maxMoveToLoot = settings.BattleMode == BotBattleMode.StrictCaster
            ? Math.Max(420, corpsePickupRange + 300)
            : Math.Max(560, corpsePickupRange + 380);
        var maxMoveToLootSq = (long)maxMoveToLoot * maxMoveToLoot;
        var baseRange = Math.Max(Math.Max(settings.LootRange, settings.LootPickupRange), 450);
        // Drops often spawn off the corpse XY we last saw — extra slack so stacks still associate with this kill.
        var killRange = baseRange + (settings.BattleMode == BotBattleMode.StrictCaster ? 460 : 220) + 520;
        var killRangeSq = (long)killRange * killRange;

        GroundItemState? best = null;
        var bestScore = long.MaxValue;
        var bestDistFromMe = long.MaxValue;

        foreach (var cur in ctx.World.Items.Values)
        {
            if (_lootItemAttempts.GetValueOrDefault(cur.ObjectId) >= perItemRetry)
                continue;

            var distKillSq = CombatService.DistanceSq(_x, _y, cur.X, cur.Y);
            var distMeSq = CombatService.DistanceSq(me.X, me.Y, cur.X, cur.Y);
            if (distKillSq > killRangeSq)
                continue;

            if (distMeSq > maxMoveToLootSq)
                continue;

            var herbBoost = TargetingService.IsLikelyHerbItemId(cur.ItemId) ? -220L : 0L;
            var score = distMeSq + distKillSq / 2 + herbBoost;
            if (score < bestScore)
            {
                best = cur;
                bestScore = score;
                bestDistFromMe = distMeSq;
            }
        }

        // At corpse but strict scoring missed (offset coords): grab nearest ground stack still tied to this kill.
        if (best is null)
        {
            var nearCorpseSq = (long)380 * 380;
            if (CombatService.DistanceSq(me.X, me.Y, _x, _y) <= nearCorpseSq)
            {
                var rescueWalkSq = (long)980 * 980;
                foreach (var cur in ctx.World.Items.Values)
                {
                    if (_lootItemAttempts.GetValueOrDefault(cur.ObjectId) >= perItemRetry)
                        continue;

                    var distKillSq = CombatService.DistanceSq(_x, _y, cur.X, cur.Y);
                    if (distKillSq > killRangeSq)
                        continue;

                    var distMeSq = CombatService.DistanceSq(me.X, me.Y, cur.X, cur.Y);
                    if (distMeSq > rescueWalkSq)
                        continue;

                    if (best is null || distMeSq < bestDistFromMe)
                    {
                        best = cur;
                        bestDistFromMe = distMeSq;
                    }
                }
            }
        }

        if (best is null)
            return false;

        item = best;
        distFromMeSq = bestDistFromMe;
        return true;
    }

    public void End(BotContext ctx, string reason)
    {
        if (TargetObjectId != 0)
            _recentByTarget[TargetObjectId] = DateTime.UtcNow;

        var pruneBefore = DateTime.UtcNow.AddSeconds(-20);
        foreach (var kv in _recentByTarget.Where(x => x.Value < pruneBefore).ToArray())
            _recentByTarget.Remove(kv.Key);

        ctx.Log.Info($"[AutoFight] post-kill summary oid=0x{TargetObjectId:X} reason={reason} seen={_itemsSeen} picked={_itemsPicked} skipped={_itemsSkipped} polls={_emptyPolls} actions={_lootActions}");
        _combat.SetDecision($"post-kill:{reason}");
        Reset();
    }

    public void Reset()
    {
        IsActive = false;
        TargetObjectId = 0;
        _npcTypeId = 0;
        _x = 0; _y = 0; _z = 0;
        _spoilSucceeded = false;
        _startedAtUtc = DateTime.MinValue;
        _sweepUntilUtc = DateTime.MinValue;
        _spawnWaitUntilUtc = DateTime.MinValue;
        _minWaitForLootUntilUtc = DateTime.MinValue;
        _nextActionAt = DateTime.MinValue;
        _nextSweepAt = DateTime.MinValue;
        _lootActions = 0;
        _emptyPolls = 0;
        _itemsSeen = 0;
        _itemsPicked = 0;
        _itemsSkipped = 0;
        _targetCanceled = false;
        _sweepAttempts = 0;
        _moveToCorpseAttempts = 0;
        _reachedCorpseZone = false;
        _lootItemAttempts.Clear();
        _seenItemIds.Clear();
    }
}

using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using L2Companion.Proxy;
using L2Companion.World;

namespace L2Companion.UI;

public partial class OverlayWidgetWindow : Window
{
    public OverlayWidgetWindow()
    {
        InitializeComponent();
        Left = 40;
        Top = 40;
    }

    public void SetStats(WorldSnapshot snapshot, ProxyDiagnostics diag, bool botRunning)
    {
        _ = diag;

        if (Dispatcher.CheckAccess())
        {
            UpdateStats(snapshot, botRunning);
            return;
        }

        Dispatcher.BeginInvoke(() => UpdateStats(snapshot, botRunning));
    }

    private void UpdateStats(WorldSnapshot snapshot, bool botRunning)
    {
        var me = snapshot.Me;
        var hpPct = ClampPct(me.HpPct);
        var mpPct = ClampPct(me.MpPct);
        var cpPct = me.MaxCp > 0 ? ClampPct(me.CurCp * 100.0 / me.MaxCp) : 0;

        CharacterNameText.Text = string.IsNullOrWhiteSpace(me.Name) ? "Character" : me.Name;
        CharacterMetaText.Text = $"Lvl {me.Level} | Class {me.ClassId} | X:{me.X} Y:{me.Y}";
        ClockText.Text = DateTime.Now.ToString("HH:mm:ss");

        BotStateText.Text = botRunning ? "BOT ON" : "BOT OFF";
        BotStateBadge.Background = botRunning ? B("#2F7350") : B("#2A4E66");
        BotStateBadge.BorderBrush = botRunning ? B("#8BE6AE") : B("#7CC3FF");

        HpBar.Value = hpPct;
        MpBar.Value = mpPct;
        CpBar.Value = cpPct;

        HpBar.Foreground = HpBrush(hpPct);
        MpBar.Foreground = B("#63A9FF");
        CpBar.Foreground = B("#A08CFF");

        HpText.Text = $"{me.CurHp}/{Math.Max(1, me.EffectiveMaxHp)} ({hpPct:0.#}%)";
        MpText.Text = $"{me.CurMp}/{Math.Max(1, me.EffectiveMaxMp)} ({mpPct:0.#}%)";
        CpText.Text = $"{me.CurCp}/{Math.Max(1, me.MaxCp)} ({cpPct:0.#}%)";

        RenderTargetCard(snapshot);
        RenderNearby(snapshot);
        RenderSession(snapshot);
    }

    private void RenderTargetCard(WorldSnapshot snapshot)
    {
        var me = snapshot.Me;
        var tid = me.TargetId;
        var npc = snapshot.Npcs.FirstOrDefault(x => x.ObjectId == tid);
        if (tid != 0 && npc is not null)
        {
            var hp = ClampPct(npc.HpPct);
            var dist = Math.Sqrt(DistanceSq(me.X, me.Y, npc.X, npc.Y));
            TargetNameText.Text = string.IsNullOrWhiteSpace(npc.Name) ? $"NPC {NpcId(npc)}" : npc.Name;
            TargetDetailsText.Text = $"Dist {dist:0} | OID 0x{npc.ObjectId:X}";
            TargetHpBar.Value = hp;
            TargetHpBar.Foreground = HpBrush(hp);
            TargetHpText.Text = $"HP {hp:0.#}%";
            return;
        }

        TargetNameText.Text = tid == 0 ? "No target" : $"Target 0x{tid:X}";
        TargetDetailsText.Text = "Dist -- | OID --";
        TargetHpBar.Value = 0;
        TargetHpBar.Foreground = B("#718BA7");
        TargetHpText.Text = "HP --";
    }

    private void RenderNearby(WorldSnapshot snapshot)
    {
        var me = snapshot.Me;

        var mobs = snapshot.Npcs
            .Where(x => x.IsAttackable && !x.IsDead)
            .OrderBy(x => DistanceSq(me.X, me.Y, x.X, x.Y))
            .Take(2)
            .Select(x =>
            {
                var name = string.IsNullOrWhiteSpace(x.Name) ? $"NPC {NpcId(x)}" : x.Name;
                var dist = Math.Sqrt(DistanceSq(me.X, me.Y, x.X, x.Y));
                return $"{name} {dist:0}u";
            })
            .ToArray();

        NearbyMobsText.Text = mobs.Length > 0
            ? "Mobs: " + string.Join(" | ", mobs)
            : "Mobs: -";

        var loot = snapshot.Items
            .OrderBy(x => DistanceSq(me.X, me.Y, x.X, x.Y))
            .Take(2)
            .Select(x =>
            {
                var dist = Math.Sqrt(DistanceSq(me.X, me.Y, x.X, x.Y));
                return $"{x.ItemId}x{x.Count} ({dist:0}u)";
            })
            .ToArray();

        NearbyLootText.Text = loot.Length > 0
            ? "Loot: " + string.Join(" | ", loot)
            : "Loot: -";
    }

    private void RenderSession(WorldSnapshot snapshot)
    {
        var s = snapshot.SessionStats;
        var elapsed = DateTime.UtcNow - s.SessionStartUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        SessionKillsText.Text = $"Kills: {s.Kills}";
        SessionLootText.Text = $"Loot: {s.LootPickedCount}";
        SessionAdenaText.Text = $"Adena+: {s.AdenaGained:N0}";
        SessionTimeText.Text = $"Time: {elapsed:hh\\:mm\\:ss}";
    }

    private static Brush HpBrush(double pct)
    {
        if (pct < 28) return B("#E35D6A");
        if (pct < 55) return B("#E6A963");
        return B("#65D78C");
    }

    private static double ClampPct(double value) => Math.Max(0, Math.Min(100, value));

    private static int NpcId(NpcSnapshot npc) => npc.NpcTypeId > 1_000_000 ? npc.NpcTypeId - 1_000_000 : npc.NpcTypeId;

    private static long DistanceSq(int x1, int y1, int x2, int y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return (long)dx * dx + (long)dy * dy;
    }

    private static Brush B(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

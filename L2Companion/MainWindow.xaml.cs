using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using L2Companion.Bot;
using L2Companion.Core;
using L2Companion.Proxy;
using L2Companion.UI;
using L2Companion.World;

namespace L2Companion;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly LogService _log;
    private int _pendingLogScroll;
    private DateTime _lastUiTickAtUtc = DateTime.UtcNow;
    private DateTime _nextLogAutoScrollAtUtc = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();

        var log = new LogService();
        _log = log;

        var world = new GameWorldState();
        var parser = new GamePacketParser(world, log);
        var proxy = new ProxyService(log, parser);
        var bot = new BotEngine(proxy, world, log);
        var stateStore = new UiStateStore(AppDomain.CurrentDomain.BaseDirectory);

        _vm = new MainViewModel(log, proxy, bot, world, stateStore);
        DataContext = _vm;
        _vm.TryAutoStartProxyOnLaunch();

        var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "L2Companion.exe");
        var buildUtc = File.Exists(exePath) ? File.GetLastWriteTimeUtc(exePath) : DateTime.UtcNow;
        var buildTag = buildUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
        Title = $"L2 Companion Control Center [{buildTag}]";
        log.Info($"Build: {buildTag}  exe={exePath}");

        Closing += (_, _) => _vm.OnWindowClosing();
        log.LogAdded += _ => Interlocked.Increment(ref _pendingLogScroll);

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        timer.Tick += OnUiTick;
        timer.Start();

        log.Info("UI started. Auto-starting proxy.");
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var uiLagMs = (now - _lastUiTickAtUtc).TotalMilliseconds;
        _lastUiTickAtUtc = now;

        if (uiLagMs > 1800)
        {
            _log.Info($"[UI] Dispatcher lag detected: {uiLagMs:0} ms");
            CrashDiagnostics.WriteMarker("ui-lag", $"{uiLagMs:0}ms");
        }

        _vm.RefreshUiTick();

        var pending = Interlocked.Exchange(ref _pendingLogScroll, 0);
        if (pending <= 0 || LogList.Items.Count <= 0)
        {
            return;
        }

        LogList.ScrollIntoView(LogList.Items[^1]);
    }
}



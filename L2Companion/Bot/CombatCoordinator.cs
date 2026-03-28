using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using L2Companion.Core;

namespace L2Companion.Bot;

public sealed class CombatIntent
{
    public int LeaderObjectId { get; set; }
    public int LeaderTargetOid { get; set; }
    public int LeaderX { get; set; }
    public int LeaderY { get; set; }
    public int LeaderZ { get; set; }
    public string CombatState { get; set; } = "idle";
    public long PullTimestampUnixMs { get; set; }
    public long Sequence { get; set; }
}

internal sealed class CombatCoordinator : IDisposable
{
    private readonly LogService _log;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private CancellationTokenSource? _cts;
    private Task? _workerTask;

    private bool _enabled;
    private CoordMode _mode = CoordMode.Standalone;
    private string _pipeName = "l2companion_combat_v2";

    private CombatIntent _latestIntent = new();
    private DateTime _latestIntentAtUtc = DateTime.MinValue;
    private string _latestIntentJson = string.Empty;

    public CombatCoordinator(LogService log)
    {
        _log = log;
    }

    public CoordMode Mode
    {
        get
        {
            lock (_gate)
            {
                return _mode;
            }
        }
    }

    public void Configure(bool enabled, CoordMode mode, string? pipeName)
    {
        mode = enabled ? mode : CoordMode.Standalone;
        var normalizedPipe = NormalizePipeName(pipeName);

        lock (_gate)
        {
            if (_enabled == enabled && _mode == mode && string.Equals(_pipeName, normalizedPipe, StringComparison.Ordinal))
            {
                return;
            }
        }

        StopWorker();

        lock (_gate)
        {
            _enabled = enabled;
            _mode = mode;
            _pipeName = normalizedPipe;
        }

        if (!enabled || mode == CoordMode.Standalone)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        _workerTask = mode == CoordMode.CoordinatorLeader
            ? Task.Run(() => LeaderAcceptLoopAsync(cts.Token), cts.Token)
            : Task.Run(() => FollowerReadLoopAsync(cts.Token), cts.Token);

        _log.Info($"[Coordinator] {mode} pipe={normalizedPipe}");
    }

    public void Publish(CombatIntent intent)
    {
        lock (_gate)
        {
            if (!_enabled || _mode != CoordMode.CoordinatorLeader)
            {
                return;
            }

            _latestIntent = intent;
            _latestIntentAtUtc = DateTime.UtcNow;
            _latestIntentJson = JsonSerializer.Serialize(intent, _jsonOptions);
        }
    }

    public bool TryGetLatestIntent(int staleMs, out CombatIntent intent)
    {
        lock (_gate)
        {
            intent = _latestIntent;
            if (!_enabled || _mode != CoordMode.CoordinatorFollower || _latestIntentAtUtc == DateTime.MinValue)
            {
                return false;
            }

            var maxStale = Math.Max(500, staleMs);
            if ((DateTime.UtcNow - _latestIntentAtUtc).TotalMilliseconds > maxStale)
            {
                return false;
            }

            return true;
        }
    }

    private async Task LeaderAcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                var pipeName = GetPipeName();
                server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => LeaderClientWriterAsync(server, ct), ct);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch
            {
                server?.Dispose();
                await Task.Delay(350, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task LeaderClientWriterAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        await using var _ = server.ConfigureAwait(false);
        await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n"
        };

        var lastSequence = long.MinValue;
        while (!ct.IsCancellationRequested && server.IsConnected)
        {
            string json;
            long sequence;

            lock (_gate)
            {
                json = _latestIntentJson;
                sequence = _latestIntent.Sequence;
            }

            if (!string.IsNullOrEmpty(json) && sequence != lastSequence)
            {
                await writer.WriteLineAsync(json).ConfigureAwait(false);
                lastSequence = sequence;
            }

            await Task.Delay(110, ct).ConfigureAwait(false);
        }
    }

    private async Task FollowerReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipeName = GetPipeName();
                await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.In, PipeOptions.Asynchronous);
                await client.ConnectAsync(1200, ct).ConfigureAwait(false);
                using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

                while (!ct.IsCancellationRequested && client.IsConnected)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        break;
                    }

                    CombatIntent? parsed = null;
                    try
                    {
                        parsed = JsonSerializer.Deserialize<CombatIntent>(line, _jsonOptions);
                    }
                    catch
                    {
                        // ignore malformed frame
                    }

                    if (parsed is null)
                    {
                        continue;
                    }

                    lock (_gate)
                    {
                        _latestIntent = parsed;
                        _latestIntentAtUtc = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(650, ct).ConfigureAwait(false);
            }
        }
    }

    private string GetPipeName()
    {
        lock (_gate)
        {
            return _pipeName;
        }
    }

    private void StopWorker()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignore
        }

        try
        {
            _workerTask?.Wait(600);
        }
        catch
        {
            // ignore
        }

        _cts?.Dispose();
        _cts = null;
        _workerTask = null;
    }

    private static string NormalizePipeName(string? raw)
    {
        var fallback = "l2companion_combat_v2";
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var chars = raw.Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray();
        var normalized = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    public void Dispose()
    {
        StopWorker();
    }
}



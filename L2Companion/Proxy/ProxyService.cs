using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using L2Companion.Core;
using L2Companion.World;

namespace L2Companion.Proxy;

public sealed class ProxyService
{
    private readonly LogService _log;
    private readonly GamePacketParser _parser;
    private readonly ServerProfileResolver _profileResolver = new();

    private TcpListener? _loginListener;
    private TcpListener? _gameListener;
    private CancellationTokenSource? _cts;

    private readonly object _sessionGate = new();
    private GameProxySession? _currentGameSession;
    private readonly ConcurrentQueue<byte[]> _pendingInjectPackets = new();

    private readonly object _endpointGate = new();
    private string _realGameHost = "127.0.0.1";
    private int _realGamePort = 7777;

    public ProxyService(LogService log, GamePacketParser parser)
    {
        _log = log;
        _parser = parser;
    }

    public bool IsRunning { get; private set; }
    public ProxyDiagnostics Diagnostics { get; } = new();

    public async Task StartAsync(ProxySettings settings)
    {
        if (IsRunning)
        {
            return;
        }

        lock (_endpointGate)
        {
            _realGameHost = settings.GameHost;
            _realGamePort = settings.GamePort;
            Diagnostics.RealGameEndpoint = $"{_realGameHost}:{_realGamePort}";
        }

        _profileResolver.Reset(settings.ServerProfileMode);

        Diagnostics.LastError = "-";
        Diagnostics.LastDropReason = "-";
        Diagnostics.LastInjectResult = "-";
        Diagnostics.ServerProfile = _profileResolver.ResolvedProfile;
        Diagnostics.SessionStage = "starting";
        Diagnostics.SetPendingInjectPackets(0);

        _cts = new CancellationTokenSource();
        _loginListener = new TcpListener(IPAddress.Loopback, settings.LocalLoginPort);
        _gameListener = new TcpListener(IPAddress.Loopback, settings.LocalGamePort);

        _loginListener.Start();
        _gameListener.Start();

        _ = AcceptLoginLoopAsync(settings, _cts.Token);
        _ = AcceptGameLoopAsync(settings, _cts.Token);

        IsRunning = true;
        Diagnostics.SessionStage = "listening";
        _log.Info($"Proxy started. Login 127.0.0.1:{settings.LocalLoginPort}, Game 127.0.0.1:{settings.LocalGamePort}");

        await Task.CompletedTask;
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        _cts?.Cancel();
        _loginListener?.Stop();
        _gameListener?.Stop();

        lock (_sessionGate)
        {
            _currentGameSession = null;
            while (_pendingInjectPackets.TryDequeue(out _))
            {
                // drained
            }
        }

        IsRunning = false;

        Diagnostics.LoginClientConnected = false;
        Diagnostics.LoginServerConnected = false;
        Diagnostics.LoginBlowfishReady = false;
        Diagnostics.GameClientConnected = false;
        Diagnostics.GameServerConnected = false;
        Diagnostics.GameCryptoReady = false;
        Diagnostics.SessionStage = "stopped";
        Diagnostics.SetPendingInjectPackets(0);

        _log.Info("Proxy stopped.");
    }

    public void InjectToServer(byte[] plainBody)
    {
        lock (_sessionGate)
        {
            if (_currentGameSession is not null)
            {
                _currentGameSession.EnqueueInjectPacket(plainBody);
                Diagnostics.SetPendingInjectPackets(_pendingInjectPackets.Count);
                return;
            }

            _pendingInjectPackets.Enqueue(plainBody);
            Diagnostics.MarkInjectResult("queued-before-session");
            Diagnostics.SetPendingInjectPackets(_pendingInjectPackets.Count);
        }
    }

    private void OnGameServerDiscovered(string host, int port)
    {
        lock (_endpointGate)
        {
            _realGameHost = host;
            _realGamePort = port;
            Diagnostics.RealGameEndpoint = $"{host}:{port}";
            Diagnostics.SessionStage = "game-endpoint-discovered";
        }
    }

    private async Task AcceptLoginLoopAsync(ProxySettings settings, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _loginListener!.AcceptTcpClientAsync(ct);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (client)
                        {
                            Diagnostics.SessionStage = "login-client-connected";
                            var session = new LoginProxySession(client, settings, _log, Diagnostics, OnGameServerDiscovered);
                            await session.RunAsync(ct);
                        }
                    }
                    catch (Exception ex) when (NetHelpers.IsExpectedDisconnect(ex, ct))
                    {
                        Diagnostics.SessionStage = "login-disconnected";
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.LastError = $"Login relay error: {ex.Message}";
                        _log.Info($"Login relay error: {ex.Message}");
                    }
                }, CancellationToken.None);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Diagnostics.LastError = $"Login listener error: {ex.Message}";
                _log.Info($"Login listener error: {ex.Message}");
            }
        }
    }

    private async Task AcceptGameLoopAsync(ProxySettings settings, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _gameListener!.AcceptTcpClientAsync(ct);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (client)
                        {
                            string host;
                            int port;
                            lock (_endpointGate)
                            {
                                host = _realGameHost;
                                port = _realGamePort;
                            }

                            Diagnostics.SessionStage = "game-client-connected";
                            var session = new GameProxySession(client, host, port, _log, _parser, Diagnostics, _profileResolver, OnGameSessionDisconnected);
                            lock (_sessionGate)
                            {
                                _currentGameSession = session;
                                FlushPendingInjectPackets(session);
                            }

                            await session.RunAsync(ct);
                        }
                    }
                    catch (Exception ex) when (NetHelpers.IsExpectedDisconnect(ex, ct))
                    {
                        Diagnostics.SessionStage = "game-disconnected";
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.LastError = $"Game relay error: {ex.Message}";
                        _log.Info($"Game relay error: {ex.Message}");
                    }
                }, CancellationToken.None);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Diagnostics.LastError = $"Game listener error: {ex.Message}";
                _log.Info($"Game listener error: {ex.Message}");
            }
        }
    }

    private void OnGameSessionDisconnected(GameProxySession session)
    {
        lock (_sessionGate)
        {
            if (!ReferenceEquals(_currentGameSession, session))
            {
                return;
            }

            _currentGameSession = null;
            Diagnostics.GameClientConnected = false;
            Diagnostics.GameServerConnected = false;
            Diagnostics.GameCryptoReady = false;
            Diagnostics.SessionStage = "game-disconnected";
            Diagnostics.SetPendingInjectPackets(_pendingInjectPackets.Count);
        }
    }

    private void FlushPendingInjectPackets(GameProxySession session)
    {
        var flushed = 0;
        while (_pendingInjectPackets.TryDequeue(out var pending))
        {
            session.EnqueueInjectPacket(pending);
            flushed++;
        }

        Diagnostics.SetPendingInjectPackets(_pendingInjectPackets.Count);

        if (flushed > 0)
        {
            Diagnostics.MarkInjectResult($"flushed:{flushed}");
            _log.Info($"Flushed pending inject packets: {flushed}");
        }
    }
}

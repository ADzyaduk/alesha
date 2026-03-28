using System.Collections.ObjectModel;
using System.Windows;

namespace L2Companion.Core;

public sealed class UiLog
{
    public DateTime Time { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class LogService
{
    private readonly ObservableCollection<UiLog> _logs = new();
    private readonly object _gate = new();

    private DateTime _windowStartUtc = DateTime.UtcNow;
    private int _windowCount;
    private int _droppedInWindow;
    private string _lastMessage = string.Empty;
    private DateTime _lastMessageAtUtc = DateTime.MinValue;

    private const int MaxLogsPerSecond = 24;
    private const int MaxLogEntries = 900;
    private const int TrimBatchSize = 120;

    public ObservableCollection<UiLog> Logs => _logs;

    public event Action<UiLog>? LogAdded;

    public void Info(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        UiLog? item = null;
        var nowUtc = DateTime.UtcNow;

        lock (_gate)
        {
            if ((nowUtc - _windowStartUtc).TotalSeconds >= 1)
            {
                _windowStartUtc = nowUtc;
                _windowCount = 0;
                _droppedInWindow = 0;
            }

            if (string.Equals(message, _lastMessage, StringComparison.Ordinal)
                && (nowUtc - _lastMessageAtUtc).TotalMilliseconds < 350)
            {
                return;
            }

            if (_windowCount >= MaxLogsPerSecond)
            {
                _droppedInWindow++;
                if (_droppedInWindow > 1)
                {
                    return;
                }

                _windowCount++;
                item = new UiLog
                {
                    Time = DateTime.Now,
                    Message = "Log flood detected, throttling..."
                };
            }
            else
            {
                _windowCount++;
                _lastMessage = message;
                _lastMessageAtUtc = nowUtc;
                item = new UiLog { Time = DateTime.Now, Message = message };
            }
        }

        Post(item);
    }

    private void Post(UiLog item)
    {
        var app = Application.Current;
        if (app?.Dispatcher is not null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => AddInternal(item));
            return;
        }

        AddInternal(item);
    }

    private void AddInternal(UiLog item)
    {
        lock (_gate)
        {
            _logs.Add(item);

            var overflow = _logs.Count - MaxLogEntries;
            if (overflow > 0)
            {
                var trimCount = Math.Min(_logs.Count, Math.Max(overflow, TrimBatchSize));
                for (var i = 0; i < trimCount; i++)
                {
                    _logs.RemoveAt(0);
                }
            }
        }

        LogAdded?.Invoke(item);
    }
}

using DeviceCheck.Agent.Core.Model;

namespace DeviceCheck.Agent.Core.Collection;

/// <summary>
/// Long-lived scan owner for the tray app and the local API. Only one scan runs at a time: concurrent callers
/// share it. Reports are kept in memory for the current run only (never written to disk by this class).
/// </summary>
public sealed class ScanService(Func<IReadOnlyList<IHardwareCollector>> collectors, ScanOptions options, int keep = 5)
{
    private readonly object _lock = new();
    private readonly LinkedList<HardwareReport> _reports = new();
    private Task<HardwareReport>? _running;

    /// <summary>Raised on scan start, per-category progress and completion (on a thread-pool thread).</summary>
    public event Action<ScanProgress>? Progress;
    public event Action<HardwareReport>? Completed;

    public bool IsScanning { get { lock (_lock) return _running is { IsCompleted: false }; } }

    public HardwareReport? Latest { get { lock (_lock) return _reports.First?.Value; } }

    public HardwareReport? Get(string id)
    {
        lock (_lock) return _reports.FirstOrDefault(r => string.Equals(r.Scan.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Starts a scan, or joins the one already running.</summary>
    public Task<HardwareReport> ScanAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_running is { IsCompleted: false }) return _running.WaitAsync(ct);
            _running = RunAsync(ct);
            return _running;
        }
    }

    private async Task<HardwareReport> RunAsync(CancellationToken ct)
    {
        await Task.Yield(); // never run collectors on the caller's (UI) thread
        var progress = new Relay(p => Progress?.Invoke(p));
        var report = await new ScanOrchestrator(collectors(), options).RunAsync(progress, ct).ConfigureAwait(false);
        lock (_lock)
        {
            _reports.AddFirst(report);
            while (_reports.Count > Math.Max(1, keep)) _reports.RemoveLast();
        }
        Completed?.Invoke(report);
        return report;
    }

    private sealed class Relay(Action<ScanProgress> fn) : IProgress<ScanProgress>
    {
        public void Report(ScanProgress value) => fn(value);
    }
}

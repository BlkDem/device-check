using DeviceCheck.Agent.Export;
using DeviceCheck.Agent.Monitoring.Core;

namespace DeviceCheck.Agent.UI;

/// <summary>
/// Storage health for the local API: one sample at a time, reused for <see cref="MaxAge"/> so that page reloads
/// never turn into frequent SMART queries. Kept in memory only.
/// </summary>
internal sealed class StorageHealthCache(IHealthMonitor<StorageHealthReport> monitor)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, string Json)? _last;

    public async Task<string> GetJsonAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_last is { } l && DateTimeOffset.UtcNow - l.At < MaxAge) return l.Json;
            var report = await monitor.SampleAsync(SampleKind.Full, ct).ConfigureAwait(false);
            var json = ReportJson.Serialize(report, indented: false);
            _last = (DateTimeOffset.UtcNow, json);
            return json;
        }
        finally { _gate.Release(); }
    }
}

using System.Diagnostics;
using DeviceCheck.Agent.Core.Model;

namespace DeviceCheck.Agent.Core.Collection;

/// <summary>
/// A collector fills one category of the report. It must handle its own source errors where possible
/// (recording them via <see cref="CollectorContext"/>); any exception that escapes is caught by the orchestrator
/// and never stops other collectors.
/// </summary>
public interface IHardwareCollector
{
    /// <summary>Stable id: "cpu", "memory", ... (also used for progress and diagnostics).</summary>
    string Id { get; }

    /// <summary>Optional collectors (USB, audio, ...) run after the main ones and never delay them.</summary>
    bool IsOptional => false;

    Task CollectAsync(CollectorContext ctx, CancellationToken ct);
}

/// <summary>
/// Error isolation and provenance helpers shared by every collector family (hardware report, health monitoring).
/// </summary>
public class CollectionContext(List<Limitation> limitations, CollectorDiagnostics diagnostics)
{
    private readonly object _lock = new();

    public CollectorDiagnostics Diagnostics { get; } = diagnostics;

    public void UsedSource(string source)
    {
        lock (_lock) if (!Diagnostics.Sources.Contains(source)) Diagnostics.Sources.Add(source);
    }

    public void Warn(string code)
    {
        lock (_lock) if (!Diagnostics.Warnings.Contains(code)) Diagnostics.Warnings.Add(code);
    }

    public void Error(string source, Exception e)
    {
        lock (_lock) Diagnostics.Errors.Add($"{source}: {e.GetType().Name}: {e.Message}");
    }

    public void Limit(string section, string code)
    {
        lock (limitations)
            if (!limitations.Any(l => l.Section == section && l.Code == code))
                limitations.Add(new Limitation(section, code));
    }

    /// <summary>Runs a source call; on failure records the error and returns <paramref name="fallback"/>.</summary>
    public async Task<T> TryAsync<T>(string source, Func<Task<T>> fn, T fallback)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var r = await fn().ConfigureAwait(false);
            UsedSource(source);
            return r;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            Error(source, e);
            return fallback;
        }
        finally { Time(source, sw.ElapsedMilliseconds); }
    }

    protected void Time(string source, long ms)
    {
        lock (_lock) Diagnostics.SourceTimingsMs[source] = Diagnostics.SourceTimingsMs.GetValueOrDefault(source) + ms;
    }

    public T Try<T>(string source, Func<T> fn, T fallback)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var r = fn();
            UsedSource(source);
            return r;
        }
        catch (Exception e)
        {
            Error(source, e);
            return fallback;
        }
        finally { Time(source, sw.ElapsedMilliseconds); }
    }
}

/// <summary>Per-collector view of the hardware report and its diagnostics.</summary>
public sealed class CollectorContext(HardwareReport report, CollectorDiagnostics diagnostics)
    : CollectionContext(report.Limitations, diagnostics)
{
    public HardwareReport Report { get; } = report;
}

public sealed record ScanProgress(string CollectorId, string Status, int Completed, int Total);

public sealed class ScanOptions
{
    public int MaxParallelism { get; init; } = 3;
    public TimeSpan CollectorTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public string AgentVersion { get; init; } = "0.0.0";
}

public sealed class ScanOrchestrator(IEnumerable<IHardwareCollector> collectors, ScanOptions options)
{
    private readonly IReadOnlyList<IHardwareCollector> _collectors = collectors.ToList();

    public async Task<HardwareReport> RunAsync(IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var report = new HardwareReport();
        report.Scan.AgentVersion = options.AgentVersion;
        report.Scan.Collector.Version = options.AgentVersion;

        int done = 0;
        int total = _collectors.Count;
        using var gate = new SemaphoreSlim(Math.Max(1, options.MaxParallelism));

        async Task RunOne(IHardwareCollector c)
        {
            var diag = new CollectorDiagnostics { Id = c.Id };
            lock (report.Diagnostics.Collectors) report.Diagnostics.Collectors.Add(diag);
            await gate.WaitAsync(ct).ConfigureAwait(false);
            var csw = Stopwatch.StartNew();
            progress?.Report(new ScanProgress(c.Id, "running", done, total));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.CollectorTimeout);
            try
            {
                // Task.Run: collectors may call blocking native APIs; WaitAsync enforces the timeout even then.
                await Task.Run(() => c.CollectAsync(new CollectorContext(report, diag), timeout.Token), timeout.Token)
                    .WaitAsync(options.CollectorTimeout, ct).ConfigureAwait(false);
                diag.Status = diag.Errors.Count == 0 ? "ok" : "partial";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                diag.Status = "cancelled";
            }
            catch (Exception e) when (e is TimeoutException or OperationCanceledException)
            {
                diag.Status = "timeout";
                diag.Errors.Add($"collector timed out after {options.CollectorTimeout.TotalSeconds:0} s");
            }
            catch (Exception e)
            {
                diag.Status = "failed";
                diag.Errors.Add($"{e.GetType().Name}: {e.Message}");
            }
            finally
            {
                diag.DurationMs = csw.ElapsedMilliseconds;
                gate.Release();
                var n = Interlocked.Increment(ref done);
                progress?.Report(new ScanProgress(c.Id, diag.Status, n, total));
            }
        }

        // Main collectors first; optional ones start only after all main collectors have finished.
        await Task.WhenAll(_collectors.Where(c => !c.IsOptional).Select(RunOne)).ConfigureAwait(false);
        await Task.WhenAll(_collectors.Where(c => c.IsOptional).Select(RunOne)).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        // Stable order for output.
        var order = _collectors.Select(c => c.Id).ToList();
        report.Diagnostics.Collectors.Sort((a, b) => order.IndexOf(a.Id).CompareTo(order.IndexOf(b.Id)));
        report.Diagnostics.DurationMs = sw.ElapsedMilliseconds;
        return report;
    }
}

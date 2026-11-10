using System.Collections.Concurrent;
using System.Diagnostics;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Tests.Fixtures;

namespace DeviceCheck.Agent.Tests;

public class OrchestratorTests
{
    private sealed class Lambda(string id, Func<CollectorContext, CancellationToken, Task> fn, bool optional = false) : IHardwareCollector
    {
        public string Id => id;
        public bool IsOptional => optional;
        public Task CollectAsync(CollectorContext ctx, CancellationToken ct) => fn(ctx, ct);
    }

    private sealed class SyncProgress : IProgress<ScanProgress>
    {
        public ConcurrentQueue<ScanProgress> Items { get; } = new();
        public void Report(ScanProgress value) => Items.Enqueue(value);
    }

    private static readonly Func<CollectorContext, CancellationToken, Task> Ok = (_, _) => Task.CompletedTask;

    [Fact]
    public async Task Failing_collector_does_not_stop_others()
    {
        var r = await new ScanOrchestrator(
        [
            new Lambda("a", Ok),
            new Lambda("b", (_, _) => throw new InvalidOperationException("boom")),
            new Lambda("c", (ctx, _) => { ctx.Report.Cpu.Model = Core.Model.F.Exact<string?>("X", "test"); return Task.CompletedTask; }),
        ], new ScanOptions()).RunAsync();

        Assert.Equal(new[] { "a", "b", "c" }, r.Diagnostics.Collectors.Select(c => c.Id));
        Assert.Equal(new[] { "ok", "failed", "ok" }, r.Diagnostics.Collectors.Select(c => c.Status));
        Assert.Contains("boom", r.Diagnostics.Collectors[1].Errors[0]);
        Assert.Equal("X", r.Cpu.Model.Value);
    }

    [Fact]
    public async Task Source_error_marks_collector_partial()
    {
        var r = await new ScanOrchestrator([new Lambda("a", (ctx, _) => { ctx.Try<int>("src", () => throw new TimeoutException(), 0); return Task.CompletedTask; })],
            new ScanOptions()).RunAsync();
        Assert.Equal("partial", r.Diagnostics.Collectors[0].Status);
    }

    [Fact]
    public async Task Hanging_collector_times_out_even_if_it_blocks()
    {
        var sw = Stopwatch.StartNew();
        var r = await new ScanOrchestrator(
        [
            new Lambda("blocking", (_, _) => { Thread.Sleep(3000); return Task.CompletedTask; }),
            new Lambda("ignores-token", (_, _) => Task.Delay(Timeout.Infinite)),
            new Lambda("fast", Ok),
        ], new ScanOptions { CollectorTimeout = TimeSpan.FromMilliseconds(200) }).RunAsync();

        Assert.True(sw.ElapsedMilliseconds < 2500, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(new[] { "timeout", "timeout", "ok" }, r.Diagnostics.Collectors.Select(c => c.Status));
    }

    [Fact]
    public async Task Cancellation_stops_the_scan()
    {
        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ScanOrchestrator(
            [new Lambda("slow", (_, ct) => Task.Delay(5000, ct))], new ScanOptions()).RunAsync(null, cts.Token));
    }

    [Fact]
    public async Task Parallelism_is_bounded()
    {
        int current = 0, max = 0;
        async Task Work(CollectorContext _, CancellationToken ct)
        {
            var n = Interlocked.Increment(ref current);
            lock (this) max = Math.Max(max, n);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref current);
        }
        await new ScanOrchestrator(Enumerable.Range(0, 8).Select(i => new Lambda($"c{i}", Work)), new ScanOptions { MaxParallelism = 2 }).RunAsync();
        Assert.InRange(max, 1, 2);
    }

    [Fact]
    public async Task Optional_collectors_run_after_main_ones()
    {
        var order = new ConcurrentQueue<string>();
        Func<CollectorContext, CancellationToken, Task> Log(string id, int delay) => async (_, ct) => { await Task.Delay(delay, ct); order.Enqueue(id); };

        await new ScanOrchestrator(
        [
            new Lambda("usb", Log("usb", 0), optional: true),
            new Lambda("cpu", Log("cpu", 100)),
            new Lambda("memory", Log("memory", 50)),
        ], new ScanOptions()).RunAsync();

        Assert.Equal("usb", order.Last());
    }

    [Fact]
    public async Task Progress_is_reported_per_category()
    {
        var p = new SyncProgress();
        await new ScanOrchestrator([new Lambda("a", Ok), new Lambda("b", Ok)], new ScanOptions()).RunAsync(p);
        var done = p.Items.Where(x => x.Status != "running").ToList();
        Assert.Equal(2, done.Count);
        Assert.All(p.Items, x => Assert.Equal(2, x.Total));
        Assert.Equal(2, done.Max(x => x.Completed));
    }

    [Fact]
    public async Task Full_fixture_machine_scan()
    {
        var r = await Machines.ScanAsync(Machines.AmdLaptop());
        Assert.Equal(7, r.Diagnostics.Collectors.Count);
        Assert.All(r.Diagnostics.Collectors, c => Assert.Equal("ok", c.Status));
        Assert.Equal("0.1.0-test", r.Scan.AgentVersion);
        Assert.Equal("agent", r.Scan.Source);
        Assert.Equal(64 * Machines.GiB, r.Memory.TotalBytes.Value);
        Assert.Equal(2, r.Gpu.Count);
        Assert.Equal(3, r.Storage.Count);
        Assert.Equal(2, r.Displays.Count);
    }
}

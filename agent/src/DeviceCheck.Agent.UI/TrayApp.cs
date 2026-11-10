using System.Diagnostics;
using System.Globalization;
using DeviceCheck.Agent.Collectors;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Export;
using DeviceCheck.Agent.LocalApi;
using DeviceCheck.Agent.Windows;

namespace DeviceCheck.Agent.UI;

/// <summary>
/// Tray-only application: no main window. The Device Check site is the UI; the tray menu opens it with a link
/// carrying the local API port and token. Nothing keeps running after Exit (no services, no autostart).
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly AgentSettings _settings;
    private readonly string _version;
    private readonly SynchronizationContext _ui;
    private readonly ScanService _scans;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status, _summary, _open, _rescan, _save, _copy;
    private readonly RegisteredWaitHandle _openWait;
    private LocalApiServer? _api;
    private bool _siteOpenedOnStart, _exiting;

    public TrayApp(AgentSettings settings, string version, EventWaitHandle openRequest)
    {
        _settings = settings;
        _version = version;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        var wmi = new WmiClient(TimeSpan.FromSeconds(10));
        _scans = new ScanService(
            () => CollectorSet.Create(wmi, new SmbiosSource(), new ProcessorSource(), new GraphicsSource(), new DisplaySource(), new PlatformSource()),
            new ScanOptions { AgentVersion = version, CollectorTimeout = TimeSpan.FromSeconds(20), MaxParallelism = 3 });
        _scans.Progress += p => Ui(() => OnProgress(p));
        _scans.Completed += r => Ui(() => OnCompleted(r));

        var title = new ToolStripMenuItem($"Device Check Agent {version}") { Enabled = false };
        _status = new ToolStripMenuItem(Strings.Scanning) { Enabled = false };
        _summary = new ToolStripMenuItem { Enabled = false, Visible = false };
        _open = new ToolStripMenuItem(Strings.Open, null, (_, _) => OpenSite()) { Font = new Font(_menu.Font, FontStyle.Bold) };
        _rescan = new ToolStripMenuItem(Strings.Rescan, null, (_, _) => Scan());
        _save = new ToolStripMenuItem(Strings.SaveJson, null, async (_, _) => await SaveJsonAsync()) { Enabled = false };
        _copy = new ToolStripMenuItem(Strings.CopyJson, null, (_, _) => CopyJson()) { Enabled = false };
        var exit = new ToolStripMenuItem(Strings.Exit, null, async (_, _) => await ExitAsync());
        _menu.Items.AddRange([title, _status, _summary, new ToolStripSeparator(), _open, _rescan, new ToolStripSeparator(), _save, _copy, new ToolStripSeparator(), exit]);

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Device Check Agent",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenSite(); };

        // A second launch of the EXE signals this event: open the site instead of starting another agent.
        _openWait = ThreadPool.RegisterWaitForSingleObject(openRequest, (_, _) => Ui(OpenSite), null, Timeout.Infinite, false);

        foreach (var w in settings.Warnings) Notify(w, ToolTipIcon.Warning);
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            _api = await LocalApiServer.StartAsync(_scans, new LocalApiOptions
            {
                PreferredPort = _settings.Port,
                AllowedOrigins = _settings.AllowedOrigins,
                AgentVersion = _version,
            });
        }
        catch (Exception e)
        {
            Notify(Strings.ApiFailed(e.Message), ToolTipIcon.Error);
        }

        // The site waits for the running scan through the API, so it can be opened right away.
        if (_settings.OpenSiteOnStart && _api is not null)
        {
            _siteOpenedOnStart = true;
            OpenSite();
        }
        else Notify(Strings.RunningInTray);
        Scan();
    }

    private void Scan()
    {
        if (_exiting) return;
        _status.Text = Strings.Scanning;
        _rescan.Enabled = false;
        _scans.ScanAsync().ContinueWith(t =>
        {
            if (t.IsFaulted) Ui(() => { _status.Text = Strings.ScanFailed; _rescan.Enabled = true; });
        }, TaskScheduler.Default);
    }

    private void OnProgress(ScanProgress p)
    {
        if (p.Status != "running") _status.Text = Strings.ScanningStep(p.Completed, p.Total);
        _icon.Text = Clip($"Device Check Agent — {_status.Text}");
    }

    private void OnCompleted(HardwareReport r)
    {
        int ok = r.Diagnostics.Collectors.Count(c => c.Status == "ok");
        _status.Text = Strings.ReportAt(r.Scan.CreatedAt, ok, r.Diagnostics.Collectors.Count);
        _summary.Text = Summary(r);
        _summary.Visible = _summary.Text.Length > 0;
        _rescan.Enabled = _save.Enabled = _copy.Enabled = true;
        _icon.Text = Clip($"Device Check Agent — {_status.Text}");
        if (!_siteOpenedOnStart && _api is null) Notify(_status.Text);
    }

    private void OpenSite()
    {
        if (_api is null || _exiting) return;
        try
        {
            Process.Start(new ProcessStartInfo(_settings.SiteLink(_api.Port, _api.Token)) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e)
        {
            Notify(e.Message, ToolTipIcon.Error);
        }
    }

    private async Task SaveJsonAsync()
    {
        var r = _scans.Latest;
        if (r is null) return;
        using var dlg = new SaveFileDialog
        {
            FileName = ReportJson.FileName(r),
            Filter = Strings.SaveFilter,
            DefaultExt = "json",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            var path = await ReportJson.WriteAsync(r, dlg.FileName);
            Notify(Strings.Saved(path));
        }
        catch (Exception e)
        {
            Notify(Strings.SaveFailed(e.Message), ToolTipIcon.Error);
        }
    }

    private void CopyJson()
    {
        var r = _scans.Latest;
        if (r is null) return;
        Clipboard.SetText(ReportJson.Serialize(r));
        Notify(Strings.Copied);
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _icon.Visible = false;
        if (_api is not null) await _api.DisposeAsync(); // closes the port before the process ends
        ExitThread();
    }

    internal static string Summary(HardwareReport r)
    {
        var parts = new List<string>();
        if (r.Cpu.Model.HasValue) parts.Add(r.Cpu.Model.Value!);
        if (r.Memory.TotalBytes.Value is long b)
        {
            var mem = $"{b / (double)(1L << 30):0.#} GiB".Replace(',', '.');
            if (r.Memory.MemoryType.HasValue) mem += $" {r.Memory.MemoryType.Value}";
            if (r.Memory.SpeedMts.Value is int s) mem += $"-{s.ToString(CultureInfo.InvariantCulture)}";
            parts.Add(mem);
        }
        parts.AddRange(r.Gpu.Where(g => g.Model.HasValue).Select(g => g.Model.Value!));
        return string.Join(" · ", parts);
    }

    private void Notify(string text, ToolTipIcon kind = ToolTipIcon.Info) =>
        _icon.ShowBalloonTip(4000, "Device Check Agent", text, kind);

    private void Ui(Action a) => _ui.Post(_ => a(), null);

    // NotifyIcon.Text is limited to 127 characters.
    private static string Clip(string s) => s.Length <= 127 ? s : s[..126] + "…";

    private static Icon LoadIcon()
    {
        using var s = typeof(TrayApp).Assembly.GetManifestResourceStream("agent.ico");
        return s is null ? SystemIcons.Application : new Icon(s, SystemInformation.SmallIconSize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _openWait.Unregister(null);
            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            if (_api is not null) _api.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        base.Dispose(disposing);
    }
}

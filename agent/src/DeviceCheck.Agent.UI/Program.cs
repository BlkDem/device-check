using System.Reflection;

namespace DeviceCheck.Agent.UI;

internal static class Program
{
    // Per-user session names: a second launch hands over to the running instance.
    private const string MutexName = @"Local\DeviceCheck.Agent";
    private const string OpenEventName = @"Local\DeviceCheck.Agent.Open";

    [STAThread]
    private static int Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: ask it to open the site, then quit.
            if (EventWaitHandle.TryOpenExisting(OpenEventName, out var ev)) using (ev) ev.Set();
            return 0;
        }

        using var openRequest = new EventWaitHandle(false, EventResetMode.AutoReset, OpenEventName);

        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var settings = AgentSettings.Load(Path.Combine(AppContext.BaseDirectory, AgentSettings.FileName), args);
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

        using var tray = new TrayApp(settings, version, openRequest);
        Application.Run(tray);
        GC.KeepAlive(mutex);
        return 0;
    }
}

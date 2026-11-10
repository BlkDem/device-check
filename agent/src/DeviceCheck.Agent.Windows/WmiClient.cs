using System.Management;
using System.Runtime.Versioning;
using DeviceCheck.Agent.Core.Sources;

namespace DeviceCheck.Agent.Windows;

/// <summary>
/// WMI/CIM client with hard timeouts: a hung WMI service never blocks the scan.
/// Connections are cached per namespace for the lifetime of the client (one scan run).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiClient(TimeSpan timeout) : IWmiClient
{
    private readonly Dictionary<string, ManagementScope> _scopes = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<WmiObject>> QueryAsync(string wmiNamespace, string wql, CancellationToken ct)
    {
        var work = Task.Run(() => Execute(wmiNamespace, wql), ct);
        try
        {
            return await work.WaitAsync(timeout + TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"WMI query timed out after {timeout.TotalSeconds:0} s: {wql}");
        }
    }

    private IReadOnlyList<WmiObject> Execute(string ns, string wql)
    {
        try
        {
            ManagementScope scope;
            lock (_scopes)
            {
                if (!_scopes.TryGetValue(ns, out scope!))
                {
                    scope = new ManagementScope(ns, new ConnectionOptions { Timeout = timeout, EnablePrivileges = false });
                    _scopes[ns] = scope;
                }
                if (!scope.IsConnected) scope.Connect();
            }

            var options = new System.Management.EnumerationOptions { Timeout = timeout, ReturnImmediately = true, Rewindable = false, DirectRead = false };
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql), options);
            using var results = searcher.Get();
            var list = new List<WmiObject>();
            foreach (var mo in results)
            {
                using (mo)
                {
                    var o = new WmiObject();
                    foreach (var p in mo.Properties) o[p.Name] = p.Value;
                    list.Add(o);
                }
            }
            return list;
        }
        catch (ManagementException e)
        {
            throw new WmiException(wql, $"{e.ErrorCode}: {e.Message.Trim()}", e);
        }
        catch (System.Runtime.InteropServices.COMException e)
        {
            throw new WmiException(wql, $"0x{e.HResult:X8}: {e.Message.Trim()}", e);
        }
        catch (UnauthorizedAccessException e)
        {
            throw new WmiException(wql, "access denied", e);
        }
    }
}

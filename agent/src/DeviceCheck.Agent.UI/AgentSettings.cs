using System.Text.Json;
using DeviceCheck.Agent.Export;

namespace DeviceCheck.Agent.UI;

/// <summary>
/// Optional settings: DeviceCheck.Agent.json next to the EXE (portable, never written by the app) and command line.
/// <code>{ "site_url": "https://…", "allowed_origins": ["https://…"], "port": 47615, "open_site_on_start": true }</code>
/// </summary>
internal sealed class AgentSettings
{
    public const string FileName = "DeviceCheck.Agent.json";
    public const string DefaultSite = "http://localhost:3000";

    public string SiteUrl { get; set; } = DefaultSite;
    public List<string> AllowedOrigins { get; set; } = [];
    public int Port { get; set; } = 47615;
    public bool OpenSiteOnStart { get; set; } = true;

    /// <summary>Problems found while loading (shown in the tray, never fatal).</summary>
    public List<string> Warnings { get; } = [];

    public static AgentSettings Load(string? path, IReadOnlyList<string> args)
    {
        var s = new AgentSettings();
        if (path is not null && File.Exists(path))
        {
            try
            {
                var file = JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(path), ReportJson.Options);
                if (file is not null) s = file;
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                s.Warnings.Add($"{FileName}: {e.Message}");
            }
        }

        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--site" when i + 1 < args.Count: s.SiteUrl = args[++i]; break;
                case "--port" when i + 1 < args.Count && int.TryParse(args[i + 1], out var p): s.Port = p; i++; break;
                case "--no-open": s.OpenSiteOnStart = false; break;
            }
        }

        if (!Uri.TryCreate(s.SiteUrl, UriKind.Absolute, out var site) || site.Scheme is not ("http" or "https"))
        {
            s.Warnings.Add($"site_url is not an http(s) URL: {s.SiteUrl}");
            s.SiteUrl = DefaultSite;
            site = new Uri(DefaultSite);
        }
        if (s.Port is < 0 or > 65535) s.Port = 47615;

        // The site itself is always allowed; for a local site both host spellings are.
        var origins = new HashSet<string>(s.AllowedOrigins.Select(o => o.TrimEnd('/')), StringComparer.Ordinal) { Origin(site) };
        if (site.IsLoopback)
        {
            origins.Add(Origin(new UriBuilder(site) { Host = "localhost" }.Uri));
            origins.Add(Origin(new UriBuilder(site) { Host = "127.0.0.1" }.Uri));
        }
        s.AllowedOrigins = origins.Order(StringComparer.Ordinal).ToList();
        return s;
    }

    /// <summary>Site link that hands the agent address and token to the page. The fragment is never sent to any server.</summary>
    public string SiteLink(int port, string token) =>
        new UriBuilder(SiteUrl) { Fragment = $"agent={port}&token={token}" }.Uri.AbsoluteUri;

    internal static string Origin(Uri u) => u.GetLeftPart(UriPartial.Authority);
}

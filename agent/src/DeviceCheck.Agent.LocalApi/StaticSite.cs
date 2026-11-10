using System.IO.Compression;

namespace DeviceCheck.Agent.LocalApi;

public sealed record StaticFile(string Path, byte[] Content, string ContentType);

/// <summary>Files of the dashboard served by the agent itself (no file system access: lookups only).</summary>
public interface IStaticSite
{
    StaticFile? Get(string path);
}

/// <summary>
/// The site's static build (nuxt generate) loaded from a ZIP into memory. URL paths map like nginx with
/// <c>index index.html</c>: "/" → "/index.html", "/about" and "/about/" → "/about/index.html".
/// </summary>
public sealed class ZipStaticSite : IStaticSite
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public ZipStaticSite(Stream zip)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        foreach (var e in archive.Entries)
        {
            if (e.FullName.EndsWith('/')) continue; // directory entry
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            _files["/" + e.FullName.Replace('\\', '/').TrimStart('/')] = ms.ToArray();
        }
    }

    public int Count => _files.Count;

    public StaticFile? Get(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains("..", StringComparison.Ordinal)) return null;
        foreach (var candidate in new[] { path, path.TrimEnd('/') + "/index.html" })
            if (_files.TryGetValue(candidate, out var bytes)) return new StaticFile(candidate, bytes, ContentType(candidate));
        return null;
    }

    internal static string ContentType(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".txt" => "text/plain; charset=utf-8",
        ".woff2" => "font/woff2",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };
}

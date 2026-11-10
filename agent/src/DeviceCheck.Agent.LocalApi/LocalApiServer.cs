using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Core.Model;
using DeviceCheck.Agent.Export;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DeviceCheck.Agent.LocalApi;

public sealed class LocalApiOptions
{
    /// <summary>Fixed port so the site can find the agent; a random free port is used if it is busy.</summary>
    public int PreferredPort { get; init; } = 47615;

    /// <summary>Exact origins allowed to call the API from a browser, e.g. "https://devicecheck.example".</summary>
    public IReadOnlyCollection<string> AllowedOrigins { get; init; } = [];

    public string AgentVersion { get; init; } = "0.0.0";

    /// <summary>The API takes no request bodies; anything larger than this is rejected.</summary>
    public long MaxRequestBodyBytes { get; init; } = 1024;

    /// <summary>Storage health provider (StorageHealthReport JSON). Null = endpoint disabled.</summary>
    public Func<CancellationToken, Task<string>>? StorageHealth { get; init; }
}

/// <summary>
/// Optional localhost API for the Device Check site.
/// Security model: loopback-only socket, Host header check (DNS rebinding), exact-match Origin allow-list with
/// restricted CORS, a random per-run bearer token for everything except status/version, fixed set of read-only
/// endpoints (no commands, no file system access), tiny request size limits.
/// </summary>
public sealed class LocalApiServer : IAsyncDisposable
{
    public const string ApiVersion = "1";

    private readonly ScanService _scans;
    private readonly LocalApiOptions _options;
    private readonly HashSet<string> _origins;
    private readonly byte[] _token;
    private readonly CancellationTokenSource _lifetime = new();
    private WebApplication? _app;

    /// <summary>Random per-run token (base64url, 256 bit). Handed to the site only via the tray menu link.</summary>
    public string Token { get; }
    public int Port { get; private set; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    private LocalApiServer(ScanService scans, LocalApiOptions options)
    {
        _scans = scans;
        _options = options;
        _origins = new HashSet<string>(options.AllowedOrigins.Select(o => o.TrimEnd('/')), StringComparer.Ordinal);
        Token = Base64Url(RandomNumberGenerator.GetBytes(32));
        _token = Encoding.ASCII.GetBytes(Token);
    }

    public static async Task<LocalApiServer> StartAsync(ScanService scans, LocalApiOptions options, CancellationToken ct = default)
    {
        var server = new LocalApiServer(scans, options);
        try
        {
            await server.StartOnAsync(options.PreferredPort, ct).ConfigureAwait(false);
        }
        catch (IOException) when (options.PreferredPort != 0)
        {
            await server.StartOnAsync(0, ct).ConfigureAwait(false); // preferred port is busy
        }
        return server;
    }

    private async Task StartOnAsync(int port, CancellationToken ct)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseKestrelCore().ConfigureKestrel(k =>
        {
            k.Listen(IPAddress.Loopback, port);
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = _options.MaxRequestBodyBytes;
            k.Limits.MaxRequestHeadersTotalSize = 16 * 1024;
            k.Limits.MaxRequestLineSize = 2048;
            k.Limits.MaxConcurrentConnections = 32;
            k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        });
        var app = builder.Build();
        app.Run(HandleAsync);
        try
        {
            await app.StartAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Port = new Uri(address).Port;
        _app = app;
    }

    private async Task HandleAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.Headers.CacheControl = "no-store";
        res.Headers.XContentTypeOptions = "nosniff";

        // Defence in depth: the socket is loopback-only anyway.
        if (ctx.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
        {
            await Error(res, 403, "loopback_only");
            return;
        }
        // DNS rebinding: a foreign host name resolving to 127.0.0.1 must not reach the API.
        if (req.Host.Host is not ("127.0.0.1" or "localhost") || req.Host.Port != Port)
        {
            await Error(res, 403, "bad_host");
            return;
        }

        var origin = req.Headers.Origin.ToString();
        if (origin.Length > 0)
        {
            if (!_origins.Contains(origin))
            {
                await Error(res, 403, "origin_not_allowed");
                return;
            }
            res.Headers.AccessControlAllowOrigin = origin;
            res.Headers.Vary = "Origin";
        }

        if (HttpMethods.IsOptions(req.Method))
        {
            if (origin.Length == 0) { res.StatusCode = 400; return; }
            res.Headers.AccessControlAllowMethods = "GET, POST";
            res.Headers.AccessControlAllowHeaders = "Authorization";
            res.Headers.AccessControlMaxAge = "600";
            // Chrome Private Network Access: a public site calling 127.0.0.1 asks for this explicitly.
            if (req.Headers["Access-Control-Request-Private-Network"] == "true")
                res.Headers["Access-Control-Allow-Private-Network"] = "true";
            res.StatusCode = 204;
            return;
        }

        if (req.ContentLength > _options.MaxRequestBodyBytes)
        {
            await Error(res, 413, "request_too_large");
            return;
        }

        var path = (req.Path.Value ?? "").TrimEnd('/');
        bool get = HttpMethods.IsGet(req.Method), post = HttpMethods.IsPost(req.Method);
        switch (path)
        {
            case "/api/status" when get:
                await Json(res, Status());
                return;
            case "/api/version" when get:
                await Json(res, new { _options.AgentVersion, SchemaVersion = SchemaInfo.Version, ApiVersion });
                return;
            case "/api/status" or "/api/version" or "/api/scan" or "/api/report" or "/api/storage":
            case var p when p.StartsWith("/api/report/", StringComparison.Ordinal):
                break;
            default:
                await Error(res, 404, "not_found");
                return;
        }

        if (!Authorized(req))
        {
            await Error(res, 401, "unauthorized");
            return;
        }

        if (path == "/api/scan" && post)
        {
            var r = await _scans.ScanAsync(_lifetime.Token);
            await Json(res, new { r.Scan.Id, r.Scan.CreatedAt });
        }
        else if (path == "/api/report" && get)
        {
            await Report(res, _scans.Latest);
        }
        else if (path == "/api/storage" && get)
        {
            if (_options.StorageHealth is null) { await Error(res, 404, "not_available"); return; }
            string json;
            try { json = await _options.StorageHealth(_lifetime.Token); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await Error(res, 500, "storage_failed");
                return;
            }
            res.ContentType = "application/json; charset=utf-8";
            await res.WriteAsync(json, Encoding.UTF8);
        }
        else if (path.StartsWith("/api/report/", StringComparison.Ordinal) && get)
        {
            var id = path["/api/report/".Length..];
            if (!Guid.TryParse(id, out _)) await Error(res, 400, "bad_id");
            else await Report(res, _scans.Get(id));
        }
        else
        {
            res.Headers.Allow = path == "/api/scan" ? "POST" : "GET";
            await Error(res, 405, "method_not_allowed");
        }
    }

    private object Status()
    {
        var last = _scans.Latest;
        return new
        {
            Status = _scans.IsScanning ? "scanning" : last is null ? "idle" : "ready",
            Agent = "device-check-agent-windows",
            _options.AgentVersion,
            SchemaVersion = SchemaInfo.Version,
            LastScan = last is null ? null : new { last.Scan.Id, last.Scan.CreatedAt },
            Features = _options.StorageHealth is null ? new[] { "hardware" } : new[] { "hardware", "storage_health" },
        };
    }

    private bool Authorized(HttpRequest req)
    {
        var h = req.Headers.Authorization.ToString();
        if (!h.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h[7..].Trim()), _token);
    }

    private static Task Report(HttpResponse res, HardwareReport? r)
    {
        if (r is null) return Error(res, 404, "no_report");
        res.ContentType = "application/json; charset=utf-8";
        return res.WriteAsync(ReportJson.Serialize(r, indented: false), Encoding.UTF8);
    }

    private static Task Json(HttpResponse res, object body, int status = 200)
    {
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        return res.WriteAsync(JsonSerializer.Serialize(body, ReportJson.CompactOptions), Encoding.UTF8);
    }

    private static Task Error(HttpResponse res, int status, string code) => Json(res, new { Error = code }, status);

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _lifetime.Cancel();
        if (_app is not null)
        {
            using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                await _app.StopAsync(stop.Token).ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }
        _lifetime.Dispose();
    }
}

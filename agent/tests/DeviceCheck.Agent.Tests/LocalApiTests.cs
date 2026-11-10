using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.Export;
using DeviceCheck.Agent.LocalApi;
using DeviceCheck.Agent.Tests.Fixtures;

namespace DeviceCheck.Agent.Tests;

public sealed class LocalApiTests : IAsyncLifetime
{
    private const string Site = "http://localhost:3000";
    private ScanService _scans = null!;
    private LocalApiServer _api = null!;
    private HttpClient _http = null!;
    private int StorageCalls;

    public async Task InitializeAsync()
    {
        // The delay makes the scan long enough for concurrent requests to overlap.
        _scans = new ScanService(() => [.. Machines.AmdLaptop(), new DelayCollector(TimeSpan.FromMilliseconds(300))], new ScanOptions { AgentVersion = "0.1.0-test" });
        _api = await LocalApiServer.StartAsync(_scans, new LocalApiOptions
        {
            PreferredPort = 0, AllowedOrigins = [Site], AgentVersion = "0.1.0-test",
            StorageHealth = async _ => { StorageCalls++; await Task.Yield(); return """{"kind":"storage_health"}"""; },
        });
        _http = new HttpClient { BaseAddress = new Uri(_api.BaseUrl) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _api.DisposeAsync();
    }

    private HttpRequestMessage Req(HttpMethod m, string path, bool auth = true, string? origin = null)
    {
        var r = new HttpRequestMessage(m, path);
        if (auth) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _api.Token);
        if (origin is not null) r.Headers.Add("Origin", origin);
        return r;
    }

    private static async Task<JsonNode> Body(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!;

    [Fact]
    public async Task Status_and_version_need_no_token()
    {
        var s = await _http.SendAsync(Req(HttpMethod.Get, "/api/status", auth: false));
        Assert.Equal(HttpStatusCode.OK, s.StatusCode);
        var j = await Body(s);
        Assert.Equal("idle", (string?)j["status"]);
        Assert.Equal("0.1.0-test", (string?)j["agent_version"]);
        Assert.Equal("1.0", (string?)j["schema_version"]);
        Assert.Null(j["last_scan"]);
        Assert.Equal("no-store", s.Headers.CacheControl?.ToString());

        var v = await Body(await _http.SendAsync(Req(HttpMethod.Get, "/api/version", auth: false)));
        Assert.Equal("1", (string?)v["api_version"]);
    }

    [Theory]
    [InlineData("GET", "/api/report")]
    [InlineData("POST", "/api/scan")]
    [InlineData("GET", "/api/report/9cc9b95b-59f5-4101-9e96-1ce68901b72b")]
    public async Task Report_endpoints_require_token(string method, string path)
    {
        var none = await _http.SendAsync(Req(new HttpMethod(method), path, auth: false));
        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);

        var wrong = Req(new HttpMethod(method), path, auth: false);
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _api.Token[..^1] + "x");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(wrong)).StatusCode);
        Assert.False(_scans.IsScanning);
        Assert.Null(_scans.Latest);
    }

    [Fact]
    public async Task Scan_then_fetch_report()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(Req(HttpMethod.Get, "/api/report"))).StatusCode);

        var scan = await _http.SendAsync(Req(HttpMethod.Post, "/api/scan"));
        Assert.Equal(HttpStatusCode.OK, scan.StatusCode);
        var id = (string?)(await Body(scan))["id"];
        Assert.True(Guid.TryParse(id, out _));

        var latest = await _http.SendAsync(Req(HttpMethod.Get, "/api/report"));
        Assert.Equal("application/json; charset=utf-8", latest.Content.Headers.ContentType?.ToString());
        var json = await latest.Content.ReadAsStringAsync();
        Assert.Empty(ReportValidator.Validate(json));
        Assert.Equal(id, (string?)JsonNode.Parse(json)!["scan"]!["id"]);

        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(Req(HttpMethod.Get, $"/api/report/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(Req(HttpMethod.Get, $"/api/report/{Guid.NewGuid()}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _http.SendAsync(Req(HttpMethod.Get, "/api/report/..%2F..%2Fetc"))).StatusCode);

        var status = await Body(await _http.SendAsync(Req(HttpMethod.Get, "/api/status", auth: false)));
        Assert.Equal("ready", (string?)status["status"]);
        Assert.Equal(id, (string?)status["last_scan"]!["id"]);
    }

    [Fact]
    public async Task Concurrent_scan_requests_share_one_scan()
    {
        var a = _http.SendAsync(Req(HttpMethod.Post, "/api/scan"));
        var b = _http.SendAsync(Req(HttpMethod.Post, "/api/scan"));
        var ids = await Task.WhenAll((await Task.WhenAll(a, b)).Select(async r => (string?)(await Body(r))["id"]));
        Assert.Equal(ids[0], ids[1]);
    }

    [Fact]
    public async Task Storage_health_endpoint()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(Req(HttpMethod.Get, "/api/storage", auth: false))).StatusCode);
        Assert.Equal(0, StorageCalls);
        var r = await _http.SendAsync(Req(HttpMethod.Get, "/api/storage", origin: Site));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("storage_health", (string?)(await Body(r))["kind"]);
        Assert.Equal(Site, r.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var status = await Body(await _http.SendAsync(Req(HttpMethod.Get, "/api/status", auth: false)));
        Assert.Contains("storage_health", status["features"]!.AsArray().Select(x => (string?)x));
    }

    [Fact]
    public async Task Storage_endpoint_is_absent_without_provider()
    {
        await using var bare = await LocalApiServer.StartAsync(_scans, new LocalApiOptions { PreferredPort = 0 });
        using var http = new HttpClient { BaseAddress = new Uri(bare.BaseUrl) };
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/storage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bare.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Allowed_origin_gets_cors_headers()
    {
        var r = await _http.SendAsync(Req(HttpMethod.Get, "/api/status", auth: false, origin: Site));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(Site, r.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:3001")]
    [InlineData("http://localhost:3000.evil.example")]
    [InlineData("null")]
    public async Task Foreign_origin_is_rejected(string origin)
    {
        var r = await _http.SendAsync(Req(HttpMethod.Get, "/api/report", origin: origin));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_allows_only_get_post_and_authorization()
    {
        var req = Req(HttpMethod.Options, "/api/scan", auth: false, origin: Site);
        req.Headers.Add("Access-Control-Request-Method", "POST");
        req.Headers.Add("Access-Control-Request-Headers", "authorization");
        req.Headers.Add("Access-Control-Request-Private-Network", "true");
        var r = await _http.SendAsync(req);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal("GET, POST", r.Headers.GetValues("Access-Control-Allow-Methods").Single());
        Assert.Equal("Authorization", r.Headers.GetValues("Access-Control-Allow-Headers").Single());
        Assert.Equal("true", r.Headers.GetValues("Access-Control-Allow-Private-Network").Single());
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("127.0.0.1.nip.io")]
    public async Task Dns_rebinding_host_is_rejected(string host)
    {
        var req = Req(HttpMethod.Get, "/api/status", auth: false);
        req.Headers.Host = $"{host}:{_api.Port}";
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Request_body_is_limited()
    {
        var req = Req(HttpMethod.Post, "/api/scan");
        req.Content = new ByteArrayContent(new byte[4 * 1024]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await _http.SendAsync(req)).StatusCode);
        Assert.Null(_scans.Latest);
    }

    [Theory]
    [InlineData("GET", "/", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/exec", HttpStatusCode.NotFound)]
    [InlineData("GET", "/../../windows/win.ini", HttpStatusCode.NotFound)]
    [InlineData("GET", "/api/scan", HttpStatusCode.MethodNotAllowed)]
    [InlineData("DELETE", "/api/report", HttpStatusCode.MethodNotAllowed)]
    public async Task Only_fixed_endpoints_exist(string method, string path, HttpStatusCode expected) =>
        Assert.Equal(expected, (await _http.SendAsync(Req(new HttpMethod(method), path))).StatusCode);

    [Fact]
    public void Token_is_random_and_long()
    {
        Assert.True(_api.Token.Length >= 43);
        Assert.Matches("^[A-Za-z0-9_-]+$", _api.Token);
    }

    [Fact]
    public async Task Busy_port_falls_back_and_dispose_releases_it()
    {
        await using var second = await LocalApiServer.StartAsync(_scans, new LocalApiOptions { PreferredPort = _api.Port, AllowedOrigins = [Site] });
        Assert.NotEqual(_api.Port, second.Port);
        Assert.NotEqual(_api.Token, second.Token);

        var port = second.Port;
        await second.DisposeAsync();
        using var tcp = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => tcp.ConnectAsync(IPAddress.Loopback, port));
    }

    [Fact]
    public void Listens_on_loopback_only()
    {
        // Not reachable through any non-loopback address of this machine.
        var lan = Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        foreach (var ip in lan)
        {
            using var tcp = new TcpClient();
            Assert.ThrowsAny<SocketException>(() => tcp.Connect(ip, _api.Port));
        }
    }
}

public class ScanServiceTests
{
    [Fact]
    public async Task Keeps_only_recent_reports_in_memory()
    {
        var s = new ScanService(() => [], new ScanOptions(), keep: 2);
        var r1 = await s.ScanAsync();
        var r2 = await s.ScanAsync();
        var r3 = await s.ScanAsync();
        Assert.Same(r3, s.Latest);
        Assert.NotNull(s.Get(r2.Scan.Id));
        Assert.Null(s.Get(r1.Scan.Id));
    }

    [Fact]
    public async Task Raises_progress_and_completed()
    {
        var s = new ScanService(() => Machines.AmdLaptop(), new ScanOptions());
        int progress = 0;
        Core.Model.HardwareReport? done = null;
        s.Progress += _ => Interlocked.Increment(ref progress);
        s.Completed += r => done = r;
        var r = await s.ScanAsync();
        Assert.Same(r, done);
        Assert.True(progress >= 14); // running + finished for each of 7 collectors
        Assert.False(s.IsScanning);
    }
}

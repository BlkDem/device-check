using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using DeviceCheck.Agent.Core.Collection;
using DeviceCheck.Agent.LocalApi;
using DeviceCheck.Agent.Tests.Fixtures;
using DeviceCheck.Agent.UI;

namespace DeviceCheck.Agent.Tests;

public sealed class LocalDashboardTests : IAsyncLifetime
{
    private LocalApiServer _api = null!;
    private HttpClient _http = null!;

    public static ZipStaticSite Site()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                w.Write(content);
            }
            Add("index.html", "<html>home</html>");
            Add("about/index.html", "<html>about</html>");
            Add("404.html", "<html>not found</html>");
            Add("_nuxt/app.abc123.js", "console.log(1)");
            Add("favicon.svg", "<svg/>");
        }
        ms.Position = 0;
        return new ZipStaticSite(ms);
    }

    public async Task InitializeAsync()
    {
        var scans = new ScanService(() => Machines.AmdLaptop(), new ScanOptions());
        _api = await LocalApiServer.StartAsync(scans, new LocalApiOptions { PreferredPort = 0, Site = Site() });
        _http = new HttpClient { BaseAddress = new Uri(_api.BaseUrl) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _api.DisposeAsync();
    }

    [Theory]
    [InlineData("/", "home", "text/html; charset=utf-8")]
    [InlineData("/about", "about", "text/html; charset=utf-8")]
    [InlineData("/about/", "about", "text/html; charset=utf-8")]
    [InlineData("/_nuxt/app.abc123.js", "console.log(1)", "text/javascript; charset=utf-8")]
    [InlineData("/favicon.svg", "<svg/>", "image/svg+xml")]
    public async Task Serves_the_dashboard_without_token(string path, string body, string type)
    {
        var r = await _http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(body, await r.Content.ReadAsStringAsync());
        Assert.Equal(type, r.Content.Headers.ContentType?.ToString());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Build_assets_are_cached_pages_are_not()
    {
        Assert.Contains("immutable", (await _http.GetAsync("/_nuxt/app.abc123.js")).Headers.CacheControl!.ToString());
        Assert.Equal("no-cache", (await _http.GetAsync("/")).Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("/missing")]
    [InlineData("/..%2F..%2Fwindows/win.ini")]
    [InlineData("/%2e%2e/%2e%2e/windows/win.ini")]
    [InlineData("/C:/Windows/win.ini")]
    public async Task Unknown_paths_get_the_404_page(string path)
    {
        var r = await _http.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.DoesNotContain("[fonts]", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Same_origin_page_can_call_the_api()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/scan");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _api.Token);
        req.Headers.Add("Origin", _api.BaseUrl); // what Chrome sends for a same-origin POST
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Api_still_requires_the_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync("/api/report")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _http.PostAsync("/", null)).StatusCode);
    }

    [Fact]
    public async Task Without_site_root_is_not_found()
    {
        await using var bare = await LocalApiServer.StartAsync(new ScanService(() => [], new ScanOptions()), new LocalApiOptions { PreferredPort = 0 });
        using var http = new HttpClient { BaseAddress = new Uri(bare.BaseUrl) };
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/")).StatusCode);
    }

    [Fact]
    public void Dashboard_mode_from_settings()
    {
        Assert.Equal("local", AgentSettings.Load(null, []).Dashboard);
        Assert.Equal("site", AgentSettings.Load(null, ["--site", "http://localhost:3000"]).Dashboard);
        Assert.Equal("http://127.0.0.1:47615/#agent=47615&token=abc", AgentSettings.LocalLink("http://127.0.0.1:47615", 47615, "abc"));
    }
}

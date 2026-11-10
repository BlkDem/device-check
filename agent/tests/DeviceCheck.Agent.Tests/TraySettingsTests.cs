using DeviceCheck.Agent.UI;
using DeviceCheck.Agent.Tests.Fixtures;

namespace DeviceCheck.Agent.Tests;

public class TraySettingsTests
{
    [Fact]
    public void Defaults_point_to_the_production_site_over_http_and_https()
    {
        var s = AgentSettings.Load(null, []);
        Assert.Equal("https://device-check.umolab.ru", s.SiteUrl);
        Assert.Equal(47615, s.Port);
        Assert.True(s.OpenSiteOnStart);
        Assert.Equal(new[] { "http://device-check.umolab.ru", "https://device-check.umolab.ru" }, s.AllowedOrigins);
        Assert.Empty(s.Warnings);
    }

    [Fact]
    public void Local_site_is_allowed_under_both_host_names()
    {
        var s = AgentSettings.Load(null, ["--site", "http://localhost:3000"]);
        Assert.Equal(new[] { "http://127.0.0.1:3000", "http://localhost:3000" }, s.AllowedOrigins);
    }

    [Fact]
    public void File_and_command_line()
    {
        var dir = Directory.CreateTempSubdirectory("dc-agent-settings-");
        try
        {
            var path = Path.Combine(dir.FullName, AgentSettings.FileName);
            File.WriteAllText(path, """{ "site_url": "https://check.example/app/", "allowed_origins": ["https://staging.check.example/"], "open_site_on_start": false }""");

            var s = AgentSettings.Load(path, ["--port", "50000"]);
            Assert.Equal("https://check.example/app/", s.SiteUrl);
            Assert.Equal(50000, s.Port);
            Assert.False(s.OpenSiteOnStart);
            Assert.Equal(new[] { "http://check.example", "https://check.example", "https://staging.check.example" }, s.AllowedOrigins);

            Assert.Equal("http://localhost:4000", AgentSettings.Load(path, ["--site", "http://localhost:4000"]).SiteUrl);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Broken_settings_fall_back_with_warning()
    {
        var dir = Directory.CreateTempSubdirectory("dc-agent-settings-");
        try
        {
            var path = Path.Combine(dir.FullName, AgentSettings.FileName);
            File.WriteAllText(path, "{ not json");
            Assert.Single(AgentSettings.Load(path, []).Warnings);

            var s = AgentSettings.Load(null, ["--site", "file:///C:/Windows"]);
            Assert.Equal(AgentSettings.DefaultSite, s.SiteUrl);
            Assert.Single(s.Warnings);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void Site_link_carries_port_and_token_in_fragment()
    {
        var s = AgentSettings.Load(null, ["--site", "https://check.example/app"]);
        var link = new Uri(s.SiteLink(47615, "abc_DEF-123"));
        Assert.Equal("/app", link.AbsolutePath);
        Assert.Equal("", link.Query); // the token never goes to the server
        Assert.Equal("#agent=47615&token=abc_DEF-123", link.Fragment);
    }

    [Fact]
    public async Task Tray_summary()
    {
        var r = await Machines.ScanAsync(Machines.AmdLaptop());
        Assert.Equal("AMD Ryzen 7 7840HS w/ Radeon 780M Graphics · 64 GiB DDR5-5600 · AMD Radeon 780M Graphics · NVIDIA GeForce RTX 4060 Laptop GPU",
            TrayApp.Summary(r));
    }
}

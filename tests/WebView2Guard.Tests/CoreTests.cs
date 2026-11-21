// WebView2 Guard — Copyright (C) 2026 Ovidiu (movidiu64). Licensed under the GNU GPL v3 (GPL-3.0-only).

using WebView2Guard.Core;
using Xunit;

namespace WebView2Guard.Tests;

public class AppVersionTests
{
    [Theory]
    [InlineData("0.1.0-beta.2", "0.1.0-beta.1", 1)]
    [InlineData("0.1.0-beta.10", "0.1.0-beta.9", 1)]
    [InlineData("0.1.0", "0.1.0-beta.9", 1)]
    [InlineData("0.2.0-beta.1", "0.1.0", 1)]
    [InlineData("v0.1.0-beta.3", "0.1.0-beta.3", 0)]
    [InlineData("0.1.0-beta.1", "0.1.0", -1)]
    [InlineData(null, "0.1.0", -1)]
    public void Compare_orders_like_semver(string? a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(AppVersion.Compare(a, b)));
}

public class DomainRulesTests
{
    [Fact]
    public void Build_and_parse_round_trip()
    {
        var domains = new[] { "login.autodesk.com", "*.autodesk.com" };
        var args = DomainRules.Build(domains);
        Assert.Equal("--host-resolver-rules=\"MAP * ~NOTFOUND, EXCLUDE login.autodesk.com, EXCLUDE *.autodesk.com\"", args);
        Assert.True(DomainRules.IsOurs(args));
        Assert.Equal(domains, DomainRules.Parse(args));
    }

    [Fact]
    public void Parse_ignores_foreign_arguments()
    {
        Assert.Null(DomainRules.Parse("--disable-features=Foo"));
        Assert.Null(DomainRules.Parse(null));
    }

    [Fact]
    public void Normalize_cleans_urls_and_duplicates()
    {
        var list = DomainRules.Normalize("https://Login.Autodesk.com/path?x=1\r\nlogin.autodesk.com, accounts.autodesk.com:443 ;");
        Assert.Equal(new[] { "login.autodesk.com", "accounts.autodesk.com" }, list);
    }

    [Theory]
    [InlineData("autodesk.com", true)]
    [InlineData("*.autodesk.com", true)]
    [InlineData("localhost", true)]
    [InlineData("autodesk", false)]
    [InlineData("*.*.com", false)]
    [InlineData("bad_domain.com", false)]
    [InlineData("a,b.com", false)]
    public void IsValid(string domain, bool expected) => Assert.Equal(expected, DomainRules.IsValid(domain));
}

public class PolicyStoreTests
{
    [Theory]
    [InlineData("acad.exe", true)]
    [InlineData("AdskIdentityManager.EXE", true)]
    [InlineData("*", false)]
    [InlineData(@"C:\x\acad.exe", false)]
    [InlineData("acad", false)]
    public void IsValidExeName(string name, bool expected) => Assert.Equal(expected, PolicyStore.IsValidExeName(name));
}

public class RuntimeUpdaterTests
{
    private const string Escaped =
        "\"https:\\u002F\\u002Fmsedge.sf.dl.delivery.mp.microsoft.com\\u002Ffilestreamingservice\\u002Ffiles\\u002F" +
        "ecc2761e-1380-46ee-9552-995b039e4e47\\u002FMicrosoft.WebView2.FixedVersionRuntime.158.0.4370.33.x64.cab\"";

    private const string Plain =
        "https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/" +
        "a7f1531c-7d30-406d-889c-0fb35549c7b9/Microsoft.WebView2.FixedVersionRuntime.157.0.4341.60.x86.cab";

    private const string OtherHost =
        "https://evil.example.com/filestreamingservice/files/" +
        "a7f1531c-7d30-406d-889c-0fb35549c7b9/Microsoft.WebView2.FixedVersionRuntime.999.0.0.1.x64.cab";

    [Fact]
    public void ParseLinks_reads_escaped_and_plain_links_from_microsoft_host_only()
    {
        var list = RuntimeUpdater.ParseLinks(Escaped + " " + Plain + " " + Plain + " " + OtherHost);
        Assert.Equal(2, list.Count);

        var x64 = RuntimeUpdater.Latest(list, Arch.X64);
        Assert.NotNull(x64);
        Assert.Equal("158.0.4370.33", x64!.Version);
        Assert.Equal("msedge.sf.dl.delivery.mp.microsoft.com", x64.Url.Host);
        Assert.EndsWith("/Microsoft.WebView2.FixedVersionRuntime.158.0.4370.33.x64.cab", x64.Url.AbsolutePath);

        Assert.Equal("157.0.4341.60", RuntimeUpdater.Latest(list, Arch.X86)!.Version);
    }

    [Fact]
    public void IsNewer_compares_numeric_versions()
    {
        var d = new RuntimeDownload("158.0.4370.33", Arch.X64, new Uri("https://msedge.sf.dl.delivery.mp.microsoft.com/x.cab"));
        var installed = new RuntimeInfo(Arch.X64, true, "157.0.4341.60", "C:\\x", null, null);
        Assert.True(RuntimeUpdater.IsNewer(d, installed));
        Assert.False(RuntimeUpdater.IsNewer(d, installed with { Version = "158.0.4370.33" }));
        Assert.True(RuntimeUpdater.IsNewer(d, installed with { Installed = false, Version = null }));
    }
}

public class PowerShellHelperTests
{
    [Theory]
    [InlineData("", "[]")]
    [InlineData("null", "[]")]
    [InlineData("{\"a\":1}", "[{\"a\":1}]")]
    [InlineData("[{\"a\":1}]", "[{\"a\":1}]")]
    public void AsJsonArray(string input, string expected) => Assert.Equal(expected, PowerShell.AsJsonArray(input));

    [Fact]
    public void Q_escapes_single_quotes() => Assert.Equal("'O''Brien'", PowerShell.Q("O'Brien"));
}

public class SyncTaskParseTests
{
    [Fact]
    public void Parse_missing_task() => Assert.False(SyncTask.Parse("{\"Exists\":false}").Exists);

    [Fact]
    public void Parse_existing_task()
    {
        var t = SyncTask.Parse("{\"Exists\":true,\"State\":\"Ready\",\"LastRun\":\"2026-11-21T15:00:03.0000000+02:00\"," +
                               "\"LastResult\":0,\"NextRun\":\"\",\"Execute\":\"C:\\\\x\\\\WebView2Guard.exe\",\"Arguments\":\"--sync\"}");
        Assert.True(t.Exists);
        Assert.Equal("Ready", t.State);
        Assert.NotNull(t.LastRun);
        Assert.Null(t.NextRun);
        Assert.Equal(@"C:\x\WebView2Guard.exe --sync", t.Command);
    }
}

public class FirewallParseTests
{
    [Fact]
    public void Parse_single_object_and_sort_allow_first()
    {
        var rules = FirewallManager.Parse(
            "[{\"Name\":\"WV2G-Block-1.0.0.1\",\"DisplayName\":\"b\",\"Action\":\"Block\",\"Program\":\"C:\\\\b.exe\",\"Enabled\":\"True\"}," +
            "{\"Name\":\"WV2G-Allow-x64\",\"DisplayName\":\"a\",\"Action\":\"Allow\",\"Program\":\"C:\\\\a.exe\",\"Enabled\":\"False\"}]");
        Assert.Equal("WV2G-Allow-x64", rules[0].Name);
        Assert.False(rules[0].Enabled);
        Assert.True(rules[1].Enabled);
        Assert.Single(FirewallManager.Parse("{\"Name\":\"x\",\"Action\":\"Block\",\"Program\":\"p\",\"Enabled\":\"True\"}"));
    }
}

public class ConfigTransferTests
{
    [Fact]
    public void Serialize_and_parse_round_trip()
    {
        var c = new GuardConfig
        {
            AppVersion = "0.1.0",
            Apps =
            {
                new AppConfig { ExeName = "AdskIdentityManager.exe", Allowed = true, Arch = Arch.X64, Domains = new() { "*.autodesk.com" } },
                new AppConfig { ExeName = "acad.exe", Arch = Arch.X64, Path = @"C:\Program Files\Autodesk\acad.exe" }
            },
            Settings = new SettingsConfig { Language = "ro", AutoUpdateRuntime = true }
        };
        var json = ConfigTransfer.Serialize(c);
        Assert.Contains("\"X64\"", json);
        var back = ConfigTransfer.Parse(json);
        Assert.Equal(2, back.Apps.Count);
        Assert.True(back.Apps[0].Allowed);
        Assert.Equal(new[] { "*.autodesk.com" }, back.Apps[0].Domains);
        Assert.Null(back.Apps[1].Domains);
        Assert.Equal("ro", back.Settings!.Language);
        Assert.True(back.Settings.AutoUpdateRuntime);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Format\":\"something else\",\"Apps\":[]}")]
    [InlineData("{\"Format\":\"WebView2Guard.Config\",\"FormatVersion\":2,\"Apps\":[]}")]
    [InlineData("{\"Format\":\"WebView2Guard.Config\",\"Apps\":[{\"ExeName\":\"..\\\\evil.exe\"}]}")]
    [InlineData("{\"Format\":\"WebView2Guard.Config\",\"Apps\":[{\"ExeName\":\"*\",\"Allowed\":true}]}")]
    public void Parse_rejects_invalid_files(string json) =>
        Assert.Throws<InvalidOperationException>(() => ConfigTransfer.Parse(json));
}


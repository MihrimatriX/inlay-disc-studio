using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Inlay.Core;
using Inlay.Theming;
using Xunit;

namespace Inlay.Theming.Tests;

public class FilterTests
{
    [Theory]
    [InlineData(7322, "2 sa 2 dk")]
    [InlineData(1440, "24 dk")]
    [InlineData(3600, "1 sa")]
    [InlineData(20, "1 dk")]
    public void Duration(double sec, string text) => Filters.Duration((decimal)sec).Should().Be(text);

    [Fact]
    public void Duration_in_english() => Filters.Duration(7322, "en-US").Should().Be("2 h 2 min");

    [Theory]
    [InlineData(201, "3:21")]
    [InlineData(59, "0:59")]
    [InlineData(3725, "1:02:05")]
    public void Clock(double sec, string text) => Filters.Clock((decimal)sec).Should().Be(text);

    [Theory]
    [InlineData(4_520_000_000, "4,21 GB")]
    [InlineData(86 * 1024 * 1024, "86 MB")]
    [InlineData(84 * 1024, "84 KB")]
    [InlineData(512, "512 B")]
    public void File_size_uses_the_turkish_decimal_comma(long bytes, string text) =>
        Filters.FileSize(bytes, CultureInfo.GetCultureInfo("tr-TR")).Should().Be(text);

    [Fact]
    public void Relurl_points_at_the_virtual_host_or_the_disc_root()
    {
        Filters.RelUrl("Film/Kuzey Rüzgârı/poster.webp", hosted: true).Should().Be("https://disc.local/Film/Kuzey%20R%C3%BCzg%C3%A2r%C4%B1/poster.webp");
        Filters.RelUrl("Film/a b.mp4", hosted: false).Should().Be("./Film/a%20b.mp4");
    }

    [Fact]
    public void Json_cannot_break_out_of_a_script_tag() =>
        Filters.Json(new Dictionary<string, object?> { ["t"] = "</script><script>alert(1)</script>&" + (char)0x2028 })
            .Should().NotContain("<").And.NotContain(">").And.NotContain("&").And.Contain("\\u2028");
}

public sealed class ThemeTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-theme-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    public static readonly string[] Standard = ["standard-mixed", "standard-movie", "standard-album", "standard-gallery", "standard-app"];
    public static TheoryData<string, string> Matrix
    {
        get
        {
            var d = new TheoryData<string, string>();
            foreach (var t in Standard) foreach (var s in new[] { "mixed", "series", "bundle" }) d.Add(t, s);
            return d;
        }
    }

    [Theory, MemberData(nameof(Matrix))]
    public void Every_standard_theme_renders_every_sample_without_lint_errors(string theme, string sample)
    {
        var m = ReferenceDiscs.Sample(sample);
        var ui = ThemeRenderer.Render(m, ReferenceDiscs.Theme(theme));
        ui.Issues.Where(i => i.Severity == LintSeverity.Error).Should().BeEmpty();
        ui.IndexHtml.Should().Contain("http-equiv=\"Content-Security-Policy\"").And.Contain("<style id=\"ds-vars\">:root{--ds-bg:");
        foreach (var item in m.Items.Where(i => i is not App { Category: AppCategory.Runtime }))
            ui.IndexHtml.Should().Contain($"id=\"v-{item.Id}\"", $"{item.Id} has a page");
        ui.Assets.Keys.Should().Contain(["assets/ds.css", "assets/ds.js", "fonts/Outfit-latin.woff2"]);
    }

    [Fact]
    public void Templates_escape_manifest_text()
    {
        var m = ReferenceDiscs.Sample("bundle");
        m = m with { Disc = m.Disc with { Title = "<img src=x onerror=alert(1)>" } };
        var ui = ThemeRenderer.Render(m, ReferenceDiscs.Theme("standard-app"));
        ui.IndexHtml.Should().NotContain("<img src=x onerror").And.Contain("&lt;img src=x onerror=alert(1)&gt;");
    }

    [Fact]
    public void Fallback_menu_inlines_the_sdk_and_reaches_assets_from_the_disc_root()
    {
        var ui = ThemeRenderer.Render(ReferenceDiscs.Sample("mixed"), ReferenceDiscs.Theme("standard-mixed"), sdkScript: "/*sdk*/");
        ui.FallbackHtml.Should().Contain("window.__inlayBoot=").And.Contain("\"hosted\":false").And.Contain("/*sdk*/");
        ui.FallbackHtml.Should().Contain("href=\"./.disc/ui/assets/ds.css\"").And.Contain("src=\"./.disc/ui/assets/ds.js\"");
        ui.FallbackHtml.Should().Contain("src=\"./Film/").And.NotContain("https://disc.local/Film");
    }

    [Fact]
    public void Auto_colors_and_defaults_resolve_from_the_palette()
    {
        var m = ReferenceDiscs.Sample("mixed");
        var s = Settings.Resolve(ReferenceDiscs.Theme("standard-mixed").Theme, m);
        s["accent"].Should().Be(m.Theme.Palette.Accent);
        s["primary"].Should().Be("cream");
        var chosen = m with { Theme = m.Theme with { Settings = new Dictionary<string, JsonElement> { ["primary"] = JsonSerializer.SerializeToElement("amber") } } };
        Settings.Resolve(ReferenceDiscs.Theme("standard-mixed").Theme, chosen)["primary"].Should().Be("amber");
    }

    [Fact]
    public void Theme_packages_round_trip_through_disctheme_zips()
    {
        var theme = ReferenceDiscs.Theme("standard-movie");
        using var ms = new MemoryStream();
        theme.SaveZip(ms);
        ms.Position = 0;
        var back = ThemeFiles.FromZip(ms);
        back.Theme.Id.Should().Be("ds.standard.movie");
        back.Files.Keys.Should().BeEquivalentTo(theme.Files.Keys);
        ThemeRenderer.Render(ReferenceDiscs.Sample("series"), back).Blocking.Should().BeFalse();
    }

    [Fact]
    public void Zip_slip_paths_are_refused()
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(z.CreateEntry("theme.json").Open())) w.Write(File.ReadAllText(Path.Combine(ReferenceDiscs.Repo, "themes", "standard-movie", "theme.json")));
            using (var w = new StreamWriter(z.CreateEntry("../../evil.dll").Open())) w.Write("x");
        }
        ms.Position = 0;
        FluentActions.Invoking(() => ThemeFiles.FromZip(ms)).Should().Throw<ThemeException>().WithMessage("*unsafe path*");
    }

    [Fact]
    public void Invalid_theme_json_is_refused()
    {
        var dir = Path.Combine(_dir, "bad");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "theme.json"), """{"id":"Bad Id","name":"x","version":"1","modes":[],"entry":"index.liquid","engine":"liquid","minLauncher":"0.1.0"}""");
        FluentActions.Invoking(() => ThemeFiles.FromFolder(dir)).Should().Throw<ThemeException>().WithMessage("theme.json*");
    }

    ThemeFiles Custom(string index, string? extraAsset = null, double? budgetMb = null, string capabilities = "\"play\"")
    {
        var dir = Path.Combine(_dir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        File.WriteAllText(Path.Combine(dir, "theme.json"),
            $$"""{"id":"me.custom","name":"Benim","version":"1.0.0","modes":["mixed"],"entry":"index.liquid","engine":"liquid","minLauncher":"0.1.0","capabilities":[{{capabilities}}]{{(budgetMb is null ? "" : $",\"budget\":{{\"maxAssetsMb\":{budgetMb.Value.ToString(CultureInfo.InvariantCulture)}}}")}}}""");
        File.WriteAllText(Path.Combine(dir, "index.liquid"), index);
        File.WriteAllText(Path.Combine(dir, "assets", "app.css"), "body{}");
        if (extraAsset is not null) File.WriteAllBytes(Path.Combine(dir, "assets", "big.bin"), new byte[(int)(1.5 * 1024 * 1024)]);
        return ThemeFiles.FromFolder(dir);
    }

    static IEnumerable<string> Rules(RenderedUi ui, LintSeverity s) => ui.Issues.Where(i => i.Severity == s).Select(i => i.Rule);

    [Fact]
    public void Lint_rejects_actions_outside_the_manifest()
    {
        var ui = ThemeRenderer.Render(ReferenceDiscs.Sample("mixed"), Custom("""<html><head></head><body><button data-action="format-c"></button><script>disc.run('nope')</script><button data-action="play-movie"></button></body></html>"""));
        ui.Issues.Where(i => i.Rule == "unknown-action").Select(i => i.Message).Should().HaveCount(2).And.OnlyContain(x => x.Contains("format-c") || x.Contains("nope"));
        ui.Blocking.Should().BeTrue();
    }

    [Fact]
    public void Lint_rejects_the_network_and_missing_assets()
    {
        var ui = ThemeRenderer.Render(ReferenceDiscs.Sample("mixed"), Custom("""
            <html><head><link rel="stylesheet" href="assets/app.css"><link rel="stylesheet" href="assets/missing.css">
            <script src="https://cdn.example.com/x.js"></script><style>body{background:url(https://evil.example/bg.png)}</style></head>
            <body><a href="https://example.com">site</a><img src="{{ 'Film/poster.webp' | relurl }}"></body></html>
            """));
        Rules(ui, LintSeverity.Error).Should().BeEquivalentTo(["missing-asset", "external-resource", "external-url", "external-resource"], o => o.WithoutStrictOrdering());
        ui.Issues.Should().NotContain(i => i.Message.Contains("assets/app.css") || i.Message.Contains("disc.local"));
    }

    [Fact]
    public void Lint_enforces_the_asset_budget_and_warns_about_capabilities()
    {
        var ui = ThemeRenderer.Render(ReferenceDiscs.Sample("mixed"), Custom("<html><head></head><body><script>disc.installBundle([])</script></body></html>", extraAsset: "big", budgetMb: 1));
        Rules(ui, LintSeverity.Error).Should().Contain("asset-budget");
        Rules(ui, LintSeverity.Warning).Should().Contain("capability");
    }

    [Fact]
    public void Liquid_errors_block_and_templates_cannot_reach_dotnet()
    {
        ThemeRenderer.Render(ReferenceDiscs.Sample("mixed"), Custom("{% if %}")).Issues.Should().Contain(i => i.Rule == "liquid-syntax");
        var ui = ThemeRenderer.Render(ReferenceDiscs.Sample("mixed"), Custom("<p>{{ disc.GetType }}{{ disc.title.Length }}{{ items.Count }}</p>"));
        ui.IndexHtml.Should().Contain("<p></p>", "only manifest data is visible to templates");
    }

    /// <summary>Rendered reference discs vs tests/golden (set INLAY_UPDATE_GOLDEN=1 to accept a deliberate change).
    /// Also writes artifacts/preview/&lt;name&gt;/ for scripts/screenshot.mjs.</summary>
    [Fact]
    public void Golden_files_and_preview_discs()
    {
        var golden = Path.Combine(ReferenceDiscs.Repo, "tests", "golden");
        var update = Environment.GetEnvironmentVariable("INLAY_UPDATE_GOLDEN") == "1";
        foreach (var (name, manifest, theme, images) in ReferenceDiscs.All())
        {
            var sdk = Path.Combine(ReferenceDiscs.Repo, "ui", "disc-sdk", "dist", "disc-sdk.js");
            var ui = ThemeRenderer.Render(manifest, ReferenceDiscs.Theme(theme), File.Exists(sdk) ? File.ReadAllText(sdk) : null);
            ui.Blocking.Should().BeFalse(string.Join("; ", ui.Issues.Select(i => i.Message)));
            var file = Path.Combine(golden, name + ".html");
            if (update || !File.Exists(file))
            {
                Directory.CreateDirectory(golden);
                File.WriteAllText(file, ui.IndexHtml);
            }
            else ui.IndexHtml.ReplaceLineEndings().Should().Be(File.ReadAllText(file).ReplaceLineEndings(), $"{name} differs from tests/golden (INLAY_UPDATE_GOLDEN=1 to accept)");

            var preview = Path.Combine(ReferenceDiscs.Repo, "artifacts", "preview", name);
            if (Directory.Exists(preview)) Directory.Delete(preview, recursive: true);
            var ui_ = Path.Combine(preview, ".disc", "ui");
            Directory.CreateDirectory(ui_);
            File.WriteAllText(Path.Combine(ui_, "index.html"), ui.IndexHtml, Encoding.UTF8);
            foreach (var (path, bytes) in ui.Assets)
            {
                var p = Path.Combine(ui_, path);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllBytes(p, bytes);
            }
            File.WriteAllText(Path.Combine(preview, ".disc", "manifest.json"), JsonSerializer.Serialize(manifest, InlayJson.Default.DiscManifest));
            File.WriteAllText(Path.Combine(preview, "Menu.html"), ui.FallbackHtml, Encoding.UTF8);
            foreach (var (disk, img) in images)
            {
                var p = Path.Combine(preview, disk.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.Copy(Path.Combine(ReferenceDiscs.Repo, "design", "img", img), p, overwrite: true);
            }
        }
    }
}

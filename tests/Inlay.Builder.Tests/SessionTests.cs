using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Inlay.Analysis;
using Inlay.Analysis.Online;
using Inlay.Core;
using Inlay.Theming;
using Xunit;

namespace Inlay.Builder.Tests;

/// <summary>M6 acceptance on the builder's engine (the React screens drive exactly these calls through <see cref="Rpc"/>):
/// a project from a dropped folder, corrections, the menu edits, copy-and-edit of a standard template, and lint errors
/// blocking the disc.</summary>
public sealed class SessionTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-builder-").FullName;
    readonly Session _s;
    View? _last;
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SessionTests()
    {
        InlayFolders.Root = Path.Combine(_dir, "data");
        _s = new Session(Prefs.Load(Path.Combine(_dir, "prefs.json")), new KeyStore(Path.Combine(_dir, "keys.dat")), drives: () => []);
        _s.Changed += v => _last = v;
    }

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    static string Repo
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !File.Exists(Path.Combine(d.FullName, "Inlay.slnx"))) d = d.Parent;
            return d!.FullName;
        }
    }

    /// <summary>A photo folder: analysis needs no FFmpeg for it, so this runs everywhere.</summary>
    string Photos(string name = "Kapadokya 2024", int count = 6)
    {
        var dir = Path.Combine(_dir, "src", name);
        Directory.CreateDirectory(dir);
        for (var i = 1; i <= count; i++) File.Copy(Path.Combine(Repo, "design", "img", $"g{i}.jpg"), Path.Combine(dir, $"IMG_{i:000}.jpg"));
        return dir;
    }

    [Fact]
    public async Task A_dropped_folder_becomes_rows_a_menu_and_a_capacity_ruler()
    {
        await _s.Add([Photos()]);
        var v = _s.View();
        v.Phase.Should().Be("ready");
        v.Kind.Should().Be("photo", "the start menu follows the recognised kind (§3.1)");
        v.Project.Name.Should().Be("Kapadokya 2024");
        v.Rows.Should().ContainSingle().Which.Should().Match<RowView>(r => r.Kind == "gallery" && r.KindLabel == "Fotoğraf" && r.Info == "6 fotoğraf" && r.Bytes > 0);
        v.Capacity!.Fits.Should().BeTrue();
        v.Capacity.Media.Should().Be("cd700");
        v.Menu!.Templates.Should().Contain(t => t.Id == "ds.standard.gallery" && t.Selected);
        v.Menu.Preview.Should().StartWith(Session.DiscOrigin + ".disc/ui/index.html");
        v.Editor!.Blocking.Should().BeFalse(string.Join("; ", v.Editor.Issues.Select(i => i.Message)));
        _last.Should().NotBeNull("every change publishes a view");
        File.Exists(Path.Combine(InlayFolders.Root, "projects", _s.Project.Id + DiscProject.Extension)).Should().BeTrue("changes autosave (§7.3)");
    }

    [Fact]
    public async Task Copy_and_edit_a_standard_template_and_a_lint_error_blocks_the_disc()
    {
        await _s.Add([Photos()]);
        _s.View().Editor!.User.Should().BeFalse();
        var write = () => _s.WriteThemeFile("index.liquid", "x");
        await write.Should().ThrowAsync<InvalidOperationException>("standard templates are read-only until copied");

        await _s.CopyTheme();
        var editor = _s.View().Editor!;
        editor.User.Should().BeTrue();
        editor.ThemeName.Should().Be("Galeri (kopya)");
        _s.View().Menu!.Templates.Should().Contain(t => t.Id == Session.UserThemeId && t.Selected);
        JsonDocument.Parse(_s.Data()).RootElement.GetProperty("theme").GetProperty("package").GetString().Should().Be("user.gallery");

        var index = _s.ReadThemeFile("index.liquid");
        await _s.WriteThemeFile("index.liquid", index.Replace("</body>", "<img src=\"https://example.com/track.png\"></body>"));
        editor = _s.View().Editor!;
        editor.Blocking.Should().BeTrue();
        editor.Issues.Should().Contain(i => i.Severity == LintSeverity.Error && i.Rule == "external-resource");
        _s.Invoking(s => s.EnsureBuildable()).Should().Throw<BuildBlockedException>().WithMessage("*Şablonda*");
        var iso = () => _s.ExportIso(Path.Combine(_dir, "x.iso"), ct: Ct);
        await iso.Should().ThrowAsync<BuildBlockedException>();
        File.Exists(Path.Combine(_dir, "x.iso")).Should().BeFalse();

        await _s.WriteThemeFile("index.liquid", index.Replace("</body>", "<p>{{ disc.title }}</p></body>"));
        _s.View().Editor!.Blocking.Should().BeFalse();
        (await _s.Preview(".disc/ui/index.html", Ct))!.Bytes.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString((await _s.Preview(".disc/ui/index.html", Ct))!.Bytes!).Should().Contain("<p>Kapadokya 2024</p>", "the preview is the edited render");

        await _s.Undo();
        System.Text.Encoding.UTF8.GetString((await _s.Preview(".disc/ui/index.html", Ct))!.Bytes!).Should().NotContain("<p>Kapadokya 2024</p>");
        _s.View().Editor!.Blocking.Should().BeTrue("undo steps back to the broken edit");
    }

    [Fact]
    public async Task Disctheme_export_and_import_round_trip()
    {
        await _s.Add([Photos()]);
        await _s.CopyTheme();
        await _s.WriteThemeFile("assets/extra.css", "body{outline:0}");
        var file = Path.Combine(_dir, "galeri.disctheme");
        _s.ExportTheme(file);
        using (var zip = ZipFile.OpenRead(file)) zip.Entries.Select(e => e.FullName).Should().Contain(["theme.json", "index.liquid", "assets/extra.css"]);

        await _s.Template("ds.standard.gallery");
        _s.View().Editor!.User.Should().BeFalse("choosing a standard card drops the copy");
        await _s.ImportTheme(file);
        _s.View().Editor!.User.Should().BeTrue();
        _s.ReadThemeFile("assets/extra.css").Should().Be("body{outline:0}");
    }

    [Fact]
    public async Task Menu_edits_reach_the_manifest_without_reanalysis()
    {
        await _s.Add([Photos()]);
        await _s.SetMenu(new MenuEdit(Autoplay: true, Remote: false, Title: "Yaz Tatili"));
        var disc = JsonDocument.Parse(_s.Data()).RootElement.GetProperty("disc");
        disc.GetProperty("autoplay").GetBoolean().Should().BeTrue();
        disc.GetProperty("remote").GetBoolean().Should().BeFalse();
        disc.GetProperty("title").GetString().Should().Be("Yaz Tatili");
        disc.GetProperty("label").GetString().Should().Be("YAZ_TATILI");
        _s.View().Menu!.AutoplayLabel.Should().Be("Slayt gösterisini başlat");

        await _s.SetSetting("columns", JsonSerializer.SerializeToElement(3));
        JsonDocument.Parse(_s.Data()).RootElement.GetProperty("theme").GetProperty("settings").GetProperty("columns").GetInt32().Should().Be(3);
        _s.View().Editor!.Settings.Single(x => x.Key == "columns").Value!.Value.GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Sections_can_be_reordered_renamed_and_hidden()
    {
        await _s.Add([Photos("Kapadokya"), Photos("Ege", 4)]);
        File.WriteAllText(Path.Combine(_dir, "src", "Okuyun.pdf"), "%PDF-1.4");
        await _s.Add([Path.Combine(_dir, "src", "Okuyun.pdf")]);
        var sections = _s.View().Editor!.Sections;
        sections.Select(x => x.Kind).Should().Equal(SectionKind.Gallery, SectionKind.File);

        await _s.SetSections([new SectionEdit("sec-file", "Okunacaklar", SectionLayout.Grid, false), new SectionEdit("sec-gallery", null, null, true)]);
        var m = JsonDocument.Parse(_s.Data()).RootElement.GetProperty("sections");
        m[0].GetProperty("id").GetString().Should().Be("sec-file");
        m[0].GetProperty("title").GetString().Should().Be("Okunacaklar");
        m[0].GetProperty("layout").GetString().Should().Be("grid");
        m[1].GetProperty("hidden").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Removing_correcting_and_renaming_rows()
    {
        await _s.Add([Photos("Kapadokya"), Photos("Ege", 4)]);
        var rows = _s.View().Rows;
        rows.Should().HaveCount(2);
        var ege = rows.Single(r => r.Title == "Ege");

        await _s.Rename(ege.Id, "Ege Kıyıları");
        _s.View().Rows.Should().Contain(r => r.Title == "Ege Kıyıları");

        await _s.SetKind(ege.Id, ContentKind.Document);
        _s.Project.Kinds.Keys.Should().Contain(k => k.StartsWith("Ege/", StringComparison.Ordinal));
        _s.View().Rows.Where(r => r.Kind == "file").Should().HaveCount(4, "a correction re-runs the classifier with the forced kind (§5.2)");

        await _s.Undo();
        var gallery = _s.View().Rows.Single(r => r.Kind == "gallery" && r.Info == "4 fotoğraf");
        await _s.Remove([gallery.Id]);
        _s.View().Rows.Should().ContainSingle(r => r.Title == "Kapadokya");
        _s.Project.Excluded.Should().HaveCount(4);
    }

    [Fact]
    public async Task Project_file_round_trips_and_reopens_to_the_same_disc()
    {
        await _s.Add([Photos()]);
        await _s.SetMenu(new MenuEdit(Title: "Kapadokya"));
        await _s.CopyTheme();
        var path = Path.Combine(_dir, "Kapadokya.discproj");
        _s.Save(path);
        var before = _s.Data();

        var other = new Session(Prefs.Load(Path.Combine(_dir, "prefs2.json")), new KeyStore(Path.Combine(_dir, "keys.dat")), drives: () => []);
        await other.Open(path);
        other.Path.Should().Be(path);
        other.View().Editor!.User.Should().BeTrue("the edited template is embedded in the project");
        static JsonElement Strip(string json) { var o = System.Text.Json.Nodes.JsonNode.Parse(json)!; o["disc"]!.AsObject().Remove("createdAt"); return JsonSerializer.SerializeToElement(o); }
        JsonSerializer.Serialize(Strip(other.Data())).Should().Be(JsonSerializer.Serialize(Strip(before)));
    }

    [Fact]
    public async Task Preview_serves_the_hosted_render_with_a_stand_in_launcher_and_disc_files()
    {
        await _s.Add([Photos()]);
        var html = System.Text.Encoding.UTF8.GetString((await _s.Preview(".disc/ui/index.html?r=1", Ct))!.Bytes!);
        html.IndexOf("<script src=\"/~preview/sdk.js\"></script>", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("ds.js", StringComparison.Ordinal));
        html.Should().NotContain("browser-note", "the preview looks like the launcher, not like Menu.html in a browser");
        var sdk = System.Text.Encoding.UTF8.GetString((await _s.Preview("~preview/sdk.js", Ct))!.Bytes!);
        sdk.Should().StartWith("window.__inlayBoot=").And.Contain("\"preview\":true");

        var photo = JsonDocument.Parse(_s.Data()).RootElement.GetProperty("items")[0].GetProperty("photos")[0];
        var thumb = await _s.Preview(Uri.EscapeDataString(photo.GetProperty("thumb").GetString()!).Replace("%2F", "/"), Ct);
        thumb!.File.Should().NotBeNull();
        File.Exists(thumb.File).Should().BeTrue("a converted file is made on first request");
        (await _s.Preview("../../secret.txt", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Iso_export_prepares_every_file_and_writes_the_image()
    {
        Assert.SkipUnless(LauncherBinary.Find() is not null, "needs the published launcher (artifacts/launcher)");
        await _s.Add([Photos()]);
        var stages = new List<string>();
        var iso = Path.Combine(_dir, "Kapadokya.iso");
        await _s.ExportIso(iso, new SyncProgress<JobProgress>(p => stages.Add(p.Stage)), Ct);
        new FileInfo(iso).Length.Should().BeGreaterThan(1024 * 1024);
        stages.Distinct().Should().Equal("prepare", "write", "done");
    }

    sealed class SyncProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }

    [Fact]
    public async Task Drive_capsule_and_recorder_choice_follow_the_disc_in_the_drive()
    {
        Inlay.Burn.Recorder[] drives =
        [
            new("rec-1", "D:", "HL-DT-ST DVDRAM", null),
            new("rec-2", "E:", "ASUS DRW-24D5MT", new Inlay.Burn.DiscInDrive(Inlay.Burn.MediaType.DvdDashR, true, true, 4_700_372_992, 4_700_372_992)),
        ];
        var s = new Session(Prefs.Load(Path.Combine(_dir, "prefs3.json")), new KeyStore(Path.Combine(_dir, "keys.dat")), drives: () => drives);
        await s.RefreshDrives();
        var v = s.View();
        v.Drives.Select(d => d.Status).Should().Equal("Sürücüde disk yok", "Boş DVD-R, 4,7 GB");
        v.Drives[1].Should().Match<DriveView>(d => d.Ready && d.Name == "E: ASUS DRW-24D5MT" && d.Fits == DiscMedia.Dvd5);
        s.RecorderId.Should().Be("rec-2", "the drive with a blank disc, unless Gelişmiş names one");
        s.SetPrefs(new PrefsEdit(Recorder: "rec-1"));
        s.RecorderId.Should().Be("rec-1");
    }

    [Fact]
    public async Task Burning_without_a_recorder_says_there_is_no_disc()
    {
        await _s.Add([Photos()]);
        var burn = () => _s.Burn(true, ct: Ct);
        (await burn.Should().ThrowAsync<Inlay.Burn.BurnException>()).Which.Error.Should().Be(Inlay.Burn.BurnError.NoMedia);
    }

    [Fact]
    public async Task Rpc_maps_methods_to_the_session()
    {
        var dir = Photos();
        await Rpc.Call(_s, "content.add", JsonSerializer.SerializeToElement(new { paths = new[] { dir } }));
        var view = (View)(await Rpc.Call(_s, "state", default))!;
        view.Rows.Should().NotBeEmpty();
        JsonSerializer.Serialize(view, Rpc.Json).Should().Contain("\"kindLabel\":\"Fotoğraf\"").And.Contain("\"phase\":\"ready\"");
        var unknown = () => Rpc.Call(_s, "disk.format", default);
        await unknown.Should().ThrowAsync<ArgumentException>();
        var badKey = () => Rpc.Call(_s, "keys.set", JsonSerializer.SerializeToElement(new { name = "other", value = "x" }));
        await badKey.Should().ThrowAsync<ArgumentException>();
        (await Rpc.Call(_s, "keys.test", JsonSerializer.SerializeToElement(new { service = "omdb" }))).Should().Be("missing", "no key is saved, so nothing is sent");
        var badService = () => Rpc.Call(_s, "keys.test", JsonSerializer.SerializeToElement(new { service = "other" }));
        await badService.Should().ThrowAsync<ArgumentException>();
    }
}

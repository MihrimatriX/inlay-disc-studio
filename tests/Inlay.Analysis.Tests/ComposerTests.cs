using System.Text.Json;
using FluentAssertions;
using ImageMagick;
using Inlay.Analysis;
using Inlay.Core;
using Inlay.Media;
using Json.Schema;
using Xunit;

namespace Inlay.Analysis.Tests;

/// <summary>Drop folder → analysis → draft → schema-valid manifest → materialized files (M3 end to end).</summary>
public sealed class ComposerTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("inlay-compose-").FullName;
    string Drop => Path.Combine(_root, "Koleksiyon");
    public void Dispose() => Directory.Delete(_root, recursive: true);
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    string P(string rel)
    {
        var p = Path.Combine(Drop, rel.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        return p;
    }

    void Image(string rel, uint w, uint h, MagickFormat f = MagickFormat.Jpeg)
    {
        using var img = new MagickImage("gradient:#ff8a3d-#203040", w, h);
        img.Write(P(rel), f);
    }

    void BuildDrop()
    {
        const string v = "-f lavfi -i testsrc2=size=320x180:rate=10:duration=3 -f lavfi -i sine=duration=3:sample_rate=48000";
        LocalMetaTests.Ffmpeg($"{v} -c:v libopenh264 -c:a ac3 -ac 6 \"{P("Kuzey Rüzgârı (2023)/Kuzey Rüzgârı.mkv")}\"");
        LocalMetaTests.Ffmpeg($"{v} -t 2 -c:v libopenh264 -c:a aac -movflags +faststart \"{P("Kuzey Rüzgârı (2023)/Fragman.mp4")}\"");
        Image("Kuzey Rüzgârı (2023)/poster.jpg", 1000, 1500);
        File.WriteAllText(P("Kuzey Rüzgârı (2023)/Kuzey Rüzgârı.tr.srt"), "1\n00:00:00,000 --> 00:00:01,000\nMerhaba\n");
        File.WriteAllText(P("Kuzey Rüzgârı (2023)/movie.nfo"), "<movie><title>Kuzey Rüzgârı</title><year>2023</year><plot>Dağ bekler.</plot><genre>Macera</genre></movie>");

        for (var i = 1; i <= 3; i++)
            LocalMetaTests.Ffmpeg($"-f lavfi -i sine=frequency={300 + i * 100}:duration=1 -c:a flac -metadata title=\"Parça {i}\" -metadata artist=Deniz -metadata album_artist=Deniz -metadata album=Işık -metadata track={i} -metadata date=2024 \"{P($"Deniz - Işık/{i:00}.flac")}\"");
        Image("Deniz - Işık/cover.jpg", 600, 600);
        File.WriteAllText(P("Deniz - Işık/01.lrc"), "[00:00.00]Açılış\n");

        for (var i = 1; i <= 5; i++) Image($"Tatil/IMG_{i:000}.jpg", 800, 600);
        Image("Tatil/panorama.jpg", 3200, 1200);
        Image("Tatil/tarama.tiff", 900, 900, MagickFormat.Tiff);
        LocalMetaTests.Ffmpeg($"-f lavfi -i testsrc2=size=160x120:rate=10:duration=2 -c:v libopenh264 \"{P("Tatil/klip.mp4")}\"");

        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"), P("Oyunumuz/setup.exe"));
        File.WriteAllText(P("Oyunumuz/veri/level1.pak"), "pak");
        File.WriteAllText(P("Oyunumuz/_Redist/vc_redist.x64.exe"), "MZ");
        File.WriteAllText(P("Belgeler/kılavuz.pdf"), "%PDF-1.4");
    }

    [Fact]
    public async Task Mixed_drop_becomes_a_valid_manifest_and_real_files()
    {
        Assert.SkipUnless(MediaTools.Available, "tools/ffmpeg is missing");
        BuildDrop();
        var analysis = await Analyzer.Run([Drop], ct: Ct);
        var draft = Composer.Compose(analysis, new ComposeOptions { Now = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero) });
        var m = draft.Manifest;

        // Schema-valid, exactly like the disc will carry it.
        var json = JsonSerializer.Serialize(m, InlayJson.Default.DiscManifest);
        var result = JsonSchema.FromFile(Path.GetFullPath("schemas/manifest.schema.json"))
            .Evaluate(JsonDocument.Parse(json).RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        result.IsValid.Should().BeTrue(string.Join("\n", result.Details?.Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")) ?? []));

        m.Disc.Home.View.Should().Be(HomeView.Menu);
        m.Disc.Media.Should().Be(DiscMedia.Cd700);
        m.Theme.Package.Should().Be("ds.standard.mixed");
        m.Sections.Select(s => s.Kind).Should().Equal(SectionKind.Movie, SectionKind.Album, SectionKind.Gallery, SectionKind.App, SectionKind.File);

        var movie = m.Items.OfType<Movie>().Single();
        movie.Should().Match<Movie>(x => x.Title == "Kuzey Rüzgârı" && x.Year == 2023 && x.Overview == "Dağ bekler." && x.Source!.Provider == MetaProvider.Nfo);
        movie.Media.Should().Match<MediaFile>(x => x.File == "Film/Kuzey Rüzgârı (2023)/Kuzey Rüzgârı.mkv" && x.Playback.Engine == PlaybackEngine.Libvlc && x.Playback.Normalized == null);
        movie.Media.Audio!.Select(a => a.Codec).Should().Equal("ac3");
        movie.Media.Subtitles.Should().ContainSingle().Which.Should().Match<Subtitle>(s => s.Lang == "tr" && s.File!.EndsWith(".tr.vtt"));
        movie.Trailer!.File.Should().Be("Film/Kuzey Rüzgârı (2023)/Fragman.mp4");
        movie.Cover.Should().Be("Film/Kuzey Rüzgârı (2023)/poster.webp");
        m.Disc.Featured.Should().Be(movie.Id);

        var album = m.Items.OfType<Album>().Single();
        album.Should().Match<Album>(a => a.Title == "Işık" && a.Artist == "Deniz" && a.Cover == "Muzik/Deniz - Işık/cover.webp");
        album.Tracks.Select(t => t.Title).Should().Equal("Parça 1", "Parça 2", "Parça 3");
        album.Tracks[0].Lyrics.Should().Be("Muzik/Deniz - Işık/01 - Parça 1.lrc");

        var gallery = m.Items.OfType<Gallery>().Single();
        gallery.Photos.Should().HaveCount(8);
        gallery.Photos.Single(p => p.File.Contains("panorama")).Should().Match<Photo>(p => p.File.EndsWith(".webp") && p.Width == 2560 && p.Height == 960);
        gallery.Photos.Single(p => p.File.Contains("tarama")).File.Should().EndWith(".avif");
        gallery.Photos.Single(p => p.Kind == PhotoKind.Video).Media!.Playback.Engine.Should().Be(PlaybackEngine.Html);

        var game = m.Items.OfType<App>().Single(a => a.Category != AppCategory.Runtime);
        var vc = m.Items.OfType<App>().Single(a => a.Category == AppCategory.Runtime);
        var install = m.Actions.Single(a => a.Id == game.Actions![0]);
        install.Should().Match<DiscAction>(a => a.Kind == ActionKind.Install && a.Target == "Setup/Oyunumuz/setup.exe" && a.Elevated == true && a.Confirm == null);
        game.Signature!.Signed.Should().BeTrue();
        game.Requirements!.Prerequisites.Should().Equal(vc.Actions![0]);
        m.Actions.Single(a => a.Id == vc.Actions[0]).Target.Should().Be("Setup/Oyunumuz/_Redist/vc_redist.x64.exe", "the copy inside the game folder is reused");
        draft.Files.Should().ContainKey("Setup/Oyunumuz/veri/level1.pak");
        draft.Files[game.Icon!].Should().BeOfType<IconJob>();

        // Every path the manifest names is produced by exactly one job.
        foreach (var a in m.Actions.Where(a => a.Kind is ActionKind.Install or ActionKind.Run or ActionKind.Open))
            draft.Files.Should().ContainKey(a.Target!);
        draft.Files.Keys.Should().OnlyContain(k => RelPath.IsValid(k));
        DiscFileSystem.JolietViolations(draft.Files.Keys).Should().BeEmpty();

        // Materialize and check the outputs.
        var work = Path.Combine(_root, "work");
        var reports = new List<double>();
        var done = await Materializer.Run(draft, work, progress: new Sync(reports.Add), ct: Ct);
        done.Files.Values.Should().OnlyContain(f => File.Exists(f));
        reports.Should().NotBeEmpty();
        reports.Max().Should().BeApproximately(1, 0.0001);
        done.Files[movie.Media.File].Should().Be(P("Kuzey Rüzgârı (2023)/Kuzey Rüzgârı.mkv"), "media is not converted by default");
        File.ReadAllText(done.Files[movie.Media.Subtitles![0].File!]).Should().StartWith("WEBVTT");
        new MagickImageInfo(done.Files[movie.Cover!]).Should().Match<MagickImageInfo>(i => i.Width == 780 && i.Format == MagickFormat.WebP);
        done.Manifest.Items.OfType<Gallery>().Single().Photos.Where(p => p.Kind != PhotoKind.Video).Should().OnlyContain(p => p.Blurhash != null && p.Blurhash.Length == 28);
        done.Files[movie.Trailer.File].Should().Be(P("Kuzey Rüzgârı (2023)/Fragman.mp4"), "untouched files are imaged straight from the source");
    }

    [Fact]
    public async Task Single_movie_opens_its_page_with_the_movie_template()
    {
        Assert.SkipUnless(MediaTools.Available, "tools/ffmpeg is missing");
        LocalMetaTests.Ffmpeg($"-f lavfi -i testsrc2=size=160x90:rate=5:duration=1 -c:v libopenh264 -movflags +faststart \"{P("Film/Gece.2020.mp4")}\"");
        var analysis = await Analyzer.Run([Drop], ct: Ct);
        var m = Composer.Compose(analysis).Manifest;
        m.Disc.Home.Should().Match<DiscHome>(h => h.View == HomeView.Item && h.Item == "mv-gece");
        m.Theme.Package.Should().Be("ds.standard.movie");
        m.Disc.Label.Should().Be("GECE");
    }

    sealed class Sync(Action<double> f) : IProgress<double> { public void Report(double v) => f(v); }
}

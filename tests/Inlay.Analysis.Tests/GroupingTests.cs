using FluentAssertions;
using Inlay.Analysis;
using Inlay.Media;
using Xunit;

namespace Inlay.Analysis.Tests;

/// <summary>§5.2 classifier rows on real folder trees; durations and tags come from fake probes.</summary>
public sealed class GroupingTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("inlay-group-").FullName;
    readonly Dictionary<string, FileFacts> _facts = new(StringComparer.OrdinalIgnoreCase);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    string Drop => Path.Combine(_root, "Drop");

    void Put(string rel, double? durationSec = null, AudioTags? tags = null)
    {
        var p = Path.Combine(Drop, rel.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        if (durationSec is not null || tags is not null)
            _facts[p] = new FileFacts(durationSec is null ? null : new MediaInfo
            {
                FormatName = "x", DurationSec = durationSec,
                Streams = [new StreamInfo { Index = 0, Kind = Scanner.TypeOf(Path.GetExtension(rel)) == FileType.Audio ? StreamKind.Audio : StreamKind.Video, Codec = "h264" }],
            }, tags);
    }

    List<ContentGroup> Group(IReadOnlyDictionary<string, ContentKind>? overrides = null) =>
        Grouping.Group(Scanner.Scan([Drop]), f => _facts.GetValueOrDefault(f.FullPath) ?? new FileFacts(null, null), overrides);

    static AudioTags Tag(string album, int track, string artist = "Deniz") => new(null, artist, artist, album, track, 1, 2024, null, 200, null);

    [Fact]
    public void A_feature_length_video_is_a_movie_with_trailer_extras_art_and_subtitles()
    {
        Put("Kuzey Rüzgârı (2023)/Kuzey Rüzgârı.mkv", 7322);
        Put("Kuzey Rüzgârı (2023)/Fragman.mp4", 90);
        Put("Kuzey Rüzgârı (2023)/Ekstralar/Kamera arkası.mp4", 300);
        Put("Kuzey Rüzgârı (2023)/poster.jpg");
        Put("Kuzey Rüzgârı (2023)/fanart.jpg");
        Put("Kuzey Rüzgârı (2023)/Kuzey Rüzgârı.tr.srt");
        Put("Kuzey Rüzgârı (2023)/movie.nfo");
        var g = Group().Should().ContainSingle().Which;
        g.Should().Match<ContentGroup>(x => x.Kind == ContentKind.Movie && x.Title == "Kuzey Rüzgârı" && x.Year == 2023 && x.Confidence >= 0.9);
        g.Trailer!.Name.Should().Be("Fragman.mp4");
        g.Extras.Select(e => e.Name).Should().Equal("Kamera arkası.mp4");
        g.Cover!.Name.Should().Be("poster.jpg");
        g.Backdrop!.Name.Should().Be("fanart.jpg");
        g.Sidecars.Select(s => s.Ext).Should().BeEquivalentTo([".srt", ".nfo"]);
    }

    [Fact]
    public void Several_movies_in_one_folder_stay_separate()
    {
        Put("Filmler/Inception.2010.1080p.mkv", 8880);
        Put("Filmler/Interstellar.2014.mkv", 10140);
        Group().Select(g => (g.Kind, g.Title, g.Year)).Should().BeEquivalentTo([(ContentKind.Movie, "Inception", (int?)2010), (ContentKind.Movie, "Interstellar", (int?)2014)]);
    }

    [Fact]
    public void Episode_patterns_make_a_series_in_order()
    {
        Put("Ofis/Ofis.Gunlukleri.S01E02.mkv", 1440);
        Put("Ofis/Ofis.Gunlukleri.S01E01.mkv", 1440);
        Put("Ofis/Ofis.Gunlukleri.S02E01.mkv", 1440);
        Put("Ofis/poster.jpg");
        var g = Group().Should().ContainSingle().Which;
        g.Should().Match<ContentGroup>(x => x.Kind == ContentKind.Series && x.Title == "Ofis Gunlukleri" && x.Cover != null);
        g.Episodes.Should().Equal(new EpisodeRef(1, 1), new EpisodeRef(1, 2), new EpisodeRef(2, 1));
    }

    [Fact]
    public void Season_folders_name_the_series()
    {
        Put("Ofis/Sezon 1/Bölüm 1.mp4", 1400);
        Put("Ofis/Sezon 1/Bölüm 2.mp4", 1400);
        Group().Single().Should().Match<ContentGroup>(x => x.Kind == ContentKind.Series && x.Title == "Ofis");
    }

    [Fact]
    public void Tagged_tracks_make_an_album_with_cover_lyrics_and_booklet()
    {
        Put("Işık/02 İkinci.flac", 200, Tag("Işık", 2));
        Put("Işık/01 Açılış.flac", 200, Tag("Işık", 1));
        Put("Işık/03 Son.flac", 200, Tag("Işık", 3));
        Put("Işık/01 Açılış.lrc");
        Put("Işık/cover.jpg");
        Put("Işık/Scans/back.jpg");
        var g = Group().Should().ContainSingle().Which;
        g.Should().Match<ContentGroup>(x => x.Kind == ContentKind.Album && x.Title == "Işık" && x.Year == 2024 && x.Confidence >= 0.9);
        g.Main.Select(t => t.Name).Should().Equal("01 Açılış.flac", "02 İkinci.flac", "03 Son.flac");
        g.Cover!.Name.Should().Be("cover.jpg");
        g.Sidecars.Should().ContainSingle(s => s.Ext == ".lrc");
        g.Booklet.Should().ContainSingle(b => b.Name == "back.jpg");
    }

    [Fact]
    public void Untagged_audio_becomes_a_playlist_per_folder()
    {
        Put("Karışık/a.mp3", 180);
        Put("Karışık/b.mp3", 180);
        Group().Single().Should().Match<ContentGroup>(x => x.Kind == ContentKind.Album && x.Title == "Karışık" && x.Confidence < 0.7);
    }

    [Fact]
    public void Photos_and_short_clips_make_a_gallery()
    {
        for (var i = 1; i <= 6; i++) Put($"Tatil 2025/IMG_{i:000}.jpg");
        Put("Tatil 2025/VID_001.mp4", 45);
        var g = Group().Should().ContainSingle().Which;
        g.Should().Match<ContentGroup>(x => x.Kind == ContentKind.Gallery && x.Title == "Tatil 2025" && x.Main.Count == 7 && x.Confidence >= 0.9);
    }

    [Fact]
    public void Installer_folder_is_an_app_copied_whole_and_unity_layout_marks_a_game()
    {
        Put("Oyunumuz/setup.exe");
        Put("Oyunumuz/data/level1.pak");
        Put("Oyunumuz/Game/UnityPlayer.dll");
        Put("Oyunumuz/Game/Oyunumuz.exe");
        Put("Oyunumuz/_Redist/vc_redist.x64.exe");
        var groups = Group();
        var app = groups.Single(g => g.Kind == ContentKind.App);
        app.Should().Match<ContentGroup>(x => x.Title == "Oyunumuz" && x.IsGame && x.Root == "Drop/Oyunumuz" && x.Installer!.Name == "setup.exe");
        app.Portable!.Name.Should().Be("Oyunumuz.exe", "install or play from the disc (§7.0)");
        app.Tree.Select(t => t.Name).Should().Contain(["setup.exe", "level1.pak", "UnityPlayer.dll"]);
        groups.Should().Contain(g => g.Kind == ContentKind.Prerequisite && g.Installer!.Name == "vc_redist.x64.exe");
    }

    [Fact]
    public void Sibling_installers_form_a_bundle_of_apps()
    {
        Put("Setup/Game/setup.exe");
        Put("Setup/Editor/LevelEditor.msi");
        Put("Setup/SoundPack/setup.exe");
        Put("Setup/Prereq/vc_redist.x64.exe");
        var groups = Group();
        groups.Where(g => g.Kind == ContentKind.App).Select(g => g.Root).Should().BeEquivalentTo(["Drop/Setup/Game", "Drop/Setup/Editor", "Drop/Setup/SoundPack"]);
        groups.Count(g => g.Kind == ContentKind.Prerequisite).Should().Be(1);
    }

    [Fact]
    public void Portable_exe_with_dlls_is_an_app_without_installer()
    {
        Put("Araç/Arac.exe");
        Put("Araç/Arac.Core.dll");
        Put("Araç/unins000.exe");
        Group().Single().Should().Match<ContentGroup>(x => x.Kind == ContentKind.App && x.Installer == null && x.Portable!.Name == "Arac.exe");
    }

    [Fact]
    public void Documents_and_hidden_clutter()
    {
        Put("Belgeler/kılavuz.pdf");
        Put("Belgeler/Thumbs.db");
        Put("Belgeler/desktop.ini");
        Put("Belgeler/._kılavuz.pdf");
        Group().Should().ContainSingle().Which.Should().Match<ContentGroup>(x => x.Kind == ContentKind.Document && x.Title == "kılavuz");
    }

    [Fact]
    public void Mixed_drop_yields_every_kind()
    {
        Put("Film/Kuzey Rüzgârı.2023.mkv", 7322);
        Put("Müzik/01.flac", 200, Tag("Işık", 1));
        Put("Foto/a.jpg");
        Put("Oyun/setup.exe");
        Put("Belge/a.pdf");
        Group().Select(g => g.Kind).Should().BeEquivalentTo([ContentKind.Movie, ContentKind.Album, ContentKind.Gallery, ContentKind.App, ContentKind.Document]);
    }

    [Fact]
    public void User_correction_forces_a_kind_for_a_folder()
    {
        Put("Konser/konser.mkv", 5400); // would be a movie
        Group().Single().Kind.Should().Be(ContentKind.Movie);
        Group(new Dictionary<string, ContentKind> { ["Drop/Konser"] = ContentKind.Gallery }).Single().Kind.Should().Be(ContentKind.Gallery);
    }

    [Fact]
    public async Task Analyzer_probes_real_files()
    {
        Assert.SkipUnless(MediaTools.Available, "tools/ffmpeg is missing");
        var dir = Path.Combine(Drop, "Klip");
        Directory.CreateDirectory(dir);
        LocalMetaTests.Ffmpeg($"-f lavfi -i testsrc2=size=64x48:rate=5:duration=1 -c:v libopenh264 \"{Path.Combine(dir, "a.mp4")}\"");
        var r = await Analyzer.Run([Drop], ct: TestContext.Current.CancellationToken);
        r.FactsOf(r.Files.Single()).Media!.Video!.Codec.Should().Be("h264");
        r.Groups.Single().Kind.Should().Be(ContentKind.Movie);
        r.Mixed.Should().BeFalse();
    }
}

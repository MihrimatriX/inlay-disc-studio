using System.Text.Json;
using Inlay.Core;

namespace Inlay.Theming.Tests;

/// <summary>The content of design/screens/t1–t4 rebuilt as manifests over design/img, so rendered templates can be
/// put next to the reference PNGs (M4 acceptance).</summary>
public static class ReferenceDiscs
{
    public static readonly string Repo = FindRepo();

    static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Inlay.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    public static ThemeFiles Theme(string name) =>
        ThemeFiles.FromFolder(Path.Combine(Repo, "themes", name), Path.Combine(Repo, "themes", "_shared"));

    public static DiscManifest Sample(string name) =>
        JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(Repo, "samples", $"manifest.{name}.json")), InlayJson.Default.DiscManifest)!;

    static MediaFile Media(string file, double sec, string container = "mp4") =>
        new() { File = file, SizeBytes = 1_000_000, Container = container, DurationSec = sec, Playback = new Playback { Engine = PlaybackEngine.Html } };

    static DiscManifest Disc(string title, DiscHome home, Palette palette, IReadOnlyList<DiscItem> items, IReadOnlyList<DiscAction> actions, SectionKind kind, string package) => new()
    {
        Version = 1,
        Disc = new Disc
        {
            Label = DiscLabel.FromTitle(title), Title = title, Subtitle = "side project GAMES", Media = DiscMedia.Dvd5,
            CreatedAt = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero), Builder = "Inlay Disc Studio 0.1.0", Locale = "tr-TR", Home = home,
        },
        Theme = new ThemeRef { Package = package, Version = "1.0.0", Palette = palette },
        Sections = [new Section { Id = "sec", Kind = kind, Title = "x", Items = items.Select(i => i.Id).ToList() }],
        Items = items,
        Actions = actions,
        Credits = [new Credit { Text = "This product uses the TMDB API but is not endorsed or certified by TMDB." }],
    };

    /// <summary>name → (manifest, theme folder, files to copy from design/img: disc path → image name).</summary>
    public static IEnumerable<(string Name, DiscManifest Manifest, string Theme, Dictionary<string, string> Images)> All()
    {
        var dark = new Palette { Bg = "#0c0d0f", Surface = "#17181c", Accent = "#ffcf6b", AccentText = "#1c0f02", Text = "#f6f2ec", Muted = "#9aa0b1" };

        yield return ("t1-film", Disc("Kuzey Rüzgârı", new DiscHome { View = HomeView.Item, Item = "mv-kuzey" }, dark,
            [new Movie
            {
                Id = "mv-kuzey", Title = "Kuzey Rüzgârı", Year = 2023, Genres = ["Macera"], Cover = "Film/poster.jpg", Backdrop = "Film/backdrop.jpg",
                Overview = "Kayıp bir istasyonu arayan dört kişi, kışın en sert haftasında dağın kuzey yüzüne tırmanır. İkinci cümle görünmez.",
                Media = Media("Film/Kuzey Rüzgârı.mp4", 7322),
                Certification = "13+", Ratings = new Ratings { Imdb = 7.8, RottenTomatoes = 91, Metacritic = 74 },
                Crew = [new CrewMember { Name = "Ayşe Kaya", Job = "Director" }, new CrewMember { Name = "Deniz Yalın", Job = "Screenplay" }],
                Chapters = [new Chapter { Title = "Açılış", StartSec = 0 }, new Chapter { Title = "Kamp", StartSec = 1240 }],
                Actions = ["play-kuzey"],
            }],
            [new DiscAction { Id = "play-kuzey", Label = "İzle", Kind = ActionKind.Play, Target = "mv-kuzey" }], SectionKind.Movie, "ds.standard.movie"),
            "standard-movie", new() { ["Film/backdrop.jpg"] = "film-hero.jpg", ["Film/poster.jpg"] = "film-poster.jpg" });

        string[] tracks = ["Ana Tema", "Köprü", "Eve Dönüş", "Yağmurlu Pazartesi", "Son Sahne", "Jenerik"];
        double[] lens = [201, 176, 219, 162, 245, 138];
        yield return ("t2-muzik", Disc("Akustik Oturumlar", new DiscHome { View = HomeView.Item, Item = "al-akustik" },
            dark with { Bg = "#1a1712" },
            [new Album
            {
                Id = "al-akustik", Title = "Akustik Oturumlar", Artist = "side project GAMES", Year = 2024, Cover = "Muzik/cover.jpg",
                Tracks = tracks.Select((t, i) => new Track { Id = $"tr-{i + 1}", No = i + 1, Title = t, Media = Media($"Muzik/{i + 1:00} {t}.flac", lens[i], "flac") }).ToList(),
                Actions = ["play-akustik"],
            }],
            [new DiscAction { Id = "play-akustik", Label = "Çal", Kind = ActionKind.Playlist, Target = "al-akustik" }], SectionKind.Album, "ds.standard.album"),
            "standard-album", new() { ["Muzik/cover.jpg"] = "album.jpg" });

        var shots = new[] { ("g1", 2400, 1100), ("g2", 1200, 1100), ("g3", 900, 1800), ("g4", 1200, 1100), ("g5", 1200, 1100), ("g6", 1200, 1100) };
        yield return ("t3-fotograf", Disc("Yolculuk 2025", new DiscHome { View = HomeView.Item, Item = "gl-yolculuk" }, dark,
            [new Gallery
            {
                Id = "gl-yolculuk", Title = "Yolculuk 2025", DateRange = new DateRange { From = new DateOnly(2025, 6, 1), To = new DateOnly(2025, 8, 30) },
                Photos = shots.Select(s => new Photo { Id = "p-" + s.Item1, File = $"Fotograflar/{s.Item1}.jpg", Thumb = $"Fotograflar/{s.Item1}.jpg", Width = s.Item2, Height = s.Item3 }).ToList(),
                Slideshow = new Slideshow { IntervalMs = 4000, Transition = SlideTransition.Kenburns },
                Actions = ["show-yolculuk"],
            }],
            [new DiscAction { Id = "show-yolculuk", Label = "Slayt gösterisi", Kind = ActionKind.Slideshow, Target = "gl-yolculuk" }], SectionKind.Gallery, "ds.standard.gallery"),
            "standard-gallery", shots.ToDictionary(s => $"Fotograflar/{s.Item1}.jpg", s => s.Item1 + ".jpg"));

        yield return ("t4-oyun", Disc("Kum Saati", new DiscHome { View = HomeView.Item, Item = "ap-kum" }, dark with { Bg = "#0d0b0a" },
            [new App
            {
                Id = "ap-kum", Title = "Kum Saati", Category = AppCategory.Game, Version = "1.4.2", Year = 2025, Tags = ["Aksiyon", "Platform"], Backdrop = ".disc/art/ap-kum/hero.jpg",
                Ratings = new Ratings { Metacritic = 82 },
                Description = "Çölün ortasında zamanı geri saran bir kum saati bulan gezgin, kaybolan şehri yeniden kurmak zorunda. Devamı görünmez.",
                Screenshots = [".disc/art/ap-kum/shot-1.jpg", ".disc/art/ap-kum/shot-2.jpg", ".disc/art/ap-kum/shot-3.jpg"],
                InstallSizeBytes = 4L * 1024 * 1024 * 1024, Signature = new Signature { Signed = true },
                Requirements = new Requirements { Os = "64-bit Windows 10", Cpu = "Intel Core i5-8400", Gpu = "GTX 1060", RamGb = 4, Directx = "Sürüm 12", DiskGb = 4 },
                Actions = ["install-kum", "run-kum"],
            }],
            [
                new DiscAction { Id = "install-kum", Label = "Kur", Kind = ActionKind.Install, Target = "Setup/Kum Saati/setup.exe", Elevated = true },
                new DiscAction { Id = "run-kum", Label = "Kurmadan oyna", Kind = ActionKind.Run, Target = "Setup/Kum Saati/Game/KumSaati.exe" },
            ], SectionKind.App, "ds.standard.app"),
            "standard-app", new() { [".disc/art/ap-kum/hero.jpg"] = "game-hero.jpg", [".disc/art/ap-kum/shot-1.jpg"] = "ch1.jpg", [".disc/art/ap-kum/shot-2.jpg"] = "ch4.jpg", [".disc/art/ap-kum/shot-3.jpg"] = "ch2.jpg" });

        yield return ("mixed", Sample("mixed"), "standard-mixed", new());
        yield return ("series", Sample("series"), "standard-movie", new());
        yield return ("bundle", Sample("bundle"), "standard-app", new());
    }
}

using System.Security.Cryptography;
using System.Text;
using Inlay.Analysis.Online;
using Inlay.Core;
using Inlay.Media;
using Inlay.Theming;

namespace Inlay.Analysis;

public sealed record ComposeOptions
{
    public string? Title { get; init; }
    public DiscMedia? Media { get; init; }
    public string Locale { get; init; } = Text.DefaultLocale;
    public PlanOptions Plan { get; init; } = new();
    public IReadOnlyDictionary<ContentGroup, MovieMeta> Movies { get; init; } = new Dictionary<ContentGroup, MovieMeta>();
    public IReadOnlyDictionary<ContentGroup, GameMeta> Games { get; init; } = new Dictionary<ContentGroup, GameMeta>();
    /// <summary>Theme chosen in "Disk menüsü"; null picks the standard template for the content (§7.2).</summary>
    public string? ThemePackage { get; init; }
    public IReadOnlyDictionary<string, object>? ThemeSettings { get; init; }
    public DateTimeOffset? Now { get; init; }
    /// <summary>Room the builder reserves for Baslat.exe, the menu and fonts when picking the medium.</summary>
    public long ReservedBytes { get; init; } = 30L * 1024 * 1024;
}

/// <summary>Content groups + metadata → <see cref="DiscDraft"/>: the §8.1 layout, schema-valid manifest items, the
/// action allow-list, sections, opening view (§7.0), palette (§5.4) and every file job.</summary>
public sealed class Composer
{
    readonly AnalysisResult _analysis;
    readonly ComposeOptions _o;
    readonly Dictionary<string, FileJob> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly List<DiscAction> _actions = [];
    readonly List<DiscItem> _items = [];
    readonly HashSet<string> _ids = [];
    readonly List<Credit> _credits = [];
    readonly Dictionary<ContentGroup, string> _itemOf = [];
    readonly Dictionary<string, string> _prereqActionOfSource = [];
    readonly Dictionary<string, string> _diskOfSource = new(StringComparer.OrdinalIgnoreCase);
    string? _paletteSource;

    Composer(AnalysisResult analysis, ComposeOptions o) => (_analysis, _o) = (analysis, o);

    public static DiscDraft Compose(AnalysisResult analysis, ComposeOptions? options = null) => new Composer(analysis, options ?? new()).Run();

    string T(string key) => Text.Get(key, _o.Locale);
    string Folder(string key) => T($"disc.folder.{key}");

    DiscDraft Run()
    {
        // Apps first: their folders are copied whole, and prerequisites inside them must point into those copies.
        foreach (var g in _analysis.Groups.Where(g => g.Kind == ContentKind.App)) App(g);
        foreach (var g in _analysis.Groups.Where(g => g.Kind == ContentKind.Prerequisite)) Prerequisite(g);
        foreach (var app in _items.OfType<App>().Where(a => a.Category != AppCategory.Runtime).ToList())
            Replace(app, app with { Requirements = (app.Requirements ?? new Requirements()) with { Prerequisites = _prereqActionOfSource.Values.ToList() } });
        foreach (var g in _analysis.Groups)
            switch (g.Kind)
            {
                case ContentKind.Movie: Movie(g); break;
                case ContentKind.Series: Series(g); break;
                case ContentKind.Album: Album(g); break;
                case ContentKind.Gallery: Gallery(g); break;
                case ContentKind.Document: Document(g); break;
            }
        if (_o.Movies.Values.Any(m => m.Provider == MetaProvider.Tmdb)) _credits.Add(new Credit { Text = Tmdb.Attribution });
        foreach (var c in _o.Movies.Values.SelectMany(m => m.Credits).Concat(_o.Games.Values.SelectMany(g => g.Credits)).Distinct()) _credits.Add(new Credit { Text = c });
        return new DiscDraft(Manifest(), _files) { Groups = _itemOf.ToDictionary(kv => kv.Value, kv => kv.Key) };
    }

    DiscManifest Manifest()
    {
        var featured = Featured();
        var title = _o.Title ?? featured?.Title ?? _analysis.Groups.FirstOrDefault()?.Title ?? "Inlay";
        var total = _files.Values.Sum(f => f.EstimateBytes) + _o.ReservedBytes;
        var kindOrder = new[] { typeof(Movie), typeof(Series), typeof(Album), typeof(Gallery), typeof(App), typeof(FileItem) };
        var sections = kindOrder.Where(k => _items.Any(i => i.GetType() == k)).Select(k => new Section
        {
            Id = "sec-" + SectionKindOf(k).ToString().ToLowerInvariant(),
            Kind = SectionKindOf(k),
            Title = T("section." + SectionKindOf(k).ToString().ToLowerInvariant()),
            Layout = k == typeof(Gallery) ? SectionLayout.Grid : k == typeof(FileItem) ? SectionLayout.List : SectionLayout.Row,
            Items = _items.Where(i => i.GetType() == k).Select(i => i.Id).ToList(),
        }).ToList();
        if (sections.Count == 0) sections.Add(new Section { Id = "sec-file", Kind = SectionKind.File, Title = T("section.file"), Items = [] });
        var home = OpeningView.Decide(_items);
        return new DiscManifest
        {
            Version = 1,
            Disc = new Disc
            {
                Label = DiscLabel.FromTitle(title),
                Title = title,
                Media = _o.Media ?? MediaCapacity.Smallest(total) ?? DiscMedia.Dvd9,
                CreatedAt = (_o.Now ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                Builder = "Inlay Disc Studio " + typeof(Composer).Assembly.GetName().Version!.ToString(3),
                Locale = _o.Locale,
                Icon = Autorun.IconFile,
                Featured = featured?.Id,
                Logo = featured switch { Movie m => m.Logo, Series s => s.Logo, _ => null },
                Home = home,
            },
            Theme = new ThemeRef
            {
                Package = _o.ThemePackage ?? StandardTheme(home),
                Version = "1.0.0",
                Palette = _paletteSource is { } src ? PaletteExtractor.FromImage(src) : DefaultPalette,
                Settings = _o.ThemeSettings?.ToDictionary(kv => kv.Key, kv => System.Text.Json.JsonSerializer.SerializeToElement(kv.Value)),
            },
            Sections = sections,
            Items = _items,
            Actions = _actions,
            Credits = _credits.Count > 0 ? _credits : null,
        };
    }

    public static readonly Palette DefaultPalette = new()
    {
        Bg = "#0b0d14", Surface = "#161a26", Accent = "#ffcf6b", AccentText = "#1c0f02", Text = "#f2f1ee", Muted = "#9aa0b1", Source = PaletteSource.Auto,
    };

    /// <summary>§7.2: the template that fits what the disc opens to.</summary>
    string StandardTheme(DiscHome home)
    {
        var only = home.View switch
        {
            HomeView.Menu => null,
            HomeView.Bundle => typeof(App),
            HomeView.Collection => home.Kind switch { CollectionKind.Movie => typeof(Movie), CollectionKind.Album => typeof(Album), CollectionKind.Gallery => typeof(Gallery), _ => typeof(App) },
            _ => _items.FirstOrDefault(i => i.Id == home.Item)?.GetType(),
        };
        return only == typeof(Movie) || only == typeof(Series) ? "ds.standard.movie"
            : only == typeof(Album) ? "ds.standard.album"
            : only == typeof(Gallery) ? "ds.standard.gallery"
            : only == typeof(App) ? "ds.standard.app"
            : "ds.standard.mixed";
    }

    DiscItem? Featured() =>
        _items.OfType<Movie>().Cast<DiscItem>().Concat(_items.OfType<Series>()).Concat(_items.OfType<Album>())
            .Concat(_items.OfType<App>().Where(a => a.Category == AppCategory.Game)).Concat(_items.OfType<Gallery>()).FirstOrDefault();

    static SectionKind SectionKindOf(Type t) =>
        t == typeof(Movie) ? SectionKind.Movie : t == typeof(Series) ? SectionKind.Series : t == typeof(Album) ? SectionKind.Album
        : t == typeof(Gallery) ? SectionKind.Gallery : t == typeof(App) ? SectionKind.App : SectionKind.File;

    // ---------------- movies ----------------

    void Movie(ContentGroup g)
    {
        var meta = _o.Movies.GetValueOrDefault(g) ?? Nfo(g);
        var title = meta?.Title ?? g.Title;
        var year = meta?.Year ?? g.Year;
        var id = Id("mv", title);
        var dir = $"{Folder("movies")}/{Seg(year is null ? title : $"{title} ({year})")}";
        var main = g.Main[0];
        var media = Media(main, $"{dir}/{Seg(title)}", out var info);
        var poster = Art(meta?.Poster ?? g.Cover?.FullPath, $"{dir}/poster.webp", 780);
        var backdrop = Art(meta?.Backdrop ?? g.Backdrop?.FullPath, $"{dir}/backdrop.webp", 1920)
                       ?? Frame(main, (info?.DurationSec ?? 0) * 0.1, $"{dir}/backdrop.webp", 1920);
        _paletteSource ??= meta?.Backdrop ?? g.Backdrop?.FullPath ?? meta?.Poster ?? g.Cover?.FullPath;
        var cast = meta?.Cast.Select((c, i) => new CastMember { Name = c.Name, Character = c.Role, Photo = Art(c.Photo, $"{dir}/oyuncular/{i + 1:00}.webp", 185) }).ToList();
        var chapters = info?.Chapters.Take(40).Select((c, i) => new Chapter
        {
            Title = c.Title.Length > 0 ? c.Title : $"{i + 1}",
            StartSec = c.StartSec,
            Thumb = Frame(main, c.StartSec + 2, $"{dir}/bolumler/{i + 1:00}.webp", 320),
        }).ToList();
        var play = Action(new DiscAction { Id = "play-" + id, Label = T("action.watch"), Kind = ActionKind.Play, Icon = ActionIcon.Play, Target = id });
        Add(g, new Movie
        {
            Id = id, Title = title, OriginalTitle = meta?.OriginalTitle, Year = year, Overview = meta?.Overview, Tagline = meta?.Tagline,
            Genres = meta?.Genres.Count > 0 ? meta.Genres : null, Rating = meta?.Rating, Ratings = meta?.Ratings, Certification = meta?.Certification,
            Cover = poster, Backdrop = backdrop, Logo = Art(meta?.Logo ?? g.Logo?.FullPath, $"{dir}/logo.webp", 800),
            Palette = Palette(meta?.Poster ?? g.Cover?.FullPath),
            Cast = cast?.Count > 0 ? cast : null,
            Crew = meta?.Crew.Count > 0 ? meta.Crew.Select(c => new CrewMember { Name = c.Name, Job = c.Job }).ToList() : null,
            Chapters = chapters?.Count > 0 ? chapters : null,
            Media = media,
            Trailer = g.Trailer is { } t ? Media(t, $"{dir}/{T("disc.folder.trailer")}", out _) : null,
            Extras = g.Extras.Count > 0 ? g.Extras.Select(e => Media(e, $"{dir}/{Folder("extras")}/{Seg(e.Stem)}", out _)).ToList() : null,
            Source = meta is null ? null : new ItemSource { Provider = meta.Provider, ExternalId = meta.ExternalId },
            Actions = [play],
        });
    }

    MovieMeta? Nfo(ContentGroup g)
    {
        var nfo = g.Sidecars.FirstOrDefault(s => s.Type == FileType.Nfo) is { } f ? LocalMeta.Nfo(f.FullPath) : null;
        return nfo is null ? null : new MovieMeta
        {
            Title = nfo.Title ?? g.Title, OriginalTitle = nfo.OriginalTitle, Year = nfo.Year ?? g.Year, Overview = nfo.Plot, Tagline = nfo.Tagline,
            Genres = nfo.Genres, Rating = nfo.Rating, Certification = nfo.Certification,
            Crew = nfo.Directors.Select(d => (d, "Director")).ToList(),
            Cast = nfo.Actors.Select(a => new MetaPerson(a.Name, a.Role, null)).ToList(),
            Provider = MetaProvider.Nfo, ExternalId = nfo.TmdbId,
        };
    }

    // ---------------- series ----------------

    void Series(ContentGroup g)
    {
        var meta = _o.Movies.GetValueOrDefault(g);
        var title = meta?.Title ?? g.Title;
        var id = Id("sr", title);
        var dir = $"{Folder("series")}/{Seg(title)}";
        var seasons = g.Main.Zip(g.Episodes).GroupBy(x => x.Second.Season).OrderBy(s => s.Key).Select(s => new Season
        {
            No = s.Key,
            Title = s.Key == 0 ? T("series.specials") : string.Format(T("series.season"), s.Key),
            Episodes = s.Select((x, i) =>
            {
                var (file, ep) = x;
                var em = meta?.Episodes.GetValueOrDefault((ep.Season, ep.Episode));
                var no = ep.Episode > 0 ? ep.Episode : i + 1;
                var stem = $"S{ep.Season:00}E{no:00}" + (em is null ? "" : " - " + em.Title);
                var media = Media(file, $"{dir}/{string.Format(T("series.season"), s.Key)}/{Seg(stem)}", out var info);
                return new Episode
                {
                    Id = Id("ep", $"{title} s{ep.Season}e{no}"), No = no, Title = em?.Title ?? string.Format(T("series.episode"), no),
                    Overview = em?.Overview, AirDate = em?.AirDate,
                    Thumb = Art(em?.Still, $"{dir}/kucuk/s{ep.Season:00}e{no:00}.webp", 400)
                            ?? Frame(file, (info?.DurationSec ?? 0) * 0.2, $"{dir}/kucuk/s{ep.Season:00}e{no:00}.webp", 400),
                    Media = media,
                };
            }).ToList(),
        }).ToList();
        _paletteSource ??= meta?.Backdrop ?? g.Backdrop?.FullPath ?? meta?.Poster ?? g.Cover?.FullPath;
        var play = Action(new DiscAction { Id = "play-" + id, Label = T("action.watch"), Kind = ActionKind.Play, Icon = ActionIcon.Play, Target = id });
        Add(g, new Series
        {
            Id = id, Title = title, Overview = meta?.Overview, Genres = meta?.Genres.Count > 0 ? meta.Genres : null, YearFrom = meta?.Year, Ratings = meta?.Ratings,
            Cover = Art(meta?.Poster ?? g.Cover?.FullPath, $"{dir}/poster.webp", 780),
            Backdrop = Art(meta?.Backdrop ?? g.Backdrop?.FullPath, $"{dir}/backdrop.webp", 1920)
                       ?? Frame(g.Main[0], 60, $"{dir}/backdrop.webp", 1920),
            Logo = Art(meta?.Logo ?? g.Logo?.FullPath, $"{dir}/logo.webp", 800),
            Palette = Palette(meta?.Poster ?? g.Cover?.FullPath),
            Seasons = seasons,
            Source = meta is null ? null : new ItemSource { Provider = meta.Provider, ExternalId = meta.ExternalId },
            Actions = [play],
        });
    }

    // ---------------- albums ----------------

    void Album(ContentGroup g)
    {
        var tags = g.Main.Select(t => _analysis.FactsOf(t).Tags).ToList();
        var artist = tags.Select(t => t?.AlbumArtist ?? t?.Artist).Where(a => a is not null).GroupBy(a => a).MaxBy(x => x.Count())?.Key;
        var mixedArtists = tags.Select(t => t?.Artist).Where(a => a is not null).Distinct().Count() > 1;
        var playlist = tags.All(t => t?.Album is null);
        var title = playlist ? $"{g.Title}" : g.Title;
        var id = Id("al", (artist is null ? "" : artist + " ") + title);
        var dir = $"{Folder("music")}/{Seg(artist is null || playlist ? title : $"{artist} - {title}")}";
        var coverSource = g.Cover?.FullPath ?? EmbeddedCover(tags);
        _paletteSource ??= coverSource;
        var tracks = g.Main.Select((f, i) =>
        {
            var t = tags[i];
            var no = t?.Track ?? i + 1;
            var trackTitle = t?.Title ?? Names.Movie(f.Name).Title;
            var path = $"{dir}/{no:00} - {Seg(trackTitle, 50)}";
            var lyrics = g.Sidecars.FirstOrDefault(s => s.Type == FileType.Lyrics && s.Stem.Equals(f.Stem, StringComparison.OrdinalIgnoreCase));
            return new Track
            {
                Id = Id("tr", $"{title} {no} {trackTitle}"), No = no, Disc = t?.Disc is > 1 ? t.Disc : null, Title = trackTitle,
                Artist = playlist || mixedArtists ? t?.Artist : null,
                Lyrics = lyrics is null ? null : Copy(lyrics, path + lyrics.Ext),
                Media = Media(f, path, out _),
            };
        }).ToList();
        var booklet = g.Booklet.Select((b, i) => b.Type == FileType.Image
            ? Art(b.FullPath, $"{dir}/{T("disc.folder.booklet")}/{i + 1:00}.webp", 2560)!
            : Copy(b, $"{dir}/{Seg(b.Name)}")).ToList();
        var play = Action(new DiscAction { Id = "play-" + id, Label = T("action.play"), Kind = ActionKind.Playlist, Icon = ActionIcon.Music, Target = id });
        Add(g, new Album
        {
            Id = id, Title = playlist ? string.Format(T("album.playlist"), title) : title, Artist = playlist ? null : artist,
            Year = g.Year, Genre = tags.Select(t => t?.Genre).FirstOrDefault(x => x is not null),
            Cover = Art(coverSource, $"{dir}/cover.webp", 1000), Palette = Palette(coverSource),
            Booklet = booklet.Count > 0 ? booklet : null, Tracks = tracks, Actions = [play],
        });
    }

    static string? EmbeddedCover(IReadOnlyList<AudioTags?> tags)
    {
        if (tags.FirstOrDefault(t => t?.Cover is { Length: > 0 })?.Cover is not { } bytes) return null;
        Directory.CreateDirectory(InlayFolders.ImageCache);
        var path = Path.Combine(InlayFolders.ImageCache, Convert.ToHexStringLower(SHA256.HashData(bytes))[..32] + ".img");
        if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
        return path;
    }

    // ---------------- galleries ----------------

    void Gallery(ContentGroup g)
    {
        var id = Id("gl", g.Title);
        var dir = $"{Folder("photos")}/{Seg(g.Title)}";
        var entries = g.Main.Select(f => (File: f, Exif: f.Type == FileType.Image ? LocalMeta.Exif(f.FullPath) : null))
            .OrderBy(x => x.Exif?.TakenAt ?? DateTimeOffset.MaxValue).ThenBy(x => x.File.Rel, StringComparer.Ordinal).ToList();
        var photos = new List<Photo>();
        foreach (var (f, exif) in entries)
        {
            var pid = $"{id}-{photos.Count + 1}";
            if (pid.Length > 64) pid = Id("ph", $"{g.Title} {photos.Count + 1}");
            var stem = Seg(f.Stem, 50);
            if (f.Type == FileType.Video)
            {
                var media = Media(f, $"{dir}/{stem}", out var info);
                var v = info?.Video;
                photos.Add(new Photo
                {
                    Id = pid, Kind = PhotoKind.Video, File = media.File, Thumb = Frame(f, 1, $"{dir}/kucuk/{stem}.webp", ImagePipeline.ThumbMax)!,
                    Width = v?.Width ?? 1920, Height = v?.Height ?? 1080, Media = media,
                });
                continue;
            }
            DisplayPlan plan;
            try { plan = ImagePipeline.PlanDisplay(f.FullPath); }
            catch (ImageMagick.MagickException) { continue; } // unreadable picture: left out of the gallery
            var file = $"{dir}/{stem}{plan.Extension}";
            Put(file, new ImageJob(f.FullPath, ImageRole.Display, ImagePipeline.DisplayMax, plan));
            var thumb = $"{dir}/kucuk/{stem}.webp";
            Put(thumb, new ImageJob(f.FullPath, ImageRole.Fitted, ImagePipeline.ThumbMax));
            string? original = null;
            if (_o.Plan.KeepOriginals && ImagePipeline.NeedsConversion(f.FullPath)) original = Copy(f, $"{dir}/original/{Seg(f.Name)}");
            photos.Add(new Photo
            {
                Id = pid, File = file, Thumb = thumb, Original = original, Width = plan.Width, Height = plan.Height,
                Kind = plan.Animated ? PhotoKind.Animated : null, TakenAt = exif?.TakenAt, Camera = exif?.Camera,
                Location = exif?.Lat is { } lat && exif.Lon is { } lon ? new GeoLocation { Lat = lat, Lon = lon } : null,
            });
        }
        if (photos.Count == 0) return;
        var dates = entries.Select(e => e.Exif?.TakenAt).OfType<DateTimeOffset>().ToList();
        _paletteSource ??= entries.FirstOrDefault(e => e.File.Type == FileType.Image).File?.FullPath;
        var show = Action(new DiscAction { Id = "show-" + id, Label = T("action.slideshow"), Kind = ActionKind.Slideshow, Icon = ActionIcon.Image, Target = id });
        Add(g, new Gallery
        {
            Id = id, Title = g.Title, Cover = photos[0].Thumb,
            DateRange = dates.Count > 0 ? new DateRange { From = DateOnly.FromDateTime(dates.Min().DateTime), To = DateOnly.FromDateTime(dates.Max().DateTime) } : null,
            Slideshow = new Slideshow { IntervalMs = 5000, Transition = SlideTransition.Kenburns },
            Photos = photos, Actions = [show],
        });
    }

    // ---------------- apps and prerequisites ----------------

    void App(ContentGroup g)
    {
        var meta = _o.Games.GetValueOrDefault(g);
        var info = (g.Installer ?? g.Portable) is { } exe ? LocalMeta.Exe(exe.FullPath) : null;
        var title = meta?.Title ?? (g.Title.Length > 0 ? g.Title : info?.ProductName ?? "Program");
        var id = Id("ap", title);
        var dir = $"{Folder("setup")}/{Seg(title)}";
        foreach (var f in g.Tree)
        {
            var rel = g.Root is { Length: > 0 } root ? f.Rel[(root.Length + 1)..] : f.Rel;
            var disk = $"{dir}/{string.Join('/', rel.Split('/').Select(s => Seg(s)))}";
            _diskOfSource[f.FullPath] = Copy(f, disk);
        }
        var art = $".disc/art/{id}";
        var icon = (g.Portable ?? g.Installer) is { Ext: ".exe" } iconExe ? Put($"{art}/icon.png", new IconJob(iconExe.FullPath)) : null;
        var actions = new List<string>();
        var signed = info?.Signed ?? false;
        if (g.Installer is { } installer)
            actions.Add(Action(new DiscAction
            {
                Id = "install-" + id, Label = T("action.install"), Kind = ActionKind.Install, Icon = ActionIcon.Install,
                Target = _diskOfSource[installer.FullPath], Elevated = true,
                Confirm = signed ? null : T("confirm.unsigned"),
            }));
        if (g.Portable is { } portable)
            actions.Add(Action(new DiscAction
            {
                Id = "run-" + id, Label = T(g.IsGame || meta is not null ? "action.playGame" : "action.run"), Kind = ActionKind.Run, Icon = ActionIcon.Run,
                Target = _diskOfSource[portable.FullPath], WorkingDir = DirOf(_diskOfSource[portable.FullPath]),
                Confirm = signed || g.Installer is not null ? null : T("confirm.unsigned"),
            }));
        actions.Add(Action(new DiscAction { Id = "browse-" + id, Label = T("action.showFolder"), Kind = ActionKind.OpenFolder, Icon = ActionIcon.Folder, Target = dir }));
        var cover = Art(meta?.Cover, $"{art}/cover.webp", 600);
        if (meta?.Hero is not null) _paletteSource ??= meta.Hero;
        var appIndex = _items.OfType<App>().Count(a => a.Category != AppCategory.Runtime);
        Add(g, new App
        {
            Id = id, Title = title, Category = g.IsGame || meta is not null ? AppCategory.Game : AppCategory.App,
            Version = info?.Version, Year = meta?.Year, Ratings = meta?.Ratings, Publisher = info?.Publisher ?? meta?.Developer, Icon = icon, Cover = cover ?? icon,
            Description = meta?.Summary, Backdrop = Art(meta?.Hero, $"{art}/hero.webp", 1920),
            Tags = meta?.Genres.Count > 0 ? meta.Genres.ToList() : null,
            Logo = Art(meta?.Logo, $"{art}/logo.webp", 800),
            Screenshots = meta?.Screenshots.Count > 0 ? meta.Screenshots.Select((s, i) => Art(s, $"{art}/shot-{i + 1}.webp", 1280)!).ToList() : null,
            InstallSizeBytes = g.Tree.Sum(f => f.Size),
            Requirements = meta?.Requirements,
            Signature = info is null ? null : new Signature { Signed = info.Signed, Signer = info.Signer },
            Bundle = g.Installer is { } inst ? new BundleInfo { Selected = true, Order = appIndex + 1, SilentArgs = Installers.SilentArgs(inst.FullPath) is { Count: > 0 } a ? a : null } : null,
            Palette = Palette(meta?.Cover),
            Actions = actions,
        });
    }

    void Prerequisite(ContentGroup g)
    {
        var f = g.Installer!;
        var disk = _diskOfSource.GetValueOrDefault(f.FullPath) ?? Copy(f, $"{Folder("setup")}/{Folder("required")}/{Seg(f.Name)}");
        var info = LocalMeta.Exe(f.FullPath);
        var title = info.ProductName ?? f.Stem;
        var id = Id("rt", title);
        var install = Action(new DiscAction
        {
            Id = "install-" + id, Label = T("action.install"), Kind = ActionKind.Install, Icon = ActionIcon.Install, Target = disk,
            Elevated = true, Args = LocalMeta.PrerequisiteSilentArgs(f.Name).ToList(),
        });
        _prereqActionOfSource[f.FullPath] = install;
        Add(g, new App
        {
            Id = id, Title = title, Category = AppCategory.Runtime, Version = info.Version, Publisher = info.Publisher,
            Signature = new Signature { Signed = info.Signed, Signer = info.Signer },
            Bundle = new BundleInfo { Required = true, Order = 0, SilentArgs = LocalMeta.PrerequisiteSilentArgs(f.Name).ToList() },
            Actions = [install],
        });
    }

    // ---------------- documents ----------------

    void Document(ContentGroup g)
    {
        var f = g.Main[0];
        var id = Id("fl", g.Title);
        var path = Copy(f, $"{Folder("documents")}/{Seg(f.Name)}");
        var open = Action(new DiscAction { Id = "open-" + id, Label = T("action.open"), Kind = ActionKind.Open, Icon = ActionIcon.Document, Target = path });
        var show = Action(new DiscAction { Id = "where-" + id, Label = T("action.showFolder"), Kind = ActionKind.OpenFolder, Icon = ActionIcon.Folder, Target = path });
        Add(g, new FileItem { Id = id, Title = g.Title, Path = path, Mime = Mime(f.Ext), SizeBytes = f.Size, Actions = [open, show] });
    }

    static string? Mime(string ext) => ext switch
    {
        ".pdf" => "application/pdf", ".txt" => "text/plain", ".md" => "text/markdown", ".epub" => "application/epub+zip",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".rtf" => "application/rtf",
        _ => null,
    };

    // ---------------- media ----------------

    /// <summary>Normalizes per §6.1 and describes the result the way the launcher will see it.</summary>
    MediaFile Media(SourceFile f, string pathWithoutExt, out MediaInfo? info)
    {
        info = _analysis.FactsOf(f).Media;
        if (info is null)
        {
            var copied = Copy(f, pathWithoutExt + f.Ext);
            return new MediaFile { File = copied, SizeBytes = f.Size, Playback = new Playback { Engine = PlaybackEngine.Shell, Fallback = PlaybackFallback.None } };
        }
        var plan = NormalizationPlanner.Plan(info, f.Ext, _o.Plan, f.Ext is ".mp4" or ".m4v" ? Mp4.MoovFirst(f.FullPath) : null);
        var disk = pathWithoutExt + (plan.Step == PlanStep.Keep ? f.Ext : plan.OutputExtension);
        FileJob job = plan.Step == PlanStep.Keep ? new CopyJob(f.FullPath, f.Size) : new MediaJob(f.FullPath, info, plan, f.Size);
        Put(disk, job);
        _diskOfSource[f.FullPath] = disk;
        string? original = null;
        if (plan.KeepOriginal && plan.Step != PlanStep.Keep) original = Copy(f, $"{DirOf(disk)}/original/{Seg(f.Name)}");

        var subtitles = new List<Subtitle>();
        foreach (var idx in plan.TextSubtitles)
        {
            var s = info.Streams.First(x => x.Index == idx);
            var lang = s.Lang ?? Text.Resolve(_o.Locale)[..2];
            var vtt = Unique($"{pathWithoutExt}.{lang}.vtt");
            Put(vtt, new VttJob(f.FullPath, idx));
            subtitles.Add(new Subtitle { Lang = lang, File = vtt, Forced = s.Forced ? true : null });
        }
        foreach (var idx in plan.ImageSubtitles)
        {
            var s = info.Streams.First(x => x.Index == idx);
            subtitles.Add(new Subtitle { Lang = s.Lang ?? Text.Resolve(_o.Locale)[..2], Embedded = idx, Forced = s.Forced ? true : null });
        }
        foreach (var side in _analysis.Groups.SelectMany(g => g.Sidecars).Where(s => s.Type == FileType.Subtitle && s.Folder == f.Folder && s.Stem.StartsWith(f.Stem, StringComparison.OrdinalIgnoreCase)))
        {
            var lang = SidecarLang(side.Stem[f.Stem.Length..]) ?? Text.Resolve(_o.Locale)[..2];
            var vtt = Unique($"{pathWithoutExt}.{lang}.vtt");
            Put(vtt, side.Ext == ".vtt" ? new CopyJob(side.FullPath, side.Size) : new VttJob(side.FullPath, null));
            subtitles.Add(new Subtitle { Lang = lang, File = vtt });
        }

        var v = info.Video;
        var audio = new List<AudioStream>();
        if (plan.AddStereoAac && info.Audio.FirstOrDefault() is { } first) audio.Add(new AudioStream { Codec = "aac", Lang = first.Lang, Channels = 2, Default = true });
        var copiedAudio = plan.Step is PlanStep.Remux or PlanStep.Transcode ? info.Audio.Where(a => plan.CopyAudio.Contains(a.Index)) : info.Audio;
        audio.AddRange(copiedAudio.Select(a => new AudioStream
        {
            Codec = plan.Step == PlanStep.ToFlac ? "flac" : plan.Step == PlanStep.ToOpus ? "opus" : a.Codec,
            Lang = a.Lang, Channels = a.Channels, Title = a.Title, Default = audio.Count == 0 && a.Default ? true : null,
        }));
        return new MediaFile
        {
            File = disk,
            SizeBytes = job.EstimateBytes,
            Container = (plan.Step == PlanStep.Keep ? f.Ext : plan.OutputExtension).TrimStart('.'),
            DurationSec = info.DurationSec is { } d ? Math.Round(d, 2) : null,
            Video = v is null ? null : new VideoStream
            {
                Codec = plan.Step == PlanStep.Transcode ? "h264" : v.Codec, Width = v.Width, Height = v.Height, Fps = v.Fps,
                Hdr = plan.ToneMap ? HdrKind.None : v.Hdr,
            },
            Audio = audio.Count > 0 ? audio : null,
            Subtitles = subtitles.Count > 0 ? subtitles : null,
            Playback = new Playback
            {
                Engine = plan.Engine, Fallback = plan.Fallback, Normalized = plan.Normalized ? true : null,
                Original = original,
            },
        };
    }

    /// <summary>"Film.tr.srt", "Film.Turkish.srt", "Film.forced.en.srt" → language code.</summary>
    static string? SidecarLang(string suffix)
    {
        foreach (var part in suffix.Split('.', '_', '-', ' ').Reverse().Where(p => p.Length >= 2))
        {
            if (FfProbe.Lang(part) is { } code && code.Length == 2) return code;
            var byName = System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.NeutralCultures)
                .FirstOrDefault(c => c.EnglishName.Equals(part, StringComparison.OrdinalIgnoreCase) || c.NativeName.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (byName is { TwoLetterISOLanguageName.Length: 2 }) return byName.TwoLetterISOLanguageName;
        }
        return null;
    }

    // ---------------- helpers ----------------

    string? Art(string? source, string disk, uint max) =>
        source is not null && File.Exists(source) ? Put(Unique(disk), new ImageJob(source, ImageRole.Fitted, max, WidthOnly: true)) : null;

    string? Frame(SourceFile video, double atSec, string disk, uint max) =>
        _analysis.FactsOf(video).Media?.Video is null ? null : Put(Unique(disk), new FrameJob(video.FullPath, Math.Max(0, atSec), max));

    Palette? Palette(string? source)
    {
        if (source is null || !File.Exists(source)) return null;
        try { return PaletteExtractor.FromImage(source); }
        catch (ImageMagick.MagickException) { return null; }
    }

    string Copy(SourceFile f, string disk) => Put(Unique(disk), new CopyJob(f.FullPath, f.Size));

    string Put(string disk, FileJob job)
    {
        if (!RelPath.IsValid(disk)) throw new InvalidOperationException($"not a disc path: {disk}");
        _files[disk] = job;
        return disk;
    }

    string Unique(string disk)
    {
        if (!_files.ContainsKey(disk)) return disk;
        var ext = Path.GetExtension(disk);
        var stem = disk[..^ext.Length];
        for (var i = 2; ; i++)
            if (!_files.ContainsKey($"{stem} ({i}){ext}")) return $"{stem} ({i}){ext}";
    }

    string Id(string prefix, string title)
    {
        var id = Names.Id(prefix, title);
        for (var i = 2; !_ids.Add(id); i++) id = Names.Id(prefix, $"{title} {i}");
        return id;
    }

    string Action(DiscAction a)
    {
        _actions.Add(a);
        return a.Id;
    }

    void Add(ContentGroup g, DiscItem item)
    {
        _items.Add(item);
        _itemOf[g] = item.Id;
    }

    void Replace(DiscItem old, DiscItem updated) => _items[_items.IndexOf(old)] = updated;

    static string DirOf(string disk) => disk.Contains('/') ? disk[..disk.LastIndexOf('/')] : "";
    static string Seg(string s, int max = 64) => Names.DiscSegment(s, max);
}

/// <summary>Installer technology from marker strings in the exe, for the bundle's silent switches (§7.0).</summary>
public static class Installers
{
    public static IReadOnlyList<string> SilentArgs(string path)
    {
        if (Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase)) return ["/qn", "/norestart"];
        var head = Head(path);
        if (head.Contains("Inno Setup", StringComparison.Ordinal)) return ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-"];
        if (head.Contains("NullsoftInst", StringComparison.Ordinal) || head.Contains("Nullsoft.NSIS", StringComparison.Ordinal)) return ["/S"];
        if (head.Contains(".wixburn", StringComparison.Ordinal) || head.Contains("WixBundle", StringComparison.Ordinal)) return ["/quiet", "/norestart"];
        if (head.Contains("InstallShield", StringComparison.Ordinal)) return ["/s"];
        return []; // unknown: the installer runs with its own window
    }

    static string Head(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var buf = new byte[Math.Min(f.Length, 4 * 1024 * 1024)];
            var n = f.Read(buf);
            return Encoding.Latin1.GetString(buf, 0, n);
        }
        catch (IOException) { return ""; }
    }
}

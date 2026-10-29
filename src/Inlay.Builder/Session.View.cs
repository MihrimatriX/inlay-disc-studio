using System.Globalization;
using System.Text;
using System.Text.Json;
using Inlay.Analysis;
using Inlay.Analysis.Online;
using Inlay.Burn;
using Inlay.Core;
using Inlay.Media;
using Inlay.Theming;

namespace Inlay.Builder;

/// <summary>Everything the four screens draw, in the user's words: sizes are bytes, the rest is display text.</summary>
public sealed record View(ProjectView Project, string Phase, double? Progress, string? Error, string? Kind,
    IReadOnlyList<RowView> Rows, CapacityView? Capacity, MenuView? Menu, EditorView? Editor,
    IReadOnlyList<DriveView> Drives, PrefsView Prefs);

public sealed record ProjectView(string Name, string? Path, bool Named, IReadOnlyList<string> Missing, bool CanUndo, bool CanRedo, IReadOnlyList<RecentView> Recent);
public sealed record RecentView(string Path, string Name);
public sealed record RowView(string Id, string? Parent, string Title, string? Info, string? Thumb, string? Badge, string Kind, string KindLabel,
    bool Accent, long Bytes, string? Note, MatchCard? Match);
public sealed record CapacityView(long Used, string Media, long Usable, bool Fits, long Over, string? Needs, IReadOnlyList<SuggestionView> Suggestions);
public sealed record SuggestionView(int Index, string Text);
public sealed record MenuView(IReadOnlyList<TemplateView> Templates, bool Autoplay, string AutoplayLabel, string Title, string Label,
    string? Icon, bool IconFromPoster, bool Remote, string Preview, string? Glow);
public sealed record TemplateView(string Id, string Name, string? Thumb, bool Selected, bool Plain);
public sealed record EditorView(bool User, string ThemeName, IReadOnlyList<string> Files, IReadOnlyList<SettingView> Settings,
    IReadOnlyList<SectionView> Sections, IReadOnlyList<IssueView> Issues, bool Blocking);
public sealed record SettingView(string Key, SettingType Type, string Label, string? Help, string? Group, IReadOnlyList<SettingOption>? Options,
    double? Min, double? Max, double? Step, string? Unit, JsonElement? Value);
public sealed record SectionView(string Id, SectionKind Kind, string Title, SectionLayout? Layout, bool Hidden, int Count);
public sealed record IssueView(LintSeverity Severity, string Rule, string Message);
/// <summary><see cref="Ready"/>: a writable disc is in. <see cref="Readable"/>: a disc with data is in ("Disk kopyala" source).
/// <see cref="Erasable"/>: rewritable media ("Diski sil").</summary>
public sealed record DriveView(string Id, string Letter, string Name, string Status, bool Ready, bool Readable, bool Erasable, long Free, DiscMedia? Fits);
/// <summary><see cref="LauncherSigned"/>: whether the Baslat.exe this builder puts on discs carries an Authenticode
/// signature (§10: without one, the user is told that discs will show a SmartScreen warning).</summary>
public sealed record PrefsView(string Accent, string Locale, bool KeepOriginals, bool MaxCompatibility, VideoEncoder Encoder, int UdfRevision, int Speed,
    bool Verify, string? Recorder, IReadOnlyDictionary<string, bool> Keys, bool LauncherSigned);

public sealed record PreviewFile(byte[]? Bytes, string? File, string Mime);

public sealed partial class Session
{
    public const string PreviewOrigin = "https://preview.inlay/";
    /// <summary>The launcher's own origin. In the builder's WebView2 nothing maps it, so the host serves it from here.</summary>
    public const string DiscOrigin = "https://disc.local/";
    const string PreviewSdk = "~preview/sdk.js";
    const long Reserved = 30L * 1024 * 1024;

    public View View() => new(
        new ProjectView(Project.Name, Path, Path is not null, Project.Missing.ToList(), _undo.Count > 0, _redo.Count > 0,
            _prefs.Recent.Where(File.Exists).Select(r => new RecentView(r, System.IO.Path.GetFileNameWithoutExtension(r))).ToList()),
        _phase, _progress, _error, DiscKind(), Rows(), CapacityOf(), Menu(), Editor(), Drives.Select(Drive).ToList(),
        new PrefsView(_prefs.Accent, _prefs.Locale, _prefs.KeepOriginals, _prefs.MaxCompatibility, _prefs.Encoder, _prefs.UdfRevision, _prefs.Speed, _prefs.Verify,
            _prefs.Recorder, KeyNames.ToDictionary(k => k, k => !string.IsNullOrEmpty(_keys.Get(k))), LauncherSigned.Value));

    static readonly Lazy<bool> LauncherSigned = new(() => LauncherBinary.Find() is { } exe && LocalMeta.Signature(exe).Signed);

    /// <summary>The "Yeni disk" entry the content belongs to; the start menu follows it after a drop (§3.1).</summary>
    string? DiscKind()
    {
        if (_manifest is null) return null;
        var featured = _manifest.Items.FirstOrDefault(i => i.Id == _manifest.Disc.Featured) ?? _manifest.Items.FirstOrDefault();
        return featured switch { Movie or Series => "movie", Album => "music", Gallery => "photo", App => "app", null => null, _ => "data" };
    }

    static string Url(string? discPath) => discPath is null ? "" : PreviewOrigin + string.Join('/', discPath.Split('/').Select(Uri.EscapeDataString));

    // ---------------- Disk içeriği ----------------

    IReadOnlyList<RowView> Rows()
    {
        if (_manifest is null || _draft is null) return [];
        var rows = new List<RowView>();
        var culture = CultureInfo.GetCultureInfo(Project.Locale);
        foreach (var item in _manifest.Items)
        {
            var g = _draft.Groups.GetValueOrDefault(item.Id);
            var card = g is null ? null : _cards.GetValueOrDefault(Key(g));
            var (kind, info) = item switch
            {
                Movie m => ("movie", Join(m.Year?.ToString(culture), m.Genres?.FirstOrDefault(), Duration(m.Media.DurationSec))),
                Series s => ("series", T("builder.info.series", s.Seasons.Count, s.Seasons.Sum(x => x.Episodes.Count))),
                Album a => ("album", Join(a.Artist, a.Year?.ToString(culture), T("builder.info.tracks", a.Tracks.Count))),
                Gallery gal => ("gallery", T("builder.info.photos", gal.Photos.Count)),
                App app => (app.Category == AppCategory.Game ? "game" : "app", Join(app.Version, app.Publisher)),
                FileItem f => ("file", System.IO.Path.GetExtension(f.Path).TrimStart('.').ToUpperInvariant()),
                _ => ("file", null),
            };
            // No poster (offline, no key, typed in by hand): the frame the composer took for the backdrop stands in.
            var thumb = item.Cover ?? (item as App)?.Icon ?? (item as Gallery)?.Photos.FirstOrDefault()?.Thumb ?? (item as Movie)?.Backdrop ?? (item as Series)?.Backdrop;
            rows.Add(new RowView(item.Id, null, item.Title, info, thumb is null ? null : Url(thumb), null, kind, T("builder.kind." + kind),
                item.Id == _manifest.Disc.Featured || _manifest.Items.Count == 1, Bytes(item), Note(item), card));
            if (item is Movie movie) rows.AddRange(Parts(movie));
        }
        return rows;
    }

    IEnumerable<RowView> Parts(Movie m)
    {
        if (m.Trailer is { } t) yield return Part($"{m.Id}~trailer", m.Id, T("builder.row.trailer"), "video", t.File, FrameOf(m, 0));
        for (var i = 0; i < (m.Extras?.Count ?? 0); i++)
        {
            var e = m.Extras![i];
            yield return Part($"{m.Id}~extra~{i}", m.Id, System.IO.Path.GetFileNameWithoutExtension(e.File), "video", e.File, FrameOf(m, i + 1));
        }
        var subs = m.Media.Subtitles?.Where(s => s.File is not null).ToList() ?? [];
        for (var i = 0; i < subs.Count; i++)
        {
            var source = SourceOf(subs[i].File!);
            var badge = System.IO.Path.GetExtension(source ?? subs[i].File!).TrimStart('.').ToUpperInvariant();
            yield return Part($"{m.Id}~sub~{i}", m.Id, T("builder.row.subtitle", Language(subs[i].Lang)), "subtitle", subs[i].File!, null) with { Badge = badge };
        }
    }

    RowView Part(string id, string parent, string title, string kind, string file, string? thumb) =>
        new(id, parent, title, null, thumb, null, kind, T("builder.kind." + kind), false, _draft!.Files.GetValueOrDefault(file)?.EstimateBytes ?? 0, null, null);

    /// <summary>A film's chapter frames stand in as thumbnails for its trailer and extras, like the design does.</summary>
    static string? FrameOf(Movie m, int n) => m.Chapters is { Count: > 0 } c ? Url(c[Math.Min(n, c.Count - 1)].Thumb) : m.Backdrop is { } b ? Url(b) : null;

    string Language(string tag)
    {
        try { return CultureInfo.GetCultureInfo(tag).DisplayName; }
        catch (CultureNotFoundException) { return tag; }
    }

    string? Duration(double? sec) => sec is > 0 ? Filters.Duration((decimal)sec.Value, Project.Locale) : null;
    static string? Join(params string?[] parts) => parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() is { Count: > 0 } l ? string.Join(" · ", l) : null;

    /// <summary>What an item puts on the disc: every disc path its record mentions, each counted once.</summary>
    long Bytes(DiscItem item)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Walk(p.Value); break;
                case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) Walk(x); break;
                case JsonValueKind.String when _draft!.Files.ContainsKey(e.GetString()!): paths.Add(e.GetString()!); break;
            }
        }
        Walk(JsonSerializer.SerializeToElement(item, InlayJson.Default.DiscItem));
        // Apps are copied as whole folders: their files are not all named in the record.
        if (item is App && _draft!.Groups.GetValueOrDefault(item.Id)?.Root is { } root)
            foreach (var (disk, job) in _draft.Files)
                if (job is CopyJob c && SourceRel(c.Source)?.StartsWith(root + "/", StringComparison.Ordinal) == true) paths.Add(disk);
        return paths.Sum(p => _draft!.Files[p].EstimateBytes);
    }

    /// <summary>The single grey note (§3.2) when a file will be prepared to play everywhere.</summary>
    string? Note(DiscItem item)
    {
        var media = item switch
        {
            Movie m => [m.Media],
            Series s => s.Seasons.SelectMany(x => x.Episodes).Select(e => e.Media),
            Album a => a.Tracks.Select(t => t.Media),
            _ => Enumerable.Empty<MediaFile>(),
        };
        var note = media.Select(f => _draft!.Files.GetValueOrDefault(f.File)).OfType<MediaJob>().SelectMany(j => j.Plan.Notes).FirstOrDefault();
        return note is null ? null : T(note);
    }

    string? SourceOf(string disk) => _draft?.Files.GetValueOrDefault(disk) switch
    {
        CopyJob c => c.Source, MediaJob m => m.Source, VttJob v => v.Source, ImageJob i => i.Source, FrameJob f => f.Source, IconJob ic => ic.Source,
        _ => null,
    };

    string? SourceRel(string full) => _analysis?.Files.FirstOrDefault(f => string.Equals(f.FullPath, full, StringComparison.OrdinalIgnoreCase))?.Rel;

    IEnumerable<string> RelsOfRow(string rowId)
    {
        var parts = rowId.Split('~');
        if (_manifest is null || _draft is null) yield break;
        if (parts.Length > 1)
        {
            var movie = _manifest.Items.OfType<Movie>().FirstOrDefault(m => m.Id == parts[0]);
            var file = parts[1] switch
            {
                "trailer" => movie?.Trailer?.File,
                "extra" => movie?.Extras?.ElementAtOrDefault(int.Parse(parts[2], CultureInfo.InvariantCulture))?.File,
                "sub" => movie?.Media.Subtitles?.Where(s => s.File is not null).ElementAtOrDefault(int.Parse(parts[2], CultureInfo.InvariantCulture))?.File,
                _ => null,
            };
            if (file is not null && SourceOf(file) is { } src && SourceRel(src) is { } rel) yield return rel;
            yield break;
        }
        if (_draft.Groups.GetValueOrDefault(rowId) is not { } g) yield break;
        if (g.Root is { } root) { yield return root; yield break; }
        foreach (var f in g.Main.Concat(g.Extras).Concat(g.Sidecars).Concat(g.Booklet)
                     .Concat(new[] { g.Trailer, g.Cover, g.Backdrop, g.Logo, g.Installer, g.Portable }.OfType<SourceFile>()))
            yield return f.Rel;
    }

    // ---------------- capacity ----------------

    CapacityView? CapacityOf()
    {
        if (OutputDraft() is not { } draft) return null;
        var used = draft.ContentBytes + Reserved;
        var media = draft.Manifest.Disc.Media;
        var usable = MediaCapacity.Usable(media);
        var suggestions = Capacity.Suggest(draft, Reserved).Select((s, i) => new SuggestionView(i, s.Kind switch
        {
            SuggestionKind.BiggerMedia => T("builder.suggest.media", T("builder.media." + s.Media.ToString()!.ToLowerInvariant())),
            SuggestionKind.DropOriginals => T("builder.suggest.originals", Size(s.SavedBytes)),
            SuggestionKind.DropTranscodedCopies => T("builder.suggest.copies", Size(s.SavedBytes)),
            SuggestionKind.DropTrailer => T("builder.suggest.trailer", Size(s.SavedBytes)),
            _ => T("builder.suggest.extras", Size(s.SavedBytes)),
        })).ToList();
        var needs = used > usable && MediaCapacity.Smallest(used) is { } bigger && bigger > media ? bigger.ToString().ToLowerInvariant() : null;
        return new CapacityView(used, media.ToString().ToLowerInvariant(), usable, used <= usable, Math.Max(0, used - usable), needs, suggestions);
    }

    /// <summary>Decimal units, as discs are sold ("4,7 GB", "86 MB").</summary>
    string Size(long bytes)
    {
        var c = CultureInfo.GetCultureInfo(Project.Locale);
        return bytes >= 1_000_000_000 ? (bytes / 1e9).ToString("0.0#", c) + " GB" : Math.Max(1, (long)Math.Round(bytes / 1e6)).ToString(c) + " MB";
    }

    DriveView Drive(Recorder r)
    {
        var name = $"{r.Letter} {r.Name}".Trim();
        if (r.Disc is not { } d) return new DriveView(r.Id, r.Letter, name, T("builder.drive.empty"), false, false, false, 0, null);
        var media = Recorders.Name(d.Type) is { Length: > 0 } known ? known : T("builder.drive.disc"); // pressed or unknown media
        var status = !d.Writable ? T("builder.drive.notWritable", media)
            : d.Blank ? T("builder.drive.blank", media, Size(d.FreeBytes))
            : T("builder.drive.free", media, Size(d.FreeBytes));
        var fits = d.Type switch
        {
            MediaType.CdR or MediaType.CdRw => DiscMedia.Cd700,
            MediaType.DvdPlusRDualLayer or MediaType.DvdDashRDualLayer or MediaType.DvdPlusRwDualLayer => DiscMedia.Dvd9,
            MediaType.Unknown => (DiscMedia?)null,
            _ => DiscMedia.Dvd5,
        };
        var readable = !d.Blank && r.Letter.Length > 0 && new DriveInfo(r.Letter + "\\").IsReady;
        var erasable = d.Type is MediaType.CdRw or MediaType.DvdDashRw && !d.Blank;
        return new DriveView(r.Id, r.Letter, name, status, d.Writable, readable, erasable, d.FreeBytes, fits);
    }

    // ---------------- Disk menüsü ----------------

    MenuView? Menu()
    {
        if (_manifest is null) return null;
        var featured = _manifest.Items.FirstOrDefault(i => i.Id == _manifest.Disc.Featured) ?? _manifest.Items.FirstOrDefault();
        var hero = featured switch { Movie m => m.Backdrop ?? m.Cover, Series s => s.Backdrop ?? s.Cover, App a => a.Backdrop ?? a.Cover, Gallery g => g.Photos.FirstOrDefault()?.Thumb, _ => featured?.Cover };
        var autoplay = featured switch { Album => "builder.home.autoplay.album", Gallery => "builder.home.autoplay.gallery", Series => "builder.home.autoplay.series", _ => "builder.home.autoplay.movie" };
        return new MenuView(Templates(hero), Project.Autoplay, T(autoplay), _manifest.Disc.Title, _manifest.Disc.Label,
            Project.Icon is not null || featured?.Cover is not null ? PreviewOrigin + "~icon?r=" + _rev : null, Project.Icon is null,
            Project.Remote, DiscOrigin + ThemeRenderer.UiRoot + "index.html?r=" + _rev, hero is null ? null : Url(hero));
    }

    IReadOnlyList<TemplateView> Templates(string? hero)
    {
        var mode = DiscKind() switch { "movie" => ThemeMode.Movie, "music" => ThemeMode.Album, "photo" => ThemeMode.Gallery, "app" => ThemeMode.App, _ => ThemeMode.Mixed };
        var mixed = _manifest!.Disc.Home.View == HomeView.Menu;
        var current = _manifest.Theme.Package;
        var variant = Project.Settings.TryGetValue("variant", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var cards = new List<TemplateView>();
        var thumb = hero is null ? null : Url(hero);
        foreach (var t in ThemeFiles.AllStandard().Select(t => t.Theme).Where(t => mixed ? t.Modes.Contains(ThemeMode.Mixed) : t.Modes[0] == mode))
        {
            var selected = Project.UserTheme is null && t.Id == current;
            if (t.Settings?.GetValueOrDefault("variant") is { Type: SettingType.Select, Options: { Count: > 0 } options } setting)
            {
                var chosen = variant ?? setting.Default?.GetString() ?? options[0].Value;
                cards.AddRange(options.Select((o, i) => new TemplateView($"{t.Id}|{o.Value}", o.Label, thumb, selected && o.Value == chosen, i > 0)));
            }
            else cards.Add(new TemplateView(t.Id, t.Name, thumb, selected, false));
        }
        if (Project.UserTheme is not null) cards.Add(new TemplateView(UserThemeId, _theme?.Theme.Name ?? "", thumb, true, false));
        return cards;
    }

    EditorView? Editor()
    {
        if (_theme is null || _ui is null || _manifest is null) return null;
        var resolved = _manifest.Theme.Settings ?? new Dictionary<string, JsonElement>();
        var settings = (_theme.Theme.Settings ?? new Dictionary<string, ThemeSetting>()).Select(kv => new SettingView(kv.Key, kv.Value.Type, kv.Value.Label,
            kv.Value.Help, kv.Value.Group, kv.Value.Options, kv.Value.Min, kv.Value.Max, kv.Value.Step, kv.Value.Unit,
            resolved.TryGetValue(kv.Key, out var chosen) ? chosen : kv.Value.Default)).ToList();
        var sections = _manifest.Sections.Select(s => new SectionView(s.Id, s.Kind, s.Title, s.Layout, s.Hidden == true, s.Items.Count)).ToList();
        var text = new[] { ".liquid", ".html", ".css", ".js", ".json", ".svg", ".txt" };
        var files = _theme.Files.Keys.Where(k => text.Any(x => k.EndsWith(x, StringComparison.OrdinalIgnoreCase))).Order(StringComparer.Ordinal).ToList();
        return new EditorView(Project.UserTheme is not null, _theme.Theme.Name, files, settings, sections,
            _ui.Issues.Select(i => new IssueView(i.Severity, i.Rule, i.Message)).ToList(), _ui.Blocking);
    }

    /// <summary>The data panel: the manifest exactly as the template sees it.</summary>
    public string Data() => _manifest is null ? "{}" : JsonSerializer.Serialize(_manifest, InlayJson.Default.DiscManifest);

    // ---------------- preview files ----------------

    /// <summary>What the preview frame loads from <see cref="DiscOrigin"/> (and thumbnails from <see cref="PreviewOrigin"/>):
    /// the hosted index.html the launcher shows, with the SDK booted against a stand-in launcher (preview.ts), its assets,
    /// and each disc file from its source. Files a job would convert are converted on first request into a preview cache.</summary>
    public async Task<PreviewFile?> Preview(string path, CancellationToken ct = default)
    {
        path = Uri.UnescapeDataString(path.Split('?')[0]);
        if (_ui is null || _manifest is null) return null;
        if (path == ThemeRenderer.UiRoot + "index.html")
        {
            // First thing in <head>: window.disc must exist before the template's own scripts (the CSP allows 'self').
            var html = _ui.IndexHtml;
            var head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase) is var h and >= 0 ? html.IndexOf('>', h) + 1 : 0;
            return new PreviewFile(Encoding.UTF8.GetBytes(html.Insert(head, $"<script src=\"/{PreviewSdk}\"></script>")), null, "text/html");
        }
        if (path == PreviewSdk)
        {
            var boot = $"window.__inlayBoot={{\"manifest\":{JsonSerializer.Serialize(_manifest, InlayJson.Default.DiscManifest)},\"hosted\":true,\"preview\":true}};\n";
            return new PreviewFile(Encoding.UTF8.GetBytes(boot + Sdk.Script), null, "text/javascript");
        }
        if (path == "Menu.html") return new PreviewFile(Encoding.UTF8.GetBytes(_ui.FallbackHtml), null, "text/html");
        if (path.StartsWith(ThemeRenderer.UiRoot, StringComparison.Ordinal) && _ui.Assets.TryGetValue(path[ThemeRenderer.UiRoot.Length..], out var asset))
            return new PreviewFile(asset, null, Mime(path));
        if (path == "~icon")
        {
            var cover = (_manifest?.Items.FirstOrDefault(i => i.Id == _manifest.Disc.Featured) ?? _manifest?.Items.FirstOrDefault())?.Cover;
            var icon = Project.Icon ?? (cover is null ? null : SourceOf(cover));
            return icon is not null && File.Exists(icon) ? new PreviewFile(null, icon, Mime(icon)) : null;
        }
        if (OutputDraft()?.Files.GetValueOrDefault(path) is not { } job) return null;
        var cache = System.IO.Path.Combine(InlayFolders.Root, "preview", Project.Id, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        switch (job)
        {
            case CopyJob c: return new PreviewFile(null, c.Source, Mime(c.Source));
            case MediaJob m: return new PreviewFile(null, m.Source, Mime(m.Source));
            case BytesJob b: return new PreviewFile(b.Content, null, Mime(path));
        }
        if (!File.Exists(cache))
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache)!);
            var queue = new FfmpegQueue();
            try
            {
                switch (job)
                {
                    case ImageJob i: await Task.Run(() => ImagePipeline.WriteFitted(i.Source, cache, Math.Min(i.Max, 1280u), widthOnly: i.WidthOnly), ct); break;
                    case FrameJob f: await queue.Run(FfmpegArgs.Frame(f.Source, f.AtSec, Math.Min(f.Max, 1280u), cache), null, ct: ct); break;
                    case VttJob v: await queue.Run(FfmpegArgs.ToVtt(v.Source, v.Stream, cache), null, ct: ct); break;
                    case IconJob ic: if (!LocalMeta.ExtractIcon(ic.Source, cache)) Inlay.Imaging.DiscIcon.WriteDefaultPng(cache); break;
                }
            }
            catch (Exception e) when (e is MediaException or IOException or ImageMagick.MagickException) { return null; }
        }
        return File.Exists(cache) ? new PreviewFile(null, cache, Mime(path)) : null;
    }

    static string Mime(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html", ".css" => "text/css", ".js" => "text/javascript", ".json" => "application/json", ".svg" => "image/svg+xml",
        ".webp" => "image/webp", ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".gif" => "image/gif", ".avif" => "image/avif",
        ".ico" => "image/x-icon", ".woff2" => "font/woff2", ".vtt" => "text/vtt", ".mp4" or ".m4v" => "video/mp4", ".webm" => "video/webm",
        ".mkv" => "video/x-matroska", ".mp3" => "audio/mpeg", ".m4a" => "audio/mp4", ".flac" => "audio/flac", ".opus" or ".ogg" => "audio/ogg",
        ".wav" => "audio/wav", ".txt" => "text/plain", ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };
}

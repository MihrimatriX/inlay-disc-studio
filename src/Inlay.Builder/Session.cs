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

/// <summary>One open project: the user's choices (<see cref="DiscProject"/>), what analysis found, metadata matches,
/// the composed draft with the user's edits applied, and its rendered menu. Every change rebuilds only as far back
/// as it has to (edit → render, match → compose, content → analyze) and then publishes a fresh <see cref="View"/>.</summary>
public sealed partial class Session
{
    enum Stage { Render, Compose, Lookup, Analyze }

    readonly Prefs _prefs;
    readonly KeyStore _keys;
    readonly MetaHttp _http;
    readonly Func<IReadOnlyList<Recorder>> _listDrives;
    static readonly HttpClient Twitch = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly Stack<DiscProject> _undo = new(), _redo = new();

    public DiscProject Project { get; private set; }
    /// <summary>Where the user saved the project; null until "Kaydet" (changes still autosave to <see cref="AutoPath"/>).</summary>
    public string? Path { get; private set; }
    string AutoPath => System.IO.Path.Combine(InlayFolders.Root, "projects", Project.Id + DiscProject.Extension);

    AnalysisResult? _analysis;
    DiscDraft? _draft;
    DiscManifest? _manifest;
    ThemeFiles? _theme;
    RenderedUi? _ui;
    readonly Dictionary<string, MovieMeta> _movies = [];
    readonly Dictionary<string, GameMeta> _games = [];
    readonly Dictionary<string, MatchCard> _cards = [];
    readonly Dictionary<string, ThemeFiles> _standard = [];
    IReadOnlyList<Recorder> _drives = [];
    string _phase = "empty";
    double? _progress;
    string? _error;
    int _rev;
    CancellationTokenSource? _work;

    /// <summary>Raised with a fresh view after every change (the host posts it to the UI as the "state" event).</summary>
    public event Action<View>? Changed;

    public Session(Prefs prefs, KeyStore keys, HttpClient? http = null, Func<IReadOnlyList<Recorder>>? drives = null)
    {
        _prefs = prefs;
        _keys = keys;
        _http = new MetaHttp(http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        _listDrives = drives ?? Recorders.List;
        Project = Fresh();
    }

    DiscProject Fresh() => new()
    {
        KeepOriginals = _prefs.KeepOriginals, MaxCompatibility = _prefs.MaxCompatibility, Encoder = _prefs.Encoder, Locale = _prefs.Locale,
    };

    string T(string key, params object[] args) => args.Length == 0 ? Text.Get(key, Project.Locale) : string.Format(CultureInfo.GetCultureInfo(Project.Locale), Text.Get(key, Project.Locale), args);

    // ---------------- project ----------------

    public void New()
    {
        _work?.Cancel();
        Project = Fresh();
        Path = null;
        (_analysis, _draft, _manifest, _ui, _theme) = (null, null, null, null, null);
        _movies.Clear(); _games.Clear(); _cards.Clear(); _undo.Clear(); _redo.Clear();
        (_phase, _progress, _error) = ("empty", null, null);
        Publish();
    }

    public async Task Open(string path)
    {
        New();
        Project = DiscProject.Load(path);
        Path = path.StartsWith(System.IO.Path.Combine(InlayFolders.Root, "projects"), StringComparison.OrdinalIgnoreCase) ? null : path;
        _prefs.Opened(path);
        await Rebuild(Stage.Analyze);
    }

    public void Save(string? path = null)
    {
        path ??= Path ?? AutoPath;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (Project.Name.Length == 0) Project.Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Project.Save(path);
        if (path != AutoPath) { Path = path; _prefs.Opened(path); }
        Publish();
    }

    /// <summary>"Kaynağı yeniden bağla": a moved source folder points to its new place; relative keys stay valid.</summary>
    public Task Relink(string old, string now) => Mutate(p => p.Sources = p.Sources.Select(s => string.Equals(s, old, StringComparison.OrdinalIgnoreCase) ? now : s).ToList(), Stage.Analyze);

    public Task Undo() => Step(_undo, _redo);
    public Task Redo() => Step(_redo, _undo);

    async Task Step(Stack<DiscProject> from, Stack<DiscProject> to)
    {
        if (from.Count == 0) return;
        var before = Project;
        to.Push(Project.Clone());
        Project = from.Pop();
        await Rebuild(Needed(before, Project));
    }

    /// <summary>How far back a change between two project states has to rebuild.</summary>
    static Stage Needed(DiscProject a, DiscProject b)
    {
        static string J(object o) => JsonSerializer.Serialize(o);
        if (J(a.Sources) != J(b.Sources) || J(a.Excluded) != J(b.Excluded) || J(a.Kinds) != J(b.Kinds)) return Stage.Analyze;
        if (J(a.Matches) != J(b.Matches)) return Stage.Lookup;
        if (a.KeepOriginals != b.KeepOriginals || a.MaxCompatibility != b.MaxCompatibility || a.Encoder != b.Encoder || a.Media != b.Media
            || a.Title != b.Title || a.Theme != b.Theme || (a.UserTheme is null) != (b.UserTheme is null)) return Stage.Compose;
        return Stage.Render;
    }

    async Task Mutate(Action<DiscProject> change, Stage stage)
    {
        _undo.Push(Project.Clone());
        if (_undo.Count > 100) { var keep = _undo.Take(100).Reverse().ToList(); _undo.Clear(); keep.ForEach(_undo.Push); }
        _redo.Clear();
        change(Project);
        await Rebuild(stage);
    }

    // ---------------- content ----------------

    public Task Add(IEnumerable<string> paths) => Mutate(p =>
    {
        foreach (var path in paths.Select(System.IO.Path.GetFullPath))
            if (!p.Sources.Contains(path, StringComparer.OrdinalIgnoreCase)) p.Sources.Add(path);
        if (p.Name.Length == 0 && p.Sources.Count > 0) p.Name = System.IO.Path.GetFileName(p.Sources[0].TrimEnd('\\', '/'));
    }, Stage.Analyze);

    /// <summary>Rows of "Disk içeriği": an item, or one of a film's parts (trailer, extra, subtitle file).</summary>
    public Task Remove(IEnumerable<string> rowIds)
    {
        var rels = rowIds.SelectMany(RelsOfRow).Distinct().ToList();
        return rels.Count == 0 ? Task.CompletedTask : Mutate(p => p.Excluded.AddRange(rels.Where(r => !p.Excluded.Contains(r))), Stage.Analyze);
    }

    /// <summary>§5.2 correction from the row's context menu.</summary>
    public Task SetKind(string itemId, ContentKind kind)
    {
        if (_draft?.Groups.GetValueOrDefault(itemId) is not { } g) return Task.CompletedTask;
        var rels = g.Root is { } root ? [root] : g.Main.Select(f => f.Rel).ToList();
        return Mutate(p => { foreach (var r in rels) p.Kinds[r] = kind; }, Stage.Analyze);
    }

    /// <summary>"Bu film hangisi?": a candidate, or the user's own title/year/poster.</summary>
    public Task Match(string itemId, MatchChoice choice)
    {
        if (_draft?.Groups.GetValueOrDefault(itemId) is not { } g) return Task.CompletedTask;
        var key = Key(g);
        _movies.Remove(key); _games.Remove(key);
        return Mutate(p => p.Matches[key] = choice, Stage.Lookup);
    }

    /// <summary>One of the "sığmıyor" suggestions (§3.5): bigger medium, or something left off the disc.</summary>
    public Task Apply(int suggestion)
    {
        if (_draft is null) return Task.CompletedTask;
        var list = Capacity.Suggest(OutputDraft()!);
        if (suggestion < 0 || suggestion >= list.Count) return Task.CompletedTask;
        var s = list[suggestion];
        switch (s.Kind)
        {
            case SuggestionKind.BiggerMedia: return Mutate(p => p.Media = s.Media, Stage.Compose);
            case SuggestionKind.DropOriginals: return Mutate(p => p.KeepOriginals = false, Stage.Compose);
            case SuggestionKind.DropTranscodedCopies: return Mutate(p => p.MaxCompatibility = false, Stage.Compose);
            case SuggestionKind.DropTrailer: return Remove([$"{s.ItemId}~trailer"]);
            case SuggestionKind.DropExtras:
                var movie = _manifest!.Items.OfType<Movie>().Single(m => m.Id == s.ItemId);
                return Remove(Enumerable.Range(0, movie.Extras?.Count ?? 0).Select(i => $"{s.ItemId}~extra~{i}"));
            default: return Task.CompletedTask;
        }
    }

    // ---------------- menu and theme ----------------

    /// <summary>Template card: "package" or "package|variant" (the theme's "variant" select, e.g. Sinema/Sade).</summary>
    public Task Template(string card)
    {
        var (package, variant) = card.Split('|') is [var a, var b] ? (a, b) : (card, null);
        return Mutate(p =>
        {
            if (package == UserThemeId) return;
            p.UserTheme = null;
            p.Theme = package;
            if (variant is null) p.Settings.Remove("variant");
            else p.Settings["variant"] = JsonSerializer.SerializeToElement(variant);
        }, Stage.Compose);
    }

    public Task SetMenu(MenuEdit e) => Mutate(p =>
    {
        if (e.Autoplay is { } a) p.Autoplay = a;
        if (e.Remote is { } r) p.Remote = r;
        if (e.Title is { } t) p.Title = t.Trim().Length == 0 ? null : t.Trim();
        if (e.Label is { } l) p.Label = l.Trim().Length == 0 ? null : DiscLabel.FromTitle(l);
        if (e.Icon is { } i) p.Icon = i.Length == 0 ? null : i;
    }, e.Title is not null ? Stage.Compose : Stage.Render);

    public Task SetSetting(string key, JsonElement value) => Mutate(p =>
    {
        if (p.Settings.TryGetValue(key, out var old) && old.ValueKind == JsonValueKind.String) p.Files.Remove(old.GetString()!);
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) p.Settings.Remove(key); else p.Settings[key] = value.Clone();
    }, Stage.Render);

    public Task SetSections(IReadOnlyList<SectionEdit> sections) => Mutate(p => p.Sections = sections.ToList(), Stage.Render);

    /// <summary>F2: the item's title on the disc (the menu, the title bar of its page).</summary>
    public Task Rename(string itemId, string title)
    {
        if (_draft?.Groups.GetValueOrDefault(itemId) is not { } g || title.Trim().Length == 0) return Task.CompletedTask;
        return Mutate(p => p.Renames[Key(g)] = title.Trim(), Stage.Render);
    }

    /// <summary>An image setting: the picture goes on the disc under .disc/media/ and the setting points at it.</summary>
    public Task SetImage(string key, string source)
    {
        var disk = $".disc/media/{Names.DiscSegment(key, 40).ToLowerInvariant()}{System.IO.Path.GetExtension(source).ToLowerInvariant()}";
        if (!RelPath.IsValid(disk) || !File.Exists(source)) throw new ArgumentException(source);
        return Mutate(p =>
        {
            p.Files[disk] = System.IO.Path.GetFullPath(source);
            p.Settings[key] = JsonSerializer.SerializeToElement(disk);
        }, Stage.Render);
    }

    public const string UserThemeId = "user";

    /// <summary>"Kopyala ve düzenle": the current theme goes into the project, renamed, and becomes editable.</summary>
    public Task CopyTheme()
    {
        var theme = _theme ?? throw new InvalidOperationException("no theme");
        var files = theme.Files.ToDictionary(f => f.Key, f => f.Value);
        using var doc = JsonDocument.Parse(files["theme.json"]);
        var json = System.Text.Json.Nodes.JsonNode.Parse(doc.RootElement.GetRawText())!.AsObject();
        json["id"] = "user." + Names.DiscSegment(theme.Theme.Id.Split('.')[^1], 40).ToLowerInvariant();
        json["name"] = T("builder.theme.copyName", theme.Theme.Name);
        json.Remove("$schema");
        files["theme.json"] = Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        return Mutate(p => p.UserTheme = Encode(files), Stage.Compose);
    }

    public string ReadThemeFile(string path) => Encoding.UTF8.GetString((_theme ?? throw new InvalidOperationException("no theme")).Files[path]);

    /// <summary>Code mode save. A broken theme.json is refused (it would leave nothing to render); Liquid errors are
    /// allowed and show up as lint errors that block burning.</summary>
    public Task WriteThemeFile(string path, string text)
    {
        if (Project.UserTheme is null) throw new InvalidOperationException("copy the theme first");
        if (!RelPath.IsValid(path)) throw new ArgumentException(path);
        var files = Decode(Project.UserTheme);
        files[path] = Encoding.UTF8.GetBytes(text);
        ThemeFiles.FromFiles(files); // validates theme.json
        return Mutate(p => p.UserTheme = Encode(files), path == "theme.json" ? Stage.Compose : Stage.Render);
    }

    public Task ImportTheme(string disctheme)
    {
        using var s = File.OpenRead(disctheme);
        var theme = ThemeFiles.FromZip(s);
        return Mutate(p => p.UserTheme = Encode(theme.Files), Stage.Compose);
    }

    public void ExportTheme(string disctheme)
    {
        using var s = File.Create(disctheme);
        (_theme ?? throw new InvalidOperationException("no theme")).SaveZip(s);
    }

    static Dictionary<string, string> Encode(IReadOnlyDictionary<string, byte[]> files) => files.ToDictionary(f => f.Key, f => Convert.ToBase64String(f.Value));
    static Dictionary<string, byte[]> Decode(Dictionary<string, string> files) => files.ToDictionary(f => f.Key, f => Convert.FromBase64String(f.Value), StringComparer.OrdinalIgnoreCase);

    // ---------------- preferences and drives ----------------

    public void SetPrefs(PrefsEdit e)
    {
        if (e.Accent is { } a) _prefs.Accent = a;
        if (e.KeepOriginals is { } k) _prefs.KeepOriginals = k;
        if (e.MaxCompatibility is { } m) _prefs.MaxCompatibility = m;
        if (e.Encoder is { } enc) _prefs.Encoder = enc;
        if (e.UdfRevision is { } u && DiscImage.UdfRevisions.Contains(u)) _prefs.UdfRevision = u;
        if (e.Speed is { } sp) _prefs.Speed = sp;
        if (e.Verify is { } v) _prefs.Verify = v;
        if (e.Recorder is { } r) _prefs.Recorder = r.Length == 0 ? null : r;
        _prefs.Save();
        // Advanced options that change how files are prepared apply to the open project too.
        if (e.KeepOriginals is not null || e.MaxCompatibility is not null || e.Encoder is not null)
            _ = Mutate(p => (p.KeepOriginals, p.MaxCompatibility, p.Encoder) = (_prefs.KeepOriginals, _prefs.MaxCompatibility, _prefs.Encoder), Stage.Compose);
        else Publish();
    }

    public static readonly string[] KeyNames = ["tmdb", "igdb.clientId", "igdb.clientSecret", "steamgriddb"];

    public Task SetKey(string name, string? value)
    {
        if (!KeyNames.Contains(name)) throw new ArgumentException(name);
        _keys.Set(name, value?.Trim());
        _cards.Clear();
        return _analysis is null ? Task.CompletedTask : Rebuild(Stage.Lookup);
    }

    public async Task RefreshDrives()
    {
        _drives = await Task.Run(_listDrives);
        Publish();
    }

    public IReadOnlyList<Recorder> Drives => _drives;

    // ---------------- rebuild ----------------

    async Task Rebuild(Stage stage)
    {
        _work?.Cancel();
        var cts = _work = new CancellationTokenSource();
        var ct = cts.Token;
        _error = null;
        try
        {
            if (Project.Sources.Count == 0)
            {
                (_analysis, _draft, _manifest, _ui, _phase) = (null, null, null, null, "empty");
                Publish();
                return;
            }
            if (stage >= Stage.Analyze || _analysis is null)
            {
                (_phase, _progress) = ("analyzing", 0);
                Publish();
                var progress = new Progress<double>(v => { if (!ct.IsCancellationRequested) { _progress = v * 0.8; Publish(); } });
                var roots = Project.Sources.Where(s => Directory.Exists(s) || File.Exists(s)).ToList();
                _analysis = await Task.Run(() => Analyzer.Run(roots, Project.Kinds, progress, ct, Project.Excluded), ct);
            }
            if (stage >= Stage.Lookup || _draft is null)
            {
                _progress = 0.85;
                Publish();
                await Lookup(ct);
            }
            if (stage >= Stage.Compose || _draft is null) await Task.Run(Compose, ct);
            Edit();
            await Task.Run(Render, ct);
            (_phase, _progress) = ("ready", null);
            if (_draft is not null) Autosave();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ThemeException or MediaException or InvalidDataException)
        {
            (_phase, _progress, _error) = (_draft is null ? "empty" : "ready", null, e.Message);
        }
        Publish();
    }

    void Autosave()
    {
        try { Save(); }
        catch (IOException e) { _error = e.Message; } // a read-only target: the project stays open, the next change retries
    }

    static string Key(ContentGroup g) => g.Root ?? g.Main.FirstOrDefault()?.Rel ?? g.Title;

    async Task Lookup(CancellationToken ct)
    {
        _cards.Clear();
        var tmdbKey = _keys.Get("tmdb");
        var tmdb = string.IsNullOrEmpty(tmdbKey) ? null : new Tmdb(_http, tmdbKey, Project.Locale);
        var (igdbId, igdbSecret, gridKey) = (_keys.Get("igdb.clientId"), _keys.Get("igdb.clientSecret"), _keys.Get("steamgriddb"));
        var igdb = string.IsNullOrEmpty(igdbId) || string.IsNullOrEmpty(igdbSecret) ? null : new Igdb(_http, Twitch, igdbId, igdbSecret);
        var grid = string.IsNullOrEmpty(gridKey) ? null : new SteamGridDb(_http, gridKey);
        var steam = new SteamStore(_http);

        foreach (var g in _analysis!.Groups)
        {
            var key = Key(g);
            var choice = Project.Matches.GetValueOrDefault(key);
            try
            {
                if (g.Kind is ContentKind.Movie or ContentKind.Series)
                {
                    if (_movies.ContainsKey(key)) continue;
                    if (choice is { Provider: null }) { _movies[key] = Manual(choice); continue; }
                    if (choice is { Id: { } id } && tmdb is not null)
                    {
                        if (await Fetch(tmdb, g, id, ct) is { } chosen) _movies[key] = chosen;
                        continue;
                    }
                    if (g.Sidecars.Any(s => s.Type == FileType.Nfo)) continue; // Kodi .nfo: the composer reads it
                    var question = g.Kind == ContentKind.Movie ? "movie" : "series";
                    if (tmdb is null) { _cards[key] = new MatchCard(question, [], true); continue; }
                    var found = g.Kind == ContentKind.Movie ? await tmdb.SearchMovie(g.Title, g.Year, ct) : await tmdb.SearchShow(g.Title, ct);
                    var same = found.Where(c => Same(c.Title, g.Title) && (g.Year is null || c.Year == g.Year)).ToList();
                    if (same.Count == 1 && await Fetch(tmdb, g, same[0].Id, ct) is { } meta) _movies[key] = meta;
                    else _cards[key] = new MatchCard(question, found.Take(3).ToList(), found.Count == 0);
                }
                else if (g.Kind == ContentKind.App && g.IsGame)
                {
                    if (_games.ContainsKey(key)) continue;
                    if (choice is { Provider: null }) { _games[key] = new GameMeta { Title = choice.Title ?? g.Title, Year = choice.Year, Cover = choice.Poster }; continue; }
                    var candidate = choice is { Id: { } gid } ? new MatchCandidate("igdb", gid, choice.Title ?? g.Title, choice.Year, null, null) : null;
                    if (candidate is null && igdb is not null)
                    {
                        var found = await igdb.Search(g.Title, ct);
                        var same = found.Where(c => Same(c.Title, g.Title)).ToList();
                        if (same.Count == 1) candidate = same[0];
                        else if (found.Count > 0) { _cards[key] = new MatchCard("game", found.Take(3).ToList(), false); }
                    }
                    if (await GameLookup.Resolve(candidate, g.Title, igdb, steam, grid, ct) is { } game) _games[key] = game;
                }
            }
            catch (HttpRequestException) { } // offline: local information only; the next change tries again
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
            catch (JsonException) { }
        }
    }

    static async Task<MovieMeta?> Fetch(Tmdb tmdb, ContentGroup g, string id, CancellationToken ct) => g.Kind == ContentKind.Movie
        ? await tmdb.Movie(id, ct)
        : await tmdb.Show(id, g.Episodes.Select(e => e.Season).Distinct(), ct);

    static MovieMeta Manual(MatchChoice c) => new() { Title = c.Title ?? "", Year = c.Year, Poster = c.Poster, Provider = MetaProvider.Manual };

    static bool Same(string a, string b) => string.Equals(Fold(a), Fold(b), StringComparison.Ordinal);
    static string Fold(string s) => new(s.Normalize(NormalizationForm.FormD).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    string? UserThemePackage => Project.UserTheme is { } files && files.TryGetValue("theme.json", out var b64)
        ? JsonDocument.Parse(Convert.FromBase64String(b64)).RootElement.GetProperty("id").GetString() : null;

    void Compose()
    {
        var groups = _analysis!.Groups;
        _draft = Composer.Compose(_analysis, new ComposeOptions
        {
            Title = Project.Title,
            Media = Project.Media,
            Locale = Project.Locale,
            Plan = new PlanOptions { KeepOriginals = Project.KeepOriginals, MaxCompatibility = Project.MaxCompatibility, Encoder = Project.Encoder },
            Movies = groups.Where(g => _movies.ContainsKey(Key(g))).ToDictionary(g => g, g => _movies[Key(g)]),
            Games = groups.Where(g => _games.ContainsKey(Key(g))).ToDictionary(g => g, g => _games[Key(g)]),
            ThemePackage = UserThemePackage ?? Project.Theme,
        });
        if (Project.Name.Length == 0) Project.Name = _draft.Manifest.Disc.Title;
    }

    /// <summary>The user's menu edits on top of the composed manifest (cheap: no file jobs change).</summary>
    void Edit()
    {
        var m = _draft!.Manifest;
        var sections = m.Sections;
        if (Project.Sections is { } edits)
        {
            var byId = m.Sections.ToDictionary(s => s.Id);
            sections = edits.Where(e => byId.ContainsKey(e.Id))
                .Select(e => byId[e.Id] with { Title = e.Title ?? byId[e.Id].Title, Layout = e.Layout ?? byId[e.Id].Layout, Hidden = e.Hidden ? true : null })
                .Concat(m.Sections.Where(s => edits.All(e => e.Id != s.Id))).ToList();
        }
        var items = m.Items.Select(i => _draft.Groups.TryGetValue(i.Id, out var g) && Project.Renames.TryGetValue(Key(g), out var name) ? i with { Title = name } : i).ToList();
        _manifest = m with
        {
            Items = items,
            Disc = m.Disc with { Label = Project.Label ?? m.Disc.Label, Autoplay = Project.Autoplay ? true : null, Remote = Project.Remote ? null : false },
            Theme = m.Theme with { Settings = Project.Settings.Count > 0 ? new Dictionary<string, JsonElement>(Project.Settings) : null },
            Sections = sections,
        };
    }

    void Render()
    {
        var package = _manifest!.Theme.Package;
        if (Project.UserTheme is { } user) _theme = ThemeFiles.FromFiles(Decode(user));
        else
        {
            if (!_standard.TryGetValue(package, out var t)) _standard[package] = t = ThemeFiles.Standard(package);
            _theme = t;
        }
        _ui = ThemeRenderer.Render(_manifest, _theme, Sdk.Script);
        _rev++;
    }

    /// <summary>The draft that burning materializes: composed files with the edited manifest.</summary>
    DiscDraft? OutputDraft()
    {
        if (_draft is null) return null;
        if (Project.Files.Count == 0) return _draft with { Manifest = _manifest! };
        var files = new Dictionary<string, FileJob>(_draft.Files, StringComparer.OrdinalIgnoreCase);
        foreach (var (disk, source) in Project.Files.Where(f => File.Exists(f.Value))) files[disk] = new CopyJob(source, new FileInfo(source).Length);
        return _draft with { Manifest = _manifest!, Files = files };
    }

    void Publish() => Changed?.Invoke(View());
}

public sealed record MatchCard(string Question, IReadOnlyList<MatchCandidate> Candidates, bool NotFound);
public sealed record MenuEdit(bool? Autoplay = null, bool? Remote = null, string? Title = null, string? Label = null, string? Icon = null);
public sealed record PrefsEdit(string? Accent = null, bool? KeepOriginals = null, bool? MaxCompatibility = null, VideoEncoder? Encoder = null,
    int? UdfRevision = null, int? Speed = null, bool? Verify = null, string? Recorder = null);

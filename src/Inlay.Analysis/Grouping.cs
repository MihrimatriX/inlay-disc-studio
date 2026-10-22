using System.Text.RegularExpressions;
using Inlay.Media;

namespace Inlay.Analysis;

public enum ContentKind { Movie, Series, Album, Gallery, App, Prerequisite, Document }

/// <summary>One thing on the disc before metadata: which source files make it up and what role each plays.</summary>
public sealed class ContentGroup
{
    public required ContentKind Kind { get; set; }
    public required string Title { get; set; }
    public int? Year { get; set; }
    /// <summary>0..1, how sure the classifier is (§5.2); the UI offers a correction when it is low.</summary>
    public double Confidence { get; set; }
    /// <summary>Movie: [main]. Series: episodes. Album: tracks. Gallery: photos and clips. App: installers/exe. Document: [file].</summary>
    public List<SourceFile> Main { get; } = [];
    /// <summary>Movie: trailer first, then extras.</summary>
    public List<SourceFile> Extras { get; } = [];
    public SourceFile? Trailer { get; set; }
    /// <summary>Subtitles, lyrics, .nfo, .cue that belong to Main files.</summary>
    public List<SourceFile> Sidecars { get; } = [];
    public List<SourceFile> Booklet { get; } = [];
    public SourceFile? Cover { get; set; }
    public SourceFile? Backdrop { get; set; }
    public SourceFile? Logo { get; set; }
    /// <summary>App: the folder copied whole (DLLs, data), relative like <see cref="SourceFile.Rel"/>.</summary>
    public string? Root { get; set; }
    public List<SourceFile> Tree { get; } = [];
    public SourceFile? Installer { get; set; }
    public SourceFile? Portable { get; set; }
    public bool IsGame { get; set; }
    /// <summary>Series: season/episode of each Main file, same order.</summary>
    public List<EpisodeRef> Episodes { get; } = [];
}

/// <summary>Per-file facts the classifier reads; the analyzer fills them from ffprobe/TagLib.</summary>
public sealed record FileFacts(MediaInfo? Media, AudioTags? Tags);

/// <summary>§5.2 classifiers: files → content groups. User corrections (§5.2 "Kullanıcı … düzeltebilir") force a kind
/// for a file or a whole folder before the automatic rules run.</summary>
public static partial class Grouping
{
    public const double MovieMinSec = 15 * 60, ClipMaxSec = 2 * 60;

    static readonly string[] CoverNames = ["cover", "folder", "front", "poster", "albumart", "albumartsmall"];
    static readonly string[] BackdropNames = ["fanart", "backdrop", "background", "hero", "banner"];
    static readonly string[] LogoNames = ["logo", "clearlogo"];
    static readonly string[] HelperExe = ["unins", "uninstall", "crash", "redist", "dxsetup", "vc_redist", "vcredist", "launcherhelper", "unitycrashhandler", "dotnet"];

    [GeneratedRegex(@"\b(trailer|fragman|teaser)\b", RegexOptions.IgnoreCase)] private static partial Regex TrailerName();
    [GeneratedRegex(@"\b(extras?|ekstra(lar)?|featurettes?|behind[ ._-]the[ ._-]scenes|kamera[ ._-]arkas[ıi]|making[ ._-]of|deleted|sample)\b", RegexOptions.IgnoreCase)] private static partial Regex ExtraName();
    [GeneratedRegex(@"\b(setup|install|installer|kurulum)\b|^setup_|\.msi$", RegexOptions.IgnoreCase)] private static partial Regex InstallerName();
    [GeneratedRegex(@"\b(game|games|oyun|oyunlar)\b", RegexOptions.IgnoreCase)] private static partial Regex GameFolder();

    public static List<ContentGroup> Group(IReadOnlyList<SourceFile> files, Func<SourceFile, FileFacts> facts,
        IReadOnlyDictionary<string, ContentKind>? overrides = null)
    {
        overrides ??= new Dictionary<string, ContentKind>();
        var used = new HashSet<SourceFile>();
        var groups = new List<ContentGroup>();

        ContentKind? Forced(SourceFile f) => overrides.Where(o => f.Rel == o.Key || f.Rel.StartsWith(o.Key + "/", StringComparison.Ordinal))
            .OrderByDescending(o => o.Key.Length).Select(o => (ContentKind?)o.Value).FirstOrDefault();
        IEnumerable<SourceFile> Free(Func<SourceFile, bool> pick) => files.Where(f => !used.Contains(f) && pick(f));

        Apps(files, Free(f => Forced(f) is null or ContentKind.App).ToList(), used, groups, forced: Free(f => Forced(f) == ContentKind.App).ToList());
        Series(Free(f => f.Type == FileType.Video && (Forced(f) == ContentKind.Series || (Forced(f) is null && Names.Episode(f.Rel) is not null))).ToList(), files, used, groups);
        Movies(Free(f => f.Type == FileType.Video && Forced(f) is null or ContentKind.Movie).ToList(), files, facts, used, groups, forced: f => Forced(f) == ContentKind.Movie);
        Albums(Free(f => f.Type == FileType.Audio && Forced(f) is null or ContentKind.Album).ToList(), files, facts, used, groups);
        Galleries(Free(f => (f.Type == FileType.Image || f.Type == FileType.Video) && Forced(f) is null or ContentKind.Gallery).ToList(), used, groups);
        foreach (var f in Free(f => f.Type is FileType.Document or FileType.Other || Forced(f) == ContentKind.Document))
        {
            used.Add(f);
            groups.Add(new ContentGroup { Kind = ContentKind.Document, Title = f.Stem, Confidence = f.Type == FileType.Document ? 0.9 : 0.4, Main = { f } });
        }
        return groups;
    }

    // ---------------- apps (§5.2 app row, §5.3 app/game) ----------------

    static void Apps(IReadOnlyList<SourceFile> all, List<SourceFile> free, HashSet<SourceFile> used, List<ContentGroup> groups, List<SourceFile> forced)
    {
        var prerequisites = new HashSet<SourceFile>();
        foreach (var p in free.Where(f => f.Type == FileType.Program && LocalMeta.IsPrerequisite(f.Name)))
        {
            used.Add(p);
            prerequisites.Add(p);
            groups.Add(new ContentGroup { Kind = ContentKind.Prerequisite, Title = p.Stem, Confidence = 1, Main = { p }, Installer = p });
        }

        var programs = free.Where(f => f.Type == FileType.Program && !used.Contains(f)).Concat(forced.Where(f => f.Type == FileType.Program)).Distinct().ToList();
        var installers = programs.Where(f => InstallerName().IsMatch(f.Name) || f.Ext == ".msi").ToList();
        var dllFolders = free.Where(f => f.Type == FileType.Library).Select(f => f.Folder).ToHashSet();
        // A portable program is an exe next to its DLLs (or a Unity/Unreal layout); helpers never count.
        var portables = programs.Except(installers)
            .Where(f => !HelperExe.Any(h => f.Name.Contains(h, StringComparison.OrdinalIgnoreCase)))
            .Where(f => dllFolders.Contains(f.Folder) || all.Any(x => x.Rel.StartsWith(f.Folder + "/" + f.Stem + "_Data/", StringComparison.OrdinalIgnoreCase)) || forced.Contains(f))
            .GroupBy(f => f.Folder).Select(g => g.MaxBy(f => f.Size)!).ToList();

        // A playable exe inside an installer's folder is the same program's "play from disc" path (§7.0), not another app.
        var installerRoots = installers.Select(f => f.Folder).Distinct().ToList();
        var roots = installerRoots.Concat(portables.Select(f => f.Folder).Where(pf => !installerRoots.Any(r => r == pf || pf.StartsWith(r + "/", StringComparison.Ordinal))))
            .Distinct().OrderBy(r => r.Length).ToList();
        // Installers in a folder whose ancestor holds another installer still form their own app (Setup/Game, Setup/Editor).
        foreach (var root in roots)
        {
            // Redistributables inside a program folder stay in its copy: its installer may expect them at that relative path.
            var inRoot = all.Where(f => (!used.Contains(f) || prerequisites.Contains(f)) && Under(f, root) && !roots.Any(r => r != root && r.Length > root.Length && Under(f, r))).ToList();
            if (inRoot.Count == 0) continue;
            var installer = installers.Where(i => i.Folder == root).OrderBy(i => i.Ext == ".msi" ? 1 : 0).ThenBy(i => i.Name.Length).FirstOrDefault();
            var portable = portables.Where(p => p.Folder == root || p.Folder.StartsWith(root + "/", StringComparison.Ordinal))
                .Where(p => !roots.Any(r => r != root && r.Length > root.Length && (p.Folder == r || p.Folder.StartsWith(r + "/", StringComparison.Ordinal))))
                .OrderBy(p => p.Folder.Length).FirstOrDefault();
            if (installer is null && portable is null) continue;
            var title = Names.Game(installer?.Name ?? "") is { Length: > 0 } fromSetup && installer!.Ext != ".msi" && !installer.Stem.Equals("setup", StringComparison.OrdinalIgnoreCase)
                ? fromSetup : Names.Game(root.Split('/')[^1]) is { Length: > 0 } fromFolder ? fromFolder : (portable ?? installer)!.Stem;
            var g = new ContentGroup
            {
                Kind = ContentKind.App, Title = title, Root = root, Installer = installer, Portable = portable,
                IsGame = IsGame(inRoot, root), Confidence = installer is not null ? 0.95 : 0.75,
            };
            g.Tree.AddRange(inRoot);
            g.Main.AddRange(new[] { installer, portable }.OfType<SourceFile>());
            foreach (var f in inRoot) used.Add(f);
            groups.Add(g);
        }
    }

    static bool IsGame(IReadOnlyList<SourceFile> tree, string root) =>
        tree.Any(f => f.Name.Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase)
                      || f.Name.StartsWith("steam_api", StringComparison.OrdinalIgnoreCase)
                      || f.Name.StartsWith("goggame-", StringComparison.OrdinalIgnoreCase)
                      || f.Rel.Contains("/Engine/Binaries/", StringComparison.OrdinalIgnoreCase)
                      || Regex.IsMatch(f.Rel, @"/[^/]+_Data/", RegexOptions.IgnoreCase))
        || root.Split('/').Any(seg => GameFolder().IsMatch(seg));

    // ---------------- series (§7.0 episode patterns) ----------------

    static void Series(List<SourceFile> videos, IReadOnlyList<SourceFile> all, HashSet<SourceFile> used, List<ContentGroup> groups)
    {
        foreach (var show in videos.GroupBy(v => SeriesKey(v), StringComparer.OrdinalIgnoreCase))
        {
            var episodes = show.Select(v => (File: v, Ep: Names.Episode(v.Rel) ?? new EpisodeRef(1, 0))).OrderBy(e => e.Ep.Season).ThenBy(e => e.Ep.Episode).ToList();
            var root = CommonFolder(episodes.Select(e => e.File));
            var g = new ContentGroup { Kind = ContentKind.Series, Title = show.Key, Confidence = episodes.Count >= 2 ? 0.95 : 0.7 };
            g.Main.AddRange(episodes.Select(e => e.File));
            g.Episodes.AddRange(episodes.Select(e => e.Ep));
            foreach (var e in episodes) used.Add(e.File);
            AttachArt(g, all, used, root, episodes.Select(e => e.File.Stem));
            AttachSubtitles(g, all, used, episodes.Select(e => e.File));
            groups.Add(g);
        }
    }

    static string SeriesKey(SourceFile f)
    {
        var fromName = Names.SeriesTitle(f.Name);
        if (fromName.Length > 0) return fromName;
        // "Ofis/Sezon 1/Bölüm 3.mp4": the series is the folder above the season folders.
        var dirs = f.Folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var show = dirs.Reverse().FirstOrDefault(d => !Regex.IsMatch(d, @"^(sezon|season|s)\s*\d+$", RegexOptions.IgnoreCase));
        return show ?? f.Stem;
    }

    // ---------------- movies (§5.2 movie row) ----------------

    static void Movies(List<SourceFile> videos, IReadOnlyList<SourceFile> all, Func<SourceFile, FileFacts> facts, HashSet<SourceFile> used,
        List<ContentGroup> groups, Func<SourceFile, bool> forced)
    {
        double Dur(SourceFile f) => facts(f).Media?.DurationSec ?? 0;
        bool Nfo(SourceFile f) => all.Any(x => x.Type == FileType.Nfo && x.Folder == f.Folder);
        bool Year(SourceFile f) => Names.Movie(f.Name).Year is not null;
        bool PhotosBeside(SourceFile f) => all.Any(x => x.Type == FileType.Image && x.Folder == f.Folder && !IsArt(x));
        bool IsExtra(SourceFile f) => TrailerName().IsMatch(f.Stem) || ExtraName().IsMatch(f.Rel);

        // Short clips next to photos belong to the gallery (§5.2 gallery row).
        var candidates = videos.Where(v => forced(v) || !(Dur(v) < ClipMaxSec && PhotosBeside(v))).ToList();
        var mains = candidates.Where(v => !IsExtra(v) && (forced(v) || Dur(v) >= MovieMinSec || Nfo(v) || Year(v))).ToList();
        // A folder with nothing feature-length still has a main video: the longest non-extra.
        foreach (var folder in candidates.GroupBy(v => v.Folder).Where(g => !g.Any(mains.Contains)))
            if (folder.Where(v => !IsExtra(v)).MaxBy(Dur) is { } longest) mains.Add(longest);

        foreach (var main in mains.OrderBy(m => m.Rel, StringComparer.Ordinal))
        {
            var parsed = Names.Movie(main.Name);
            var folderTitle = Names.Movie(main.Folder.Split('/')[^1]);
            var title = main.Stem.Length <= 3 || main.Stem.Equals("movie", StringComparison.OrdinalIgnoreCase) ? folderTitle : parsed;
            var year = title.Year ?? folderTitle.Year;
            var g = new ContentGroup
            {
                Kind = ContentKind.Movie, Title = title.Title.Length > 0 ? title.Title : main.Stem, Year = year,
                Confidence = Math.Round(Math.Min(1, 0.4 + (Dur(main) >= MovieMinSec ? 0.3 : 0) + (Nfo(main) ? 0.2 : 0) + (year is not null ? 0.1 : 0)), 2),
                Main = { main },
            };
            used.Add(main);
            groups.Add(g);
        }

        // Trailers and extras go to the movie in the same folder (or the parent folder, for Extras/ subfolders).
        foreach (var v in candidates.Where(v => !used.Contains(v)))
        {
            var owner = groups.Where(g => g.Kind == ContentKind.Movie)
                .Where(g => v.Folder == g.Main[0].Folder || v.Folder.StartsWith(g.Main[0].Folder + "/", StringComparison.Ordinal))
                .OrderByDescending(g => g.Main[0].Folder.Length).ThenByDescending(g => Prefix(v.Stem, g.Main[0].Stem)).FirstOrDefault();
            if (owner is null) continue;
            used.Add(v);
            if (owner.Trailer is null && TrailerName().IsMatch(v.Stem)) owner.Trailer = v;
            else owner.Extras.Add(v);
        }
        foreach (var g in groups.Where(g => g.Kind == ContentKind.Movie && g.Main.Count == 1 && used.Contains(g.Main[0])))
        {
            if (g.Cover is not null || g.Backdrop is not null) continue;
            AttachArt(g, all, used, g.Main[0].Folder, [g.Main[0].Stem]);
            AttachSubtitles(g, all, used, g.Main);
            g.Sidecars.AddRange(all.Where(x => x.Type == FileType.Nfo && x.Folder == g.Main[0].Folder && used.Add(x)));
        }
    }

    // ---------------- albums (§5.2 album row) ----------------

    static void Albums(List<SourceFile> audio, IReadOnlyList<SourceFile> all, Func<SourceFile, FileFacts> facts, HashSet<SourceFile> used, List<ContentGroup> groups)
    {
        foreach (var album in audio.GroupBy(a => AlbumKey(a, facts(a).Tags)))
        {
            var tracks = album.OrderBy(t => facts(t).Tags?.Disc ?? 1).ThenBy(t => facts(t).Tags?.Track ?? int.MaxValue).ThenBy(t => t.Rel, StringComparer.Ordinal).ToList();
            var tags = tracks.Select(t => facts(t).Tags).OfType<AudioTags>().ToList();
            var albumName = tags.Select(t => t.Album).FirstOrDefault(a => a is not null);
            var folder = CommonFolder(tracks);
            var g = new ContentGroup
            {
                Kind = ContentKind.Album,
                Title = albumName ?? (folder.Length > 0 ? folder.Split('/')[^1] : tracks[0].Stem),
                Year = tags.Select(t => t.Year).FirstOrDefault(y => y is not null),
                Confidence = albumName is not null && tracks.Count >= 3 ? 0.95 : albumName is not null ? 0.75 : 0.5,
            };
            g.Main.AddRange(tracks);
            foreach (var t in tracks) used.Add(t);
            AttachArt(g, all, used, folder, []);
            g.Sidecars.AddRange(all.Where(x => (x.Type is FileType.Lyrics or FileType.Cue) && tracks.Any(t => t.Folder == x.Folder) && used.Add(x)));
            g.Booklet.AddRange(all.Where(x => !used.Contains(x) && (x.Ext == ".pdf" && x.Folder == folder
                || x.Type == FileType.Image && Regex.IsMatch(x.Folder, @"/(booklet|scans?|kitapc[ıi]k)$", RegexOptions.IgnoreCase) && x.Folder.StartsWith(folder, StringComparison.Ordinal))).ToList());
            foreach (var b in g.Booklet) used.Add(b);
            groups.Add(g);
        }
    }

    static string AlbumKey(SourceFile f, AudioTags? t) =>
        t?.Album is { } album ? $"tag:{(t.AlbumArtist ?? t.Artist ?? "").ToLowerInvariant()}|{album.ToLowerInvariant()}" : $"dir:{f.Folder}";

    // ---------------- galleries (§5.2 gallery row) ----------------

    static void Galleries(List<SourceFile> media, HashSet<SourceFile> used, List<ContentGroup> groups)
    {
        // Art (poster, cover, fanart) was claimed by its movie/album already; whatever image is left is a photo.
        foreach (var folder in media.Where(m => !used.Contains(m)).GroupBy(m => m.Folder))
        {
            var photos = folder.Where(m => m.Type == FileType.Image).ToList();
            var g = new ContentGroup
            {
                Kind = ContentKind.Gallery, Title = folder.Key.Length > 0 ? folder.Key.Split('/')[^1] : folder.First().Stem,
                Confidence = photos.Count >= 5 ? 0.9 : 0.6,
            };
            g.Main.AddRange(folder.OrderBy(m => m.Rel, StringComparer.Ordinal));
            foreach (var m in folder) used.Add(m);
            groups.Add(g);
        }
    }

    // ---------------- helpers ----------------

    static bool IsArt(SourceFile f) => f.Type == FileType.Image &&
        (CoverNames.Concat(BackdropNames).Concat(LogoNames).Any(n => f.Stem.Equals(n, StringComparison.OrdinalIgnoreCase)
            || f.Stem.EndsWith("-" + n, StringComparison.OrdinalIgnoreCase)) || Regex.IsMatch(f.Stem, @"^season\d+-poster$", RegexOptions.IgnoreCase));

    static void AttachArt(ContentGroup g, IReadOnlyList<SourceFile> all, HashSet<SourceFile> used, string folder, IEnumerable<string> stems)
    {
        var art = all.Where(f => f.Type == FileType.Image && f.Folder == folder && !used.Contains(f)).ToList();
        SourceFile? Find(string[] names) => art.FirstOrDefault(f => names.Any(n => f.Stem.Equals(n, StringComparison.OrdinalIgnoreCase)
            || stems.Any(s => f.Stem.Equals($"{s}-{n}", StringComparison.OrdinalIgnoreCase))));
        g.Cover ??= Find(CoverNames);
        g.Backdrop ??= Find(BackdropNames);
        g.Logo ??= Find(LogoNames);
        foreach (var f in new[] { g.Cover, g.Backdrop, g.Logo }.OfType<SourceFile>()) used.Add(f);
    }

    static void AttachSubtitles(ContentGroup g, IReadOnlyList<SourceFile> all, HashSet<SourceFile> used, IEnumerable<SourceFile> videos)
    {
        var vids = videos.ToList();
        foreach (var s in all.Where(f => f.Type == FileType.Subtitle && !used.Contains(f)))
        {
            var owner = vids.Where(v => v.Folder == s.Folder || s.Folder.StartsWith(v.Folder + "/", StringComparison.Ordinal))
                .MaxBy(v => Prefix(s.Stem, v.Stem));
            if (owner is null || (vids.Count > 1 && Prefix(s.Stem, owner.Stem) < owner.Stem.Length)) continue;
            used.Add(s);
            g.Sidecars.Add(s);
        }
    }

    static int Prefix(string a, string b)
    {
        var n = 0;
        while (n < a.Length && n < b.Length && char.ToLowerInvariant(a[n]) == char.ToLowerInvariant(b[n])) n++;
        return n;
    }

    static bool Under(SourceFile f, string root) => root.Length == 0 || f.Rel.StartsWith(root + "/", StringComparison.Ordinal);

    static string CommonFolder(IEnumerable<SourceFile> files)
    {
        var folders = files.Select(f => f.Folder.Split('/')).ToList();
        var common = folders[0].AsEnumerable();
        foreach (var f in folders.Skip(1)) common = common.Zip(f).TakeWhile(p => p.First == p.Second).Select(p => p.First).ToList();
        return string.Join('/', common);
    }
}

using Inlay.Media;

namespace Inlay.Analysis;

public enum FileType { Video, Audio, Image, Subtitle, Lyrics, Cue, Nfo, Program, Library, Document, Other }

/// <summary>A source file. <see cref="Rel"/> starts with the dropped folder's own name ("Kuzey Rüzgârı/film.mkv"),
/// '/' separated, so folder names stay available as titles.</summary>
public sealed record SourceFile(string FullPath, string Rel, long Size)
{
    public string Name => Path.GetFileName(FullPath);
    public string Stem => Path.GetFileNameWithoutExtension(FullPath);
    public string Ext => Path.GetExtension(FullPath).ToLowerInvariant();
    public string Folder => Rel.Contains('/') ? Rel[..Rel.LastIndexOf('/')] : "";
    public FileType Type => Scanner.TypeOf(Ext);
}

/// <summary>§5.1: walk the dropped folders, skipping hidden/system files and OS clutter.</summary>
public static class Scanner
{
    static readonly HashSet<string> Clutter = new(StringComparer.OrdinalIgnoreCase)
        { "Thumbs.db", "desktop.ini", ".DS_Store", "ehthumbs.db", "$RECYCLE.BIN", "System Volume Information", "__MACOSX" };

    static readonly Dictionary<string, FileType> Types = new()
    {
        [".mp4"] = FileType.Video, [".m4v"] = FileType.Video, [".mkv"] = FileType.Video, [".webm"] = FileType.Video, [".avi"] = FileType.Video,
        [".mov"] = FileType.Video, [".wmv"] = FileType.Video, [".mpg"] = FileType.Video, [".mpeg"] = FileType.Video, [".ts"] = FileType.Video,
        [".m2ts"] = FileType.Video, [".mts"] = FileType.Video, [".vob"] = FileType.Video, [".flv"] = FileType.Video, [".ogv"] = FileType.Video,
        [".3gp"] = FileType.Video, [".rm"] = FileType.Video, [".rmvb"] = FileType.Video, [".divx"] = FileType.Video,
        [".mp3"] = FileType.Audio, [".m4a"] = FileType.Audio, [".aac"] = FileType.Audio, [".flac"] = FileType.Audio, [".opus"] = FileType.Audio,
        [".ogg"] = FileType.Audio, [".oga"] = FileType.Audio, [".wav"] = FileType.Audio, [".wma"] = FileType.Audio, [".ape"] = FileType.Audio,
        [".wv"] = FileType.Audio, [".tta"] = FileType.Audio, [".dsf"] = FileType.Audio, [".dff"] = FileType.Audio, [".mpc"] = FileType.Audio,
        [".aif"] = FileType.Audio, [".aiff"] = FileType.Audio,
        [".srt"] = FileType.Subtitle, [".ass"] = FileType.Subtitle, [".ssa"] = FileType.Subtitle, [".vtt"] = FileType.Subtitle,
        [".lrc"] = FileType.Lyrics, [".cue"] = FileType.Cue, [".nfo"] = FileType.Nfo,
        [".exe"] = FileType.Program, [".msi"] = FileType.Program, [".dll"] = FileType.Library,
        [".pdf"] = FileType.Document, [".txt"] = FileType.Document, [".md"] = FileType.Document, [".docx"] = FileType.Document,
        [".doc"] = FileType.Document, [".odt"] = FileType.Document, [".rtf"] = FileType.Document, [".epub"] = FileType.Document,
        [".xlsx"] = FileType.Document, [".pptx"] = FileType.Document,
    };

    public static FileType TypeOf(string ext) =>
        Types.TryGetValue(ext, out var t) ? t : ImagePipeline.IsImage("x" + ext) ? FileType.Image : FileType.Other;

    public static IReadOnlyList<SourceFile> Scan(IEnumerable<string> roots)
    {
        var files = new List<SourceFile>();
        foreach (var root in roots.Select(Path.GetFullPath))
        {
            if (File.Exists(root))
            {
                var fi = new FileInfo(root);
                if (Keep(fi)) files.Add(new SourceFile(fi.FullName, fi.Name, fi.Length));
                continue;
            }
            var dir = new DirectoryInfo(root);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                ReturnSpecialDirectories = false,
            };
            foreach (var fi in dir.EnumerateFiles("*", options))
            {
                var rel = Path.GetRelativePath(dir.Parent?.FullName ?? dir.FullName, fi.FullName).Replace('\\', '/');
                if (rel.Split('/').Any(Clutter.Contains) || !Keep(fi)) continue;
                files.Add(new SourceFile(fi.FullName, rel, fi.Length));
            }
        }
        return files.OrderBy(f => f.Rel, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), true)).ToList();
    }

    static bool Keep(FileInfo f) => !Clutter.Contains(f.Name) && !f.Name.StartsWith("._", StringComparison.Ordinal) && f.Length > 0;
}

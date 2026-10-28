using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Inlay.Analysis;

public sealed record ParsedTitle(string Title, int? Year);
public sealed record EpisodeRef(int Season, int Episode);

/// <summary>File/folder name → title (§5.3), episode numbers (§7.0) and clean game names (§5.3 step 1).</summary>
public static partial class Names
{
    // Release-name noise after the title: resolution, source, codecs, audio, groups.
    [GeneratedRegex(@"\b(2160p|1080p|720p|576p|480p|4k|uhd|hdr10?|dv|bluray|blu-ray|brrip|bdrip|web-?dl|webrip|hdtv|dvdrip|remux|x26[45]|h\.?26[45]|hevc|avc|aac|ac3|dts|truehd|atmos|ddp?5\.1|10bit|proper|repack|extended|unrated|internal|multi|dual)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();

    [GeneratedRegex(@"[\[(]?\b((?:19|20)\d{2})\b[\])]?")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"\bS(\d{1,2})[ ._-]?E(\d{1,3})\b|\b(\d{1,2})x(\d{2,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SxE();

    [GeneratedRegex(@"\b(?:B[öo]l[üu]m|Episode|Ep)[ ._-]?(\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeWord();

    [GeneratedRegex(@"\b(?:Sezon|Season|S)[ ._-]?(\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonWord();

    public static ParsedTitle Movie(string fileOrFolderName)
    {
        var name = Path.GetFileNameWithoutExtension(fileOrFolderName);
        name = name.Replace('.', ' ').Replace('_', ' ');
        int? year = null;
        var years = YearPattern().Matches(name);
        // The last plausible year wins ("2001 A Space Odyssey 1968" → 1968); a lone leading year is part of the title.
        var y = years.LastOrDefault(m => m.Index > 0);
        if (y is not null && int.Parse(y.Groups[1].Value, CultureInfo.InvariantCulture) is var yy && yy is >= 1880 and <= 2100)
        {
            year = yy;
            name = name[..y.Index];
        }
        name = Noise().Replace(name, "");
        return new ParsedTitle(Tidy(name), year);
    }

    /// <summary>S01E03, 1x03, "Sezon 1/Bölüm 3" (folder gives the season), "Bölüm 3" alone (season 1).</summary>
    public static EpisodeRef? Episode(string relativePath)
    {
        var file = Path.GetFileNameWithoutExtension(relativePath);
        if (SxE().Match(file) is { Success: true } m)
            return m.Groups[1].Success
                ? new EpisodeRef(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
                : new EpisodeRef(int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture));
        if (EpisodeWord().Match(file) is { Success: true } e)
        {
            var dirs = relativePath.Replace('\\', '/').Split('/')[..^1];
            var season = dirs.Select(d => SeasonWord().Match(d)).LastOrDefault(s => s.Success);
            return new EpisodeRef(season is null ? 1 : int.Parse(season.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(e.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        return null;
    }

    /// <summary>Series name from an episode file name: everything before the episode marker.</summary>
    public static string SeriesTitle(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).Replace('.', ' ').Replace('_', ' ');
        var m = SxE().Match(name);
        if (!m.Success) m = EpisodeWord().Match(name);
        return Tidy(m.Success ? name[..m.Index] : name);
    }

    [GeneratedRegex(@"(?:^|[\s_.-])(setup|install(er)?|kurulum|gog|fitgirl|dodi|repack|elamigos|codex|plaza|skidrow|portable|x64|x86|win(32|64)?|v?\d+(\.\d+){1,3}[a-z]?)(?=$|[\s_.-])", RegexOptions.IgnoreCase)]
    private static partial Regex GameNoise();

    /// <summary>"setup_the_witcher_3_v4.04_(gog)" → "The Witcher 3" (§5.3 step 1).</summary>
    public static string Game(string name)
    {
        // "Hollow.Knight.v1.5.78-GOG" has no extension: only strip real installer/archive ones.
        var s = Path.GetExtension(name).ToLowerInvariant() is ".exe" or ".msi" or ".zip" or ".rar" or ".7z" or ".iso"
            ? Path.GetFileNameWithoutExtension(name) : name;
        s = Regex.Replace(s, @"[\[(][^\])]*[\])]", " ");
        s = Regex.Replace(s, @"(?<=^|[\s_.-])v?\d+(\.\d+){1,3}[a-z]?(?=$|[\s_-])", " ", RegexOptions.IgnoreCase); // versions, before dots go
        s = s.Replace('_', ' ').Replace('.', ' ');
        string prev;
        do { prev = s; s = GameNoise().Replace(s, " "); } while (s != prev);
        s = Regex.Replace(s, @"\s+-\s*$", "");
        var tidy = Tidy(s);
        return tidy.Length > 0 && tidy.All(ch => !char.IsLetter(ch) || char.IsLower(ch)) ? TitleCase(tidy) : tidy;
    }

    /// <summary>Manifest id from a title: "Kuzey Rüzgârı" → "mv-kuzey-ruzgari" (pattern ^[a-z0-9][a-z0-9._-]{0,63}$).</summary>
    public static string Id(string prefix, string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.Replace('ı', 'i').Replace('İ', 'I').Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9')) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        var id = $"{prefix}-{(slug.Length == 0 ? "x" : slug)}";
        return id.Length > 64 ? id[..64].TrimEnd('-') : id;
    }

    /// <summary>A file/folder name that Joliet (64 chars) and UDF accept; Turkish letters stay.</summary>
    public static string DiscSegment(string name, int max = 64)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['*', '?', ':', '"', '<', '>', '|', ';']).ToHashSet();
        var clean = new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        clean = Regex.Replace(clean, @"\s+", " ").Trim().TrimEnd('.');
        if (clean.Length == 0) clean = "_";
        if (clean.Length <= max) return clean;
        var ext = Path.GetExtension(clean);
        if (ext.Length > 10) ext = "";
        return clean[..(max - ext.Length)].TrimEnd(' ', '.') + ext;
    }

    static string Tidy(string s) => Regex.Replace(s, @"[\s\-–—]+$|^[\s\-–—]+", "").Replace("  ", " ").Trim();

    static string TitleCase(string s) => CultureInfo.GetCultureInfo("tr-TR").TextInfo.ToTitleCase(s);
}

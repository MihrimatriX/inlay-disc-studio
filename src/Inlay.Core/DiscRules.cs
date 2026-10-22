using System.Globalization;
using System.Text;

namespace Inlay.Core;

/// <summary>Planning capacities from app-prompt §8.4. Burn time re-reads the real value from the drive.</summary>
public static class MediaCapacity
{
    public const int SectorSize = 2048;
    public const long FourGiB = 4L * 1024 * 1024 * 1024;

    /// <summary>Share of the medium reserved for file system structures (§8.4).</summary>
    public const double FileSystemOverhead = 0.015;

    public static long Sectors(DiscMedia media) => media switch
    {
        DiscMedia.Cd700 => 360_000,
        DiscMedia.Dvd5 => 2_295_104,
        DiscMedia.Dvd9 => 4_173_824,
        _ => throw new ArgumentOutOfRangeException(nameof(media)),
    };

    public static long Bytes(DiscMedia media) => Sectors(media) * SectorSize;

    /// <summary>Bytes available for content once the file system share is set aside.</summary>
    public static long Usable(DiscMedia media) => (long)(Bytes(media) * (1 - FileSystemOverhead));

    public static bool Fits(long contentBytes, DiscMedia media) => contentBytes <= Usable(media);

    /// <summary>Smallest medium that holds the content, or null if even DVD DL is too small.</summary>
    public static DiscMedia? Smallest(long contentBytes) =>
        Enum.GetValues<DiscMedia>().Cast<DiscMedia?>().FirstOrDefault(m => Fits(contentBytes, m!.Value));
}

public static class DiscLabel
{
    /// <summary>Volume label for UDF/Joliet and autorun: A–Z, 0–9, _; at most 32 characters (§8.3).</summary>
    public static string FromTitle(string title)
    {
        var sb = new StringBuilder();
        // Invariant upper-casing leaves Turkish dotless ı alone; İ is handled by FormD (I + combining dot).
        foreach (var c in title.Replace('ı', 'i').Normalize(NormalizationForm.FormD).ToUpperInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue; // Ü → U, İ → I
            var mapped = c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') ? c : '_';
            if (mapped == '_' && (sb.Length == 0 || sb[^1] == '_')) continue;
            sb.Append(mapped);
        }
        var label = sb.ToString().TrimEnd('_');
        label = label.Length > 32 ? label[..32].TrimEnd('_') : label;
        return label.Length == 0 ? "INLAY_DISC" : label;
    }

    /// <summary>ISO9660 allows 16 characters; the full label lives in Joliet/UDF.</summary>
    public static string Iso9660(string label) => label.Length > 16 ? label[..16].TrimEnd('_') : label;
}

[Flags]
public enum DiscFileSystems { Iso9660 = 1, Joliet = 2, Udf = 4 }

public static class DiscFileSystem
{
    /// <summary>Joliet file name limit in characters (§8.3).</summary>
    public const int JolietMaxName = 64;

    /// <summary>ISO9660 and Joliet cannot store files of 4 GiB or more, so such discs are UDF only (§8.3).</summary>
    public static DiscFileSystems Choose(IEnumerable<long> fileSizes) =>
        fileSizes.Any(s => s >= MediaCapacity.FourGiB)
            ? DiscFileSystems.Udf
            : DiscFileSystems.Iso9660 | DiscFileSystems.Joliet | DiscFileSystems.Udf;

    /// <summary>Disk paths whose last segment is too long for Joliet; only relevant when Joliet is written.</summary>
    public static IEnumerable<string> JolietViolations(IEnumerable<string> diskPaths) =>
        diskPaths.Where(p => p.Split('/').Any(seg => seg.Length > JolietMaxName));
}

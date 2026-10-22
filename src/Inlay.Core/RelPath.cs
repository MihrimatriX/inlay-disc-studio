using System.Text.RegularExpressions;

namespace Inlay.Core;

/// <summary>Disc-root relative path, '/' separated (manifest $defs/relPath).</summary>
public static partial class RelPath
{
    // Same expression as $defs/relPath in manifest.schema.json: no drive, no leading '/', no '..', no URL, no reserved chars.
    [GeneratedRegex("""^(?![a-zA-Z]:)(?!/)(?!.*\.\.)(?!.*://)[^<>:"|?*]+$""")]
    private static partial Regex Pattern();

    public static bool IsValid(string? path) => path is not null && Pattern().IsMatch(path);

    /// <summary>Absolute path of <paramref name="rel"/> under <paramref name="root"/>, or null if it is invalid
    /// or escapes the root after normalization (launcher security rule §9.3.2).</summary>
    public static string? Resolve(string root, string? rel)
    {
        if (!IsValid(rel)) return null;
        // A disc is mounted at a drive root ("E:\"), which keeps its separator: append one only when missing.
        var prefix = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(prefix)) prefix += Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(prefix, rel!.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}

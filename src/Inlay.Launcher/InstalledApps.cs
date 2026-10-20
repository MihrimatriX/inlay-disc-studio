using System.Text.RegularExpressions;
using Inlay.Core;
using Microsoft.Win32;

namespace Inlay.Launcher;

/// <summary>One Add/Remove Programs entry (…\CurrentVersion\Uninstall\*).</summary>
public sealed record UninstallEntry(string? DisplayName, string? Publisher, string? InstallLocation, string? DisplayIcon);

/// <summary>Reads the Uninstall keys. One production implementation; tests substitute it.</summary>
public interface IUninstallRegistry
{
    IEnumerable<UninstallEntry> Entries();
}

/// <summary>HKLM and HKCU, 64- and 32-bit views: where installers register themselves.</summary>
public sealed class WindowsUninstallRegistry : IUninstallRegistry
{
    const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public IEnumerable<UninstallEntry> Entries()
    {
        foreach (var hive in (RegistryHive[])[RegistryHive.LocalMachine, RegistryHive.CurrentUser])
        foreach (var view in (RegistryView[])[RegistryView.Registry64, RegistryView.Registry32])
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var list = root.OpenSubKey(Uninstall);
            if (list is null) continue;
            foreach (var name in list.GetSubKeyNames())
            {
                using var key = list.OpenSubKey(name);
                if (key is null) continue;
                yield return new UninstallEntry(key.GetValue("DisplayName") as string, key.GetValue("Publisher") as string,
                    key.GetValue("InstallLocation") as string, key.GetValue("DisplayIcon") as string);
            }
        }
    }
}

/// <summary>Finds the installed copy of a manifest app, so "Oyna" can start it after an installer-only install (§7.0).</summary>
public static partial class InstalledApps
{
    /// <summary>The main exe of the installed app, or null when no Uninstall entry matches or none of its exes qualifies.</summary>
    public static string? FindExe(App app, IUninstallRegistry registry)
    {
        var title = Norm(app.Title);
        if (title.Length == 0) return null;
        var publisher = Norm(app.Publisher);
        var matches = registry.Entries()
            .Where(e => Norm(e.DisplayName).Contains(title, StringComparison.Ordinal))
            // Both sides naming a publisher and sharing nothing means another vendor's "Title Something".
            .Where(e => publisher.Length == 0 || Norm(e.Publisher) is not { Length: > 0 } p || p.Contains(publisher) || publisher.Contains(p))
            .OrderBy(e => Norm(e.DisplayName).Length); // "Foo" over "Foo Map Editor"
        foreach (var e in matches)
            if ((FromIcon(e.DisplayIcon) ?? FromFolder(e.InstallLocation, title)) is { } exe) return exe;
        return null;
    }

    /// <summary>DisplayIcon is usually <c>"C:\…\game.exe",0</c>.</summary>
    static string? FromIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        var path = IconIndex().Replace(icon.Trim(), "").Trim('"');
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !NotMain(Path.GetFileName(path)) && File.Exists(path) ? path : null;
    }

    /// <summary>The exe in InstallLocation (two levels down at most) whose name is closest to the title, then the shallowest, then the largest.</summary>
    static string? FromFolder(string? dir, string title)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir = dir.Trim().Trim('"'))) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true };
        return new DirectoryInfo(dir).EnumerateFiles("*.exe", options)
            .Where(f => !NotMain(f.Name))
            .OrderByDescending(f => Norm(Path.GetFileNameWithoutExtension(f.Name)) is var n && n == title ? 2 : n.Length > 0 && (title.Contains(n) || n.Contains(title)) ? 1 : 0)
            .ThenBy(f => Path.GetRelativePath(dir, f.FullName).Count(c => c == Path.DirectorySeparatorChar))
            .ThenByDescending(f => f.Length)
            .FirstOrDefault()?.FullName;
    }

    static bool NotMain(string file) => NotMainExe().IsMatch(file);

    /// <summary>Letters and digits only, lower case: "Foo: The Game™" and "FOO THE GAME" compare equal.</summary>
    static string Norm(string? s) => s is null ? "" : string.Concat(s.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    [GeneratedRegex(@",\s*-?\d+\s*$")]
    private static partial Regex IconIndex();

    [GeneratedRegex(@"^(unins|uninst|setup|install|crash|vc_?redist|dxsetup|dotnet|ue4prereq|unitycrashhandler|update)", RegexOptions.IgnoreCase)]
    private static partial Regex NotMainExe();
}

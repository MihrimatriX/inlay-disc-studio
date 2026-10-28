using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;

namespace Inlay.Setup;

/// <summary>A per-user install (no administrator rights): files under %LOCALAPPDATA%\Programs, an entry in
/// Settings › Apps, Start menu (and optionally desktop) shortcuts, the .discproj association. Projects, preferences
/// and keys in %LOCALAPPDATA%\Inlay are the user's and survive updates.</summary>
static class Installer
{
    public const string AppExe = "Inlay.exe", UninstallExe = "Uninstall.exe";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\InlayDiscStudio";
    const string ProgId = "InlayDiscStudio.Project";
    static readonly string Locale = Inlay.Core.Text.DefaultLocale;

    public static string DefaultDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Inlay Disc Studio");
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Inlay");
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Inlay Disc Studio.lnk");
    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Inlay Disc Studio.lnk");

    public static (string Dir, string Version)? Installed()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key?.GetValue("InstallLocation") is string dir && Directory.Exists(dir) ? (dir, key.GetValue("DisplayVersion") as string ?? "") : null;
    }

    /// <summary>Inlay windows still open from <paramref name="dir"/> (files in use cannot be replaced).</summary>
    public static bool Running(string dir) => Process.GetProcessesByName("Inlay").Any(p =>
    {
        try { return p.MainModule?.FileName.StartsWith(dir, StringComparison.OrdinalIgnoreCase) == true; }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    });

    /// <summary>Unpacks next to the target and swaps folders at the end, so a cancelled or failed update leaves the
    /// installed version as it was.</summary>
    public static void Extract(Stream payload, string dir, IProgress<double> progress, CancellationToken ct)
    {
        var fresh = dir + ".new";
        if (Directory.Exists(fresh)) Directory.Delete(fresh, recursive: true);
        try
        {
            using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                var total = Math.Max(1, zip.Entries.Sum(e => e.Length));
                long done = 0;
                var root = Path.GetFullPath(fresh) + Path.DirectorySeparatorChar;
                foreach (var entry in zip.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    var target = Path.GetFullPath(Path.Combine(fresh, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(entry.FullName); // zip-slip
                    if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using (var input = entry.Open())
                    using (var output = File.Create(target))
                    {
                        var buffer = new byte[1 << 20];
                        int n;
                        while ((n = input.Read(buffer)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, n);
                            progress.Report((double)(done += n) / total);
                        }
                    }
                }
            }
            var old = dir + ".old";
            if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
            if (Directory.Exists(dir)) Directory.Move(dir, old);
            Directory.Move(fresh, dir);
            try { if (Directory.Exists(old)) Directory.Delete(old, recursive: true); }
            catch (IOException) { } // a file still mapped somewhere; the next update removes it
        }
        catch
        {
            try { if (Directory.Exists(fresh)) Directory.Delete(fresh, recursive: true); } catch (IOException) { }
            throw;
        }
    }

    public static void Register(string dir, string version, bool desktop)
    {
        var app = Path.Combine(dir, AppExe);
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "Inlay Disc Studio");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "Inlay Disc Studio");
            key.SetValue("DisplayIcon", app + ",0");
            key.SetValue("InstallLocation", dir);
            key.SetValue("UninstallString", $"\"{Path.Combine(dir, UninstallExe)}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{Path.Combine(dir, UninstallExe)}\" --uninstall --quiet");
            key.SetValue("EstimatedSize", (int)(Size(dir) / 1024), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        }
        using (var ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.discproj"))
            ext.SetValue("", ProgId);
        using (var prog = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
        {
            prog.SetValue("", Inlay.Core.Text.Get("setup.projectType", Locale));
            using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue("", app + ",0");
            using (var open = prog.CreateSubKey(@"shell\open\command")) open.SetValue("", $"\"{app}\" \"%1\"");
        }
        Native.Shortcut(StartMenuLink, app, Inlay.Core.Text.Get("setup.description", Locale));
        if (desktop) Native.Shortcut(DesktopLink, app, Inlay.Core.Text.Get("setup.description", Locale));
        Native.SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, 0, 0);
    }

    public static void Remove(string dir, bool userData)
    {
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, throwOnMissingSubKey: false);
        using (var ext = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.discproj", writable: true))
            if (ext?.GetValue("") as string == ProgId) Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\.discproj", throwOnMissingSubKey: false);
        foreach (var lnk in new[] { StartMenuLink, DesktopLink }) File.Delete(lnk);
        Native.SHChangeNotify(0x08000000, 0, 0, 0);
        DeleteWithRetry(dir);
        if (userData) DeleteWithRetry(DataDir);
    }

    static void DeleteWithRetry(string dir)
    {
        for (var i = 0; i < 20 && Directory.Exists(dir); i++)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Thread.Sleep(250); }
        }
    }

    static long Size(string dir) => new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
}

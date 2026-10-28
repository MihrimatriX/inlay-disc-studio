using System.Globalization;
using Microsoft.Win32;

namespace Inlay.Launcher;

/// <summary>SystemInfo (sdk/disc-sdk.d.ts). memoryBytes feeds the game template's compatibility line (§7.2).</summary>
public sealed record SystemInfo(
    string LauncherVersion,
    string WebviewVersion,
    string Os,
    IReadOnlyDictionary<string, long> FreeSpaceBytes,
    long MemoryBytes,
    bool HasLibVlc,
    bool HasHevcExtension,
    bool IsElevated,
    string Locale);

public static class SystemProbe
{
    public static SystemInfo Collect(string root, string webviewVersion) => new(
        typeof(SystemProbe).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        webviewVersion,
        OsName(),
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .ToDictionary(d => d.Name.TrimEnd('\\'), d => d.AvailableFreeSpace),
        Memory(),
        Directory.Exists(Path.Combine(root, ".disc", "player")),
        HasHevc(),
        Environment.IsPrivilegedProcess,
        CultureInfo.CurrentUICulture.Name);

    /// <summary>"Windows 11 23H2": Windows 11 still reports 10.0, the build number tells them apart.</summary>
    static string OsName()
    {
        var build = Environment.OSVersion.Version.Build;
        var display = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", null) as string;
        return $"Windows {(build >= 22000 ? 11 : 10)}{(display is null ? "" : " " + display)}";
    }

    static long Memory()
    {
        var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        return Native.GlobalMemoryStatusEx(ref m) ? (long)m.ullTotalPhys : 0;
    }

    /// <summary>The Store "HEVC Video Extensions" package lets WebView2 decode H.265 natively.</summary>
    static bool HasHevc()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\PackageRepository\Packages");
        return key?.GetSubKeyNames().Any(n => n.StartsWith("Microsoft.HEVCVideoExtension", StringComparison.OrdinalIgnoreCase)) == true;
    }
}

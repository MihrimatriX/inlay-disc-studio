using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Inlay.Core;

namespace Inlay.Launcher;

static class Program
{
    const string WebView2Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    [STAThread]
    static int Main(string[] args)
    {
        // The disc root is the folder Baslat.exe sits in; --root is for development and tests.
        var rootArg = Array.IndexOf(args, "--root");
        var root = Path.GetFullPath(rootArg >= 0 && rootArg + 1 < args.Length ? args[rootArg + 1] : AppContext.BaseDirectory);

        // Headless LibVLC check used by the plugin-pruning tests: plays a disc file with .disc/player only.
        var probe = Array.IndexOf(args, "--probe-media");
        if (probe >= 0 && probe + 1 < args.Length) return ProbeMedia(root, args[probe + 1]);

        var worker = Array.IndexOf(args, "--bundle-worker");
        if (worker >= 0 && worker + 1 < args.Length)
            return BundleRunner.WorkerMain(root, args[worker + 1], args[(worker + 2)..]).GetAwaiter().GetResult();

        Native.CoInitializeEx(0, 0x2 /* COINIT_APARTMENTTHREADED */);
        var disc = DiscLoader.Load(root);
        var locale = disc.Manifest?.Disc.Locale;
        var key = Key(disc.Manifest?.Disc.Label ?? root);

        // §9.1: a second start brings the running window forward instead of opening another.
        using var mutex = new Mutex(true, $"Local\\Inlay.Launcher.{key}", out var first);
        if (!first)
        {
            var hwnd = Native.FindWindowW(LauncherWindow.ClassName(key), null);
            if (hwnd != 0)
            {
                if (Native.IsIconic(hwnd) != 0) Native.ShowWindow(hwnd, Native.SW_RESTORE);
                Native.SetForegroundWindow(hwnd);
            }
            return 0;
        }

        if (Native.GetAvailableCoreWebView2BrowserVersionString(null, out var version) < 0 || version == 0)
            return NoWebView(root, locale);
        Marshal.FreeCoTaskMem(version);

        // §9.2: the disc is read-only; WebView2 keeps its profile under %LOCALAPPDATA%.
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Inlay", "Launcher", key);
        Directory.CreateDirectory(data);
        new LauncherWindow(root, disc, data).Create(key);
        LauncherWindow.RunLoop();
        return 0;
    }

    static int ProbeMedia(string root, string rel)
    {
        var file = RelPath.Resolve(root, rel);
        var dir = Path.Combine(root, ".disc", "player");
        if (file is null || !File.Exists(file) || !Player.VlcSession.Available(dir)) { Console.WriteLine("{\"status\":\"missing\"}"); return 2; }
        using var s = new Player.VlcSession(dir, false, "--vout=dummy", "--aout=dummy");
        s.Play(0, file);
        var st = s.State();
        for (var i = 0; i < 60 && st.PositionSec < 0.4 && st.Status is not ("ended" or "error"); i++) { Thread.Sleep(50); st = s.State(); }
        Console.WriteLine($"{{\"status\":\"{st.Status}\",\"positionSec\":{st.PositionSec.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"audio\":{st.Audio.Count}}}");
        return st.PositionSec >= 0.3 ? 0 : 1;
    }

    /// <summary>Per-disc id for the mutex, window class and WebView2 profile.</summary>
    static string Key(string label) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(label)))[..12];

    /// <summary>§9.2: without the WebView2 Runtime, offer the static menu in the browser or the Runtime download.</summary>
    static int NoWebView(string root, string? locale)
    {
        var choice = Dialog.Ask(0, "Inlay", Text.Get("launcher.noWebView.title", locale), Text.Get("launcher.noWebView.body", locale),
            Text.Get("launcher.noWebView.openInBrowser", locale), Text.Get("launcher.noWebView.download", locale));
        var target = choice switch
        {
            0 => RelPath.Resolve(root, "Menu.html"),
            1 => WebView2Download,
            _ => null,
        };
        if (target is not null && (target == WebView2Download || File.Exists(target)))
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        return 0;
    }
}

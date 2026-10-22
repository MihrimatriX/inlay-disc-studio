using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Inlay.Core;
using static Inlay.Setup.Native;

namespace Inlay.Setup;

/// <summary>InlaySetup.exe installs or updates; the same program without its payload is Uninstall.exe.
/// <c>--quiet [--dir X] [--no-register]</c> installs without dialogs (tests, scripts); <c>--uninstall [--quiet]</c> removes.</summary>
static class Program
{
    static readonly string Version = typeof(Program).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    /// <summary>Turkish, like the builder it installs (Ayarlar can switch the builder later).</summary>
    static readonly string Locale = Text.DefaultLocale;

    static string T(string key, params object[] args) =>
        args.Length == 0 ? Text.Get(key, Locale) : string.Format(CultureInfo.GetCultureInfo(Locale), Text.Get(key, Locale), args);

    [STAThread]
    static int Main(string[] args)
    {
        string? Arg(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        var quiet = args.Contains("--quiet");
        try
        {
            return args.Contains("--uninstall")
                ? Uninstall(Arg("--dir"), quiet, args.Contains("--user-data"), args.Contains("--confirmed"), Arg("--wait"))
                : Install(Arg("--dir"), quiet, args.Contains("--no-register"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (!quiet) Ui.Show(new Page(T("setup.failed.title"), T("setup.failed.content", e.Message), [], TDCBF_CLOSE_BUTTON));
            return 3;
        }
    }

    static int Install(string? dirArg, bool quiet, bool noRegister)
    {
        using var payload = typeof(Program).Assembly.GetManifestResourceStream("payload.zip");
        if (payload is null)
        {
            if (!quiet) Ui.Show(new Page(T("setup.noPayload.title"), T("setup.noPayload.content"), [], TDCBF_CLOSE_BUTTON));
            return 2;
        }
        var installed = Installer.Installed();
        var dir = Path.GetFullPath(dirArg ?? installed?.Dir ?? Installer.DefaultDir);

        if (quiet)
        {
            Installer.Extract(payload, dir, new Progress<double>(), CancellationToken.None);
            if (!noRegister) Installer.Register(dir, Version, desktop: false);
            return 0;
        }

        long size;
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true)) size = zip.Entries.Sum(e => e.Length);
        payload.Position = 0;
        var mb = Math.Round(size / 1e6).ToString("N0", CultureInfo.GetCultureInfo(Locale));

        var first = installed is null
            ? new Page(T("setup.install.title"), T("setup.install.content", dir, mb), [T("setup.install.action")], TDCBF_CANCEL_BUTTON,
                Verify: T("setup.desktop"), VerifyChecked: true, Footer: T("setup.footer", Version))
            : new Page(T("setup.update.title"), T("setup.update.content", installed.Value.Version, Version, dir), [T("setup.update.action")], TDCBF_CANCEL_BUTTON,
                Footer: T("setup.footer", Version));
        var (choice, desktop) = Ui.Show(first);
        if (choice != 0) return 1;
        if (!WaitUntilClosed(dir)) return 1;

        var (_, launch, completed) = Ui.Run(
            new Page(T("setup.progress.title"), T("setup.progress.content"), [], TDCBF_CANCEL_BUTTON),
            (progress, ct) =>
            {
                Installer.Extract(payload, dir, progress, ct);
                if (!noRegister) Installer.Register(dir, Version, desktop && installed is null);
            },
            new Page(T("setup.done.title"), T("setup.done.content"), [], TDCBF_CLOSE_BUTTON, Verify: T("setup.launch"), VerifyChecked: true),
            e => new Page(T("setup.failed.title"), T("setup.failed.content", e.Message), [], TDCBF_CLOSE_BUTTON));
        if (completed && launch) Process.Start(new ProcessStartInfo(Path.Combine(dir, Installer.AppExe)) { UseShellExecute = true });
        return completed ? 0 : 1;
    }

    static bool WaitUntilClosed(string dir)
    {
        while (Installer.Running(dir))
            if (Ui.Show(new Page(T("setup.running.title"), T("setup.running.content"), [T("setup.retry")], TDCBF_CANCEL_BUTTON)).Button != 0) return false;
        return true;
    }

    static int Uninstall(string? dirArg, bool quiet, bool userData, bool confirmed, string? waitFor)
    {
        var dir = Path.GetFullPath(dirArg ?? Installer.Installed()?.Dir ?? Path.GetDirectoryName(Environment.ProcessPath)!);
        if (!confirmed && !quiet)
        {
            var (choice, data) = Ui.Show(new Page(T("setup.uninstall.title"), T("setup.uninstall.content", dir), [T("setup.uninstall.action")],
                TDCBF_CANCEL_BUTTON, Verify: T("setup.uninstall.userData")));
            if (choice != 0) return 1;
            userData = data;
        }
        if (!WaitUntilClosed(dir)) return 1;

        // A program cannot delete the folder it runs from: continue from a copy in %TEMP%.
        var self = Environment.ProcessPath!;
        if (self.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            var copy = Path.Combine(Path.GetTempPath(), $"InlayUninstall-{Guid.NewGuid():N}.exe");
            File.Copy(self, copy);
            var next = new ProcessStartInfo(copy) { UseShellExecute = false };
            foreach (var a in new[] { "--uninstall", "--confirmed", "--dir", dir, "--wait", Environment.ProcessId.ToString(CultureInfo.InvariantCulture) }) next.ArgumentList.Add(a);
            if (quiet) next.ArgumentList.Add("--quiet");
            if (userData) next.ArgumentList.Add("--user-data");
            Process.Start(next);
            return 0;
        }

        if (waitFor is not null && int.TryParse(waitFor, out var pid))
            try { Process.GetProcessById(pid).WaitForExit(15_000); } catch (ArgumentException) { } // already gone

        Installer.Remove(dir, userData);
        if (!quiet) Ui.Show(new Page(T("setup.uninstall.done"), T("setup.uninstall.doneContent"), [], TDCBF_CLOSE_BUTTON));

        // This copy in %TEMP% deletes itself once it has exited.
        if (self.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & del \"{self}\"") { CreateNoWindow = true, UseShellExecute = false });
        return 0;
    }
}

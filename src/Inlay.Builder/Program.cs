using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using Inlay.Analysis.Online;

namespace Inlay.Builder;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Tests and screenshots run against a scratch data folder instead of %LOCALAPPDATA%\Inlay.
        if (Environment.GetEnvironmentVariable("INLAY_ROOT") is { Length: > 0 } root) InlayFolders.Root = root;
        var prefs = Prefs.Load();
        var culture = CultureInfo.GetCultureInfo(prefs.Locale);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;

        var session = new Session(prefs, new KeyStore());
        if (args is ["--iso", var iso, .. var rest] && rest.Length > 0) return Headless(session, iso, rest);

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new MainWindow(session, prefs);
        // "Inlay.exe proje.discproj" (double-click on a project) opens it once the UI is up.
        if (args.Length > 0 && args[0].EndsWith(DiscProject.Extension, StringComparison.OrdinalIgnoreCase) && File.Exists(args[0]))
            window.ContentRendered += async (_, _) => await session.Open(Path.GetFullPath(args[0]));
        return app.Run(window);
    }

    /// <summary><c>Inlay.exe --iso out.iso [--title "Ad"] kaynak…</c> (or a .discproj): the same pipeline as the window,
    /// without one — for the sample ISO, scripts and CI. Exit 0 on success, 1 when the disc cannot be made.</summary>
    static int Headless(Session session, string iso, string[] rest)
    {
        AttachConsole(-1); // print into the console that started us, if any
        try
        {
            string? title = null;
            var sources = new List<string>();
            for (var i = 0; i < rest.Length; i++)
                if (rest[i] == "--title" && i + 1 < rest.Length) title = rest[++i];
                else sources.Add(Path.GetFullPath(rest[i]));
            if (sources is [var project] && project.EndsWith(DiscProject.Extension, StringComparison.OrdinalIgnoreCase)) session.Open(project).GetAwaiter().GetResult();
            else session.Add(sources).GetAwaiter().GetResult();
            if (title is not null) session.SetMenu(new MenuEdit(Title: title)).GetAwaiter().GetResult();
            var view = session.View();
            if (view.Error is { } error) throw new InvalidOperationException(error);
            Console.WriteLine($"{view.Project.Name}: {view.Rows.Count(r => r.Parent is null)} item(s), {view.Capacity?.Used / 1e6:0} MB on {view.Capacity?.Media}");
            var last = "";
            session.ExportIso(Path.GetFullPath(iso), new Progress<JobProgress>(p =>
            {
                var line = $"{p.Stage} {p.Value:P0}";
                if (line != last) Console.WriteLine(last = line);
            })).GetAwaiter().GetResult();
            Console.WriteLine(Path.GetFullPath(iso));
            return 0;
        }
        catch (Exception e) when (e is BuildBlockedException or InvalidOperationException or IOException or ArgumentException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
}

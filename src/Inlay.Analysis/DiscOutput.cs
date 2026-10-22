using System.Text;
using System.Text.Json;
using Inlay.Core;
using Inlay.Imaging;
using Inlay.Media;
using Inlay.Theming;

namespace Inlay.Analysis;

/// <summary>Everything that goes on the disc besides content (§8.1): manifest, rendered menu, Menu.html fallback,
/// icon, autorun.inf, licenses and the launcher.</summary>
public sealed record DiscFiles(IReadOnlyDictionary<string, string> Files, DiscManifest Manifest, RenderedUi Ui);

public static class DiscOutput
{
    public const string FallbackPage = "Menu.html";

    /// <summary>Renders the theme against the final manifest and writes the generated files into <paramref name="workDir"/>.
    /// Returns null files when lint blocks burning (§7.1.5); the issues are in <see cref="DiscFiles.Ui"/>.</summary>
    public static DiscFiles Assemble(Materialized content, ThemeFiles theme, string workDir, string? userIcon = null)
    {
        var files = new Dictionary<string, string>(content.Files, StringComparer.OrdinalIgnoreCase);
        string Write(string disk, byte[] bytes)
        {
            var path = Path.Combine(workDir, disk.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return files[disk] = path;
        }

        // Licenses travel with the disc (§10) and are listed in the manifest the menu's "Hakkında" reads.
        var licenses = new List<License>(content.Manifest.Licenses ?? []);
        foreach (var font in theme.Theme.Fonts?.Select(f => f.Family).Distinct() ?? [])
        {
            var text = theme.Files.FirstOrDefault(f => f.Key.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase) && f.Key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).Value;
            if (text is null) continue;
            var disk = $".disc/licenses/{Names.DiscSegment(font)}-OFL.txt";
            Write(disk, text);
            licenses.Add(new License { Name = font, Text = "SIL Open Font License 1.1", File = disk });
        }
        // §9.4: LibVLC goes on the disc only when something plays through it, with only the plugins that media needs.
        var extraIssues = new List<LintIssue>();
        if (LibVlcPlugins.Needed(content.Manifest))
        {
            if (LibVlcPlugins.Find() is not { } vlc)
                extraIssues.Add(new LintIssue(LintSeverity.Error, "libvlc-missing", "LibVLC is needed for this disc but was not found"));
            else
            {
                files[".disc/player/libvlc.dll"] = Path.Combine(vlc, "libvlc.dll");
                files[".disc/player/libvlccore.dll"] = Path.Combine(vlc, "libvlccore.dll");
                foreach (var plugin in LibVlcPlugins.For(LibVlcPlugins.Played(content.Manifest)))
                    if (File.Exists(Path.Combine(vlc, "plugins", plugin))) files[".disc/player/plugins/" + plugin] = Path.Combine(vlc, "plugins", plugin);
                Write(".disc/licenses/LibVLC.txt", Encoding.UTF8.GetBytes(LibVlcNotice));
                licenses.Add(new License { Name = "LibVLC", Text = "LGPL-2.1-or-later", File = ".disc/licenses/LibVLC.txt" });
                if (MediaTools.LgplText() is { } lgpl)
                {
                    files[".disc/licenses/LGPL-3.0.txt"] = lgpl;
                    licenses.Add(new License { Name = "GNU LGPL 3.0", Text = "LGPL-3.0", File = ".disc/licenses/LGPL-3.0.txt" });
                }
            }
        }
        var manifest = content.Manifest with { Licenses = licenses.Count > 0 ? licenses : null };

        var rendered = ThemeRenderer.Render(manifest, theme, Sdk.Script);
        var ui = rendered with { Issues = [.. rendered.Issues, .. extraIssues] };
        if (ui.Blocking) return new DiscFiles(new Dictionary<string, string>(), manifest, ui);

        Write(".disc/manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, InlayJson.Default.DiscManifest));
        Write(ThemeRenderer.UiRoot + "index.html", Encoding.UTF8.GetBytes(ui.IndexHtml));
        foreach (var (path, bytes) in ui.Assets) Write(ThemeRenderer.UiRoot + path, bytes);
        Write(FallbackPage, Encoding.UTF8.GetBytes(ui.FallbackHtml));

        var ico = Path.Combine(workDir, ".disc", "icon.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(ico)!);
        DiscIcon.Write(ico, userIcon, cover: FeaturedCover(manifest, files));
        files[Autorun.IconFile] = ico;
        Write("autorun.inf", Autorun.Encoding.GetPreamble().Concat(Autorun.Encoding.GetBytes(Autorun.Build(manifest))).ToArray());
        files[Autorun.LauncherFile] = LauncherBinary.Path;
        return new DiscFiles(files, manifest, ui);
    }

    /// <summary>The LGPL notice that travels with the LibVLC binaries (§10: dynamic linking, licence text, source).</summary>
    const string LibVlcNotice = """
        LibVLC 3.0 — https://www.videolan.org/vlc/libvlc.html
        Copyright © the VideoLAN and VLC authors.

        The files in .disc/player (libvlc.dll, libvlccore.dll and plugins/) are unmodified LibVLC binaries, used by
        Baslat.exe through dynamic linking. LibVLC is free software licensed under the GNU Lesser General Public License,
        version 2.1 or (at your option) any later version; the text of version 3 is in LGPL-3.0.txt next to this file.

        Source code: https://code.videolan.org/videolan/vlc (tag 3.0.x). You may replace these files with your own
        build of LibVLC 3.0.
        """;

    /// <summary>The icon source (§8.2): the featured item's cover, as already prepared for the disc.</summary>
    static string? FeaturedCover(DiscManifest m, IReadOnlyDictionary<string, string> files)
    {
        var item = m.Items.FirstOrDefault(i => i.Id == m.Disc.Featured) ?? m.Items.FirstOrDefault();
        var cover = item switch { App a => a.Icon ?? a.Cover, _ => item?.Cover };
        return cover is not null && files.TryGetValue(cover, out var local) ? local : null;
    }
}

/// <summary>The page SDK the launcher injects; Menu.html carries it inline (built from ui/disc-sdk).</summary>
public static class Sdk
{
    public static string Script { get; } = Load();

    static string Load()
    {
        using var s = typeof(Sdk).Assembly.GetManifestResourceStream("disc-sdk.js") ?? throw new InvalidOperationException("disc-sdk.js is not embedded");
        return new StreamReader(s).ReadToEnd();
    }
}

/// <summary>Where Baslat.exe comes from: next to the builder ("launcher/"), or artifacts/launcher in a checkout.</summary>
public static class LauncherBinary
{
    public static string Path => Find() ?? throw new InvalidOperationException("Baslat.exe not found (publish src/Inlay.Launcher)");

    public static string? Find()
    {
        if (Environment.GetEnvironmentVariable("INLAY_LAUNCHER") is { Length: > 0 } env && File.Exists(env)) return env;
        var beside = System.IO.Path.Combine(AppContext.BaseDirectory, "launcher", "Baslat.exe");
        if (File.Exists(beside)) return beside;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var dev = System.IO.Path.Combine(d.FullName, "artifacts", "launcher", "Baslat.exe");
            if (File.Exists(dev)) return dev;
        }
        return null;
    }
}

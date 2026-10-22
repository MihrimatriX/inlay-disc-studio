using System.Text;

namespace Inlay.Core;

public static class Autorun
{
    public const string LauncherFile = "Baslat.exe";
    public const string IconFile = ".disc/icon.ico";

    /// <summary>autorun.inf text (§8.2). Write it with <see cref="Encoding"/> so Turkish labels survive.</summary>
    public static string Build(DiscManifest m)
    {
        var title = m.Disc.Title.Length > 32 ? m.Disc.Title[..32] : m.Disc.Title;
        return $"""
            [autorun]
            open={LauncherFile}
            icon={IconFile.Replace('/', '\\')}
            label={title}
            action={ActionText(m)}
            shellexecute={LauncherFile}

            """.ReplaceLineEndings("\r\n");
    }

    /// <summary>Windows reads autorun.inf as UTF-16 LE when it starts with a BOM; ANSI would mangle "ğ, ş, ı".</summary>
    public static Encoding Encoding { get; } = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

    /// <summary>The AutoPlay choice text, derived from what the disc opens to (§7.0).</summary>
    public static string ActionText(DiscManifest m)
    {
        var key = m.Disc.Home.View switch
        {
            HomeView.Bundle => "autorun.installAll",
            HomeView.Series => "autorun.watchSeries",
            HomeView.Item => m.Items.FirstOrDefault(i => i.Id == m.Disc.Home.Item) switch
            {
                Movie => "autorun.watchMovie",
                Album => "autorun.playAlbum",
                Gallery => "autorun.showPhotos",
                App { Category: AppCategory.Game } => "autorun.installGame",
                App => "autorun.installApp",
                _ => "autorun.openMenu",
            },
            _ => "autorun.openMenu",
        };
        return Text.Get(key, m.Disc.Locale);
    }
}

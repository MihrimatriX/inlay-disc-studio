using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Inlay.Analysis;
using Inlay.Burn;
using Inlay.Core;
using Inlay.Media;
using Inlay.Theming;
using Xunit;

namespace Inlay.Launcher.Tests;

/// <summary>M1–M4 end to end: folder → analysis → draft → materialized files → rendered menu → ISO → mounted disc →
/// Baslat.exe from the disc → the standard menu over CDP.</summary>
public sealed class FullChainTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-chain-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static string Repo
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !File.Exists(Path.Combine(d.FullName, "Inlay.slnx"))) d = d.Parent;
            return d!.FullName;
        }
    }

    static void Ffmpeg(string args)
    {
        var p = Process.Start(new ProcessStartInfo(MediaTools.FfmpegPath, "-v error -y " + args) { UseShellExecute = false, RedirectStandardError = true })!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception(err);
    }

    string P(string rel)
    {
        var p = Path.Combine(_dir, "Drop", rel.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        return p;
    }

    [Fact]
    public async Task A_dropped_folder_becomes_a_disc_that_opens_its_menu()
    {
        Assert.SkipUnless(LauncherBinary.Find() is not null, "publish the AOT launcher to artifacts/launcher first");
        Assert.SkipUnless(MediaTools.Available, "tools/ffmpeg is missing");
        Assert.SkipUnless(Directory.Exists(Path.Combine(Repo, "node_modules", "playwright-core")), "run npm ci");
        Assert.SkipUnless(Environment.UserInteractive && Environment.GetEnvironmentVariable("CI") is null, "opens real windows; run on a desktop session");

        Ffmpeg($"-f lavfi -i testsrc2=size=320x180:rate=10:duration=4 -f lavfi -i sine=duration=4 -c:v libopenh264 -c:a ac3 \"{P("Kuzey Rüzgârı (2023)/Kuzey Rüzgârı.mkv")}\"");
        File.Copy(Path.Combine(Repo, "design", "img", "film-hero.jpg"), P("Kuzey Rüzgârı (2023)/fanart.jpg"));
        File.Copy(Path.Combine(Repo, "design", "img", "film-poster.jpg"), P("Kuzey Rüzgârı (2023)/poster.jpg"));
        for (var i = 1; i <= 2; i++)
            Ffmpeg($"-f lavfi -i sine=frequency={400 + 100 * i}:duration=1 -c:a flac -metadata title=\"Parça {i}\" -metadata artist=Deniz -metadata album=Işık -metadata track={i} \"{P($"Işık/{i:00}.flac")}\"");
        File.Copy(Path.Combine(Repo, "design", "img", "album.jpg"), P("Işık/cover.jpg"));

        var analysis = await Analyzer.Run([Path.Combine(_dir, "Drop")], ct: Ct);
        var draft = Composer.Compose(analysis, new ComposeOptions { Title = "Inlay Deneme" });
        var work = Path.Combine(_dir, "work");
        var content = await Materializer.Run(draft, work, ct: Ct);
        var disc = DiscOutput.Assemble(content, ThemeFiles.Standard(draft.Manifest.Theme.Package), work);
        disc.Ui.Blocking.Should().BeFalse(string.Join("; ", disc.Ui.Issues.Select(i => i.Message)));
        disc.Files.Keys.Should().Contain(["Baslat.exe", "autorun.inf", ".disc/icon.ico", ".disc/manifest.json", ".disc/ui/index.html", "Menu.html"]);

        var plan = new DiscImagePlan(disc.Manifest.Disc.Label, disc.Manifest.Disc.Media,
            disc.Files.Select(f => new DiscEntry(f.Key, f.Value)).ToList(), [".disc"]);
        DiscImage.Check(plan).Should().BeEmpty();
        var iso = Path.Combine(_dir, "disc.iso");
        DiscImage.ExportIso(plan, iso, ct: Ct);

        var drive = Ps($"(Mount-DiskImage -ImagePath '{iso}' -PassThru | Get-Volume).DriveLetter").Trim();
        try
        {
            var root = $"{drive}:\\";
            var movie = disc.Manifest.Items.OfType<Movie>().Single();
            var probe = $$"""
                {
                  home: [...document.querySelectorAll(".view")].find(v => !v.hidden)?.id,
                  cards: document.querySelectorAll("#v-menu .card").length,
                  title: document.querySelector("#v-menu [data-hero-title]")?.textContent,
                  fontsLoaded: await document.fonts.ready.then(f => [...f].some(x => x.family === "Outfit" && x.status === "loaded")),
                  playing: await disc.play("{{movie.Id}}").then(() => new Promise(r => setTimeout(() => r(disc.player.state()?.status), 1500))),
                  engine: disc.player.state()?.engine,
                }
                """;
            var output = Run("node", $"\"{Path.Combine(Repo, "scripts", "cdp-probe.mjs")}\" \"{Path.Combine(root, "Baslat.exe")}\" - \"{probe.ReplaceLineEndings(" ").Replace("\"", "\\\"")}\"");
            var r = JsonDocument.Parse(output[output.IndexOf('{')..]).RootElement;
            r.GetProperty("home").GetString().Should().Be("v-menu");
            r.GetProperty("cards").GetInt32().Should().Be(2);
            r.GetProperty("title").GetString().Should().Be("Kuzey Rüzgârı");
            r.GetProperty("fontsLoaded").GetBoolean().Should().BeTrue("Outfit comes from the disc, not the network");
            r.GetProperty("playing").GetString().Should().Be("playing");
            r.GetProperty("engine").GetString().Should().Be("libvlc", "the MKV goes on the disc unconverted");
            File.Exists(Path.Combine(root, "Menu.html")).Should().BeTrue();
        }
        finally
        {
            Ps($"Dismount-DiskImage -ImagePath '{iso}' | Out-Null");
        }
    }

    static string Run(string file, string args)
    {
        var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Repo })!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception($"{Path.GetFileName(file)} failed ({p.ExitCode}): {stderr}");
        return stdout.Result;
    }

    static string Ps(string script) => Run("powershell", $"-NoProfile -Command \"{script}\"");
}

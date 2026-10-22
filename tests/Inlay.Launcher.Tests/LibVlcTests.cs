using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Inlay.Analysis;
using Inlay.Core;
using Inlay.Media;
using Inlay.Theming;
using Xunit;

namespace Inlay.Launcher.Tests;

/// <summary>M5 acceptance: a HEVC + DTS film goes to the libvlc engine, the disc carries pruned LibVLC, and the
/// launcher plays it in the video window with the shared controls in the clipped overlay.</summary>
public sealed class LibVlcTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-vlc-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
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

    static string Run(string file, IEnumerable<string> args)
    {
        var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = Repo })!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception($"{Path.GetFileName(file)} failed ({p.ExitCode}): {stderr}");
        return stdout.Result;
    }

    [Fact]
    public async Task Hevc_dts_film_plays_through_libvlc_with_the_shared_controls()
    {
        Assert.SkipUnless(LauncherBinary.Find() is not null && MediaTools.Available && LibVlcPlugins.Find() is not null, "needs the AOT launcher, FFmpeg and LibVLC");
        Assert.SkipUnless(Environment.UserInteractive && Environment.GetEnvironmentVariable("CI") is null, "opens real windows; run on a desktop session");

        var film = Path.Combine(_dir, "Drop", "Gece Treni (2024)", "Gece Treni.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(film)!);
        Run(MediaTools.FfmpegPath, ["-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=25:duration=12", "-f", "lavfi", "-i", "sine=duration=12:sample_rate=48000",
            "-c:v", "libkvazaar", "-c:a", "dca", "-strict", "-2", film]);

        var analysis = await Analyzer.Run([Path.Combine(_dir, "Drop")], ct: Ct);
        var draft = Composer.Compose(analysis);
        var movie = draft.Manifest.Items.OfType<Movie>().Single();
        movie.Media.Playback.Engine.Should().Be(PlaybackEngine.Libvlc, "§6.1: HEVC keeps the original for LibVLC");
        var work = Path.Combine(_dir, "disc");
        var content = await Materializer.Run(draft, Path.Combine(_dir, "work"), ct: Ct);
        var disc = DiscOutput.Assemble(content, ThemeFiles.Standard(draft.Manifest.Theme.Package), Path.Combine(_dir, "gen"));
        disc.Ui.Blocking.Should().BeFalse(string.Join("; ", disc.Ui.Issues.Select(i => i.Message)));
        disc.Files.Keys.Should().Contain([".disc/player/libvlc.dll", ".disc/player/plugins/demux/libmkv_plugin.dll", ".disc/player/plugins/packetizer/libpacketizer_hevc_plugin.dll", ".disc/licenses/LibVLC.txt"]);
        disc.Files.Keys.Should().NotContain(k => k.Contains("libvpx") || k.Contains("libtaglib"), "pruned");
        disc.Manifest.Licenses.Should().Contain(l => l.Name == "LibVLC");

        foreach (var (disk, local) in disc.Files)
        {
            var to = Path.Combine(work, disk.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(local, to, overwrite: true);
        }

        // Window capture for a visual check; kept outside the repo (INLAY_KEEP_SHOT=path keeps a copy).
        var shot = Path.Combine(_dir, "libvlc-overlay.png");
        var output = Run("node", [Path.Combine(Repo, "scripts", "vlc-probe.mjs"), Path.Combine(work, "Baslat.exe"), work, movie.Id, shot, disc.Manifest.Disc.Title]);
        var r = JsonDocument.Parse(output[output.IndexOf('{')..]).RootElement;
        r.GetProperty("started").GetProperty("engine").GetString().Should().Be("libvlc");
        r.GetProperty("state").GetProperty("status").GetString().Should().Be("playing");
        r.GetProperty("state").GetProperty("positionSec").GetDouble().Should().BeGreaterThan(0.5);
        var controls = r.GetProperty("controls");
        controls.GetProperty("open").GetBoolean().Should().BeTrue();
        controls.GetProperty("remote").GetBoolean().Should().BeTrue("the overlay draws controls over LibVLC's picture");
        controls.GetProperty("title").GetString().Should().Be(movie.Title);
        controls.GetProperty("buttons").EnumerateArray().Select(b => b.GetString()).Should().Contain(["toggle", "back", "fwd", "close"]);
        r.GetProperty("after").GetProperty("state").GetProperty("status").GetString().Should().Be("ended", "closing the overlay stops LibVLC");
        if (Environment.GetEnvironmentVariable("INLAY_KEEP_SHOT") is { Length: > 0 } keep) File.Copy(shot, keep, overwrite: true);
    }
}

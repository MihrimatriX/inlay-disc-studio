using System.Diagnostics;
using FluentAssertions;
using Inlay.Core;
using Inlay.Media;
using Inlay.Media.Tests;
using Xunit;

namespace Inlay.Launcher.Player.Tests;

/// <summary>§9.4 "Bu listeyi test et": every format plays through the NativeAOT launcher with only the pruned plugins
/// on the (temporary) disc — a separate process per file, because LibVLC reads its plugin folder once per process.</summary>
public sealed class PruningTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-prune-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } } // DLLs may still be mapped for a moment

    static string? Launcher()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "artifacts", "launcher", "Baslat.exe");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>The manifest description the composer would write for this file (what pruning reads).</summary>
    static MediaFile Describe(string disk, MediaInfo info) => new()
    {
        File = disk, SizeBytes = 1, Container = Path.GetExtension(disk).TrimStart('.'),
        Video = info.Video is { } v ? new VideoStream { Codec = v.Codec } : null,
        Audio = info.Audio.Select(a => new AudioStream { Codec = a.Codec }).ToList(),
        Subtitles = info.Subtitles.Select(s => new Subtitle { Lang = "tr", Embedded = s.Index }).ToList(),
        Playback = new Playback { Engine = PlaybackEngine.Libvlc },
    };

    public static TheoryData<string> Formats =>
    [
        "hevc_aac.mkv", "hevc_hdr10.mkv", "h264_dts.mkv", "h264_truehd.mkv", "h264_ac3.mkv", "h264_pcm.mkv", "h264_ass.mkv",
        "mpeg2.ts", "mpeg4asp.avi", "wmv2.wmv", "vp9_opus.webm",
        "tone.wma", "tone.wv", "tone_alac.m4a", "tone.flac", "tone.mp3", "tone.opus",
        // Not "tone.tta": FFmpeg's TTA encoder output does not play in LibVLC 3.0 even with every plugin. It never
        // reaches LibVLC anyway: §6.1 converts TTA to FLAC for the html engine. Not "tone.ogg": with the dummy audio
        // output Vorbis never advances its clock, even with every plugin (a limit of this headless probe); Ogg Vorbis
        // always plays in the html engine.
    ];

    [Theory, MemberData(nameof(Formats))]
    public async Task Plays_with_only_the_pruned_plugins(string fixture)
    {
        var launcher = Launcher();
        Assert.SkipUnless(launcher is not null, "publish the AOT launcher to artifacts/launcher first");
        var vlc = LibVlcPlugins.Find();
        Assert.SkipUnless(vlc is not null, "LibVLC package not restored");

        var src = MediaFixtures.Get(fixture);
        var info = await FfProbe.Run(src, TestContext.Current.CancellationToken);
        var disk = "Film/" + fixture;
        var root = Path.Combine(_dir, fixture.Replace('.', '_'));
        void Put(string rel, string from)
        {
            var to = Path.Combine(root, rel.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(from, to, overwrite: true);
        }
        Put(disk, src);
        Put(".disc/player/libvlc.dll", Path.Combine(vlc!, "libvlc.dll"));
        Put(".disc/player/libvlccore.dll", Path.Combine(vlc!, "libvlccore.dll"));
        var plugins = LibVlcPlugins.For([Describe(disk, info)]).Concat(["audio_output/libadummy_plugin.dll", "video_output/libvdummy_plugin.dll"]); // test-only outputs
        foreach (var p in plugins) Put(".disc/player/plugins/" + p, Path.Combine(vlc!, "plugins", p));

        var proc = Process.Start(new ProcessStartInfo(launcher!, ["--root", root, "--probe-media", disk]) { RedirectStandardOutput = true, UseShellExecute = false })!;
        var output = await proc.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await proc.WaitForExitAsync(TestContext.Current.CancellationToken);
        proc.ExitCode.Should().Be(0, $"{fixture} should play with {plugins.Count()} plugins: {output}");
    }

    [Fact]
    public void A_film_disc_ships_a_fraction_of_libvlc()
    {
        var vlc = LibVlcPlugins.Find();
        Assert.SkipUnless(vlc is not null, "LibVLC package not restored");
        var film = new MediaFile
        {
            File = "Film/a.mkv", SizeBytes = 1, Container = "mkv", Video = new VideoStream { Codec = "hevc" },
            Audio = [new AudioStream { Codec = "dts" }], Subtitles = [new Subtitle { Lang = "tr", Embedded = 3 }],
            Playback = new Playback { Engine = PlaybackEngine.Libvlc },
        };
        long Size(IEnumerable<string> files) => files.Sum(f => new FileInfo(Path.Combine(vlc!, "plugins", f)).Length);
        var pruned = Size(LibVlcPlugins.For([film]));
        var full = Directory.EnumerateFiles(Path.Combine(vlc!, "plugins"), "*.dll", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        pruned.Should().BeLessThan(full / 3);
        LibVlcPlugins.For([film]).Should().OnlyContain(p => File.Exists(Path.Combine(vlc!, "plugins", p)), "every listed plugin exists in LibVLC 3.0");
    }
}

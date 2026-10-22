using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Inlay.Launcher.Player;
using Inlay.Media.Tests;
using Xunit;

namespace Inlay.Launcher.Player.Tests;

public class DiscoveryTests
{
    internal static string FullLibVlc => typeof(DiscoveryTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "LibVlcDir").Value!;

    [Fact]
    public async Task Modules_used_per_fixture()
    {
        var lines = new List<string>();
        foreach (var f in (Environment.GetEnvironmentVariable("INLAY_VLC_DISCOVER") ?? "hevc_aac.mkv,h264_dts.mkv,h264_truehd.mkv,mpeg2.ts,mpeg4asp.avi,wmv2.wmv,tone.wma,tone.wv,tone_alac.m4a,h264_srt.mkv").Split(','))
        {
            using var s = new VlcSession(FullLibVlc, captureLog: true, "--vout=dummy", "--aout=dummy");
            s.Play(0, MediaFixtures.Get(f));
            for (var i = 0; i < 40 && s.State().PositionSec < 0.5; i++) await Task.Delay(100, TestContext.Current.CancellationToken);
            var st = s.State();
            var used = s.Log.Select(l => Regex.Match(l, "using (.+?) module \"(.+?)\"")).Where(m => m.Success).Select(m => $"{m.Groups[1].Value}={m.Groups[2].Value}").Distinct();
            lines.Add($"{f}: {st.Status} {st.PositionSec:0.0}s :: {string.Join(", ", used)}");
        }
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "inlay-vlc-modules.txt"), lines);
        lines.Should().NotBeEmpty();
    }
}

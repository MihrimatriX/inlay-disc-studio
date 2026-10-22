using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Inlay.Media;
using Xunit;

namespace Inlay.Media.Tests;

/// <summary>The §12 "tests/media" set, synthesized with the bundled FFmpeg on first use and cached in %TEMP%
/// (no binaries in the repo). Formats FFmpeg cannot encode (APE, PGS, DSD, Musepack) are covered by probe JSON
/// fixtures in PlannerTests instead.</summary>
public static class MediaFixtures
{
    static readonly string Dir = Path.Combine(Path.GetTempPath(), "inlay-media-fixtures");
    const string Video = "-f lavfi -i testsrc2=size=320x240:rate=25:duration=2";
    const string Tone = "-f lavfi -i sine=frequency=440:sample_rate=48000:duration=2";
    const string Stereo = "-f lavfi -i sine=frequency=440:sample_rate=44100:duration=2 -ac 2";

    static readonly Dictionary<string, string> Recipes = new()
    {
        ["h264_aac.mp4"] = $"{Video} {Tone} -c:v libopenh264 -c:a aac -movflags +faststart",
        ["h264_aac_moov_last.mp4"] = $"{Video} {Tone} -c:v libopenh264 -c:a aac",
        ["h264_aac.mkv"] = $"{Video} {Tone} -c:v libopenh264 -c:a aac",
        ["vp9_opus.webm"] = $"{Video} {Tone} -c:v libvpx-vp9 -deadline realtime -cpu-used 8 -c:a libopus",
        ["hevc_aac.mkv"] = $"{Video} {Tone} -c:v libkvazaar -c:a aac",
        // kvazaar ignores -color_trc; write HDR10 signalling (BT.2020 + PQ) straight into the HEVC VUI.
        ["hevc_hdr10.mkv"] = $"{Video} {Tone} -c:v libkvazaar -bsf:v hevc_metadata=colour_primaries=9:transfer_characteristics=16:matrix_coefficients=9 -c:a aac",
        ["h264_ac3.mkv"] = $"{Video} {Tone} -c:v libopenh264 -c:a ac3 -ac 6",
        ["h264_dts.mkv"] = $"{Video} {Tone} -c:v libopenh264 -c:a dca -strict -2",
        ["h264_truehd.mkv"] = $"{Video} {Tone} -c:v libopenh264 -c:a truehd -strict -2",
        ["h264_pcm.mkv"] = $"{Video} {Tone} -c:v libopenh264 -c:a pcm_s16le",
        ["h264_srt.mkv"] = $"{Video} {Tone} -i {{srt}} -map 0 -map 1 -map 2 -c:v libopenh264 -c:a aac -c:s subrip -metadata:s:s:0 language=tur",
        ["h264_ass.mkv"] = $"{Video} {Tone} -i {{srt}} -map 0 -map 1 -map 2 -c:v libopenh264 -c:a aac -c:s ass -metadata:s:s:0 language=eng",
        ["mpeg2.ts"] = $"{Video} {Tone} -c:v mpeg2video -c:a mp2",
        ["mpeg4asp.avi"] = $"{Video} {Tone} -c:v mpeg4 -vtag XVID -c:a libmp3lame",
        ["wmv2.wmv"] = $"{Video} {Tone} -c:v wmv2 -c:a wmav2",
        ["tone.mp3"] = $"{Stereo} -c:a libmp3lame",
        ["tone.m4a"] = $"{Stereo} -c:a aac",
        ["tone.flac"] = $"{Stereo} -c:a flac",
        ["tone.opus"] = $"{Stereo} -c:a libopus",
        ["tone.ogg"] = $"{Stereo} -c:a libvorbis",
        ["tone.wav"] = $"{Stereo} -c:a pcm_s16le",
        ["tone_alac.m4a"] = $"{Stereo} -c:a alac",
        ["tone.wv"] = $"{Stereo} -c:a wavpack",
        ["tone.tta"] = $"{Stereo} -c:a tta",
        ["tone.wma"] = $"{Stereo} -c:a wmav2",
    };

    public static string Srt => Ensure("subs.srt", () =>
        "1\n00:00:00,000 --> 00:00:01,000\nMerhaba dünya\n\n2\n00:00:01,000 --> 00:00:02,000\n<i>İkinci</i> satır\n");

    static readonly object Gate = new();

    public static string Get(string name)
    {
        lock (Gate) return Generate(name);
    }

    static string Generate(string name)
    {
        Assert.SkipUnless(MediaTools.Available, "tools/ffmpeg is missing");
        var recipe = Recipes[name].Replace("{srt}", $"\"{Srt}\"");
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(recipe)))[..8];
        var path = Path.Combine(Dir, $"{Path.GetFileNameWithoutExtension(name)}.{key}{Path.GetExtension(name)}");
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(Dir);
        var tmp = path + ".tmp" + Path.GetExtension(name);
        Ffmpeg($"-v error -y {recipe} \"{tmp}\"");
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    static string Ensure(string name, Func<string> content)
    {
        Directory.CreateDirectory(Dir);
        var p = Path.Combine(Dir, name);
        if (!File.Exists(p)) File.WriteAllText(p, content());
        return p;
    }

    public static string Ffmpeg(string args) => Exec(MediaTools.FfmpegPath, args);

    public static string Exec(string file, string args)
    {
        var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception($"{Path.GetFileName(file)} {args}\n{stderr}");
        return stdout.Result;
    }

    /// <summary>Per-packet MD5 of the video stream without decoding: equal lists mean a bit-exact copy.</summary>
    public static IReadOnlyList<string> VideoPacketHashes(string path) =>
        Ffmpeg($"-v error -i \"{path}\" -map 0:v:0 -c copy -f framemd5 -")
            .Split('\n').Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split(',')[^1].Trim()).ToList();

    /// <summary>MD5 of the decoded audio as 16-bit PCM: equal hashes mean a lossless conversion.</summary>
    public static string DecodedAudioHash(string path) =>
        Ffmpeg($"-v error -i \"{path}\" -map 0:a:0 -c:a pcm_s16le -f md5 -").Trim();
}

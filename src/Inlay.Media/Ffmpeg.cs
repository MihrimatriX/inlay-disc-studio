using System.Diagnostics;
using System.Globalization;

namespace Inlay.Media;

/// <summary>ffmpeg command lines for a plan (§6.1). Pure, so the exact arguments are unit-tested.</summary>
public static class FfmpegArgs
{
    /// <summary>HDR → SDR for "maximum compatibility" transcodes (§6.1).</summary>
    public const string ToneMapFilter =
        "zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p";

    public static List<string> For(MediaPlan plan, MediaInfo info, string input, string output, VideoEncoder encoder = VideoEncoder.Software)
    {
        var a = new List<string> { "-hide_banner", "-nostdin", "-y", "-v", "error", "-progress", "pipe:1", "-nostats", "-i", input };
        switch (plan.Step)
        {
            case PlanStep.Faststart:
                a.AddRange(["-map", "0:v:0", "-map", "0:a?", "-c", "copy", "-sn", "-dn", "-movflags", "+faststart"]);
                break;
            case PlanStep.Remux:
            case PlanStep.Transcode:
                a.AddRange(["-map", $"0:{info.Video!.Index}"]);
                var outAudio = 0;
                if (plan.AddStereoAac && info.Audio.FirstOrDefault() is { } first)
                {
                    a.AddRange(["-map", $"0:{first.Index}", $"-c:a:{outAudio}", "aac", $"-ac:a:{outAudio}", "2", $"-b:a:{outAudio}", "192k"]);
                    if (first.Lang is { } lang) a.AddRange([$"-metadata:s:a:{outAudio}", $"language={lang}"]);
                    outAudio++;
                }
                foreach (var idx in plan.CopyAudio)
                {
                    a.AddRange(["-map", $"0:{idx}", $"-c:a:{outAudio}", "copy"]);
                    outAudio++;
                }
                for (var i = 0; i < outAudio; i++) a.AddRange([$"-disposition:a:{i}", i == 0 ? "default" : "0"]);
                if (plan.Step == PlanStep.Remux) a.AddRange(["-c:v", "copy"]);
                else a.AddRange(VideoEncode(info.Video, plan.ToneMap, encoder));
                a.AddRange(["-sn", "-dn", "-map_chapters", "0", "-movflags", "+faststart", "-strict", "-2"]);
                break;
            case PlanStep.ToFlac:
                a.AddRange(["-map", "0:a:0", "-map", "0:v?", "-c:a", "flac", "-compression_level", "8", "-c:v", "copy", "-disposition:v", "attached_pic", "-map_metadata", "0"]);
                break;
            case PlanStep.ToOpus:
                a.AddRange(["-map", "0:a:0", "-c:a", "libopus", "-b:a", "192k", "-vn", "-map_metadata", "0"]);
                break;
            default:
                throw new ArgumentException($"{plan.Step} needs no ffmpeg run");
        }
        a.Add(output);
        return a;
    }

    /// <summary>H.264 for maximum compatibility. The LGPL FFmpeg build has no x264 (§10), so software encoding uses
    /// OpenH264 in bitrate mode; hardware encoders are offered under Ayarlar › Gelişmiş (§6.2).</summary>
    static IEnumerable<string> VideoEncode(StreamInfo v, bool toneMap, VideoEncoder encoder)
    {
        var pixels = (v.Width ?? 1920) * (v.Height ?? 1080);
        var kbps = pixels switch { <= 640 * 480 => 2500, <= 1280 * 720 => 5000, <= 1920 * 1080 => 9000, _ => 20000 };
        var vf = toneMap ? ToneMapFilter : "format=yuv420p";
        IEnumerable<string> codec = encoder switch
        {
            VideoEncoder.Nvenc => ["-c:v", "h264_nvenc", "-preset", "p5", "-rc", "vbr", "-cq", "21", "-b:v", "0"],
            VideoEncoder.Qsv => ["-c:v", "h264_qsv", "-global_quality", "21"],
            VideoEncoder.Amf => ["-c:v", "h264_amf", "-quality", "quality", "-rc", "cqp", "-qp_i", "21", "-qp_p", "23"],
            _ => ["-c:v", "libopenh264", "-b:v", $"{kbps}k", "-maxrate", $"{kbps * 3 / 2}k", "-profile:v", "high"],
        };
        return ["-vf", vf, .. codec];
    }

    /// <summary>One frame as WebP: backdrops, chapter/episode thumbnails and gallery clip thumbnails.</summary>
    public static List<string> Frame(string input, double atSec, uint maxWidth, string output) =>
    [
        "-hide_banner", "-nostdin", "-y", "-v", "error", "-ss", atSec.ToString("0.###", CultureInfo.InvariantCulture), "-i", input,
        "-frames:v", "1", "-vf", $"scale='min({maxWidth},iw)':-2", "-c:v", "libwebp", "-quality", "80", output,
    ];

    /// <summary>Embedded or sidecar text subtitle → WebVTT (§6.1).</summary>
    public static List<string> ToVtt(string input, int? streamIndex, string output) =>
    [
        "-hide_banner", "-nostdin", "-y", "-v", "error", "-i", input,
        .. streamIndex is { } i ? (string[])["-map", $"0:{i}"] : [],
        "-c:s", "webvtt", output,
    ];
}

/// <summary>Runs ffmpeg jobs with bounded parallelism, progress from <c>-progress pipe:1</c>, and cancellation that
/// kills the process and deletes the partial output (§6.2).</summary>
public sealed class FfmpegQueue(int? maxParallel = null)
{
    readonly SemaphoreSlim _slots = new(maxParallel ?? Math.Max(1, Environment.ProcessorCount / 4));

    public async Task Run(IReadOnlyList<string> args, double? durationSec, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var output = args[^1];
        await _slots.WaitAsync(ct);
        try
        {
            var psi = new ProcessStartInfo(MediaTools.FfmpegPath, args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var stderr = p.StandardError.ReadToEndAsync(CancellationToken.None);
            try
            {
                while (await p.StandardOutput.ReadLineAsync(ct) is { } line)
                {
                    if (durationSec is > 0 && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                        && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
                        progress?.Report(Math.Clamp(us / 1e6 / durationSec.Value, 0, 1));
                }
                await p.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await p.WaitForExitAsync(CancellationToken.None);
                TryDelete(output);
                throw;
            }
            if (p.ExitCode != 0)
            {
                TryDelete(output);
                throw new MediaException($"ffmpeg failed ({p.ExitCode}) for {Path.GetFileName(output)}: {(await stderr).Trim()}");
            }
            progress?.Report(1);
        }
        finally { _slots.Release(); }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Reads top-level MP4 boxes to tell whether 'moov' comes before 'mdat' (streamable, "+faststart").</summary>
public static class Mp4
{
    public static bool? MoovFirst(string path)
    {
        using var f = File.OpenRead(path);
        Span<byte> head = stackalloc byte[16];
        while (f.Position + 8 <= f.Length)
        {
            var start = f.Position;
            if (f.Read(head[..8]) < 8) return null;
            long size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(head);
            var type = System.Text.Encoding.ASCII.GetString(head[4..8]);
            if (size == 1)
            {
                if (f.Read(head[8..16]) < 8) return null;
                size = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(head[8..16]);
            }
            else if (size == 0) size = f.Length - start;
            if (type == "moov") return true;
            if (type == "mdat") return false;
            if (size < 8) return null;
            f.Position = start + size;
        }
        return null;
    }
}

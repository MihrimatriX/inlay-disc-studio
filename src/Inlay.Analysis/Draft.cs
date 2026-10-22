using Inlay.Core;
using Inlay.Media;

namespace Inlay.Analysis;

/// <summary>How one disc file comes to be. Estimates feed the capacity ruler before anything is converted (§8.4).</summary>
public abstract record FileJob
{
    public abstract long EstimateBytes { get; }
}

public sealed record CopyJob(string Source, long Size) : FileJob { public override long EstimateBytes => Size; }

public sealed record MediaJob(string Source, MediaInfo Info, MediaPlan Plan, long SourceSize) : FileJob
{
    public override long EstimateBytes => Estimate(Info, Plan, SourceSize);

    /// <summary>Remux ≈ source (+ an AAC stereo track); transcode from the target bitrate; FLAC ≈ source; Opus 192 kb/s.</summary>
    public static long Estimate(MediaInfo info, MediaPlan plan, long size)
    {
        var sec = info.DurationSec ?? 0;
        long Kbps(double k) => (long)(k * 1000 / 8 * sec);
        var aac = plan.AddStereoAac ? Kbps(192) : 0;
        return plan.Step switch
        {
            PlanStep.Keep or PlanStep.Faststart => size,
            PlanStep.Remux => size + aac,
            PlanStep.Transcode => Kbps(((info.Video?.Width ?? 1920) * (info.Video?.Height ?? 1080)) switch { <= 640 * 480 => 2500, <= 1280 * 720 => 5000, <= 1920 * 1080 => 9000, _ => 20000 }) + aac + Kbps(640),
            PlanStep.ToFlac => (long)(size * 1.05),
            PlanStep.ToOpus => Kbps(192),
            _ => size,
        };
    }
}

public sealed record VttJob(string Source, int? Stream) : FileJob { public override long EstimateBytes => 64 * 1024; }

public enum ImageRole { Display, Fitted }

public sealed record ImageJob(string Source, ImageRole Role, uint Max, DisplayPlan? Display = null, bool WidthOnly = false) : FileJob
{
    public override long EstimateBytes => Role == ImageRole.Display && Display is { Copy: true } ? new FileInfo(Source).Length
        : (long)Math.Min(new FileInfo(Source).Length, Math.Pow(Math.Min(Max, 2560), 2) * 0.12);
}

public sealed record FrameJob(string Source, double AtSec, uint Max) : FileJob { public override long EstimateBytes => Max * Max / 8; }

public sealed record IconJob(string Source) : FileJob { public override long EstimateBytes => 60 * 1024; }

public sealed record BytesJob(byte[] Content) : FileJob { public override long EstimateBytes => Content.Length; }

/// <summary>The disc as the builder edits it: a schema-valid manifest whose paths are disc paths, plus what to write
/// at each disc path. Burning materializes the jobs; editing only changes this record.</summary>
public sealed record DiscDraft(DiscManifest Manifest, IReadOnlyDictionary<string, FileJob> Files)
{
    public long ContentBytes => Files.Values.Sum(f => f.EstimateBytes);
    /// <summary>Item id → the content group it came from (the builder's corrections and matches act on groups).</summary>
    public IReadOnlyDictionary<string, ContentGroup> Groups { get; init; } = new Dictionary<string, ContentGroup>();
}

using Inlay.Core;

namespace Inlay.Media;

public enum PlanStep { Keep, Faststart, Remux, Transcode, ToFlac, ToOpus }

public enum VideoEncoder { Software, Nvenc, Qsv, Amf }

/// <summary>User choices from Ayarlar › Gelişmiş (§6, §11).</summary>
public sealed record PlanOptions
{
    /// <summary>"Maksimum uyumluluk": remux/transcode what WebView2 cannot play instead of handing it to LibVLC.
    /// Off (default), no media file is converted.</summary>
    public bool MaxCompatibility { get; init; }
    /// <summary>"Orijinalleri koru": also put the untouched source under original/.</summary>
    public bool KeepOriginals { get; init; }
    public VideoEncoder Encoder { get; init; } = VideoEncoder.Software;
}

/// <summary>What happens to one media file before it goes on the disc, and how the launcher will play it.</summary>
public sealed record MediaPlan
{
    public required PlanStep Step { get; init; }
    /// <summary>Extension of the file that lands on the disc (".mp4", ".flac" or the source's own).</summary>
    public required string OutputExtension { get; init; }
    public required PlaybackEngine Engine { get; init; }
    public PlaybackFallback Fallback { get; init; } = PlaybackFallback.Shell;
    /// <summary>The untouched source is also written (under original/) — for LibVLC fallback or by user choice.</summary>
    public bool KeepOriginal { get; init; }
    /// <summary>Add an AAC stereo track (first, default) because no source track plays in WebView2.</summary>
    public bool AddStereoAac { get; init; }
    /// <summary>Audio stream indexes copied unchanged into the output.</summary>
    public IReadOnlyList<int> CopyAudio { get; init; } = [];
    /// <summary>Embedded text subtitles extracted to WebVTT sidecars.</summary>
    public IReadOnlyList<int> TextSubtitles { get; init; } = [];
    /// <summary>Picture subtitles (PGS, VobSub): only LibVLC shows them, from the original.</summary>
    public IReadOnlyList<int> ImageSubtitles { get; init; } = [];
    public bool ToneMap { get; init; }
    /// <summary>i18n keys of the one gray note shown in "Disk içeriği" (§11).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public bool Normalized => Step != PlanStep.Keep;
}

/// <summary>§6.1 decision table, applied only with "Maksimum uyumluluk"; by default files stay as they are.
/// Pure: same probe + options → same plan. Each table row is a test.</summary>
public static class NormalizationPlanner
{
    static readonly HashSet<string> HtmlVideo = ["h264", "vp9", "av1", "vp8"];
    static readonly HashSet<string> HtmlAudio = ["aac", "opus", "mp3", "flac", "vorbis"];
    /// <summary>Audio codecs the MP4 muxer carries; anything else only survives in the original file.</summary>
    static readonly HashSet<string> Mp4Audio = ["aac", "mp3", "ac3", "eac3", "opus", "flac", "alac", "dts"];
    static readonly HashSet<string> TextSubs = ["subrip", "srt", "ass", "ssa", "mov_text", "webvtt", "text"];
    static readonly HashSet<string> ImageSubs = ["hdmv_pgs_subtitle", "dvd_subtitle", "dvb_subtitle", "xsub"];

    static readonly HashSet<string> MusicKeep = ["mp3", "aac", "flac", "opus", "vorbis", "pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_f32le", "pcm_u8"];
    static readonly HashSet<string> MusicToFlac = ["alac", "ape", "wavpack", "tta"];
    static readonly HashSet<string> MusicLossyLegacy = ["wmav1", "wmav2", "wmapro", "wmavoice", "musepack7", "musepack8"];
    static readonly HashSet<string> MusicLosslessLegacy = ["wmalossless", "dsd_lsbf", "dsd_msbf", "dsd_lsbf_planar", "dsd_msbf_planar"];

    public static MediaPlan Plan(MediaInfo info, string extension, PlanOptions options, bool? moovFirst = null)
    {
        extension = extension.ToLowerInvariant();
        if (!options.MaxCompatibility) return AsIs(info, extension);
        return info.Video is { } video ? PlanVideo(info, video, extension, options, moovFirst) : PlanMusic(info, extension, options);
    }

    /// <summary>Default: every media file goes on the disc untouched. WebView2 plays what it can decode, LibVLC the rest;
    /// remux, extra AAC tracks, FLAC and transcodes only happen with "Maksimum uyumluluk".</summary>
    static MediaPlan AsIs(MediaInfo info, string ext)
    {
        if (info.Video is not { } video)
        {
            var codec = info.Audio.FirstOrDefault()?.Codec ?? "unknown";
            return new MediaPlan { Step = PlanStep.Keep, OutputExtension = ext, Engine = MusicKeep.Contains(codec) ? PlaybackEngine.Html : PlaybackEngine.Libvlc };
        }
        var (text, image, notes) = Subtitles(info);
        var container = (info.FormatName.Contains("mp4") && ext is ".mp4" or ".m4v") || (info.FormatName.Contains("webm") && ext == ".webm");
        var audioPlays = !info.Audio.Any() || info.Audio.Any(a => HtmlAudio.Contains(a.Codec));
        var html = HtmlVideo.Contains(video.Codec) && container && audioPlays && image.Count == 0;
        return new MediaPlan
        {
            Step = PlanStep.Keep, OutputExtension = ext, Engine = html ? PlaybackEngine.Html : PlaybackEngine.Libvlc,
            TextSubtitles = text, ImageSubtitles = image, Notes = notes,
        };
    }

    static (List<int> Text, List<int> Image, List<string> Notes) Subtitles(MediaInfo info)
    {
        var text = info.Subtitles.Where(s => TextSubs.Contains(s.Codec)).Select(s => s.Index).ToList();
        var image = info.Subtitles.Where(s => ImageSubs.Contains(s.Codec)).Select(s => s.Index).ToList();
        var notes = new List<string>();
        if (info.Subtitles.Any(s => s.Codec is "ass" or "ssa")) notes.Add("media.note.assStyle");
        return (text, image, notes);
    }

    /// <summary>"Maksimum uyumluluk": prepare everything so WebView2 plays it (§6.1 table).</summary>
    static MediaPlan PlanVideo(MediaInfo info, StreamInfo video, string ext, PlanOptions o, bool? moovFirst)
    {
        var audio = info.Audio.ToList();
        var (text, image, notes) = Subtitles(info);

        // HEVC, MPEG-2, VC-1, MPEG-4 ASP, WMV, RealVideo…
        if (!HtmlVideo.Contains(video.Codec))
            return Normalized(PlanStep.Transcode, audio, text, image, o, notes, toneMap: video.Hdr != HdrKind.None);

        var webm = ext == ".webm" && info.FormatName.Contains("webm") && audio.All(a => a.Codec is "opus" or "vorbis");
        if (webm)
            return new MediaPlan { Step = PlanStep.Keep, OutputExtension = ext, Engine = PlaybackEngine.Html, TextSubtitles = text, ImageSubtitles = image, Notes = notes };

        var isMp4 = info.FormatName.Contains("mp4") && ext is ".mp4" or ".m4v";
        var needsAac = audio.Count > 0 && !audio.Any(a => HtmlAudio.Contains(a.Codec));
        var lostAudio = audio.Any(a => !Mp4Audio.Contains(a.Codec));
        if (isMp4 && !needsAac && !lostAudio && image.Count == 0)
        {
            var step = moovFirst == false ? PlanStep.Faststart : PlanStep.Keep;
            return new MediaPlan
            {
                Step = step, OutputExtension = ".mp4", Engine = PlaybackEngine.Html, TextSubtitles = text, Notes = Note(notes, step),
                KeepOriginal = o.KeepOriginals && step != PlanStep.Keep, Fallback = PlaybackFallback.Shell,
            };
        }
        return Normalized(PlanStep.Remux, audio, text, image, o, notes, toneMap: false);
    }

    static MediaPlan Normalized(PlanStep step, List<StreamInfo> audio, List<int> text, List<int> image, PlanOptions o, List<string> notes, bool toneMap)
    {
        var needsAac = audio.Count > 0 && !audio.Any(a => HtmlAudio.Contains(a.Codec));
        var lost = audio.Any(a => !Mp4Audio.Contains(a.Codec)) || image.Count > 0;
        // The original stays on the disc when something (an audio track, picture subtitles) would otherwise be lost.
        var keep = o.KeepOriginals || lost;
        return new MediaPlan
        {
            Step = step,
            OutputExtension = ".mp4",
            Engine = PlaybackEngine.Html,
            Fallback = keep ? PlaybackFallback.Libvlc : PlaybackFallback.Shell,
            KeepOriginal = keep,
            AddStereoAac = needsAac || (step == PlanStep.Transcode && audio.Count > 0 && !audio.Any(a => a.Codec == "aac")),
            CopyAudio = audio.Where(a => Mp4Audio.Contains(a.Codec)).Select(a => a.Index).ToList(),
            TextSubtitles = text,
            ImageSubtitles = image,
            ToneMap = toneMap,
            Notes = Note(notes, step),
        };
    }

    static MediaPlan PlanMusic(MediaInfo info, string ext, PlanOptions o)
    {
        var codec = info.Audio.FirstOrDefault()?.Codec ?? "unknown";
        if (MusicKeep.Contains(codec))
            return new MediaPlan { Step = PlanStep.Keep, OutputExtension = ext, Engine = PlaybackEngine.Html };
        if (MusicToFlac.Contains(codec))
            return new MediaPlan { Step = PlanStep.ToFlac, OutputExtension = ".flac", Engine = PlaybackEngine.Html, KeepOriginal = o.KeepOriginals, Notes = ["media.note.prepared"] };
        if (MusicLossyLegacy.Contains(codec) || MusicLosslessLegacy.Contains(codec))
        {
            var lossless = MusicLosslessLegacy.Contains(codec);
            return new MediaPlan
            {
                Step = lossless ? PlanStep.ToFlac : PlanStep.ToOpus, OutputExtension = lossless ? ".flac" : ".opus",
                Engine = PlaybackEngine.Html, KeepOriginal = o.KeepOriginals, Notes = ["media.note.prepared"],
            };
        }
        // Anything unknown: LibVLC plays the original.
        return new MediaPlan { Step = PlanStep.Keep, OutputExtension = ext, Engine = PlaybackEngine.Libvlc };
    }

    static List<string> Note(List<string> notes, PlanStep step)
    {
        if (step != PlanStep.Keep) notes.Insert(0, "media.note.prepared");
        return notes;
    }
}

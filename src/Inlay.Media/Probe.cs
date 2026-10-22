using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Inlay.Core;

namespace Inlay.Media;

public enum StreamKind { Video, Audio, Subtitle, Attachment, Data }

public sealed record StreamInfo
{
    public required int Index { get; init; }
    public required StreamKind Kind { get; init; }
    public required string Codec { get; init; }
    public string? Lang { get; init; }
    public string? Title { get; init; }
    public int? Channels { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? Fps { get; init; }
    public HdrKind Hdr { get; init; }
    public bool Default { get; init; }
    public bool Forced { get; init; }
    /// <summary>Embedded cover art (music files carry it as a one-frame "video" stream).</summary>
    public bool AttachedPic { get; init; }
}

public sealed record ChapterInfo(string Title, double StartSec);

/// <summary>What ffprobe says about a file (§5, §6). The planner only ever sees this, so it stays a pure function.</summary>
public sealed record MediaInfo
{
    public required string FormatName { get; init; }
    public double? DurationSec { get; init; }
    public long SizeBytes { get; init; }
    public required IReadOnlyList<StreamInfo> Streams { get; init; }
    public IReadOnlyList<ChapterInfo> Chapters { get; init; } = [];
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();

    public StreamInfo? Video => Streams.FirstOrDefault(s => s.Kind == StreamKind.Video && !s.AttachedPic);
    public IEnumerable<StreamInfo> Audio => Streams.Where(s => s.Kind == StreamKind.Audio);
    public IEnumerable<StreamInfo> Subtitles => Streams.Where(s => s.Kind == StreamKind.Subtitle);
}

public static class FfProbe
{
    public static async Task<MediaInfo> Run(string path, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(MediaTools.FfprobePath, ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-show_chapters", path])
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) throw new MediaException($"ffprobe failed for {Path.GetFileName(path)}: {(await stderr).Trim()}");
        return Parse(await stdout);
    }

    public static MediaInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var format = root.GetProperty("format");
        return new MediaInfo
        {
            FormatName = Str(format, "format_name") ?? "",
            DurationSec = Num(format, "duration"),
            SizeBytes = (long)(Num(format, "size") ?? 0),
            Tags = Tags(format),
            Streams = root.TryGetProperty("streams", out var streams) ? streams.EnumerateArray().Select(Stream).ToList() : [],
            Chapters = root.TryGetProperty("chapters", out var ch)
                ? ch.EnumerateArray().Select(c => new ChapterInfo(Tags(c).GetValueOrDefault("title") ?? "", Num(c, "start_time") ?? 0)).ToList()
                : [],
        };
    }

    static StreamInfo Stream(JsonElement s)
    {
        var tags = Tags(s);
        var disposition = s.TryGetProperty("disposition", out var d) ? d : default;
        bool Flag(string name) => disposition.ValueKind == JsonValueKind.Object && disposition.TryGetProperty(name, out var v) && v.GetInt32() == 1;
        return new StreamInfo
        {
            Index = s.GetProperty("index").GetInt32(),
            Kind = Str(s, "codec_type") switch
            {
                "video" => StreamKind.Video,
                "audio" => StreamKind.Audio,
                "subtitle" => StreamKind.Subtitle,
                "attachment" => StreamKind.Attachment,
                _ => StreamKind.Data,
            },
            Codec = Str(s, "codec_name") ?? "unknown",
            Lang = Lang(tags.GetValueOrDefault("language")),
            Title = tags.GetValueOrDefault("title"),
            Channels = (int?)Num(s, "channels"),
            Width = (int?)Num(s, "width"),
            Height = (int?)Num(s, "height"),
            Fps = Rate(Str(s, "avg_frame_rate")) ?? Rate(Str(s, "r_frame_rate")),
            Hdr = Hdr(s),
            Default = Flag("default"),
            Forced = Flag("forced"),
            AttachedPic = Flag("attached_pic"),
        };
    }

    static HdrKind Hdr(JsonElement s)
    {
        if (s.TryGetProperty("side_data_list", out var side) && side.EnumerateArray()
                .Any(x => Str(x, "side_data_type")?.Contains("DOVI", StringComparison.OrdinalIgnoreCase) == true))
            return HdrKind.DolbyVision;
        return Str(s, "color_transfer") switch
        {
            "smpte2084" => HdrKind.Hdr10,
            "arib-std-b67" => HdrKind.Hlg,
            _ => HdrKind.None,
        };
    }

    /// <summary>ffprobe reports ISO 639-2 ("tur", "eng"); the manifest wants BCP 47 ("tr", "en"). "und" means unknown.</summary>
    public static string? Lang(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim().ToLowerInvariant();
        if (code is "und" or "mis" or "mul" or "zxx") return null;
        if (code.Length == 2) return code;
        // Matroska often carries ISO 639-2/B codes; .NET knows the /T forms.
        code = Bibliographic.GetValueOrDefault(code, code);
        var culture = CultureInfo.GetCultures(CultureTypes.NeutralCultures).FirstOrDefault(c => c.ThreeLetterISOLanguageName == code);
        return culture is { TwoLetterISOLanguageName.Length: 2 } ? culture.TwoLetterISOLanguageName : code.Length == 3 ? code : null;
    }

    static readonly Dictionary<string, string> Bibliographic = new()
    {
        ["ger"] = "deu", ["fre"] = "fra", ["dut"] = "nld", ["chi"] = "zho", ["cze"] = "ces", ["gre"] = "ell", ["per"] = "fas",
        ["rum"] = "ron", ["slo"] = "slk", ["alb"] = "sqi", ["arm"] = "hye", ["baq"] = "eus", ["geo"] = "kat", ["ice"] = "isl",
        ["mac"] = "mkd", ["may"] = "msa", ["wel"] = "cym", ["bur"] = "mya", ["tib"] = "bod",
    };

    static IReadOnlyDictionary<string, string> Tags(JsonElement e) =>
        e.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Object
            ? t.EnumerateObject().GroupBy(p => p.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Value.GetString() ?? "")
            : new Dictionary<string, string>();

    static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static double? Num(JsonElement e, string name) =>
        !e.TryGetProperty(name, out var v) ? null
        : v.ValueKind == JsonValueKind.Number ? v.GetDouble()
        : v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d
        : null;

    static double? Rate(string? r)
    {
        if (r is null) return null;
        var parts = r.Split('/');
        return parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var n) && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var d) && d > 0 && n > 0
            ? Math.Round(n / d, 3) : null;
    }
}

public sealed class MediaException(string message) : Exception(message);

/// <summary>Where ffmpeg/ffprobe live: INLAY_FFMPEG_DIR, the "ffmpeg" folder next to the builder, or tools/ffmpeg/bin in a checkout.</summary>
public static class MediaTools
{
    static readonly Lazy<string> Dir = new(() =>
    {
        if (Environment.GetEnvironmentVariable("INLAY_FFMPEG_DIR") is { Length: > 0 } env) return env;
        var beside = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        if (File.Exists(Path.Combine(beside, "ffmpeg.exe"))) return beside;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var dev = Path.Combine(d.FullName, "tools", "ffmpeg", "bin");
            if (File.Exists(Path.Combine(dev, "ffmpeg.exe"))) return dev;
        }
        throw new MediaException("ffmpeg not found (set INLAY_FFMPEG_DIR)");
    });

    public static string FfmpegPath => Path.Combine(Dir.Value, "ffmpeg.exe");
    public static string FfprobePath => Path.Combine(Dir.Value, "ffprobe.exe");

    /// <summary>The LGPL text shipped with the FFmpeg build (bin/..), reused for the LibVLC notice.</summary>
    public static string? LgplText()
    {
        // Next to the binaries in the installed builder (ffmpeg/LICENSE.txt), one level up in a BtbN checkout.
        try { return new[] { Path.Combine(Dir.Value, "LICENSE.txt"), Path.Combine(Dir.Value, "..", "LICENSE.txt") }.Where(File.Exists).Select(Path.GetFullPath).FirstOrDefault(); }
        catch (MediaException) { return null; }
    }

    public static bool Available
    {
        get { try { return File.Exists(FfmpegPath); } catch (MediaException) { return false; } }
    }
}

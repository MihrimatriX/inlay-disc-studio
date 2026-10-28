using FluentAssertions;
using ImageMagick;
using Inlay.Core;
using Inlay.Media;
using Xunit;

namespace Inlay.Media.Tests;

/// <summary>Plans carried out by the real FFmpeg and checked with ffprobe (§12 media row, M3 acceptance).</summary>
public sealed class ExecutionTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-media-").FullName;
    readonly FfmpegQueue _queue = new(2);
    public void Dispose() => Directory.Delete(_dir, recursive: true);
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    async Task<(string Output, MediaInfo Info)> Execute(string fixture, PlanOptions? options = null)
    {
        var src = MediaFixtures.Get(fixture);
        var info = await FfProbe.Run(src, Ct);
        var plan = NormalizationPlanner.Plan(info, Path.GetExtension(src), options ?? new PlanOptions { MaxCompatibility = true }, Mp4.MoovFirst(src));
        var output = Path.Combine(_dir, Path.GetFileNameWithoutExtension(fixture) + plan.OutputExtension);
        await _queue.Run(FfmpegArgs.For(plan, info, src, output), info.DurationSec, ct: Ct);
        return (output, await FfProbe.Run(output, Ct));
    }

    [Fact]
    public async Task Mkv_to_mp4_remux_keeps_the_video_stream_bit_exact()
    {
        var src = MediaFixtures.Get("h264_aac.mkv");
        var (output, info) = await Execute("h264_aac.mkv");
        info.FormatName.Should().Contain("mp4");
        Mp4.MoovFirst(output).Should().BeTrue();
        MediaFixtures.VideoPacketHashes(output).Should().Equal(MediaFixtures.VideoPacketHashes(src)).And.NotBeEmpty();
    }

    [Fact]
    public async Task Faststart_moves_moov_to_the_front()
    {
        Mp4.MoovFirst(MediaFixtures.Get("h264_aac_moov_last.mp4")).Should().BeFalse();
        var (output, _) = await Execute("h264_aac_moov_last.mp4");
        Mp4.MoovFirst(output).Should().BeTrue();
    }

    [Theory]
    [InlineData("h264_ac3.mkv", "ac3")]
    [InlineData("h264_dts.mkv", "dts")]
    public async Task Surround_sources_gain_a_default_aac_stereo_track(string fixture, string kept)
    {
        var (_, info) = await Execute(fixture);
        var audio = info.Audio.ToList();
        audio.Select(a => a.Codec).Should().Equal("aac", kept);
        audio[0].Should().Match<StreamInfo>(a => a.Channels == 2 && a.Default);
        audio[1].Default.Should().BeFalse();
    }

    [Fact]
    public async Task Hdr_hevc_transcodes_to_sdr_h264()
    {
        var (_, info) = await Execute("hevc_hdr10.mkv", new PlanOptions { MaxCompatibility = true });
        info.Video!.Should().Match<StreamInfo>(v => v.Codec == "h264" && v.Hdr == HdrKind.None);
        info.Audio.Single().Codec.Should().Be("aac");
    }

    [Theory]
    [InlineData("tone_alac.m4a")]
    [InlineData("tone.wv")]
    [InlineData("tone.tta")]
    public async Task Lossless_music_converts_to_flac_without_changing_a_sample(string fixture)
    {
        var (output, info) = await Execute(fixture);
        info.Audio.Single().Codec.Should().Be("flac");
        MediaFixtures.DecodedAudioHash(output).Should().Be(MediaFixtures.DecodedAudioHash(MediaFixtures.Get(fixture)));
    }

    [Fact]
    public async Task Wma_converts_to_opus_with_max_compatibility() =>
        (await Execute("tone.wma", new PlanOptions { MaxCompatibility = true })).Info.Audio.Single().Codec.Should().Be("opus");

    [Theory]
    [InlineData("h264_srt.mkv")]
    [InlineData("h264_ass.mkv")]
    public async Task Embedded_text_subtitles_become_webvtt(string fixture)
    {
        var src = MediaFixtures.Get(fixture);
        var info = await FfProbe.Run(src, Ct);
        var vtt = Path.Combine(_dir, "sub.vtt");
        await _queue.Run(FfmpegArgs.ToVtt(src, info.Subtitles.Single().Index, vtt), null, ct: Ct);
        var text = File.ReadAllText(vtt);
        text.Should().StartWith("WEBVTT").And.Contain("Merhaba dünya").And.Contain("00:01.000 --> 00:02.000");
    }

    [Fact]
    public async Task Sidecar_srt_becomes_webvtt()
    {
        var vtt = Path.Combine(_dir, "side.vtt");
        await _queue.Run(FfmpegArgs.ToVtt(MediaFixtures.Srt, null, vtt), null, ct: Ct);
        File.ReadAllText(vtt).Should().StartWith("WEBVTT").And.Contain("İkinci");
    }

    [Fact]
    public async Task Progress_reaches_one_and_cancellation_removes_the_partial_file()
    {
        var src = MediaFixtures.Get("hevc_aac.mkv");
        var info = await FfProbe.Run(src, Ct);
        var plan = NormalizationPlanner.Plan(info, ".mkv", new PlanOptions { MaxCompatibility = true });
        var reports = new List<double>();
        var output = Path.Combine(_dir, "p.mp4");
        await _queue.Run(FfmpegArgs.For(plan, info, src, output), info.DurationSec, new Progress(reports.Add), Ct);
        reports.Should().NotBeEmpty().And.BeInAscendingOrder().And.EndWith(1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1));
        var cancelled = Path.Combine(_dir, "c.mp4");
        await FluentActions.Awaiting(() => _queue.Run(FfmpegArgs.For(plan, info, src, cancelled), info.DurationSec, ct: cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        File.Exists(cancelled).Should().BeFalse();
    }

    [Fact]
    public async Task Ffmpeg_errors_surface_and_leave_no_output()
    {
        var output = Path.Combine(_dir, "bad.mp4");
        await FluentActions.Awaiting(() => _queue.Run(["-v", "error", "-i", Path.Combine(_dir, "missing.mkv"), output], null, ct: Ct))
            .Should().ThrowAsync<MediaException>();
        File.Exists(output).Should().BeFalse();
    }

    sealed class Progress(Action<double> report) : IProgress<double> { public void Report(double v) => report(v); }
}

public sealed class ImageTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-img-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Make(string name, uint w, uint h, MagickFormat format)
    {
        var p = Path.Combine(_dir, name);
        using var img = new MagickImage("gradient:#ff8a3d-#203040", w, h);
        img.Write(p, format);
        return p;
    }

    [Fact]
    public void Small_browser_images_are_used_as_they_are()
    {
        var src = Make("small.png", 800, 600, MagickFormat.Png);
        var r = ImagePipeline.Prepare(src, Path.Combine(_dir, "out"), "p1", keepOriginal: false);
        Path.GetExtension(r.File).Should().Be(".png");
        File.ReadAllBytes(r.File).Should().Equal(File.ReadAllBytes(src));
        (r.Width, r.Height).Should().Be((800, 600));
        new MagickImageInfo(r.Thumb).Should().Match<MagickImageInfo>(i => i.Width == 400 && i.Format == MagickFormat.WebP);
    }

    [Fact]
    public void Large_images_get_a_2560_px_webp()
    {
        var r = ImagePipeline.Prepare(Make("big.jpg", 4000, 3000, MagickFormat.Jpeg), Path.Combine(_dir, "out"), "p2", false);
        Path.GetExtension(r.File).Should().Be(".webp");
        (r.Width, r.Height).Should().Be((2560, 1920));
    }

    [Fact]
    public void Tiff_becomes_avif_and_the_original_is_kept_when_asked()
    {
        var r = ImagePipeline.Prepare(Make("scan.tiff", 1200, 900, MagickFormat.Tiff), Path.Combine(_dir, "out"), "p3", keepOriginal: true);
        new MagickImageInfo(r.File).Format.Should().Be(MagickFormat.Avif);
        r.Original.Should().NotBeNull().And.EndWith(Path.Combine("original", "p3.tiff"));
    }

    [Theory]
    [InlineData(MagickFormat.Heic)]
    [InlineData(MagickFormat.Cr2)]
    [InlineData(MagickFormat.Nef)]
    [InlineData(MagickFormat.Dng)]
    [InlineData(MagickFormat.Psd)]
    public void Phone_and_camera_formats_can_be_read(MagickFormat format) =>
        MagickFormatInfo.Create(format)!.SupportsReading.Should().BeTrue();

    [Fact]
    public void Avif_and_webp_can_be_written()
    {
        MagickFormatInfo.Create(MagickFormat.Avif)!.SupportsWriting.Should().BeTrue();
        MagickFormatInfo.Create(MagickFormat.WebP)!.SupportsWriting.Should().BeTrue();
    }

    [Fact]
    public void Heic_becomes_avif()
    {
        string src;
        try { src = Make("phone.heic", 1200, 900, MagickFormat.Heic); }
        catch (MagickException) { Assert.Skip("this Magick.NET build cannot encode HEIC to make the fixture"); return; }
        var r = ImagePipeline.Prepare(src, Path.Combine(_dir, "out"), "p4", false);
        new MagickImageInfo(r.File).Format.Should().Be(MagickFormat.Avif);
    }

    [Fact]
    public void Blurhash_encodes_size_and_the_average_color()
    {
        // 4×3 components → size flag 'L' and 6 + 2·11 characters. The DC term of a flat image is the color itself:
        // #ff8000 = 16744448 → base83 "TNoS".
        var rgb = Enumerable.Repeat(new byte[] { 0xff, 0x80, 0x00 }, 16 * 16).SelectMany(x => x).ToArray();
        var hash = BlurHash.Encode(rgb, 16, 16, 4, 3);
        hash.Should().HaveLength(28).And.StartWith("L");
        hash[2..6].Should().Be("TNoS");
        var other = Enumerable.Range(0, 16 * 16).SelectMany(i => new[] { (byte)(i % 16 * 16), (byte)(i / 16 * 16), (byte)128 }).ToArray();
        BlurHash.Encode(other, 16, 16, 4, 3).Should().NotBe(hash);
    }
}

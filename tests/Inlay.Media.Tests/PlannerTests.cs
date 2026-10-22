using FluentAssertions;
using Inlay.Core;
using Inlay.Media;
using Xunit;

namespace Inlay.Media.Tests;

/// <summary>§6.1, one test per table row, on real ffprobe output of synthesized files.</summary>
public class PlannerTests
{
    static readonly PlanOptions Default = new();
    static readonly PlanOptions MaxCompat = new() { MaxCompatibility = true };

    static async Task<(MediaInfo Info, MediaPlan Plan)> PlanFor(string fixture, PlanOptions? o = null)
    {
        var path = MediaFixtures.Get(fixture);
        var info = await FfProbe.Run(path, TestContext.Current.CancellationToken);
        var ext = Path.GetExtension(path);
        return (info, NormalizationPlanner.Plan(info, ext, o ?? Default, ext == ".mp4" ? Mp4.MoovFirst(path) : null));
    }

    [Fact]
    public async Task Row_mp4_h264_aac_is_left_alone()
    {
        var (_, p) = await PlanFor("h264_aac.mp4");
        p.Should().Match<MediaPlan>(x => x.Step == PlanStep.Keep && x.Engine == PlaybackEngine.Html && !x.Normalized);
    }

    [Fact]
    public async Task Row_mp4_without_faststart_is_left_alone_or_remuxed_with_faststart()
    {
        (await PlanFor("h264_aac_moov_last.mp4")).Plan.Should().Match<MediaPlan>(x => x.Step == PlanStep.Keep && x.Engine == PlaybackEngine.Html);
        var (_, p) = await PlanFor("h264_aac_moov_last.mp4", MaxCompat);
        p.Step.Should().Be(PlanStep.Faststart);
        p.Engine.Should().Be(PlaybackEngine.Html);
        p.Notes.Should().Equal("media.note.prepared");
    }

    [Fact]
    public async Task Row_webm_vp9_opus_is_left_alone() =>
        (await PlanFor("vp9_opus.webm")).Plan.Should().Match<MediaPlan>(x => x.Step == PlanStep.Keep && x.Engine == PlaybackEngine.Html);

    [Fact]
    public async Task Row_mkv_h264_goes_to_libvlc_or_is_remuxed_to_mp4()
    {
        (await PlanFor("h264_aac.mkv")).Plan.Should().Match<MediaPlan>(x => x.Step == PlanStep.Keep && x.OutputExtension == ".mkv" && x.Engine == PlaybackEngine.Libvlc);
        var (_, p) = await PlanFor("h264_aac.mkv", MaxCompat);
        p.Should().Match<MediaPlan>(x => x.Step == PlanStep.Remux && x.OutputExtension == ".mp4" && x.Engine == PlaybackEngine.Html && !x.AddStereoAac && !x.KeepOriginal);
    }

    [Fact]
    public async Task Row_hevc_keeps_the_original_for_libvlc()
    {
        var (_, p) = await PlanFor("hevc_aac.mkv");
        p.Should().Match<MediaPlan>(x => x.Step == PlanStep.Keep && x.Engine == PlaybackEngine.Libvlc && x.OutputExtension == ".mkv");
    }

    [Fact]
    public async Task Row_hevc_with_max_compatibility_is_transcoded()
    {
        var (_, p) = await PlanFor("hevc_aac.mkv", MaxCompat);
        p.Should().Match<MediaPlan>(x => x.Step == PlanStep.Transcode && x.Engine == PlaybackEngine.Html && !x.ToneMap);
    }

    [Fact]
    public async Task Row_hdr_transcode_tone_maps()
    {
        var (info, p) = await PlanFor("hevc_hdr10.mkv", MaxCompat);
        info.Video!.Hdr.Should().Be(HdrKind.Hdr10);
        p.ToneMap.Should().BeTrue();
    }

    [Theory]
    [InlineData("mpeg2.ts")]
    [InlineData("mpeg4asp.avi")]
    [InlineData("wmv2.wmv")]
    public async Task Row_legacy_video_keeps_the_original_for_libvlc(string fixture)
    {
        (await PlanFor(fixture)).Plan.Engine.Should().Be(PlaybackEngine.Libvlc);
        (await PlanFor(fixture, MaxCompat)).Plan.Step.Should().Be(PlanStep.Transcode);
    }

    [Theory]
    [InlineData("h264_ac3.mkv", "ac3")]
    [InlineData("h264_dts.mkv", "dts")]
    public async Task Row_surround_audio_gets_an_aac_stereo_track_and_keeps_its_own(string fixture, string codec)
    {
        (await PlanFor(fixture)).Plan.Should().Match<MediaPlan>(x => x.Step == PlanStep.Keep && x.Engine == PlaybackEngine.Libvlc && !x.AddStereoAac);
        var (info, p) = await PlanFor(fixture, MaxCompat);
        p.AddStereoAac.Should().BeTrue();
        p.CopyAudio.Should().Equal(info.Audio.Single(a => a.Codec == codec).Index);
        p.Should().Match<MediaPlan>(x => x.Engine == PlaybackEngine.Html && !x.KeepOriginal);
    }

    [Theory]
    [InlineData("h264_truehd.mkv")]
    [InlineData("h264_pcm.mkv")]
    public async Task Row_audio_mp4_cannot_carry_stays_in_the_original_for_libvlc(string fixture)
    {
        var (_, p) = await PlanFor(fixture, MaxCompat);
        p.Should().Match<MediaPlan>(x => x.AddStereoAac && x.CopyAudio.Count == 0 && x.KeepOriginal && x.Fallback == PlaybackFallback.Libvlc && x.Engine == PlaybackEngine.Html);
    }

    [Fact]
    public async Task Row_srt_becomes_webvtt()
    {
        var (info, p) = await PlanFor("h264_srt.mkv");
        p.TextSubtitles.Should().Equal(info.Subtitles.Single().Index);
        info.Subtitles.Single().Lang.Should().Be("tr");
        p.Notes.Should().NotContain("media.note.assStyle");
    }

    [Fact]
    public async Task Row_ass_becomes_webvtt_and_warns_about_styling()
    {
        var (_, p) = await PlanFor("h264_ass.mkv");
        p.TextSubtitles.Should().ContainSingle();
        p.Notes.Should().Contain("media.note.assStyle");
    }

    [Theory]
    [InlineData("hdmv_pgs_subtitle")]
    [InlineData("dvd_subtitle")]
    public void Row_picture_subtitles_keep_the_original_for_libvlc(string codec)
    {
        var info = FfProbe.Parse(ProbeJson("matroska,webm", ("video", "h264"), ("audio", "aac"), ("subtitle", codec)));
        NormalizationPlanner.Plan(info, ".mkv", Default).Should().Match<MediaPlan>(x => x.Step == PlanStep.Keep && x.Engine == PlaybackEngine.Libvlc && x.ImageSubtitles.Count == 1);
        var p = NormalizationPlanner.Plan(info, ".mkv", MaxCompat);
        p.Should().Match<MediaPlan>(x => x.Step == PlanStep.Remux && x.ImageSubtitles.Count == 1 && x.KeepOriginal && x.Fallback == PlaybackFallback.Libvlc);
    }

    [Theory]
    [InlineData("tone.mp3")]
    [InlineData("tone.m4a")]
    [InlineData("tone.flac")]
    [InlineData("tone.opus")]
    [InlineData("tone.ogg")]
    [InlineData("tone.wav")]
    public async Task Row_music_that_plays_everywhere_is_left_alone(string fixture) =>
        (await PlanFor(fixture)).Plan.Should().Match<MediaPlan>(x => x.Step == PlanStep.Keep && x.Engine == PlaybackEngine.Html);

    [Theory]
    [InlineData("tone_alac.m4a")]
    [InlineData("tone.wv")]
    [InlineData("tone.tta")]
    public async Task Row_lossless_music_goes_to_libvlc_or_becomes_flac(string fixture)
    {
        (await PlanFor(fixture)).Plan.Should().Match<MediaPlan>(x => x.Step == PlanStep.Keep && x.Engine == PlaybackEngine.Libvlc);
        (await PlanFor(fixture, MaxCompat)).Plan.Should().Match<MediaPlan>(x => x.Step == PlanStep.ToFlac && x.OutputExtension == ".flac" && x.Engine == PlaybackEngine.Html);
    }

    [Fact]
    public void Row_ape_goes_to_libvlc_or_becomes_flac()
    {
        var info = FfProbe.Parse(ProbeJson("ape", ("audio", "ape")));
        NormalizationPlanner.Plan(info, ".ape", Default).Engine.Should().Be(PlaybackEngine.Libvlc);
        NormalizationPlanner.Plan(info, ".ape", MaxCompat).Step.Should().Be(PlanStep.ToFlac);
    }

    [Fact]
    public async Task Row_wma_keeps_the_original_for_libvlc_or_becomes_opus()
    {
        (await PlanFor("tone.wma")).Plan.Engine.Should().Be(PlaybackEngine.Libvlc);
        (await PlanFor("tone.wma", MaxCompat)).Plan.Should().Match<MediaPlan>(x => x.Step == PlanStep.ToOpus && x.Engine == PlaybackEngine.Html);
    }

    [Theory]
    [InlineData("dsf", "dsd_lsbf_planar", PlanStep.ToFlac)]
    [InlineData("mpc8", "musepack8", PlanStep.ToOpus)]
    public void Row_dsd_and_musepack_go_to_libvlc_or_convert(string format, string codec, PlanStep converted)
    {
        var info = FfProbe.Parse(ProbeJson(format, ("audio", codec)));
        NormalizationPlanner.Plan(info, "." + format, Default).Engine.Should().Be(PlaybackEngine.Libvlc);
        NormalizationPlanner.Plan(info, "." + format, MaxCompat).Step.Should().Be(converted);
    }

    [Fact]
    public void Keep_originals_option_keeps_the_source_next_to_converted_files() =>
        NormalizationPlanner.Plan(FfProbe.Parse(ProbeJson("matroska,webm", ("video", "h264"), ("audio", "aac"))), ".mkv",
            new PlanOptions { KeepOriginals = true, MaxCompatibility = true }).Should().Match<MediaPlan>(x => x.KeepOriginal && x.Fallback == PlaybackFallback.Libvlc);

    [Fact]
    public void Probe_maps_languages_and_hdr()
    {
        FfProbe.Lang("tur").Should().Be("tr");
        FfProbe.Lang("ger").Should().Be("de");
        FfProbe.Lang("en").Should().Be("en");
        FfProbe.Lang("und").Should().BeNull();
        var dv = FfProbe.Parse("""{"format":{"format_name":"mov,mp4"},"streams":[{"index":0,"codec_type":"video","codec_name":"hevc","side_data_list":[{"side_data_type":"DOVI configuration record"}]}]}""");
        dv.Video!.Hdr.Should().Be(HdrKind.DolbyVision);
    }

    /// <summary>Minimal ffprobe output for formats FFmpeg can decode but not encode.</summary>
    static string ProbeJson(string format, params (string type, string codec)[] streams) =>
        $$"""{"format":{"format_name":"{{format}}","duration":"2.0","size":"1000"},"streams":[{{string.Join(",", streams.Select((s, i) =>
            $$"""{"index":{{i}},"codec_type":"{{s.type}}","codec_name":"{{s.codec}}","width":320,"height":240,"channels":2}"""))}}]}""";
}

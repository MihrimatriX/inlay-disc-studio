using Inlay.Core;

namespace Inlay.Media;

/// <summary>§9.4 plugin pruning: the LibVLC plugins a disc needs for the containers and codecs it plays through LibVLC.
/// Derived from the modules LibVLC 3.0 loads for each test format (Inlay.Launcher.Player.Tests, DiscoveryTests) and
/// checked by playing every format with only this set. A missing plugin is a silent playback failure, so when in
/// doubt a plugin stays.</summary>
public static class LibVlcPlugins
{
    /// <summary>Always: file access, audio/video output with fallbacks, conversion, hardware decoding, subtitles text.</summary>
    public static readonly string[] Base =
    [
        "access/libfilesystem_plugin.dll", "stream_filter/libcache_read_plugin.dll",
        "audio_output/libmmdevice_plugin.dll", "audio_output/libwasapi_plugin.dll", "audio_output/libdirectsound_plugin.dll", "audio_output/libwaveout_plugin.dll",
        "audio_mixer/libfloat_mixer_plugin.dll", "audio_filter/libscaletempo_plugin.dll", "audio_filter/libugly_resampler_plugin.dll",
        "audio_filter/libspeex_resampler_plugin.dll", "audio_filter/libaudio_format_plugin.dll", "audio_filter/libsimple_channel_mixer_plugin.dll",
        "audio_filter/libtrivial_channel_mixer_plugin.dll", "audio_filter/libremap_plugin.dll", "audio_filter/libtospdif_plugin.dll",
        "codec/libavcodec_plugin.dll", "codec/libspdif_plugin.dll", "codec/libd3d11va_plugin.dll", "codec/libdxva2_plugin.dll",
        "video_output/libdirect3d11_plugin.dll", "video_output/libdirect3d9_plugin.dll", "video_output/libwingdi_plugin.dll",
        // drawable: render into the launcher's video window (without it LibVLC opens its own); winhibit: no screen saver mid-film.
        "video_output/libdrawable_plugin.dll", "video_output/libwinhibit_plugin.dll",
        "d3d11/libdirect3d11_filters_plugin.dll", "d3d9/libdirect3d9_filters_plugin.dll",
        "video_chroma/libswscale_plugin.dll", "video_chroma/libyuvp_plugin.dll", "video_chroma/libchain_plugin.dll",
        "video_chroma/libi420_rgb_plugin.dll", "video_chroma/libi420_yuy2_plugin.dll", "video_filter/libdeinterlace_plugin.dll",
        "text_renderer/libfreetype_plugin.dll", "packetizer/libpacketizer_copy_plugin.dll",
    ];

    static readonly Dictionary<string, string[]> ByContainer = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mkv"] = ["demux/libmkv_plugin.dll"], ["webm"] = ["demux/libmkv_plugin.dll"], ["mka"] = ["demux/libmkv_plugin.dll"],
        ["ts"] = ["demux/libts_plugin.dll"], ["m2ts"] = ["demux/libts_plugin.dll"], ["mts"] = ["demux/libts_plugin.dll"],
        ["avi"] = ["demux/libavi_plugin.dll"], ["divx"] = ["demux/libavi_plugin.dll"],
        ["wmv"] = ["demux/libasf_plugin.dll"], ["wma"] = ["demux/libasf_plugin.dll"], ["asf"] = ["demux/libasf_plugin.dll"],
        ["mp4"] = ["demux/libmp4_plugin.dll"], ["m4v"] = ["demux/libmp4_plugin.dll"], ["m4a"] = ["demux/libmp4_plugin.dll"], ["mov"] = ["demux/libmp4_plugin.dll"],
        ["mpg"] = ["demux/libps_plugin.dll", "demux/libes_plugin.dll"], ["mpeg"] = ["demux/libps_plugin.dll", "demux/libes_plugin.dll"], ["vob"] = ["demux/libps_plugin.dll"],
        ["mp3"] = ["demux/libes_plugin.dll"], ["aac"] = ["demux/libes_plugin.dll"],
        ["flac"] = ["demux/libflacsys_plugin.dll"], ["ogg"] = ["demux/libogg_plugin.dll"], ["oga"] = ["demux/libogg_plugin.dll"], ["opus"] = ["demux/libogg_plugin.dll"],
        ["wav"] = ["demux/libwav_plugin.dll"], ["tta"] = ["demux/libtta_plugin.dll"],
        ["aif"] = ["demux/libaiff_plugin.dll"], ["aiff"] = ["demux/libaiff_plugin.dll"],
        ["sub"] = ["demux/libvobsub_plugin.dll"], ["idx"] = ["demux/libvobsub_plugin.dll"],
        // ape, wv, dsf, dff, mpc, rm, rmvb, flv: libavcodec_plugin carries the avformat demuxer.
    };

    static readonly Dictionary<string, string[]> ByCodec = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mp3"] = ["codec/libmpg123_plugin.dll", "packetizer/libpacketizer_mpegaudio_plugin.dll"],
        ["mp2"] = ["codec/libmpg123_plugin.dll", "packetizer/libpacketizer_mpegaudio_plugin.dll"],
        ["mpeg2video"] = ["packetizer/libpacketizer_mpegvideo_plugin.dll"], ["mpeg1video"] = ["packetizer/libpacketizer_mpegvideo_plugin.dll"],
        ["mpeg4"] = ["packetizer/libpacketizer_mpeg4video_plugin.dll"],
        ["h264"] = ["packetizer/libpacketizer_h264_plugin.dll"], ["hevc"] = ["packetizer/libpacketizer_hevc_plugin.dll"],
        ["dts"] = ["packetizer/libpacketizer_dts_plugin.dll"], ["truehd"] = ["packetizer/libpacketizer_mlp_plugin.dll"], ["mlp"] = ["packetizer/libpacketizer_mlp_plugin.dll"],
        ["flac"] = ["codec/libflac_plugin.dll", "packetizer/libpacketizer_flac_plugin.dll"],
        ["aac"] = ["packetizer/libpacketizer_mpeg4audio_plugin.dll"],
        ["opus"] = ["codec/libopus_plugin.dll"], ["vorbis"] = ["codec/libvorbis_plugin.dll"], ["theora"] = ["codec/libtheora_plugin.dll"],
        ["vp8"] = ["codec/libvpx_plugin.dll"], ["vp9"] = ["codec/libvpx_plugin.dll"], ["av1"] = ["codec/libdav1d_plugin.dll"],
    };

    static readonly Dictionary<string, string[]> BySubtitle = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vtt"] = ["codec/libwebvtt_plugin.dll"],
        ["text"] = ["codec/libsubsdec_plugin.dll", "codec/libsubsusf_plugin.dll", "codec/libsubstx3g_plugin.dll", "demux/libsubtitle_plugin.dll"],
        ["picture"] = ["codec/libspudec_plugin.dll", "codec/libdvbsub_plugin.dll"],
    };

    /// <summary>Media the launcher may hand to LibVLC: engine libvlc, or an original kept as the libvlc fallback.</summary>
    public static IEnumerable<MediaFile> Played(DiscManifest m) => m.Items.SelectMany(MediaOf)
        .Where(f => f.Playback.Engine == PlaybackEngine.Libvlc || f.Playback.Fallback == PlaybackFallback.Libvlc);

    public static bool Needed(DiscManifest m) => Played(m).Any();

    public static IReadOnlySet<string> For(IEnumerable<MediaFile> files)
    {
        var set = new HashSet<string>(Base, StringComparer.OrdinalIgnoreCase);
        void Add(Dictionary<string, string[]> table, string? key)
        {
            if (key is not null && table.TryGetValue(key, out var plugins)) set.UnionWith(plugins);
        }
        foreach (var f in files)
        {
            foreach (var file in new[] { f.File, f.Playback.Original }.OfType<string>())
                Add(ByContainer, Path.GetExtension(file).TrimStart('.'));
            Add(ByContainer, f.Container);
            Add(ByCodec, f.Video?.Codec);
            foreach (var a in f.Audio ?? []) Add(ByCodec, a.Codec);
            foreach (var s in f.Subtitles ?? [])
            {
                if (s.File is not null) Add(BySubtitle, "vtt");
                if (s.Embedded is not null) { Add(BySubtitle, "text"); Add(BySubtitle, "picture"); }
            }
        }
        return set;
    }

    /// <summary>Every media file an item plays (film, trailer, extras, episodes, tracks, gallery videos).</summary>
    public static IEnumerable<MediaFile> MediaOf(DiscItem i) => i switch
    {
        Movie m => new[] { m.Media, m.Trailer }.OfType<MediaFile>().Concat(m.Extras ?? []),
        Series s => s.Seasons.SelectMany(x => x.Episodes).Select(e => e.Media),
        Album a => a.Tracks.Select(t => t.Media),
        Gallery g => g.Photos.Select(p => p.Media).OfType<MediaFile>(),
        App a => a.Trailer is { } t ? [t] : [],
        _ => [],
    };

    /// <summary>The LibVLC package folder (libvlc.dll + plugins/): INLAY_LIBVLC_DIR, "libvlc" next to the builder, or NuGet.</summary>
    public static string? Find()
    {
        if (Environment.GetEnvironmentVariable("INLAY_LIBVLC_DIR") is { Length: > 0 } env && File.Exists(Path.Combine(env, "libvlc.dll"))) return env;
        var beside = Path.Combine(AppContext.BaseDirectory, "libvlc");
        if (File.Exists(Path.Combine(beside, "libvlc.dll"))) return beside;
        var nuget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", "videolan.libvlc.windows");
        return Directory.Exists(nuget)
            ? Directory.EnumerateDirectories(nuget).Select(d => Path.Combine(d, "build", "x64")).Where(d => File.Exists(Path.Combine(d, "libvlc.dll"))).OrderDescending().FirstOrDefault()
            : null;
    }
}

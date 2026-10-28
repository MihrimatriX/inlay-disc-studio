using System.Collections.Concurrent;
using LibVLCSharp.Shared;

namespace Inlay.Launcher.Player;

public sealed record VlcTrack(int Id, string Label, bool Active);

/// <summary>What the overlay and the page see (mirrors sdk PlayerState plus the track lists for the menus).</summary>
public sealed record VlcState(string Status, double PositionSec, double DurationSec, double Volume, IReadOnlyList<VlcTrack> Audio, IReadOnlyList<VlcTrack> Subtitles);

/// <summary>LibVLC 3 playback into a window handle (§9.4 libvlc engine). LibVLC is loaded from the disc's .disc/player
/// folder; its own mouse/keyboard handling is off so the HTML overlay owns the controls.</summary>
public sealed class VlcSession : IDisposable
{
    readonly LibVLC _vlc;
    readonly MediaPlayer _player;
    Media? _media;
    public ConcurrentQueue<string> Log { get; } = new();

    static bool _loaded;

    public static bool Available(string playerDir) => File.Exists(Path.Combine(playerDir, "libvlc.dll"));

    public VlcSession(string playerDir, bool captureLog = false, params string[] extraOptions)
    {
        if (!_loaded)
        {
            Core.Initialize(playerDir);
            _loaded = true;
        }
        _vlc = new LibVLC(enableDebugLogs: captureLog,
            ["--no-video-title-show", "--no-osd", "--no-snapshot-preview", "--no-stats", "--no-lua", "--no-media-library", .. extraOptions]);
        if (captureLog) _vlc.Log += (_, e) => Log.Enqueue($"{e.Module}: {e.Message}");
        _player = new MediaPlayer(_vlc) { EnableMouseInput = false, EnableKeyInput = false };
    }

    public void Play(nint hwnd, string file, double startSec = 0, IEnumerable<string>? subtitleFiles = null)
    {
        _media?.Dispose();
        _media = new Media(_vlc, file, FromType.FromPath);
        if (startSec > 0) _media.AddOption($":start-time={startSec.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        foreach (var s in subtitleFiles ?? []) _media.AddSlave(MediaSlaveType.Subtitle, 4, new Uri(s).AbsoluteUri);
        _player.Hwnd = hwnd;
        _player.Play(_media);
    }

    public void Pause() => _player.SetPause(true);
    public void Resume() => _player.SetPause(false);
    public void Toggle() => _player.Pause();
    public void Stop() => _player.Stop();
    public void Seek(double sec) => _player.Time = (long)(Math.Max(0, sec) * 1000);
    public void SetVolume(double v) => _player.Volume = (int)Math.Round(Math.Clamp(v, 0, 1) * 100);
    public void SetAudio(int id) => _player.SetAudioTrack(id);
    public void SetSubtitle(int id) => _player.SetSpu(id); // -1: off

    public VlcState State()
    {
        var status = _player.State switch
        {
            VLCState.Opening or VLCState.Buffering or VLCState.NothingSpecial => "loading",
            VLCState.Playing => "playing",
            VLCState.Paused => "paused",
            VLCState.Ended or VLCState.Stopped => "ended",
            _ => "error",
        };
        var audio = _player.AudioTrackDescription.Where(t => t.Id >= 0).Select(t => new VlcTrack(t.Id, t.Name ?? "", t.Id == _player.AudioTrack)).ToList();
        var spu = _player.SpuDescription.Where(t => t.Id >= 0).Select(t => new VlcTrack(t.Id, t.Name ?? "", t.Id == _player.Spu)).ToList();
        return new VlcState(status, Math.Max(0, _player.Time) / 1000.0, Math.Max(0, _player.Length) / 1000.0, Math.Max(0, _player.Volume) / 100.0, audio, spu);
    }

    public void Dispose()
    {
        _player.Stop();
        _player.Dispose();
        _media?.Dispose();
        _vlc.Dispose();
    }
}

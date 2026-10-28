using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Inlay.Core;
using Inlay.Launcher.Player;
using Inlay.Launcher.WebView2;

namespace Inlay.Launcher;

/// <summary>§9.4 libvlc engine: a video popup over the menu and a WebView2 overlay above it drawing the same controls as
/// the html player, state pushed every 250 ms; media keys and XInput (§9.4) for both engines. The overlay window is
/// clipped (SetWindowRgn) to the panels the page reports: a transparent WebView2 over another window is not composited
/// (tried WS_EX_NOREDIRECTIONBITMAP and LWA_COLORKEY: the picture stays hidden), a window region is (ADR 0012).</summary>
sealed partial class LauncherWindow
{
    const nuint PlayerTimer = 7, PadTimer = 8;
    const uint WS_EX_TOOLWINDOW = 0x80;

    sealed record Queued(string ItemId, string? TrackId, MediaFile Media, string Title);

    VlcSession? _vlc;
    nint _videoHwnd, _overlayHwnd;
    ICoreWebView2Controller? _overlayController;
    ICoreWebView2? _overlay;
    List<Queued> _tracks = [];
    int _index;
    string _lastStatus = "";

    string PlayerDir => Path.Combine(_root, ".disc", "player");

    public Task<string?> Player(string cmd, string? id, JsonElement opts)
    {
        var tcs = new TaskCompletionSource<string?>();
        Post(() =>
        {
            try { tcs.SetResult(PlayerOnUi(cmd, id, opts)); }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task;
    }

    string? PlayerOnUi(string cmd, string? id, JsonElement opts)
    {
        var m = _disc.Manifest ?? throw new BridgeError("internal");
        double Num(string name) => opts.ValueKind == JsonValueKind.Object && opts.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        bool Flag(string name) => opts.ValueKind == JsonValueKind.Object && opts.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
        switch (cmd)
        {
            case "play":
            {
                var q = Find(m, id) ?? throw new BridgeError("not-found");
                if (q.Media.Playback.Engine == PlaybackEngine.Shell && !Flag("original"))
                {
                    new WindowsShell(Confirm).Open(RelPath.Resolve(_root, q.Media.File) ?? throw new BridgeError("not-found"));
                    return null;
                }
                _tracks = [q];
                _index = 0;
                return Start(Num("startSec"), Flag("original"));
            }
            case "playlist":
            {
                var album = m.Items.OfType<Album>().FirstOrDefault(a => a.Id == id) ?? throw new BridgeError("not-found");
                _tracks = album.Tracks.Select(t => new Queued(album.Id, t.Id, t.Media, t.Title)).ToList();
                if (Flag("shuffle")) _tracks = [.. _tracks.OrderBy(_ => Random.Shared.Next())];
                var start = opts.ValueKind == JsonValueKind.Object && opts.TryGetProperty("startTrackId", out var s) ? s.GetString() : null;
                _index = Math.Max(0, _tracks.FindIndex(q => q.TrackId == start));
                return Start(0, false);
            }
            case "slideshow":
                throw new BridgeError("unknown-action"); // the template's own viewer shows galleries
        }
        if (_vlc is null) return null;
        switch (cmd)
        {
            case "player.pause": _vlc.Pause(); break;
            case "player.resume": _vlc.Resume(); break;
            case "player.stop": StopPlayer(); break;
            case "player.seek": _vlc.Seek(Num("sec")); break;
            case "player.setVolume": _vlc.SetVolume(Num("volume")); break;
            case "player.next": Step(1); break;
            case "player.prev": Step(-1); break;
            case "player.audio": _vlc.SetAudio((int)Num("id")); break;
            case "player.subtitle": _vlc.SetSubtitle(opts.TryGetProperty("id", out var sid) ? sid.GetInt32() : -1); break;
            default: throw new BridgeError("unknown-action");
        }
        return StateJson();
    }

    /// <summary>Items, album tracks, episodes, gallery clips and a movie's trailer/extras (by disc path), like the SDK.</summary>
    static Queued? Find(DiscManifest m, string? id)
    {
        if (id is null) return null;
        foreach (var i in m.Items)
            switch (i)
            {
                case Movie mv when mv.Id == id: return new(mv.Id, null, mv.Media, mv.Title);
                case Movie mv when new[] { mv.Trailer }.Concat(mv.Extras ?? []).FirstOrDefault(x => x?.File == id) is { } x: return new(mv.Id, x.File, x, mv.Title);
                case Album a when a.Tracks.FirstOrDefault(t => t.Id == id || a.Id == id) is { } t: return new(a.Id, t.Id, t.Media, t.Title);
                case Series s when s.Seasons.SelectMany(z => z.Episodes).FirstOrDefault(e => e.Id == id || s.Id == id) is { } e: return new(s.Id, e.Id, e.Media, $"{s.Title} · {e.Title}");
                case Gallery g when g.Photos.FirstOrDefault(p => p.Id == id || p.Media?.File == id)?.Media is { } pm: return new(g.Id, id, pm, g.Title);
            }
        return null;
    }

    string Start(double startSec, bool original)
    {
        if (!VlcSession.Available(PlayerDir)) throw new BridgeError("unsupported-codec");
        var q = _tracks[_index];
        var rel = original && q.Media.Playback.Original is { } o ? o : q.Media.File;
        var file = RelPath.Resolve(_root, rel) ?? throw new BridgeError("not-found");
        if (!File.Exists(file)) throw new BridgeError("not-found");
        _vlc ??= new VlcSession(PlayerDir);
        var video = q.Media.Video is not null;
        if (video) OpenWindows(q.Title);
        else CloseWindows();
        var subs = (q.Media.Subtitles ?? []).Select(s => s.File is null ? null : RelPath.Resolve(_root, s.File)).OfType<string>().Where(File.Exists);
        _vlc.Play(video ? _videoHwnd : 0, file, startSec, subs);
        _lastStatus = "";
        SetTimer(_hwnd, PlayerTimer, 250, 0);
        return StateJson();
    }

    void Step(int delta)
    {
        var i = _index + delta;
        if (i < 0 || i >= _tracks.Count) return;
        _index = i;
        Start(0, false);
    }

    void StopPlayer()
    {
        KillTimer(_hwnd, PlayerTimer);
        _vlc?.Stop();
        EmitPlayer(ended: true);
        CloseWindows();
    }

    string StateJson(bool ended = false)
    {
        var q = _tracks.Count > 0 ? _tracks[_index] : null;
        var s = _vlc?.State();
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("itemId", q?.ItemId ?? "");
            if (q?.TrackId is { } t) w.WriteString("trackId", t);
            w.WriteString("engine", "libvlc");
            w.WriteString("status", ended ? "ended" : s?.Status ?? "ended");
            w.WriteNumber("positionSec", s?.PositionSec ?? 0);
            w.WriteNumber("durationSec", s?.DurationSec is > 0 ? s.DurationSec : q?.Media.DurationSec ?? 0);
            w.WriteNumber("volume", s?.Volume ?? 1);
            void Tracks(string name, IReadOnlyList<VlcTrack>? list)
            {
                w.WriteStartArray(name);
                foreach (var tr in list ?? [])
                {
                    w.WriteStartObject();
                    w.WriteNumber("id", tr.Id);
                    w.WriteString("label", tr.Label);
                    w.WriteBoolean("active", tr.Active);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            Tracks("audio", s?.Audio);
            Tracks("subtitles", s?.Subtitles);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    void EmitPlayer(bool ended = false)
    {
        var message = $$"""{"v":1,"event":"player","data":{{StateJson(ended)}}}""";
        _webview?.PostWebMessageAsJson(message);
        _overlay?.PostWebMessageAsJson(message);
    }

    /// <summary>WM_TIMER: push state; at the end, go to the next track or close.</summary>
    void OnPlayerTick()
    {
        if (_vlc is null) return;
        var status = _vlc.State().Status;
        if (status == "ended" && _lastStatus is "playing" or "paused")
        {
            if (_index + 1 < _tracks.Count) { Step(1); return; }
            StopPlayer();
            return;
        }
        _lastStatus = status;
        WakeOnMouseMove();
        EmitPlayer();
    }

    // ---------------- windows ----------------

    unsafe void OpenWindows(string title)
    {
        if (_videoHwnd != 0) { PositionPlayer(); return; }
        fixed (char* cls = "InlayVideo")
        {
            var wc = new Native.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Native.WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = Native.GetModuleHandleW(null),
                hCursor = Native.LoadCursorW(0, 32512), hbrBackground = Native.CreateSolidBrush(0), lpszClassName = cls,
            };
            Native.RegisterClassExW(wc); // fails harmlessly when already registered
        }
        _videoHwnd = Native.CreateWindowExW(WS_EX_TOOLWINDOW, "InlayVideo", title, Native.WS_POPUP | Native.WS_VISIBLE, 0, 0, 0, 0, _hwnd, 0, Native.GetModuleHandleW(null), 0);
        _overlayHwnd = Native.CreateWindowExW(WS_EX_TOOLWINDOW, "InlayVideo", title, Native.WS_POPUP | Native.WS_VISIBLE, 0, 0, 0, 0, _videoHwnd, 0, Native.GetModuleHandleW(null), 0);
        SetWindowRgn(_overlayHwnd, CreateRectRgn(0, 0, 0, 0), 1); // nothing shows until the page reports its panels
        PositionPlayer();
        _env!.CreateCoreWebView2Controller(_overlayHwnd, new ControllerCompleted((hr, controller) =>
        {
            if (hr < 0 || controller is null || _overlayHwnd == 0) return;
            _overlayController = controller;
            ((ICoreWebView2Controller2)controller).put_DefaultBackgroundColor(new COREWEBVIEW2_COLOR { A = 255, R = 0x14, G = 0x14, B = 0x16 });
            Native.GetClientRect(_overlayHwnd, out var r);
            controller.put_Bounds(r);
            controller.get_CoreWebView2(out _overlay);
            _overlay!.get_Settings(out var settings);
            settings!.put_AreDefaultContextMenusEnabled(0);
            settings.put_IsStatusBarEnabled(0);
            _overlay.add_WebMessageReceived(new WebMessageReceived((_, args) =>
            {
                args!.get_WebMessageAsJson(out var json);
                OverlayCommand(json);
            }), out _);
            _overlay.add_NewWindowRequested(new NewWindowRequested((_, a) => a!.put_Handled(1)), out _);
            var boot = $"window.__inlayBoot={{\"manifest\":{_disc.Json},\"hosted\":true,\"overlay\":{{\"title\":{JsonSerializer.Serialize(title, InlayJson.Default.String)}}}}};\n{Sdk()}";
            _overlay.AddScriptToExecuteOnDocumentCreated(boot, 0);
            _overlay.NavigateToString("<!doctype html><html><head><meta charset=\"utf-8\"></head><body style=\"margin:0;background:#141416\"></body></html>");
            controller.MoveFocus(MoveFocusProgrammatic);
        }));
    }

    void OverlayCommand(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var cmd = root.GetProperty("cmd").GetString() ?? "";
            var opts = root.TryGetProperty("opts", out var o) ? o.Clone() : default;
            if (cmd == "overlay.region") ClipOverlay(opts);
            else if (cmd.StartsWith("player.", StringComparison.Ordinal)) PlayerOnUi(cmd, null, opts);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or BridgeError) { }
    }

    /// <summary>Shows only the control panels of the overlay; everything else is the video window underneath.</summary>
    void ClipOverlay(JsonElement opts)
    {
        if (_overlayHwnd == 0 || opts.ValueKind != JsonValueKind.Object) return;
        var scale = opts.TryGetProperty("scale", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetDouble() : 1;
        var region = CreateRectRgn(0, 0, 0, 0);
        foreach (var r in opts.GetProperty("rects").EnumerateArray())
        {
            int V(string n) => (int)Math.Round(r.GetProperty(n).GetDouble() * scale);
            var (x, y, w, h, rad) = (V("x"), V("y"), V("w"), V("h"), V("r"));
            var part = CreateRoundRectRgn(x, y, x + w + 1, y + h + 1, rad * 2, rad * 2);
            CombineRgn(region, region, part, 2 /* RGN_OR */);
            DeleteObject(part);
        }
        SetWindowRgn(_overlayHwnd, region, 1); // the window owns the region from here on
    }

    POINT _cursor;

    /// <summary>The clipped overlay cannot see the mouse over the picture: the launcher does, and wakes the controls.</summary>
    void WakeOnMouseMove()
    {
        if (_overlay is null || !GetCursorPos(out var p) || (p.X == _cursor.X && p.Y == _cursor.Y)) return;
        _cursor = p;
        Native.GetWindowRect(_videoHwnd, out var r);
        if (p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom) _overlay.PostWebMessageAsJson("""{"v":1,"event":"wake","data":{}}""");
    }

    /// <summary>Clicking the picture activates the video window: hand the keyboard back to the controls.</summary>
    void FocusOverlay()
    {
        if (_overlayController is null) return;
        _overlayController.MoveFocus(MoveFocusProgrammatic);
        _overlay?.PostWebMessageAsJson("""{"v":1,"event":"wake","data":{}}""");
    }

    /// <summary>Video and overlay cover the menu's client area (the whole monitor in full screen).</summary>
    void PositionPlayer()
    {
        if (_videoHwnd == 0) return;
        Native.GetClientRect(_hwnd, out var r);
        var tl = new POINT { X = r.Left, Y = r.Top };
        ClientToScreen(_hwnd, ref tl);
        var (w, h) = (r.Right - r.Left, r.Bottom - r.Top);
        Native.SetWindowPos(_videoHwnd, 0, tl.X, tl.Y, w, h, Native.SWP_NOOWNERZORDER);
        Native.SetWindowPos(_overlayHwnd, 0, tl.X, tl.Y, w, h, Native.SWP_NOOWNERZORDER);
        if (_overlayController is not null)
        {
            Native.GetClientRect(_overlayHwnd, out var o);
            _overlayController.put_Bounds(o);
        }
    }

    void CloseWindows()
    {
        _overlayController?.Close();
        _overlayController = null;
        _overlay = null;
        if (_overlayHwnd != 0) Native.DestroyWindow(_overlayHwnd);
        if (_videoHwnd != 0) Native.DestroyWindow(_videoHwnd);
        _overlayHwnd = _videoHwnd = 0;
        Native.SetForegroundWindow(_hwnd);
        _controller?.MoveFocus(MoveFocusProgrammatic);
    }

    static string Sdk()
    {
        using var s = typeof(LauncherWindow).Assembly.GetManifestResourceStream("disc-sdk.js")!;
        return new StreamReader(s).ReadToEnd();
    }

    // ---------------- media keys and XInput (§9.4) ----------------

    /// <summary>WM_APPCOMMAND → "media-key"; LibVLC also obeys directly.</summary>
    void OnAppCommand(nint lParam)
    {
        var cmd = ((int)((long)lParam >> 16)) & ~0xF000;
        var key = cmd switch { 14 or 46 or 47 => "play-pause", 11 => "next", 12 => "prev", 13 => "stop", _ => null };
        if (key is null) return;
        if (_vlc is not null && _tracks.Count > 0)
            switch (key)
            {
                case "play-pause": _vlc!.Toggle(); break;
                case "next": Step(1); break;
                case "prev": Step(-1); break;
                case "stop": StopPlayer(); break;
            }
        _webview?.PostWebMessageAsJson($$$"""{"v":1,"event":"media-key","data":{"key":"{{{key}}}"}}""");
    }

    ushort _padButtons;

    /// <summary>XInput buttons as "gamepad" events for the menu (disc.remote=false turns them off).</summary>
    void OnPadTick()
    {
        if (_disc.Manifest?.Disc.Remote == false) return; // "Kumanda ile gezinme" off
        if (XInputGetState(0, out var st) != 0) { _padButtons = 0; return; }
        var down = (ushort)(st.Gamepad.wButtons & ~_padButtons);
        _padButtons = st.Gamepad.wButtons;
        foreach (var (bit, name) in new (ushort, string)[] { (0x1, "up"), (0x2, "down"), (0x4, "left"), (0x8, "right"), (0x1000, "a"), (0x2000, "b") })
            if ((down & bit) != 0) _webview?.PostWebMessageAsJson($$$"""{"v":1,"event":"gamepad","data":{"button":"{{{name}}}"}}""");
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }
    [StructLayout(LayoutKind.Sequential)] struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }

    [LibraryImport("user32.dll")] private static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint proc);
    [LibraryImport("user32.dll")] private static partial int KillTimer(nint hwnd, nuint id);
    [LibraryImport("user32.dll")] private static partial int SetWindowRgn(nint hwnd, nint region, int redraw);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out POINT p);
    [LibraryImport("gdi32.dll")] private static partial nint CreateRectRgn(int l, int t, int r, int b);
    [LibraryImport("gdi32.dll")] private static partial nint CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [LibraryImport("gdi32.dll")] private static partial int CombineRgn(nint dest, nint a, nint b, int mode);
    [LibraryImport("gdi32.dll")] private static partial int DeleteObject(nint obj);
    [LibraryImport("user32.dll")] private static partial int ClientToScreen(nint hwnd, ref POINT p);
    [LibraryImport("xinput1_4.dll")] private static partial uint XInputGetState(uint user, out XINPUT_STATE state);
}

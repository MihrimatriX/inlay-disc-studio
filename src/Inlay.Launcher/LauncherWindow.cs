using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text.Json;
using Inlay.Core;
using Inlay.Launcher.WebView2;

namespace Inlay.Launcher;

/// <summary>The launcher's Win32 window hosting WebView2 (§9.1–9.2).</summary>
sealed partial class LauncherWindow : IBridgeHost
{
    public const string Origin = "https://disc.local/";
    public const string StartPage = Origin + ".disc/ui/index.html";
    const uint WM_RUN_QUEUED = Native.WM_APP + 1;
    const int HostResourceAccessAllow = 1, WebResourceContextAll = 0, MoveFocusProgrammatic = 0;

    static LauncherWindow? _instance;

    readonly string _root, _dataFolder;
    readonly LoadedDisc _disc;
    readonly ConcurrentQueue<Action> _queue = new();
    readonly int _uiThread = Environment.CurrentManagedThreadId;
    nint _hwnd;
    ICoreWebView2Environment? _env;
    ICoreWebView2Controller? _controller;
    ICoreWebView2? _webview;
    Bridge? _bridge;
    string _webviewVersion = "";
    bool _fullscreen;
    Native.WINDOWPLACEMENT _placement;

    public LauncherWindow(string root, LoadedDisc disc, string dataFolder) => (_root, _disc, _dataFolder) = (root, disc, dataFolder);

    string Locale => _disc.Manifest?.Disc.Locale ?? Text.DefaultLocale;

    public static string ClassName(string key) => "InlayLauncher." + key;

    public unsafe void Create(string key)
    {
        _instance = this;
        var bg = BackgroundColor();
        fixed (char* cls = ClassName(key))
        {
            var wc = new Native.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Native.WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = Native.GetModuleHandleW(null),
                hCursor = Native.LoadCursorW(0, 32512 /* IDC_ARROW */),
                hbrBackground = Native.CreateSolidBrush((uint)(bg.R | bg.G << 8 | bg.B << 16)),
                lpszClassName = cls,
            };
            Native.RegisterClassExW(wc);
        }
        _hwnd = Native.CreateWindowExW(0, ClassName(key), _disc.Manifest?.Disc.Title ?? "Inlay", Native.WS_OVERLAPPEDWINDOW,
            Native.CW_USEDEFAULT, Native.CW_USEDEFAULT, 1280, 760, 0, 0, Native.GetModuleHandleW(null), 0);
        var on = 1;
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, on, sizeof(int));
        var mica = 2; // DWMSBT_MAINWINDOW (Windows 11; ignored on 10)
        Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, mica, sizeof(int));
        if (RelPath.Resolve(_root, Autorun.IconFile) is { } ico && File.Exists(ico))
        {
            var big = Native.LoadImageW(0, ico, 1 /* IMAGE_ICON */, 0, 0, 0x10 | 0x40 /* LR_LOADFROMFILE | LR_DEFAULTSIZE */);
            Native.SendMessageW(_hwnd, Native.WM_SETICON, 1, big);
            Native.SendMessageW(_hwnd, Native.WM_SETICON, 0, big);
        }
        Native.ShowWindow(_hwnd, Native.SW_SHOW);
        SetTimer(_hwnd, PadTimer, 33, 0);
        StartWebView();
    }

    COREWEBVIEW2_COLOR BackgroundColor()
    {
        var hex = _disc.Manifest?.Theme.Palette.Bg ?? "#0b0d14";
        return new COREWEBVIEW2_COLOR { A = 255, R = Convert.ToByte(hex[1..3], 16), G = Convert.ToByte(hex[3..5], 16), B = Convert.ToByte(hex[5..7], 16) };
    }

    // ---------------- WebView2 ----------------

    void StartWebView()
    {
        var hr = Native.CreateCoreWebView2EnvironmentWithOptions(null, _dataFolder, 0, new EnvironmentCompleted((code, env) =>
        {
            if (code < 0 || env is null) { Fail(code); return; }
            _env = env;
            env.get_BrowserVersionString(out _webviewVersion);
            env.CreateCoreWebView2Controller(_hwnd, new ControllerCompleted((code2, controller) =>
            {
                if (code2 < 0 || controller is null) { Fail(code2); return; }
                OnController(controller);
            }));
        }));
        if (hr < 0) Fail(hr);
    }

    void Fail(int hr)
    {
        Dialog.Ask(_hwnd, "Inlay", Text.Get("launcher.failed.title", Locale), $"{Text.Get("launcher.failed.body", Locale)} (0x{hr:x8})", Text.Get("launcher.close", Locale));
        Native.DestroyWindow(_hwnd);
    }

    void OnController(ICoreWebView2Controller controller)
    {
        _controller = controller;
        ((ICoreWebView2Controller2)controller).put_DefaultBackgroundColor(BackgroundColor());
        Resize();
        controller.get_CoreWebView2(out _webview);
        var wv = _webview!;

        wv.get_Settings(out var settings);
#if DEBUG
        settings!.put_AreDevToolsEnabled(1);
#else
        settings!.put_AreDevToolsEnabled(0);
#endif
        settings.put_AreDefaultContextMenusEnabled(0);
        settings.put_AreHostObjectsAllowed(0);
        settings.put_IsStatusBarEnabled(0);
        settings.put_AreDefaultScriptDialogsEnabled(0);

        wv.add_NavigationStarting(new NavigationStarting((_, args) =>
        {
            args!.get_Uri(out var uri);
            if (!Allowed(uri)) args.put_Cancel(1);
        }), out _);
        wv.add_NewWindowRequested(new NewWindowRequested((_, args) => args!.put_Handled(1)), out _);
        wv.add_ContainsFullScreenElementChanged(new FullScreenChanged((sender, _) =>
        {
            sender!.get_ContainsFullScreenElement(out var full);
            SetFullscreen(full != 0);
        }), out _);

        if (_disc.Manifest is not { } manifest)
        {
            wv.NavigateToString(ErrorPage());
            return;
        }

        ((ICoreWebView2_3)wv).SetVirtualHostNameToFolderMapping("disc.local", _root, HostResourceAccessAllow);
        wv.AddWebResourceRequestedFilter("*", WebResourceContextAll);
        wv.add_WebResourceRequested(new WebResourceRequested((_, args) => BlockNetwork(args!)), out _);

        var shell = new WindowsShell(Confirm);
        var actions = new ActionRunner(manifest, _root, shell, Emit, Close, new WindowsUninstallRegistry());
        var bundles = new BundleRunner(_root, actions, shell, p => Emit("bundle-progress", p));
        _bridge = new Bridge(actions, bundles, manifest, this);
        wv.add_WebMessageReceived(new WebMessageReceived((sender, args) =>
        {
            args!.get_WebMessageAsJson(out var json);
            _ = HandleAsync(json);
        }), out _);

        wv.AddScriptToExecuteOnDocumentCreated(BootScript(_disc.Json!), 0);
        wv.Navigate(StartPage);
    }

    static bool Allowed(string uri) =>
        uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase) || uri == "about:blank" || uri.StartsWith("data:text/html", StringComparison.Ordinal);

    public static string BootScript(string manifestJson)
    {
        using var s = typeof(LauncherWindow).Assembly.GetManifestResourceStream("disc-sdk.js")!;
        return $"window.__inlayBoot={{\"manifest\":{manifestJson},\"hosted\":true}};\n{new StreamReader(s).ReadToEnd()}";
    }

    /// <summary>§10 "the disc menu makes no network calls", enforced natively: WebView2 raises WebResourceRequested
    /// only for requests the disc.local folder mapping does not serve, so every one of them is answered 403 here.</summary>
    void BlockNetwork(ICoreWebView2WebResourceRequestedEventArgs args)
    {
        _env!.CreateWebResourceResponse(0, 403, "Forbidden", "", out var response);
        args.put_Response(response);
    }

    string ErrorPage() => $$"""
        <!doctype html><html lang="tr"><meta charset="utf-8"><title>Inlay</title>
        <body style="margin:0;height:100vh;display:grid;place-items:center;background:#0b0d14;color:#f2f1ee;font:20px 'Segoe UI Variable Text','Segoe UI',sans-serif">
        <div style="max-width:560px;text-align:center"><h1 style="font-weight:600;font-size:32px">{{System.Net.WebUtility.HtmlEncode(Text.Get("launcher.badDisc.title", Locale))}}</h1>
        <p style="color:#9aa0b1">{{System.Net.WebUtility.HtmlEncode(Text.Get("launcher.badDisc.body", Locale))}}</p></div></body></html>
        """;

    async Task HandleAsync(string json)
    {
        var reply = await _bridge!.Handle(json);
        if (reply is not null) Post(() => _webview?.PostWebMessageAsJson(reply));
    }

    void Emit(string name, object data)
    {
        var message = data switch
        {
            ActionEvent a => Bridge.Event(name, a, LauncherJson.Default.ActionEvent),
            BundleProgress p => Bridge.Event(name, p, LauncherJson.Default.BundleProgress),
            FocusEvent f => Bridge.Event(name, f, LauncherJson.Default.FocusEvent),
            _ => throw new ArgumentException(data.GetType().Name),
        };
        Post(() => _webview?.PostWebMessageAsJson(message));
    }

    bool Confirm(string message) => Invoke(() =>
        Dialog.Ask(_hwnd, _disc.Manifest?.Disc.Title ?? "Inlay", message, "", Text.Get("launcher.continue", Locale), Text.Get("launcher.cancel", Locale)) == 0);

    // ---------------- IBridgeHost ----------------

    public SystemInfo Probe() => SystemProbe.Collect(_root, _webviewVersion);

    public void Close() => Native.PostMessageW(_hwnd, Native.WM_CLOSE, 0, 0);

    public unsafe void SetFullscreen(bool on) => Post(() =>
    {
        if (on == _fullscreen) return;
        _fullscreen = on;
        var style = (uint)Native.GetWindowLongPtr(_hwnd, Native.GWL_STYLE);
        if (on)
        {
            _placement.length = (uint)sizeof(Native.WINDOWPLACEMENT);
            Native.GetWindowPlacement(_hwnd, ref _placement);
            var mi = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
            Native.GetMonitorInfoW(Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST), ref mi);
            Native.SetWindowLongPtr(_hwnd, Native.GWL_STYLE, (nint)(style & ~Native.WS_OVERLAPPEDWINDOW));
            var r = mi.rcMonitor;
            Native.SetWindowPos(_hwnd, 0, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Native.SWP_NOOWNERZORDER | Native.SWP_FRAMECHANGED);
        }
        else
        {
            Native.SetWindowLongPtr(_hwnd, Native.GWL_STYLE, (nint)(style | Native.WS_OVERLAPPEDWINDOW));
            Native.SetWindowPlacement(_hwnd, _placement);
            Native.SetWindowPos(_hwnd, 0, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOOWNERZORDER | Native.SWP_FRAMECHANGED);
        }
    });

    // ---------------- UI thread ----------------

    void Post(Action work)
    {
        _queue.Enqueue(work);
        Native.PostMessageW(_hwnd, WM_RUN_QUEUED, 0, 0);
    }

    T Invoke<T>(Func<T> work)
    {
        if (Environment.CurrentManagedThreadId == _uiThread) return work();
        var done = new TaskCompletionSource<T>();
        Post(() => { try { done.SetResult(work()); } catch (Exception e) { done.SetException(e); } });
        return done.Task.GetAwaiter().GetResult();
    }

    void Resize()
    {
        if (_controller is null) return;
        Native.GetClientRect(_hwnd, out var r);
        _controller.put_Bounds(r);
    }

    [UnmanagedCallersOnly]
    static unsafe nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var self = _instance;
        switch (msg)
        {
            case WM_RUN_QUEUED when self is not null:
                while (self._queue.TryDequeue(out var work)) work();
                return 0;
            case Native.WM_SIZE when self is not null:
                if (hwnd == self._hwnd) self.Resize();
                self.PositionPlayer();
                return 0;
            case 0x0003 /* WM_MOVE */ when self is not null && hwnd == self._hwnd:
                self.PositionPlayer();
                break;
            case 0x0113 /* WM_TIMER */ when self is not null:
                if ((nuint)wParam == PlayerTimer) self.OnPlayerTick();
                else if ((nuint)wParam == PadTimer) self.OnPadTick();
                return 0;
            case Native.WM_APPCOMMAND when self is not null:
                self.OnAppCommand(lParam);
                return 1;
            case Native.WM_SETFOCUS when self is not null && hwnd == self._videoHwnd:
                self.FocusOverlay();
                return 0;
            case Native.WM_SETFOCUS when self?._controller is not null && hwnd == self._hwnd:
                self._controller.MoveFocus(MoveFocusProgrammatic);
                return 0;
            case Native.WM_ACTIVATE when self?._bridge is not null:
                self.Emit("visibility", new FocusEvent((wParam & 0xFFFF) != 0));
                break;
            case Native.WM_DESTROY when self is not null && hwnd == self._hwnd:
                self._vlc?.Dispose();
                self._controller?.Close();
                Native.PostQuitMessage(0);
                return 0;
        }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public static void RunLoop()
    {
        while (Native.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(msg);
            Native.DispatchMessageW(msg);
        }
    }

    // ---------------- COM callbacks ----------------

    [GeneratedComClass] sealed partial class EnvironmentCompleted(Action<int, ICoreWebView2Environment?> f) : ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler
    { public void Invoke(int errorCode, ICoreWebView2Environment? result) => f(errorCode, result); }

    [GeneratedComClass] sealed partial class ControllerCompleted(Action<int, ICoreWebView2Controller?> f) : ICoreWebView2CreateCoreWebView2ControllerCompletedHandler
    { public void Invoke(int errorCode, ICoreWebView2Controller? result) => f(errorCode, result); }

    [GeneratedComClass] sealed partial class NavigationStarting(Action<ICoreWebView2?, ICoreWebView2NavigationStartingEventArgs?> f) : ICoreWebView2NavigationStartingEventHandler
    { public void Invoke(ICoreWebView2? sender, ICoreWebView2NavigationStartingEventArgs? args) => f(sender, args); }

    [GeneratedComClass] sealed partial class NewWindowRequested(Action<ICoreWebView2?, ICoreWebView2NewWindowRequestedEventArgs?> f) : ICoreWebView2NewWindowRequestedEventHandler
    { public void Invoke(ICoreWebView2? sender, ICoreWebView2NewWindowRequestedEventArgs? args) => f(sender, args); }

    [GeneratedComClass] sealed partial class WebMessageReceived(Action<ICoreWebView2?, ICoreWebView2WebMessageReceivedEventArgs?> f) : ICoreWebView2WebMessageReceivedEventHandler
    { public void Invoke(ICoreWebView2? sender, ICoreWebView2WebMessageReceivedEventArgs? args) => f(sender, args); }

    [GeneratedComClass] sealed partial class WebResourceRequested(Action<ICoreWebView2?, ICoreWebView2WebResourceRequestedEventArgs?> f) : ICoreWebView2WebResourceRequestedEventHandler
    { public void Invoke(ICoreWebView2? sender, ICoreWebView2WebResourceRequestedEventArgs? args) => f(sender, args); }

    [GeneratedComClass] sealed partial class FullScreenChanged(Action<ICoreWebView2?, nint> f) : ICoreWebView2ContainsFullScreenElementChangedEventHandler
    { public void Invoke(ICoreWebView2? sender, nint args) => f(sender, args); }
}

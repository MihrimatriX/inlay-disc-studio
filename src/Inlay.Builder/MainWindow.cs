using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using Inlay.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Path = System.IO.Path;

namespace Inlay.Builder;

/// <summary>The builder window (§11): a native 44 px title bar drawn like design/screens (so Snap Layouts work on the
/// maximize button) above one WebView2 that runs the React UI. The host owns dialogs, drag-and-drop paths, the preview
/// origin and long jobs; everything else goes to <see cref="Session"/> through <see cref="Rpc"/>.</summary>
sealed class MainWindow : Window
{
    const string AppHost = "app.inlay";
    readonly Session _session;
    readonly Prefs _prefs;
    readonly WebView2 _web = new();
    readonly TextBlock _project = new();
    readonly Border _maxButton;
    readonly Rectangle _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x8c, 8, 8, 10)), Visibility = Visibility.Collapsed };
    readonly FontFamily _geist = new(new Uri("pack://application:,,,/"), "./Fonts/#Geist");
    CancellationTokenSource? _job;
    bool _busy;
    bool _maxHot;

    static readonly Color Bg = Color.FromRgb(0x0f, 0x10, 0x13);
    static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9a, 0x9c, 0xa6));

    public MainWindow(Session session, Prefs prefs)
    {
        (_session, _prefs) = (session, prefs);
        Title = "Inlay";
        Width = 1440; Height = 900; MinWidth = 1100; MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Inlay.ico"));
        Background = new SolidColorBrush(Bg);
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false,
        });

        var bar = new Grid { Height = 44 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Image { Source = BitmapFrame.Create(new Uri("pack://application:,,,/Inlay.ico")), Width = 18, Height = 18, Margin = new Thickness(0, 0, 10, 0) });
        brand.Children.Add(new TextBlock { Text = "Inlay", FontFamily = _geist, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0xec, 0xec, 0xef)) });
        _project.FontFamily = _geist; _project.FontSize = 13; _project.Foreground = Muted; _project.Margin = new Thickness(10, 0, 0, 0);
        brand.Children.Add(_project);
        bar.Children.Add(brand);
        bar.Children.Add(Caption(1, "M1 6h10", Text.Get("builder.window.minimize", _prefs.Locale), () => WindowState = WindowState.Minimized));
        _maxButton = Caption(2, "M1.5 1.5h9v9h-9z", Text.Get("builder.window.maximize", _prefs.Locale), ToggleMaximize);
        bar.Children.Add(_maxButton);
        bar.Children.Add(Caption(3, "M1.5 1.5l9 9M10.5 1.5l-9 9", Text.Get("builder.window.close", _prefs.Locale), Close, close: true));

        // bg token: the same radial light the page paints below the title bar, so the two meet without a seam.
        var root = new Grid
        {
            Background = new RadialGradientBrush
            {
                Center = new Point(0, 0), GradientOrigin = new Point(0, 0), RadiusX = 0.7, RadiusY = 0.6,
                GradientStops = { new GradientStop(Color.FromRgb(0x20, 0x22, 0x2a), 0), new GradientStop(Color.FromArgb(0, 0x20, 0x22, 0x2a), 0.7) },
            },
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(bar);
        Grid.SetRow(_web, 1);
        root.Children.Add(_web);
        root.Children.Add(_dim);
        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(Bg.R, Bg.G, Bg.B);
        Content = root;

        StateChanged += (_, _) => root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        SourceInitialized += (_, _) => Native(new WindowInteropHelper(this).Handle);
        Loaded += async (_, _) => await Start();
        Closing += (_, _) => _job?.Cancel();
        _session.Changed += v => Dispatcher.BeginInvoke(() => Post("state", v));
    }

    Border Caption(int column, string path, string label, Action click, bool close = false)
    {
        var icon = new System.Windows.Shapes.Path { Data = Geometry.Parse(path), Stroke = Muted, StrokeThickness = 1.2, Width = 10, Height = 10, Stretch = Stretch.None };
        var b = new Border { Background = Brushes.Transparent, Child = icon, ToolTip = label };
        AutomationProperties.SetName(b, label);
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        Grid.SetColumn(b, column);
        var hot = close ? new SolidColorBrush(Color.FromRgb(0xc4, 0x2b, 0x1c)) : new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        b.MouseEnter += (_, _) => { b.Background = hot; if (close) icon.Stroke = Brushes.White; };
        b.MouseLeave += (_, _) => { b.Background = Brushes.Transparent; icon.Stroke = Muted; };
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ---------------- Win32: dark frame, rounded corners, Snap Layouts on the maximize button ----------------

    void Native(nint hwnd)
    {
        int dark = 1, round = 2;
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE
        DwmSetWindowAttribute(hwnd, 33, ref round, 4);  // DWMWA_WINDOW_CORNER_PREFERENCE: round
        HwndSource.FromHwnd(hwnd).AddHook(Hook);
    }

    nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x84, WM_NCMOUSEMOVE = 0xA0, WM_NCLBUTTONDOWN = 0xA1, WM_NCLBUTTONUP = 0xA2, WM_NCMOUSELEAVE = 0x2A2,
            WM_MOUSEMOVE = 0x200, WM_DEVICECHANGE = 0x219, HTMAXBUTTON = 9;
        switch (msg)
        {
            // Windows 11 shows the Snap Layouts flyout for HTMAXBUTTON; WPF content never reports it on its own.
            case WM_NCHITTEST when OverMax(lParam):
                handled = true;
                return HTMAXBUTTON;
            case WM_NCMOUSEMOVE:
                MaxHot(wParam == HTMAXBUTTON);
                break;
            case WM_NCMOUSELEAVE or WM_MOUSEMOVE:
                MaxHot(false);
                break;
            case WM_NCLBUTTONDOWN when wParam == HTMAXBUTTON:
                handled = true;
                break;
            case WM_NCLBUTTONUP when wParam == HTMAXBUTTON:
                handled = true;
                ToggleMaximize();
                break;
            case WM_DEVICECHANGE:
                _ = _session.RefreshDrives(); // disc inserted or ejected
                break;
        }
        return 0;
    }

    bool OverMax(nint lParam)
    {
        var p = new Point((short)(lParam & 0xffff), (short)((lParam >> 16) & 0xffff));
        if (PresentationSource.FromVisual(_maxButton) is null) return false;
        var local = _maxButton.PointFromScreen(p);
        return local.X >= 0 && local.Y >= 0 && local.X < _maxButton.ActualWidth && local.Y < _maxButton.ActualHeight;
    }

    void MaxHot(bool hot)
    {
        if (hot == _maxHot) return;
        _maxHot = hot;
        _maxButton.Background = hot ? new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)) : Brushes.Transparent;
        if (hot) TrackNonClientLeave();
    }

    void TrackNonClientLeave()
    {
        var t = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = 0x2 | 0x10 /* LEAVE | NONCLIENT */, hwndTrack = new WindowInteropHelper(this).Handle };
        TrackMouseEvent(ref t);
    }

    // ---------------- WebView2 ----------------

    async Task Start()
    {
        string? version = null;
        try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
        catch (WebView2RuntimeNotFoundException) { }
        if (version is null)
        {
            MessageBox.Show(this, Text.Get("builder.noWebView", _prefs.Locale), "Inlay", MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Analysis.Online.InlayFolders.Root, "WebView2"));
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = Environment.GetEnvironmentVariable("INLAY_DEVTOOLS") == "1";
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(AppHost, Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Deny);
        core.AddWebResourceRequestedFilter(Session.PreviewOrigin + "*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.AddWebResourceRequestedFilter(Session.DiscOrigin + "*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += OnPreviewRequest;
        core.WebMessageReceived += OnMessage;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.Navigate($"https://{AppHost}/index.html");
        _ = _session.RefreshDrives();
    }

    void Post(string name, object? data)
    {
        if (_web.CoreWebView2 is null) return;
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new Dictionary<string, object?> { ["event"] = name, ["data"] = data }, Rpc.Json));
        if (name == "state" && data is View v) _project.Text = v.Project.Name.Length > 0 ? "/ " + v.Project.Name : "";
    }

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith($"https://{AppHost}/", StringComparison.Ordinal)) return;
        string? id = null;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var m = doc.RootElement;
            id = m.GetProperty("id").ToString();
            var method = m.GetProperty("method").GetString()!;
            var p = m.TryGetProperty("params", out var pp) ? pp.Clone() : default;
            var result = await Host(method, p, e) ?? (IsHost(method) ? null : await Rpc.Call(_session, method, p));
            Reply(id, true, result);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Burn errors travel as codes; the page words them (builder.burn.error.*).
            if (id is not null) Reply(id, false, ex switch { OperationCanceledException => "cancelled", Inlay.Burn.BurnException b => "burn:" + b.Error, _ => ex.Message });
        }
    }

    void Reply(string id, bool ok, object? result) =>
        _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new Dictionary<string, object?> { ["id"] = id, ["ok"] = ok, [ok ? "result" : "error"] = result }, Rpc.Json));

    static bool IsHost(string method) => method.StartsWith("dialog.", StringComparison.Ordinal) || method.StartsWith("window.", StringComparison.Ordinal)
        || method is "content.drop" or "job.cancel" or "burn.check" or "disc.tray" || Jobs.Contains(method);

    /// <summary>Long jobs: one at a time, progress pushed as "job" events, cancellable.</summary>
    static readonly string[] Jobs = ["iso.export", "burn.start", "burn.iso", "disc.erase", "disc.read"];

    /// <summary>Methods only the window can do. Returns a non-null result when it handled one that has a result.</summary>
    async Task<object?> Host(string method, JsonElement p, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? Opt(string name) => p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        switch (method)
        {
            case "dialog.folder":
            {
                var d = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
                return d.ShowDialog(this) == true ? d.FolderNames : Array.Empty<string>();
            }
            case "dialog.files":
            {
                var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = Opt("filter") ?? "" };
                return d.ShowDialog(this) == true ? d.FileNames : Array.Empty<string>();
            }
            case "dialog.save":
            {
                var d = new Microsoft.Win32.SaveFileDialog { Filter = Opt("filter") ?? "", FileName = Opt("name") ?? "" };
                return d.ShowDialog(this) == true ? d.FileName : "";
            }
            case "content.drop":
                // Dropped File objects arrive as WebView2 additional objects; only the host can read their paths.
                var paths = e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path).ToArray() ?? [];
                if (paths.Length > 0) await _session.Add(paths);
                return paths;
            case "window.modal":
                _dim.Visibility = p.GetProperty("on").GetBoolean() ? Visibility.Visible : Visibility.Collapsed;
                return null;
            case var job when Jobs.Contains(job):
            {
                if (_busy) throw new Inlay.Burn.BurnException(Inlay.Burn.BurnError.Busy); // one write job in the whole app (§8.5)
                _job = new CancellationTokenSource();
                _busy = true;
                var progress = new Progress<JobProgress>(j => Post("job", j));
                bool Check() => !(p.ValueKind == JsonValueKind.Object && p.TryGetProperty("check", out var c) && c.ValueKind == JsonValueKind.False);
                try
                {
                    switch (job)
                    {
                        case "iso.export": await _session.ExportIso(p.GetProperty("path").GetString()!, progress, _job.Token); return true;
                        case "burn.start": return await _session.Burn(Check(), progress, _job.Token);
                        case "burn.iso": return await _session.BurnIso(p.GetProperty("path").GetString()!, Check(), progress, _job.Token);
                        case "disc.erase": await _session.Erase(progress, _job.Token); return true;
                        default: return await _session.ReadDisc(p.GetProperty("recorder").GetString()!, progress, _job.Token);
                    }
                }
                finally { _busy = false; }
            }
            case "job.cancel":
                _job?.Cancel();
                return null;
            case "burn.check":
                _session.SetCheck(p.GetProperty("on").GetBoolean());
                return null;
            case "disc.tray":
                await _session.OpenTray();
                return null;
        }
        return null;
    }

    async void OnPreviewRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            var uri = e.Request.Uri;
            var path = uri.StartsWith(Session.DiscOrigin, StringComparison.Ordinal) ? uri[Session.DiscOrigin.Length..] : uri[Session.PreviewOrigin.Length..];
            var file = await _session.Preview(path);
            var env = _web.CoreWebView2.Environment;
            if (file is null) { e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", ""); return; }
            Stream body = file.Bytes is { } b ? new MemoryStream(b, writable: false) : File.OpenRead(file.File!);
            var headers = $"Content-Type: {file.Mime}\r\nCache-Control: no-store\r\nAccept-Ranges: bytes";
            // Seeking in the preview player asks for byte ranges.
            if (e.Request.Headers.Contains("Range") && System.Text.RegularExpressions.Regex.Match(e.Request.Headers.GetHeader("Range"), @"bytes=(\d+)-(\d*)") is { Success: true } r)
            {
                var length = body.Length;
                var start = Math.Min(long.Parse(r.Groups[1].Value), Math.Max(0, length - 1));
                var end = Math.Min(r.Groups[2].Value.Length > 0 ? long.Parse(r.Groups[2].Value) : length - 1, length - 1);
                body.Position = start;
                var slice = new MemoryStream();
                var buffer = new byte[Math.Min(end - start + 1, 8 * 1024 * 1024)];
                var read = body.Read(buffer, 0, buffer.Length);
                slice.Write(buffer, 0, read);
                slice.Position = 0;
                await body.DisposeAsync();
                e.Response = env.CreateWebResourceResponse(slice, 206, "Partial Content",
                    $"{headers}\r\nContent-Range: bytes {start}-{start + read - 1}/{length}\r\nContent-Length: {read}");
                return;
            }
            e.Response = env.CreateWebResourceResponse(body, 200, "OK", headers);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            e.Response = _web.CoreWebView2.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
        }
        finally { deferral.Complete(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TRACKMOUSEEVENT { public uint cbSize, dwFlags; public nint hwndTrack; public uint dwHoverTime; }

    [DllImport("user32.dll")] static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT e);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int value, int size);
}

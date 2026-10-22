using System.Runtime.InteropServices;
using Inlay.Launcher.WebView2;

namespace Inlay.Launcher;

/// <summary>The Win32 surface the launcher needs; source-generated marshalling only (NativeAOT).</summary>
static unsafe partial class Native
{
    public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const uint WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_ACTIVATE = 0x0006, WM_SETFOCUS = 0x0007, WM_CLOSE = 0x0010,
        WM_SETICON = 0x0080, WM_DPICHANGED = 0x02E0, WM_APPCOMMAND = 0x0319, WM_APP = 0x8000;
    public const int GWL_STYLE = -16;
    public const uint SWP_NOZORDER = 0x4, SWP_NOOWNERZORDER = 0x200, SWP_FRAMECHANGED = 0x20, SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1;
    public const int SW_SHOW = 5, SW_RESTORE = 9;
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public uint cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT { public uint length, flags, showCmd; public int minX, minY, maxX, maxY; public RECT normal; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [LibraryImport("user32.dll", SetLastError = true)] public static partial ushort RegisterClassExW(in WNDCLASSEXW wc);
    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [LibraryImport("user32.dll")] public static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] public static partial int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);
    [LibraryImport("user32.dll")] public static partial int TranslateMessage(in MSG msg);
    [LibraryImport("user32.dll")] public static partial nint DispatchMessageW(in MSG msg);
    [LibraryImport("user32.dll")] public static partial void PostQuitMessage(int code);
    [LibraryImport("user32.dll")] public static partial int PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] public static partial nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll")] public static partial int ShowWindow(nint hwnd, int cmd);
    [LibraryImport("user32.dll")] public static partial int DestroyWindow(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int GetClientRect(nint hwnd, out RECT rect);
    [LibraryImport("user32.dll")] public static partial int GetWindowRect(nint hwnd, out RECT rect);
    [LibraryImport("user32.dll")] public static partial nint LoadCursorW(nint instance, nint name);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static partial nint GetWindowLongPtr(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [LibraryImport("user32.dll")] public static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [LibraryImport("user32.dll")] public static partial int GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT wp);
    [LibraryImport("user32.dll")] public static partial int SetWindowPlacement(nint hwnd, in WINDOWPLACEMENT wp);
    [LibraryImport("user32.dll")] public static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] public static partial int GetMonitorInfoW(nint monitor, ref MONITORINFO info);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint FindWindowW(string? className, string? title);
    [LibraryImport("user32.dll")] public static partial int SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] public static partial int IsIconic(nint hwnd);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint flags);
    [LibraryImport("user32.dll")] public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("gdi32.dll")] public static partial nint CreateSolidBrush(uint colorRef);
    [LibraryImport("dwmapi.dll")] public static partial int DwmSetWindowAttribute(nint hwnd, uint attr, in int value, int size);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint GetModuleHandleW(string? name);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    [LibraryImport("ole32.dll")] public static partial int CoInitializeEx(nint reserved, uint coInit);
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateStreamOnFileEx(string file, uint mode, uint attributes, int create, nint template, out nint stream);

    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int CreateCoreWebView2EnvironmentWithOptions(string? browserFolder, string? userDataFolder, nint options,
        ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler handler);
    [LibraryImport("WebView2Loader", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetAvailableCoreWebView2BrowserVersionString(string? browserFolder, out nint version);

    // ---- task dialog (comctl32 v6 via app.manifest) ----

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TASKDIALOG_BUTTON { public int id; public char* text; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TASKDIALOGCONFIG
    {
        public uint cbSize;
        public nint hwndParent, hInstance;
        public int dwFlags, dwCommonButtons;
        public char* pszWindowTitle;
        public nint hMainIcon;
        public char* pszMainInstruction, pszContent;
        public uint cButtons;
        public TASKDIALOG_BUTTON* pButtons;
        public int nDefaultButton;
        public uint cRadioButtons;
        public nint pRadioButtons;
        public int nDefaultRadioButton;
        public char* pszVerificationText, pszExpandedInformation, pszExpandedControlText, pszCollapsedControlText;
        public nint hFooterIcon;
        public char* pszFooter;
        public nint pfCallback, lpCallbackData;
        public uint cxWidth;
    }

    [LibraryImport("comctl32.dll")]
    public static partial int TaskDialogIndirect(in TASKDIALOGCONFIG config, out int button, nint radio, nint verification);
}

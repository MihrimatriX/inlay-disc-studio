using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Inlay.Setup;

/// <summary>The Win32 the installer needs, source-generated for NativeAOT: task dialogs (comctl32 v6), shell links,
/// shell change notification.</summary>
static unsafe partial class Native
{
    public const int TDF_USE_HICON_MAIN = 0x2, TDF_ALLOW_DIALOG_CANCELLATION = 0x8, TDF_SHOW_PROGRESS_BAR = 0x200,
        TDF_CALLBACK_TIMER = 0x800, TDF_VERIFICATION_FLAG_CHECKED = 0x100, TDF_SIZE_TO_CONTENT = 0x1000000;
    public const int TDCBF_CANCEL_BUTTON = 0x8, TDCBF_CLOSE_BUTTON = 0x20;
    public const uint TDN_CREATED = 0, TDN_BUTTON_CLICKED = 2, TDN_TIMER = 4;
    public const uint WM_USER = 0x400, TDM_NAVIGATE_PAGE = WM_USER + 101, TDM_CLICK_BUTTON = WM_USER + 102,
        TDM_SET_PROGRESS_BAR_POS = WM_USER + 106;
    public const int IDCANCEL = 2;

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
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint, int> pfCallback;
        public nint lpCallbackData;
        public uint cxWidth;
    }

    [LibraryImport("comctl32.dll")]
    public static partial int TaskDialogIndirect(TASKDIALOGCONFIG* config, out int button, nint radio, out int verification);

    [LibraryImport("user32.dll")] public static partial nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint LoadIconW(nint instance, string name);
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)] public static partial nint GetModuleHandleW(string? name);
    [LibraryImport("shell32.dll")] public static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
    [LibraryImport("ole32.dll")] public static partial int CoInitializeEx(nint reserved, uint coInit);
    [LibraryImport("ole32.dll")] public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    /// <summary>A string that lives as long as the process (dialog pages are navigated to after the call that built them).</summary>
    public static char* Keep(string s) => (char*)Marshal.StringToHGlobalUni(s);

    // ---------------- shell links ----------------

    [GeneratedComInterface, Guid("000214F9-0000-0000-C000-000000000046")]
    public partial interface IShellLinkW
    {
        void GetPath(nint file, int max, nint data, uint flags);
        void GetIDList(out nint list);
        void SetIDList(nint list);
        void GetDescription(nint name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(nint dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments(nint args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out ushort key);
        void SetHotkey(ushort key);
        void GetShowCmd(out int cmd);
        void SetShowCmd(int cmd);
        void GetIconLocation(nint path, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [GeneratedComInterface, Guid("0000010b-0000-0000-C000-000000000046")]
    public partial interface IPersistFile
    {
        void GetClassID(out Guid clsid);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
        void GetCurFile(out nint file);
    }

    static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");

    /// <summary>Writes a .lnk to <paramref name="target"/>.</summary>
    public static void Shortcut(string lnk, string target, string description)
    {
        CoInitializeEx(0, 0x2 /* APARTMENTTHREADED */);
        var iid = typeof(IShellLinkW).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ShellLinkClass, 0, 0x1 /* INPROC_SERVER */, iid, out var unknown));
        try
        {
            var cw = new StrategyBasedComWrappers();
            var link = (IShellLinkW)cw.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.None);
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription(description);
            link.SetIconLocation(target, 0);
            Directory.CreateDirectory(Path.GetDirectoryName(lnk)!);
            ((IPersistFile)link).Save(lnk, true);
        }
        finally { Marshal.Release(unknown); }
    }
}

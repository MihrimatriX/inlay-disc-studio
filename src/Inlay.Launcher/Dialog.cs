namespace Inlay.Launcher;

/// <summary>Native task dialogs (§9.2 fallback, action confirmations). Text comes from i18n.</summary>
static unsafe class Dialog
{
    const int TDF_ALLOW_DIALOG_CANCELLATION = 0x8, TDF_USE_COMMAND_LINKS = 0x10, TDF_POSITION_RELATIVE_TO_WINDOW = 0x1000;
    const int IDCANCEL = 2;

    /// <summary>Shows the dialog and returns the index of the chosen button, or -1 when dismissed.</summary>
    public static int Ask(nint owner, string title, string instruction, string content, params string[] buttons)
    {
        var handles = new List<System.Runtime.InteropServices.GCHandle>();
        char* Pin(string s)
        {
            var h = System.Runtime.InteropServices.GCHandle.Alloc((s + "\0").ToCharArray(), System.Runtime.InteropServices.GCHandleType.Pinned);
            handles.Add(h);
            return (char*)h.AddrOfPinnedObject();
        }
        try
        {
            var native = stackalloc Native.TASKDIALOG_BUTTON[buttons.Length];
            for (var i = 0; i < buttons.Length; i++) native[i] = new Native.TASKDIALOG_BUTTON { id = 100 + i, text = Pin(buttons[i]) };
            var config = new Native.TASKDIALOGCONFIG
            {
                cbSize = (uint)sizeof(Native.TASKDIALOGCONFIG),
                hwndParent = owner,
                dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | TDF_USE_COMMAND_LINKS | (owner != 0 ? TDF_POSITION_RELATIVE_TO_WINDOW : 0),
                pszWindowTitle = Pin(title),
                pszMainInstruction = Pin(instruction),
                pszContent = Pin(content),
                cButtons = (uint)buttons.Length,
                pButtons = native,
                nDefaultButton = 100,
            };
            return Native.TaskDialogIndirect(config, out var chosen, 0, 0) == 0 && chosen != IDCANCEL ? chosen - 100 : -1;
        }
        finally
        {
            foreach (var h in handles) h.Free();
        }
    }
}

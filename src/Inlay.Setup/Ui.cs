using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Inlay.Setup.Native;

namespace Inlay.Setup;

/// <summary>One task dialog page: custom buttons come back as their index, Cancel/Close as -1.</summary>
sealed record Page(string Instruction, string Content, string[] Buttons, int CommonButtons = 0, string? Verify = null, bool VerifyChecked = false, string? Footer = null);

/// <summary>Installer UI made of task dialogs (the system's own look, keyboard and screen readers included). A long job
/// runs behind a progress page that then navigates to its "done" or "failed" page.</summary>
static unsafe class Ui
{
    static readonly string Title = "Inlay Disc Studio";

    public static (int Button, bool Verified) Show(Page page)
    {
        var config = Build(page, progress: false);
        TaskDialogIndirect(config, out var button, 0, out var verified);
        return (button >= 100 ? button - 100 : -1, verified != 0);
    }

    // State the native callback reads: only one progress dialog exists at a time.
    static Task? _worker;
    static CancellationTokenSource? _cts;
    static double _value;
    static Action<IProgress<double>, CancellationToken>? _work;
    static TASKDIALOGCONFIG* _done;
    static Func<Exception, Page>? _failed;
    static bool _navigated, _closing;
    public static Exception? Error { get; private set; }

    /// <summary>Shows <paramref name="progress"/> while <paramref name="work"/> runs, then <paramref name="done"/> (or the
    /// page <paramref name="failed"/> builds). Returns the final page's button, its checkbox, and whether the work finished.</summary>
    public static (int Button, bool Verified, bool Completed) Run(Page progress, Action<IProgress<double>, CancellationToken> work, Page done, Func<Exception, Page> failed)
    {
        (_work, _failed, _navigated, _closing, _value, Error) = (work, failed, false, false, 0, null);
        _done = Build(done, progress: false);
        _cts = new CancellationTokenSource();
        var config = Build(progress, progress: true);
        TaskDialogIndirect(config, out var button, 0, out var verified);
        var completed = _worker is { IsCompletedSuccessfully: true };
        return (button >= 100 ? button - 100 : -1, verified != 0, completed);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static int Callback(nint hwnd, uint msg, nint wParam, nint lParam, nint data)
    {
        switch (msg)
        {
            case TDN_CREATED when _worker is null || _worker.IsCompleted:
                _worker = Task.Run(() => _work!(new Progress(), _cts!.Token));
                break;
            case TDN_TIMER when !_navigated && _worker is not null:
                SendMessageW(hwnd, TDM_SET_PROGRESS_BAR_POS, (int)(Volatile.Read(ref _value) * 100), 0);
                if (!_worker.IsCompleted) break;
                _navigated = true;
                if (_worker.IsCanceled || _worker.Exception?.InnerException is OperationCanceledException)
                {
                    _closing = true;
                    SendMessageW(hwnd, TDM_CLICK_BUTTON, IDCANCEL, 0);
                }
                else if (_worker.Exception is { } e)
                {
                    Error = e.InnerException ?? e;
                    SendMessageW(hwnd, TDM_NAVIGATE_PAGE, 0, (nint)Build(_failed!(Error), progress: false));
                }
                else SendMessageW(hwnd, TDM_NAVIGATE_PAGE, 0, (nint)_done);
                break;
            // Cancel while working: ask the work to stop and close once it has.
            case TDN_BUTTON_CLICKED when (int)wParam == IDCANCEL && !_navigated && !_closing:
                _cts!.Cancel();
                return 1; // S_FALSE: keep the dialog until the worker has stopped
        }
        return 0;
    }

    sealed class Progress : IProgress<double> { public void Report(double value) => Volatile.Write(ref _value, value); }

    /// <summary>Builds a page in unmanaged memory that lives as long as the process (pages are navigated to later).</summary>
    static TASKDIALOGCONFIG* Build(Page p, bool progress)
    {
        var buttons = (TASKDIALOG_BUTTON*)NativeMemory.Alloc((nuint)Math.Max(1, p.Buttons.Length), (nuint)sizeof(TASKDIALOG_BUTTON));
        for (var i = 0; i < p.Buttons.Length; i++) buttons[i] = new TASKDIALOG_BUTTON { id = 100 + i, text = Keep(p.Buttons[i]) };
        var c = (TASKDIALOGCONFIG*)NativeMemory.AllocZeroed((nuint)sizeof(TASKDIALOGCONFIG));
        c->cbSize = (uint)sizeof(TASKDIALOGCONFIG);
        c->hInstance = GetModuleHandleW(null);
        c->dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | TDF_SIZE_TO_CONTENT | (progress ? TDF_SHOW_PROGRESS_BAR | TDF_CALLBACK_TIMER : 0)
            | (p.VerifyChecked ? TDF_VERIFICATION_FLAG_CHECKED : 0);
        c->dwCommonButtons = p.CommonButtons;
        c->pszWindowTitle = Keep(Title);
        // The exe's own icon (the product disc): .NET stores ApplicationIcon as icon group 32512.
        c->hMainIcon = LoadIconW(GetModuleHandleW(null), "#32512");
        if (c->hMainIcon != 0) c->dwFlags |= TDF_USE_HICON_MAIN;
        c->pszMainInstruction = Keep(p.Instruction);
        c->pszContent = Keep(p.Content);
        c->cButtons = (uint)p.Buttons.Length;
        c->pButtons = buttons;
        c->nDefaultButton = p.Buttons.Length > 0 ? 100 : 0;
        if (p.Verify is not null) c->pszVerificationText = Keep(p.Verify);
        if (p.Footer is not null) c->pszFooter = Keep(p.Footer);
        if (progress) c->pfCallback = &Callback;
        c->cxWidth = 300;
        return c;
    }
}

# Pictures one process's windows without reading the screen: PrintWindow(PW_RENDERFULLCONTENT) renders each window
# itself, so other applications can never appear in the image. The main window is drawn first, then the process's
# visible popups bottom to top (the launcher's LibVLC video and its clipped controls overlay), each through its
# window region. Usage: powershell -File scripts/window-shot.ps1 -ProcessId 1234 -Out shot.png [-Class InlayVideo]
param([Parameter(Mandatory)] [int] $ProcessId, [Parameter(Mandatory)] [string] $Out, [string] $Class = "InlayVideo")

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowShot
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr h, IntPtr rgn);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);

    public static void Save(IntPtr main, uint pid, string cls, string path)
    {
        RECT m; GetWindowRect(main, out m);
        using (var bmp = new Bitmap(m.R - m.L, m.B - m.T))
        using (var g = Graphics.FromImage(bmp))
        {
            Draw(g, main, m);
            var popups = new List<IntPtr>();
            EnumWindows((h, _) =>
            {
                uint p; GetWindowThreadProcessId(h, out p);
                var name = new StringBuilder(64); GetClassName(h, name, 64);
                if (p == pid && h != main && IsWindowVisible(h) && name.ToString() == cls) popups.Add(h);
                return true;
            }, IntPtr.Zero);
            popups.Reverse(); // EnumWindows lists top first
            foreach (var h in popups) Draw(g, h, m);
            bmp.Save(path);
        }
    }

    static void Draw(Graphics g, IntPtr h, RECT origin)
    {
        RECT r; GetWindowRect(h, out r);
        if (r.R <= r.L || r.B <= r.T) return;
        using (var one = new Bitmap(r.R - r.L, r.B - r.T))
        {
            using (var og = Graphics.FromImage(one)) { var dc = og.GetHdc(); PrintWindow(h, dc, 2); og.ReleaseHdc(dc); }
            var rgn = CreateRectRgn(0, 0, 0, 0);
            var clipped = GetWindowRgn(h, rgn) > 1; // SIMPLEREGION or COMPLEXREGION
            var state = g.Save();
            if (clipped) { var region = Region.FromHrgn(rgn); region.Translate(r.L - origin.L, r.T - origin.T); g.Clip = region; }
            g.DrawImage(one, r.L - origin.L, r.T - origin.T);
            g.Restore(state);
            DeleteObject(rgn);
        }
    }
}
'@

$main = (Get-Process -Id $ProcessId).MainWindowHandle
if ($main -eq [IntPtr]::Zero) { exit 3 }
[WindowShot]::Save($main, $ProcessId, $Class, $Out)

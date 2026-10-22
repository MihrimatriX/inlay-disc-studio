using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Inlay.Core;

namespace Inlay.Launcher;

/// <summary>Failure reported to the page as a DiscErrorCode (sdk/disc-sdk.d.ts).</summary>
public sealed class BridgeError(string code) : Exception(code)
{
    public string Code => Message;
}

/// <summary>What the launcher asks of Windows. One production implementation; tests substitute it.</summary>
public interface IShell
{
    /// <summary>ShellExecute a program; null when Windows handed the file to an already running app.</summary>
    IShellProcess? Start(string file, string arguments, string? workingDir, bool elevated);

    /// <summary>Opens a document, folder or https URL with its default handler.</summary>
    void Open(string target, string? arguments = null);

    /// <summary>Native yes/no question; true means go ahead.</summary>
    bool Confirm(string message);
}

public interface IShellProcess
{
    Task<int> WaitForExitAsync();
}

public sealed record RunResult(int? ExitCode);

/// <summary>Runs manifest actions — and only those (§9.3). Page input is an action id, never a path.</summary>
public sealed class ActionRunner(DiscManifest manifest, string root, IShell shell, Action<string, object> emit, Action closeLauncher)
{
    /// <summary>Installer exit codes that mean success: done, "a newer version is already installed" (VC++), reboot needed.</summary>
    public static bool Succeeded(int exitCode) => exitCode is 0 or 1638 or 3010;

    readonly ConcurrentDictionary<string, byte> _running = new();

    public DiscAction Find(string? id) =>
        manifest.Actions.FirstOrDefault(a => a.Id == id) ?? throw new BridgeError("unknown-action");

    public async Task<RunResult> Run(string? id, Func<IReadOnlyList<BundleStep>, Task<BundleResult>> runSteps)
    {
        var action = Find(id);
        if (action.Kind is ActionKind.Play or ActionKind.Playlist or ActionKind.Slideshow)
            throw new BridgeError("unknown-action"); // media actions are played by the page SDK
        if (!_running.TryAdd(action.Id, 0)) throw new BridgeError("busy");
        try
        {
            if (action.Confirm is { } question && !shell.Confirm(question)) throw new BridgeError("cancelled");
            switch (action.Kind)
            {
                case ActionKind.Url:
                    if (!Uri.TryCreate(action.Target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                        throw new BridgeError("unknown-action");
                    if (!shell.Confirm(string.Format(Text.Get("launcher.openUrl", manifest.Disc.Locale), uri.Host))) throw new BridgeError("cancelled");
                    shell.Open(uri.AbsoluteUri);
                    return new RunResult(null);
                case ActionKind.Open:
                    shell.Open(Resolve(action.Target, mustBeFile: false));
                    return new RunResult(null);
                case ActionKind.OpenFolder:
                    var path = Resolve(action.Target, mustBeFile: false);
                    if (Directory.Exists(path)) shell.Open(path);
                    else shell.Open("explorer.exe", $"/select,\"{path}\"");
                    return new RunResult(null);
            }

            var prerequisites = OwnerOf(action.Id)?.Requirements?.Prerequisites ?? [];
            if (action.Kind == ActionKind.Install && prerequisites.Count > 0)
            {
                // Prerequisites install silently first, all under one UAC prompt (§7.0).
                var steps = prerequisites.Select(p => new BundleStep(Find(p).Id, null, Silent: true))
                    .Append(new BundleStep(action.Id, OwnerOf(action.Id)?.Id, Silent: false)).ToList();
                emit("action-started", new ActionEvent(action.Id, null));
                var result = await runSteps(steps);
                var exit = result.Failed.FirstOrDefault()?.ExitCode ?? 0;
                emit("action-exited", new ActionEvent(action.Id, exit));
                return Finish(exit);
            }

            var (file, args) = Command(action, silent: false);
            var process = shell.Start(file, args, WorkingDir(action), action.Elevated == true);
            emit("action-started", new ActionEvent(action.Id, null));
            if (action.CloseLauncher == CloseLauncher.OnStart) closeLauncher();
            if (process is null) return new RunResult(null);
            var code = await process.WaitForExitAsync();
            emit("action-exited", new ActionEvent(action.Id, code));
            if (action.CloseLauncher == CloseLauncher.OnExit) closeLauncher();
            return Finish(code);
        }
        finally { _running.TryRemove(action.Id, out _); }
    }

    static RunResult Finish(int exitCode) => exitCode == 3010 ? throw new BridgeError("reboot-required") : new RunResult(exitCode);

    public App? OwnerOf(string actionId) => manifest.Items.OfType<App>().FirstOrDefault(a => a.Actions?.Contains(actionId) == true);

    /// <summary>File and command line for install/run actions. MSI packages go through msiexec so "runas" applies.</summary>
    public (string File, string Args) Command(DiscAction action, bool silent)
    {
        if (action.Kind is not (ActionKind.Install or ActionKind.Run)) throw new BridgeError("unknown-action");
        var target = Resolve(action.Target, mustBeFile: true);
        var args = silent ? OwnerOf(action.Id)?.Bundle?.SilentArgs ?? action.Args ?? [] : action.Args ?? [];
        return Path.GetExtension(target).Equals(".msi", StringComparison.OrdinalIgnoreCase)
            ? ("msiexec.exe", CommandLine.Join(["/i", target, .. args]))
            : (target, CommandLine.Join(args));
    }

    public string? WorkingDir(DiscAction action) =>
        action.WorkingDir is { } wd ? Resolve(wd, mustBeFile: false) : Path.GetDirectoryName(Resolve(action.Target, mustBeFile: true));

    string Resolve(string? rel, bool mustBeFile)
    {
        var full = RelPath.Resolve(root, rel) ?? throw new BridgeError("not-found");
        if (!(File.Exists(full) || (!mustBeFile && Directory.Exists(full)))) throw new BridgeError("not-found");
        return full;
    }
}

public sealed record ActionEvent(string Id, int? ExitCode);

public static class CommandLine
{
    /// <summary>Joins arguments with the quoting rules CommandLineToArgvW / the MSVC runtime parse back.</summary>
    public static string Join(IEnumerable<string> args) => string.Join(' ', args.Select(Quote));

    public static string Quote(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        var sb = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { slashes++; continue; }
            sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
            slashes = 0;
        }
        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }
}

/// <summary>ShellExecute through System.Diagnostics.Process (it calls ShellExecuteEx with "runas" when asked).</summary>
public sealed class WindowsShell(Func<string, bool> confirm) : IShell
{
    const int ErrorCancelled = 1223, ErrorFileNotFound = 2, ErrorPathNotFound = 3;

    public IShellProcess? Start(string file, string arguments, string? workingDir, bool elevated) =>
        Wrap(() =>
        {
            var p = Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = true,
                Verb = elevated ? "runas" : "",
                WorkingDirectory = workingDir ?? "",
            });
            return p is null ? null : new Running(p);
        });

    public void Open(string target, string? arguments = null) =>
        Wrap(() => Process.Start(new ProcessStartInfo(target, arguments ?? "") { UseShellExecute = true })?.Dispose());

    public bool Confirm(string message) => confirm(message);

    static T Wrap<T>(Func<T> start)
    {
        try { return start(); }
        catch (Win32Exception e) when (e.NativeErrorCode == ErrorCancelled) { throw new BridgeError("cancelled"); }
        catch (Win32Exception e) when (e.NativeErrorCode is ErrorFileNotFound or ErrorPathNotFound) { throw new BridgeError("not-found"); }
    }

    static void Wrap(Action start) => Wrap<int>(() => { start(); return 0; });

    sealed class Running(Process p) : IShellProcess
    {
        public async Task<int> WaitForExitAsync()
        {
            using (p)
            {
                await p.WaitForExitAsync();
                return p.ExitCode;
            }
        }
    }
}

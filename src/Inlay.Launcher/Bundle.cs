using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Inlay.Core;

namespace Inlay.Launcher;

/// <summary>One installer run. <paramref name="AppId"/> is the bundle row it belongs to (null for a prerequisite).</summary>
public sealed record BundleStep(string ActionId, string? AppId, bool Silent);

public sealed record BundleFailure(string Id, int ExitCode);
public sealed record BundleResult(IReadOnlyList<string> Installed, IReadOnlyList<BundleFailure> Failed);

/// <summary>"bundle-progress" event payload (sdk/disc-sdk.d.ts).</summary>
public sealed record BundleProgress(string Id, int Index, int Total, int Percent, string State, int? ExitCode = null);

public static class BundlePlanner
{
    /// <summary>Install order for the program bundle (§7.0): required apps first even if not asked for, then by
    /// bundle.order; each app runs its first install action silently. Unknown or non-app ids are refused.</summary>
    public static IReadOnlyList<BundleStep> Plan(DiscManifest m, IEnumerable<string> appIds)
    {
        var apps = m.Items.OfType<App>().ToDictionary(a => a.Id);
        var chosen = new HashSet<string>();
        foreach (var id in appIds)
            if (!apps.ContainsKey(id)) throw new BridgeError("unknown-action");
            else chosen.Add(id);
        foreach (var a in apps.Values.Where(a => a.Bundle?.Required == true)) chosen.Add(a.Id);

        return chosen.Select(id => apps[id])
            .OrderBy(a => a.Bundle?.Required == true ? 0 : 1)
            .ThenBy(a => a.Bundle?.Order ?? int.MaxValue)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .Select(a => new BundleStep(InstallAction(m, a), a.Id, Silent: true))
            .ToList();
    }

    static string InstallAction(DiscManifest m, App app) =>
        app.Actions?.FirstOrDefault(id => m.Actions.Any(x => x.Id == id && x.Kind == ActionKind.Install))
        ?? throw new BridgeError("unknown-action");

    public static string Encode(IEnumerable<BundleStep> steps) =>
        string.Join(' ', steps.Select(s => $"{s.ActionId}:{(s.Silent ? 's' : 'i')}:{s.AppId ?? "-"}"));

    public static IReadOnlyList<BundleStep> Decode(IEnumerable<string> tokens) => tokens.Select(t =>
    {
        var p = t.Split(':');
        if (p.Length != 3) throw new BridgeError("unknown-action");
        return new BundleStep(p[0], p[2] == "-" ? null : p[2], p[1] == "s");
    }).ToList();
}

/// <summary>Executes bundle steps. Elevated launchers run them in process; otherwise the launcher starts itself once
/// with "runas" as <c>--bundle-worker</c> and relays its progress (one UAC prompt for the whole bundle).</summary>
public sealed class BundleRunner(string root, ActionRunner actions, IShell shell, Action<BundleProgress> progress)
{
    public Task<BundleResult> Run(IReadOnlyList<BundleStep> steps) =>
        Environment.IsPrivilegedProcess ? Task.Run(() => Execute(steps, actions, shell, progress)) : ViaWorker(steps);

    /// <summary>Runs the steps one after another and reports each row (the worker uses this too).</summary>
    public static async Task<BundleResult> Execute(IReadOnlyList<BundleStep> steps, ActionRunner actions, IShell shell, Action<BundleProgress> report)
    {
        var installed = new List<string>();
        var failed = new List<BundleFailure>();
        for (var i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var id = s.AppId ?? s.ActionId;
            report(new BundleProgress(id, i, steps.Count, i * 100 / steps.Count, "installing"));
            int code;
            try
            {
                var action = actions.Find(s.ActionId);
                var (file, args) = actions.Command(action, s.Silent);
                var p = shell.Start(file, args, actions.WorkingDir(action), elevated: false);
                code = p is null ? 0 : await p.WaitForExitAsync();
            }
            catch (BridgeError) { code = -1; }
            var ok = ActionRunner.Succeeded(code);
            if (ok) installed.Add(id); else failed.Add(new BundleFailure(id, code));
            report(new BundleProgress(id, i, steps.Count, (i + 1) * 100 / steps.Count, ok ? "done" : "failed", code));
        }
        return new BundleResult(installed, failed);
    }

    async Task<BundleResult> ViaWorker(IReadOnlyList<BundleStep> steps)
    {
        var pipe = $"inlay-bundle-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var self = Environment.ProcessPath ?? throw new BridgeError("internal");
        var worker = shell.Start(self, CommandLine.Join(["--root", root, "--bundle-worker", pipe, .. BundlePlanner.Encode(steps).Split(' ')]), null, elevated: true)
            ?? throw new BridgeError("internal");
        var exited = worker.WaitForExitAsync();

        var connect = server.WaitForConnectionAsync();
        if (await Task.WhenAny(connect, exited) == exited) throw new BridgeError("internal");
        using var reader = new StreamReader(server);
        BundleResult? result = null;
        while (await reader.ReadLineAsync() is { } line)
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("installed", out _))
                result = doc.RootElement.Deserialize(LauncherJson.Default.BundleResult);
            else
                progress(doc.RootElement.Deserialize(LauncherJson.Default.BundleProgress)!);
        }
        await exited;
        return result ?? throw new BridgeError("internal");
    }

    /// <summary>Entry point of the elevated <c>--bundle-worker</c> process: re-reads the manifest from the disc and
    /// accepts nothing but action ids from its unelevated parent.</summary>
    public static async Task<int> WorkerMain(string root, string pipe, IEnumerable<string> tokens)
    {
        var manifest = DiscLoader.Load(root).Manifest ?? throw new BridgeError("internal");
        var shell = new WindowsShell(_ => false);
        var actions = new ActionRunner(manifest, root, shell, (_, _) => { }, () => { });
        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.Out);
        await client.ConnectAsync(10_000);
        await using var writer = new StreamWriter(client) { AutoFlush = true };
        var result = await Execute(BundlePlanner.Decode(tokens), actions, shell,
            p => writer.WriteLine(JsonSerializer.Serialize(p, LauncherJson.Default.BundleProgress)));
        await writer.WriteLineAsync(JsonSerializer.Serialize(result, LauncherJson.Default.BundleResult));
        return 0;
    }
}

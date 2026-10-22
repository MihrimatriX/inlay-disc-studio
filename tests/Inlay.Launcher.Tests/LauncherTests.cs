using System.Text.Json;
using FluentAssertions;
using Inlay.Core;
using Inlay.Launcher;
using Xunit;

namespace Inlay.Launcher.Tests;

sealed class FakeShell : IShell
{
    public readonly List<(string File, string Args, string? Dir, bool Elevated)> Started = [];
    public readonly List<string> Opened = [];
    public bool Answer = true;
    public TaskCompletionSource<int> Exit = new();
    public Func<string, IShellProcess?>? OnStart;

    public IShellProcess? Start(string file, string arguments, string? workingDir, bool elevated)
    {
        Started.Add((file, arguments, workingDir, elevated));
        return OnStart is not null ? OnStart(file) : new Proc(Exit.Task);
    }

    public void Open(string target, string? arguments = null) => Opened.Add(arguments is null ? target : $"{target} {arguments}");
    public bool Confirm(string message) => Answer;

    public sealed class Proc(Task<int> exit) : IShellProcess { public Task<int> WaitForExitAsync() => exit; }
}

public sealed class LauncherTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("inlay-disc-").FullName;
    readonly FakeShell _shell = new();
    readonly List<(string Name, object Data)> _events = [];
    int _closed;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    DiscManifest Manifest(Func<DiscManifest, DiscManifest>? edit = null)
    {
        var m = JsonSerializer.Deserialize(File.ReadAllText("samples/manifest.bundle.json"), InlayJson.Default.DiscManifest)!;
        foreach (var a in m.Actions)
        {
            var p = Path.Combine(_root, a.Target!.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "MZ");
        }
        return edit is null ? m : edit(m);
    }

    ActionRunner Runner(DiscManifest m) => new(m, _root, _shell, (n, d) => _events.Add((n, d)), () => _closed++);

    static Task<BundleResult> NoBundle(IReadOnlyList<BundleStep> _) => throw new InvalidOperationException("no bundle expected");

    static DiscManifest WithAction(DiscManifest m, DiscAction a) => m with { Actions = [.. m.Actions, a] };

    async Task<string> Code(Func<Task> act)
    {
        try { await act(); return "ok"; }
        catch (BridgeError e) { return e.Code; }
    }

    [Fact]
    public async Task Unknown_action_ids_are_refused_and_nothing_starts()
    {
        (await Code(() => Runner(Manifest()).Run("format-c", NoBundle))).Should().Be("unknown-action");
        (await Code(() => Runner(Manifest()).Run(null, NoBundle))).Should().Be("unknown-action");
        _shell.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task Install_starts_the_resolved_target_elevated_and_reports_exit()
    {
        var run = Runner(Manifest()).Run("inst-game", NoBundle);
        _shell.Exit.SetResult(0);
        (await run).ExitCode.Should().Be(0);
        _shell.Started.Should().ContainSingle().Which.Should().Be(
            (Path.Combine(_root, "Setup", "Game", "setup.exe"), "", Path.Combine(_root, "Setup", "Game"), true));
        _events.Select(e => e.Name).Should().Equal("action-started", "action-exited");
        _events[1].Data.Should().Be(new ActionEvent("inst-game", 0));
    }

    [Fact]
    public async Task Msi_packages_run_through_msiexec()
    {
        var run = Runner(Manifest()).Run("inst-editor", NoBundle);
        _shell.Exit.SetResult(0);
        await run;
        _shell.Started.Single().Should().Match<(string File, string Args, string? Dir, bool Elevated)>(s =>
            s.File == "msiexec.exe" && s.Args == $"/i {Path.Combine(_root, "Setup", "Editor", "LevelEditor.msi")}");
    }

    [Fact]
    public async Task Same_action_twice_is_busy()
    {
        var runner = Runner(Manifest());
        var first = runner.Run("inst-game", NoBundle);
        (await Code(() => runner.Run("inst-game", NoBundle))).Should().Be("busy");
        _shell.Exit.SetResult(0);
        await first;
        _shell.Exit = new TaskCompletionSource<int>();
        var again = runner.Run("inst-game", NoBundle);
        _shell.Exit.SetResult(1);
        (await again).ExitCode.Should().Be(1, "the slot frees once the first run exits");
    }

    [Fact]
    public async Task Exit_3010_asks_for_a_reboot()
    {
        var run = Runner(Manifest()).Run("inst-game", NoBundle);
        _shell.Exit.SetResult(3010);
        (await Code(() => run)).Should().Be("reboot-required");
    }

    [Fact]
    public async Task Declined_confirmation_cancels_before_starting()
    {
        var m = Manifest(m => m with { Actions = [.. m.Actions.Select(a => a.Id == "inst-game" ? a with { Confirm = "İmzasız. Devam?" } : a)] });
        _shell.Answer = false;
        (await Code(() => Runner(m).Run("inst-game", NoBundle))).Should().Be("cancelled");
        _shell.Started.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Setup/Missing/setup.exe")]
    [InlineData("../outside.exe")]
    [InlineData("Setup/../../outside.exe")]
    public async Task Targets_outside_the_disc_or_missing_are_not_found(string target)
    {
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_root)!, "outside.exe"), "MZ");
        var m = WithAction(Manifest(), new DiscAction { Id = "evil", Label = "x", Kind = ActionKind.Run, Target = target });
        (await Code(() => Runner(m).Run("evil", NoBundle))).Should().Be("not-found");
        _shell.Started.Should().BeEmpty();
    }

    [Fact]
    public async Task Url_actions_open_https_after_confirmation_only()
    {
        var m = WithAction(Manifest(), new DiscAction { Id = "web", Label = "Site", Kind = ActionKind.Url, Target = "https://example.com/oyun" });
        await Runner(m).Run("web", NoBundle);
        _shell.Opened.Should().Equal("https://example.com/oyun");

        var http = WithAction(Manifest(), new DiscAction { Id = "web", Label = "Site", Kind = ActionKind.Url, Target = "http://example.com" });
        (await Code(() => Runner(http).Run("web", NoBundle))).Should().Be("unknown-action");
        _shell.Answer = false;
        (await Code(() => Runner(m).Run("web", NoBundle))).Should().Be("cancelled");
    }

    [Fact]
    public async Task OpenFolder_selects_files_in_explorer()
    {
        var m = WithAction(Manifest(), new DiscAction { Id = "where", Label = "Göster", Kind = ActionKind.OpenFolder, Target = "Setup/Game/setup.exe" });
        await Runner(m).Run("where", NoBundle);
        _shell.Opened.Should().Equal($"explorer.exe /select,\"{Path.Combine(_root, "Setup", "Game", "setup.exe")}\"");
    }

    [Fact]
    public async Task Close_on_start_closes_without_waiting()
    {
        var m = Manifest(m => m with { Actions = [.. m.Actions.Select(a => a.Id == "inst-game" ? a with { CloseLauncher = CloseLauncher.OnStart } : a)] });
        _shell.OnStart = _ => null;
        await Runner(m).Run("inst-game", NoBundle);
        _closed.Should().Be(1);
    }

    [Fact]
    public async Task Install_with_prerequisites_runs_them_first_in_one_bundle()
    {
        var m = Manifest(m => m with
        {
            Items = [.. m.Items.Select(i => i is App { Id: "ap-game" } a ? a with { Requirements = new Requirements { Prerequisites = ["inst-vc"] } } : i)],
        });
        IReadOnlyList<BundleStep>? steps = null;
        await Runner(m).Run("inst-game", s => { steps = s; return Task.FromResult(new BundleResult(["ap-game"], [])); });
        steps.Should().Equal(new BundleStep("inst-vc", null, true), new BundleStep("inst-game", "ap-game", false));
    }

    [Fact]
    public void Bundle_plan_puts_required_first_then_order()
    {
        var plan = BundlePlanner.Plan(Manifest(), ["ap-sound", "ap-game"]);
        plan.Select(s => s.AppId).Should().Equal("ap-vc", "ap-game", "ap-sound");
        plan.Should().OnlyContain(s => s.Silent);
        FluentActions.Invoking(() => BundlePlanner.Plan(Manifest(), ["inst-game"])).Should().Throw<BridgeError>().Which.Code.Should().Be("unknown-action");
    }

    [Fact]
    public void Bundle_steps_survive_the_worker_command_line()
    {
        var plan = BundlePlanner.Plan(Manifest(), ["ap-game"]);
        BundlePlanner.Decode(BundlePlanner.Encode(plan).Split(' ')).Should().Equal(plan);
    }

    [Fact]
    public async Task Bundle_uses_silent_args_and_reports_each_row()
    {
        var m = Manifest();
        _shell.OnStart = f => new FakeShell.Proc(Task.FromResult(f.EndsWith("setup.exe") ? 1 : 0));
        var progress = new List<BundleProgress>();
        var result = await BundleRunner.Execute(BundlePlanner.Plan(m, ["ap-game"]), Runner(m), _shell, progress.Add);
        _shell.Started.Select(s => s.Args).Should().Equal("/install /quiet /norestart", "/VERYSILENT");
        result.Installed.Should().Equal("ap-vc");
        result.Failed.Should().Equal(new BundleFailure("ap-game", 1));
        progress.Select(p => (p.Id, p.State)).Should().Equal(("ap-vc", "installing"), ("ap-vc", "done"), ("ap-game", "installing"), ("ap-game", "failed"));
        progress[^1].Percent.Should().Be(100);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    public void Arguments_are_quoted_for_argv(string arg, string quoted) => CommandLine.Quote(arg).Should().Be(quoted);
}

public sealed class BridgeTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("inlay-bridge-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);

    sealed class Host : IBridgeHost
    {
        public bool? Fullscreen;
        public SystemInfo Probe() => new("0.1.0", "1", "Windows 11", new Dictionary<string, long>(), 1, false, false, false, "tr-TR");
        public void SetFullscreen(bool on) => Fullscreen = on;
        public void Close() { }
        public Task<string?> Player(string cmd, string? id, JsonElement opts) => throw new BridgeError("unsupported-codec");
    }

    (Bridge, Host) Make()
    {
        var m = JsonSerializer.Deserialize(File.ReadAllText("samples/manifest.bundle.json"), InlayJson.Default.DiscManifest)!;
        var shell = new FakeShell();
        var actions = new ActionRunner(m, _root, shell, (_, _) => { }, () => { });
        var host = new Host();
        return (new Bridge(actions, new BundleRunner(_root, actions, shell, _ => { }), m, host), host);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"v":2,"req":1,"cmd":"run"}""")]
    [InlineData("""{"v":1,"cmd":"run"}""")]
    public async Task Non_protocol_messages_get_no_reply(string message) => (await Make().Item1.Handle(message)).Should().BeNull();

    [Fact]
    public async Task Unknown_commands_and_actions_are_refused()
    {
        var (bridge, _) = Make();
        (await bridge.Handle("""{"v":1,"req":7,"cmd":"eval","id":"x"}""")).Should().Be("""{"v":1,"res":7,"ok":false,"error":"unknown-action"}""");
        (await bridge.Handle("""{"v":1,"req":8,"cmd":"run","id":"C:/Windows/System32/cmd.exe"}""")).Should().Be("""{"v":1,"res":8,"ok":false,"error":"unknown-action"}""");
    }

    [Fact]
    public async Task System_and_fullscreen_round_trip()
    {
        var (bridge, host) = Make();
        var reply = JsonDocument.Parse((await bridge.Handle("""{"v":1,"req":1,"cmd":"system"}"""))!).RootElement;
        reply.GetProperty("ok").GetBoolean().Should().BeTrue();
        reply.GetProperty("data").GetProperty("os").GetString().Should().Be("Windows 11");
        (await bridge.Handle("""{"v":1,"req":2,"cmd":"setFullscreen","opts":{"on":true}}""")).Should().Be("""{"v":1,"res":2,"ok":true}""");
        host.Fullscreen.Should().BeTrue();
    }

    [Fact]
    public async Task Media_errors_come_back_as_codes() =>
        (await Make().Item1.Handle("""{"v":1,"req":3,"cmd":"play","id":"ap-game"}""")).Should().Be("""{"v":1,"res":3,"ok":false,"error":"unsupported-codec"}""");
}

public sealed class DiscLoaderTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("inlay-load-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);

    void Put(string json)
    {
        Directory.CreateDirectory(Path.Combine(_root, ".disc"));
        File.WriteAllText(Path.Combine(_root, ".disc", "manifest.json"), json);
    }

    [Fact]
    public void Valid_manifest_loads()
    {
        Put(File.ReadAllText("samples/manifest.mixed.json"));
        DiscLoader.Load(_root).Should().Match<LoadedDisc>(d => d.Manifest != null && d.Error == null);
    }

    [Fact]
    public void Missing_manifest_is_reported() => DiscLoader.Load(_root).Error.Should().Be("missing");

    [Fact]
    public void Schema_violations_are_refused()
    {
        Put(File.ReadAllText("samples/manifest.bundle.json").Replace("Setup/Game/setup.exe", "../../Windows/System32/cmd.exe"));
        DiscLoader.Load(_root).Should().Match<LoadedDisc>(d => d.Manifest == null && d.Error == "schema");
    }

    [Fact]
    public void Broken_json_is_refused()
    {
        Put("{ not json");
        DiscLoader.Load(_root).Manifest.Should().BeNull();
    }
}

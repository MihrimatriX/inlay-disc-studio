using System.Text.Json;
using FluentAssertions;
using Inlay.Core;
using Inlay.Launcher;
using Xunit;

namespace Inlay.Launcher.Tests;

sealed class FakeRegistry(params UninstallEntry[] entries) : IUninstallRegistry
{
    public readonly List<UninstallEntry> List = [.. entries];
    public IEnumerable<UninstallEntry> Entries() => List;
}

public sealed class InstalledAppsTests : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory("inlay-disc-").FullName;
    readonly string _programs = Directory.CreateTempSubdirectory("inlay-programs-").FullName;
    readonly FakeShell _shell = new();
    readonly List<(string Name, object Data)> _events = [];

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_programs, recursive: true);
    }

    static App Game(string title = "Oyunumuz", string? publisher = null) => new() { Id = "ap-game", Title = title, Publisher = publisher };

    string Exe(string rel, int size = 2)
    {
        var p = Path.Combine(_programs, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, new byte[size]);
        return p;
    }

    /// <summary>The bundle sample: "Oyunumuz" ships only setup.exe, no run action.</summary>
    DiscManifest Manifest()
    {
        var m = JsonSerializer.Deserialize(File.ReadAllText("samples/manifest.bundle.json"), InlayJson.Default.DiscManifest)!;
        foreach (var a in m.Actions)
        {
            var p = Path.Combine(_root, a.Target!.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "MZ");
        }
        return m;
    }

    ActionRunner Runner(IUninstallRegistry registry) => new(Manifest(), _root, _shell, (n, d) => _events.Add((n, d)), () => { }, registry);

    [Fact]
    public void DisplayIcon_names_the_main_exe()
    {
        var exe = Exe(@"Oyunumuz\bin\game.exe");
        var reg = new FakeRegistry(new UninstallEntry("Oyunumuz 1.2", "Stüdyo", Path.Combine(_programs, "Oyunumuz"), $"\"{exe}\",0"));
        InstalledApps.FindExe(Game(), reg).Should().Be(exe);
    }

    [Fact]
    public void Uninstaller_icon_falls_back_to_the_install_folder()
    {
        var dir = Path.Combine(_programs, "Oyunumuz");
        var uninst = Exe(@"Oyunumuz\unins000.exe", 500);
        Exe(@"Oyunumuz\UnityCrashHandler64.exe", 900);
        Exe(@"Oyunumuz\launcher.exe", 10);
        var main = Exe(@"Oyunumuz\Oyunumuz.exe", 5);
        var reg = new FakeRegistry(new UninstallEntry("Oyunumuz", null, dir, uninst));
        InstalledApps.FindExe(Game(), reg).Should().Be(main, "the exe named like the title wins over larger helpers");
    }

    [Fact]
    public void Without_a_title_match_the_shallowest_largest_exe_is_taken()
    {
        var dir = Path.Combine(_programs, "OG");
        Exe(@"OG\tools\big.exe", 900);
        var main = Exe(@"OG\run.exe", 100);
        Exe(@"OG\small.exe", 10);
        InstalledApps.FindExe(Game(), new FakeRegistry(new UninstallEntry("OYUNUMUZ™", null, $"\"{dir}\"", null))).Should().Be(main);
    }

    [Fact]
    public void Other_programs_and_other_publishers_do_not_match()
    {
        var exe = Exe(@"Other\game.exe");
        var reg = new FakeRegistry(
            new UninstallEntry("Başka Oyun", null, null, exe),
            new UninstallEntry("Oyunumuz", "Rakip Yazılım", null, exe),
            new UninstallEntry(null, null, null, exe));
        InstalledApps.FindExe(Game(publisher: "Stüdyo"), reg).Should().BeNull();
        InstalledApps.FindExe(Game(), new FakeRegistry(new UninstallEntry("Oyunumuz", null, null, Path.Combine(_programs, "missing.exe")))).Should().BeNull();
    }

    [Fact]
    public void The_closest_display_name_wins()
    {
        var editor = Exe(@"Editor\editor.exe");
        var game = Exe(@"Game\game.exe");
        var reg = new FakeRegistry(new UninstallEntry("Oyunumuz Harita Editörü", null, null, editor), new UninstallEntry("Oyunumuz", null, null, game));
        InstalledApps.FindExe(Game(), reg).Should().Be(game);
    }

    [Fact]
    public async Task Successful_install_reports_that_the_installed_copy_can_run_and_runInstalled_starts_it()
    {
        var reg = new FakeRegistry();
        var runner = Runner(reg);
        var install = runner.Run("inst-game", _ => throw new InvalidOperationException());
        var exe = Exe(@"Oyunumuz\Oyunumuz.exe");
        reg.List.Add(new UninstallEntry("Oyunumuz", null, Path.GetDirectoryName(exe), null)); // the installer registers itself
        _shell.Exit.SetResult(0);
        (await install).Should().Be(new RunResult(0, true));

        runner.RunInstalled("ap-game").Should().Be(new RunResult(null));
        _shell.Started.Last().Should().Be((exe, "", Path.GetDirectoryName(exe), false));
        _events.Last().Should().Be(("action-started", (object)new ActionEvent("ap-game", null)));
    }

    [Fact]
    public async Task Failed_or_unregistered_installs_keep_canRunInstalled_unset()
    {
        var exe = Exe(@"Oyunumuz\Oyunumuz.exe");
        var runner = Runner(new FakeRegistry(new UninstallEntry("Oyunumuz", null, Path.GetDirectoryName(exe), null)));
        var failed = runner.Run("inst-game", _ => throw new InvalidOperationException());
        _shell.Exit.SetResult(1603);
        (await failed).CanRunInstalled.Should().BeNull();

        _shell.Exit = new TaskCompletionSource<int>();
        var other = Runner(new FakeRegistry()).Run("inst-game", _ => throw new InvalidOperationException());
        _shell.Exit.SetResult(0);
        (await other).CanRunInstalled.Should().BeNull();
    }

    [Fact]
    public void RunInstalled_takes_only_app_ids_and_needs_a_registered_copy()
    {
        var runner = Runner(new FakeRegistry());
        FluentActions.Invoking(() => runner.RunInstalled("inst-game")).Should().Throw<BridgeError>().Which.Code.Should().Be("unknown-action");
        FluentActions.Invoking(() => runner.RunInstalled(@"C:\Windows\notepad.exe")).Should().Throw<BridgeError>().Which.Code.Should().Be("unknown-action");
        FluentActions.Invoking(() => runner.RunInstalled("ap-game")).Should().Throw<BridgeError>().Which.Code.Should().Be("not-found");
        _shell.Started.Should().BeEmpty();
    }
}

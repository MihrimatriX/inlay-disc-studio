using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Inlay.Burn;
using Inlay.Core;
using Xunit;

namespace Inlay.Launcher.Tests;

/// <summary>§12 integration: ISO → Mount-DiskImage → Baslat.exe from the read-only disc → CDP (Playwright).
/// Needs the AOT launcher (dotnet publish src/Inlay.Launcher -c Release -o artifacts/launcher), tools/ffmpeg and npm ci.</summary>
public sealed class IntegrationTests : IDisposable
{
    static readonly string Repo = FindRepo();
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-int-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    static string FindRepo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Inlay.slnx"))) d = d.Parent;
        return d?.FullName ?? "";
    }

    static string Launcher => Path.Combine(Repo, "artifacts", "launcher", "Baslat.exe");
    static string Ffmpeg => Path.Combine(Repo, "tools", "ffmpeg", "bin", "ffmpeg.exe");

    string Put(string rel, string content)
    {
        var p = Path.Combine(_dir, rel.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public async Task Menu_opens_from_a_mounted_iso_refuses_foreign_actions_and_plays_html_video()
    {
        Assert.SkipUnless(File.Exists(Launcher), "publish the AOT launcher to artifacts/launcher first");
        Assert.SkipUnless(File.Exists(Ffmpeg), "tools/ffmpeg is missing");
        Assert.SkipUnless(Directory.Exists(Path.Combine(Repo, "node_modules", "playwright-core")), "run npm ci");
        Assert.SkipUnless(Environment.UserInteractive && Environment.GetEnvironmentVariable("CI") is null, "opens real windows; run on a desktop session");

        var clip = Path.Combine(_dir, "clip.mp4");
        Run(Ffmpeg, $"-v error -y -f lavfi -i testsrc2=size=640x360:rate=25 -f lavfi -i sine=frequency=440 -t 3 -c:v libopenh264 -c:a aac -movflags +faststart \"{clip}\"");
        var page = Put(".disc/ui/index.html", """
            <!doctype html><html lang="tr"><meta charset="utf-8"><title>Menü</title>
            <body><video id="v" muted></video></body></html>
            """);
        var manifest = Path.Combine(Repo, "samples", "manifest.mixed.json");
        var plan = new DiscImagePlan("INLAY_TEST", DiscMedia.Cd700,
        [
            new("Baslat.exe", Launcher),
            new(".disc/manifest.json", manifest),
            new(".disc/ui/index.html", page),
            new("Film/yapim-hikayesi.mp4", clip),
        ], [".disc"]);
        var iso = Path.Combine(_dir, "disc.iso");
        DiscImage.ExportIso(plan, iso, ct: TestContext.Current.CancellationToken);

        var drive = Ps($"(Mount-DiskImage -ImagePath '{iso}' -PassThru | Get-Volume).DriveLetter").Trim();
        try
        {
            var root = $"{drive}:\\";
            const string probe = """
                {
                  title: disc.manifest.disc.title,
                  readOnly: await fetch("https://disc.local/.disc/manifest.json").then(r => r.ok),
                  foreign: await disc.run("format-c").catch(e => e.code),
                  bridgeRefuses: await new Promise(r => {
                    const req = 99001;
                    chrome.webview.addEventListener("message", e => e.data.res === req && r(e.data.error));
                    chrome.webview.postMessage({ v: 1, req, cmd: "run", id: "C:/Windows/System32/cmd.exe" });
                  }),
                  video: await disc.play("mv-making-of", { element: document.getElementById("v") })
                    .then(() => new Promise(r => setTimeout(() => r(disc.player.state()), 1500))),
                }
                """;
            var output = Run("node", $"\"{Path.Combine(Repo, "scripts", "cdp-probe.mjs")}\" \"{Path.Combine(root, "Baslat.exe")}\" - \"{probe.ReplaceLineEndings(" ").Replace("\"", "\\\"")}\"");
            var r = JsonDocument.Parse(output[output.IndexOf('{')..]).RootElement;
            r.GetProperty("title").GetString().Should().Be("side project GAMES — Koleksiyon");
            r.GetProperty("readOnly").GetBoolean().Should().BeTrue();
            r.GetProperty("foreign").GetString().Should().Be("unknown-action");
            r.GetProperty("bridgeRefuses").GetString().Should().Be("unknown-action");
            var video = r.GetProperty("video");
            video.GetProperty("status").GetString().Should().Be("playing");
            video.GetProperty("positionSec").GetDouble().Should().BeGreaterThan(0.3);
        }
        finally
        {
            Ps($"Dismount-DiskImage -ImagePath '{iso}' | Out-Null");
        }
    }

    static string Run(string file, string args)
    {
        var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Repo })!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception($"{Path.GetFileName(file)} failed ({p.ExitCode}): {stderr}");
        return stdout.Result;
    }

    static string Ps(string script) => Run("powershell", $"-NoProfile -Command \"{script}\"");
}

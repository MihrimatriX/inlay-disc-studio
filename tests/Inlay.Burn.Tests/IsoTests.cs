using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using ImageMagick;
using Inlay.Burn;
using Inlay.Core;
using Inlay.Imaging;
using Xunit;

namespace Inlay.Burn.Tests;

public sealed class IsoTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-iso-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    string Write(string rel, string content)
    {
        var p = Path.Combine(_dir, "src", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
        return p;
    }

    /// <summary>The M1 pipeline: content + manifest + icon + autorun → ISO.</summary>
    DiscImagePlan Plan()
    {
        var manifest = JsonSerializer.Deserialize(File.ReadAllText("samples/manifest.bundle.json"), InlayJson.Default.DiscManifest)!;
        var ico = Path.Combine(_dir, "icon.ico");
        DiscIcon.Write(ico);
        var inf = Path.Combine(_dir, "autorun.inf");
        File.WriteAllText(inf, Autorun.Build(manifest), Autorun.Encoding);
        return new DiscImagePlan(DiscLabel.FromTitle(manifest.Disc.Title), manifest.Disc.Media,
        [
            new("autorun.inf", inf),
            new(".disc/icon.ico", ico),
            new(".disc/manifest.json", Write(".disc/manifest.json", JsonSerializer.Serialize(manifest, InlayJson.Default.DiscManifest))),
            new("Setup/Game/setup.exe", Write("Setup/Game/setup.exe", "MZ")),
            new("Belgeler/Oku beni — Türkçe.txt", Write("Belgeler/Oku beni — Türkçe.txt", "merhaba")),
        ], [".disc"]);
    }

    [Fact]
    public void Iso_mounts_with_label_icon_autorun_and_hidden_disc_folder()
    {
        var iso = Path.Combine(_dir, "disc.iso");
        var reports = new List<double>();
        DiscImage.ExportIso(Plan(), iso, new SyncProgress(reports.Add), TestContext.Current.CancellationToken);
        reports.Should().NotBeEmpty().And.EndWith(1.0);

        using var mount = Mounted.Iso(iso);
        mount.Label.Should().Be("OYUNUMUZ_KURULUM_DISKI");
        mount.FileSystem.Should().Be("UDF"); // Windows prefers UDF when the bridge format is present
        File.ReadAllText(Path.Combine(mount.Root, "Belgeler", "Oku beni — Türkçe.txt")).Should().Be("merhaba");
        File.ReadAllText(Path.Combine(mount.Root, "autorun.inf"), Autorun.Encoding).Should().Contain("action=Programları kur");
        new DirectoryInfo(Path.Combine(mount.Root, ".disc")).Attributes.Should().HaveFlag(FileAttributes.Hidden);
        Shell.IconLocation(mount.Root).Should().EndWithEquivalentOf(@".disc\icon.ico");
    }

    [Fact]
    public void Plans_that_do_not_fit_are_refused()
    {
        var plan = Plan() with { Media = DiscMedia.Cd700 };
        var big = Path.Combine(_dir, "big.bin");
        using (var f = File.Create(big)) f.SetLength(800_000_000); // sparse on NTFS, costs nothing
        plan = plan with { Files = [.. plan.Files, new DiscEntry("big.bin", big)] };
        DiscImage.Check(plan).Should().Contain("capacity");
        FluentActions.Invoking(() => DiscImage.ExportIso(plan, Path.Combine(_dir, "x.iso"))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Invalid_disk_paths_are_refused() =>
        DiscImage.Check(Plan() with { Files = [new DiscEntry("../evil.exe", Plan().Files[0].SourcePath)] })
            .Should().Contain("path:../evil.exe");

    [Fact]
    public void Cancelling_leaves_no_partial_iso()
    {
        var iso = Path.Combine(_dir, "c.iso");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        FluentActions.Invoking(() => DiscImage.ExportIso(Plan(), iso, ct: cts.Token)).Should().Throw<OperationCanceledException>();
        File.Exists(iso).Should().BeFalse();
        File.Exists(iso + ".part").Should().BeFalse();
    }

    /// <summary>Writes a real 4 GiB ISO; opt in with INLAY_SLOW_TESTS=1.</summary>
    [Fact]
    public void Four_GiB_file_makes_a_udf_only_iso()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("INLAY_SLOW_TESTS") == "1", "set INLAY_SLOW_TESTS=1");
        var big = Path.Combine(_dir, "big.mkv");
        using (var f = File.Create(big)) f.SetLength(MediaCapacity.FourGiB);
        var plan = new DiscImagePlan("BIG", DiscMedia.Dvd9, [new("Film/big.mkv", big)], []);
        plan.FileSystems.Should().Be(DiscFileSystems.Udf);
        var iso = Path.Combine(_dir, "big.iso");
        DiscImage.ExportIso(plan, iso, ct: TestContext.Current.CancellationToken);
        using var mount = Mounted.Iso(iso);
        mount.FileSystem.Should().Be("UDF");
        new FileInfo(Path.Combine(mount.Root, "Film", "big.mkv")).Length.Should().Be(MediaCapacity.FourGiB);
    }

    sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

public sealed class IconTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-ico-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Default_icon_has_all_sizes_and_png_256()
    {
        var ico = Path.Combine(_dir, "disc.ico");
        DiscIcon.Write(ico);
        Ico.Entries(ico).Select(e => e.Size).Should().Equal(16, 24, 32, 48, 64, 256);
        Ico.Entries(ico).Single(e => e.Size == 256).IsPng.Should().BeTrue();
    }

    [Fact]
    public void Cover_becomes_rounded_square()
    {
        var cover = Path.Combine(_dir, "cover.png");
        using (var img = new MagickImage("gradient:#ff8a3d-#203040", 300, 450)) img.Write(cover);
        var ico = Path.Combine(_dir, "cover.ico");
        DiscIcon.Write(ico, cover: cover);

        using var frames = new MagickImageCollection(ico);
        var big = frames.Single(f => f.Width == 256);
        big.GetPixels().GetPixel(0, 0).GetChannel(3).Should().Be(0, "the corner is cut away");
        big.GetPixels().GetPixel(128, 128).GetChannel(3).Should().Be(255);
    }
}

static class Ico
{
    public static IEnumerable<(int Size, bool IsPng)> Entries(string path)
    {
        var b = File.ReadAllBytes(path);
        int count = BitConverter.ToUInt16(b, 4);
        for (var i = 0; i < count; i++)
        {
            var e = 6 + i * 16;
            var size = b[e] == 0 ? 256 : b[e];
            var offset = BitConverter.ToInt32(b, e + 12);
            yield return (size, b[offset] == 0x89 && b[offset + 1] == (byte)'P');
        }
    }
}

sealed class Mounted : IDisposable
{
    readonly string _iso;
    public string Root { get; }
    public string Label { get; }
    public string FileSystem { get; }

    Mounted(string iso, string root, string label, string fs) => (_iso, Root, Label, FileSystem) = (iso, root, label, fs);

    public static Mounted Iso(string iso)
    {
        var lines = Ps($"$v = Mount-DiskImage -ImagePath '{iso}' -PassThru | Get-Volume; $v.DriveLetter; $v.FileSystemLabel; $v.FileSystem");
        return new Mounted(iso, $"{lines[0]}:\\", lines[1], lines[2]);
    }

    public void Dispose() => Ps($"Dismount-DiskImage -ImagePath '{_iso}' | Out-Null");

    static string[] Ps(string script)
    {
        var p = Process.Start(new ProcessStartInfo("powershell", ["-NoProfile", "-Command", script]) { RedirectStandardOutput = true })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

static class Shell
{
    /// <summary>Where Explorer takes the drive icon from; for optical drives this honors autorun.inf icon=.</summary>
    public static string IconLocation(string driveRoot)
    {
        var info = new SHFILEINFO();
        SHGetFileInfo(driveRoot, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x1000 /* SHGFI_ICONLOCATION */);
        return info.szDisplayName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern nint SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
}

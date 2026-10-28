using System.Security.Cryptography;
using FluentAssertions;
using Inlay.Burn;
using Inlay.Core;
using Xunit;

namespace Inlay.Burn.Tests;

/// <summary>M7 without a burner in the test machine: the decisions (speed, error states) as tables, and verification
/// and the raw read of "Disk kopyala" against a mounted ISO, which Windows exposes as a real CD-ROM volume.
/// Physical CD-R / DVD-R / DVD+R DL burns are on the manual checklist (docs/burn-checklist.md).</summary>
public sealed class BurnTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-burn-").FullName;
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(new[] { 10820, 8115, 5410, 2705 }, 8115)] // DVD 16x/12x/8x/4x → 12x
    [InlineData(new[] { 3600, 1800 }, 1800)]
    [InlineData(new[] { 1800 }, 1800)]
    [InlineData(new int[0], 0)]
    public void Recommended_speed_is_one_below_the_fastest(int[] supported, int expected) =>
        Burner.Recommended(supported).Should().Be(expected);

    [Theory]
    [InlineData(unchecked((int)0xC0AA0202), BurnError.NoMedia)]
    [InlineData(unchecked((int)0xC0AA0404), BurnError.TooSmall)]
    [InlineData(unchecked((int)0xC0AA0300), BurnError.BufferUnderrun)]
    [InlineData(unchecked((int)0xC0AA0210), BurnError.Busy)]
    [InlineData(unchecked((int)0xC0AA0207), BurnError.Busy)]
    [InlineData(unchecked((int)0xC0AA0405), BurnError.NotBlank)]
    [InlineData(unchecked((int)0xC0AA0209), BurnError.WriteProtected)]
    [InlineData(unchecked((int)0xC0AA0002), BurnError.Cancelled)]
    [InlineData(unchecked((int)0x80004005), BurnError.WriteFailed)]
    public void Imapi_errors_map_to_the_states_the_user_sees(int hr, BurnError error) => BurnException.Map(hr).Should().Be(error);

    [Fact]
    public void Speed_in_x_units() => (8115 / Burner.OneX(MediaType.DvdDashR)).Should().BeApproximately(12, 0.1);

    (DiscImagePlan Plan, string Iso) Image()
    {
        var src = Path.Combine(_dir, "src");
        Directory.CreateDirectory(Path.Combine(src, "Film"));
        var big = Path.Combine(src, "Film", "film.bin");
        File.WriteAllBytes(big, RandomNumberGenerator.GetBytes(6 * 1024 * 1024 + 123));
        var small = Path.Combine(src, "oku.txt");
        File.WriteAllText(small, "merhaba");
        var plan = new DiscImagePlan("INLAY_TEST", DiscMedia.Cd700, [new("Film/film.bin", big), new(".disc/oku.txt", small)], [".disc"]);
        var iso = Path.Combine(_dir, "test.iso");
        DiscImage.ExportIso(plan, iso, ct: Ct);
        return (plan, iso);
    }

    [Fact]
    public async Task Verification_reads_the_disc_back_and_finds_changed_and_missing_files()
    {
        var (plan, iso) = Image();
        var stages = new List<BurnStage>();
        var hashes = await Verification.Hash(plan.Files.Select(f => (f.DiskPath, f.SourcePath)).ToList(), new SyncProgress<BurnProgress>(p => stages.Add(p.Stage)), Ct);
        stages.Should().OnlyContain(s => s == BurnStage.Hashing);

        using var mount = Mounted.Iso(iso);
        var report = await Verification.Check(mount.Root, hashes, ct: Ct);
        report.Ok.Should().BeTrue();
        report.Files.Should().Be(2);

        var wrong = hashes.ToDictionary(h => h.Key, h => h.Value);
        wrong["Film/film.bin"] = new string('0', 64);
        wrong["Belgeler/yok.txt"] = new string('0', 64);
        report = await Verification.Check(mount.Root, wrong, ct: Ct);
        report.Ok.Should().BeFalse();
        report.Mismatched.Should().Equal("Film/film.bin");
        report.Missing.Should().Equal("Belgeler/yok.txt");
    }

    [Fact]
    public async Task Disc_copy_reads_the_data_area_as_an_identical_image()
    {
        var (_, iso) = Image();
        using var mount = Mounted.Iso(iso);
        (await Verification.SameSectors(iso, mount.Root, ct: Ct)).Should().BeTrue("ISO burns verify sector by sector");

        var copy = Path.Combine(_dir, "copy.iso");
        await RawVolume.ReadImage(mount.Root, copy, ct: Ct);
        var copied = File.ReadAllBytes(copy);
        copied.Length.Should().Be((int)new DriveInfo(mount.Root).TotalSize).And.BeGreaterThan(6 * 1024 * 1024);
        copied.Should().Equal(File.ReadAllBytes(iso)[..copied.Length], "the copy is the source's data area, byte for byte");

        // The copy is itself a disc image: it mounts and holds the same files.
        using var again = Mounted.Iso(copy);
        File.ReadAllText(Path.Combine(again.Root, ".disc", "oku.txt")).Should().Be("merhaba");
        SHA256.HashData(File.ReadAllBytes(Path.Combine(again.Root, "Film", "film.bin"))).Should().Equal(SHA256.HashData(File.ReadAllBytes(Path.Combine(mount.Root, "Film", "film.bin"))));
    }

    [Fact]
    public void Recorder_listing_never_throws() => Recorders.List().Should().NotBeNull();

    sealed class SyncProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
}

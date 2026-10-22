using System.Diagnostics;
using FluentAssertions;
using ImageMagick;
using Inlay.Analysis;
using Inlay.Media;
using Xunit;

namespace Inlay.Analysis.Tests;

public sealed class LocalMetaTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("inlay-meta-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    internal static void Ffmpeg(string args)
    {
        var p = Process.Start(new ProcessStartInfo(MediaTools.FfmpegPath, "-v error -y " + args) { RedirectStandardError = true, UseShellExecute = false })!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception(err);
    }

    [Fact]
    public void Audio_tags_and_cover_are_read()
    {
        Assert.SkipUnless(MediaTools.Available, "tools/ffmpeg is missing");
        var cover = Path.Combine(_dir, "c.png");
        using (var img = new MagickImage(MagickColors.Orange, 64, 64)) img.Write(cover);
        var mp3 = Path.Combine(_dir, "t.mp3");
        Ffmpeg($"-f lavfi -i sine=duration=1 -i \"{cover}\" -map 0 -map 1 -c:a libmp3lame -c:v png -disposition:v attached_pic " +
               "-metadata title=\"Açılış\" -metadata artist=\"Deniz\" -metadata album_artist=\"Deniz\" -metadata album=\"Işık\" -metadata track=3/10 -metadata disc=1 -metadata date=2024 -metadata genre=Ambient " +
               $"-id3v2_version 3 \"{mp3}\"");
        var tags = LocalMeta.Audio(mp3)!;
        tags.Should().Match<AudioTags>(t => t.Title == "Açılış" && t.Artist == "Deniz" && t.Album == "Işık" && t.Track == 3 && t.Disc == 1 && t.Year == 2024 && t.Genre == "Ambient");
        tags.DurationSec.Should().BeApproximately(1, 0.3); // TagLib estimates MP3 length; the pipeline takes ffprobe's
        tags.Cover.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Broken_audio_is_not_fatal()
    {
        var bad = Path.Combine(_dir, "bad.mp3");
        File.WriteAllText(bad, "not audio");
        LocalMeta.Audio(bad).Should().BeNull();
    }

    [Fact]
    public void Exif_date_camera_and_gps_are_read()
    {
        var jpg = Path.Combine(_dir, "p.jpg");
        using (var img = new MagickImage(MagickColors.SkyBlue, 64, 48))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Make, "Canon");
            exif.SetValue(ExifTag.Model, "EOS R6");
            exif.SetValue(ExifTag.DateTimeOriginal, "2025:03:14 13:22:00");
            exif.SetValue(ExifTag.OffsetTimeOriginal, "+03:00");
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.GPSLatitude, [new Rational(41, 1), new Rational(0, 1), new Rational(2952, 100)]);
            exif.SetValue(ExifTag.GPSLongitudeRef, "E");
            exif.SetValue(ExifTag.GPSLongitude, [new Rational(28, 1), new Rational(58, 1), new Rational(48, 1)]);
            img.SetProfile(exif);
            img.Write(jpg);
        }
        var e = LocalMeta.Exif(jpg)!;
        e.TakenAt.Should().Be(new DateTimeOffset(2025, 3, 14, 13, 22, 0, TimeSpan.FromHours(3)));
        e.Camera.Should().Be("Canon EOS R6");
        e.Lat.Should().BeApproximately(41.0082, 0.0001);
        e.Lon.Should().BeApproximately(28.98, 0.0001);
    }

    [Fact]
    public void Kodi_nfo_is_read()
    {
        var nfo = Path.Combine(_dir, "movie.nfo");
        File.WriteAllText(nfo, """
            <?xml version="1.0" encoding="UTF-8"?>
            <movie>
              <title>Kuzey Rüzgârı</title><originaltitle>North Wind</originaltitle><year>2023</year>
              <plot>Kayıp bir istasyonu arayan dört kişi.</plot><tagline>Dağ bekler.</tagline>
              <genre>Macera</genre><genre>Dram</genre>
              <ratings><rating name="themoviedb" default="true"><value>7.4</value></rating></ratings>
              <mpaa>13+</mpaa><director>Ayşe Kaya</director>
              <actor><name>Mert Demir</name><role>Kerem</role><thumb>https://image.tmdb.org/t/p/w185/a.jpg</thumb></actor>
              <uniqueid type="tmdb" default="true">12345</uniqueid>
            </movie>
            """);
        LocalMeta.Nfo(nfo).Should().BeEquivalentTo(new NfoMovie("Kuzey Rüzgârı", "North Wind", 2023, "Kayıp bir istasyonu arayan dört kişi.", "Dağ bekler.",
            ["Macera", "Dram"], 7.4, "13+", ["Ayşe Kaya"], [new NfoPerson("Mert Demir", "Kerem", "https://image.tmdb.org/t/p/w185/a.jpg")], "12345"));
    }

    [Fact]
    public void Tvshow_nfo_is_not_a_movie()
    {
        var nfo = Path.Combine(_dir, "tvshow.nfo");
        File.WriteAllText(nfo, "<tvshow><title>x</title></tvshow>");
        LocalMeta.Nfo(nfo).Should().BeNull();
    }

    static string SignedExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");

    [Fact]
    public void Signed_exe_reports_publisher_and_signer()
    {
        var info = LocalMeta.Exe(SignedExe);
        info.Publisher.Should().Contain("Microsoft");
        info.Version.Should().NotBeNullOrEmpty();
        info.Signed.Should().BeTrue();
        info.Signer.Should().NotBeNullOrEmpty(); // the .NET certificate's simple name is ".NET"
    }

    [Fact]
    public void Copied_and_tampered_exe_is_unsigned()
    {
        var copy = Path.Combine(_dir, "setup.exe");
        var bytes = File.ReadAllBytes(SignedExe);
        bytes[0x200] ^= 0xFF; // flip a byte in the headers' slack: the Authenticode hash no longer matches
        File.WriteAllBytes(copy, bytes);
        LocalMeta.Signature(copy).Signed.Should().BeFalse();
    }

    [Fact]
    public void Exe_icon_becomes_a_256_png()
    {
        var png = Path.Combine(_dir, "icon.png");
        LocalMeta.ExtractIcon(Path.Combine(Environment.SystemDirectory, "..", "explorer.exe"), png).Should().BeTrue();
        new MagickImageInfo(png).Should().Match<MagickImageInfo>(i => i.Width == 256 && i.Height == 256 && i.Format == MagickFormat.Png);
    }

    [Fact]
    public void Msi_properties_are_read()
    {
        var msi = Directory.EnumerateFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer"), "*.msi").FirstOrDefault();
        Assert.SkipUnless(msi is not null, "no cached MSI on this machine");
        var info = LocalMeta.Exe(msi!);
        info.ProductName.Should().NotBeNullOrEmpty();
        info.Version.Should().MatchRegex(@"^\d+(\.\d+)*");
    }

    [Theory]
    [InlineData("vc_redist.x64.exe", true)]
    [InlineData("VCREDIST_x86.exe", true)]
    [InlineData("DXSETUP.exe", true)]
    [InlineData("ndp48-x86-x64-allos-enu.exe", true)]
    [InlineData("windowsdesktop-runtime-8.0.11-win-x64.exe", true)]
    [InlineData("setup.exe", false)]
    public void Prerequisites_are_recognized(string name, bool prereq) => LocalMeta.IsPrerequisite(name).Should().Be(prereq);
}

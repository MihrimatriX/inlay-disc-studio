using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using ImageMagick;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Inlay.Analysis;

public sealed record AudioTags(
    string? Title, string? Artist, string? AlbumArtist, string? Album, int? Track, int? Disc, int? Year,
    string? Genre, double? DurationSec, byte[]? Cover);

public sealed record ExifInfo(DateTimeOffset? TakenAt, string? Camera, double? Lat, double? Lon);

public sealed record NfoPerson(string Name, string? Role, string? Thumb);
public sealed record NfoMovie(
    string? Title, string? OriginalTitle, int? Year, string? Plot, string? Tagline, IReadOnlyList<string> Genres,
    double? Rating, string? Certification, IReadOnlyList<string> Directors, IReadOnlyList<NfoPerson> Actors, string? TmdbId);

public sealed record ExeInfo(string? ProductName, string? Version, string? Publisher, bool Signed, string? Signer);

/// <summary>Offline metadata (§5.3): tags, EXIF, Kodi .nfo, PE/MSI version info, icons and Authenticode.</summary>
public static partial class LocalMeta
{
    public static AudioTags? Audio(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path);
            var t = f.Tag;
            return new AudioTags(
                Blank(t.Title), Blank(t.FirstPerformer), Blank(t.FirstAlbumArtist), Blank(t.Album),
                t.Track > 0 ? (int)t.Track : null, t.Disc > 0 ? (int)t.Disc : null, t.Year > 0 ? (int)t.Year : null,
                Blank(t.FirstGenre), f.Properties?.Duration.TotalSeconds is > 0 and var d ? d : null,
                t.Pictures.FirstOrDefault()?.Data?.Data);
        }
        catch (Exception e) when (e is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException)
        {
            return null;
        }
    }

    /// <summary>EXIF date, camera and GPS. No reverse geocoding: that would be a network call (§5.3 privacy).</summary>
    public static ExifInfo? Exif(string path)
    {
        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(path);
            var sub = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            DateTimeOffset? taken = null;
            if (sub?.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt) == true)
            {
                // EXIF times are local wall-clock; OffsetTimeOriginal ("+03:00") tells the zone when the camera wrote it.
                var offset = sub.GetString(ExifDirectoryBase.TagTimeZoneOriginal);
                taken = offset is not null && TimeSpan.TryParse(offset.TrimStart('+'), CultureInfo.InvariantCulture, out var o)
                    ? new DateTimeOffset(dt, offset.StartsWith('-') ? -o : o)
                    : new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), TimeSpan.Zero);
            }
            var camera = string.Join(' ', new[] { ifd0?.GetString(ExifDirectoryBase.TagMake), ifd0?.GetString(ExifDirectoryBase.TagModel) }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).Distinct());
            var gps = dirs.OfType<GpsDirectory>().FirstOrDefault()?.GetGeoLocation();
            return new ExifInfo(taken, camera.Length > 0 ? camera : null, gps?.Latitude, gps?.Longitude);
        }
        catch (Exception e) when (e is ImageProcessingException or IOException)
        {
            return null;
        }
    }

    /// <summary>Kodi movie .nfo (takes precedence over TMDB, §5.3).</summary>
    public static NfoMovie? Nfo(string path)
    {
        try
        {
            var root = XDocument.Load(path).Root;
            if (root?.Name.LocalName != "movie") return null;
            string? S(string n) => Blank(root.Element(n)?.Value);
            var uniqueTmdb = root.Elements("uniqueid").FirstOrDefault(e => (string?)e.Attribute("type") == "tmdb")?.Value;
            return new NfoMovie(
                S("title"), S("originaltitle"),
                int.TryParse(S("year") ?? S("premiered")?[..4], out var y) ? y : null,
                S("plot") ?? S("outline"), S("tagline"),
                root.Elements("genre").Select(e => e.Value.Trim()).Where(g => g.Length > 0).ToList(),
                double.TryParse(root.Element("ratings")?.Elements("rating").FirstOrDefault()?.Element("value")?.Value ?? S("rating"),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? Math.Clamp(r, 0, 10) : null,
                S("mpaa") ?? S("certification"),
                root.Elements("director").Select(e => e.Value.Trim()).Where(d => d.Length > 0).ToList(),
                root.Elements("actor").Select(a => new NfoPerson(a.Element("name")?.Value.Trim() ?? "", Blank(a.Element("role")?.Value), Blank(a.Element("thumb")?.Value)))
                    .Where(a => a.Name.Length > 0).ToList(),
                Blank(uniqueTmdb) ?? S("tmdbid"));
        }
        catch (Exception e) when (e is System.Xml.XmlException or IOException)
        {
            return null;
        }
    }

    /// <summary>Product name, version and publisher from the PE version resource or the MSI property table; Authenticode.</summary>
    public static ExeInfo Exe(string path)
    {
        string? name = null, version = null, publisher = null;
        if (Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            name = MsiProperty(path, "ProductName");
            version = MsiProperty(path, "ProductVersion");
            publisher = MsiProperty(path, "Manufacturer");
        }
        else
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            name = Blank(v.ProductName) ?? Blank(v.FileDescription);
            version = Blank(v.ProductVersion)?.Split('+')[0] ?? Blank(v.FileVersion);
            publisher = Blank(v.CompanyName);
        }
        var (signed, signer) = Signature(path);
        return new ExeInfo(name, version, publisher, signed, signer);
    }

    /// <summary>The largest icon in the file as a 256 px PNG (§5.3), or false when it has none.</summary>
    public static bool ExtractIcon(string exePath, string pngPath)
    {
        var icons = new nint[1];
        if (PrivateExtractIconsW(exePath, 0, 256, 256, icons, null, 1, 0) < 1 || icons[0] == 0) return false;
        try
        {
            if (!GetIconInfo(icons[0], out var info)) return false;
            try
            {
                var bmp = new BITMAP();
                GetObjectW(info.hbmColor, Marshal.SizeOf<BITMAP>(), ref bmp);
                int w = bmp.bmWidth, h = bmp.bmHeight;
                var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
                var pixels = new byte[w * h * 4];
                var dc = GetDC(0);
                try { GetDIBits(dc, info.hbmColor, 0, (uint)h, pixels, ref bi, 0); }
                finally { ReleaseDC(0, dc); }
                using var img = new MagickImage(pixels, new PixelReadSettings((uint)w, (uint)h, StorageType.Char, PixelMapping.BGRA));
                if (w != 256) img.Resize(256, 256);
                img.Write(pngPath, MagickFormat.Png);
                return true;
            }
            finally
            {
                DeleteObject(info.hbmColor);
                DeleteObject(info.hbmMask);
            }
        }
        finally { DestroyIcon(icons[0]); }
    }

    /// <summary>WinVerifyTrust (chain to a trusted root, no UI, no revocation network check) + signer name.</summary>
    public static (bool Signed, string? Signer) Signature(string path)
    {
        var file = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        Marshal.StructureToPtr(file, filePtr, false);
        try
        {
            var data = new WINTRUST_DATA
            {
                cbStruct = Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2, // WTD_UI_NONE
                fdwRevocationChecks = 0,
                dwUnionChoice = 1, // WTD_CHOICE_FILE
                pFile = filePtr,
                dwStateAction = 0,
                dwProvFlags = 0x1000 | 0x800, // WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE
            };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            if (WinVerifyTrust(-1, ref action, ref data) != 0) return (false, null);
#pragma warning disable SYSLIB0057 // reading the signer of a PE file has no X509CertificateLoader equivalent
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            return (true, cert.GetNameInfo(X509NameType.SimpleName, false));
        }
        catch (System.Security.Cryptography.CryptographicException) { return (false, null); }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(filePtr);
            Marshal.FreeHGlobal(filePtr);
        }
    }

    static string? MsiProperty(string path, string property)
    {
        MsiSetInternalUI(2 /* INSTALLUILEVEL_NONE */, 0);
        if (MsiOpenPackageExW(path, 1 /* IGNOREMACHINESTATE */, out var h) != 0) return null;
        try
        {
            var size = 256u;
            var buf = new char[size];
            return MsiGetProductPropertyW(h, property, buf, ref size) == 0 ? Blank(new string(buf, 0, (int)size)) : null;
        }
        finally { MsiCloseHandle(h); }
    }

    /// <summary>Redistributables that install silently before the program (§5.3, §7.0).</summary>
    public static bool IsPrerequisite(string fileName)
    {
        var n = Path.GetFileName(fileName).ToLowerInvariant();
        return n.StartsWith("vc_redist") || n.StartsWith("vcredist") || n is "dxsetup.exe" || n.StartsWith("dxwebsetup")
            || (n.StartsWith("ndp") && n.EndsWith(".exe")) || n.StartsWith("dotnet-runtime") || n.StartsWith("windowsdesktop-runtime")
            || n.StartsWith("physx") || n.StartsWith("oalinst");
    }

    /// <summary>Silent install switches for well-known redistributables (used inside the bundle).</summary>
    public static IReadOnlyList<string> PrerequisiteSilentArgs(string fileName)
    {
        var n = Path.GetFileName(fileName).ToLowerInvariant();
        if (n.StartsWith("vc_redist") || n.StartsWith("windowsdesktop-runtime") || n.StartsWith("dotnet-runtime")) return ["/install", "/quiet", "/norestart"];
        if (n.StartsWith("vcredist")) return ["/q", "/norestart"];
        if (n is "dxsetup.exe") return ["/silent"];
        if (n.StartsWith("ndp")) return ["/q", "/norestart"];
        return ["/S"];
    }

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---- interop ----

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PrivateExtractIconsW(string file, int index, int cx, int cy, [Out] nint[] icons, [Out] uint[]? ids, uint count, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyIcon(nint icon);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetIconInfo(nint icon, out ICONINFO info);
    [LibraryImport("user32.dll")] private static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll")] private static partial int ReleaseDC(nint hwnd, nint dc);
    [LibraryImport("gdi32.dll")] private static partial int GetObjectW(nint obj, int size, ref BITMAP bmp);
    [LibraryImport("gdi32.dll")] private static partial int GetDIBits(nint dc, nint bmp, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [LibraryImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DeleteObject(nint obj);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(nint hwnd, ref Guid action, ref WINTRUST_DATA data);
    [LibraryImport("msi.dll")] private static partial int MsiSetInternalUI(int level, nint window);
    [LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint MsiOpenPackageExW(string path, uint options, out nint handle);
    [LibraryImport("msi.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint MsiGetProductPropertyW(nint handle, string property, [Out] char[] value, ref uint size);
    [LibraryImport("msi.dll")] private static partial uint MsiCloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public int fIcon, xHotspot, yHotspot; public nint hbmMask, hbmColor; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public nint bmBits; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_FILE_INFO { public int cbStruct; public string pcwszFilePath; public nint hFile; public nint pgKnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public int cbStruct; public nint pPolicyCallbackData, pSIPClientData; public int dwUIChoice, fdwRevocationChecks, dwUnionChoice;
        public nint pFile; public int dwStateAction; public nint hWVTStateData, pwszURLReference; public int dwProvFlags, dwUIContext; public nint pSignatureSettings;
    }
}

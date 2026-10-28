using System.Collections;

namespace Inlay.Burn;

/// <summary>What is in a recorder (§8.5 "sürücü/medya algılama"). Sizes in bytes (2048-byte sectors).</summary>
public sealed record DiscInDrive(MediaType Type, bool Blank, bool Writable, long FreeBytes, long TotalBytes);

public sealed record Recorder(string Id, string Letter, string Name, DiscInDrive? Disc);

/// <summary>IMAPI_MEDIA_PHYSICAL_TYPE values that a disc burner can meet.</summary>
public enum MediaType
{
    Unknown = 0, CdRom = 1, CdR = 2, CdRw = 3, DvdRom = 4, DvdRam = 5, DvdPlusR = 6, DvdPlusRw = 7, DvdPlusRDualLayer = 8,
    DvdDashR = 9, DvdDashRw = 10, DvdDashRDualLayer = 11, Disk = 12, DvdPlusRwDualLayer = 13, BdRom = 17, BdR = 18, BdRe = 19,
}

public static class Recorders
{
    /// <summary>Every optical recorder IMAPI2 sees, with the disc in it. Empty when IMAPI2 is missing or there is none.</summary>
    public static IReadOnlyList<Recorder> List() => Sta.Run(() =>
    {
        var list = new List<Recorder>();
        dynamic master;
        try { master = Com("IMAPI2.MsftDiscMaster2"); }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException) { return list; }
        if (!(bool)master.IsSupportedEnvironment) return list;
        foreach (string id in (IEnumerable)master)
        {
            try { list.Add(Read(id)); }
            catch (System.Runtime.InteropServices.COMException) { } // a drive that vanished or is busy elsewhere
        }
        return list;
    });

    static Recorder Read(string id)
    {
        dynamic rec = Com("IMAPI2.MsftDiscRecorder2");
        rec.InitializeDiscRecorder(id);
        var paths = (object[])rec.VolumePathNames;
        var letter = paths.Length > 0 ? ((string)paths[0]).TrimEnd('\\') : "";
        var name = $"{((string)rec.VendorId).Trim()} {((string)rec.ProductId).Trim()}".Trim();
        return new Recorder(id, letter, name, Media(rec));
    }

    static DiscInDrive? Media(dynamic rec)
    {
        dynamic data = Com("IMAPI2.MsftDiscFormat2Data");
        if (!(bool)data.IsRecorderSupported(rec)) return null;
        if (!(bool)data.IsCurrentMediaSupported(rec)) return Present(rec) ? new DiscInDrive(MediaType.Unknown, false, false, 0, 0) : null;
        data.Recorder = rec;
        var type = (MediaType)(int)data.CurrentPhysicalMediaType;
        var blank = (bool)data.MediaHeuristicallyBlank;
        long free = 0, total = 0;
        try
        {
            free = (long)(int)data.FreeSectorsOnMedia * 2048;
            total = (long)(int)data.TotalSectorsOnMedia * 2048;
        }
        catch (System.Runtime.InteropServices.COMException) { } // finalized or unreadable media report no sector counts
        var writable = blank || ((int)data.CurrentMediaStatus & 0x4 /* APPENDABLE */) != 0;
        return new DiscInDrive(type, blank, writable && free > 0, free, total);
    }

    /// <summary>A pressed or finalized disc IMAPI2 will not write still counts as "a disc is in the drive".</summary>
    internal static bool Present(dynamic rec)
    {
        try { return ((object[])rec.CurrentProfiles).Length > 0; }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }

    internal static dynamic Com(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId, throwOnError: true)!)!;

    /// <summary>The name people know a medium by ("DVD-R", "DVD+R DL").</summary>
    public static string Name(MediaType t) => t switch
    {
        MediaType.CdRom => "CD", MediaType.CdR => "CD-R", MediaType.CdRw => "CD-RW",
        MediaType.DvdRom => "DVD", MediaType.DvdRam => "DVD-RAM", MediaType.DvdPlusR => "DVD+R", MediaType.DvdPlusRw => "DVD+RW",
        MediaType.DvdPlusRDualLayer => "DVD+R DL", MediaType.DvdDashR => "DVD-R", MediaType.DvdDashRw => "DVD-RW",
        MediaType.DvdDashRDualLayer => "DVD-R DL", MediaType.DvdPlusRwDualLayer => "DVD+RW DL",
        MediaType.BdRom => "BD", MediaType.BdR => "BD-R", MediaType.BdRe => "BD-RE",
        _ => "",
    };
}

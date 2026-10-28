using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Inlay.Core;

namespace Inlay.Burn;

/// <summary>One file on the disc: where it lands (disc-root relative, '/' separated) and where its bytes come from.</summary>
public sealed record DiscEntry(string DiskPath, string SourcePath);

public sealed record DiscImagePlan(
    string Label,
    DiscMedia Media,
    IReadOnlyList<DiscEntry> Files,
    IReadOnlyList<string> HiddenPaths,
    int UdfRevision = 0x201)
{
    public long ContentBytes => Files.Sum(f => new FileInfo(f.SourcePath).Length);
    public DiscFileSystems FileSystems => DiscFileSystem.Choose(Files.Select(f => new FileInfo(f.SourcePath).Length));
}

/// <summary>Builds the file system image with IMAPI2FS (§8.3) and streams it to an ISO (§8.5).
/// IMAPI2 objects are dual interfaces, so they are driven late-bound through IDispatch: the builder is not AOT,
/// and this avoids re-declaring dozens of vtable slots in exact header order.</summary>
public static class DiscImage
{
    public static readonly int[] UdfRevisions = [0x102, 0x201, 0x250];

    /// <summary>Problems that block imaging, in user terms. Empty means the plan can be written.</summary>
    public static IReadOnlyList<string> Check(DiscImagePlan plan)
    {
        var issues = new List<string>();
        if (!MediaCapacity.Fits(plan.ContentBytes, plan.Media)) issues.Add("capacity");
        if (plan.FileSystems.HasFlag(DiscFileSystems.Joliet) && DiscFileSystem.JolietViolations(plan.Files.Select(f => f.DiskPath)).Any())
            issues.Add("joliet-name");
        if (!UdfRevisions.Contains(plan.UdfRevision)) issues.Add("udf-revision");
        foreach (var f in plan.Files)
            if (!RelPath.IsValid(f.DiskPath)) issues.Add($"path:{f.DiskPath}");
        return issues;
    }

    static readonly object ImageLock = new();

    /// <summary>Creates the image and hands its stream to <paramref name="consume"/> on an STA thread. One image at a
    /// time per process: two IMAPI2FS images built on two threads at once were seen to release each other's RCWs
    /// ("COM object that has been separated from its underlying RCW").</summary>
    public static T WithImage<T>(DiscImagePlan plan, Func<IStream, long, T> consume)
    {
        var issues = Check(plan);
        if (issues.Count > 0) throw new InvalidOperationException($"Disc plan is not writable: {string.Join(", ", issues)}");
        lock (ImageLock) return Sta.Run(() =>
        {
            dynamic fsi = Activator.CreateInstance(Type.GetTypeFromProgID("IMAPI2FS.MsftFileSystemImage", throwOnError: true)!)!;
            var streams = new List<IStream>();
            try
            {
                fsi.FileSystemsToCreate = (int)plan.FileSystems;
                fsi.UDFRevision = plan.UdfRevision;
                fsi.FreeMediaBlocks = (int)MediaCapacity.Sectors(plan.Media);
                fsi.VolumeName = plan.Label;
                dynamic root = fsi.Root;
                var dirs = new HashSet<string>();
                foreach (var f in plan.Files.OrderBy(f => f.DiskPath, StringComparer.Ordinal))
                {
                    EnsureDirs(root, f.DiskPath, dirs);
                    var s = OpenRead(f.SourcePath);
                    streams.Add(s);
                    root.AddFile(ImapiPath(f.DiskPath), s);
                }
                foreach (var h in plan.HiddenPaths) root[ImapiPath(h)].IsHidden = true;

                dynamic result = fsi.CreateResultImage();
                long size = (long)result.TotalBlocks * (int)result.BlockSize;
                try { return consume((IStream)result.ImageStream, size); }
                finally { Marshal.FinalReleaseComObject(result); }
            }
            finally
            {
                foreach (var s in streams) Marshal.FinalReleaseComObject(s);
                Marshal.FinalReleaseComObject(fsi);
            }
        });
    }

    public static void ExportIso(DiscImagePlan plan, string isoPath, IProgress<double>? progress = null, CancellationToken ct = default) =>
        WithImage(plan, (image, size) =>
        {
            var tmp = isoPath + ".part";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    var buf = new byte[1 << 20];
                    var read = Marshal.AllocHGlobal(sizeof(int));
                    try
                    {
                        long done = 0;
                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            image.Read(buf, buf.Length, read);
                            var n = Marshal.ReadInt32(read);
                            if (n == 0) break;
                            fs.Write(buf, 0, n);
                            done += n;
                            progress?.Report((double)done / size);
                        }
                    }
                    finally { Marshal.FreeHGlobal(read); }
                }
                File.Move(tmp, isoPath, overwrite: true);
            }
            finally { File.Delete(tmp); }
            return 0;
        });

    static string ImapiPath(string diskPath) => "\\" + diskPath.Replace('/', '\\');

    static void EnsureDirs(dynamic root, string diskPath, HashSet<string> made)
    {
        var parts = diskPath.Split('/');
        for (var i = 1; i < parts.Length; i++)
        {
            var dir = string.Join('/', parts[..i]);
            if (made.Add(dir)) root.AddDirectory(ImapiPath(dir));
        }
    }

    const int STGM_READ = 0, STGM_SHARE_DENY_WRITE = 0x20;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateStreamOnFileEx(string file, int mode, int attributes, bool create, IStream? template, out IStream stream);

    static IStream OpenRead(string path)
    {
        SHCreateStreamOnFileEx(Path.GetFullPath(path), STGM_READ | STGM_SHARE_DENY_WRITE, 0, false, null, out var s);
        return s;
    }
}

static class Sta
{
    /// <summary>IMAPI2 COM objects are created and used on one STA thread (§8.5).</summary>
    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception e) { error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
        return result;
    }
}

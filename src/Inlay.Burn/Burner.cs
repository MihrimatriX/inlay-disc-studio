using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Inlay.Core;
using static Inlay.Burn.Recorders;

namespace Inlay.Burn;

public enum BurnStage { Hashing, Preparing, Formatting, Writing, Finalizing, Verifying, Done }

/// <summary>Where a burn is. <see cref="SpeedX"/> is the drive's current speed in 1x units of the medium.</summary>
public readonly record struct BurnProgress(BurnStage Stage, double Value, long DoneBytes = 0, long TotalBytes = 0, double? SpeedX = null, double? RemainingSec = null);

/// <summary>The §8.5 error states the UI tells apart; the rest is <see cref="BurnError.WriteFailed"/>.</summary>
public enum BurnError { NoMedia, NotBlank, TooSmall, Busy, BufferUnderrun, WriteProtected, Unsupported, WriteFailed, VerifyFailed, Cancelled, NotErasable }

public sealed class BurnException(BurnError error, string? detail = null, Exception? inner = null)
    : Exception(detail ?? error.ToString(), inner)
{
    public BurnError Error { get; } = error;

    /// <summary>IMAPI2 HRESULTs (imapi2error.h) to what the user needs to know.</summary>
    public static BurnError Map(int hr) => (uint)hr switch
    {
        0xC0AA0202 or 0xC0AA0205 => BurnError.NoMedia,                // RECORDER_MEDIA_NO_MEDIA, BECOMING_READY
        0xC0AA0203 or 0xC0AA0406 => BurnError.Unsupported,            // MEDIA_INCOMPATIBLE, DF2DATA_MEDIA_IS_NOT_SUPPORTED
        0xC0AA0207 or 0xC0AA0210 or 0xC0AA0400 => BurnError.Busy,     // MEDIA_BUSY, RECORDER_LOCKED, WRITE_IN_PROGRESS
        0xC0AA0209 => BurnError.WriteProtected,
        0xC0AA0300 => BurnError.BufferUnderrun,                       // LOSS_OF_STREAMING
        0xC0AA0404 => BurnError.TooSmall,                             // STREAM_TOO_LARGE_FOR_CURRENT_MEDIA
        0xC0AA0402 or 0xC0AA0405 => BurnError.NotBlank,               // INVALID_MEDIA_STATE, MEDIA_NOT_BLANK
        0xC0AA0002 => BurnError.Cancelled,                            // REQUEST_CANCELLED
        _ => BurnError.WriteFailed,
    };
}

/// <summary><see cref="SpeedSectors"/>: 0 = recommended, -1 = slowest (safest), otherwise sectors per second.
/// <see cref="CheckAndEject"/> is asked when writing ends ("Bitince kontrol et ve diski çıkar" can change mid-burn);
/// null skips hashing altogether.</summary>
public sealed record BurnOptions(string RecorderId, int SpeedSectors = 0, Func<bool>? CheckAndEject = null);

/// <summary>§8.5 report: which files read back identical, and how fast the disc read.</summary>
public sealed record VerifyReport(int Files, IReadOnlyList<string> Mismatched, IReadOnlyList<string> Missing, IReadOnlyList<double> ReadMBps)
{
    public bool Ok => Mismatched.Count == 0 && Missing.Count == 0;
}

/// <summary>Writes an image to a blank disc with IMAPI2 (§8.5): exclusive access, recommended speed, progress from
/// the DDiscFormat2DataEvents.Update callback, the system kept awake, one write at a time in the whole app.</summary>
public static class Burner
{
    static readonly SemaphoreSlim OneWrite = new(1, 1);

    /// <summary>Prepare → hash → write → (verify) → eject. Verification needs the original files, so it is only offered
    /// for plans; <see cref="BurnIso"/> verifies sector by sector instead.</summary>
    public static async Task<VerifyReport?> Burn(DiscImagePlan plan, BurnOptions o, IProgress<BurnProgress>? progress = null, CancellationToken ct = default)
    {
        if (!await OneWrite.WaitAsync(0, ct)) throw new BurnException(BurnError.Busy);
        try
        {
            var hashes = o.CheckAndEject is null ? null : await Verification.Hash(plan.Files.Select(f => (f.DiskPath, f.SourcePath)).ToList(), progress, ct);
            var letter = await Task.Run(() => DiscImage.WithImage(plan, (image, size) => Write(o, image, size, progress, ct)), ct);
            VerifyReport? report = null;
            if (hashes is not null && o.CheckAndEject!())
            {
                report = await Verification.Check(await Reload(o.RecorderId, letter, ct), hashes, progress, ct);
                Eject(o.RecorderId);
            }
            progress?.Report(new(BurnStage.Done, 1));
            return report;
        }
        finally { OneWrite.Release(); }
    }

    /// <summary>"ISO kalıbı yaz": the file is the image. Verification reads the disc back sector by sector.</summary>
    public static async Task<bool?> BurnIso(string iso, BurnOptions o, IProgress<BurnProgress>? progress = null, CancellationToken ct = default)
    {
        if (!await OneWrite.WaitAsync(0, ct)) throw new BurnException(BurnError.Busy);
        try
        {
            var size = new FileInfo(iso).Length;
            var letter = await Task.Run(() => Sta.Run(() =>
            {
                SHCreateStreamOnFileEx(Path.GetFullPath(iso), 0x20 /* READ | SHARE_DENY_WRITE */, 0, false, null, out var stream);
                try { return Write(o, stream, size, progress, ct); }
                finally { Marshal.FinalReleaseComObject(stream); }
            }), ct);
            bool? same = null;
            if (o.CheckAndEject?.Invoke() == true)
            {
                same = await Verification.SameSectors(iso, await Reload(o.RecorderId, letter, ct), progress, ct);
                Eject(o.RecorderId);
            }
            progress?.Report(new(BurnStage.Done, 1));
            return same;
        }
        finally { OneWrite.Release(); }
    }

    /// <summary>The speed §8.5 recommends: not the highest, the one below it (sectors per second; 0 = the drive's choice).</summary>
    public static int Recommended(IReadOnlyList<int> supported) =>
        supported.Count == 0 ? 0 : supported.OrderByDescending(s => s).Skip(supported.Count > 1 ? 1 : 0).First();

    /// <summary>1x in sectors per second: 75 for CD, about 676 for DVD (1 385 000 B/s).</summary>
    public static double OneX(MediaType t) => t is MediaType.CdR or MediaType.CdRw or MediaType.CdRom ? 75 : 676.27;

    static string Write(BurnOptions o, IStream image, long size, IProgress<BurnProgress>? progress, CancellationToken ct)
    {
        dynamic rec = Com("IMAPI2.MsftDiscRecorder2");
        rec.InitializeDiscRecorder(o.RecorderId);
        var paths = (object[])rec.VolumePathNames;
        var letter = paths.Length > 0 ? ((string)paths[0]).TrimEnd('\\') : "";
        dynamic data = Com("IMAPI2.MsftDiscFormat2Data");
        var locked = false;
        int cookie = 0;
        IConnectionPoint? point = null;
        try
        {
            if (!(bool)data.IsRecorderSupported(rec)) throw new BurnException(BurnError.Unsupported);
            if (!(bool)data.IsCurrentMediaSupported(rec)) throw new BurnException(Present(rec) ? BurnError.Unsupported : BurnError.NoMedia);
            data.Recorder = rec;
            data.ClientName = "Inlay";
            if (!(bool)data.MediaHeuristicallyBlank) throw new BurnException(BurnError.NotBlank);
            if ((long)(int)data.FreeSectorsOnMedia * 2048 < size) throw new BurnException(BurnError.TooSmall); // §8.4: the real free space at burn time
            var type = (MediaType)(int)data.CurrentPhysicalMediaType;
            var speeds = ((object[])data.SupportedWriteSpeeds).Select(Convert.ToInt32).ToList();
            data.SetWriteSpeed(o.SpeedSectors switch { > 0 => o.SpeedSectors, < 0 when speeds.Count > 0 => speeds.Min(), _ => Recommended(speeds) }, false);
            data.ForceMediaToBeClosed = true;

            rec.AcquireExclusiveAccess(true, "Inlay");
            locked = true;
            var sink = new DataEvents(size, OneX(type), progress, ct);
            var iid = typeof(DDiscFormat2DataEvents).GUID;
            ((IConnectionPointContainer)data).FindConnectionPoint(ref iid, out point);
            point!.Advise(sink, out cookie);
            using (KeepAwake.Now())
                data.Write(image);
            ct.ThrowIfCancellationRequested();
            return letter;
        }
        catch (COMException e) { throw ct.IsCancellationRequested ? new OperationCanceledException(ct) : new BurnException(BurnException.Map(e.HResult), e.Message, e); }
        finally
        {
            if (point is not null && cookie != 0) point.Unadvise(cookie);
            if (locked) try { rec.ReleaseExclusiveAccess(); } catch (COMException) { }
            Marshal.FinalReleaseComObject(data);
            Marshal.FinalReleaseComObject(rec);
        }
    }

    /// <summary>A finished disc must be read as the new file system: eject and close the tray, then wait for the volume.
    /// Drives that cannot close their tray (laptops) wait for the user to push it back in.</summary>
    static async Task<string> Reload(string recorderId, string letter, CancellationToken ct)
    {
        Sta.Run(() =>
        {
            dynamic rec = Com("IMAPI2.MsftDiscRecorder2");
            try
            {
                rec.InitializeDiscRecorder(recorderId);
                rec.EjectMedia();
                if ((bool)rec.DeviceCanLoadMedia) rec.CloseTray();
            }
            catch (COMException) { }
            finally { Marshal.FinalReleaseComObject(rec); }
            return 0;
        });
        var root = letter + "\\";
        for (var i = 0; i < 600; i++) // five minutes for someone to push the tray back
        {
            try { if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) return root; }
            catch (IOException) { } // the drive is still spinning up
            await Task.Delay(500, ct);
        }
        throw new BurnException(BurnError.VerifyFailed, "the disc did not come back after writing");
    }

    public static void Eject(string recorderId) => Sta.Run(() =>
    {
        dynamic rec = Com("IMAPI2.MsftDiscRecorder2");
        try { rec.InitializeDiscRecorder(recorderId); rec.EjectMedia(); }
        catch (COMException) { } // a drive without a motorised tray
        finally { Marshal.FinalReleaseComObject(rec); }
        return 0;
    });

    /// <summary>"Disk tak": opens the tray so a disc can go in.</summary>
    public static void OpenTray(string recorderId) => Eject(recorderId);

    /// <summary>"Diski sil": quick erase of rewritable media (CD-RW, DVD-RW). DVD+RW needs none and reports NotErasable.</summary>
    public static Task Erase(string recorderId, IProgress<BurnProgress>? progress = null, CancellationToken ct = default) => Task.Run(() =>
    {
        if (!OneWrite.Wait(0, ct)) throw new BurnException(BurnError.Busy);
        try
        {
            Sta.Run(() =>
            {
                dynamic rec = Com("IMAPI2.MsftDiscRecorder2");
                dynamic erase = Com("IMAPI2.MsftDiscFormat2Erase");
                var locked = false;
                int cookie = 0;
                IConnectionPoint? point = null;
                try
                {
                    rec.InitializeDiscRecorder(recorderId);
                    if (!(bool)erase.IsRecorderSupported(rec)) throw new BurnException(BurnError.Unsupported);
                    if (!(bool)erase.IsCurrentMediaSupported(rec)) throw new BurnException(Present(rec) ? BurnError.NotErasable : BurnError.NoMedia);
                    erase.Recorder = rec;
                    erase.ClientName = "Inlay";
                    erase.FullErase = false;
                    rec.AcquireExclusiveAccess(true, "Inlay");
                    locked = true;
                    var iid = typeof(DDiscFormat2EraseEvents).GUID;
                    ((IConnectionPointContainer)erase).FindConnectionPoint(ref iid, out point);
                    point!.Advise(new EraseEvents(progress), out cookie);
                    using (KeepAwake.Now()) erase.EraseMedia();
                }
                catch (COMException e) { throw new BurnException(BurnException.Map(e.HResult), e.Message, e); }
                finally
                {
                    if (point is not null && cookie != 0) point.Unadvise(cookie);
                    if (locked) try { rec.ReleaseExclusiveAccess(); } catch (COMException) { }
                    Marshal.FinalReleaseComObject(erase);
                    Marshal.FinalReleaseComObject(rec);
                }
                return 0;
            });
            progress?.Report(new(BurnStage.Done, 1));
        }
        finally { OneWrite.Release(); }
    }, ct);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateStreamOnFileEx(string file, int mode, int attributes, bool create, IStream? template, out IStream stream);

    // ---------------- IMAPI2 event sinks ----------------

    [ComImport, Guid("2735413C-7F64-5B0F-8F00-5D77AFBE261E"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface DDiscFormat2DataEvents
    {
        [DispId(0x200)] void Update([MarshalAs(UnmanagedType.IDispatch)] object sender, [MarshalAs(UnmanagedType.IDispatch)] object progress);
    }

    [ComImport, Guid("2735413A-7F64-5B0F-8F00-5D77AFBE261E"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    public interface DDiscFormat2EraseEvents
    {
        [DispId(0x200)] void Update([MarshalAs(UnmanagedType.IDispatch)] object sender, int elapsedSeconds, int estimatedTotalSeconds);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    sealed class DataEvents(long size, double oneX, IProgress<BurnProgress>? progress, CancellationToken ct) : DDiscFormat2DataEvents
    {
        long _lastLba = -1;
        DateTime _lastAt = DateTime.UtcNow;
        double? _speed;

        public void Update(object sender, object args)
        {
            dynamic e = args;
            if (ct.IsCancellationRequested) { try { ((dynamic)sender).CancelWrite(); } catch (COMException) { } return; }
            var action = (int)e.CurrentAction; // IMAPI_FORMAT2_DATA_WRITE_ACTION
            var stage = action switch { 0 or 2 or 3 => BurnStage.Preparing, 1 => BurnStage.Formatting, 4 => BurnStage.Writing, _ => BurnStage.Finalizing };
            if (stage != BurnStage.Writing) { progress?.Report(new(stage, 0)); return; }
            long start = (int)e.StartLba, count = (int)e.SectorCount, last = (int)e.LastWrittenLba;
            var now = DateTime.UtcNow;
            if (_lastLba >= 0 && now > _lastAt) _speed = (last - _lastLba) / (now - _lastAt).TotalSeconds / oneX;
            (_lastLba, _lastAt) = (last, now);
            var written = Math.Clamp((last - start) * 2048, 0, size);
            progress?.Report(new(BurnStage.Writing, count > 0 ? (double)(last - start) / count : 0, written, size, _speed, (int)e.RemainingTime));
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    sealed class EraseEvents(IProgress<BurnProgress>? progress) : DDiscFormat2EraseEvents
    {
        public void Update(object sender, int elapsed, int total) =>
            progress?.Report(new(BurnStage.Formatting, total > 0 ? Math.Min(1, (double)elapsed / total) : 0, RemainingSec: Math.Max(0, total - elapsed)));
    }
}

/// <summary>§8.5 "uyku engelle": the system stays awake while this thread burns.</summary>
static class KeepAwake
{
    const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_AWAYMODE_REQUIRED = 0x40;
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);

    public static IDisposable Now()
    {
        SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED);
        return new Release();
    }

    sealed class Release : IDisposable { public void Dispose() => SetThreadExecutionState(ES_CONTINUOUS); }
}

using Inlay.Analysis;
using Inlay.Analysis.Online;
using Inlay.Burn;
using Inlay.Core;
using Inlay.Media;
using Inlay.Theming;

namespace Inlay.Builder;

/// <summary>Why the disc cannot be made yet, in user terms (the UI shows <see cref="Exception.Message"/>).</summary>
public sealed class BuildBlockedException(string message) : Exception(message);

/// <summary>Stage of a long job ("prepare", "hash", "write", "finalize", "verify", "read", "erase", "done") and its 0..1
/// progress; burns also report the drive's speed (in x) and IMAPI2's own time estimate.</summary>
public readonly record struct JobProgress(string Stage, double Value, long DoneBytes = 0, long TotalBytes = 0, double? SpeedX = null, double? RemainingSec = null);

/// <summary>What the done dialog shows after a burn (§8.5 report).</summary>
public sealed record BurnResult(bool Checked, int Files, IReadOnlyList<string> Mismatched, IReadOnlyList<string> Missing, IReadOnlyList<double> ReadMBps);

public sealed partial class Session
{
    /// <summary>Lint errors and an over-full disc stop burning and ISO export (§7.1.5, §8.4).</summary>
    public void EnsureBuildable()
    {
        if (_ui is null || OutputDraft() is null) throw new BuildBlockedException(T("builder.blocked.empty"));
        if (_ui.Blocking) throw new BuildBlockedException(T("builder.blocked.theme"));
        if (CapacityOf() is { Fits: false }) throw new BuildBlockedException(T("builder.blocked.capacity"));
    }

    /// <summary>Runs every file job, then lays out the disc (menu, icon, autorun, launcher, player).</summary>
    public async Task<DiscFiles> Prepare(IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        EnsureBuildable();
        var draft = OutputDraft()!;
        var work = InlayFolders.Work(Project.Id);
        var content = await Materializer.Run(draft, work, progress: new Progress<double>(v => progress?.Report(new("prepare", v))), ct: ct);
        var disc = DiscOutput.Assemble(content, _theme!, System.IO.Path.Combine(work, ".gen"), Project.Icon);
        if (disc.Ui.Blocking) throw new BuildBlockedException(T("builder.blocked.theme"));
        return disc;
    }

    public DiscImagePlan Plan(DiscFiles disc) =>
        new(disc.Manifest.Disc.Label, disc.Manifest.Disc.Media, disc.Files.Select(f => new DiscEntry(f.Key, f.Value)).ToList(), [".disc"], _prefs.UdfRevision);

    /// <summary>"ISO olarak kaydet".</summary>
    public async Task ExportIso(string iso, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var disc = await Prepare(progress, ct);
        var plan = Plan(disc);
        if (DiscImage.Check(plan) is { Count: > 0 } problems) throw new BuildBlockedException(T("builder.blocked.image", string.Join(", ", problems)));
        var total = plan.ContentBytes;
        await Task.Run(() => DiscImage.ExportIso(plan, iso, new Progress<double>(v => progress?.Report(new("write", v, (long)(v * total), total))), ct), ct);
        progress?.Report(new("done", 1, total, total));
    }

    // ---------------- burning (§8.5) ----------------

    DiscFiles? _prepared;
    int _preparedRev = -1;
    volatile bool _checkAndEject = true;
    string? _copyImage;

    /// <summary>The recorder to use: the one chosen in Gelişmiş, else the first with a writable disc, else any.</summary>
    public string? RecorderId => (_prefs.Recorder is { } r && _drives.Any(d => d.Id == r) ? r : null)
        ?? _drives.FirstOrDefault(d => d.Disc?.Writable == true)?.Id ?? _drives.FirstOrDefault()?.Id;

    /// <summary>"Bitince kontrol et ve diski çıkar", which the user may change while the disc is being written.</summary>
    public void SetCheck(bool on) => _checkAndEject = on;

    public async Task<BurnResult> Burn(bool check, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var id = RecorderId ?? throw new BurnException(BurnError.NoMedia);
        _checkAndEject = check;
        // "Bir kopya daha": the same render burns again without preparing the files twice.
        var disc = _preparedRev == _rev && _prepared is not null ? _prepared : await Prepare(progress, ct);
        (_prepared, _preparedRev) = (disc, _rev);
        var plan = Plan(disc);
        if (DiscImage.Check(plan) is { Count: > 0 } problems) throw new BuildBlockedException(T("builder.blocked.image", string.Join(", ", problems)));
        try
        {
            var report = await Burner.Burn(plan, new BurnOptions(id, _prefs.Speed, () => _checkAndEject), Relay(progress), ct);
            return report is null ? new BurnResult(false, plan.Files.Count, [], [], []) : new BurnResult(true, report.Files, report.Mismatched, report.Missing, report.ReadMBps);
        }
        finally { await RefreshDrives(); }
    }

    /// <summary>"ISO kalıbı yaz".</summary>
    public async Task<BurnResult> BurnIso(string iso, bool check, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var id = RecorderId ?? throw new BurnException(BurnError.NoMedia);
        _checkAndEject = check;
        try
        {
            var same = await Burner.BurnIso(iso, new BurnOptions(id, _prefs.Speed, () => _checkAndEject), Relay(progress), ct);
            return new BurnResult(same is not null, 1, same == false ? [System.IO.Path.GetFileName(iso)] : [], [], []);
        }
        finally { await RefreshDrives(); }
    }

    /// <summary>"Diski sil": quick erase of a rewritable disc.</summary>
    public async Task Erase(IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var id = RecorderId ?? throw new BurnException(BurnError.NoMedia);
        try { await Burner.Erase(id, Relay(progress, "erase"), ct); }
        finally { await RefreshDrives(); }
    }

    /// <summary>"Disk kopyala" step 1 (ADR 0004): the data disc in <paramref name="recorderId"/> becomes an image, and the
    /// tray opens for the blank disc. Step 2 is <see cref="BurnIso"/> with the returned image.</summary>
    public async Task<string> ReadDisc(string recorderId, IProgress<JobProgress>? progress = null, CancellationToken ct = default)
    {
        var drive = _drives.FirstOrDefault(d => d.Id == recorderId) ?? throw new BurnException(BurnError.NoMedia);
        if (drive.Letter.Length == 0 || !new DriveInfo(drive.Letter + "\\").IsReady) throw new BurnException(BurnError.NoMedia);
        if (_copyImage is not null) File.Delete(_copyImage);
        Directory.CreateDirectory(System.IO.Path.Combine(InlayFolders.Root, "copy"));
        var iso = System.IO.Path.Combine(InlayFolders.Root, "copy", $"disk-{DateTime.Now:yyyyMMdd-HHmmss}.iso");
        await RawVolume.ReadImage(drive.Letter + "\\", iso, Relay(progress, "read"), ct);
        _copyImage = iso;
        Burner.Eject(recorderId);
        await RefreshDrives();
        return iso;
    }

    /// <summary>"Disk tak": opens the tray.</summary>
    public async Task OpenTray()
    {
        if (RecorderId is { } id) await Task.Run(() => Burner.OpenTray(id));
    }

    static IProgress<BurnProgress> Relay(IProgress<JobProgress>? p, string? only = null) => new SyncRelay(b => p?.Report(new JobProgress(only ?? b.Stage switch
    {
        BurnStage.Hashing => "hash",
        BurnStage.Writing => "write",
        BurnStage.Finalizing => "finalize",
        BurnStage.Verifying => "verify",
        BurnStage.Done => "done",
        _ => "write", // IMAPI2 getting the drive ready: the bar waits at the start of writing
    }, b.Value, b.DoneBytes, b.TotalBytes, b.SpeedX, b.RemainingSec)));

    /// <summary>IMAPI2 reports on its own thread; the host's Progress&lt;T&gt; marshals to the UI.</summary>
    sealed class SyncRelay(Action<BurnProgress> report) : IProgress<BurnProgress> { public void Report(BurnProgress value) => report(value); }
}

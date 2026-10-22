using System.Diagnostics;
using System.Security.Cryptography;

namespace Inlay.Burn;

/// <summary>§8.5 verification: SHA-256 of every file before writing, read back from the finished disc afterwards,
/// with the read speed sampled once a second for the report's graph.</summary>
public static class Verification
{
    const int Chunk = 1 << 20;

    public static async Task<IReadOnlyDictionary<string, string>> Hash(IReadOnlyList<(string DiskPath, string Source)> files,
        IProgress<BurnProgress>? progress = null, CancellationToken ct = default)
    {
        var total = files.Sum(f => new FileInfo(f.Source).Length);
        long done = 0;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (disk, source) in files)
        {
            await using var s = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, Chunk, FileOptions.SequentialScan);
            result[disk] = await Sha(s, n => progress?.Report(new(BurnStage.Hashing, (double)(done += n) / Math.Max(1, total), done, total)), ct);
        }
        return result;
    }

    /// <summary>Reads every hashed file from <paramref name="root"/> (the burned disc) and compares.</summary>
    public static async Task<VerifyReport> Check(string root, IReadOnlyDictionary<string, string> hashes, IProgress<BurnProgress>? progress = null, CancellationToken ct = default)
    {
        var mismatched = new List<string>();
        var missing = new List<string>();
        var meter = new Meter();
        var total = hashes.Keys.Sum(k => SafeLength(Path.Combine(root, k.Replace('/', '\\'))));
        long done = 0;
        foreach (var (disk, expected) in hashes.OrderBy(h => h.Key, StringComparer.Ordinal))
        {
            var path = Path.Combine(root, disk.Replace('/', '\\'));
            if (!File.Exists(path)) { missing.Add(disk); continue; }
            try
            {
                await using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, Chunk, FileOptions.SequentialScan);
                var actual = await Sha(s, n => { meter.Add(n); progress?.Report(new(BurnStage.Verifying, (double)(done += n) / Math.Max(1, total), done, total)); }, ct);
                if (actual != expected) mismatched.Add(disk);
            }
            catch (IOException) { mismatched.Add(disk); } // unreadable sectors
        }
        return new VerifyReport(hashes.Count, mismatched, missing, meter.Samples);
    }

    /// <summary>"ISO kalıbı yaz" check: the disc's data area equals the image. A volume only reads as far as its file
    /// system says; IMAPI images run on past that with padding and the closing UDF anchor, so the image is compared
    /// over the readable length.</summary>
    public static async Task<bool> SameSectors(string iso, string root, IProgress<BurnProgress>? progress = null, CancellationToken ct = default)
    {
        var length = Math.Min(new FileInfo(iso).Length, new DriveInfo(root).TotalSize);
        await using var source = new FileStream(iso, FileMode.Open, FileAccess.Read, FileShare.Read, Chunk, FileOptions.SequentialScan);
        var expected = await Sha(new Limited(source, length), _ => { }, ct);
        await using var disc = RawVolume.Open(root);
        long done = 0;
        var actual = await Sha(new Limited(disc, length), n => progress?.Report(new(BurnStage.Verifying, (double)(done += n) / length, done, length)), ct);
        return actual == expected;
    }

    static long SafeLength(string path) { try { return new FileInfo(path).Length; } catch (IOException) { return 0; } }

    static async Task<string> Sha(Stream s, Action<int> read, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[Chunk];
        int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            sha.AppendData(buf, 0, n);
            read(n);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    /// <summary>MB/s once a second.</summary>
    sealed class Meter
    {
        readonly Stopwatch _clock = Stopwatch.StartNew();
        long _bytes;
        double _windowStart;
        public List<double> Samples { get; } = [];

        public void Add(int n)
        {
            _bytes += n;
            var now = _clock.Elapsed.TotalSeconds;
            if (now - _windowStart < 1) return;
            Samples.Add(_bytes / 1e6 / (now - _windowStart));
            (_bytes, _windowStart) = (0, now);
        }
    }

    /// <summary>Reads at most <paramref name="length"/> bytes: a disc's last sector may run past the ISO's end.</summary>
    sealed class Limited(Stream inner, long limit) : Stream
    {
        readonly long _limit = limit;
        long _left = limit;
        public override int Read(byte[] buffer, int offset, int count) => Take(inner.Read(buffer, offset, (int)Math.Min(count, Round(_left))));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            Take(await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, Round(_left))], ct));
        int Take(int n) { var keep = (int)Math.Min(n, _left); _left -= keep; return keep; }
        static long Round(long left) => left <= 0 ? 0 : (left + 2047) / 2048 * 2048; // volume reads are whole sectors
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _limit;
        public override long Position { get => _limit - _left; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>A data disc read as raw 2048-byte sectors (\\.\E:), for ISO verification and "Disk kopyala" (ADR 0004).</summary>
public static class RawVolume
{
    public static FileStream Open(string root) =>
        new($@"\\.\{root.TrimEnd('\\', '/')}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);

    /// <summary>The disc's data area as an image file (the file system's own size, so trailing run-out is not copied).</summary>
    public static async Task ReadImage(string root, string iso, IProgress<BurnProgress>? progress = null, CancellationToken ct = default)
    {
        var total = new DriveInfo(root).TotalSize;
        var tmp = iso + ".part";
        try
        {
            await using (var disc = Open(root))
            await using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                var buf = new byte[1 << 20];
                long done = 0;
                while (done < total)
                {
                    var n = await disc.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, total - done)), ct);
                    if (n == 0) break;
                    await output.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    progress?.Report(new(BurnStage.Preparing, (double)done / total, done, total));
                }
            }
            File.Move(tmp, iso, overwrite: true);
        }
        finally { File.Delete(tmp); }
    }
}

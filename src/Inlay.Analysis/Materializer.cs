using System.Collections.Concurrent;
using Inlay.Core;
using Inlay.Imaging;
using Inlay.Media;

namespace Inlay.Analysis;

/// <summary>The files of a draft, ready to image: disc path → local file. Copies point at the source itself.</summary>
public sealed record Materialized(IReadOnlyDictionary<string, string> Files, DiscManifest Manifest);

/// <summary>Runs every job of a draft into the project work folder (%LOCALAPPDATA%\Inlay\work\&lt;id&gt;, §6.2) with
/// bounded parallelism and byte-weighted progress, then patches what only the outputs know (BlurHash) into the manifest.</summary>
public static class Materializer
{
    public static async Task<Materialized> Run(DiscDraft draft, string workDir, FfmpegQueue? queue = null,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        queue ??= new FfmpegQueue();
        var files = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var blur = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var total = Math.Max(1, draft.Files.Values.Sum(j => j.EstimateBytes));
        long done = 0;
        void Advance(FileJob j) => progress?.Report((double)Interlocked.Add(ref done, j.EstimateBytes) / total);

        // Media jobs go through the FFmpeg queue (it limits CPU use); images and frames are cheap enough to run beside them.
        await Parallel.ForEachAsync(draft.Files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct }, async (entry, token) =>
        {
            var (disk, job) = (entry.Key, entry.Value);
            var output = Path.Combine(workDir, disk.Replace('/', Path.DirectorySeparatorChar));
            switch (job)
            {
                case CopyJob c:
                    files[disk] = c.Source;
                    break;
                case BytesJob b:
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    await File.WriteAllBytesAsync(output, b.Content, token);
                    files[disk] = output;
                    break;
                case MediaJob m:
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    await queue.Run(FfmpegArgs.For(m.Plan, m.Info, m.Source, output), m.Info.DurationSec, ct: token);
                    files[disk] = output;
                    break;
                case VttJob v:
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    await queue.Run(FfmpegArgs.ToVtt(v.Source, v.Stream, output), null, ct: token);
                    files[disk] = output;
                    break;
                case FrameJob f:
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    await queue.Run(FfmpegArgs.Frame(f.Source, f.AtSec, f.Max, output), null, ct: token);
                    files[disk] = output;
                    break;
                case ImageJob { Role: ImageRole.Display } i:
                    blur[disk] = ImagePipeline.WriteDisplay(i.Source, output, i.Display ?? ImagePipeline.PlanDisplay(i.Source));
                    files[disk] = i.Display is { Copy: true } ? i.Source : output;
                    break;
                case ImageJob i:
                    ImagePipeline.WriteFitted(i.Source, output, i.Max, widthOnly: i.WidthOnly);
                    files[disk] = output;
                    break;
                case IconJob icon:
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    if (!LocalMeta.ExtractIcon(icon.Source, output)) DiscIcon.WriteDefaultPng(output);
                    files[disk] = output;
                    break;
                default:
                    throw new NotSupportedException(job.GetType().Name);
            }
            Advance(job);
        });

        var manifest = draft.Manifest with
        {
            Items = draft.Manifest.Items.Select(i => i is Gallery g
                ? g with { Photos = g.Photos.Select(p => blur.TryGetValue(p.File, out var h) ? p with { Blurhash = h } : p).ToList() }
                : i).ToList(),
        };
        return new Materialized(files, manifest);
    }
}

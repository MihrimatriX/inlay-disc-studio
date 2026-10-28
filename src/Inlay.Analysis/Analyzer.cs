using System.Collections.Concurrent;
using Inlay.Media;

namespace Inlay.Analysis;

public sealed record AnalysisResult(IReadOnlyList<SourceFile> Files, IReadOnlyDictionary<string, FileFacts> Facts, List<ContentGroup> Groups)
{
    public FileFacts FactsOf(SourceFile f) => Facts.GetValueOrDefault(f.FullPath) ?? new FileFacts(null, null);

    /// <summary>§5.2: several kinds above threshold → mixed. Prerequisites never count as content.</summary>
    public bool Mixed => Groups.Where(g => g.Kind != ContentKind.Prerequisite).Select(g => g.Kind).Distinct().Count() > 1;
}

/// <summary>Scan → probe (ffprobe, TagLib) in parallel → group. Runs in the background after a drop (§11).</summary>
public static class Analyzer
{
    public static async Task<AnalysisResult> Run(IEnumerable<string> roots, IReadOnlyDictionary<string, ContentKind>? overrides = null,
        IProgress<double>? progress = null, CancellationToken ct = default, IReadOnlyCollection<string>? excluded = null)
    {
        // Files the user removed in "Disk içeriği" (scanner-relative, a file or a whole folder).
        var files = Scanner.Scan(roots).Where(f => excluded is null || !excluded.Any(x => f.Rel == x || f.Rel.StartsWith(x + "/", StringComparison.Ordinal))).ToList();
        var probe = files.Where(f => f.Type is FileType.Video or FileType.Audio).ToList();
        var facts = new ConcurrentDictionary<string, FileFacts>();
        var done = 0;
        await Parallel.ForEachAsync(probe, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (f, token) =>
        {
            MediaInfo? media = null;
            try { media = await FfProbe.Run(f.FullPath, token); }
            catch (MediaException) { } // unreadable file: it still goes on the disc as a plain file
            var tags = f.Type == FileType.Audio ? LocalMeta.Audio(f.FullPath) : null;
            facts[f.FullPath] = new FileFacts(media, tags);
            progress?.Report((double)Interlocked.Increment(ref done) / probe.Count);
        });
        var groups = Grouping.Group(files, f => facts.GetValueOrDefault(f.FullPath) ?? new FileFacts(null, null), overrides);
        return new AnalysisResult(files, facts, groups);
    }
}

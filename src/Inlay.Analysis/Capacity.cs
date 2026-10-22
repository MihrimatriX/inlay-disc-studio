using Inlay.Core;

namespace Inlay.Analysis;

public enum SuggestionKind { BiggerMedia, DropOriginals, DropTranscodedCopies, DropTrailer, DropExtras }

/// <summary>One way out when the disc is full (§6.1 end, §8.4). <see cref="SavedBytes"/> is shown as "(−86 MB)".</summary>
public sealed record Suggestion(SuggestionKind Kind, long SavedBytes, string? ItemId = null, DiscMedia? Media = null);

public static class Capacity
{
    /// <summary>Options that make the draft fit, cheapest change first; empty when it already fits.</summary>
    public static IReadOnlyList<Suggestion> Suggest(DiscDraft draft, long reservedBytes = 30L * 1024 * 1024)
    {
        var media = draft.Manifest.Disc.Media;
        var total = draft.ContentBytes + reservedBytes;
        if (MediaCapacity.Fits(total, media)) return [];
        var list = new List<Suggestion>();
        var m = draft.Manifest;

        var originals = m.Items.SelectMany(MediaOf).Select(f => f.Playback.Original).OfType<string>()
            .Concat(m.Items.OfType<Gallery>().SelectMany(g => g.Photos).Select(p => p.Original).OfType<string>())
            .Distinct().Sum(Size);
        if (originals > 0) list.Add(new Suggestion(SuggestionKind.DropOriginals, originals));

        // Maximum compatibility kept both: the original HEVC (LibVLC) and a transcoded H.264 copy. Keep the HEVC.
        var transcoded = m.Items.SelectMany(MediaOf).Where(f => f.Playback is { Original: not null, Normalized: true } && f.Video?.Codec == "h264")
            .Where(f => draft.Files.GetValueOrDefault(f.File) is MediaJob { Plan.Step: Inlay.Media.PlanStep.Transcode }).Sum(f => Size(f.File));
        if (transcoded > 0) list.Add(new Suggestion(SuggestionKind.DropTranscodedCopies, transcoded));

        foreach (var movie in m.Items.OfType<Movie>())
        {
            if (movie.Trailer is { } t) list.Add(new Suggestion(SuggestionKind.DropTrailer, Size(t.File), movie.Id));
            if (movie.Extras is { Count: > 0 } x) list.Add(new Suggestion(SuggestionKind.DropExtras, x.Sum(e => Size(e.File)), movie.Id));
        }

        if (MediaCapacity.Smallest(total) is { } bigger && bigger > media) list.Add(new Suggestion(SuggestionKind.BiggerMedia, 0, Media: bigger));
        // Prefer changes that alone make the disc fit, then the smallest change.
        var over = total - MediaCapacity.Usable(media);
        return list.OrderBy(s => s.Kind == SuggestionKind.BiggerMedia ? 0 : s.SavedBytes >= over ? 1 : 2).ThenBy(s => s.SavedBytes).ToList();

        long Size(string disk) => draft.Files.GetValueOrDefault(disk)?.EstimateBytes ?? 0;
    }

    static IEnumerable<MediaFile> MediaOf(DiscItem i) => i switch
    {
        Movie m => new[] { m.Media, m.Trailer }.OfType<MediaFile>().Concat(m.Extras ?? []),
        Series s => s.Seasons.SelectMany(x => x.Episodes).Select(e => e.Media),
        Album a => a.Tracks.Select(t => t.Media),
        Gallery g => g.Photos.Select(p => p.Media).OfType<MediaFile>(),
        App a => a.Trailer is { } t ? [t] : [],
        _ => [],
    };
}

using Inlay.Core;

namespace Inlay.Analysis;

/// <summary>§7.0: what the disc opens to. Rule: the shortest way to the content — no menu in front of a single thing.</summary>
public static class OpeningView
{
    public static DiscHome Decide(IReadOnlyList<DiscItem> items)
    {
        // Prerequisites (VC++ runtime, DirectX…) install silently; they are not content of their own (§7.0).
        var content = items.Where(i => i is not App { Category: AppCategory.Runtime }).ToList();
        var kinds = content.Select(i => i.GetType()).Distinct().ToList();
        if (kinds.Count == 0) return items.Count > 0 ? new DiscHome { View = HomeView.Bundle } : new DiscHome { View = HomeView.Menu };
        if (kinds.Count > 1) return new DiscHome { View = HomeView.Menu };

        var kind = kinds[0];
        if (content.Count == 1)
            return kind == typeof(Series)
                ? new DiscHome { View = HomeView.Series, Item = content[0].Id }
                : new DiscHome { View = HomeView.Item, Item = content[0].Id };

        if (kind == typeof(App)) return new DiscHome { View = HomeView.Bundle };
        if (kind == typeof(Movie)) return new DiscHome { View = HomeView.Collection, Kind = CollectionKind.Movie };
        if (kind == typeof(Album)) return new DiscHome { View = HomeView.Collection, Kind = CollectionKind.Album };
        if (kind == typeof(Gallery)) return new DiscHome { View = HomeView.Collection, Kind = CollectionKind.Gallery };
        return new DiscHome { View = HomeView.Menu }; // several series or several documents: a plain list
    }
}

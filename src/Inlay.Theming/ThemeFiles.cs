using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Inlay.Core;
using Json.Schema;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Inlay.Theming;

/// <summary>A theme package in memory: theme.json + entry + partials + assets/ + fonts/ (§4.2). Small by budget, so
/// folders, standard themes (with the shared layer) and .disctheme zips all load the same way.</summary>
public sealed class ThemeFiles
{
    public required ThemePackage Theme { get; init; }
    public required IReadOnlyDictionary<string, byte[]> Files { get; init; }

    static readonly Lazy<JsonSchema> Schema = new(() =>
    {
        using var s = typeof(ThemeFiles).Assembly.GetManifestResourceStream("theme.schema.json")!;
        return JsonSchema.FromText(new StreamReader(s).ReadToEnd());
    });

    /// <summary>A theme folder; files missing there fall back to <paramref name="shared"/> (standard themes share partials and assets).</summary>
    public static ThemeFiles FromFolder(string dir, string? shared = null)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        void Add(string root)
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                files.TryAdd(rel, File.ReadAllBytes(f));
            }
        }
        Add(dir);
        if (shared is not null && Directory.Exists(shared)) Add(shared);
        return Create(files);
    }

    /// <summary>Standard themes by package id ("ds.standard.movie"), from the builder's themes/ folder or the checkout.</summary>
    public static ThemeFiles Standard(string packageId)
    {
        var folder = "standard-" + packageId.Split('.')[^1];
        if (ThemesDir() is { } themes && Directory.Exists(Path.Combine(themes, folder)))
            return FromFolder(Path.Combine(themes, folder), Path.Combine(themes, "_shared"));
        throw new ThemeException($"standard theme {packageId} not found");
    }

    /// <summary>Every standard theme, in folder order (the builder's template cards).</summary>
    public static IReadOnlyList<ThemeFiles> AllStandard() => ThemesDir() is { } themes
        ? Directory.GetDirectories(themes, "standard-*").Order(StringComparer.Ordinal).Select(d => FromFolder(d, Path.Combine(themes, "_shared"))).ToList()
        : [];

    static string? ThemesDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "themes", "_shared"))) return Path.Combine(d.FullName, "themes");
        return null;
    }

    /// <summary>A package held in memory elsewhere (the theme a .discproj embeds), checked like any other.</summary>
    public static ThemeFiles FromFiles(IReadOnlyDictionary<string, byte[]> files) => Create(new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase));

    public static ThemeFiles FromZip(Stream zip)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var e in archive.Entries.Where(e => e.Length > 0 || !e.FullName.EndsWith('/')))
        {
            var rel = e.FullName.Replace('\\', '/');
            if (!RelPath.IsValid(rel)) throw new ThemeException($"unsafe path in package: {rel}"); // zip-slip
            total += e.Length;
            if (total > 64L * 1024 * 1024) throw new ThemeException("package is larger than 64 MB");
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            files[rel] = ms.ToArray();
        }
        return Create(files);
    }

    public void SaveZip(Stream output)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var (path, bytes) in Files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            var e = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var s = e.Open();
            s.Write(bytes);
        }
    }

    static ThemeFiles Create(Dictionary<string, byte[]> files)
    {
        if (!files.TryGetValue("theme.json", out var json)) throw new ThemeException("theme.json is missing");
        using var doc = JsonDocument.Parse(json);
        var result = Schema.Value.Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!result.IsValid)
            throw new ThemeException("theme.json: " + string.Join("; ", result.Details?.Where(d => d.Errors is not null)
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation} {e.Value}")).Take(5) ?? []));
        var theme = JsonSerializer.Deserialize(json, InlayJson.Default.ThemePackage)!;
        if (theme.Engine == ThemeEngine.Liquid && !files.ContainsKey(theme.Entry)) throw new ThemeException($"entry {theme.Entry} is missing");
        return new ThemeFiles { Theme = theme, Files = files };
    }

    public string Text(string path) => Encoding.UTF8.GetString(Files[path]);

    /// <summary>What Fluid's include/render tags read.</summary>
    internal IFileProvider Provider => new MemoryProvider(Files);

    sealed class MemoryProvider(IReadOnlyDictionary<string, byte[]> files) : IFileProvider
    {
        public IFileInfo GetFileInfo(string subpath)
        {
            var key = subpath.TrimStart('/', '\\').Replace('\\', '/');
            return files.TryGetValue(key, out var b) ? new Entry(key, b) : new NotFoundFileInfo(key);
        }
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

        sealed class Entry(string name, byte[] bytes) : IFileInfo
        {
            public bool Exists => true;
            public long Length => bytes.Length;
            public string? PhysicalPath => null;
            public string Name => name;
            public DateTimeOffset LastModified => DateTimeOffset.MinValue;
            public bool IsDirectory => false;
            public Stream CreateReadStream() => new MemoryStream(bytes, writable: false);
        }
    }
}

public sealed class ThemeException(string message) : Exception(message);

namespace Weld.World.Assets;

public sealed class AssetEntry
{
    private readonly Func<Stream> _open;

    public AssetEntry(IAssetArchive archive, string entryName, Func<Stream> open, string? physicalPath = null, long size = -1)
    {
        Archive = archive;
        EntryName = entryName;
        _open = open;
        PhysicalPath = physicalPath;
        Size = size;
    }

    public IAssetArchive Archive { get; }
    public string EntryName { get; }
    public string? PhysicalPath { get; }
    public long Size { get; }

    public Stream Open() => _open();

    public byte[] ReadAllBytes()
    {
        using var s = Open();
        if (s is MemoryStream ms) return ms.ToArray();
        using var copy = new MemoryStream(Size > 0 ? (int)Math.Min(Size, int.MaxValue) : 0);
        s.CopyTo(copy);
        return copy.ToArray();
    }

    public override string ToString() => $"{Archive.Name}:{EntryName}";
}

public interface IAssetArchive : IDisposable
{
    string Name { get; }

    bool Contains(string entryName);

    AssetEntry? Find(string entryName);

    IEnumerable<string> EnumerateEntries();
}

public sealed class DirectoryArchive : IAssetArchive
{
    private readonly Dictionary<string, string> _index = new(StringComparer.OrdinalIgnoreCase);

    public DirectoryArchive(string name, string directory)
    {
        Name = name;
        Directory = Path.GetFullPath(directory);
        Exists = System.IO.Directory.Exists(Directory);
        if (Exists) BuildIndex();
    }

    public string Name { get; }
    public string Directory { get; }
    public bool Exists { get; }

    public static string ArchivePathToDirectory(string imgPath)
    {
        imgPath = imgPath.Replace('\\', '/');
        var slash = imgPath.LastIndexOf('/');
        var dir = slash >= 0 ? imgPath[..(slash + 1)] : "";
        var file = slash >= 0 ? imgPath[(slash + 1)..] : imgPath;
        return dir + file.Replace('.', '_');
    }

    public static DirectoryArchive FromImgReference(string gameRoot, string imgPath)
        => new(imgPath, Path.Combine(gameRoot, ArchivePathToDirectory(imgPath)));

    private void BuildIndex()
    {
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Directory, file).Replace('\\', '/');
            _index[rel] = file;
        }
    }

    public void Refresh()
    {
        _index.Clear();
        if (Exists) BuildIndex();
    }

    public bool Contains(string entryName) => _index.ContainsKey(Normalize(entryName));

    public AssetEntry? Find(string entryName)
    {
        if (!_index.TryGetValue(Normalize(entryName), out var full)) return null;
        var size = new FileInfo(full).Length;
        return new AssetEntry(this, entryName, () => File.Open(full, FileMode.Open, FileAccess.Read, FileShare.Read), full, size);
    }

    public IEnumerable<string> EnumerateEntries() => _index.Keys;

    private static string Normalize(string entry) => entry.Replace('\\', '/').TrimStart('/');

    public void Dispose() { }

    public override string ToString() => $"{Name} → {Directory}{(Exists ? "" : " (missing)")}";
}

namespace Weld.World.Assets;

public interface IModel : IDisposable
{
    string Name { get; }
    BoundingBox? Bounds { get; }
    long MemoryFootprint { get; }
}

public interface ITextureDictionary : IDisposable
{
    string Name { get; }
    long MemoryFootprint { get; }
}

public interface ICollisionModel : IDisposable
{
    string Name { get; }
}

public sealed class AssetLoadContext
{
    public AssetLoadContext(Level level, string assetName, ItemDefinition? definition, CancellationToken cancellation)
    {
        Level = level;
        AssetName = assetName;
        Definition = definition;
        Cancellation = cancellation;
    }

    public Level Level { get; }
    public string AssetName { get; }
    public ItemDefinition? Definition { get; }
    public CancellationToken Cancellation { get; }
    public IWorldLog Log => Level.Log;
}

public interface IAssetFormat
{
    string Name { get; }

    IEnumerable<string> ResolveEntryNames(string assetName);
}

public interface IModelFormat : IAssetFormat
{
    IModel Load(AssetLoadContext context, AssetEntry entry);
}

public interface ITextureDictionaryFormat : IAssetFormat
{
    ITextureDictionary Load(AssetLoadContext context, AssetEntry entry);
}

public interface ICollisionFormat : IAssetFormat
{
    ICollisionModel Load(AssetLoadContext context, AssetEntry entry);
}

public sealed class AssetFormatRegistry
{
    private readonly List<IModelFormat> _models = new();
    private readonly List<ITextureDictionaryFormat> _txds = new();
    private readonly List<ICollisionFormat> _cols = new();

    public IReadOnlyList<IModelFormat> ModelFormats => _models;
    public IReadOnlyList<ITextureDictionaryFormat> TextureDictionaryFormats => _txds;
    public IReadOnlyList<ICollisionFormat> CollisionFormats => _cols;

    public AssetFormatRegistry AddModelFormat(IModelFormat f) { _models.Add(f); return this; }
    public AssetFormatRegistry AddTextureDictionaryFormat(ITextureDictionaryFormat f) { _txds.Add(f); return this; }
    public AssetFormatRegistry AddCollisionFormat(ICollisionFormat f) { _cols.Add(f); return this; }

    public bool TryResolve<TFormat>(IReadOnlyList<TFormat> formats, IReadOnlyList<IAssetArchive> archives, string assetName,
        out TFormat format, out AssetEntry entry) where TFormat : IAssetFormat
    {
        foreach (var f in formats)
        {
            foreach (var candidate in f.ResolveEntryNames(assetName))
            {
                foreach (var archive in archives)
                {
                    var e = archive.Find(candidate);
                    if (e != null)
                    {
                        format = f;
                        entry = e;
                        return true;
                    }
                }
            }
        }
        format = default!;
        entry = null!;
        return false;
    }

    public bool TryResolveModel(IReadOnlyList<IAssetArchive> archives, string dffFile, out IModelFormat format, out AssetEntry entry)
        => TryResolve(_models, archives, dffFile, out format, out entry);

    public bool TryResolveTextureDictionary(IReadOnlyList<IAssetArchive> archives, string txdName, out ITextureDictionaryFormat format, out AssetEntry entry)
        => TryResolve(_txds, archives, txdName, out format, out entry);
}

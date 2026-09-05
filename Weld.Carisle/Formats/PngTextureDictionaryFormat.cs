using StbImageSharp;
using Weld.World;
using Weld.World.Assets;

namespace Weld.Carisle.Formats;

public sealed class CpuTexture
{
    public CpuTexture(string name, int width, int height, byte[] rgba)
    {
        Name = name; Width = width; Height = height; Rgba = rgba;
    }
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; private set; }
    internal void Release() => Rgba = Array.Empty<byte>();
}

public sealed class PngTextureDictionary : ITextureDictionary
{
    private readonly Dictionary<string, CpuTexture> _textures;

    public PngTextureDictionary(string name, Dictionary<string, CpuTexture> textures)
    {
        Name = name;
        _textures = textures;
        MemoryFootprint = textures.Values.Sum(t => (long)t.Width * t.Height * 4 * 4 / 3);
    }

    public string Name { get; }
    public long MemoryFootprint { get; }
    public IReadOnlyDictionary<string, CpuTexture> Textures => _textures;
    public CpuTexture? Fallback => _textures.GetValueOrDefault(PngTextureDictionaryFormat.FallbackTextureName);

    public CpuTexture? Find(string? textureName)
    {
        if (textureName != null && _textures.TryGetValue(textureName, out var t)) return t;
        return Fallback;
    }

    public void ReleaseCpuData()
    {
        foreach (var t in _textures.Values) t.Release();
    }

    public void Dispose() => ReleaseCpuData();
}

public sealed class PngTextureDictionaryFormat : ITextureDictionaryFormat
{
    public const string FallbackTextureName = "texture";

    public PngTextureDictionaryFormat(string folderSuffix = "txd")
    {
        FolderSuffix = folderSuffix;
    }

    public string Name => "PNG folder TXD";
    public string FolderSuffix { get; }

    public string FolderFor(string txdName) => txdName + FolderSuffix;

    public IEnumerable<string> ResolveEntryNames(string assetName)
    {
        yield return $"{FolderFor(assetName)}/{FallbackTextureName}.png";
    }

    public ITextureDictionary Load(AssetLoadContext context, AssetEntry entry)
    {
        var folder = FolderFor(context.AssetName) + "/";
        var textures = new Dictionary<string, CpuTexture>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in entry.Archive.EnumerateEntries())
        {
            if (!name.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.IndexOf('/', folder.Length) >= 0) continue;

            var e = entry.Archive.Find(name);
            if (e == null) continue;
            context.Cancellation.ThrowIfCancellationRequested();

            using var stream = e.Open();
            var img = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            var stem = Path.GetFileNameWithoutExtension(name);
            textures[stem] = new CpuTexture(stem, img.Width, img.Height, img.Data);
        }

        if (!textures.ContainsKey(FallbackTextureName))
            throw new FileNotFoundException($"TXD '{context.AssetName}' has no {FallbackTextureName}.png in {folder}");

        context.Log.Debug($"[{Name}] {context.AssetName}: {textures.Count} texture(s)");
        return new PngTextureDictionary(context.AssetName, textures);
    }
}

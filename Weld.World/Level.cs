using Weld.World.Assets;
using Weld.World.Collision;
using Weld.World.Data;
using Weld.World.Streaming;

namespace Weld.World;

public sealed partial class Level : IDisposable
{
    private readonly List<IAssetArchive> _archives = new();
    private readonly List<ItemDefinition> _definitions = new();
    private readonly Dictionary<string, ItemDefinition> _definitionsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, ItemDefinition> _definitionsById = new();
    private readonly List<WorldInstance> _instances = new();
    private readonly Dictionary<string, StreamedModel> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StreamedTextureDictionary> _txds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ColliderPlacement> _colliders = new();
    private bool _disposed;

    private Level(string gameRoot, string datPath, LevelFile manifest, AssetFormatRegistry formats, LevelOptions options, IWorldLog log)
    {
        GameRoot = Path.GetFullPath(gameRoot);
        DatPath = datPath;
        Manifest = manifest;
        Formats = formats;
        Options = options;
        Log = log;
        InitializeStreaming();
    }

    public string GameRoot { get; }
    public string DatPath { get; }
    public LevelFile Manifest { get; }
    public AssetFormatRegistry Formats { get; }
    public LevelOptions Options { get; }
    public IWorldLog Log { get; }

    public IReadOnlyList<IAssetArchive> Archives => _archives;
    public IReadOnlyList<ItemDefinition> Definitions => _definitions;
    public IReadOnlyList<WorldInstance> Instances => _instances;
    public IReadOnlyCollection<StreamedModel> Models => _models.Values;
    public IReadOnlyCollection<StreamedTextureDictionary> TextureDictionaries => _txds.Values;
    public IReadOnlyList<ColliderPlacement> Colliders => _colliders;

    public BoundingBox WorldBounds { get; private set; } = BoundingBox.Empty;

    public int CurrentInterior { get; set; }

    public ItemDefinition? FindDefinition(string modelName) => _definitionsByName.GetValueOrDefault(modelName);
    public ItemDefinition? FindDefinition(int id) => _definitionsById.GetValueOrDefault(id);
    public StreamedModel? FindModel(string dffFile) => _models.GetValueOrDefault(dffFile);
    public StreamedTextureDictionary? FindTextureDictionary(string name) => _txds.GetValueOrDefault(name);

    public static Level Load(string gameRoot, string datPath, AssetFormatRegistry formats, LevelOptions? options = null, IWorldLog? log = null)
    {
        options ??= new LevelOptions();
        log ??= NullWorldLog.Instance;

        var fullDat = Path.IsPathRooted(datPath) ? datPath : Path.Combine(gameRoot, datPath);
        log.Info($"Loading level '{fullDat}'");
        var manifest = XmlDocumentLoader.Load<LevelFile>(fullDat);

        var level = new Level(gameRoot, datPath, manifest, formats, options, log);
        level.OpenArchives();
        level.LoadItemDefinitions();
        level.LoadItemPlacements();
        level.ResolveLodPairs();
        level.LoadCollisionFiles();
        level.ComputeWorldBounds();
        log.Info($"Level ready: {level._archives.Count} archive(s), {level._definitions.Count} definition(s), {level._instances.Count} instance(s), {level._models.Count} unique model(s), {level._txds.Count} txd(s), {level._colliders.Count} collider(s)");
        return level;
    }

    public static Level LoadFromExeDirectory(string datPath, AssetFormatRegistry formats, LevelOptions? options = null, IWorldLog? log = null)
        => Load(AppContext.BaseDirectory, datPath, formats, options, log);

    private string Resolve(string relative) => Path.Combine(GameRoot, relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));

    private void OpenArchives()
    {
        foreach (var img in Manifest.ImgFiles)
        {
            var archive = DirectoryArchive.FromImgReference(GameRoot, img);
            if (!archive.Exists)
                Log.Warn($"Archive '{img}' → directory '{archive.Directory}' does not exist. Models from it will fail to stream.");
            else
                Log.Info($"Archive '{img}' → '{archive.Directory}' ({archive.EnumerateEntries().Count()} entries)");
            _archives.Add(archive);
        }
        if (_archives.Count == 0)
            Log.Warn("The .dat lists no <ImgFile>; nothing can be streamed.");
    }

    public void AddArchive(IAssetArchive archive) => _archives.Add(archive);

    private void LoadItemDefinitions()
    {
        foreach (var rel in Manifest.ItemDefinitions)
        {
            var path = Resolve(rel);
            if (!File.Exists(path))
            {
                if (Options.StrictManifest) throw new FileNotFoundException($"IDE listed in .dat not found: {rel}", path);
                Log.Warn($"IDE not found, skipping: {rel}");
                continue;
            }

            var ide = XmlDocumentLoader.Load<ItemDefinitionFile>(path);
            foreach (var obj in ide.Objects)
            {
                var def = new ItemDefinition(obj, rel) { IsLod = (obj.Flags & Options.LodFlagMask) != 0 };
                if (_definitionsByName.ContainsKey(def.ModelName))
                    Log.Warn($"Duplicate ModelName '{def.ModelName}' in {rel}; the later one wins for name lookups.");
                _definitions.Add(def);
                _definitionsByName[def.ModelName] = def;
                _definitionsById[def.Id] = def;

                var txd = string.IsNullOrWhiteSpace(def.TextureDictionary) ? null : GetOrCreateTxdSlot(def.TextureDictionary);
                if (!_models.TryGetValue(def.DffFile, out var model))
                {
                    model = new StreamedModel(def.DffFile, txd);
                    _models[def.DffFile] = model;
                }
                model._definitions.Add(def);
                model.MaxDrawDistance = MathF.Max(model.MaxDrawDistance, def.DrawDistance);
                def.Model = model;
            }
            Log.Info($"IDE '{rel}': {ide.Objects.Count} object(s), {ide.Objects.Count(o => (o.Flags & Options.LodFlagMask) != 0)} LOD-flagged");
        }
    }

    private StreamedTextureDictionary GetOrCreateTxdSlot(string name)
    {
        if (!_txds.TryGetValue(name, out var slot))
        {
            slot = new StreamedTextureDictionary(name);
            _txds[name] = slot;
        }
        return slot;
    }

    private void LoadItemPlacements()
    {
        foreach (var rel in Manifest.ItemPlacementLists)
        {
            var path = Resolve(rel);
            if (!File.Exists(path))
            {
                if (Options.StrictManifest) throw new FileNotFoundException($"IPL listed in .dat not found: {rel}", path);
                Log.Warn($"IPL not found, skipping: {rel}");
                continue;
            }

            var ipl = XmlDocumentLoader.Load<ItemPlacementListFile>(path);
            var placed = 0;
            var fileStart = _instances.Count;
            foreach (var inst in ipl.Instances)
            {
                var def = FindDefinition(inst.ModelName) ?? FindDefinition(inst.ID);
                if (def == null)
                {
                    Log.Warn($"IPL '{rel}': instance #{inst.ID} references unknown model '{inst.ModelName}'; skipped.");
                    continue;
                }
                var wi = new WorldInstance(inst, def, rel, _instances.Count);
                _instances.Add(wi);
                def._instances.Add(wi);
                RecordExplicitLod(wi, inst, fileStart);
                placed++;
            }
            Log.Info($"IPL '{rel}': {placed}/{ipl.Instances.Count} instance(s) placed");
        }
    }

    private void LoadCollisionFiles()
    {
        if (!Options.LoadCollision) return;
        foreach (var rel in Manifest.CollisionFiles)
        {
            var path = Resolve(rel);
            if (!File.Exists(path))
            {
                if (Options.StrictManifest) throw new FileNotFoundException($"COL listed in .dat not found: {rel}", path);
                Log.Warn($"COL not found, skipping: {rel}");
                continue;
            }

            var col = XmlDocumentLoader.Load<CollisionFile>(path);
            var loaded = 0;
            foreach (var xml in col.Colliders)
            {
                var placement = new ColliderPlacement(xml, rel, _colliders.Count);
                _colliders.Add(placement);
                if (Formats.CollisionFormats.Count == 0) continue;
                if (LoadCollider(placement)) loaded++;
            }
            Log.Info(Formats.CollisionFormats.Count == 0
                ? $"COL '{rel}': {col.Colliders.Count} collider(s) parsed, no ICollisionFormat registered so no meshes loaded"
                : $"COL '{rel}': {loaded}/{col.Colliders.Count} collider mesh(es) loaded");
        }
    }

    private bool LoadCollider(ColliderPlacement placement)
    {
        if (!Formats.TryResolve(Formats.CollisionFormats, _archives, placement.ModelName, out var format, out var entry))
        {
            placement.State = StreamingState.Failed;
            placement.Error = new FileNotFoundException($"No registered ICollisionFormat can find '{placement.ModelName}' in {_archives.Count} archive(s)");
            Log.Warn($"Collider '{placement.ModelName}' not found in any archive; skipped.");
            return false;
        }
        try
        {
            var ctx = new AssetLoadContext(this, placement.ModelName, null, CancellationToken.None);
            placement.Collision = format.Load(ctx, entry);
            placement.Format = format;
            placement.Entry = entry;
            placement.State = StreamingState.Resident;
            Log.Debug($"Collider '{placement.ModelName}' loaded via {format.Name}");
            return true;
        }
        catch (Exception ex)
        {
            placement.State = StreamingState.Failed;
            placement.Error = ex;
            Log.Error($"Collider '{placement.ModelName}' failed to load: {ex.Message}");
            return false;
        }
    }

    public event Action<ColliderPlacement>? ColliderLoaded;

    public void ReloadColliders()
    {
        foreach (var c in _colliders)
        {
            if (c.State == StreamingState.Resident) continue;
            if (LoadCollider(c)) ColliderLoaded?.Invoke(c);
        }
    }

    private void ComputeWorldBounds()
    {
        var b = BoundingBox.Empty;
        foreach (var i in _instances) b = b.Encapsulate(i.WorldBounds);
        WorldBounds = _instances.Count > 0 ? b : new BoundingBox(default, default);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ShutdownStreaming();
        foreach (var c in _colliders) { c.Collision?.Dispose(); c.Collision = null; }
        foreach (var a in _archives) a.Dispose();
    }
}

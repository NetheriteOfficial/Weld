using Weld.World.Assets;

namespace Weld.World.Streaming;

public enum StreamingState
{
    Unloaded,
    Queued,
    Loading,
    Resident,
    Failed,
}

public abstract class StreamedAsset
{
    protected StreamedAsset(string name) { Name = name; }

    public string Name { get; }
    public StreamingState State { get; internal set; } = StreamingState.Unloaded;
    public Exception? Error { get; internal set; }
    public IAssetFormat? Format { get; internal set; }
    public AssetEntry? Entry { get; internal set; }

    internal int LastWantedFrame = -1;
    internal double LastWantedTime = double.NegativeInfinity;
    internal double FailedAtTime;
    internal float RequestDistance = float.MaxValue;
    internal float LastRequestDistance = float.MaxValue;

    public double LastLoadSeconds { get; internal set; }

    public double ResidentSince { get; internal set; } = double.NaN;

    public long ResidentBytes { get; internal set; }

    public bool IsResident => State == StreamingState.Resident;

    public override string ToString() => $"{GetType().Name} {Name} [{State}]";
}

public sealed class StreamedTextureDictionary : StreamedAsset
{
    internal StreamedTextureDictionary(string name) : base(name) { }

    public ITextureDictionary? Dictionary { get; internal set; }

    public int ReferenceCount => _refCount;
    internal int _refCount;

    internal readonly object LoadGate = new();
    internal Task<ITextureDictionary>? LoadTask;
}

public sealed class StreamedModel : StreamedAsset
{
    internal StreamedModel(string name, StreamedTextureDictionary? txd) : base(name)
    {
        TextureDictionary = txd;
    }

    public IModel? Model { get; internal set; }
    public StreamedTextureDictionary? TextureDictionary { get; }
    public IReadOnlyList<ItemDefinition> Definitions => _definitions;
    internal readonly List<ItemDefinition> _definitions = new();
    public ItemDefinition PrimaryDefinition => _definitions[0];

    public float MaxDrawDistance { get; internal set; }

    internal CancellationTokenSource? Cancellation;
}

public enum LodPairingSource
{
    None,
    Explicit,
    Definition,
    NameConvention,
    Spatial,
}

public struct StreamingStats
{
    public int Frame;
    public int InstancesTotal;
    public int InstancesInRange;
    public int InstancesInFrustum;
    public int InstancesDrawable;
    public int InstancesWaitingForModel;
    public int ModelsResident;
    public int ModelsQueued;
    public int ModelsLoading;
    public int ModelsFailed;
    public int TextureDictionariesResident;
    public int LodsDrawn;
    public int InstancesFading;
    public long ResidentBytes;
    public int BudgetEvictions;
    public double UpdateMilliseconds;

    public override readonly string ToString() =>
        $"f{Frame} draw {InstancesDrawable}/{InstancesTotal} (range {InstancesInRange}, frustum {InstancesInFrustum}, waiting {InstancesWaitingForModel}) " +
        $"lod {LodsDrawn} fade {InstancesFading} models res {ModelsResident} q {ModelsQueued} ld {ModelsLoading} fail {ModelsFailed} txd {TextureDictionariesResident} " +
        $"mem {ResidentBytes / (1024.0 * 1024.0):F1}MB{(BudgetEvictions > 0 ? $" (-{BudgetEvictions})" : "")} {UpdateMilliseconds:F2}ms";
}

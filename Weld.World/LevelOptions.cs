namespace Weld.World;

public sealed class LevelOptions
{
    public float StreamInMargin { get; set; } = 50f;

    public float StreamOutHysteresis { get; set; } = 25f;

    public double UnloadDelaySeconds { get; set; } = 3.0;

    public double FailedRetrySeconds { get; set; } = 10.0;

    public int MaxConcurrentLoads { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    public int MaxCompletionsPerFrame { get; set; } = 2;

    public int MaxEvictionsPerFrame { get; set; } = 4;

    public (double Min, double Max) SimulatedLoadLatency { get; set; } = (0, 0);

    public long MemoryBudgetBytes { get; set; } = 0;

    public double BudgetProtectSeconds { get; set; } = 0.5;

    public double StreamInFadeSeconds { get; set; } = 0.35;

    public float LodFadeDistance { get; set; } = 20f;

    public uint LodFlagMask { get; set; } = 1;

    public bool AutoLodPairing { get; set; } = true;

    public float LodPairingRadius { get; set; } = 400f;

    public bool LodSpatialFallback { get; set; } = true;

    public bool SynchronousLoading { get; set; }

    public bool FrustumCulling { get; set; } = true;

    public bool FilterByInterior { get; set; } = true;

    public bool LoadCollision { get; set; } = true;

    public bool StrictManifest { get; set; }

    public static LevelOptions Modern() => new();

    public static LevelOptions Ps2Harsh() => new()
    {
        StreamInMargin = 0f,
        StreamOutHysteresis = 5f,
        UnloadDelaySeconds = 0.75,
        MaxConcurrentLoads = 1,
        MaxCompletionsPerFrame = 1,
        MaxEvictionsPerFrame = 8,
        SimulatedLoadLatency = (0.12, 0.55),
        MemoryBudgetBytes = 48L * 1024 * 1024,
        BudgetProtectSeconds = 0.25,
        StreamInFadeSeconds = 0.3,
        LodFadeDistance = 12f,
        FailedRetrySeconds = 5,
    };

    public void CopyFrom(LevelOptions other)
    {
        StreamInMargin = other.StreamInMargin;
        StreamOutHysteresis = other.StreamOutHysteresis;
        UnloadDelaySeconds = other.UnloadDelaySeconds;
        FailedRetrySeconds = other.FailedRetrySeconds;
        MaxConcurrentLoads = other.MaxConcurrentLoads;
        MaxCompletionsPerFrame = other.MaxCompletionsPerFrame;
        MaxEvictionsPerFrame = other.MaxEvictionsPerFrame;
        SimulatedLoadLatency = other.SimulatedLoadLatency;
        MemoryBudgetBytes = other.MemoryBudgetBytes;
        BudgetProtectSeconds = other.BudgetProtectSeconds;
        StreamInFadeSeconds = other.StreamInFadeSeconds;
        LodFadeDistance = other.LodFadeDistance;
        LodFlagMask = other.LodFlagMask;
        AutoLodPairing = other.AutoLodPairing;
        LodPairingRadius = other.LodPairingRadius;
        LodSpatialFallback = other.LodSpatialFallback;
        SynchronousLoading = other.SynchronousLoading;
        FrustumCulling = other.FrustumCulling;
        FilterByInterior = other.FilterByInterior;
        LoadCollision = other.LoadCollision;
        StrictManifest = other.StrictManifest;
    }
}

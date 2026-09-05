using System.Collections.Concurrent;
using System.Diagnostics;
using Weld.World.Assets;
using Weld.World.Streaming;

namespace Weld.World;

public sealed partial class Level
{
    public event Action<StreamedTextureDictionary>? TextureDictionaryResident;
    public event Action<StreamedTextureDictionary>? TextureDictionaryEvicted;
    public event Action<StreamedModel>? ModelResident;
    public event Action<StreamedModel>? ModelEvicted;
    public event Action<StreamedModel, Exception>? ModelLoadFailed;

    private readonly List<WorldInstance> _visible = new();
    private readonly PriorityQueue<StreamedModel, float> _loadQueue = new();
    private readonly HashSet<StreamedModel> _inFlight = new();
    private readonly ConcurrentQueue<LoadResult> _completed = new();
    private readonly List<StreamedModel> _budgetScratch = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _latencyRng = new(1234);
    private SemaphoreSlim _loadSlots = null!;
    private CancellationTokenSource _shutdown = null!;
    private int _frame;
    private StreamingStats _stats;
    private StreamingCameraData _lastCamera = StreamingCameraData.Default;
    private Frustum _lastFrustum;
    private long _residentBytes;

    public IReadOnlyList<WorldInstance> VisibleInstances => _visible;
    public StreamingStats Stats => _stats;
    public int Frame => _frame;
    public double Time => _clock.Elapsed.TotalSeconds;
    public StreamingCameraData LastCamera => _lastCamera;
    public Frustum LastFrustum => _lastFrustum;
    public long ResidentBytes => _residentBytes;

    private readonly record struct LoadResult(StreamedModel Model, IModel? Payload, IModelFormat? Format, AssetEntry? Entry,
        StreamedTextureDictionary? Txd, ITextureDictionary? TxdPayload, ITextureDictionaryFormat? TxdFormat, AssetEntry? TxdEntry,
        Exception? Error, double Seconds);

    private void InitializeStreaming()
    {
        _loadSlots = new SemaphoreSlim(Math.Max(1, Options.MaxConcurrentLoads));
        _shutdown = new CancellationTokenSource();
    }

    private void ShutdownStreaming()
    {
        _shutdown.Cancel();
        for (var i = 0; i < 200 && _inFlight.Count > 0; i++)
        {
            DrainCompletions(int.MaxValue, announce: false);
            Thread.Sleep(5);
        }
        foreach (var m in _models.Values) EvictModel(m, announce: true);
        foreach (var t in _txds.Values) EvictTxd(t, announce: true);
        _loadSlots.Dispose();
    }

    public void Update(in StreamingCameraData camera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var sw = Stopwatch.StartNew();
        _frame++;
        _lastCamera = camera;
        var now = Time;
        var frustum = Options.FrustumCulling ? camera.BuildFrustum() : default;
        _lastFrustum = frustum;

        var stats = new StreamingStats { Frame = _frame, InstancesTotal = _instances.Count };
        _visible.Clear();

        DrainCompletions(Options.MaxCompletionsPerFrame, announce: true);

        foreach (var inst in _instances)
        {
            var def = inst.Definition;
            var model = def.Model;

            if (Options.FilterByInterior && inst.Interior != 0 && inst.Interior != CurrentInterior)
            {
                inst.DistanceToCamera = float.MaxValue;
                inst.IsInRange = inst.IsInFrustum = inst.IsDrawable = false;
                inst.Opacity = 0;
                continue;
            }

            var dist = inst.WorldBounds.Distance(camera.Position);
            var drawDist = camera.EffectiveDrawDistance(def.DrawDistance);
            inst.DistanceToCamera = dist;
            inst.IsInRange = dist <= drawDist;

            if (dist <= drawDist + Options.StreamInMargin)
            {
                model.LastWantedFrame = _frame;
                model.LastWantedTime = now;
                if (model.TextureDictionary != null) model.TextureDictionary.LastWantedTime = now;
                if (dist < model.RequestDistance) model.RequestDistance = dist;
            }

            inst.IsDrawable = false;
            if (!inst.IsInRange)
            {
                inst.IsInFrustum = false;
                inst.Opacity = 0;
                continue;
            }
            stats.InstancesInRange++;

            inst.IsInFrustum = !Options.FrustumCulling || frustum.Intersects(inst.WorldBounds);
            if (!inst.IsInFrustum) { inst.Opacity = 0; continue; }
            stats.InstancesInFrustum++;
            inst.LastVisibleFrame = _frame;
        }

        var evictions = 0;
        foreach (var model in _models.Values)
        {
            var wanted = model.LastWantedFrame == _frame;
            switch (model.State)
            {
                case StreamingState.Unloaded when wanted:
                    model.State = StreamingState.Queued;
                    _loadQueue.Enqueue(model, model.RequestDistance);
                    break;

                case StreamingState.Failed when wanted && now - model.FailedAtTime >= Options.FailedRetrySeconds:
                    model.State = StreamingState.Queued;
                    _loadQueue.Enqueue(model, model.RequestDistance);
                    break;

                case StreamingState.Resident when !wanted:
                    if (evictions < Options.MaxEvictionsPerFrame &&
                        now - model.LastWantedTime >= Options.UnloadDelaySeconds &&
                        !AnyInstanceWithin(model, camera, Options.StreamInMargin + Options.StreamOutHysteresis))
                    {
                        EvictModel(model, announce: true);
                        evictions++;
                    }
                    break;
            }
            model.LastRequestDistance = model.RequestDistance;
            model.RequestDistance = float.MaxValue;
        }

        PumpLoadQueue();

        stats.BudgetEvictions = EnforceBudget(now);

        foreach (var txd in _txds.Values)
        {
            if (txd.State == StreamingState.Resident && txd._refCount <= 0 && now - txd.LastWantedTime >= Options.UnloadDelaySeconds)
                EvictTxd(txd, announce: true);
        }

        BuildDrawList(camera, now, ref stats);

        foreach (var m in _models.Values)
        {
            switch (m.State)
            {
                case StreamingState.Resident: stats.ModelsResident++; break;
                case StreamingState.Queued: stats.ModelsQueued++; break;
                case StreamingState.Loading: stats.ModelsLoading++; break;
                case StreamingState.Failed: stats.ModelsFailed++; break;
            }
        }
        foreach (var t in _txds.Values) if (t.State == StreamingState.Resident) stats.TextureDictionariesResident++;
        stats.ResidentBytes = _residentBytes;
        stats.UpdateMilliseconds = sw.Elapsed.TotalMilliseconds;
        _stats = stats;
    }

    private void BuildDrawList(in StreamingCameraData camera, double now, ref StreamingStats stats)
    {
        var fadeBand = MathF.Max(Options.LodFadeDistance, 0.001f);

        foreach (var inst in _instances)
        {
            if (inst.IsLod) continue;
            var model = inst.Definition.Model;
            var resident = model.State == StreamingState.Resident;

            if (!inst.IsVisible || !resident)
            {
                inst.LodBlend = 0;
                inst.StreamFade = 0;
                inst.Opacity = 0;
                inst.DitherInverted = false;
                if (inst.IsVisible && !resident) stats.InstancesWaitingForModel++;
                continue;
            }

            var drawDist = camera.EffectiveDrawDistance(inst.Definition.DrawDistance);
            var blend = inst.LodParent != null
                ? Math.Clamp((drawDist - inst.DistanceToCamera) / fadeBand, 0f, 1f)
                : 1f;
            inst.LodBlend = blend;
            inst.StreamFade = StreamFadeOf(model, now);
            inst.Opacity = blend * inst.StreamFade;
            inst.DitherInverted = false;

            if (inst.Opacity > 0f)
            {
                inst.IsDrawable = true;
                _visible.Add(inst);
                stats.InstancesDrawable++;
                if (inst.Opacity < 1f) stats.InstancesFading++;
            }
        }

        foreach (var inst in _instances)
        {
            if (!inst.IsLod) continue;
            var model = inst.Definition.Model;
            var resident = model.State == StreamingState.Resident;
            inst.DitherInverted = true;

            if (!inst.IsVisible || !resident)
            {
                inst.LodBlend = 0;
                inst.StreamFade = 0;
                inst.Opacity = 0;
                if (inst.IsVisible && !resident) stats.InstancesWaitingForModel++;
                continue;
            }

            var minCoverage = 1f;
            if (inst._lodChildren.Count == 0)
            {
                minCoverage = 0f;
            }
            else
            {
                foreach (var child in inst._lodChildren)
                {
                    var c = child.IsInRange && child.Definition.Model.State == StreamingState.Resident ? child.LodBlend * child.StreamFade : 0f;
                    if (c < minCoverage) minCoverage = c;
                    if (minCoverage <= 0f) break;
                }
            }

            inst.LodBlend = 1f - minCoverage;
            inst.StreamFade = StreamFadeOf(model, now);
            inst.Opacity = inst.LodBlend * inst.StreamFade;

            if (inst.Opacity > 0f)
            {
                inst.IsDrawable = true;
                _visible.Add(inst);
                stats.InstancesDrawable++;
                stats.LodsDrawn++;
                if (inst.Opacity < 1f) stats.InstancesFading++;
            }
        }
    }

    private float StreamFadeOf(StreamedModel model, double now)
    {
        if (Options.StreamInFadeSeconds <= 0 || double.IsNaN(model.ResidentSince)) return 1f;
        return (float)Math.Clamp((now - model.ResidentSince) / Options.StreamInFadeSeconds, 0.0, 1.0);
    }

    private static bool AnyInstanceWithin(StreamedModel model, in StreamingCameraData cam, float margin)
    {
        foreach (var def in model._definitions)
        {
            var dd = cam.EffectiveDrawDistance(def.DrawDistance) + margin;
            var dd2 = dd * dd;
            foreach (var inst in def._instances)
                if (inst.WorldBounds.DistanceSquared(cam.Position) <= dd2) return true;
        }
        return false;
    }

    private int EnforceBudget(double now)
    {
        var budget = Options.MemoryBudgetBytes;
        if (budget <= 0 || _residentBytes <= budget) return 0;

        _budgetScratch.Clear();
        foreach (var m in _models.Values)
            if (m.State == StreamingState.Resident) _budgetScratch.Add(m);

        var frame = _frame;
        _budgetScratch.Sort((a, b) =>
        {
            var aw = a.LastWantedFrame == frame;
            var bw = b.LastWantedFrame == frame;
            if (aw != bw) return aw ? 1 : -1;
            if (!aw) return a.LastWantedTime.CompareTo(b.LastWantedTime);
            return b.LastRequestDistance.CompareTo(a.LastRequestDistance);
        });

        var evicted = 0;
        foreach (var m in _budgetScratch)
        {
            if (_residentBytes <= budget) break;
            var protectedRecent = m.LastWantedFrame == frame && now - m.LastWantedTime < Options.BudgetProtectSeconds && IsAnyInstanceDrawn(m);
            if (protectedRecent) continue;
            EvictModel(m, announce: true);
            evicted++;
        }
        if (evicted > 0) Log.Debug($"Budget: evicted {evicted} model(s); resident {_residentBytes / 1024} KB / {budget / 1024} KB");
        return evicted;
    }

    private static bool IsAnyInstanceDrawn(StreamedModel m)
    {
        foreach (var d in m._definitions)
            foreach (var i in d._instances)
                if (i.IsVisible) return true;
        return false;
    }

    public void PreloadAll()
    {
        foreach (var model in _models.Values)
        {
            if (model.State is StreamingState.Resident or StreamingState.Loading) continue;
            model.State = StreamingState.Loading;
            _inFlight.Add(model);
            if (model.TextureDictionary != null) model.TextureDictionary._refCount++;
            _completed.Enqueue(ExecuteLoad(model, CancellationToken.None, simulateLatency: false));
        }
        DrainCompletions(int.MaxValue, announce: true);
    }

    private void PumpLoadQueue()
    {
        while (_loadQueue.Count > 0)
        {
            if (!Options.SynchronousLoading && _inFlight.Count >= Options.MaxConcurrentLoads) break;

            var model = _loadQueue.Dequeue();
            if (model.State != StreamingState.Queued) continue;
            if (model.LastWantedFrame != _frame)
            {
                model.State = StreamingState.Unloaded;
                continue;
            }

            model.State = StreamingState.Loading;
            _inFlight.Add(model);
            if (model.TextureDictionary != null) { model.TextureDictionary._refCount++; model.TextureDictionary.LastWantedTime = Time; }

            if (Options.SynchronousLoading)
            {
                _completed.Enqueue(ExecuteLoad(model, _shutdown.Token, simulateLatency: false));
                continue;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            model.Cancellation = cts;
            var token = cts.Token;
            _ = Task.Run(async () =>
            {
                await _loadSlots.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try { _completed.Enqueue(ExecuteLoad(model, token, simulateLatency: true)); }
                finally { _loadSlots.Release(); }
            });
        }
        if (Options.SynchronousLoading)
            DrainCompletions(int.MaxValue, announce: true);
    }

    private LoadResult ExecuteLoad(StreamedModel model, CancellationToken token, bool simulateLatency)
    {
        var sw = Stopwatch.StartNew();
        var def = model.PrimaryDefinition;
        var txd = model.TextureDictionary;
        ITextureDictionary? txdPayload = null;
        ITextureDictionaryFormat? txdFormat = null;
        AssetEntry? txdEntry = null;

        try
        {
            token.ThrowIfCancellationRequested();

            if (simulateLatency)
            {
                var (min, max) = Options.SimulatedLoadLatency;
                if (max > 0)
                {
                    double t;
                    lock (_latencyRng) t = min + _latencyRng.NextDouble() * Math.Max(0, max - min);
                    if (t > 0) token.WaitHandle.WaitOne(TimeSpan.FromSeconds(t));
                    token.ThrowIfCancellationRequested();
                }
            }

            if (txd != null && txd.State != StreamingState.Resident)
            {
                Task<ITextureDictionary> task;
                lock (txd.LoadGate)
                {
                    task = txd.LoadTask ??= Task.Run(() =>
                    {
                        if (!Formats.TryResolveTextureDictionary(_archives, txd.Name, out var f, out var e))
                            throw new FileNotFoundException($"No registered ITextureDictionaryFormat can find TXD '{txd.Name}' in {_archives.Count} archive(s) " +
                                                            $"(tried: {string.Join(", ", Formats.TextureDictionaryFormats.SelectMany(x => x.ResolveEntryNames(txd.Name)))})");
                        var ctx = new AssetLoadContext(this, txd.Name, def, token);
                        var payload = f.Load(ctx, e);
                        txd.Format = f;
                        txd.Entry = e;
                        return payload;
                    }, token);
                }
                txdPayload = task.GetAwaiter().GetResult();
                txdFormat = txd.Format as ITextureDictionaryFormat;
                txdEntry = txd.Entry;
            }

            if (!Formats.TryResolveModel(_archives, model.Name, out var format, out var entry))
                throw new FileNotFoundException($"No registered IModelFormat can find model '{model.Name}' in {_archives.Count} archive(s) " +
                                                $"(tried: {string.Join(", ", Formats.ModelFormats.SelectMany(x => x.ResolveEntryNames(model.Name)))})");

            var context = new AssetLoadContext(this, model.Name, def, token);
            var payload = format.Load(context, entry);
            return new LoadResult(model, payload, format, entry, txd, txdPayload, txdFormat, txdEntry, null, sw.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            return new LoadResult(model, null, null, null, txd, txdPayload, txdFormat, txdEntry, ex, sw.Elapsed.TotalSeconds);
        }
    }

    private void DrainCompletions(int max, bool announce)
    {
        var n = 0;
        while (n < max && _completed.TryDequeue(out var r))
        {
            n++;
            var model = r.Model;
            _inFlight.Remove(model);
            model.Cancellation?.Dispose();
            model.Cancellation = null;

            if (r.Txd != null && r.Txd.State != StreamingState.Resident)
            {
                if (r.TxdPayload != null)
                {
                    r.Txd.Dictionary = r.TxdPayload;
                    r.Txd.Format = r.TxdFormat;
                    r.Txd.Entry = r.TxdEntry;
                    r.Txd.State = StreamingState.Resident;
                    r.Txd.Error = null;
                    r.Txd.ResidentSince = Time;
                    r.Txd.ResidentBytes = Math.Max(0, r.TxdPayload.MemoryFootprint);
                    _residentBytes += r.Txd.ResidentBytes;
                    lock (r.Txd.LoadGate) r.Txd.LoadTask = null;
                    Log.Debug($"TXD '{r.Txd.Name}' resident via {r.TxdFormat?.Name}");
                    if (announce) TextureDictionaryResident?.Invoke(r.Txd);
                }
                else if (r.Error != null)
                {
                    r.Txd.State = StreamingState.Failed;
                    r.Txd.Error = r.Error;
                    lock (r.Txd.LoadGate) r.Txd.LoadTask = null;
                }
            }

            if (r.Error != null || r.Payload == null)
            {
                if (r.Txd != null) r.Txd._refCount--;
                model.Error = r.Error;
                model.FailedAtTime = Time;
                if (_disposed || r.Error is OperationCanceledException)
                {
                    model.State = StreamingState.Unloaded;
                    r.Payload?.Dispose();
                    continue;
                }
                model.State = StreamingState.Failed;
                Log.Error($"Model '{model.Name}' failed to load: {r.Error?.Message}");
                if (announce) ModelLoadFailed?.Invoke(model, r.Error ?? new InvalidOperationException("format returned null"));
                continue;
            }

            if (_disposed)
            {
                r.Payload.Dispose();
                if (r.Txd != null) r.Txd._refCount--;
                model.State = StreamingState.Unloaded;
                continue;
            }

            model.Model = r.Payload;
            model.Format = r.Format;
            model.Entry = r.Entry;
            model.Error = null;
            model.LastLoadSeconds = r.Seconds;
            model.ResidentSince = Time;
            model.ResidentBytes = Math.Max(0, r.Payload.MemoryFootprint);
            _residentBytes += model.ResidentBytes;
            model.State = StreamingState.Resident;
            Log.Debug($"Model '{model.Name}' resident via {r.Format?.Name} in {r.Seconds * 1000:F1} ms ({model.ResidentBytes / 1024} KB)");
            if (announce) ModelResident?.Invoke(model);
        }
    }

    private void EvictModel(StreamedModel model, bool announce)
    {
        if (model.State == StreamingState.Loading)
        {
            model.Cancellation?.Cancel();
            return;
        }
        if (model.State == StreamingState.Queued)
        {
            model.State = StreamingState.Unloaded;
            return;
        }
        if (model.State != StreamingState.Resident) { model.State = StreamingState.Unloaded; return; }

        if (announce) ModelEvicted?.Invoke(model);
        model.Model?.Dispose();
        model.Model = null;
        model.Format = null;
        model.Entry = null;
        model.State = StreamingState.Unloaded;
        model.ResidentSince = double.NaN;
        _residentBytes -= model.ResidentBytes;
        model.ResidentBytes = 0;
        if (model.TextureDictionary != null) model.TextureDictionary._refCount--;
        Log.Debug($"Model '{model.Name}' evicted");
    }

    private void EvictTxd(StreamedTextureDictionary txd, bool announce)
    {
        if (txd.State != StreamingState.Resident) return;
        if (announce) TextureDictionaryEvicted?.Invoke(txd);
        txd.Dictionary?.Dispose();
        txd.Dictionary = null;
        txd.Format = null;
        txd.Entry = null;
        txd.State = StreamingState.Unloaded;
        txd.ResidentSince = double.NaN;
        _residentBytes -= txd.ResidentBytes;
        txd.ResidentBytes = 0;
        Log.Debug($"TXD '{txd.Name}' evicted");
    }
}

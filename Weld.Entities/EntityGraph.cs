using System.Diagnostics;

namespace Weld.Entities;

public sealed class EntityGraph
{
    public const int TickRate = 60;
    public const float FixedDeltaConst = 1f / TickRate;

    private readonly GameWorld _world;
    private readonly List<BaseEntity> _entities = new();
    private readonly List<BaseEntity> _pendingAdd = new();
    private readonly List<BaseEntity> _pendingRemove = new();
    private readonly Dictionary<int, BaseEntity> _byId = new();
    private readonly Dictionary<uint, NetworkedEntity> _byNetId = new();
    private readonly List<NetworkedEntity> _networked = new();
    private int _nextId = 1;
    private uint _nextNetId = 1;
    private double _accumulator;
    private bool _ticking;

    internal EntityGraph(GameWorld world)
    {
        _world = world;
    }

    public float FixedDelta => FixedDeltaConst;
    public ulong Tick { get; private set; }
    public double Time => Tick * (double)FixedDeltaConst;
    public float Alpha => (float)Math.Clamp(_accumulator / FixedDeltaConst, 0, 1);
    public int MaxCatchUpTicks { get; set; } = 6;
    public int Count => _entities.Count;
    public double LastStepMilliseconds { get; private set; }

    public IReadOnlyList<BaseEntity> Entities => _entities;
    public IReadOnlyList<NetworkedEntity> Networked => _networked;

    public event Action<BaseEntity>? Spawned;
    public event Action<BaseEntity>? Removed;
    public event Action<ulong>? TickBegan;
    public event Action<ulong>? TickEnded;

    public T Add<T>(T entity, uint networkId = 0, ushort ownerClientId = 0) where T : BaseEntity
    {
        if (entity.World != null && entity.World != _world) throw new InvalidOperationException("entity already belongs to another world");
        entity.World = _world;
        entity.Id = _nextId++;
        if (entity is NetworkedEntity n)
        {
            n.NetworkId = networkId != 0 ? networkId : _nextNetId++;
            if (networkId >= _nextNetId) _nextNetId = networkId + 1;
            n.OwnerClientId = ownerClientId;
        }
        entity.PreviousPosition = entity.Position;
        entity.PreviousYaw = entity.Yaw;
        if (_ticking) _pendingAdd.Add(entity);
        else Insert(entity);
        return entity;
    }

    private void Insert(BaseEntity entity)
    {
        _entities.Add(entity);
        _byId[entity.Id] = entity;
        if (entity is NetworkedEntity n)
        {
            _byNetId[n.NetworkId] = n;
            _networked.Add(n);
        }
        entity.SpawnTick = Tick;
        entity.IsSpawned = true;
        entity.OnSpawn();
        Spawned?.Invoke(entity);
    }

    internal void Remove(BaseEntity entity)
    {
        if (_ticking) _pendingRemove.Add(entity);
        else Erase(entity);
    }

    private void Erase(BaseEntity entity)
    {
        if (!_entities.Remove(entity)) { _pendingAdd.Remove(entity); return; }
        _byId.Remove(entity.Id);
        if (entity is NetworkedEntity n)
        {
            _byNetId.Remove(n.NetworkId);
            _networked.Remove(n);
        }
        entity.IsRemoved = true;
        entity.IsSpawned = false;
        entity.OnRemove();
        Removed?.Invoke(entity);
    }

    public BaseEntity? Find(int id) => _byId.GetValueOrDefault(id);
    public NetworkedEntity? FindNetworked(uint networkId) => _byNetId.GetValueOrDefault(networkId);
    public IEnumerable<T> OfType<T>() where T : BaseEntity => _entities.OfType<T>();

    public int Advance(double realDeltaSeconds)
    {
        _accumulator += Math.Max(0, realDeltaSeconds);
        var maxAcc = FixedDeltaConst * MaxCatchUpTicks;
        if (_accumulator > maxAcc) _accumulator = maxAcc;
        var steps = 0;
        while (_accumulator >= FixedDeltaConst)
        {
            Step();
            _accumulator -= FixedDeltaConst;
            steps++;
        }
        return steps;
    }

    public void Step()
    {
        var sw = Stopwatch.StartNew();
        _ticking = true;
        TickBegan?.Invoke(Tick);
        Flush();
        foreach (var e in _entities)
        {
            e.CapturePrevious();
        }
        foreach (var e in _entities)
        {
            if (e.IsRemoved) continue;
            e.TicksExisted++;
            e.Tick();
        }
        _world.Physics?.Step(FixedDeltaConst);
        foreach (var e in _entities)
        {
            if (e.IsRemoved) continue;
            e.PostPhysicsTick();
        }
        _ticking = false;
        Flush();
        Tick++;
        TickEnded?.Invoke(Tick);
        LastStepMilliseconds = sw.Elapsed.TotalMilliseconds;
    }

    private void Flush()
    {
        if (_pendingRemove.Count > 0)
        {
            var list = _pendingRemove.ToArray();
            _pendingRemove.Clear();
            foreach (var e in list) Erase(e);
        }
        if (_pendingAdd.Count > 0)
        {
            var list = _pendingAdd.ToArray();
            _pendingAdd.Clear();
            foreach (var e in list) if (!e.IsRemoved) Insert(e);
        }
    }

    public void Clear()
    {
        foreach (var e in _entities.ToArray()) Erase(e);
        _pendingAdd.Clear();
        _pendingRemove.Clear();
    }
}

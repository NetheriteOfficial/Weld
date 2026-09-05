using System.Numerics;
using Weld.Physics;
using Weld.World;

namespace Weld.Entities;

public enum NetRole
{
    Host,
    DedicatedServer,
    Client,
}

public sealed class GameWorld : IDisposable
{
    private bool _disposed;

    public GameWorld(Level level, NetRole role, IPhysicsWorld? physics, EntityTypeRegistry? types = null, IWorldLog? log = null)
    {
        Level = level;
        Role = role;
        Types = types ?? EntityTypeRegistry.CreateDefault();
        Log = log ?? level.Log;
        Physics = physics;
        Physics?.AddLevelColliders(level);
        Entities = new EntityGraph(this);
    }

    public Level Level { get; }
    public NetRole Role { get; }
    public EntityTypeRegistry Types { get; }
    public IWorldLog Log { get; }
    public IPhysicsWorld? Physics { get; }
    public EntityGraph Entities { get; }

    public bool IsServer => Role != NetRole.Client;
    public bool IsClient => Role != NetRole.DedicatedServer;
    public ushort LocalClientId { get; set; }
    public PlayerEntity? LocalPlayer { get; set; }
    public bool RenderLocalPlayer { get; set; }

    public int Advance(double realDelta) => Entities.Advance(realDelta);

    public T Spawn<T>(T entity, Vector3 position, ushort ownerClientId = 0, uint networkId = 0) where T : BaseEntity
    {
        entity.Position = position;
        return Entities.Add(entity, networkId, ownerClientId);
    }

    public PlayerEntity SpawnPlayer(ushort clientId, string name, Vector3? position = null)
    {
        var p = new PlayerEntity { Name = name, Yaw = MathF.PI / 2f };
        var pos = position ?? FindSpawnPoint();
        Spawn(p, pos, clientId);
        if (clientId == LocalClientId && clientId != 0) LocalPlayer = p;
        return p;
    }

    public Vector3 FindSpawnPoint()
    {
        var center = Level.WorldBounds.Center;
        var top = new Vector3(center.X, center.Y, Level.WorldBounds.Max.Z + 50f);
        if (Physics != null && Physics.RayCast(top, -Vector3.UnitZ, 500f, out var hit))
            return hit.Point + new Vector3(0, 0, 1.2f);
        return new Vector3(center.X, center.Y, Level.WorldBounds.Max.Z + 2f);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Entities.Clear();
        Physics?.Dispose();
    }
}

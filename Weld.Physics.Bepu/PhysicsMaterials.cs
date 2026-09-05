using BepuPhysics;
using BepuPhysics.Collidables;
using Weld.World.Collision;

namespace Weld.Physics.Bepu;

public sealed class PhysicsMaterials
{
    private readonly Dictionary<int, StaticSurfaceInfo> _statics = new();
    private readonly Dictionary<int, float> _bodyFriction = new();

    public float DefaultFriction { get; set; } = 1f;

    public void RegisterStatic(StaticHandle handle, TriangleCollisionMesh mesh, ColliderPlacementRef owner)
    {
        _statics[handle.Value] = new StaticSurfaceInfo(mesh, owner, MathF.Sqrt(SurfaceProperties.Of(mesh.DominantSurface).Friction));
    }

    public void UnregisterStatic(StaticHandle handle) => _statics.Remove(handle.Value);

    public void SetBodyFriction(BodyHandle handle, float friction) => _bodyFriction[handle.Value] = MathF.Sqrt(friction);

    public void RemoveBody(BodyHandle handle) => _bodyFriction.Remove(handle.Value);

    public float FrictionFor(CollidableReference c)
    {
        if (c.Mobility == CollidableMobility.Static)
            return _statics.TryGetValue(c.StaticHandle.Value, out var s) ? s.Friction : MathF.Sqrt(DefaultFriction);
        return _bodyFriction.TryGetValue(c.BodyHandle.Value, out var f) ? f : MathF.Sqrt(DefaultFriction);
    }

    public bool TryGetStatic(StaticHandle handle, out StaticSurfaceInfo info) => _statics.TryGetValue(handle.Value, out info);

    public SurfaceType SurfaceAt(CollidableReference c, int childIndex)
    {
        if (c.Mobility != CollidableMobility.Static) return SurfaceType.Default;
        if (!_statics.TryGetValue(c.StaticHandle.Value, out var s)) return SurfaceType.Default;
        var tris = s.Mesh.TriangleSurfaces;
        return childIndex >= 0 && childIndex < tris.Length ? tris[childIndex] : s.Mesh.DominantSurface;
    }
}

public readonly record struct StaticSurfaceInfo(TriangleCollisionMesh Mesh, ColliderPlacementRef Owner, float Friction);

public readonly record struct ColliderPlacementRef(int Index, string ModelName);

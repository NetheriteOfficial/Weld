using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Trees;
using BepuUtilities.Memory;
using Weld.World;
using Weld.World.Collision;

namespace Weld.Physics.Bepu;

public sealed class BepuPhysicsWorld : IPhysicsWorld
{
    private readonly Dictionary<int, StaticHandle> _colliderStatics = new();
    private readonly List<TypedIndex> _shapes = new();
    private readonly Dictionary<int, BepuBody> _bodies = new();
    private bool _disposed;

    public BepuPhysicsWorld(Vector3? gravity = null)
    {
        Gravity = gravity ?? new Vector3(0, 0, -9.81f);
        BufferPool = new BufferPool();
        Materials = new PhysicsMaterials();
        Simulation = Simulation.Create(BufferPool,
            new WeldNarrowPhaseCallbacks(Materials),
            new WeldPoseIntegratorCallbacks(Gravity),
            new SolveDescription(8, 1));
    }

    public Simulation Simulation { get; }
    public BufferPool BufferPool { get; }
    public PhysicsMaterials Materials { get; }
    public Vector3 Gravity { get; }
    public int StaticCount => _colliderStatics.Count;
    public int BodyCount => _bodies.Count;
    public IWorldLog Log { get; set; } = NullWorldLog.Instance;

    public void Step(float dt)
    {
        Simulation.Timestep(dt);
    }

    public int AddLevelColliders(Level level)
    {
        var added = 0;
        foreach (var c in level.Colliders)
            if (AddCollider(c)) added++;
        level.ColliderLoaded += c => AddCollider(c);
        Log.Info($"Physics: {added} static collider(s) from '{level.DatPath}'");
        return added;
    }

    public bool AddCollider(ColliderPlacement placement)
    {
        if (placement.Mesh is not { } mesh || mesh.TriangleCount == 0) return false;
        if (_colliderStatics.ContainsKey(placement.Index)) return false;

        var scaleRot = WorldTransform.Compose(Vector3.Zero, placement.RotationDegrees, placement.Scale);
        BufferPool.Take<Triangle>(mesh.TriangleCount, out var triangles);
        for (var i = 0; i < mesh.TriangleCount; i++)
        {
            mesh.GetTriangle(i, out var a, out var b, out var c);
            bool flipWinding = scaleRot.GetDeterminant() < 0;

            var ta = Vector3.Transform(a, scaleRot);
            var tb = Vector3.Transform(b, scaleRot);
            var tc = Vector3.Transform(c, scaleRot);

            triangles[i] = !flipWinding
                ? new Triangle(ta, tc, tb)
                : new Triangle(ta, tb, tc);
        }
        var shape = new Mesh(triangles, Vector3.One, BufferPool);
        var shapeIndex = Simulation.Shapes.Add(shape);
        _shapes.Add(shapeIndex);
        var handle = Simulation.Statics.Add(new StaticDescription(placement.Position, Quaternion.Identity, shapeIndex));
        _colliderStatics[placement.Index] = handle;
        Materials.RegisterStatic(handle, mesh, new ColliderPlacementRef(placement.Index, placement.ModelName));
        return true;
    }

    public IPhysicsBody AddDynamicBox(Vector3 position, Quaternion orientation, Vector3 size, float mass, float friction = 0.8f)
    {
        var box = new Box(size.X, size.Y, size.Z);
        var inertia = box.ComputeInertia(mass);
        var shape = Simulation.Shapes.Add(box);
        _shapes.Add(shape);
        var handle = Simulation.Bodies.Add(BodyDescription.CreateDynamic(new RigidPose(position, orientation), inertia, shape, new BodyActivityDescription(0.01f)));
        Materials.SetBodyFriction(handle, friction);
        var body = new BepuBody(this, handle, mass);
        _bodies[handle.Value] = body;
        return body;
    }

    public IPhysicsBody AddCharacterCapsule(Vector3 position, float radius, float height, float mass)
    {
        var capsule = new Capsule(radius, MathF.Max(0.01f, height - 2 * radius));
        var inertia = capsule.ComputeInertia(mass);
        inertia.InverseInertiaTensor = default;
        var shape = Simulation.Shapes.Add(capsule);
        _shapes.Add(shape);
        var orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        var handle = Simulation.Bodies.Add(BodyDescription.CreateDynamic(new RigidPose(position, orientation), inertia, shape, new BodyActivityDescription(-1f)));
        Materials.SetBodyFriction(handle, 0f);
        var body = new BepuBody(this, handle, mass);
        _bodies[handle.Value] = body;
        return body;
    }

    internal void RemoveBody(BepuBody body)
    {
        _bodies.Remove(body.Handle.Value);
        if (!Simulation.Bodies.BodyExists(body.Handle)) return;
        Materials.RemoveBody(body.Handle);
        Simulation.Bodies.Remove(body.Handle);
    }

    public bool RayCast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit, IPhysicsBody? ignore = null)
    {
        var handler = new ClosestRayHitHandler(ignore is BepuBody b ? b.Handle : null);
        Simulation.RayCast(origin, direction, maxDistance, ref handler);
        if (!handler.Hit)
        {
            hit = default;
            return false;
        }
        var surface = Materials.SurfaceAt(handler.Collidable, handler.ChildIndex);
        BepuBody? hitBody = null;
        if (handler.Collidable.Mobility != CollidableMobility.Static) _bodies.TryGetValue(handler.Collidable.BodyHandle.Value, out hitBody);
        hit = new RayHit(origin + direction * handler.T, handler.Normal, handler.T, surface, hitBody);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Simulation.Dispose();
        BufferPool.Clear();
    }

    private struct ClosestRayHitHandler : IRayHitHandler
    {
        private readonly bool _hasIgnore;
        private readonly BodyHandle _ignore;
        public bool Hit;
        public float T;
        public Vector3 Normal;
        public CollidableReference Collidable;
        public int ChildIndex;

        public ClosestRayHitHandler(BodyHandle? ignore)
        {
            _hasIgnore = ignore.HasValue;
            _ignore = ignore ?? default;
            Hit = false;
            T = float.MaxValue;
            Normal = default;
            Collidable = default;
            ChildIndex = -1;
        }

        public bool AllowTest(CollidableReference collidable)
        {
            if (!_hasIgnore) return true;
            return !(collidable.Mobility != CollidableMobility.Static && collidable.BodyHandle.Value == _ignore.Value);
        }

        public bool AllowTest(CollidableReference collidable, int childIndex) => AllowTest(collidable);

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal, CollidableReference collidable, int childIndex)
        {
            if (t >= maximumT) return;
            maximumT = t;
            T = t;
            Normal = normal;
            Collidable = collidable;
            ChildIndex = childIndex;
            Hit = true;
        }
    }
}

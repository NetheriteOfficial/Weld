using System.Numerics;
using BepuPhysics;

namespace Weld.Physics.Bepu;

public sealed class BepuBody : IPhysicsBody
{
    private readonly BepuPhysicsWorld _world;
    private bool _disposed;

    internal BepuBody(BepuPhysicsWorld world, BodyHandle handle, float mass)
    {
        _world = world;
        Handle = handle;
        Mass = mass;
    }

    public BodyHandle Handle { get; }
    public float Mass { get; }
    public bool IsAlive => !_disposed && _world.Simulation.Bodies.BodyExists(Handle);

    private BodyReference Ref => _world.Simulation.Bodies[Handle];

    public Vector3 Position
    {
        get => Ref.Pose.Position;
        set { var r = Ref; r.Pose.Position = value; r.Awake = true; }
    }

    public Quaternion Orientation
    {
        get => Ref.Pose.Orientation;
        set { var r = Ref; r.Pose.Orientation = value; r.Awake = true; }
    }

    public Vector3 LinearVelocity
    {
        get => Ref.Velocity.Linear;
        set { var r = Ref; r.Velocity.Linear = value; r.Awake = true; }
    }

    public Vector3 AngularVelocity
    {
        get => Ref.Velocity.Angular;
        set { var r = Ref; r.Velocity.Angular = value; r.Awake = true; }
    }

    public bool Awake
    {
        get => Ref.Awake;
        set { var r = Ref; r.Awake = value; }
    }

    public void ApplyImpulse(Vector3 impulse)
    {
        var r = Ref;
        r.Awake = true;
        r.Velocity.Linear += impulse / Mass;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _world.RemoveBody(this);
    }
}

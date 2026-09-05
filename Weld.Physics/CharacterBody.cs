using System.Numerics;
using Weld.World.Collision;

namespace Weld.Physics;

public sealed class CharacterBody : IDisposable
{
    private readonly IPhysicsWorld _world;
    private bool _disposed;

    public CharacterBody(IPhysicsWorld world, Vector3 position, float radius = 0.35f, float height = 1.8f, float mass = 80f)
    {
        _world = world;
        Radius = radius;
        Height = height;
        Body = world.AddCharacterCapsule(position, radius, height, mass);
    }

    public IPhysicsBody Body { get; }
    public float Radius { get; }
    public float Height { get; }
    public float WalkSpeed { get; set; } = 5.5f;
    public float RunSpeed { get; set; } = 9f;
    public float JumpSpeed { get; set; } = 5.2f;
    public float GroundAcceleration { get; set; } = 40f;
    public float AirAcceleration { get; set; } = 6f;
    public float GroundProbe { get; set; } = 0.15f;
    public float MaxSlopeCos { get; set; } = 0.55f;

    public bool IsGrounded { get; private set; }
    public SurfaceType GroundSurface { get; private set; } = SurfaceType.Default;
    public Vector3 GroundNormal { get; private set; } = Vector3.UnitZ;

    public Vector3 Position
    {
        get => Body.Position;
        set
        {
            Body.Position = value;
            Body.LinearVelocity = Vector3.Zero;
            Body.Awake = true;
        }
    }

    public Vector3 Velocity => Body.LinearVelocity;
    public Vector3 FeetPosition => Position - new Vector3(0, 0, Height * 0.5f);

    public void Move(Vector2 wishDirection, bool run, bool jump, float dt)
    {
        Body.Awake = true;
        ProbeGround();

        var target = wishDirection.LengthSquared() > 1e-6f ? Vector2.Normalize(wishDirection) * (run ? RunSpeed : WalkSpeed) : Vector2.Zero;
        var v = Body.LinearVelocity;
        var horizontal = new Vector2(v.X, v.Y);
        var accel = (IsGrounded ? GroundAcceleration : AirAcceleration) * dt;
        var delta = target - horizontal;
        if (delta.Length() > accel) delta = Vector2.Normalize(delta) * accel;
        horizontal += delta;

        var z = v.Z;
        if (IsGrounded)
        {
            if (jump) z = JumpSpeed;
            else if (z < 0) z = MathF.Max(z, -1f);
        }
        Body.LinearVelocity = new Vector3(horizontal.X, horizontal.Y, z);
    }

    private void ProbeGround()
    {
        var length = Height * 0.5f + GroundProbe;
        if (_world.RayCast(Position, -Vector3.UnitZ, length, out var hit, Body) && Vector3.Dot(hit.Normal, Vector3.UnitZ) >= MaxSlopeCos)
        {
            IsGrounded = true;
            GroundSurface = hit.Surface;
            GroundNormal = hit.Normal;
            return;
        }
        IsGrounded = false;
        GroundNormal = Vector3.UnitZ;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Body.Dispose();
    }
}

using System.Numerics;
using Weld.Physics;
using Weld.Entities.Net;
using Weld.World;

namespace Weld.Entities;

public class PropEntity : NetworkedEntity
{
    private IPhysicsBody? _body;

    public Vector3 Size { get; set; } = Vector3.One;
    public float Mass { get; set; } = 10f;
    public Quaternion Orientation { get; set; } = Quaternion.Identity;
    public Vector4 Color { get; set; } = new(0.9f, 0.6f, 0.2f, 1f);

    public override BoundingBox LocalBounds => new(-Size * 0.5f, Size * 0.5f);
    public override Matrix4x4 WorldMatrix => Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Position);
    public override Matrix4x4 InterpolatedWorldMatrix(float alpha) => Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(InterpolatedPosition(alpha));
    public override Vector4 DebugColor => Color;
    public override int ReplicationIntervalTicks => 3;

    public override void OnSpawn()
    {
        if (IsAuthority && World.Physics != null)
            _body = World.Physics.AddDynamicBox(Position, Orientation, Size, Mass);
    }

    public override void Tick()
    {
        if (_body == null) base.Tick();
    }

    public override void PostPhysicsTick()
    {
        if (_body == null) return;
        var pos = _body.Position;
        var moved = Vector3.DistanceSquared(pos, Position) > 1e-6f;
        Position = pos;
        Orientation = _body.Orientation;
        Velocity = _body.LinearVelocity;
        if (moved) MarkDirty();
    }

    public void ApplyImpulse(Vector3 impulse)
    {
        if (_body == null) return;
        _body.ApplyImpulse(impulse);
        MarkDirty();
    }

    public override void WriteSpawn(NetWriter w)
    {
        base.WriteSpawn(w);
        w.Write(Size).Write(Mass).Write(Color).Write(Orientation);
    }

    public override void ReadSpawn(NetReader r)
    {
        base.ReadSpawn(r);
        Size = r.ReadVector3();
        Mass = r.ReadFloat();
        Color = r.ReadVector4();
        Orientation = r.ReadQuaternion();
    }

    public override void WriteState(NetWriter w)
    {
        base.WriteState(w);
        w.Write(Orientation);
    }

    public override void ReadState(NetReader r, ulong serverTick)
    {
        base.ReadState(r, serverTick);
        Orientation = Quaternion.Slerp(Orientation, r.ReadQuaternion(), 0.5f);
    }

    public override void OnRemove()
    {
        _body?.Dispose();
        _body = null;
    }
}

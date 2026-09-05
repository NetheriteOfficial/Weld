using System.Numerics;
using Weld.World;

namespace Weld.Entities;

public abstract class BaseEntity
{
    public int Id { get; internal set; } = -1;
    public GameWorld World { get; internal set; } = null!;
    public EntityGraph Graph => World.Entities;
    public bool IsSpawned { get; internal set; }
    public bool IsRemoved { get; internal set; }
    public ulong SpawnTick { get; internal set; }
    public int TicksExisted { get; internal set; }

    public Vector3 Position { get; set; }
    public Vector3 PreviousPosition { get; internal set; }
    public Vector3 Velocity { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float PreviousYaw { get; internal set; }

    public virtual BoundingBox LocalBounds => new(new Vector3(-0.5f), new Vector3(0.5f));
    public virtual bool IsNetworked => false;
    public virtual Vector4 DebugColor => new(1f, 0.4f, 0.1f, 1f);
    public virtual bool IsRenderable => true;

    public string TypeName => World.Types.NameOf(this);

    public Vector3 Forward => new(MathF.Cos(Pitch) * MathF.Cos(Yaw), MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch));
    public Vector3 FlatForward => new(MathF.Cos(Yaw), MathF.Sin(Yaw), 0f);
    public Vector3 Right => Vector3.Normalize(Vector3.Cross(FlatForward, Vector3.UnitZ));

    public virtual Matrix4x4 WorldMatrix => Matrix4x4.CreateRotationZ(Yaw) * Matrix4x4.CreateTranslation(Position);
    public BoundingBox WorldBounds => LocalBounds.Transform(WorldMatrix);

    public Vector3 InterpolatedPosition(float alpha) => Vector3.Lerp(PreviousPosition, Position, Math.Clamp(alpha, 0f, 1f));

    public float InterpolatedYaw(float alpha)
    {
        var d = MathF.IEEERemainder(Yaw - PreviousYaw, MathF.Tau);
        return PreviousYaw + d * Math.Clamp(alpha, 0f, 1f);
    }

    public virtual Matrix4x4 InterpolatedWorldMatrix(float alpha) => Matrix4x4.CreateRotationZ(InterpolatedYaw(alpha)) * Matrix4x4.CreateTranslation(InterpolatedPosition(alpha));

    public float DistanceTo(BaseEntity other) => Vector3.Distance(Position, other.Position);
    public float DistanceTo(Vector3 point) => Vector3.Distance(Position, point);

    public virtual void OnSpawn() { }
    public virtual void Tick() { }
    public virtual void PostPhysicsTick() { }
    public virtual void OnRemove() { }

    public void Remove()
    {
        if (IsRemoved) return;
        IsRemoved = true;
        World?.Entities.Remove(this);
    }

    internal void CapturePrevious()
    {
        PreviousPosition = Position;
        PreviousYaw = Yaw;
    }

    public override string ToString() => $"{GetType().Name}#{Id} @ {Position}";
}

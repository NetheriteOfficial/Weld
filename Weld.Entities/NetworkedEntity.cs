using System.Numerics;
using Weld.Entities.Net;

namespace Weld.Entities;

public abstract class NetworkedEntity : BaseEntity
{
    private StateSample _target;
    private bool _hasTarget;

    public uint NetworkId { get; internal set; }
    public ushort OwnerClientId { get; internal set; }
    public bool IsAuthority => World.IsServer;
    public bool IsLocallyControlled => World.LocalClientId != 0 && OwnerClientId == World.LocalClientId;
    public bool IsRemoteProxy => !IsAuthority && !IsLocallyControlled;
    public bool Dirty { get; private set; } = true;
    public ulong LastReplicatedTick { get; set; }
    public ulong LastReceivedTick { get; set; }

    public override bool IsNetworked => true;
    public virtual int ReplicationIntervalTicks => 3;
    public virtual bool ReplicateState => true;
    public virtual float SnapDistance => 3f;
    public virtual float InterpolationRate => 0.35f;

    public void MarkDirty() => Dirty = true;
    public void ClearDirty() => Dirty = false;

    public virtual void WriteSpawn(NetWriter w)
    {
        w.Write(Position).Write(Yaw).Write(Pitch);
    }

    public virtual void ReadSpawn(NetReader r)
    {
        Position = r.ReadVector3();
        Yaw = r.ReadFloat();
        Pitch = r.ReadFloat();
        PreviousPosition = Position;
        PreviousYaw = Yaw;
    }

    public virtual void WriteState(NetWriter w)
    {
        w.Write(Position).Write(Velocity).Write(Yaw).Write(Pitch);
    }

    public virtual void ReadState(NetReader r, ulong serverTick)
    {
        var sample = new StateSample(serverTick, r.ReadVector3(), r.ReadVector3(), r.ReadFloat(), r.ReadFloat());
        if (serverTick < LastReceivedTick) return;
        LastReceivedTick = serverTick;
        ApplyRemoteState(sample);
    }

    protected virtual void ApplyRemoteState(in StateSample sample)
    {
        _target = sample;
        _hasTarget = true;
        if (Vector3.Distance(Position, sample.Position) > SnapDistance)
        {
            Position = sample.Position;
            PreviousPosition = sample.Position;
        }
    }

    protected void InterpolateTowardsTarget()
    {
        if (!_hasTarget) return;
        var predicted = _target.Position + _target.Velocity * (Graph.FixedDelta * (Graph.Tick > _target.Tick ? MathF.Min(Graph.Tick - _target.Tick, 6) : 0));
        Position = Vector3.Lerp(Position, predicted, InterpolationRate);
        Velocity = _target.Velocity;
        Yaw = LerpAngle(Yaw, _target.Yaw, InterpolationRate);
        Pitch = float.Lerp(Pitch, _target.Pitch, InterpolationRate);
    }

    public override void Tick()
    {
        if (IsRemoteProxy) InterpolateTowardsTarget();
    }

    protected static float LerpAngle(float a, float b, float t)
    {
        var d = MathF.IEEERemainder(b - a, MathF.Tau);
        return a + d * t;
    }

    public readonly record struct StateSample(ulong Tick, Vector3 Position, Vector3 Velocity, float Yaw, float Pitch);
}

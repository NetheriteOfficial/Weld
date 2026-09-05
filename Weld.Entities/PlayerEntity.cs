using System.Numerics;
using Weld.Entities.Net;
using Weld.Physics;
using Weld.World;
using Weld.World.Collision;

namespace Weld.Entities;

public class PlayerEntity : LivingEntity
{
    private PlayerInput _input;
    private bool _jumpQueued;

    public CharacterBody? Body { get; private set; }
    public string Name { get; set; } = "Player";
    public float EyeHeight { get; set; } = 0.75f;
    public bool IsGrounded { get; private set; }
    public SurfaceType GroundSurface { get; private set; }
    public uint LastProcessedInput { get; private set; }
    public float Radius { get; set; } = 0.35f;
    public float Height { get; set; } = 1.8f;

    public override BoundingBox LocalBounds => new(new Vector3(-Radius, -Radius, -Height * 0.5f), new Vector3(Radius, Radius, Height * 0.5f));
    public bool IsLocal => IsLocallyControlled || ReferenceEquals(World.LocalPlayer, this);
    public override Vector4 DebugColor => IsLocal ? new Vector4(0.2f, 0.9f, 0.3f, 1f) : new Vector4(0.2f, 0.5f, 1f, 1f);
    public override bool IsRenderable => !IsLocal || World.RenderLocalPlayer;
    public override int ReplicationIntervalTicks => 1;
    public override float SnapDistance => 2f;

    public Vector3 EyePosition => Position + new Vector3(0, 0, EyeHeight);
    public bool SimulatesPhysics => IsAuthority || IsLocallyControlled;

    public void SetInput(in PlayerInput input)
    {
        if (input.Sequence != 0 && input.Sequence <= LastProcessedInput && !IsLocallyControlled) return;
        _input = input;
        if (input.Jump) _jumpQueued = true;
    }

    public override void OnSpawn()
    {
        if (SimulatesPhysics && World.Physics != null)
            Body = new CharacterBody(World.Physics, Position, Radius, Height);
    }

    public override void Tick()
    {
        if (!SimulatesPhysics || Body == null)
        {
            base.Tick();
            return;
        }
        Yaw = _input.Yaw;
        Pitch = _input.Pitch;
        var wish = Vector2.Zero;
        if (_input.Move.LengthSquared() > 1e-6f && !IsDead)
        {
            var f = new Vector2(MathF.Cos(Yaw), MathF.Sin(Yaw));
            var r = new Vector2(f.Y, -f.X);
            wish = f * _input.Move.Y + r * _input.Move.X;
        }
        Body.Move(wish, _input.Run, _jumpQueued && !IsDead, Graph.FixedDelta);
        _jumpQueued = false;
        LastProcessedInput = _input.Sequence;
        base.Tick();
    }

    public override void PostPhysicsTick()
    {
        if (Body == null) return;
        Position = Body.Position;
        Velocity = Body.Velocity;
        IsGrounded = Body.IsGrounded;
        GroundSurface = Body.GroundSurface;
        if (IsAuthority) MarkDirty();
    }

    public void Teleport(Vector3 position)
    {
        Position = position;
        PreviousPosition = position;
        if (Body != null) Body.Position = position;
        MarkDirty();
    }

    protected override void OnDeath(BaseEntity? source)
    {
        World.Log.Info($"{Name} died");
    }

    public override void WriteSpawn(NetWriter w)
    {
        base.WriteSpawn(w);
        w.Write(Name);
    }

    public override void ReadSpawn(NetReader r)
    {
        base.ReadSpawn(r);
        Name = r.ReadString();
    }

    public override void WriteState(NetWriter w)
    {
        base.WriteState(w);
        w.Write(IsGrounded).Write((byte)GroundSurface).Write(LastProcessedInput);
    }

    public override void ReadState(NetReader r, ulong serverTick)
    {
        base.ReadState(r, serverTick);
        var grounded = r.ReadBool();
        var surface = (SurfaceType)r.ReadByte();
        var processed = r.ReadUInt();
        if (IsLocallyControlled) return;
        IsGrounded = grounded;
        GroundSurface = surface;
        LastProcessedInput = processed;
    }

    protected override void ApplyRemoteState(in StateSample sample)
    {
        if (IsLocallyControlled)
        {
            if (Body != null && Vector3.Distance(Body.Position, sample.Position) > SnapDistance)
            {
                Body.Position = sample.Position;
                Position = sample.Position;
            }
            return;
        }
        base.ApplyRemoteState(sample);
    }

    public override void OnRemove()
    {
        Body?.Dispose();
        Body = null;
    }
}

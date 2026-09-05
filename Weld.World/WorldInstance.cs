using System.Numerics;
using Weld.World.Data;
using Weld.World.Streaming;

namespace Weld.World;

public sealed class WorldInstance
{
    internal WorldInstance(XmlInstance xml, ItemDefinition definition, string sourceFile, int index)
    {
        Id = xml.ID;
        Index = index;
        Definition = definition;
        Interior = xml.Interior;
        Position = xml.Position.ToVector3();
        Scale = xml.Scale.ToVector3();
        RotationDegrees = xml.Rotation.ToVector3();
        SourceFile = sourceFile;
        RecomputeTransform();
    }

    public int Id { get; }
    public int Index { get; }
    public ItemDefinition Definition { get; }
    public int Interior { get; }
    public string SourceFile { get; }

    public Vector3 Position { get; private set; }
    public Vector3 Scale { get; private set; }
    public Vector3 RotationDegrees { get; private set; }

    public Matrix4x4 WorldMatrix { get; private set; }
    public BoundingBox WorldBounds { get; private set; }

    public float DistanceToCamera { get; internal set; } = float.MaxValue;
    public bool IsInRange { get; internal set; }
    public bool IsInFrustum { get; internal set; }
    public bool IsVisible => IsInRange && IsInFrustum;
    public bool IsDrawable { get; internal set; }
    public int LastVisibleFrame { get; internal set; } = -1;

    public bool IsLod => Definition.IsLod;
    public WorldInstance? LodParent { get; internal set; }
    public IReadOnlyList<WorldInstance> LodChildren => _lodChildren;
    internal readonly List<WorldInstance> _lodChildren = new();
    public LodPairingSource LodSource { get; internal set; }

    public float LodBlend { get; internal set; } = 1f;
    public float StreamFade { get; internal set; } = 1f;
    public float Opacity { get; internal set; } = 1f;
    public bool DitherInverted { get; internal set; }

    public void SetTransform(Vector3 position, Vector3 rotationDegrees, Vector3 scale)
    {
        Position = position;
        RotationDegrees = rotationDegrees;
        Scale = scale;
        RecomputeTransform();
    }

    private void RecomputeTransform()
    {
        WorldMatrix = WorldTransform.Compose(Position, RotationDegrees, Scale);
        WorldBounds = Definition.LocalBounds.Transform(WorldMatrix);
    }

    public override string ToString() => $"Instance #{Id} {Definition.ModelName} @ {Position}";
}

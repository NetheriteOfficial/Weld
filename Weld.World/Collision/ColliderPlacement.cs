using System.Numerics;
using Weld.World.Assets;
using Weld.World.Data;
using Weld.World.Streaming;

namespace Weld.World.Collision;

public sealed class ColliderPlacement
{
    internal ColliderPlacement(XmlCollider xml, string sourceFile, int index)
    {
        Id = xml.ID;
        Index = index;
        ModelName = xml.ModelName;
        Flags = xml.Flags;
        Position = xml.Position.ToVector3();
        Scale = xml.Scale.ToVector3();
        RotationDegrees = xml.Rotation.ToVector3();
        SourceFile = sourceFile;
        WorldMatrix = WorldTransform.Compose(Position, RotationDegrees, Scale);
        Matrix4x4.Decompose(WorldMatrix, out _, out var q, out _);
        Orientation = q;
    }

    public int Id { get; }
    public int Index { get; }
    public string ModelName { get; }
    public uint Flags { get; }
    public Vector3 Position { get; }
    public Vector3 Scale { get; }
    public Vector3 RotationDegrees { get; }
    public Quaternion Orientation { get; }
    public Matrix4x4 WorldMatrix { get; }
    public string SourceFile { get; }

    public StreamingState State { get; internal set; } = StreamingState.Unloaded;
    public Exception? Error { get; internal set; }
    public ICollisionModel? Collision { get; internal set; }
    public ICollisionFormat? Format { get; internal set; }
    public AssetEntry? Entry { get; internal set; }

    public TriangleCollisionMesh? Mesh => Collision as TriangleCollisionMesh;
    public BoundingBox? WorldBounds => Mesh?.Bounds.Transform(WorldMatrix);

    public override string ToString() => $"Collider #{Id} {ModelName} @ {Position} [{State}]";
}

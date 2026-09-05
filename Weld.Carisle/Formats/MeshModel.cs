using System.Numerics;
using Weld.World;
using Weld.World.Assets;

namespace Weld.Carisle.Formats;

public sealed class MeshModel : IModel
{
    public const int FloatsPerVertex = 8;
    public const int StrideBytes = FloatsPerVertex * sizeof(float);

    public MeshModel(string name, List<SubMesh> subMeshes, BoundingBox bounds)
    {
        Name = name;
        SubMeshes = subMeshes;
        Bounds = bounds;
        MemoryFootprint = subMeshes.Sum(s => (long)s.Vertices.Length * sizeof(float) + (long)s.Indices.Length * sizeof(uint));
    }

    public string Name { get; }
    public List<SubMesh> SubMeshes { get; }
    public BoundingBox? Bounds { get; }

    public int VertexCount => SubMeshes.Sum(s => s.Vertices.Length / FloatsPerVertex);
    public int TriangleCount => SubMeshes.Sum(s => s.Indices.Length / 3);

    public long MemoryFootprint { get; private set; }

    public void ReleaseCpuData()
    {
        foreach (var s in SubMeshes) s.ReleaseCpuData();
    }

    public void Dispose() => ReleaseCpuData();

    public sealed class SubMesh
    {
        public SubMesh(float[] vertices, uint[] indices, string? textureName, Vector4 diffuseColor)
        {
            Vertices = vertices;
            Indices = indices;
            TextureName = textureName;
            DiffuseColor = diffuseColor;
        }

        public float[] Vertices { get; private set; }
        public uint[] Indices { get; private set; }
        public string? TextureName { get; }
        public Vector4 DiffuseColor { get; }

        internal void ReleaseCpuData()
        {
            Vertices = Array.Empty<float>();
            Indices = Array.Empty<uint>();
        }
    }
}

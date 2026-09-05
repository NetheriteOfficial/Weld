using System.Numerics;
using Weld.World.Assets;

namespace Weld.World.Collision;

public sealed class TriangleCollisionMesh : ICollisionModel
{
    public TriangleCollisionMesh(string name, Vector3[] vertices, int[] indices, SurfaceType[] triangleSurfaces, string[] materialNames)
    {
        if (indices.Length % 3 != 0) throw new ArgumentException("index count must be a multiple of 3", nameof(indices));
        if (triangleSurfaces.Length != indices.Length / 3) throw new ArgumentException("one surface per triangle required", nameof(triangleSurfaces));
        Name = name;
        Vertices = vertices;
        Indices = indices;
        TriangleSurfaces = triangleSurfaces;
        MaterialNames = materialNames;
        Bounds = vertices.Length > 0 ? BoundingBox.FromPoints(vertices) : new BoundingBox(default, default);
    }

    public string Name { get; }
    public Vector3[] Vertices { get; }
    public int[] Indices { get; }
    public SurfaceType[] TriangleSurfaces { get; }
    public string[] MaterialNames { get; }
    public BoundingBox Bounds { get; }
    public int TriangleCount => Indices.Length / 3;

    public SurfaceType DominantSurface
    {
        get
        {
            if (TriangleSurfaces.Length == 0) return SurfaceType.Default;
            Span<int> counts = stackalloc int[Enum.GetValues<SurfaceType>().Length];
            foreach (var s in TriangleSurfaces) counts[(int)s]++;
            var best = 0;
            for (var i = 1; i < counts.Length; i++) if (counts[i] > counts[best]) best = i;
            return (SurfaceType)best;
        }
    }

    public void GetTriangle(int index, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        a = Vertices[Indices[index * 3]];
        b = Vertices[Indices[index * 3 + 1]];
        c = Vertices[Indices[index * 3 + 2]];
    }

    public void Dispose() { }
}

using System.Numerics;
using Assimp;
using Weld.World;
using Weld.World.Assets;
using Weld.World.Collision;
using AssimpMatrix = Assimp.Matrix4x4;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace Weld.Carisle.Formats;

public sealed class AssimpCollisionFormat : ICollisionFormat
{
    private static readonly PostProcessSteps Steps =
        PostProcessSteps.Triangulate |
        PostProcessSteps.JoinIdenticalVertices |
        PostProcessSteps.PreTransformVertices;

    public AssimpCollisionFormat(string extension = ".fbx", float scale = 1f)
    {
        Extension = extension;
        Scale = scale;
    }

    public string Name => "Assimp collision FBX";
    public string Extension { get; }
    public float Scale { get; }

    public IEnumerable<string> ResolveEntryNames(string assetName)
    {
        yield return assetName + Extension;
    }

    public ICollisionModel Load(AssetLoadContext context, AssetEntry entry)
    {
        using var importer = new AssimpContext();
        importer.SetConfig(new Assimp.Configs.FBXPreservePivotsConfig(false));
        importer.Scale = Scale;

        Scene scene;
        if (entry.PhysicalPath != null) scene = importer.ImportFile(entry.PhysicalPath, Steps);
        else
        {
            using var stream = entry.Open();
            scene = importer.ImportFileFromStream(stream, Steps, Extension.TrimStart('.'));
        }
        if (scene == null || !scene.HasMeshes) throw new InvalidDataException($"no collision geometry in '{entry}'");

        var materialNames = new string[scene.MaterialCount];
        var materialSurfaces = new SurfaceType[scene.MaterialCount];
        for (var i = 0; i < scene.MaterialCount; i++)
        {
            materialNames[i] = scene.Materials[i].Name ?? "";
            materialSurfaces[i] = SurfaceProperties.Parse(materialNames[i]);
        }

        var vertices = new List<Vector3>();
        var indices = new List<int>();
        var surfaces = new List<SurfaceType>();

        Walk(scene.RootNode, AssimpMatrix.Identity);

        void Walk(Node node, AssimpMatrix parent)
        {
            var world = node.Transform * parent;
            var m = ToNumerics(world);
            foreach (var mi in node.MeshIndices)
            {
                var mesh = scene.Meshes[mi];
                if (!mesh.PrimitiveType.HasFlag(PrimitiveType.Triangle)) continue;
                var baseVertex = vertices.Count;
                for (var v = 0; v < mesh.VertexCount; v++)
                    vertices.Add(Vector3.Transform(ToNumerics(mesh.Vertices[v]), m));
                var surface = mesh.MaterialIndex >= 0 && mesh.MaterialIndex < materialSurfaces.Length ? materialSurfaces[mesh.MaterialIndex] : SurfaceType.Default;
                foreach (var face in mesh.Faces)
                {
                    if (face.IndexCount != 3) continue;
                    indices.Add(baseVertex + face.Indices[0]);
                    indices.Add(baseVertex + face.Indices[1]);
                    indices.Add(baseVertex + face.Indices[2]);
                    surfaces.Add(surface);
                }
            }
            foreach (var child in node.Children) Walk(child, world);
        }

        if (indices.Count == 0) throw new InvalidDataException($"'{entry}' has no triangles");
        context.Log.Debug($"[{Name}] {entry.EntryName}: {indices.Count / 3} tri(s), materials: {string.Join(", ", materialNames.Select((n, i) => $"{n}→{materialSurfaces[i]}"))}");
        return new TriangleCollisionMesh(context.AssetName, vertices.ToArray(), indices.ToArray(), surfaces.ToArray(), materialNames);
    }

    private static Vector3 ToNumerics(Vector3D v) => new(v.X, v.Y, v.Z);

    private static Matrix4x4 ToNumerics(AssimpMatrix a) => new(
        a.A1, a.B1, a.C1, a.D1,
        a.A2, a.B2, a.C2, a.D2,
        a.A3, a.B3, a.C3, a.D3,
        a.A4, a.B4, a.C4, a.D4);
}

using System.Numerics;
using Assimp;
using Weld.World;
using Weld.World.Assets;
using AssimpMatrix = Assimp.Matrix4x4;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace Weld.Carisle.Formats;

public sealed class AssimpFbxModelFormat : IModelFormat
{
    private static readonly PostProcessSteps Steps =
        PostProcessSteps.Triangulate |
        PostProcessSteps.GenerateSmoothNormals |
        PostProcessSteps.JoinIdenticalVertices |
        PostProcessSteps.ImproveCacheLocality |
        PostProcessSteps.FlipUVs |
        PostProcessSteps.PreTransformVertices;

    public AssimpFbxModelFormat(string extension = ".fbx", float scale = 1f)
    {
        Extension = extension;
        Scale = scale;
    }

    public string Name => "Assimp FBX";
    public string Extension { get; }
    public float Scale { get; }

    public IEnumerable<string> ResolveEntryNames(string assetName)
    {
        yield return assetName + Extension;
    }

    public IModel Load(AssetLoadContext context, AssetEntry entry)
    {
        using var importer = new AssimpContext();
        importer.SetConfig(new Assimp.Configs.FBXPreservePivotsConfig(false));
        importer.Scale = Scale;

        Scene scene;
        if (entry.PhysicalPath != null)
        {
            scene = importer.ImportFile(entry.PhysicalPath, Steps);
        }
        else
        {
            using var stream = entry.Open();
            scene = importer.ImportFileFromStream(stream, Steps, Extension.TrimStart('.'));
        }

        if (scene == null || !scene.HasMeshes)
            throw new InvalidDataException($"Assimp produced no meshes for '{entry}'");

        context.Cancellation.ThrowIfCancellationRequested();

        var byMaterial = new Dictionary<int, (List<float> verts, List<uint> idx)>();
        var bounds = Weld.World.BoundingBox.Empty;

        Walk(scene.RootNode, AssimpMatrix.Identity);

        void Walk(Node node, AssimpMatrix parent)
        {
            var world = node.Transform * parent;
            var m = ToNumerics(world);
            Matrix4x4.Invert(m, out var inv);
            var normalM = Matrix4x4.Transpose(inv);

            foreach (var mi in node.MeshIndices)
            {
                var mesh = scene.Meshes[mi];
                if (!mesh.PrimitiveType.HasFlag(PrimitiveType.Triangle)) continue;

                if (!byMaterial.TryGetValue(mesh.MaterialIndex, out var acc))
                {
                    acc = (new List<float>(mesh.VertexCount * MeshModel.FloatsPerVertex), new List<uint>(mesh.FaceCount * 3));
                    byMaterial[mesh.MaterialIndex] = acc;
                }

                var baseVertex = (uint)(acc.verts.Count / MeshModel.FloatsPerVertex);
                var hasUv = mesh.HasTextureCoords(0);
                var hasN = mesh.HasNormals;
                for (var v = 0; v < mesh.VertexCount; v++)
                {
                    var p = Vector3.Transform(ToNumerics(mesh.Vertices[v]), m);
                    var n = hasN ? Vector3.Normalize(Vector3.TransformNormal(ToNumerics(mesh.Normals[v]), normalM)) : Vector3.UnitZ;
                    var uv = hasUv ? mesh.TextureCoordinateChannels[0][v] : new Vector3D(0, 0, 0);
                    acc.verts.Add(p.X); acc.verts.Add(p.Y); acc.verts.Add(p.Z);
                    acc.verts.Add(n.X); acc.verts.Add(n.Y); acc.verts.Add(n.Z);
                    acc.verts.Add(uv.X); acc.verts.Add(uv.Y);
                    bounds = bounds.Encapsulate(p);
                }
                foreach (var face in mesh.Faces)
                {
                    if (face.IndexCount != 3) continue;
                    acc.idx.Add(baseVertex + (uint)face.Indices[0]);
                    acc.idx.Add(baseVertex + (uint)face.Indices[1]);
                    acc.idx.Add(baseVertex + (uint)face.Indices[2]);
                }
            }
            foreach (var child in node.Children) Walk(child, world);
        }

        var subMeshes = new List<MeshModel.SubMesh>(byMaterial.Count);
        foreach (var (matIndex, acc) in byMaterial)
        {
            if (acc.idx.Count == 0) continue;
            string? texName = null;
            var color = Vector4.One;
            if (matIndex >= 0 && matIndex < scene.MaterialCount)
            {
                var mat = scene.Materials[matIndex];
                if (mat.HasTextureDiffuse)
                    texName = Path.GetFileNameWithoutExtension(mat.TextureDiffuse.FilePath.Replace('\\', '/'));
                if (mat.HasColorDiffuse)
                    color = new Vector4(mat.ColorDiffuse.R, mat.ColorDiffuse.G, mat.ColorDiffuse.B, mat.ColorDiffuse.A);
            }
            subMeshes.Add(new MeshModel.SubMesh(acc.verts.ToArray(), acc.idx.ToArray(), texName, color));
        }

        if (subMeshes.Count == 0)
            throw new InvalidDataException($"'{entry}' contains no triangle geometry");

        context.Log.Debug($"[{Name}] {entry.EntryName}: {subMeshes.Count} sub-mesh(es), bounds {bounds}");
        return new MeshModel(context.AssetName, subMeshes, bounds);
    }

    private static Vector3 ToNumerics(Vector3D v) => new(v.X, v.Y, v.Z);

    private static Matrix4x4 ToNumerics(AssimpMatrix a) => new(
        a.A1, a.B1, a.C1, a.D1,
        a.A2, a.B2, a.C2, a.D2,
        a.A3, a.B3, a.C3, a.D3,
        a.A4, a.B4, a.C4, a.D4);
}

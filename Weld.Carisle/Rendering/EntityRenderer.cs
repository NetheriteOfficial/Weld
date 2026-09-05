using System.Numerics;
using Silk.NET.OpenGL;
using Weld.Carisle.Formats;
using Weld.Entities;

namespace Weld.Carisle.Rendering;

internal sealed unsafe class EntityRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao, _vbo, _ebo;
    private const uint IndexCount = 36;

    public EntityRenderer(GL gl)
    {
        _gl = gl;
        var verts = BuildCube();
        var idx = BuildIndices();

        _vbo = _gl.CreateBuffer();
        fixed (float* p = verts) _gl.NamedBufferData(_vbo, (nuint)(verts.Length * sizeof(float)), p, VertexBufferObjectUsage.StaticDraw);
        _ebo = _gl.CreateBuffer();
        fixed (uint* p = idx) _gl.NamedBufferData(_ebo, (nuint)(idx.Length * sizeof(uint)), p, VertexBufferObjectUsage.StaticDraw);

        _vao = _gl.CreateVertexArray();
        _gl.VertexArrayVertexBuffer(_vao, 0, _vbo, 0, (uint)MeshModel.StrideBytes);
        _gl.VertexArrayElementBuffer(_vao, _ebo);
        for (uint i = 0; i < 3; i++)
        {
            _gl.EnableVertexArrayAttrib(_vao, i);
            _gl.VertexArrayAttribFormat(_vao, i, i == 2 ? 2 : 3, VertexAttribType.Float, false, i == 0 ? 0u : i == 1 ? 12u : 24u);
            _gl.VertexArrayAttribBinding(_vao, i, 0);
        }
    }

    public int DrawnLastFrame { get; private set; }

    public void Draw(GameWorld world, float alpha, int uModel, int uDiffuse, int uHasTexture, int uOpacity, int uDitherInvert)
    {
        DrawnLastFrame = 0;
        _gl.BindVertexArray(_vao);
        _gl.Uniform1(uHasTexture, 0);
        _gl.Uniform1(uOpacity, 1f);
        _gl.Uniform1(uDitherInvert, 0);
        foreach (var e in world.Entities.Entities)
        {
            if (!e.IsRenderable || e.IsRemoved) continue;
            var size = e.LocalBounds.Size;
            var center = e.LocalBounds.Center;
            var m = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(center) * e.InterpolatedWorldMatrix(alpha);
            _gl.UniformMatrix4(uModel, 1, false, (float*)&m);
            var c = e.DebugColor;
            _gl.Uniform4(uDiffuse, c.X, c.Y, c.Z, c.W);
            _gl.DrawElements(PrimitiveType.Triangles, IndexCount, DrawElementsType.UnsignedInt, (void*)0);
            DrawnLastFrame++;
        }
        _gl.BindVertexArray(0);
    }

    private static float[] BuildCube()
    {
        var faces = new (Vector3 n, Vector3 u, Vector3 v)[]
        {
            (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            (-Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ),
            (Vector3.UnitY, -Vector3.UnitX, Vector3.UnitZ),
            (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),
        };
        var data = new List<float>(24 * MeshModel.FloatsPerVertex);
        foreach (var (n, u, v) in faces)
        {
            for (var i = 0; i < 4; i++)
            {
                var su = (i == 1 || i == 2) ? 1f : -1f;
                var sv = (i >= 2) ? 1f : -1f;
                var p = (n + u * su + v * sv) * 0.5f;
                data.AddRange(new[] { p.X, p.Y, p.Z, n.X, n.Y, n.Z, su * 0.5f + 0.5f, sv * 0.5f + 0.5f });
            }
        }
        return data.ToArray();
    }

    private static uint[] BuildIndices()
    {
        var idx = new uint[IndexCount];
        for (uint f = 0; f < 6; f++)
        {
            var b = f * 4;
            var o = f * 6;
            idx[o] = b; idx[o + 1] = b + 1; idx[o + 2] = b + 2;
            idx[o + 3] = b; idx[o + 4] = b + 2; idx[o + 5] = b + 3;
        }
        return idx;
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
    }
}

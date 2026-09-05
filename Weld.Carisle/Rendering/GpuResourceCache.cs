using System.Numerics;
using Silk.NET.OpenGL;
using Weld.Carisle.Formats;
using Weld.World;
using Weld.World.Streaming;

namespace Weld.Carisle.Rendering;

internal sealed class GpuSubMesh
{
    public uint Vao, Vbo, Ebo;
    public uint IndexCount;
    public uint Texture;
    public Vector4 DiffuseColor;
}

internal sealed class GpuModel
{
    public required string Name;
    public readonly List<GpuSubMesh> SubMeshes = new();
}

internal sealed class GpuTextureDictionary
{
    public required string Name;
    public readonly Dictionary<string, uint> Textures = new(StringComparer.OrdinalIgnoreCase);
    public uint Fallback;

    public uint Find(string? textureName)
    {
        if (textureName != null && Textures.TryGetValue(textureName, out var t)) return t;
        return Fallback;
    }
}

internal sealed unsafe class GpuResourceCache : IDisposable
{
    private readonly GL _gl;
    private readonly Level _level;
    private readonly Dictionary<StreamedModel, GpuModel> _models = new();
    private readonly Dictionary<StreamedTextureDictionary, GpuTextureDictionary> _txds = new();
    private readonly uint _whiteTexture;

    public GpuResourceCache(GL gl, Level level)
    {
        _gl = gl;
        _level = level;
        _whiteTexture = CreateTexture(1, 1, new byte[] { 255, 255, 255, 255 }, mipmaps: false);

        level.TextureDictionaryResident += OnTxdResident;
        level.TextureDictionaryEvicted += OnTxdEvicted;
        level.ModelResident += OnModelResident;
        level.ModelEvicted += OnModelEvicted;

        foreach (var t in level.TextureDictionaries) if (t.IsResident) OnTxdResident(t);
        foreach (var m in level.Models) if (m.IsResident) OnModelResident(m);
    }

    public GpuModel? GetModel(StreamedModel model) => _models.GetValueOrDefault(model);
    public int ModelCount => _models.Count;
    public int TextureDictionaryCount => _txds.Count;

    private void OnTxdResident(StreamedTextureDictionary slot)
    {
        if (_txds.ContainsKey(slot)) return;
        if (slot.Dictionary is not PngTextureDictionary png)
        {
            Console.WriteLine($"[GpuResourceCache] TXD '{slot.Name}' is a {slot.Dictionary?.GetType().Name}; only PngTextureDictionary is supported by this renderer.");
            return;
        }

        var gpu = new GpuTextureDictionary { Name = slot.Name };
        foreach (var (name, tex) in png.Textures)
            gpu.Textures[name] = CreateTexture(tex.Width, tex.Height, tex.Rgba, mipmaps: true);
        gpu.Fallback = png.Fallback != null ? gpu.Textures[png.Fallback.Name] : _whiteTexture;
        _txds[slot] = gpu;
        png.ReleaseCpuData();
    }

    private void OnTxdEvicted(StreamedTextureDictionary slot)
    {
        if (!_txds.Remove(slot, out var gpu)) return;
        foreach (var t in gpu.Textures.Values) _gl.DeleteTexture(t);
    }

    private void OnModelResident(StreamedModel slot)
    {
        if (_models.ContainsKey(slot)) return;
        if (slot.Model is not MeshModel mesh)
        {
            Console.WriteLine($"[GpuResourceCache] model '{slot.Name}' is a {slot.Model?.GetType().Name}; only MeshModel is supported by this renderer.");
            return;
        }

        GpuTextureDictionary? txd = null;
        if (slot.TextureDictionary != null) _txds.TryGetValue(slot.TextureDictionary, out txd);

        var gpu = new GpuModel { Name = slot.Name };
        foreach (var sub in mesh.SubMeshes)
        {
            var g = new GpuSubMesh
            {
                IndexCount = (uint)sub.Indices.Length,
                DiffuseColor = sub.DiffuseColor,
                Texture = txd?.Find(sub.TextureName) ?? (sub.TextureName != null ? _whiteTexture : 0),
            };

            g.Vbo = _gl.CreateBuffer();
            fixed (float* p = sub.Vertices)
                _gl.NamedBufferData(g.Vbo, (nuint)(sub.Vertices.Length * sizeof(float)), p, VertexBufferObjectUsage.StaticDraw);

            g.Ebo = _gl.CreateBuffer();
            fixed (uint* p = sub.Indices)
                _gl.NamedBufferData(g.Ebo, (nuint)(sub.Indices.Length * sizeof(uint)), p, VertexBufferObjectUsage.StaticDraw);

            g.Vao = _gl.CreateVertexArray();
            _gl.VertexArrayVertexBuffer(g.Vao, 0, g.Vbo, 0, (uint)MeshModel.StrideBytes);
            _gl.VertexArrayElementBuffer(g.Vao, g.Ebo);

            SetAttrib(g.Vao, 0, 3, 0);
            SetAttrib(g.Vao, 1, 3, 3 * sizeof(float));
            SetAttrib(g.Vao, 2, 2, 6 * sizeof(float));

            gpu.SubMeshes.Add(g);
        }

        _models[slot] = gpu;
        mesh.ReleaseCpuData();
    }

    private void SetAttrib(uint vao, uint index, int size, uint offset)
    {
        _gl.EnableVertexArrayAttrib(vao, index);
        _gl.VertexArrayAttribFormat(vao, index, size, VertexAttribType.Float, false, offset);
        _gl.VertexArrayAttribBinding(vao, index, 0);
    }

    private void OnModelEvicted(StreamedModel slot)
    {
        if (!_models.Remove(slot, out var gpu)) return;
        foreach (var s in gpu.SubMeshes)
        {
            _gl.DeleteVertexArray(s.Vao);
            _gl.DeleteBuffer(s.Vbo);
            _gl.DeleteBuffer(s.Ebo);
        }
    }

    private uint CreateTexture(int width, int height, byte[] rgba, bool mipmaps)
    {
        var levels = mipmaps ? (uint)Math.Floor(Math.Log2(Math.Max(width, height))) + 1 : 1u;
        var tex = _gl.CreateTexture(TextureTarget.Texture2D);
        _gl.TextureStorage2D(tex, levels, SizedInternalFormat.Rgba8, (uint)width, (uint)height);
        fixed (byte* p = rgba)
            _gl.TextureSubImage2D(tex, 0, 0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        if (mipmaps) _gl.GenerateTextureMipmap(tex);
        _gl.TextureParameter(tex, TextureParameterName.TextureMinFilter, (int)(mipmaps ? GLEnum.LinearMipmapLinear : GLEnum.Nearest));
        _gl.TextureParameter(tex, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TextureParameter(tex, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        _gl.TextureParameter(tex, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        return tex;
    }

    public void Dispose()
    {
        _level.TextureDictionaryResident -= OnTxdResident;
        _level.TextureDictionaryEvicted -= OnTxdEvicted;
        _level.ModelResident -= OnModelResident;
        _level.ModelEvicted -= OnModelEvicted;
        foreach (var m in _models.Keys.ToList()) OnModelEvicted(m);
        foreach (var t in _txds.Keys.ToList()) OnTxdEvicted(t);
        _gl.DeleteTexture(_whiteTexture);
    }
}

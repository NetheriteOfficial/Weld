using System.Numerics;
using System.Text;

namespace Weld.Entities.Net;

public sealed class NetWriter
{
    private readonly MemoryStream _stream;
    private readonly BinaryWriter _writer;

    public NetWriter(int capacity = 256)
    {
        _stream = new MemoryStream(capacity);
        _writer = new BinaryWriter(_stream, Encoding.UTF8, true);
    }

    public int Length => (int)_stream.Length;

    public void Reset()
    {
        _stream.SetLength(0);
        _stream.Position = 0;
    }

    public NetWriter Write(bool v) { _writer.Write(v); return this; }
    public NetWriter Write(byte v) { _writer.Write(v); return this; }
    public NetWriter Write(ushort v) { _writer.Write(v); return this; }
    public NetWriter Write(short v) { _writer.Write(v); return this; }
    public NetWriter Write(uint v) { _writer.Write(v); return this; }
    public NetWriter Write(int v) { _writer.Write(v); return this; }
    public NetWriter Write(ulong v) { _writer.Write(v); return this; }
    public NetWriter Write(float v) { _writer.Write(v); return this; }
    public NetWriter Write(double v) { _writer.Write(v); return this; }
    public NetWriter Write(string v) { _writer.Write(v ?? ""); return this; }
    public NetWriter Write(Vector2 v) { _writer.Write(v.X); _writer.Write(v.Y); return this; }
    public NetWriter Write(Vector3 v) { _writer.Write(v.X); _writer.Write(v.Y); _writer.Write(v.Z); return this; }
    public NetWriter Write(Vector4 v) { _writer.Write(v.X); _writer.Write(v.Y); _writer.Write(v.Z); _writer.Write(v.W); return this; }
    public NetWriter Write(Quaternion q) { _writer.Write(q.X); _writer.Write(q.Y); _writer.Write(q.Z); _writer.Write(q.W); return this; }
    public NetWriter WriteBytes(ReadOnlySpan<byte> bytes) { _writer.Write((ushort)bytes.Length); _writer.Write(bytes); return this; }

    public byte[] ToArray() => _stream.ToArray();
    public ReadOnlySpan<byte> Span => _stream.GetBuffer().AsSpan(0, (int)_stream.Length);
}

public sealed class NetReader
{
    private readonly MemoryStream _stream;
    private readonly BinaryReader _reader;

    public NetReader(byte[] data) : this(data, 0, data.Length) { }

    public NetReader(byte[] data, int offset, int count)
    {
        _stream = new MemoryStream(data, offset, count, false);
        _reader = new BinaryReader(_stream, Encoding.UTF8, true);
    }

    public int Remaining => (int)(_stream.Length - _stream.Position);
    public int Position => (int)_stream.Position;

    public bool ReadBool() => _reader.ReadBoolean();
    public byte ReadByte() => _reader.ReadByte();
    public ushort ReadUShort() => _reader.ReadUInt16();
    public short ReadShort() => _reader.ReadInt16();
    public uint ReadUInt() => _reader.ReadUInt32();
    public int ReadInt() => _reader.ReadInt32();
    public ulong ReadULong() => _reader.ReadUInt64();
    public float ReadFloat() => _reader.ReadSingle();
    public double ReadDouble() => _reader.ReadDouble();
    public string ReadString() => _reader.ReadString();
    public Vector2 ReadVector2() => new(_reader.ReadSingle(), _reader.ReadSingle());
    public Vector3 ReadVector3() => new(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
    public Vector4 ReadVector4() => new(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
    public Quaternion ReadQuaternion() => new(_reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle(), _reader.ReadSingle());
    public byte[] ReadBytes() => _reader.ReadBytes(_reader.ReadUInt16());
}

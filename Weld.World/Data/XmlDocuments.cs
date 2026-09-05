using System.Numerics;
using System.Xml;
using System.Xml.Serialization;

namespace Weld.World.Data;

public sealed class XmlVector3
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public XmlVector3() { }
    public XmlVector3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public Vector3 ToVector3() => new(X, Y, Z);
    public static XmlVector3 From(Vector3 v) => new(v.X, v.Y, v.Z);
    public override string ToString() => $"({X}, {Y}, {Z})";
}

[XmlRoot("Level")]
public sealed class LevelFile
{
    [XmlArray("ImgFiles"), XmlArrayItem("ImgFile")]
    public List<string> ImgFiles { get; set; } = new();

    [XmlArray("ItemDefinitions"), XmlArrayItem("ItemDefinition")]
    public List<string> ItemDefinitions { get; set; } = new();

    [XmlArray("CollisionFiles"), XmlArrayItem("CollisionFile")]
    public List<string> CollisionFiles { get; set; } = new();

    [XmlArray("ItemPlacementLists"), XmlArrayItem("ItemPlacementList")]
    public List<string> ItemPlacementLists { get; set; } = new();
}

public sealed class XmlObjectDefinition
{
    public int ID { get; set; }
    public string ModelName { get; set; } = "";
    public string DffFile { get; set; } = "";
    public string TextureDictionary { get; set; } = "";
    public float DrawDistance { get; set; } = 300f;
    public uint Flags { get; set; }
    public XmlBoundingBox? BoundingBox { get; set; }
    public string? LodModel { get; set; }
}

public sealed class XmlBoundingBox
{
    public XmlVector3 Min { get; set; } = new();
    public XmlVector3 Max { get; set; } = new();
    public BoundingBox ToBoundingBox() => new(Min.ToVector3(), Max.ToVector3());
}

[XmlRoot("ItemDefinition")]
public sealed class ItemDefinitionFile
{
    [XmlArray("Objects"), XmlArrayItem("Object")]
    public List<XmlObjectDefinition> Objects { get; set; } = new();
}

public sealed class XmlInstance
{
    public int ID { get; set; }
    public string ModelName { get; set; } = "";
    public int Interior { get; set; }
    public XmlVector3 Position { get; set; } = new();
    public XmlVector3 Scale { get; set; } = new(1, 1, 1);
    public XmlVector3 Rotation { get; set; } = new();
    public int Lod { get; set; } = -1;
}

[XmlRoot("ItemPlacementList")]
public sealed class ItemPlacementListFile
{
    [XmlArray("Instances"), XmlArrayItem("Instance")]
    public List<XmlInstance> Instances { get; set; } = new();
}

public sealed class XmlCollider
{
    public int ID { get; set; }
    public string ModelName { get; set; } = "";
    public uint Flags { get; set; }
    public XmlVector3 Position { get; set; } = new();
    public XmlVector3 Scale { get; set; } = new(1, 1, 1);
    public XmlVector3 Rotation { get; set; } = new();
}

[XmlRoot("Colliders")]
public sealed class CollisionFile
{
    [XmlElement("Collider")]
    public List<XmlCollider> Colliders { get; set; } = new();
}

public static class XmlDocumentLoader
{
    private static readonly Dictionary<Type, XmlSerializer> Cache = new();

    private static XmlSerializer For<T>()
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(typeof(T), out var s))
            {
                s = new XmlSerializer(typeof(T));
                Cache[typeof(T)] = s;
            }
            return s;
        }
    }

    public static T Load<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Weld.World: XML file not found: {path}", path);
        using var fs = File.OpenRead(path);
        return Load<T>(fs, path);
    }

    public static T Load<T>(Stream stream, string? displayName = null)
    {
        var settings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true, DtdProcessing = DtdProcessing.Prohibit };
        using var reader = XmlReader.Create(stream, settings);
        try
        {
            return (T)For<T>().Deserialize(reader)!;
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException($"Weld.World: failed to parse {typeof(T).Name} from '{displayName ?? "<stream>"}': {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    public static void Save<T>(T value, string path)
    {
        using var fs = File.Create(path);
        using var writer = XmlWriter.Create(fs, new XmlWriterSettings { Indent = true, IndentChars = "    " });
        For<T>().Serialize(writer, value);
    }
}

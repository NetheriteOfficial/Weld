using System.Numerics;
using Weld.World.Data;
using Weld.World.Streaming;

namespace Weld.World;

public sealed class ItemDefinition
{
    internal ItemDefinition(XmlObjectDefinition xml, string sourceFile)
    {
        Id = xml.ID;
        ModelName = xml.ModelName;
        DffFile = string.IsNullOrWhiteSpace(xml.DffFile) ? xml.ModelName : xml.DffFile;
        TextureDictionary = xml.TextureDictionary;
        DrawDistance = xml.DrawDistance;
        Flags = xml.Flags;
        LocalBounds = xml.BoundingBox?.ToBoundingBox() ?? new BoundingBox(new Vector3(-1), new Vector3(1));
        LodModelName = string.IsNullOrWhiteSpace(xml.LodModel) ? null : xml.LodModel;
        SourceFile = sourceFile;
    }

    public bool IsLod { get; internal set; }
    public string? LodModelName { get; }

    public int Id { get; }
    public string ModelName { get; }
    public string DffFile { get; }
    public string TextureDictionary { get; }
    public float DrawDistance { get; }
    public uint Flags { get; }
    public BoundingBox LocalBounds { get; }
    public string SourceFile { get; }

    public StreamedModel Model { get; internal set; } = null!;

    public IReadOnlyList<WorldInstance> Instances => _instances;
    internal readonly List<WorldInstance> _instances = new();

    public bool IsResident => Model.State == StreamingState.Resident;

    public override string ToString() => $"#{Id} {ModelName} ({DffFile} / {TextureDictionary}) dd={DrawDistance}";
}

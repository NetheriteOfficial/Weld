using System.Numerics;

namespace Weld.World;

public readonly record struct BoundingBox(Vector3 Min, Vector3 Max)
{
    public static readonly BoundingBox Empty = new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Extents => (Max - Min) * 0.5f;
    public Vector3 Size => Max - Min;
    public bool IsValid => Min.X <= Max.X && Min.Y <= Max.Y && Min.Z <= Max.Z;

    public float BoundingRadius => Extents.Length();

    public BoundingBox Encapsulate(Vector3 point) =>
        new(Vector3.Min(Min, point), Vector3.Max(Max, point));

    public BoundingBox Encapsulate(in BoundingBox other) =>
        new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X &&
        p.Y >= Min.Y && p.Y <= Max.Y &&
        p.Z >= Min.Z && p.Z <= Max.Z;

    public bool Intersects(in BoundingBox o) =>
        Min.X <= o.Max.X && Max.X >= o.Min.X &&
        Min.Y <= o.Max.Y && Max.Y >= o.Min.Y &&
        Min.Z <= o.Max.Z && Max.Z >= o.Min.Z;

    public float DistanceSquared(Vector3 p)
    {
        var c = Vector3.Clamp(p, Min, Max);
        return Vector3.DistanceSquared(c, p);
    }

    public float Distance(Vector3 p) => MathF.Sqrt(DistanceSquared(p));

    public BoundingBox Transform(in Matrix4x4 m)
    {
        var center = Vector3.Transform(Center, m);
        var e = Extents;
        var ax = new Vector3(MathF.Abs(m.M11), MathF.Abs(m.M12), MathF.Abs(m.M13)) * e.X;
        var ay = new Vector3(MathF.Abs(m.M21), MathF.Abs(m.M22), MathF.Abs(m.M23)) * e.Y;
        var az = new Vector3(MathF.Abs(m.M31), MathF.Abs(m.M32), MathF.Abs(m.M33)) * e.Z;
        var ne = ax + ay + az;
        return new BoundingBox(center - ne, center + ne);
    }

    public static BoundingBox FromPoints(IEnumerable<Vector3> points)
    {
        var b = Empty;
        foreach (var p in points) b = b.Encapsulate(p);
        return b;
    }

    public override string ToString() => $"[{Min} .. {Max}]";
}

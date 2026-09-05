using System.Numerics;

namespace Weld.World;

public readonly struct Frustum
{
    public readonly Plane Left, Right, Bottom, Top, Near, Far;

    public Frustum(Plane left, Plane right, Plane bottom, Plane top, Plane near, Plane far)
    {
        Left = left; Right = right; Bottom = bottom; Top = top; Near = near; Far = far;
    }

    public static Frustum FromViewProjection(in Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);

        return new Frustum(
            Normalize(c4 + c1),
            Normalize(c4 - c1),
            Normalize(c4 + c2),
            Normalize(c4 - c2),
            Normalize(c4 + c3),
            Normalize(c4 - c3)
        );
    }

    private static Plane Normalize(Vector4 v)
    {
        var n = new Vector3(v.X, v.Y, v.Z);
        var len = n.Length();
        if (len <= float.Epsilon) return new Plane(Vector3.UnitZ, 0);
        return new Plane(n / len, v.W / len);
    }

    public bool Contains(Vector3 point)
    {
        return Signed(Left, point) >= 0 && Signed(Right, point) >= 0 &&
               Signed(Bottom, point) >= 0 && Signed(Top, point) >= 0 &&
               Signed(Near, point) >= 0 && Signed(Far, point) >= 0;
    }

    public bool IntersectsSphere(Vector3 center, float radius)
    {
        return Signed(Left, center) >= -radius && Signed(Right, center) >= -radius &&
               Signed(Bottom, center) >= -radius && Signed(Top, center) >= -radius &&
               Signed(Near, center) >= -radius && Signed(Far, center) >= -radius;
    }

    public bool Intersects(in BoundingBox box)
    {
        return !Outside(Left, box) && !Outside(Right, box) &&
               !Outside(Bottom, box) && !Outside(Top, box) &&
               !Outside(Near, box) && !Outside(Far, box);
    }

    private static bool Outside(in Plane plane, in BoundingBox box)
    {
        var n = plane.Normal;
        var p = new Vector3(
            n.X >= 0 ? box.Max.X : box.Min.X,
            n.Y >= 0 ? box.Max.Y : box.Min.Y,
            n.Z >= 0 ? box.Max.Z : box.Min.Z);
        return Signed(plane, p) < 0;
    }

    private static float Signed(in Plane plane, Vector3 p) => Vector3.Dot(plane.Normal, p) + plane.D;
}

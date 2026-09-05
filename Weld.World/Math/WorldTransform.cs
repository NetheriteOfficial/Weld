using System.Numerics;

namespace Weld.World;

public static class WorldTransform
{
    public const float BlenderZOffsetDegrees = 180f;
    private const float DegToRad = MathF.PI / 180f;

    public static Matrix4x4 Compose(Vector3 position, Vector3 rotationDegrees, Vector3 scale)
    {
        var r = rotationDegrees * DegToRad;
        return Matrix4x4.CreateScale(scale) *
               Matrix4x4.CreateRotationX(r.X) *
               Matrix4x4.CreateRotationY(r.Y) *
               Matrix4x4.CreateRotationZ((rotationDegrees.Z + BlenderZOffsetDegrees) * DegToRad) *
               Matrix4x4.CreateTranslation(position);
    }

    public static Quaternion Orientation(Vector3 rotationDegrees)
    {
        Matrix4x4.Decompose(Compose(Vector3.Zero, rotationDegrees, Vector3.One), out _, out var q, out _);
        return q;
    }
}

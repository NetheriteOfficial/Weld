using System.Numerics;

namespace Weld.World;

public readonly record struct StreamingCameraData(
    Vector3 Position,
    Vector3 Forward,
    Vector3 Up,
    float FieldOfView,
    float AspectRatio,
    float NearPlane,
    float FarPlane,
    float StreamingRadiusOverride = 0f,
    float DrawDistanceScale = 1f)
{
    public static StreamingCameraData Default => new(
        Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY,
        MathF.PI / 3f, 640f / 448f, 0.1f, 1000f);

    public static StreamingCameraData LookAt(Vector3 eye, Vector3 target, Vector3 up, float fov, float aspect, float near, float far)
        => new(eye, Vector3.Normalize(target - eye), up, fov, aspect, near, far);

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Position + Forward, Up);

    public Matrix4x4 ProjectionMatrix => Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, AspectRatio, NearPlane, FarPlane);

    public Matrix4x4 ViewProjectionMatrix => ViewMatrix * ProjectionMatrix;

    public Frustum BuildFrustum() => Frustum.FromViewProjection(ViewProjectionMatrix);

    public float EffectiveDrawDistance(float ideDrawDistance)
    {
        var d = StreamingRadiusOverride > 0 ? StreamingRadiusOverride : ideDrawDistance * DrawDistanceScale;
        return MathF.Min(d, FarPlane);
    }
}

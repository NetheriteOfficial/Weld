using System.Numerics;
using Weld.Entities;
using Weld.World;

namespace Weld.Carisle.Game;

internal sealed class PlayerCamera
{
    public float FieldOfView { get; set; } = MathF.PI / 3f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 1200f;
    public float ThirdPersonDistance { get; set; } = 0f;
    public Vector3 Position { get; private set; }
    public Vector3 Forward { get; private set; } = Vector3.UnitY;

    public void Update(PlayerEntity? player, float yaw, float pitch, float alpha, GameWorld world)
    {
        Forward = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
        if (player == null)
        {
            Position = world.Level.WorldBounds.Center + new Vector3(0, -80, 40);
            return;
        }
        var eye = player.InterpolatedPosition(alpha) + new Vector3(0, 0, player.EyeHeight);
        Position = ThirdPersonDistance > 0 ? eye - Forward * ThirdPersonDistance : eye;
    }

    public StreamingCameraData ToStreamingCamera(float aspect) =>
        new(Position, Forward, Vector3.UnitZ, FieldOfView, aspect, NearPlane, FarPlane);
}

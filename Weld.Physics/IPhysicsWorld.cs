using System.Numerics;
using Weld.World;
using Weld.World.Collision;

namespace Weld.Physics;

public interface IPhysicsBody : IDisposable
{
    Vector3 Position { get; set; }
    Quaternion Orientation { get; set; }
    Vector3 LinearVelocity { get; set; }
    Vector3 AngularVelocity { get; set; }
    bool Awake { get; set; }
    float Mass { get; }
    bool IsAlive { get; }
    void ApplyImpulse(Vector3 impulse);
}

public readonly record struct RayHit(Vector3 Point, Vector3 Normal, float Distance, SurfaceType Surface, IPhysicsBody? Body);

public interface IPhysicsWorld : IDisposable
{
    Vector3 Gravity { get; }
    int StaticCount { get; }
    int BodyCount { get; }
    void Step(float dt);
    int AddLevelColliders(Level level);
    IPhysicsBody AddDynamicBox(Vector3 position, Quaternion orientation, Vector3 size, float mass, float friction = 0.8f);
    IPhysicsBody AddCharacterCapsule(Vector3 position, float radius, float height, float mass);
    bool RayCast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit, IPhysicsBody? ignore = null);
}

using Weld.Carisle.Formats;
using Weld.Entities;
using Weld.Physics.Bepu;
using Weld.World;
using Weld.World.Assets;

namespace Weld.Carisle.Game;

public static class GameBootstrap
{
    public static AssetFormatRegistry CreateFormats() => new AssetFormatRegistry()
        .AddModelFormat(new AssimpFbxModelFormat(".fbx"))
        .AddTextureDictionaryFormat(new PngTextureDictionaryFormat("txd"))
        .AddCollisionFormat(new AssimpCollisionFormat(".fbx"));

    public static Level LoadLevel(AppOptions options, IWorldLog log)
    {
        var levelOptions = options.Ps2Streaming ? LevelOptions.Ps2Harsh() : LevelOptions.Modern();
        if (options.Role == NetRole.DedicatedServer)
        {
            levelOptions.SynchronousLoading = true;
            levelOptions.MemoryBudgetBytes = 0;
        }
        return Level.LoadFromExeDirectory(options.DatPath, CreateFormats(), levelOptions, log);
    }

    public static GameWorld CreateWorld(Level level, NetRole role, IWorldLog log)
    {
        var physics = new BepuPhysicsWorld { Log = log };
        return new GameWorld(level, role, physics, EntityTypeRegistry.CreateDefault(), log);
    }

    public static void SpawnDemoProps(GameWorld world, int count = 8)
    {
        var origin = world.FindSpawnPoint() + new System.Numerics.Vector3(3, 0, 4);
        var rng = new Random(7);
        for (var i = 0; i < count; i++)
        {
            var prop = new PropEntity
            {
                Size = new System.Numerics.Vector3(0.6f + rng.NextSingle() * 0.8f, 0.6f + rng.NextSingle() * 0.8f, 0.6f + rng.NextSingle() * 0.8f),
                Mass = 5f + rng.NextSingle() * 20f,
                Color = new System.Numerics.Vector4(0.4f + rng.NextSingle() * 0.6f, 0.4f + rng.NextSingle() * 0.6f, 0.4f + rng.NextSingle() * 0.6f, 1f),
            };
            world.Spawn(prop, origin + new System.Numerics.Vector3(rng.NextSingle() * 4 - 2, rng.NextSingle() * 4 - 2, i * 1.5f));
        }
    }
}

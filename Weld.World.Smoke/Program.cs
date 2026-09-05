using System.Numerics;
using Weld.Entities;
using Weld.Entities.Net;
using Weld.Physics;
using Weld.World;
using Weld.World.Assets;
using Weld.World.Collision;
using Weld.World.Streaming;

var sourceData = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../GameData"));
var gameRoot = Path.Combine(Path.GetTempPath(), "weld-smoke-" + Guid.NewGuid().ToString("N"));
CopyTree(Path.Combine(sourceData, "data"), Path.Combine(gameRoot, "data"));
var img = Path.Combine(gameRoot, "models", "carringtonisland_img");
Directory.CreateDirectory(Path.Combine(img, "generictxd"));
foreach (var n in new[] { "00", "01", "02" }) File.WriteAllText(Path.Combine(img, $"c_carisle_mid_seabed_{n}.fbx"), "placeholder");
File.WriteAllText(Path.Combine(img, "generictxd", "texture.png"), "placeholder");

static void CopyTree(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
    foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
}
var log = new ConsoleWorldLog { Minimum = WorldLogLevel.Debug };
var failures = 0;
void Check(bool cond, string what)
{
    Console.WriteLine($"  [{(cond ? "PASS" : "FAIL")}] {what}");
    if (!cond) failures++;
}

var formats = new AssetFormatRegistry()
    .AddModelFormat(new StubModelFormat())
    .AddTextureDictionaryFormat(new StubTxdFormat());

Console.WriteLine("Archive mapping:");
Check(DirectoryArchive.ArchivePathToDirectory("models/carringtonisland.img") == "models/carringtonisland_img", "models/carringtonisland.img -> models/carringtonisland_img");
Check(DirectoryArchive.ArchivePathToDirectory("a.b/c.d.img") == "a.b/c_d_img", "directory part untouched, every dot in file name replaced");

Console.WriteLine("\nSynchronous streaming:");
using (var level = Level.Load(gameRoot, "data/carringtonisland.dat", formats, new LevelOptions { SynchronousLoading = true }, log))
{
    var resident = new List<string>();
    level.ModelResident += m => resident.Add(m.Name);
    level.TextureDictionaryResident += t => resident.Add("txd:" + t.Name);

    Check(level.Definitions.Count == 3, "3 definitions parsed from IDE");
    Check(level.Instances.Count == 3, "3 instances parsed from IPL");
    Check(level.Archives.Count == 1 && ((DirectoryArchive)level.Archives[0]).Exists, "archive directory found");
    Check(level.Colliders.Count == 7 && level.Colliders.All(c => c.State == StreamingState.Unloaded), "7 colliders parsed from .col, meshes skipped (no ICollisionFormat registered)");

    var inst0 = level.Instances[0];
    Check(inst0.WorldBounds.Contains(inst0.Position), "instance 0 world bounds contain its position");

    var cam = new StreamingCameraData(
        Position: inst0.Position + new Vector3(0, 0, 60),
        Forward: -Vector3.UnitZ, Up: Vector3.UnitY,
        FieldOfView: MathF.PI / 3f, AspectRatio: 640f / 448f, NearPlane: 0.1f, FarPlane: 1000f);

    level.Update(cam);
    Console.WriteLine("  " + level.Stats);
    Check(level.Stats.ModelsResident == 3, "all 3 models resident after first synchronous Update (all within 500m draw distance)");
    Check(resident.Count(s => s.StartsWith("txd:")) == 1, "shared 'generic' TXD loaded exactly once");
    Check(resident.IndexOf("txd:generic") < resident.FindIndex(s => !s.StartsWith("txd:")), "TXD announced before the first model");
    Check(inst0.IsVisible && inst0.StreamFade == 0f && !level.VisibleInstances.Contains(inst0), "instance directly below the camera is visible but starts its stream-in fade at 0");
    Thread.Sleep(400); level.Update(cam);
    Check(level.VisibleInstances.Contains(inst0) && inst0.Opacity == 1f, "…and is drawn solid once the fade completes");
    Check(level.VisibleInstances.Count < 3, "frustum culls at least one of the instances that are off to the side");

    level.Update(cam with { Forward = Vector3.UnitZ });
    Check(level.VisibleInstances.Count == 0, "looking straight up: 0 drawable");
    Check(level.Stats.InstancesInRange == 3, "still 3 in range");
    Check(level.Stats.ModelsResident == 3, "no eviction while in range");

    level.Options.FrustumCulling = false;
    level.Update(cam with { Forward = Vector3.UnitZ });
    Check(level.VisibleInstances.Count == 3, "frustum culling disabled: all 3 in-range instances drawable");
    level.Options.FrustumCulling = true;

    level.Update(cam with { DrawDistanceScale = 0.13f });
    Check(level.Stats.InstancesInRange == 1, $"DrawDistanceScale 0.13 -> 1 instance in range (got {level.Stats.InstancesInRange})");

    var evicted = 0;
    level.ModelEvicted += _ => evicted++;
    level.Options.UnloadDelaySeconds = 0.05;
    var far = cam with { Position = new Vector3(5000, 5000, 5000) };
    level.Update(far);
    Thread.Sleep(80);
    level.Update(far);
    Check(evicted == 3 && level.Stats.ModelsResident == 0, $"all models evicted after unload delay (evicted={evicted})");
    level.Update(far);
    Check(level.Stats.TextureDictionariesResident == 0, "TXD evicted once unreferenced");

    level.Update(cam);
    Check(level.Stats.ModelsResident == 3, "models reload when camera returns");
}

Console.WriteLine("\nAsynchronous streaming:");
using (var level = Level.Load(gameRoot, "data/carringtonisland.dat", formats, new LevelOptions { MaxConcurrentLoads = 2, MaxCompletionsPerFrame = 1 }, log))
{
    var cam = StreamingCameraData.LookAt(level.Instances[0].Position + new Vector3(0, 0, 60), level.Instances[0].Position, Vector3.UnitY, MathF.PI / 3, 640f / 448f, 0.1f, 1000f);
    var frames = 0;
    while (level.Stats.ModelsResident < 3 && frames < 500)
    {
        level.Update(cam);
        frames++;
        Thread.Sleep(2);
    }
    Console.WriteLine("  " + level.Stats);
    Check(level.Stats.ModelsResident == 3, $"3 models streamed in asynchronously over {frames} frame(s)");
    Check(frames >= 3, "MaxCompletionsPerFrame=1 spread promotions over multiple frames");
}

Console.WriteLine("\nFailure handling:");
using (var level = Level.Load(gameRoot, "data/carringtonisland.dat", new AssetFormatRegistry().AddModelFormat(new StubModelFormat(".nope")), new LevelOptions { SynchronousLoading = true }, new ConsoleWorldLog { Minimum = WorldLogLevel.Error }))
{
    var failed = 0;
    level.ModelLoadFailed += (_, _) => failed++;
    level.Update(StreamingCameraData.Default with { Position = level.Instances[0].Position });
    Check(failed == 3 && level.Stats.ModelsFailed == 3, "unresolvable models end in Failed with an event each");
    Check(level.VisibleInstances.Count == 0, "failed models are not drawable");
}

Console.WriteLine("\nLOD + fades + budget (synthetic level):");
var lodRoot = Path.Combine(gameRoot, "lod");
Directory.CreateDirectory(Path.Combine(lodRoot, "data"));
Directory.CreateDirectory(Path.Combine(lodRoot, "models", "test_img", "generictxd"));
File.WriteAllText(Path.Combine(lodRoot, "models", "test_img", "generictxd", "texture.png"), "x");
foreach (var n in new[] { "tile_a", "tile_b", "lodtile_a", "far_rock" }) File.WriteAllText(Path.Combine(lodRoot, "models", "test_img", n + ".fbx"), "x");
File.WriteAllText(Path.Combine(lodRoot, "data", "test.dat"), """
<?xml version="1.0" encoding="UTF-8"?>
<Level>
  <ImgFiles><ImgFile>models/test.img</ImgFile></ImgFiles>
  <ItemDefinitions><ItemDefinition>data/test.ide</ItemDefinition></ItemDefinitions>
  <ItemPlacementLists><ItemPlacementList>data/test.ipl</ItemPlacementList></ItemPlacementLists>
</Level>
""");
File.WriteAllText(Path.Combine(lodRoot, "data", "test.ide"), """
<?xml version="1.0" encoding="UTF-8"?>
<ItemDefinition><Objects>
  <Object><ID>0</ID><ModelName>tile_a</ModelName><DffFile>tile_a</DffFile><TextureDictionary>generic</TextureDictionary><DrawDistance>100</DrawDistance><Flags>0</Flags>
    <BoundingBox><Min><X>-10</X><Y>-10</Y><Z>-1</Z></Min><Max><X>10</X><Y>10</Y><Z>1</Z></Max></BoundingBox></Object>
  <Object><ID>1</ID><ModelName>tile_b</ModelName><DffFile>tile_b</DffFile><TextureDictionary>generic</TextureDictionary><DrawDistance>100</DrawDistance><Flags>0</Flags>
    <BoundingBox><Min><X>-10</X><Y>-10</Y><Z>-1</Z></Min><Max><X>10</X><Y>10</Y><Z>1</Z></Max></BoundingBox></Object>
  <Object><ID>2</ID><ModelName>lodtile_a</ModelName><DffFile>lodtile_a</DffFile><TextureDictionary>generic</TextureDictionary><DrawDistance>600</DrawDistance><Flags>1</Flags>
    <BoundingBox><Min><X>-40</X><Y>-40</Y><Z>-1</Z></Min><Max><X>40</X><Y>40</Y><Z>1</Z></Max></BoundingBox></Object>
  <Object><ID>3</ID><ModelName>far_rock</ModelName><DffFile>far_rock</DffFile><TextureDictionary>generic</TextureDictionary><DrawDistance>1000</DrawDistance><Flags>0</Flags>
    <BoundingBox><Min><X>-1</X><Y>-1</Y><Z>-1</Z></Min><Max><X>1</X><Y>1</Y><Z>1</Z></Max></BoundingBox></Object>
</Objects></ItemDefinition>
""");
File.WriteAllText(Path.Combine(lodRoot, "data", "test.ipl"), """
<?xml version="1.0" encoding="UTF-8"?>
<ItemPlacementList><Instances>
  <Instance><ID>0</ID><ModelName>tile_a</ModelName><Interior>0</Interior><Position><X>0</X><Y>0</Y><Z>0</Z></Position><Scale><X>1</X><Y>1</Y><Z>1</Z></Scale><Rotation><X>0</X><Y>0</Y><Z>0</Z></Rotation></Instance>
  <Instance><ID>1</ID><ModelName>tile_b</ModelName><Interior>0</Interior><Position><X>20</X><Y>0</Y><Z>0</Z></Position><Scale><X>1</X><Y>1</Y><Z>1</Z></Scale><Rotation><X>0</X><Y>0</Y><Z>0</Z></Rotation></Instance>
  <Instance><ID>2</ID><ModelName>lodtile_a</ModelName><Interior>0</Interior><Position><X>10</X><Y>0</Y><Z>0</Z></Position><Scale><X>1</X><Y>1</Y><Z>1</Z></Scale><Rotation><X>0</X><Y>0</Y><Z>0</Z></Rotation></Instance>
  <Instance><ID>3</ID><ModelName>far_rock</ModelName><Interior>0</Interior><Position><X>0</X><Y>500</Y><Z>0</Z></Position><Scale><X>1</X><Y>1</Y><Z>1</Z></Scale><Rotation><X>0</X><Y>0</Y><Z>0</Z></Rotation></Instance>
</Instances></ItemPlacementList>
""");

using (var level = Level.Load(lodRoot, "data/test.dat", formats,
           new LevelOptions { SynchronousLoading = true, StreamInFadeSeconds = 0, LodFadeDistance = 20, FrustumCulling = false }, log))
{
    var a = level.Instances[0]; var b = level.Instances[1]; var lod = level.Instances[2]; var rock = level.Instances[3];
    Check(lod.IsLod && !a.IsLod && !rock.IsLod, "Flags=1 marks lodtile_a as LOD");
    Check(a.LodParent == lod && a.LodSource == LodPairingSource.NameConvention, $"tile_a paired to lodtile_a by name ({a.LodSource})");
    Check(b.LodParent == lod && b.LodSource == LodPairingSource.Spatial, $"tile_b paired to lodtile_a spatially ({b.LodSource})");
    Check(rock.LodParent == null && lod.LodChildren.Count == 2, "far_rock unpaired; LOD has 2 children");

    StreamingCameraData Cam(float z) => StreamingCameraData.Default with { Position = new Vector3(0, 0, z), Forward = -Vector3.UnitZ, FarPlane = 5000, AspectRatio = 640f / 448f };

    level.Update(Cam(30));
    Check(a.Opacity == 1f && !a.DitherInverted, "near: HD tile_a fully opaque");
    Check(!level.VisibleInstances.Contains(lod), "near: LOD not drawn when both children are fully covered");

    level.Update(Cam(91));
    var ta = a.LodBlend; var tb = b.LodBlend;
    Check(ta > 0.45f && ta < 0.55f, $"fade band: tile_a blend ≈ 0.5 (got {ta:F2})");
    Check(level.VisibleInstances.Contains(a) && level.VisibleInstances.Contains(lod), "fade band: both HD and LOD drawn (cross-fade)");
    Check(lod.DitherInverted && MathF.Abs(lod.Opacity - (1f - MathF.Min(ta, tb))) < 1e-4f, $"LOD opacity = 1 - min(child coverage) = {lod.Opacity:F2}, complementary dither");
    Check(level.Stats.InstancesFading == 3 && level.Stats.LodsDrawn == 1, "stats count 3 fading instances, 1 LOD");

    level.Update(Cam(200));
    Check(!level.VisibleInstances.Contains(a) && lod.Opacity == 1f && level.VisibleInstances.Contains(lod), "far: only the LOD, fully opaque");
    Check(rock.Opacity == 1f && rock.LodParent == null, "far_rock (no LOD) is drawn solid without any distance fade");

    level.Update(Cam(2000));
    Thread.Sleep(10);
    level.Options.UnloadDelaySeconds = 0; level.Update(Cam(2000)); level.Update(Cam(2000));
    Check(level.Stats.ModelsResident == 0, "everything evicted far away");
    level.Options.MaxCompletionsPerFrame = 0;
    level.Options.SynchronousLoading = false;
    level.Update(Cam(30));
    Check(!level.VisibleInstances.Contains(a) && level.Stats.InstancesWaitingForModel >= 1, "HD not yet resident: waiting counter set");
    level.Options.MaxCompletionsPerFrame = 10;
    for (var i = 0; i < 50 && level.Stats.ModelsResident < 4; i++) { level.Update(Cam(30)); Thread.Sleep(2); }
    Check(level.Stats.ModelsResident == 4, "everything streams back in asynchronously");

    level.Options.StreamInFadeSeconds = 0.2;
    level.Options.SynchronousLoading = true;
    level.Update(Cam(2000)); Thread.Sleep(5); level.Update(Cam(2000)); level.Update(Cam(2000));
    level.Update(Cam(30));
    var o0 = a.Opacity;
    Thread.Sleep(100); level.Update(Cam(30));
    var o1 = a.Opacity;
    Thread.Sleep(150); level.Update(Cam(30));
    var o2 = a.Opacity;
    Check(o0 < 0.15f && o1 > o0 && o1 < 1f && o2 == 1f, $"stream-in fade ramps {o0:F2} -> {o1:F2} -> {o2:F2}");
    Check(lod.Opacity > 0f || o2 == 1f, "LOD keeps covering while the HD fades in");

    level.Options.StreamInFadeSeconds = 0;
    level.Options.MemoryBudgetBytes = (long)(2.5 * 1024 * 1024);
    level.Options.BudgetProtectSeconds = 0;
    level.Update(Cam(30));
    Console.WriteLine("  " + level.Stats);
    Check(level.ResidentBytes <= level.Options.MemoryBudgetBytes, $"budget enforced: {level.ResidentBytes / 1024} KB resident");
    Check(level.Stats.BudgetEvictions >= 1, $"{level.Stats.BudgetEvictions} budget eviction(s) this frame");
    Check(rock.Definition.Model.State != StreamingState.Resident, "farthest wanted model (far_rock, 500 m) was the one sacrificed");
    Check(a.Definition.Model.State == StreamingState.Resident, "nearest model kept");
}

Console.WriteLine("\nPS2 preset:");
var ps2 = LevelOptions.Ps2Harsh();
Check(ps2.StreamInMargin == 0 && ps2.MaxConcurrentLoads == 1 && ps2.MemoryBudgetBytes > 0 && ps2.SimulatedLoadLatency.Max > 0, "Ps2Harsh: no preload margin, serial loads, budget, disc latency");
var live = new LevelOptions(); live.CopyFrom(ps2);
Check(live.SimulatedLoadLatency == ps2.SimulatedLoadLatency && live.LodFadeDistance == ps2.LodFadeDistance, "CopyFrom transfers the preset onto a live options object");

Console.WriteLine("\nBlender +180 Z rotation:");
{
    var m = WorldTransform.Compose(new Vector3(10, 20, 30), Vector3.Zero, Vector3.One);
    var p = Vector3.Transform(new Vector3(1, 2, 0), m);
    Check(Vector3.Distance(p, new Vector3(9, 18, 30)) < 1e-4f, $"rotation (0,0,0) maps local (1,2,0) to position + (-1,-2,0): {p}");
    var m90 = WorldTransform.Compose(Vector3.Zero, new Vector3(0, 0, 90), Vector3.One);
    var p90 = Vector3.Transform(Vector3.UnitX, m90);
    Check(Vector3.Distance(p90, new Vector3(0, -1, 0)) < 1e-4f, $"rotation Z=90 (+180) maps +X to -Y: {p90}");
    var q = WorldTransform.Orientation(new Vector3(0, 0, 90));
    var pq = Vector3.Transform(Vector3.UnitX, q);
    Check(Vector3.Distance(pq, p90) < 1e-4f, "quaternion orientation matches the matrix");
}

Console.WriteLine("\nCollision (.col) loading + surfaces:");
Check(SurfaceProperties.Parse("MAT_GRASS_wet") == SurfaceType.Grass && SurfaceProperties.Parse("rock_cliff") == SurfaceType.Rock && SurfaceProperties.Parse("Material.001") == SurfaceType.Default, "material names parse to surface types");
Check(SurfaceProperties.Of(SurfaceType.Water).Walkable == false && SurfaceProperties.Of(SurfaceType.Rock).Friction > SurfaceProperties.Of(SurfaceType.Sand).Friction, "surface table lookups");
var colImg = Path.Combine(gameRoot, "models", "carringtonisland_img");
foreach (var n in new[] { "col_carisle_mid_dirtpath_00", "col_carisle_mid_dirtpath_01", "col_carisle_mid_landmass_00", "col_carisle_mid_landmass_01", "col_carisle_mid_landmass_02", "col_carisle_mid_seabed_00", "col_carisle_mid_seabed_01" })
    File.WriteAllText(Path.Combine(colImg, n + ".fbx"), "placeholder");
var colFormats = new AssetFormatRegistry().AddModelFormat(new StubModelFormat()).AddTextureDictionaryFormat(new StubTxdFormat()).AddCollisionFormat(new StubCollisionFormat());
using (var level = Level.Load(gameRoot, "data/carringtonisland.dat", colFormats, new LevelOptions { SynchronousLoading = true }, log))
{
    Check(level.Colliders.Count == 7 && level.Colliders.All(c => c.State == StreamingState.Resident), "7 collider meshes loaded via ICollisionFormat from the archive");
    var c0 = level.Colliders[0];
    Check(c0.ModelName == "col_carisle_mid_dirtpath_00" && MathF.Abs(c0.RotationDegrees.Z + 90) < 1e-4f && c0.Flags == 1, "collider 0 fields parsed");
    Check(c0.Mesh != null && c0.Mesh.TriangleCount == 2 && c0.Mesh.TriangleSurfaces[0] == SurfaceType.Grass && c0.Mesh.TriangleSurfaces[1] == SurfaceType.Rock, "per-triangle surfaces from materials (GRASS, ROCK)");
    Check(c0.WorldBounds!.Value.Contains(c0.Position), "collider world bounds contain its position");
    var fake = new FlatGroundPhysics(-100);
    Check(fake.AddLevelColliders(level) == 7, "IPhysicsWorld receives all 7 colliders");
}

Console.WriteLine("\nEntity graph (60 Hz):");
using (var level = Level.Load(gameRoot, "data/carringtonisland.dat", formats, new LevelOptions { SynchronousLoading = true }, new ConsoleWorldLog { Minimum = WorldLogLevel.Warning }))
using (var world = new GameWorld(level, NetRole.Host, new FlatGroundPhysics(0f), log: new ConsoleWorldLog { Minimum = WorldLogLevel.Warning }))
{
    var counter = world.Spawn(new CounterEntity(), Vector3.Zero);
    Check(world.Entities.Count == 1 && counter.IsSpawned && counter.SpawnCalls == 1, "spawn adds and calls OnSpawn");
    var steps = 0;
    for (var i = 0; i < 60; i++) steps += world.Advance(1.0 / 60 + 1e-9);
    Check(steps == 60 && world.Entities.Tick == 60 && counter.Ticks == 60, $"60 frames of 1/60 s = 60 fixed ticks ({steps})");
    world.Advance(1.0 / 120);
    Check(world.Entities.Alpha > 0.4f && world.Entities.Alpha < 0.6f, $"sub-tick time accumulates (alpha ≈ {world.Entities.Alpha:F2})");
    Check(world.Advance(10.0) == world.Entities.MaxCatchUpTicks, $"catch-up clamped to {world.Entities.MaxCatchUpTicks} ticks");
    counter.SpawnChildOnNextTick = true;
    world.Entities.Step();
    Check(world.Entities.Count == 2 && world.Entities.OfType<CounterEntity>().Count() == 2, "entity spawned during a tick is inserted at the end of that tick");
    counter.Remove();
    world.Entities.Step();
    Check(world.Entities.Count == 1 && counter.IsRemoved && counter.RemoveCalls == 1, "Remove() schedules OnRemove");

    var player = world.SpawnPlayer(0, "tester", new Vector3(0, 0, 5f));
    world.LocalPlayer = player;
    Check(player.Body != null && player.IsLocal && player.IsAuthority && !player.IsRenderable, "host player owns a character body and is not rendered first-person");
    for (var i = 0; i < 120; i++) world.Entities.Step();
    Check(player.IsGrounded && MathF.Abs(player.Position.Z - 0.9f) < 0.05f && player.GroundSurface == SurfaceType.Grass, $"player falls onto the flat ground and reports the surface ({player.Position.Z:F2}, {player.GroundSurface})");
    player.SetInput(new PlayerInput { Move = new Vector2(0, 1), Yaw = 0f });
    for (var i = 0; i < 60; i++) world.Entities.Step();
    Check(player.Position.X > 3f && MathF.Abs(player.Position.Y) < 0.01f, $"forward input along yaw 0 moves +X ({player.Position.X:F2})");
    player.SetInput(new PlayerInput { Jump = true, Yaw = 0f });
    world.Entities.Step();
    Check(player.Velocity.Z > 4f, $"jump gives upward velocity ({player.Velocity.Z:F2})");
    Check(player.Dirty && Replication.DueForReplication(world).Contains(player), "moved player is dirty and due for replication");

    var prop = world.Spawn(new PropEntity { Size = new Vector3(1, 1, 1) }, new Vector3(5, 5, 3));
    for (var i = 0; i < 120; i++) world.Entities.Step();
    Check(prop.Position.Z < 0.6f && prop.Position.Z > 0.4f, $"prop rests on the ground ({prop.Position.Z:F2})");
    player.Damage(30f);
    Check(player.Health == 70f && !player.IsDead, "damage applies on authority");
    player.Kill();
    Check(player.IsDead, "kill");
}

Console.WriteLine("\nNet serialization + replication codec:");
{
    var w = new NetWriter();
    w.Write(true).Write((byte)7).Write((ushort)65000).Write(-5).Write(3.5f).Write("héllo").Write(new Vector3(1, 2, 3)).Write(Quaternion.Identity).WriteBytes(new byte[] { 1, 2, 3 });
    var r = new NetReader(w.ToArray());
    Check(r.ReadBool() && r.ReadByte() == 7 && r.ReadUShort() == 65000 && r.ReadInt() == -5 && r.ReadFloat() == 3.5f && r.ReadString() == "héllo" && r.ReadVector3() == new Vector3(1, 2, 3) && r.ReadQuaternion() == Quaternion.Identity && r.ReadBytes().SequenceEqual(new byte[] { 1, 2, 3 }) && r.Remaining == 0, "writer/reader round trip");
    var input = new PlayerInput { Sequence = 9, Move = new Vector2(0.5f, -1), Yaw = 1.2f, Pitch = -0.3f, Run = true, Jump = true };
    var back = Replication.DecodeInput(Replication.EncodeInput(input));
    Check(back.Sequence == 9 && back.Move == input.Move && back.Run && back.Jump && !back.Use, "PlayerInput round trip");
}
using (var level = Level.Load(gameRoot, "data/carringtonisland.dat", formats, new LevelOptions { SynchronousLoading = true }, new ConsoleWorldLog { Minimum = WorldLogLevel.Warning }))
using (var server = new GameWorld(level, NetRole.DedicatedServer, new FlatGroundPhysics(0f), log: new ConsoleWorldLog { Minimum = WorldLogLevel.Warning }))
using (var client = new GameWorld(level, NetRole.Client, new FlatGroundPhysics(0f), log: new ConsoleWorldLog { Minimum = WorldLogLevel.Warning }))
{
    client.LocalClientId = 2;
    var p1 = server.SpawnPlayer(1, "alice", new Vector3(0, 0, 2));
    var p2 = server.SpawnPlayer(2, "bob", new Vector3(4, 0, 2));
    var prop = server.Spawn(new PropEntity { Size = new Vector3(2, 1, 1), Color = new Vector4(1, 0, 0, 1) }, new Vector3(1, 1, 5));
    for (var i = 0; i < 30; i++) server.Entities.Step();

    var spawnChunks = Replication.EncodeSpawns(server.Entities.Networked, server.Types, chunkBytes: 80);
    Check(spawnChunks.Count >= 2, $"spawn snapshot chunked at small size into {spawnChunks.Count} message(s)");
    var spawned = spawnChunks.SelectMany(c => Replication.DecodeSpawns(c, client)).ToList();
    Check(spawned.Count == 3 && client.Entities.Count == 3, "client materialises 3 networked entities");
    var cp2 = client.Entities.FindNetworked(p2.NetworkId) as PlayerEntity;
    var cp1 = client.Entities.FindNetworked(p1.NetworkId) as PlayerEntity;
    var cprop = client.Entities.FindNetworked(prop.NetworkId) as PropEntity;
    Check(cp2 != null && cp2.Name == "bob" && cp2.IsLocallyControlled && cp2.Body != null, "owned player is locally controlled and simulates physics on the client");
    Check(cp1 != null && cp1.Name == "alice" && cp1.IsRemoteProxy && cp1.Body == null, "other player is a remote proxy without a body");
    Check(cprop != null && cprop.Size == new Vector3(2, 1, 1) && cprop.Color.X == 1f && cprop.IsRemoteProxy, "prop spawn payload carried size/colour");
    Check(spawnChunks.SelectMany(c => Replication.DecodeSpawns(c, client)).Count() == 0, "re-applying a spawn snapshot is idempotent");

    p1.SetInput(new PlayerInput { Move = new Vector2(0, 1), Yaw = 0f, Sequence = 1 });
    for (var i = 0; i < 60; i++) server.Entities.Step();
    var due = Replication.DueForReplication(server).ToList();
    Check(due.Contains(p1) && due.Contains(prop), "moved entities are due");
    var stateChunks = Replication.EncodeStates(server.Entities.Tick, due);
    var applied = stateChunks.Sum(c => Replication.DecodeStates(c, client));
    Check(applied == due.Count, $"{applied} state(s) applied on the client");
    Check(Vector3.Distance(cp1!.Position, p1.Position) < 0.01f, $"far-off proxy snapped to server position ({Vector3.Distance(cp1.Position, p1.Position):F3})");
    var before = cp1.Position;
    p1.SetInput(new PlayerInput { Move = new Vector2(0, 1), Yaw = 0f, Sequence = 2 });
    for (var i = 0; i < 6; i++) server.Entities.Step();
    foreach (var c in Replication.EncodeStates(server.Entities.Tick, Replication.DueForReplication(server).ToList())) Replication.DecodeStates(c, client);
    client.Entities.Step();
    Check(cp1.Position.X > before.X && cp1.Position.X <= p1.Position.X + 0.5f, "proxy interpolates towards the newer server state");

    prop.Remove();
    server.Entities.Step();
    var despawn = Replication.EncodeDespawns(new[] { prop.NetworkId });
    Check(Replication.DecodeDespawns(despawn, client) == 1 && client.Entities.FindNetworked(prop.NetworkId) == null, "despawn removes the proxy");
    var (cid, tick, pid) = Replication.DecodeWelcome(Replication.EncodeWelcome(2, 123, p2.NetworkId));
    Check(cid == 2 && tick == 123 && pid == p2.NetworkId, "welcome round trip");
}

try { Directory.Delete(gameRoot, true); } catch { }
Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;

sealed class StubModel : IModel
{
    public StubModel(string name) { Name = name; }
    public string Name { get; }
    public BoundingBox? Bounds => null;
    public long MemoryFootprint => 1024 * 1024;
    public void Dispose() { }
}

sealed class StubModelFormat : IModelFormat
{
    private readonly string _ext;
    public StubModelFormat(string ext = ".fbx") { _ext = ext; }
    public string Name => "Stub" + _ext;
    public IEnumerable<string> ResolveEntryNames(string assetName) { yield return assetName + _ext; }
    public IModel Load(AssetLoadContext context, AssetEntry entry)
    {
        Thread.Sleep(5);
        _ = entry.ReadAllBytes();
        return new StubModel(context.AssetName);
    }
}

sealed class StubTxd : ITextureDictionary
{
    public StubTxd(string name) { Name = name; }
    public string Name { get; }
    public long MemoryFootprint => 256 * 1024;
    public void Dispose() { }
}

sealed class StubTxdFormat : ITextureDictionaryFormat
{
    public string Name => "StubPngTxd";
    public IEnumerable<string> ResolveEntryNames(string assetName) { yield return $"{assetName}txd/texture.png"; }
    public ITextureDictionary Load(AssetLoadContext context, AssetEntry entry) => new StubTxd(context.AssetName);
}

sealed class StubCollisionFormat : ICollisionFormat
{
    public string Name => "StubCol";
    public IEnumerable<string> ResolveEntryNames(string assetName) { yield return assetName + ".fbx"; }
    public ICollisionModel Load(AssetLoadContext context, AssetEntry entry)
    {
        var verts = new[] { new Vector3(-50, -50, 0), new Vector3(50, -50, 0), new Vector3(50, 50, 0), new Vector3(-50, 50, 0) };
        var idx = new[] { 0, 1, 2, 0, 2, 3 };
        return new TriangleCollisionMesh(context.AssetName, verts, idx, new[] { SurfaceType.Grass, SurfaceType.Rock }, new[] { "GRASS", "ROCK" });
    }
}

sealed class CounterEntity : BaseEntity
{
    public int Ticks, SpawnCalls, RemoveCalls;
    public bool SpawnChildOnNextTick;
    public override void OnSpawn() => SpawnCalls++;
    public override void OnRemove() => RemoveCalls++;
    public override void Tick()
    {
        Ticks++;
        if (SpawnChildOnNextTick)
        {
            SpawnChildOnNextTick = false;
            World.Spawn(new CounterEntity(), Position + Vector3.UnitX);
        }
    }
}

sealed class FlatGroundPhysics : IPhysicsWorld
{
    private readonly List<FlatBody> _bodies = new();
    private readonly float _groundZ;
    public FlatGroundPhysics(float groundZ) { _groundZ = groundZ; }
    public Vector3 Gravity => new(0, 0, -9.81f);
    public int StaticCount { get; private set; }
    public int BodyCount => _bodies.Count;
    public int AddLevelColliders(Level level) { StaticCount += level.Colliders.Count(c => c.Mesh != null); return StaticCount; }
    public IPhysicsBody AddDynamicBox(Vector3 position, Quaternion orientation, Vector3 size, float mass, float friction = 0.8f) => Add(position, orientation, size.Z * 0.5f, mass);
    public IPhysicsBody AddCharacterCapsule(Vector3 position, float radius, float height, float mass) => Add(position, Quaternion.Identity, height * 0.5f, mass);
    private FlatBody Add(Vector3 p, Quaternion q, float halfHeight, float mass) { var b = new FlatBody(this, p, q, halfHeight, mass); _bodies.Add(b); return b; }
    private void Remove(FlatBody b) => _bodies.Remove(b);
    public void Step(float dt)
    {
        foreach (var b in _bodies)
        {
            b.LinearVelocity += Gravity * dt;
            b.Position += b.LinearVelocity * dt;
            var floor = _groundZ + b.HalfHeight;
            if (b.Position.Z < floor) { b.Position = new Vector3(b.Position.X, b.Position.Y, floor); b.LinearVelocity = new Vector3(b.LinearVelocity.X, b.LinearVelocity.Y, MathF.Max(0, b.LinearVelocity.Z)); }
        }
    }
    public bool RayCast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit hit, IPhysicsBody? ignore = null)
    {
        hit = default;
        if (direction.Z >= 0) return false;
        var t = (_groundZ - origin.Z) / direction.Z;
        if (t < 0 || t > maxDistance) return false;
        hit = new RayHit(origin + direction * t, Vector3.UnitZ, t, SurfaceType.Grass, null);
        return true;
    }
    public void Dispose() { }

    sealed class FlatBody : IPhysicsBody
    {
        private readonly FlatGroundPhysics _w;
        public FlatBody(FlatGroundPhysics w, Vector3 p, Quaternion q, float hh, float mass) { _w = w; Position = p; Orientation = q; HalfHeight = hh; Mass = mass; }
        public float HalfHeight;
        public Vector3 Position { get; set; }
        public Quaternion Orientation { get; set; }
        public Vector3 LinearVelocity { get; set; }
        public Vector3 AngularVelocity { get; set; }
        public bool Awake { get; set; } = true;
        public float Mass { get; }
        public bool IsAlive { get; private set; } = true;
        public void ApplyImpulse(Vector3 impulse) => LinearVelocity += impulse / Mass;
        public void Dispose() { IsAlive = false; _w.Remove(this); }
    }
}

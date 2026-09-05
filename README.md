# Weld

```
Weld.sln
├── Weld.World/          asset pipeline: .dat/.ide/.ipl/.col, archives, formats API, streaming, LOD, culling. No engine deps.
├── Weld.Physics/        physics abstraction (IPhysicsWorld/IPhysicsBody, RayHit) + generic CharacterBody controller
├── Weld.Physics.Bepu/   BepuPhysics 2.4 backend: static meshes from .col colliders, per-triangle surfaces, capsules, boxes
├── Weld.Entities/       Minecraft-style OOP entities, 60 Hz EntityGraph, GameWorld, transport-agnostic replication codec
├── Weld.Networking/     Riptide GameServer / GameClient (spawn, despawn, state, input)
├── Weld.Carisle/        the single game EXE: host / dedicated server / client, Silk.NET renderer, Assimp formats, player controller
├── Weld.World.Smoke/    headless self-test (87 checks) for World, Entities, replication, collision loading
└── GameData/            data/ tree copied next to Weld.Carisle.exe at build time
```

## Running the single EXE

```
Weld.Carisle                      host: local player + listen server on UDP 7777
Weld.Carisle --host 7777          same, explicit port
Weld.Carisle --server 7777        dedicated headless server (60 Hz loop, no window)
Weld.Carisle --connect 1.2.3.4    client; port defaults to 7777
Weld.Carisle --name Bob --modern  player name; --modern disables the PS2 streaming profile
```

Controls: WASD, mouse look, Space jump, Shift run, E throw a physics prop (host), R respawn (host), Tab release mouse,
F1 wireframe, F2 upscale filter, F3 aspect, F4 frustum culling, F5 PS2/modern streaming, F6 dither fades, F7 third person.

## Entities

`BaseEntity` owns the lifecycle and every function an entity can override: `OnSpawn`, `Tick` (fixed 60 Hz, before
physics), `PostPhysicsTick`, `OnRemove`, plus `LocalBounds`, `DebugColor`, `IsRenderable`, `WorldMatrix`.
`NetworkedEntity` adds `NetworkId`, `OwnerClientId`, dirty tracking, `WriteSpawn/ReadSpawn`, `WriteState/ReadState`
and remote-proxy interpolation. `LivingEntity` adds health/damage/death events. `PlayerEntity` drives a `CharacterBody`
from `PlayerInput` (server-authoritative, locally predicted for the owning client). `PropEntity` is a networked
dynamic box. `EntityTypeRegistry` maps type ids ↔ factories so the client can materialise spawns.

`EntityGraph.Advance(realDt)` accumulates time and runs `Step()` at exactly 60 Hz (clamped catch-up); `Alpha` gives
the render interpolation factor. Every entity ticks, then the physics world steps, then `PostPhysicsTick` runs.

## Networking

The server owns all state. On connect it sends a spawn snapshot, spawns a `PlayerEntity` owned by the client and sends
`Welcome`. Every tick it broadcasts reliable `Spawn`/`Despawn` deltas and unreliable, chunked `State` messages for
dirty entities (per-entity `ReplicationIntervalTicks`). Clients send `Input` every tick. The codec lives in
`Weld.Entities.Net.Replication` and is byte-array based, so it is tested without Riptide.

## Collision

The `.dat` `<CollisionFile>` is an XML `<Colliders>` list (ID, ModelName, Flags, Position, Scale, Rotation). Each
collider's mesh is `{ModelName}.fbx` inside the archive, loaded through `ICollisionFormat`. The FBX's material names
map to `SurfaceType` (GRASS, ROCK, DIRT, SAND, MUD, CONCRETE, ASPHALT, METAL, WOOD, WATER, GLASS, GRAVEL) per
triangle; `SurfaceProperties` gives friction, restitution, walkability and footstep sound. `BepuPhysicsWorld` bakes
scale/rotation into a static `Mesh` per collider and answers `RayCast` with the surface under the hit triangle, which
`CharacterBody` exposes as `GroundSurface`.

Every model matrix (IPL instances, colliders) applies `RotationDegrees.Z + 180` (`WorldTransform.BlenderZOffsetDegrees`)
to match the Blender export workflow.

## Runtime layout (relative to Weld.Carisle.exe)

```
./Weld.Carisle.exe
./data/carringtonisland.dat                      <Level> manifest
./data/maps/CARISLEmid/CARISLEmid.ide            <ItemDefinition>
./data/maps/CARISLEmid/CARISLEmid.ipl            <ItemPlacementList>
./data/maps/CARISLEmid/CARISLEmid.col            listed in the .dat; skipped (with an Info log) until an ICollisionFormat is registered
./models/carringtonisland_img/                   "models/carringtonisland.img" → '.' replaced by '_'
      c_carisle_mid_seabed_00.fbx                <DffFile> + ".fbx"
      generictxd/texture.png                     <TextureDictionary> + "txd" + "/texture.png"
```

## Weld.World in one screen

```csharp
var formats = new AssetFormatRegistry()          // the EXE decides what a model/TXD *is*
    .AddModelFormat(new AssimpFbxModelFormat())
    .AddTextureDictionaryFormat(new PngTextureDictionaryFormat());

var level = Level.LoadFromExeDirectory("data/carringtonisland.dat", formats, new LevelOptions(), new ConsoleWorldLog());

level.ModelResident  += m => /* upload m.Model to the GPU (main thread) */;
level.ModelEvicted   += m => /* free it */;

// every frame:
var cam = new StreamingCameraData(position, forward, up, fovRadians, 640f/448f, near, far);
level.Update(cam);                                // streaming + culling
foreach (var inst in level.VisibleInstances)      // in range, in frustum, model resident
    Draw(inst.Definition.Model, inst.WorldMatrix);
```

* `StreamingCameraData` is a `record struct` (System.Numerics) — build it from any windowing layer.
  It also gives you `ViewMatrix`, `ProjectionMatrix`, `BuildFrustum()` and `EffectiveDrawDistance()`.
* Streaming: a model is requested when any instance is within `DrawDistance + StreamInMargin`,
  loaded on a worker thread (priority = distance, `MaxConcurrentLoads` in flight), promoted to
  Resident on the thread that calls `Update` (`MaxCompletionsPerFrame` per frame), and evicted after
  `UnloadDelaySeconds` once every instance is beyond `DrawDistance + StreamInMargin + StreamOutHysteresis`.
  TXDs are loaded once and shared; they are evicted when no resident model references them.
* Culling: AABB (IDE `<BoundingBox>` transformed by the IPL transform) vs. distance and the six frustum planes.
* Extending formats: implement `IModelFormat` / `ITextureDictionaryFormat` / `ICollisionFormat`.
  `ResolveEntryNames("c_carisle_mid_seabed_00")` returns candidate archive entries (`"…​.fbx"`); `Load` runs on a
  worker thread and must not touch the GPU. `IAssetArchive` can be implemented for real packed `.img` files;
  `DirectoryArchive` is the loose-folder version used now.
* `LevelOptions.SynchronousLoading = true` makes everything deterministic for tools/tests.

### LOD, fades and the PS2 profile

* **LOD flag.** An IDE object with `<Flags>1</Flags>` (`LevelOptions.LodFlagMask`) is a LOD stand-in. Each HD
  instance is linked to one LOD instance at load time, in this order: IPL `<Lod>index</Lod>` (SA style) →
  IDE `<LodModel>name</LodModel>` → name convention (`lodfoo`, `lod_foo`, `foo_lod`, `foolod`, SA-style
  `lod` + name[3..]) → nearest LOD whose bounds contain the HD position. `WorldInstance.LodParent /
  LodChildren / LodSource` expose the result.
* **Cross-fade.** Over the last `LodFadeDistance` units before an HD object's draw distance its `LodBlend` goes
  1 → 0; the LOD's blend is `1 - min(children coverage)` so it stays until every HD part is in.
  `WorldInstance.Opacity = LodBlend × StreamFade`; the LOD gets `DitherInverted = true`. Rendered as an
  ordered-dither screen door with the complementary Bayer phase, HD and LOD cover exactly disjoint pixels:
  no blending, no double coverage, depth stays correct.
* **Stream-in fade.** `StreamFade` ramps 0 → 1 over `StreamInFadeSeconds` after a model becomes resident.
* **`LevelOptions.Ps2Harsh()`** — San Andreas on a PS2: `StreamInMargin = 0` (nothing preloads; things appear
  when they enter range), one load at a time behind a simulated 0.12–0.55 s DVD seek
  (`SimulatedLoadLatency`), a 48 MB `MemoryBudgetBytes` with LRU eviction — and when that isn't enough, the
  farthest *wanted* models are dropped too — plus a 0.75 s unload delay and fast dithered fades.
  `LevelOptions.Modern()` is the comfortable alternative; `Options.CopyFrom(preset)` swaps on a live level (F5).
* `IModel.MemoryFootprint` / `ITextureDictionary.MemoryFootprint` feed the budget; `StreamingStats` reports
  `ResidentBytes`, `BudgetEvictions`, `LodsDrawn` and `InstancesFading`.

## Weld.Carisle: RenderPipeline

`RenderPipeline.RenderFrame(cam, windowW, windowH)` = `BeginFrame()` (bind the 640x448 FBO, clear) →
`RenderLevel(cam)` (draw `Level.VisibleInstances` sorted by model with a lit/textured/fogged shader) →
`Present(w, h)` (fullscreen-triangle upscale: `Nearest`, `Linear`, or `SharpBilinear`, optional letterboxing;
`UseBlitPresent = true` restores the original `BlitNamedFramebuffer` path).
`GpuResourceCache` listens to the Level events and owns all VAOs/textures, so Weld.World never sees OpenGL.

Keys: WASD/QE move, RMB look, Shift fast, F1 wireframe, F2 cycle upscale filter, F3 aspect, F4 frustum culling,
F5 PS2-harsh ↔ modern streaming, F6 dithered fades on/off.

## Building

```
dotnet build Weld.sln
dotnet run --project Weld.World.Smoke      # headless self-test, prints ALL CHECKS PASSED
dotnet run --project Weld.Carisle          # needs assets in GameData/models/carringtonisland_img/
```

Note: Weld.World, Weld.Physics, Weld.Entities and the smoke test were built and run here. Weld.Physics.Bepu
(BepuPhysics 2.4.0), Weld.Networking (RiptideNetworking.Riptide 2.2.0) and Weld.Carisle (Silk.NET 2.21,
AssimpNet 5.0.0-beta1, StbImageSharp 2.30.15) were compiler-checked against the BCL only, since this environment
could not reach NuGet.

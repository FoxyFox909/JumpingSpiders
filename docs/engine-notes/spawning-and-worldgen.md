> Reference notes on Vintage Story 1.22.7 internals, gathered from the game sources when this mod was started.
> They describe the engine, not the mod's current design (see `CLAUDE.md` and `README.md` for that). File paths in
> them are from the machine they were written on.

# Creature spawning and world generation in Vintage Story 1.22.7

Research notes for the JumpingSpiders mod (spider + "Silk Hammock" block). Everything here was checked against the 1.22.7 sources and assets on this machine. Where I could not check something, the text says so.

## Path aliases used below

| Alias | Path |
|---|---|
| `API` | `/home/user/anegostudios/vsapi` |
| `ESS` | `/home/user/anegostudios/vsessentialsmod` |
| `SURV` | `/home/user/anegostudios/vssurvivalmod` |
| `ASSETS` | `/home/user/vs-game/1.22.7/assets/survival` |
| `LIB` | `/tmp/claude-0/-home-user-JumpingSpiders/2e9b440f-b417-5ea5-83b3-c877c65a9d07/scratchpad/libfull`: an `ilspycmd 11.2.0.10050 -p` decompile of `/home/user/vs-game/1.22.7/VintagestoryLib.dll` (closed source). Line numbers under `LIB` refer to that decompiled output. |

**Correction to the brief.** In 1.22.7 the standard world generator, including `GenCreatures`, the tree generator and block patches, lives in **vsessentialsmod** (`ESS/Systems/WorldGen/Standard/...`), not in vssurvivalmod. vssurvivalmod contributes the block classes (`BlockLog`, `BlockMushroom`, `BEMycelium`), temporal stability and some spawn filters.

---

## 1. Entity JSON spawn conditions

### 1.1 Where they live and how they are deserialized

In the entity type JSON they go under `server.spawnconditions`. Variants such as `spawnconditionsByType` are resolved like any other `ByType` property before deserialization.

`ESS/Loading/EntityType.cs:497-500`:
```csharp
        /// The spawn conditions for the entity. Without this, the entity will not spawn anywhere.
        [JsonProperty]
        [DocumentAsJson("Recommended", "None")]
        public SpawnConditions SpawnConditions;
```
These are copied by reference into `EntityProperties.Server.SpawnConditions` (`ESS/Loading/EntityType.cs:280-286`).

`EntityProperties.Clone()` does **not** deep-copy them (`API/Common/Entity/EntityProperties.cs:609`, `SpawnConditions = SpawnConditions`). Changing them in code therefore changes them for every entity of the type. Vanilla relies on this in `SURV/Systems/Rifts/RiftWeather.cs:228`, which rewrites `Runtime.MaxQuantity` at runtime.

All classes are in `API/Common/Entity/SpawnConditions.cs`. Field names are case-insensitive in JSON (Newtonsoft).

#### `SpawnConditions` (lines 193-234)

| Field | Type |
|---|---|
| `Climate` | `ClimateSpawnCondition` |
| `Runtime` | `RuntimeSpawnConditions` |
| `Worldgen` | `WorldGenSpawnConditions` |

The `[OnDeserialized]` hook at lines 224-232 runs this:
```csharp
            if (Climate != null)
            {
                Runtime?.SetFrom(Climate);
                Worldgen?.SetFrom(Climate);
            }
```
`SetFrom` (lines 102-121) overwrites the following in **both** Runtime and Worldgen: MinTemp, MaxTemp, MinRain, MaxRain, MinForest, MaxForest, MinShrubs, MaxShrubs, MinY, MaxY, MinForestOrShrubs, MapCode, HabitatSize, and RandomMapCodePool (only if Climate's is non-null). These are copied even when you did not set them inside `climate`, in which case the defaults are copied. See Pitfalls.

#### `ClimateSpawnCondition` (lines 17-144): fields and defaults

| JSON field | Type | Default | Meaning / where checked |
|---|---|---|---|
| `minTemp` / `maxTemp` | float | -40 / 40 | Compared against `WorldGenTemperature` at runtime (`MatchesClimate`, line 125) |
| `minRain` / `maxRain` | float | 0 / 1 | Compared against `Rainfall` |
| `minForest` / `maxForest` | float | 0 / 1 | Compared against `ForestDensity` (`MatchesForestation`, line 132) |
| `minShrubs` / `maxShrubs` | float | 0 / 1 | Compared against `ShrubDensity` |
| `minY` / `maxY` | float | 0 / 2 | 0..1 = world bottom to sea level, 1..2 = sea level to world top |
| `minForestOrShrubs` | float | 0 | Must be <= max(forest, shrub) |
| `randomMapCodePool` | string[] | null | Picks one 2D "animal density map". Spawning is only allowed where that map is >= 128 of 255 |
| `habitatSize` | int | 2 | Only used for `Underwater`/`Sea` habitats. The doc attribute says "true", but the field is an int |

#### `BaseSpawnConditions : ClimateSpawnCondition` (lines 388-562)

| JSON field | Type | Default |
|---|---|---|
| `group` | string | null. Vanilla uses "hostile" / "neutral" / "passive" |
| `minLightLevel` / `maxLightLevel` | int | 0 / 32 |
| `lightLevelType` | `EnumLightLevelType` | `MaxLight` |
| `herdSize` (alias `groupSize`, obsolete) | NatFloat | uniform(1,0) |
| `companions` | AssetLocation[] | empty |
| `insideBlockCodes` | AssetLocation[] | `["air"]`. Matching is by path prefix: a trailing `*` means "begins with" (lines 529-559) |
| `requireSolidGround` | bool | true |
| `tryOnlySurface` | bool | false |
| `climateValueMode` | `EnumGetClimateMode` | `WorldGenValues`. Not copied by `SetFrom` |

#### `RuntimeSpawnConditions : BaseSpawnConditions` (lines 259-339)

| JSON field | Type | Default |
|---|---|---|
| `chance` | double | 1 |
| `maxQuantity` | int | 20 |
| `maxQuantityByGroup` | `{ code: AssetLocation wildcard, maxQuantity: int }` | null |
| `spawnCapPlayerScaling` | float | 1 |
| `minDistanceToPlayer` | int | 18 |

#### `WorldGenSpawnConditions : BaseSpawnConditions` (lines 345-382)

| JSON field | Type | Default |
|---|---|---|
| `triesPerChunk` | NatFloat | Zero |

#### Related entity-level properties

| Property | Location | Used for |
|---|---|---|
| `habitat` | `ESS/Loading/EntityType.cs:34` | Habitat checks |
| `collisionBoxSize` | `API/Common/Entity/EntityProperties.cs:49` | Builds `SpawnCollisionBox` (lines 149-161), which the collision check at spawn uses (grown by 0.1) |
| `attributes.harshWinterSpawnRate`, `attributes.newWorldSpawnDelayHours` | Read by `SURV/Systems/CustomSpawnConditions.cs:122-136` | Runtime spawning only |
| `attributes.spawnCloserDuringLowStability` | Read by `SURV/Systems/TemporalStability/TemporalStability.cs:793` | Runtime spawning only |

#### Block-side requirements

`Block.CanCreatureSpawnOn` (`API/Common/Collectible/Block/Block.cs:798-828`) is called on the block **below** the spawn position. It does the following:

1. Asks block behaviors first (`BlockBehavior.CanCreatureSpawnOn(..., ref EnumHandling)`, `API/Common/Collectible/Block/BlockBehavior.cs:121`).
2. Otherwise checks `AllCreaturesAllowed`, or `AllowSpawnCreatureGroups` containing `"*"` or `sc.Group` (fields at `Block.cs:304-305`).
3. Requires `SideSolid[UP]` if `RequireSolidGround` is set.

The doc comment says it "Might be called outside the main thread".

### 1.2 Which fields each spawner actually uses

| Field | Runtime spawner (LIB) | Worldgen `GenCreatures` (ESS) |
|---|---|---|
| temp/rain | yes. Temperature is sampled at **Y = seaLevel*1.09**, whatever the spawn Y (`LIB/Vintagestory.Server/ServerSystemEntitySpawner.cs:551`). Compared against `WorldGenTemperature` even if `climateValueMode` is Now (line 563 → `MatchesClimate`) | yes. Sampled at "109% of sea level" (`93.GenCreatures.cs:309-311`) |
| forest/shrub | yes, at the spawn x/z (`ServerSystemEntitySpawner.cs:462-463`) | yes (`93.GenCreatures.cs:313-314, 404-406`) |
| minY/maxY | yes (lines 297-311, 358-363) | yes (lines 408-413) |
| light min/max + `lightLevelType` | **only through `EntityPlayer.CanSpawnNearby`**, i.e. survival's `TemporalStability.CanSpawnNearby` (`SURV/.../TemporalStability.cs:788-836`). The engine never checks light itself | yes, but always with `MaxLight`; `lightLevelType` is ignored (`93.GenCreatures.cs:398`) |
| `minDistanceToPlayer` | **only in survival's** `TemporalStability.CanSpawnNearby` (line 835) | n/a |
| `chance`, `maxQuantity`, `maxQuantityByGroup`, `spawnCapPlayerScaling` | yes (`ServerSystemEntitySpawner.cs:643-713`) | no |
| `group` | runtime: "hostile" is blocked during the grace timer (line 669) | Worldgen reads `Runtime.Group` only to decide whether `neutralCreatureSpawnMultiplier` applies (`93.GenCreatures.cs:222-223`) |
| `triesPerChunk` | n/a | yes |
| `insideBlockCodes`, `requireSolidGround`, companions, herd size | yes | yes |
| `randomMapCodePool` | via GenCreatures' `OnTrySpawnEntity` handler (`93.GenCreatures.cs:422-436`) | yes (line 260-261) |
| `habitatSize` | yes, water habitats only (lines 484-507) | no |

### 1.3 Vanilla forest animal examples (verbatim)

#### Raccoon

`ASSETS/entities/animal/mammal/raccoon-adult.json:261-288`:
```json5
		spawnconditions: {
			worldgen: {
				TriesPerChunk: { avg: 0.06, var: 0 },
				tryOnlySurface: true,
				groupSize: { dist: "verynarrowgaussian", avg: 2, var: 3 },
				insideBlockCodes: ["air", "tallgrass-*", "snowlayer-1"],
				companions: ["raccoon-{type}-adult-female", "raccoon-{type}-baby-male", "raccoon-{type}-baby-female"]
			},
			runtime: {
				group: "neutral",
				tryOnlySurface: true,
				chance: 0.0009,
				maxQuantity: 4,
				// Make them spawn away from artifical light so they don't spawn inside farmland
				maxLightLevel: 7,
				groupSize: { avg: 1, var: 0 },
				insideBlockCodes: ["air", "tallgrass-*", "snowlayer-1"],
				companions: ["raccoon-{type}-adult-female", "raccoon-{type}-baby-male", "raccoon-{type}-baby-female"]
			},
			climate: {
				minTemp: -4,
				maxTemp: 24,
				minRain: 0.25,
				minForest: 0.25,
				maxForest: 1.0
			}
		}
```
Note that the raccoon uses `maxLightLevel: 7` with the default `lightLevelType` of `MaxLight`. `MaxLight` includes sunlight and ignores time of day, so in practice raccoons spawn only in shade or darkness. The comment suggests the intent was to avoid artificial light.

#### Red fox (excerpt)

`ASSETS/entities/animal/mammal/fox-adult.json:341-375` (`spawnconditionsByType`, red fox entry):
```json5
			"fox-red-adult-male": {
				worldgen: {
					TriesPerChunk: { avg: 0.075, var: 0 },
					tryOnlySurface: true,
					groupSize: { dist: "verynarrowgaussian", avg: 2, var: 3 },
					insideBlockCodes: ["air", "tallgrass-*", "snowlayer-1"],
					minTemp: -15,
					maxTemp: 15,
					minRain: 0.25,
					minForestOrShrubs: 0.25,
					maxForest: 0.75,
					maxShrubs: 0.75,
					companions: ["fox-red-adult-female", "fox-red-baby-female", "fox-red-baby-male"]
				},
				runtime: {
					group: "neutral",
					tryOnlySurface: true,
					chance: 0.0006,
					maxQuantity: 4,
					// Make them spawn away from artifical light so they don't spawn inside farmland
					maxLightLevel: 7,
					lightLevelType: "onlyBlockLight",
					groupSize: { dist: "verynarrowgaussian", avg: 1, var: 2 },
					insideBlockCodes: ["air", "tallgrass-*", "snowlayer-1"],
					minTemp: -15,
					maxTemp: 15,
					minRain: 0.25,
					minForestOrShrubs: 0.25,
					maxForest: 0.75,
					maxShrubs: 0.75,
					companions: ["fox-red-adult-female", "fox-red-baby-female", "fox-red-baby-male"]
				}
			},
```

#### Bear (excerpt)

`ASSETS/entities/animal/mammal/bear-adult.json:602-660`:
```json5
		spawnconditions: {
			worldgen: {
				TriesPerChunk: { avg: 0.007, var: 0 },
				tryOnlySurface: true,
				groupSize: { dist: "verynarrowgaussian", avg: 1, var: 1 },
				insideBlockCodesByType: { ... }
			},
			runtime: {
				group: "hostile",
				MinDistanceToPlayer: 30,
				tryOnlySurface: true,
				chance: 0.000015,
				maxQuantity: 1,
				maxQuantityByGroup: { code: "bear-*", "maxQuantity": 4 },
				groupSize: { dist: "verynarrowgaussian", avg: 1, var: 1 },
				insideBlockCodesByType: { ... },
				// Make them spawn away from artifical light so they don't spawn inside farmland
				maxLightLevel: 7,
				lightLevelType: "onlyBlockLight"
			},
			climate: {
				maxY: 1.6,
				minTempByType: { "bear-polar-*": -48, "bear-brown-*": -15, ... },
				...
				minForestByType: { "bear-polar-*": 0, "bear-brown-*": 0.2, "bear-black-*": 0.5, ... }
			}
		}
```
In the bear excerpt, `{ ... }` marks lines I shortened. Everything else is verbatim.

Other vanilla entities with `minForest`: wolf, hare, goat, deer, moose, pig and butterfly (`grep -l minForest` under `ASSETS/entities`).

---

## 2. The runtime spawner (closed source, decompiled)

### 2.1 Classes involved

All of these are in `LIB/Vintagestory.Server/`:

- `ServerSystemEntitySpawner`
- `SpawnerOffthread`
- `EntitySpawnerResult`
- `SpawnState`
- `SpawnArea`
- `ServerMain` (`SpawnEntity`, `LoadEntity`, `DespawnEntity`)
- `ServerSystemEntitySimulation`

### 2.2 When and how it decides to spawn

1. **Startup** (`ServerSystemEntitySpawner.cs:86-126`):
   - Registers the main-thread tick listener `SpawnReadyMobs` every 250 ms (line 90).
   - Starts a dedicated thread named `"physicsManagerHelper"` that runs `SpawnerOffthread.Start` (lines 91-97), unless `server.ReducedServerThreads` is set (line 73).
   - Initialises the grace timer from the world config `graceTimer` (default "5" days).
   - Calls `Runtime.Initialise(...)` for every entity type (lines 119-125).
2. **Off-thread search loop** (`SpawnerOffthread.cs:18-36`): sleeps `500 - elapsed` ms, then calls `FindMobSpawnPositions_offthread`. Without the extra thread, the same work runs inside `PrepareForSpawning` on the main thread (lines 185-188).
3. **`LoadViableSpawnAreas`** (lines 570-641) runs on the main thread, at first and then every 10 s (line 179). For each playing client in dimension 0 it builds the set of chunk columns within **±3 chunks** in X and Z (a 7x7 area), and counts the entities per code in chunks ±3 chunk-Y around the player. These counts are `spawnCounts`.
4. **`ReloadSpawnStates_offthread`** (lines 643-713) runs on each search pass. For every entity type that has `Runtime`:
   - The type is skipped if `MaxQuantity == 0`, or if it is hostile during the grace period, or **if `rand.NextDouble() >= Chance`**. `chance` is therefore the per-pass (~2 per second) probability that the type is considered at all.
   - The global cap counts all `server.LoadedEntities` of that code (or its `originaltype` attribute): `MaxQuantity * (1 + max(0,(players-1) * serverConfig.SpawnCapPlayerScaling * sc.SpawnCapPlayerScaling)) - existing`, then also limited by `MaxQuantityByGroup` (lines 674-680).
   - The per-area cap is `SpawnCapScaledPerPlayer = MaxQuantity*scale/players` (line 708), compared against the area's `spawnCounts` (lines 292-296).
5. **`TrySpawnSomethingAt_offthrad`** (lines 263-392). For each column in each area:
   - Shuffles 7 chunk-Y slices around the player's chunk-Y and picks a random x/z per slice.
   - For each `SpawnState`, checks minY/maxY and rolls the herd size.
   - Fires **`OnTrySpawnGroupNearOffthread`** (line 326).
   - Then makes up to `groupSize*4+5` attempts at positions within ±5 blocks, wrapped to stay inside the same chunk (`randomWithinSameChunk`, line 394-397). Each attempt calls `CanSpawnAt_offthread`.
   - If enough positions are found, it enqueues an `EntitySpawnerResult` (lines 383-389).
6. **`CanSpawnAt_offthread`** (lines 399-520) checks, in order:
   - inside-block and the block below (`CanCreatureSpawnOn`), with a surface or ±5 downward scan;
   - temperature and rain;
   - forest and shrub;
   - collision box;
   - **`nearestPlayer.Entity.CanSpawnNearby(type, pos, sc)`** (line 479-483);
   - water habitat size.
7. **Main thread, `SpawnReadyMobs` → `EntitySpawnerResult.Spawn`** (`EntitySpawnerResult.cs:24-57`):
   - Re-checks the block via `CheckCanSpawnAt`.
   - Fires **`OnTrySpawnEntity`** with `server.blockAccessor`, which is `WorldMap.RelaxedBlockAccess` (`ServerMain.cs:436`) (line 45).
   - `DoSpawn` creates the entity, sets `HerdId`, position and yaw, sets `Attributes["origin"]="entityspawner"`, and `originaltype` if the handler swapped the type (lines 59-77). It then **enqueues** the entity into `server.DelayedSpawnQueue`.
8. **Main thread, `ServerSystemEntitySimulation.UpdateEvery100ms`** (registered at line 32; body at lines 138-164) dequeues up to `MagicNum.MaxEntitySpawnsPerTick` (default 8, `MagicNum.cs:118`) and calls `server.SpawnEntity(entity)`. This fires **`OnEntitySpawn`**.
9. Nothing at all runs unless `SaveGameData.EntitySpawning` is true (line 147) and at least one client is connected (line 193).

### 2.3 Hooks a mod can use

Verbatim signatures:

`API/Common/API/Delegates.cs:127-135`:
```csharp
    public delegate void EntityDelegate(Entity entity);
    public delegate bool TrySpawnEntityDelegate(IBlockAccessor blockAccessor, ref EntityProperties properties, Vec3d spawnPosition, long herdId);
    public delegate bool TrySpawnGroupDelegate(IBlockAccessor blockaccessor, EntityProperties props, BlockPos pos);
    public delegate void EntityDespawnDelegate(Entity entity, EntityDespawnData reasonData);
    public delegate void EntityDeathDelegate(Entity entity, DamageSource damageSource);
```
`API/Server/API/IServerEventAPI.cs`:
```csharp
        bool TriggerTrySpawnEntity(IBlockAccessor blockAccessor, ref EntityProperties properties, Vec3d position, long herdId);   // line 25
        /// Called when the server attempts to spawn given entity. Return false to deny spawning.
        event TrySpawnEntityDelegate OnTrySpawnEntity;                                                                           // line 117
        event TrySpawnGroupDelegate OnTrySpawnGroupNearOffthread;                                                                // line 119
        event ChunkColumnBeginLoadChunkThread BeginChunkColumnLoadChunkThread;                                                   // line 94
        event ChunkColumnLoadedDelegate ChunkColumnLoaded;                                                                       // line 99
        event ChunkColumnUnloadDelegate ChunkColumnUnloaded;                                                                     // line 104
```
`API/Common/API/IEventAPI.cs:108-123`, shared by client and server:
```csharp
        /// Triggered when a new entity spawned
        event EntityDelegate OnEntitySpawn;
        /// Triggered when a new entity got loaded (either spawned or loaded from disk)
        event EntityDelegate OnEntityLoaded;
        event EntityDeathDelegate OnEntityDeath;
        /// Triggered when a new entity despawned
        event EntityDespawnDelegate OnEntityDespawn;
```

#### Per-entity hooks in your own behavior or entity class

`API/Common/Entity/EntityBehavior.cs`:

| Hook | Line |
|---|---|
| `Initialize(EntityProperties, JsonObject)` | 34 |
| `AfterInitialized(bool onFirstSpawn)` | 41 |
| `OnEntitySpawn()` | 58 |
| `OnEntityLoaded()` | 63 |
| `OnEntityDespawn(EntityDespawnData)` | 69 |
| `OnEntityDeath(DamageSource)` | 174 |

`Entity` has the same virtuals: `API/Common/Entity/Entity.cs:1440, 1454, 1469`.

#### Other filters

- `EntityPlayer.OnCanSpawnNearby` (`API/Common/Entity/EntityPlayer.cs:74, 1076-1084`) is a single delegate, not an event. **Survival assigns it on PlayerJoin** (`SURV/Systems/TemporalStability/TemporalStability.cs:324-330`). Overwriting it would break vanilla's light and distance checks, so wrap it instead of replacing it, or better, do not touch it.
- `Block.CanCreatureSpawnOn` / `BlockBehavior.CanCreatureSpawnOn`, on your own blocks.

#### How the events are dispatched

- `sapi.Event.OnTrySpawnEntity`, `OnTrySpawnGroupNearOffthread` and `OnEntitySpawn` all register into `server.ModEventManager` (`LIB/Vintagestory.Server/ServerEventAPI.cs:40-80`).
- The core dispatcher `CoreServerEventManager` runs the engine handlers first and then the mod handlers (`LIB/.../CoreServerEventManager.cs:176-229`).
- For the two "try" events, the first handler that returns `false` stops the chain, and an exception in a handler is logged and skipped (`LIB/.../ServerEventManager.cs:151-205`).
- `sapi.Event.TriggerTrySpawnEntity` (what GenCreatures calls) goes **only** to the mod event manager (`LIB/.../ServerEventAPI.cs:446-448`).

#### What vanilla already subscribes

You need to coexist with these:

| Subscriber | Event | Effect |
|---|---|---|
| `ESS/.../93.GenCreatures.cs:69` | `OnTrySpawnEntity` | Animal map-code density |
| `SURV/Entities/EntityButterfly.cs:25` | `OnTrySpawnEntity` | Butterflies need temperature >= 0 |
| `SURV/Systems/TemporalStability/TemporalStability.cs:296` | `OnTrySpawnEntity` | Storm mob swap via `ref properties` |
| `SURV/Lore/StoryStructuresSpawnConditions.cs:71` | `OnTrySpawnEntity` | No hostiles in story structures |
| `SURV/Lore/Devastation/DevastationEffects.cs:446` | `OnTrySpawnEntity` | |
| `SURV/Systems/CustomSpawnConditions.cs:117` | `OnTrySpawnGroupNearOffthread` | Harsh winter rate, new-world delay |

### 2.4 Which hooks fire for which path, and on which thread

| Hook | Runtime spawner | Worldgen (`GenCreatures`) | Loaded from disk | Mod code calling `World.SpawnEntity` |
|---|---|---|---|---|
| `OnTrySpawnGroupNearOffthread` | yes, on the **spawner thread** (`physicsManagerHelper`), or on the main thread if ReducedServerThreads. Block accessor = `server.blockAccessor` | **no** | no | no |
| `OnTrySpawnEntity` | yes, **main thread**. Block accessor = main RelaxedBlockAccess | yes, **worldgen thread**. Block accessor = the **`IWorldGenBlockAccessor`** (`93.GenCreatures.cs:344`) | no | no (only if you call `TriggerTrySpawnEntity` yourself) |
| `Entity/Behavior.OnEntitySpawn` + `Event.OnEntitySpawn` | yes, main thread, up to ~100 ms+ later (delayed queue) | **no** in the normal case (see the edge case below) | no | yes, synchronously on the calling thread (`ServerMain.cs:2946-2980`) |
| `AfterInitialized(onFirstSpawn)` | `true` | **`false`** | `false` | `true` |
| `Entity/Behavior.OnEntityLoaded` + `Event.OnEntityLoaded` | **no** (the doc comment on IEventAPI.cs:113 says "either spawned or loaded" but the implementation does not fire it on spawn) | yes, main thread, when the generated column is committed | yes, main thread | no |
| `entity.Attributes["origin"]` | `"entityspawner"` (`EntitySpawnerResult.cs:75`) | `"worldgen"` (`93.GenCreatures.cs:369`) | whatever was saved | unset unless you set it |

How the "loaded" path works:

- The worldgen path calls `IWorldGenBlockAccessor.AddEntity(ent)`. That assigns `EntityId = ++SaveGameData.LastEntityId` and adds the entity to the *generating* chunk without initialising it (`LIB/.../BlockAccessorWorldGen.cs:317-325`).
- When a column finishes (generated or read from disk), `mainThreadLoadChunkColumn` is enqueued as a main-thread task (`LIB/.../ServerSystemSupplyChunks.cs:279-282` and `408-412`).
- That task fires `ChunkColumnLoaded` and then calls `server.LoadEntity` for every entity in the chunks (lines 482-549).
- `LoadEntity` calls `Initialize`, then `AfterInitialized(onFirstSpawn: false)`, then `OnEntityLoaded()`, then `TriggerEntityLoaded` (`ServerMain.cs:2893-2925`).

`BeginChunkColumnLoadChunkThread` fires on the chunk thread just before that (`ServerSystemSupplyChunks.cs:397`).

**Worldgen edge case.** `GenCreatures` (lines 349-356) decides between `api.World.SpawnEntity(ent)` and `wgenBlockAccessor.AddEntity(ent)` by testing the variable `pos`. That variable is the *last attempted* position, not `so.Pos`. When it points into an already-loaded neighbour chunk, `SpawnEntity` is called **from the worldgen thread**, and that fires `OnEntitySpawn` off the main thread. In the reverse mismatch, `AddEntity` silently drops the entity (`BlockAccessorWorldGen.cs:319-324`). Handlers must tolerate both.

**Despawn and death.** `OnEntityDespawn` fires on the main thread from `ServerSystemEntitySimulation.TickEntities` (lines 214-222) or `ServerMain.DespawnEntity` (lines 2987-3001). `OnEntityDeath` fires from `Entity.Die` only when the reason is `Death` (`API/Common/Entity/Entity.cs:2228-2230`). See section 8.

---

## 3. Worldgen creature spawning in the standard generator

`ESS/Systems/WorldGen/Standard/ChunkGen/93.GenCreatures.cs`

**Registration.** The system has `ExecuteOrder() => 0.1` (line 53) and registers in `StartServerSide` (lines 55-71):
- `InitWorldGenerator(initWorldGen, "standard")`
- `ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.PreDone, "standard")`
- `MapRegionGeneration` (animal maps)
- `GetWorldgenBlockAccessor(...)`
- `OnTrySpawnEntity += ...`

**Per column** (`OnChunkColumnGen`, lines 175-242):
1. Skips the column if a story structure claims the `creatures` category (line 181).
2. Reads the climate, forest and shrub maps of the region corners.
3. For each entity type with `Worldgen` conditions, computes `tries = TriesPerChunk.nextFloat()`, multiplied by `neutralCreatureSpawnMultiplier` for non-hostiles (`ASSETS/worldgen/global.json:11` = 1.0).
4. `while (tries-- > rnd.NextDouble())` picks a random x/z (and the surface Y if `tryOnlySurface`) and calls `TrySpawnGroupAt`.

**`TrySpawnGroupAt`** (lines 247-360):
1. Checks the animal map code.
2. Rolls the herd size.
3. Makes `groupSize*4+5` attempts at offsets of about ±5 blocks. It may read neighbour chunks through `api.World.BlockAccessor` if they are not generating (line 291).
4. Calls `CanSpawnAtPosition` and `CanSpawnAtConditions`.
5. If the whole group fits, it gets a herd id from `api.WorldManager.GetNextUniqueId()` and, for each member:
   - `CreateEntity` (sets Pos, yaw, `origin=worldgen`; lines 363-371) and sets `HerdId`;
   - **then** `api.Event.TriggerTrySpawnEntity(wgenBlockAccessor, ref so.ForType, so.Pos, herdId)`;
   - if allowed, `AddEntity` or `SpawnEntity` (see 2.4).

**Consequences.**
- The worldgen `OnTrySpawnEntity` can **veto** a spawn.
- Changing `ref properties` in the worldgen path has **no effect**, because the entity was created beforehand from the old type (line 338 vs 344).
- Worldgen spawns run once per column at generation and ignore `MaxQuantity`.
- The thread is the worldgen thread (pass `PreDone`). The `IBlockAccessor` handed to the event is the `IWorldGenBlockAccessor`, so a handler can test `ba is IWorldGenBlockAccessor` to tell worldgen from runtime.

**Threads.** Worldgen runs on the chunk thread. By default `MaxWorldgenThreads = 1` (`LIB/.../MagicNum.cs:117`, overridable in `servermagicnumbers.json`), so `additionalWorldGenThreadsCount = 0` (`LIB/.../ChunkServerThread.cs:43-52`). `GetWorldgenBlockAccessor` handlers are invoked once at startup (`LIB/.../ServerSystemLoadAndSaveGame.cs:175-178`).

---

## 4. Options for "spawn a spider together with a hammock block"

### Shared API calls

**Worldgen pass registration.** Handlers run in registration order. Registration order follows `ModSystem.ExecuteOrder()`, because ModLoader sorts systems by it (`LIB/Vintagestory.Common/ModLoader.cs:256`). The pass enum and its vanilla orders are in `API/Server/Worldgen/EnumWorldGenPass.cs`: Vegetation = 3 has trees/patches at 0.5, rivulets at 0.9 and light at 0.95; PreDone = 5 has creatures at 0.1.
```csharp
public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
public override double ExecuteOrder() => 0.6;               // after GenVegetationAndPatches (0.5, ESS/.../GenVegetationAndPatches.cs:57-60), before GenLight (0.95)
IWorldGenBlockAccessor wgenBa;
public override void StartServerSide(ICoreServerAPI sapi) {
    sapi.Event.GetWorldgenBlockAccessor(cp => wgenBa = cp.GetBlockAccessor(false));   // IChunkProviderThread.GetBlockAccessor(bool updateHeightmap)
    sapi.Event.InitWorldGenerator(OnInitWorldGen, "standard");
    sapi.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.Vegetation, "standard");
}
void OnChunkColumnGen(IChunkColumnGenerateRequest req) { /* req.Chunks (IServerChunk[]), req.ChunkX, req.ChunkZ, req.Chunks[0].MapChunk */ }
```
(Signatures: `IServerEventAPI.cs:31, 38, 80`; `IChunkProviderThread.cs:12`; `IChunkColumnGenerateRequest.cs:7-15`.)

**Worldgen block accessor** (`API/Server/Worldgen/IWorldGenBlockAccessor.cs`):
- `SetBlock(int, BlockPos)`
- `SpawnBlockEntity(string, BlockPos, ItemStack)`. Only works for generating chunks (`LIB/.../BlockAccessorWorldGen.cs:302-315`).
- `AddEntity(Entity)`. Only works for generating chunks (lines 317-325).
- `ScheduleBlockUpdate`, `GetBlockEntity`

`GetChunkAtBlockPos` returns only *generating* chunks (lines 116-129). `SetBlock` also writes silently into already-loaded chunks without notifying clients (lines 189-220). Stay inside `req.ChunkX/ChunkZ`.

**Spawning an entity from code:**
```csharp
EntityProperties type = sapi.World.GetEntityType(new AssetLocation("jumpingspiders", "jumpingspider")); // IWorldAccessor.cs:246
Entity e = sapi.World.ClassRegistry.CreateEntity(type);   // IClassRegistryAPI.cs:54
e.Pos.SetPos(x + 0.5, y, z + 0.5);                         // EntityPos.SetPos(double,double,double), EntityPos.cs:309 (ServerPos is obsolete: Entity.cs:179-183)
e.Pos.SetYaw((float)sapi.World.Rand.NextDouble() * GameMath.TWOPI);
e.PositionBeforeFalling.Set(e.Pos.X, e.Pos.Y, e.Pos.Z);
e.Attributes.SetString("origin", "jumpingspiders");
sapi.World.SpawnEntity(e);                                 // IWorldAccessor.cs:271, main thread; fires OnEntitySpawn synchronously
// worldgen variant: wgenBa.AddEntity(e) instead of SpawnEntity (entity is initialised later on main-thread load)
```
`ServerMain.SpawnEntity(Entity, EntityProperties)` ignores its `type` argument and looks the type up from `entity.Code` again (`ServerMain.cs:2937-2944`).

**Placing a block at runtime (main thread):**
- Look up the block with `sapi.World.GetBlock(new AssetLocation("jumpingspiders","silkhammock-north"))` (`IWorldAccessor.cs:239`).
- Place it with `sapi.World.BlockAccessor.SetBlock(block.BlockId, pos)`. That calls `Block.OnBlockPlaced`, which spawns the block entity if `EntityClass != null` (`API/.../Block.cs:1381-1383`, via `LIB/Vintagestory.Common/BlockAccessorBase.cs:105-156`).

### (a) React on the main thread at spawn time

**Hooks.**
- Use `OnTrySpawnEntity`, not `OnEntitySpawn`, as the decision point. It fires for **both** runtime spawns (main thread, RelaxedBlockAccess) and worldgen spawns (worldgen thread, `IWorldGenBlockAccessor`). It receives the herdId, which is the same value as `EntityAgent.HerdId` on the entity that will be created (`EntitySpawnerResult.cs:26,45,64`; `93.GenCreatures.cs:334-344`).
- Within the handler you can:
  1. return `false` if the position is not in a spider chunk, is claimed (`sapi.World.Claims.Get(pos)`, `ILandClaimAPI.cs:39`), or the chunk has many player-placed blocks (`((IServerChunk)ba.GetChunkAtBlockPos(pos)).BlocksPlaced`, `IServerChunk.cs:45`, incremented only for player placement at `LIB/.../ServerSystemBlockSimulation.cs:562-566`);
  2. look up or place the hammock with the given `ba`;
  3. record `herdId → hammockPos`.
- Then in `OnEntitySpawn` (runtime) or `OnEntityLoaded` (worldgen), copy the hammock position into `entity.Attributes`. Entity attributes are persisted server-side (`Entity.cs:2147-2149`) and not sent to clients.

**Moving the spawn into the hammock at night.** `spawnPosition` is a `Vec3d` reference. In the runtime path the same object is later passed to `DoSpawn` (`EntitySpawnerResult.cs:45-47`), so mutating it moves the spawn. This is undocumented. A cleaner way is to return `false` and spawn your own entity from code inside the handler, since it is on the main thread for runtime spawns. For worldgen, the entity already exists (`93.GenCreatures.cs:338`), so the position cannot be changed. Teleport later with `Entity.TeleportToDouble` (`Entity.cs:823`) or let the AI walk to the hammock.

**Chunk constraints.** Runtime spawn positions lie in loaded chunks within the player's 7x7 column area. The tree search may cross into unloaded columns, so check `ba.GetChunkAtBlockPos(p) != null`, or use `SearchBlocks(..., onChunkMissing)` (`IBlockAccessor.cs:382`).

**Cost.** The handler runs on the main tick. Keep the scan small (section 6).

**Assessment.** Works for both spawn sources with one hook, and reuses vanilla climate, forest, light and cap gating from JSON. Downsides:
- JSON spawning is ground-based (solid ground, `insideBlockCodes`), so night "in hammock" placement needs custom code.
- The worldgen half runs on the worldgen thread. Keep its state in thread-safe structures (`ConcurrentDictionary`).
- `OnEntitySpawn` alone would miss every worldgen spider.

### (b) Custom worldgen pass placing hammocks and/or spiders

Register at `EnumWorldGenPass.Vegetation` with `ExecuteOrder` between 0.5 and 0.95, so trees exist and light has not yet been computed. Alternatively use `PreDone` next to GenCreatures; then lighting is already done.

Per column:
1. `if (!IsSpiderChunk(req.ChunkX, req.ChunkZ)) return;`
2. `var mc = req.Chunks[0].MapChunk; ushort[] hm = mc.WorldGenTerrainHeightMap;`
3. Scan the 32x32 heightmap at `hm[z*32+x]+1 .. +3` for grown logs. Trees start one block above the soil height: `TreeGen.GrowTree` + `growBranch` (`ESS/.../Treegen/TreeGen.cs:34-77,115-149`; `TreeGenConfig.yOffset` default 0, `TreeGenConfig.cs:12`).
4. Climb the trunk to choose a height, and place the hammock with `wgenBa.SetBlock` + `SpawnBlockEntity`, or with `block.TryPlaceBlockForWorldGen(wgenBa, pos, facing, rnd)` (`Block.cs:841`).
5. Optionally create the spider and `wgenBa.AddEntity(e)`.
6. Store the hammock position in `mc.SetModdata("jumpingspiders:hammock", ...)` (thread-safe, see 7).

The block entity is initialised later on the main thread, and `OnBlockPlaced` is called for block entities registered as "new" (`LIB/.../ServerSystemSupplyChunks.cs:573-590`; `BlockAccessorWorldGen.cs:313`).

**Assessment.** Cheapest and safest for block placement: no main-thread cost, full access to the fresh chunk, deterministic. It only affects chunks generated **after** the mod is installed, though. Already explored land never gets hammocks, and a worldgen-spawned spider appears only once per column. So (b) needs (a) or (c) next to it for respawning.

### (c) Disable JSON runtime spawning and spawn from a server tick

1. Leave out `runtime` (or set `maxQuantity: 0`; `ServerSystemEntitySpawner.cs:669`). Keep `worldgen` if you want.
2. Register `sapi.Event.RegisterGameTickListener(OnTick, 5000)` (`IEventAPI.cs:187`), which runs on the main thread.
3. On each tick, for each player in `sapi.World.AllOnlinePlayers` (`IWorldAccessor.cs:363`):
   - pick nearby spider-chunk columns that are loaded (`sapi.WorldManager.GetMapChunk(cx,cz)`, `IWorldManagerAPI.cs:135`);
   - read the hammock from map chunk moddata, or find a tree and place one;
   - check the climate (section 6) and the time of day (section 9);
   - check caps by counting `sapi.World.LoadedEntities` (`IServerWorldAccessor.cs:23`) or using `GetEntitiesAround` (`IWorldAccessor.cs:302`);
   - check distance to players;
   - spawn from code at the hammock (night) or near it.
4. If you also want the vanilla mod-compat filters, call `sapi.Event.TriggerTrySpawnEntity(sapi.World.BlockAccessor, ref props, pos, herdId)` yourself.

**Assessment.** Full control: night placement in the hammock, hammock reuse, chunk selection and rate limits all live in one place, and nothing depends on the spawner's ground-only search. Downsides:
- You must reimplement caps, light and distance rules.
- Mods that hook `OnTrySpawnEntity` or `OnTrySpawnGroupNearOffthread` do not see your spawns unless you trigger them.
- Main-thread cost is yours to budget (keep scans small and amortised).

---

## 5. How mushrooms get onto tree trunks in the standard generator

1. **Block patches with `placement: "OnTrees"`.**
   - The enum is at `ESS/.../8.GenVegetationAndPatches/BlockPatch.cs:14-27`; the fields `Placement` and `TreeType` are at lines 69-71.
   - Example, `ASSETS/worldgen/blockpatches/mushroom.json:2-12`:
     ```json5
     	{
     		blockCodes: ["mushroom-beardedtooth-normal-north"],
     		placement: "OnTrees",
     		treeType: "Decidious",
     		minRain: 0.4,
     		minTemp: 0,
     		maxTemp: 30,
     		quantity: { avg: 1, var: 0 },
     		chance: 8,
     		// Game will randomly choose one based on world seed
     		randomMapCodePool: ["mush1", "mush2", "mush3", "mush4", "mush5", "mush6", "mush7"]
     	},
     ```
   - Patches are loaded from `worldgen/blockpatches/` across **all domains** (`GenVegetationAndPatches.cs:135`; `IAssetManager.GetMany` "If no domain is specified, all domains will be searched", `API/Common/Assets/IAssetManager.cs:77-85`). A mod can therefore add `assets/jumpingspiders/worldgen/blockpatches/*.json`.
   - `OnTrees` and `UnderTrees` patches are excluded from the normal patch pass (`BlockPatchConfig.cs:32`) and handed to the tree generator's `ForestFloorSystem` (`Treegen/ForestFloorSystem.cs:54-73`).
2. **When.** After each tree is grown, `TreeGen.GrowTree` calls `forestFloor.CreateForestFloor(ba, config, pos, random, ...)` (`TreeGen.cs:74-77`). This happens in `GenVegetationAndPatches.genTrees` (Vegetation pass, order 0.5, `GenVegetationAndPatches.cs:416-490`). `CreateForestFloor` returns early if `Fertility <= 0.25` or `forestness <= 0.4`, where `forestness = ForestDensity² * 4 * (Fertility + 0.25)` (`ForestFloorSystem.cs:92-96`).
3. **Where heights are chosen.** `ForestFloorSystem.GenPatches`, lines 276-330, verbatim core:
   ```csharp
                   float chance = 3 * forestNess * blockPatch.Chance * bpc.ChanceMultiplier.nextFloat(1f, rnd);
                   while (chance-- > rnd.NextFloat())
                   {
                       int dx = 1 - rnd.NextInt(2) * 2;
                       int dy = rnd.NextInt(5);
                       int dz = 1 - rnd.NextInt(2) * 2;
                       tmpPos.Set(pos.X + dx, pos.Y + dy, pos.Z + dz);
                       var block = api.GetBlock(tmpPos);
                       if (block.Id != 0) continue;
                       BlockFacing facing = null;
                       for (int j = 0; j < 4; j++)
                       {
                           var f = BlockFacing.HORIZONTALS[j];
                           var nblock = api.GetBlockOnSide(tmpPos, f);
                           if (nblock is IBlockLog && nblock.Variant["type"] != "resin")
                           { facing = f; break; }
                       }
                       if (facing == null) break;
                       ...
                       blockPatch.Blocks[index].TryPlaceBlockForWorldGen(blockAccessor, tmpPos, facing, rnd);
   ```
   (Lines 315-325, the climate, `IsPatchSuitableUnderTree` and map-code checks, are elided.)
   - `pos` is the tree base: the soil block at the heightmap, `TreeGen.cs:42`. Heights are therefore **0..4 above the soil**.
   - The cell is **diagonal** (±1, ±1) to the trunk. For a straight 1x1 trunk, the four horizontal neighbours of a diagonal cell do not include the trunk column, so `facing` stays null and the loop `break`s. Placement can only succeed where logs sit orthogonally next to that diagonal cell: multi-trunk or leaning trees, low branches, or neighbouring trees. This follows from the code; I did not measure it in game.
4. **The mushroom block itself.** `BlockMushroom.TryPlaceBlockForWorldGen` (`SURV/Block/BlockMushroom.cs:73-89`) does not place a mushroom directly. It spawns a `Mycelium` block entity *inside the log* at `pos + facing` (or one below), provided the log has `BlockBehaviorMyceliumHost`. `BEMycelium.OnGenerated → generateSideGrowingMushrooms` (`SURV/BlockEntity/BEMycelium.cs:203-330`) then places 1..5 side mushrooms on free sides of that log at dy ∈ [-2, 2]. The default `Block.TryPlaceBlockForWorldGen` only does `SetBlock` (+ block entity) if the target is replaceable (`Block.cs:841-863`), and ignores `onBlockFace`.
5. **Tree generator config.** Tree JSON (e.g. `ASSETS/worldgen/treegen/englishoak.json:29-35`, `treeBlocks`) offers vines (`vinesBlockCode`), moss decor (`mossDecorCode`) and "other log" variants. These are applied per log block in `TreeGen.PlaceBlockEtc` (`TreeGen.cs:236-304`). There is no generic "attach this block to the trunk at height N" option. `TreeGenParams` exposes only `vinesGrowthChance`, `mossGrowthChance` and `otherBlockChance` (`API/Server/ITreeGenerator.cs:16-24`).

**Can a mod reuse it through JSON?**
- **Pure JSON** (`OnTrees` patch with the hammock block) gets: a plain `SetBlock` in the air cell 0..4 blocks above ground and diagonal to the trunk, at a low success rate, unoriented, and not "high on the trunk". That is not good enough.
- **JSON + a small code override** (the hammock's block class overrides `TryPlaceBlockForWorldGen`):
  - The `OnTrees` patch supplies a worldgen-thread call with the worldgen block accessor and `facing` toward a log.
  - The override walks up the log at `pos + facing`, picks a height, checks the spider-chunk hash, places the oriented hammock (and block entity), and returns true.
  - The rate is controlled by `chance` and the patch climate filters, but it inherits the low hit rate on 1x1 trunks described above.
- **A dedicated worldgen pass** (option b) is more predictable.

---

## 6. Finding a tree at runtime

### 6.1 Recognising trunk blocks

Vanilla trunk block codes come from `ASSETS/worldgen/treegen/*.json` (`logBlockCode`, `otherLogBlockCode`, `trunkSegmentBase`):

| Pattern | Block type file | Class |
|---|---|---|
| `log-grown-{wood}-ud` (maple, kapok, pine, birch, acacia, oak, larch, baldcypress, walnut, redwood, purpleheart, ebony) | `blocktypes/wood/woodtyped/log.json` | `BlockLog` |
| `log-resin-{pine,acacia}-ud` (also `log-resinharvested-*`) | `log-withresin.json` | **no class, plain `Block`** |
| `lognarrow-grown-cypress-ud` | `log-narrow.json` | plain `Block` |
| `logsection-grown-redwood-{ne,se,sw,nw}-ud` | `log-section.json` | `BlockLogSection` |

- Only `BlockLog` implements `IBlockLog` (`SURV/Block/BlockLog.cs:12`; marker interface at `ESS/Systems/WorldGen/Standard/IBlockLog.cs:7`). `is IBlockLog` therefore misses resin logs, narrow cypress and redwood sections.
- A robust test for "natural trunk": `block.BlockMaterial == EnumBlockMaterial.Wood && block.Attributes?["treeFellingGroupCode"].AsString() != null`. That attribute is only defined for grown variants:
  - `log.json:23-25` `treeFellingGroupCodeByType: { "log-grown-*": "{wood}" }`
  - `log-narrow.json:11-13` and `log-section.json:14-16` (`"*-grown-*"`)
  - `log-withresin.json:26,32` (all variants)

  The same attribute is what the axe tree-felling uses (`SURV/Item/ItemAxe.cs:203`). I believe leaves also carry `treeFellingGroupCode`; I did not check that. Hence the `BlockMaterial == Wood` guard.
- An alternative test: `block.Code.Path.StartsWith("log-grown-") || ...StartsWith("log-resin") || ...StartsWith("lognarrow-grown-") || ...StartsWith("logsection-grown-")` plus `Variant["rotation"] == "ud"`.
- Player-placed logs are `log-placed-*` (`log.json:10`), so the grown/placed distinction also keeps hammocks off log cabins.

### 6.2 Cheap scanning

- Trees grow from the worldgen surface. `IBlockAccessor.GetTerrainMapheightAt(BlockPos)` (`IBlockAccessor.cs:662`) and `IMapChunk.WorldGenTerrainHeightMap` (`API/Common/API/IMapChunk.cs:106`) give that height. **It is never updated after worldgen**, which is good for finding trunk bases.
- Scan only `(2R+1)²` columns at `terrainHeight+1..+2` for a trunk block, then walk upward with `GetBlock` while it is still a trunk block, to get the trunk height. About 2 reads per column plus the trunk height.
- For volume scans, use:
  - `IBlockAccessor.WalkBlocks(min, max, Action<Block,int,int,int>, bool centerOrder)` (`IBlockAccessor.cs:372`);
  - `SearchBlocks(min, max, ActionConsumable<Block,BlockPos>, Action<int,int,int> onChunkMissing)` (`IBlockAccessor.cs:382`; return false to stop);
  - or `sapi.World.GetBlockAccessorPrefetch(false,false)` + `PrefetchBlocks(min,max)` (`IWorldAccessor.cs:759`, `IBlockAccessorPrefetch.cs:18`).
  - The doc comments of `WalkBlocks` and `SearchBlocks` say "Currently NOT dimensionally aware" (fine for the normal world).
- Off-thread reads: the spawner itself uses a caching accessor `GetCachingBlockAccessor(false,false)` (`IWorldAccessor.cs:768`) plus chunk read locks.
- Finding an existing hammock cheaply:
  - Keep its position in map chunk moddata (section 7). One lookup per column, no block scan.
  - Or register the hammock block entity as a point of interest: `POIRegistry` in `ESS/Systems/POIRegistry.cs`; `IPointOfInterest { Vec3d Position; string Type; }` at lines 17-25; `AddPOI` line 283, `RemovePOI` line 297, `GetNearestPoi(Vec3d, float radius, PoiMatcher)` line 194. POIs exist only for loaded block entities.

### 6.3 Climate and forest density at a position (same values as spawning)

- `IBlockAccessor.GetClimateAt(BlockPos pos, EnumGetClimateMode mode = NowValues, double totalDays = 0)` (`IBlockAccessor.cs:720`).
- With `EnumGetClimateMode.WorldGenValues`, the server implementation (`LIB/.../ServerWorldMap.cs:557-577, 615-658`) returns a `ClimateCondition` (`IBlockAccessor.cs:64-120`) with:
  - `WorldGenTemperature`/`Temperature`: altitude-adjusted for `pos.Y`;
  - `Rainfall`/`WorldgenRainfall`;
  - `Fertility`;
  - **`ForestDensity` and `ShrubDensity` (0..1)**: interpolated from the region forest and shrub maps at x/z, the same `AddWorldGenForestShrub` the spawner uses.
- To reproduce the runtime spawner exactly, query with `pos.Y = (int)(sapi.World.SeaLevel * 1.09)` (`ServerSystemEntitySpawner.cs:551`) and compare `WorldGenTemperature` and `Rainfall` (`MatchesClimate`) and `ForestDensity`/`ShrubDensity` (`MatchesForestation`). You can even call `sc.MatchesClimate(c)` and `sc.MatchesForestation(c)` on your own `Runtime` conditions (`SpawnConditions.cs:123-136`).
- The `OnGetClimate` handlers in Temperature/WeatherSystemServer return early for WorldGenValues (`SURV/Systems/Temperature.cs:55`, `ESS/Systems/Weather/WeatherSystemBase.cs:163-165`, registered at `WeatherSystemServer.cs:68`). The Devastation one does not (`SURV/Lore/Devastation/DevastationEffects.cs:89`).
- The result is `null` for invalid positions or regions without a climate map.

---

## 7. Deterministic per-chunk selection, and chunk mod data

### 7.1 Seed and hash functions

**Seed:**
- `sapi.World.Seed` (`int`, `API/Common/API/IWorldAccessor.cs:90`)
- `sapi.WorldManager.Seed` (`IWorldManagerAPI.cs:390`)

**Hash functions** in `API/Math/GameMath.cs`, all pure and process-independent:
- `GameMath.MurmurHash3(int x, int y, int z)` (line 1355), with fixed `murmurseed = 144` (line 52);
- `GameMath.MurmurHash3Mod(int x, int y, int z, int mod)` (line 1341);
- `GameMath.oaatHash(int x, int y)` / `oaatHash(int x, int y, int z)` (lines 1284, 1302), documented as "Fast, but not very random";
- `GameMath.DotNetStringHash(string)` (line 1151), a deterministic string hash meant for persistent use.

**LCG:** `new LCGRandom(long worldSeed)` → `InitPositionSeed(int x, int z)` → `NextInt(max)` / `NextFloat()` (`API/Math/LCGRandom.cs:72, 110, 155, 188`). This is what vanilla worldgen uses (e.g. `GenVegetationAndPatches.cs:437-438`).

**Suggested spider-chunk test** (fraction 1/N):
```csharp
const int Salt = 0x5A1D3E;                 // mod-specific constant
static bool IsSpiderChunk(int seed, int cx, int cz, int n = 64)
    => GameMath.MurmurHash3Mod(cx, seed ^ Salt, cz, n) == 0;
```
Or a jittered grid, which guarantees spacing: divide chunk coords into `G x G` cells (`Math.DivRem` with floor for negatives; world coordinates are non-negative in VS, so plain `/` works) and pick exactly one column per cell with `LCGRandom(seed + Salt).InitPositionSeed(cellX, cellZ); NextInt(G*G)`. Accept it with a probability for sparser fields. Chunk coords are `blockX / 32` (`GlobalConstants.ChunkSize` = 32; `sapi.WorldManager.ChunkSize`, `IWorldManagerAPI.cs:383`).

**Do not use `string.GetHashCode()` or anything derived from it.** On .NET it is randomised per process. I checked this here: two `dotnet fsi` runs printed `1161781477` and `2009265080` for `"wolf-male".GetHashCode()`. Vanilla does exactly this in `BaseSpawnConditions.Initialise` (`SpawnConditions.cs:502`, `entityName.GetHashCode()`) and `GenCreatures.loadAnimalMaps` (`93.GenCreatures.cs:169-170`). So `randomMapCodePool` selection and new animal maps can change between server restarts. Avoid `randomMapCodePool` if you need stable areas.

### 7.2 Custom data on chunks and map chunks

| API | Signature (verbatim) | Persistence | Sent to client | Thread notes (from implementation) |
|---|---|---|---|---|
| `IMapChunk` (per 32x32 column; `API/Common/API/IMapChunk.cs:59-93`) | `void SetModdata(string key, byte[] data);` `byte[] GetModdata(string key);` `void SetModdata<T>(string key, T data);` `T GetModdata<T>(string key, T defaultValue = default(T));` `void RemoveModdata(string key);` | saved with the map chunk. `SetModdata`/`RemoveModdata` call `MarkDirty()` (`LIB/.../ServerMapChunk.cs:205-242`) | **no** ("Client: Not implemented") | `Set`/`Get` take `lock (modDataLock)` (lines 207, 216). `RemoveModdata` does **not** lock (line 240). Lookup `sapi.WorldManager.GetMapChunk(cx,cz)` reads a `ConcurrentDictionary` (`LIB/.../ServerWorldMap.cs:235-239`, `ServerMain.cs:291`) |
| `IWorldChunk` (32³; `API/Common/API/IWorldChunk.cs:217-254`) | same four methods plus `Dictionary<string, object> LiveModData { get; set; }` | saved. `SetModdata` calls `MarkModified()` (`LIB/Vintagestory.Common/WorldChunk.cs:409-419`) | **yes** ("When set on the server before the chunk is sent to the client, the data will also be sent") | plain `Dictionary`, no locks. Main thread only |
| `IServerChunk` (`API/Server/API/IServerChunk.cs:18,25`) | `void SetServerModdata(string key, byte[] data);` `byte[] GetServerModdata(string key);` | serialized (`LIB/.../ServerChunk.cs:797`), but `SetServerModdata` does **not** mark the chunk dirty (lines 659-662). Call `chunk.MarkModified()` yourself | no | no locks |
| `IMapRegion` (`API/Common/API/IMapRegion.cs:156-189`) | `SetModdata`/`GetModdata`/`GetModdata<T>(string key)` | saved | no | not inspected |

The generic versions use `SerializerUtil`, which is protobuf-net (`API/Util/SerializerUtil.cs:1, 28-61`). `T` must be a primitive or a `[ProtoContract]` type.

**Recommendation.** Store the per-column state (`isSpiderChunk` evaluated?, hammock `BlockPos`, last spawn time) in **map chunk moddata** under `"jumpingspiders:..."`. It is server-only, locked and saved. During worldgen you can write it through `req.Chunks[0].MapChunk`.

---

## 8. Despawning

**JSON config.** The behavior code is `"despawn"` (registered at `ESS/Core.cs:155`), class `EntityBehaviorDespawn` (`ESS/Entity/Behavior/BehaviorDespawn.cs`). Options read in `Initialize` (lines 82-112):

| Option | Default | Meaning |
|---|---|---|
| `minPlayerDistance` | -1 (ignored) | |
| `belowLightLevel` | -1 (ignored) | Measured with `MaxLight`, which includes sunlight and ignores time of day (line 175) |
| `minSeconds` | 30 | Plus up to 5% jitter. The entity attribute `minsecondsToDespawn` overrides it |
| `afterDays` | (none) | Sets `WatchedAttributes["despawnTotalDays"]` once |

Vanilla example, `ASSETS/entities/animal/mammal/wolf-adult.json:47`:
```json5
			{ code: "despawn", minPlayerDistance: 16, belowLightLevel: 8, minSeconds: 300, afterDays: 14 },
```

**Tick logic** (lines 115-160):
- Every ~2.5-3.5 s of *ticked* time, if a player is within `minPlayerDistance`, **or** the light is >= `belowLightLevel`, the timer resets.
- Otherwise the entity dies with `EnumDespawnReason.Expire` once the accumulated time exceeds `minSeconds`, or once `TotalDays > despawnTotalDays`.
- Consequence: with neither `minPlayerDistance` nor `belowLightLevel` set, `PlayerInRange()` and `LightLevelOk()` are always false, so the entity expires after `minSeconds` even next to a player.
- It only ticks while the chunk is loaded.

**How a mod learns about it.**
- `sapi.Event.OnEntityDespawn += (Entity e, EntityDespawnData d) => ...` (`IEventAPI.cs:123`, delegate `Delegates.cs:133`), on the main thread.
- Or `EntityBehavior.OnEntityDespawn(EntityDespawnData)` in your own behavior.
- `EntityDespawnData` (`API/Common/Entity/EntityDespawn.cs:5-17`) has `Reason` (`EnumDespawnReason`) and `DamageSourceForDeath`.
- `EnumDespawnReason` (`API/Common/Combat/EnumDespawnReason.cs:5-45`):

| Value | Meaning |
|---|---|
| `Death` | Killed. The only value that fires `Event.OnEntityDeath` and `EntityBehavior.OnEntityDeath` and drops loot (`Entity.cs:2228-2266`) |
| `Combusted` | |
| `OutOfRange` | Client-side tracking-range removal, `LIB/.../PhysicsManager.cs:164-167` |
| `PickedUp` | |
| `Unload` | Chunk unloaded, `LIB/.../ServerChunk.cs:213-221`. The entity stays saved in the chunk: `ServerMain.DespawnEntity` skips the chunk removal for `Unload`, lines 2992-2996 |
| `Disconnect` | Player entities |
| `Expire` | Despawn behavior |
| `Removed` | `/entity remove`, etc. |

`Entity.Die(reason, dmg)` sets `DespawnReason`. `ShouldDespawn` (`EntityAgent.cs:142-149`, `!Alive && AllowDespawn`) then makes `TickEntities` call `DespawnEntity` (`ServerSystemEntitySimulation.cs:214-222`).

**Is the data gone?** For every reason except `Unload`, `DespawnEntity` removes the entity from `LoadedEntities` and from its chunk (`ServerMain.cs:2990-2993`). It is not saved again, so the entity and its `Attributes`/`WatchedAttributes` are gone for good. The `Entity` object is still intact inside the `OnEntityDespawn` handler. Copy anything you need (e.g. "hammock at X has no owner now") into the hammock's block entity or the map chunk moddata there. For `Unload`, the entity comes back through `OnEntityLoaded` when the chunk reloads.

---

## 9. Time of day

`IGameCalendar`, available as `world.Calendar` on both sides (`API/Common/API/IGameCalendar.cs`):

| Member | Line | Notes |
|---|---|---|
| `float HourOfDay` | 252 | `TotalHours % HoursPerDay` (`LIB/Vintagestory.Common/GameCalendar.cs:104`) |
| `int FullHourOfDay` | 247 | |
| `float HoursPerDay` | 228 | 24 by default (`GameCalendar.cs:16`). Not guaranteed 24 |
| `double TotalHours` / `TotalDays` | 257, 262 | |
| `float SpeedOfTime` | 191 | Doc: 60 by default, times `CalendarSpeedMul` 0.5, giving a 48-minute real-time day |
| `float CalendarSpeedMul` | 223 | |
| `float GetDayLightStrength(double x, double z)` | 155 | Location-dependent (latitude/season via `GetCelestialAngles`). Roughly 0..1.2, and includes moonlight (`GameCalendar.cs:390-410`) |
| `Vec3f GetSunPosition(Vec3d pos, double totalDays)` | 171 | Normalised vector |
| `EnumSeason GetSeason(BlockPos)` | 285 | |

**Recommended "is it night here":**
```csharp
bool isNight = world.Calendar.GetSunPosition(pos.ToVec3d(), world.Calendar.TotalDays).Y < 0;
```
This is the form vanilla spawn code uses: `SURV/Systems/TemporalStability/TemporalStability.cs:811-812`, `var sunpos = api.World.Calendar.GetSunPosition(spawnPosition, api.World.Calendar.TotalDays); bool isDaytime = sunpos.Y >= 0;`.

The softer alternative is `GetDayLightStrength(x, z) < 0.6`, the threshold butterflies use (`SURV/Entity/AITask/AiTaskButterflyRest.cs:83`). Moonlight can raise it at night.

**AI task convention.** AI tasks use the hour normalised to a 24h clock:
- Legacy `AiTaskBase.IsInValidDayTimeHours` (`ESS/Entity/AI/AiTaskBase.cs:332-350`):
  ```csharp
                  double hourOfDay = entity.World.Calendar.HourOfDay / entity.World.Calendar.HoursPerDay * 24f;
  ```
  It uses JSON `duringDayTimeFrames` (`DayTimeFrame` struct with `Matches(hourOfDay)` handling midnight wrap, lines 12-22) plus `DayTimeFrameInaccuracy` jitter.
- The refactored base (`ESS/Entity/AI/TasksRefactored/AiTaskBase.cs:196-208, 678-690`) uses JSON `duringDayTimeFramesHours: [[from,to],...]`, `DayTimeFrameInaccuracy` (default 3) and `StopIfOutOfDayTimeFrames`, and computes `HourOfDay / HoursPerDay * 24 + inaccuracy`.

Hour-based frames are global, because `HourOfDay` does not depend on location. Sun position depends on latitude and season. Use the sun test for "night at this spider", and hours for schedules.

---

## Surprises and pitfalls

1. **`OnEntitySpawn` never fires for worldgen creatures.** They are *loaded* (`OnEntityLoaded`, `AfterInitialized(false)`, `origin="worldgen"`), whereas runtime spawns fire `OnEntitySpawn` and never `OnEntityLoaded`, despite the doc comment. Handle both, and mark processed entities with an attribute.
2. **`OnTrySpawnEntity` runs on two threads with two accessor types:** main thread with RelaxedBlockAccess (runtime), and the worldgen thread with `IWorldGenBlockAccessor` (worldgen). Any shared state must be thread-safe. In worldgen, changing `ref properties` does nothing, because the entity is already created.
3. **Light and `minDistanceToPlayer` are not engine checks for runtime spawns.** They exist only in survival's `TemporalStability.CanSpawnNearby`, installed per player as the single delegate `EntityPlayer.OnCanSpawnNearby`. Do not overwrite that delegate. Worldgen ignores `lightLevelType`. The default `MaxLight` ignores time of day, so a light threshold is not a day/night test.
4. **A `climate` block silently overwrites** temperature, rain, forest, shrub, Y range, habitat size and map code in **both** runtime and worldgen, including fields you did not set (they get defaults).
5. Runtime temperature is always sampled at **seaLevel×1.09**. `climateValueMode: NowValues` still compares *worldgen* temperature (only rainfall becomes "now").
6. `chance` is a per-pass (~2/s) probability that the type is considered, not a per-position chance. `maxQuantity` counts **all loaded entities** of that code, scaled by player count.
7. Runtime spawn search is limited to ±3 chunk columns around each player, and to the player's chunk-Y ±3 unless `tryOnlySurface`. Group members stay in the same chunk as the first position.
8. **Despawn behavior without `minPlayerDistance`/`belowLightLevel` despawns after `minSeconds` (default 30 s) regardless of players.** `Expire`/`Death`/`Removed` delete the entity permanently. Persist what you need in `OnEntityDespawn`.
9. `IBlockLog` is implemented only by `BlockLog`. Resin, cypress and redwood-section trunks are plain `Block`/`BlockLogSection`. Use `treeFellingGroupCode` + `BlockMaterial.Wood`, or code prefixes.
10. The vanilla "OnTrees" placement looks only 0..4 blocks above the tree base, on diagonal cells, which rarely touch 1-wide trunks (inferred from code). It cannot put anything high on a trunk without code.
11. `string.GetHashCode()` is per-process random in .NET 10. Vanilla's `randomMapCodePool` and animal maps depend on it, so they are not stable across restarts. Use `GameMath.MurmurHash3*`/`LCGRandom` with `World.Seed`.
12. `IServerChunk.SetServerModdata` does not mark the chunk dirty. `IWorldChunk` moddata is sent to clients and is unlocked. `IMapChunk.RemoveModdata` is unlocked. Prefer map chunk `SetModdata`/`GetModdata`.
13. The worldgen accessor's `SetBlock` writes into already-loaded chunks without client sync, and its `AddEntity`/`SpawnBlockEntity` silently do nothing outside generating chunks. Keep worldgen writes inside the current column.
14. `World.SpawnEntity(e, type)` ignores `type`. `Entity.ServerPos` is obsolete (alias of `Pos`).
15. `IServerChunk.BlocksPlaced`/`BlocksRemoved` count player placements and removals per chunk. This is a cheap "player base" heuristic, alongside `World.Claims.Get(pos)`.

## Recommendation

Use a **hybrid of (b) and (c)**, keeping vanilla JSON only for gating data:

1. **Chunk selection:** `IsSpiderChunk(cx, cz)` from `GameMath.MurmurHash3Mod(cx, World.Seed ^ Salt, cz, N)` or a jittered grid. Cache the result and the hammock position in **map chunk moddata**.
2. **Worldgen pass (b),** Vegetation pass, ExecuteOrder ≈ 0.6: in spider chunks with a suitable climate (`GetClimateAt(..., WorldGenValues)` plus your own `RuntimeSpawnConditions.MatchesClimate/MatchesForestation`), find a grown trunk via the heightmap, place the hammock high on it with the worldgen accessor, record it in map chunk moddata, and optionally `AddEntity` an initial spider (it will arrive through `OnEntityLoaded`).
3. **Runtime (c):** a main-thread tick listener (e.g. every 5-10 s, amortised per player) handles respawning:
   - visits loaded spider chunks near players;
   - **reuses the recorded hammock**, or finds a tree and places one in chunks generated before the mod was installed;
   - checks caps, claims, `BlocksPlaced`, distance and sun position;
   - spawns the spider *asleep in the hammock at night* or within a radius by day;
   - calls `TriggerTrySpawnEntity` for mod compatibility.

   Keep `runtime` out of the JSON, or set `maxQuantity: 0` if you only want the fields for `Matches*`.
4. Put the `"despawn"` behavior on wild spiders, with `minPlayerDistance` set. In `OnEntityDespawn`, clear the hammock's owner unless the reason is `Unload`, and leave the block in place.

Option (a) is viable as a lighter alternative (one `OnTrySpawnEntity` hook covering runtime and worldgen). However, the ground-only search of the vanilla spawner and its two-thread behaviour make "spawn asleep in a hammock" and "reuse the nearby hammock" awkward. (c) keeps all of that in one main-thread place, and (b) gives cheap, deterministic hammock placement in new terrain.

## Not verified

- The actual in-game success rate of `OnTrees` placement on 1-wide trunks (inferred from geometry only).
- Whether leaves carry `treeFellingGroupCode` (not checked; guard with `BlockMaterial`).
- `IMapRegion` moddata locking and persistence (not inspected).
- Behaviour with `MaxWorldgenThreads > 1` (default is 1; with more threads, passes run on different threads and the single accessor fetched in `GetWorldgenBlockAccessor` may be used from another thread; not analysed).
- Decompiled line numbers depend on the ilspycmd version above.

> Reference notes on Vintage Story 1.22.7 internals, gathered from the game sources when this mod was started.
> They describe the engine, not the mod's current design (see `CLAUDE.md` and `README.md` for that). File paths in
> them are from the machine they were written on.

# Entity AI and behavior framework: Vintage Story 1.22.7 reference

Scope: the server-side creature AI (task AI, emotion states, pathfinding), entity physics, damage and death, breeding and growth, and attribute storage. I checked every claim against the 1.22.7 sources on this machine. Paths are abbreviated as follows:

| Prefix | Full path |
|---|---|
| `ESS/` | `/home/user/anegostudios/vsessentialsmod/` |
| `API/` | `/home/user/anegostudios/vsapi/` |
| `SURV/` | `/home/user/anegostudios/vssurvivalmod/` |
| `ASSETS/` | `/home/user/vs-game/1.22.7/assets/survival/` |
| `LIB` | `/home/user/vs-game/1.22.7/VintagestoryLib.dll` (closed source, decompiled with ilspycmd where noted) |

The note "(unverified)" marks anything I could not confirm in source.

---

## 1. AI task framework

### 1.1 Two generations of task classes exist side by side

| Generation | Base classes | File | JSON codes |
|---|---|---|---|
| Classic | `public abstract class AiTaskBase : IAiTask` (namespace `Vintagestory.API.Common`) and `public abstract class AiTaskBaseTargetable : AiTaskBase, IWorldIntersectionSupplier` (namespace `Vintagestory.GameContent`) | `ESS/Entity/AI/AiTaskBase.cs:25`, `ESS/Entity/AI/AiTaskBaseTargetable.cs:16` | `wander`, `lookaround`, `meleeattack`, `seekentity`, `fleeentity`, `stayclosetoentity`, `getoutofwater`, `idle`, `seekfoodandeat`, `seekblockandlay`, `useinventory`, and others (`ESS/Entity/AI/AiTaskManager.cs:35-55`) |
| Refactored ("R") | `public abstract class AiTaskBaseR : IAiTask` with config class `AiTaskBaseConfig`, and `public abstract class AiTaskBaseTargetableR : AiTaskBaseR, IWorldIntersectionSupplier` with `AiTaskBaseTargetableConfig` | `ESS/Entity/AI/TasksRefactored/AiTaskBase.cs:28,341`, `ESS/Entity/AI/TasksRefactored/AiTaskBaseTargetable.cs:27,259` | `wander-r`, `fleeentity-r`, `idle-r`, `meleeattack-r`, `seekentity-r`, `shootatentity-r`, and others (`AiTaskManager.cs:57-73`). Survival adds `followleadholder-r` (`SURV/Systems/Core.cs:216`) |

**Vanilla 1.22.7 creature JSONs use only the classic family.** I grepped every asset for `code: "...-r"` and found no matches. The task codes each creature uses:

- **Wolf** (`ASSETS/entities/animal/mammal/wolf-adult.json:185-401`): meleeattack, fleeentity, seekentity, followleadholder, getoutofwater, stayclosetoentity, idle, seekfoodandeat, wander, lookaround.
- **Fox** (`fox-adult.json:150-335`): the same set, all classic.
- **Chicken** (`ASSETS/entities/animal/bird/chicken-adult.json:170-365`): classic, plus seekblockandlay.
- **Hare** (`hare-adult.json:145-277`): classic.
- **Raccoon** (`raccoon-adult.json:116-255`): classic, plus useinventory.
- **Drifter** (`ASSETS/entities/lore/drifter.json:198-350`): classic, plus `throwatentity`.

Both families implement the same interface and are run by the same manager.

### 1.2 `IAiTask` (`ESS/Entity/AI/IAiTask.cs:7-85`)

```csharp
string? Id { get; }
int Slot { get; }
float Priority { get; }
float PriorityForCancel { get; }
string ProfilerName { get; set; }
bool ShouldExecute();
void StartExecute();
bool ContinueExecute(float dt);
void FinishExecute(bool cancelled);
void AfterInitialize();
void OnStateChanged(EnumEntityState beforeState);
bool Notify(string key, object data);
void OnEntityLoaded();
void OnEntitySpawn();
void OnEntityDespawn(EntityDespawnData reason);
void OnEntityHurt(DamageSource source, float damage);
bool CanContinueExecute();
```

### 1.3 How tasks are built from JSON

`EntityBehaviorTaskAI` (`ESS/Entity/AI/BehaviorTaskAI.cs:9`, code `"taskai"`) handles construction:

- It reads `shuffle` (line 54), `aiCreatureType` (a string parsed with `Enum.TryParse`, line 56) and the `aitasks` array (line 64).
- It skips a task whose `enabled` is false (line 69). It looks the task `code` up in `AiTaskRegistry.TaskTypes` (line 82).
- It instantiates the task with **`Activator.CreateInstance(taskType, entityAgent, taskConfig, attributes)`** (line 91). Every custom task therefore needs a public constructor `(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)`.
- It only works on `EntityAgent` (lines 48-52).
- It creates a single `WaypointsTraverser` for the entity (line 62). All tasks share it through `entity.GetBehavior<EntityBehaviorTaskAI>().PathTraverser`.

**Classic configuration** (`AiTaskBase` constructor, `AiTaskBase.cs:197-283`):

- `JsonUtil.Populate(taskConfig.Token, this)` (line 216) fills every `[JsonProperty]` member. The class is `[JsonObject(MemberSerialization = MemberSerialization.OptIn)]` (line 24). Newtonsoft matches names case-insensitively, and `JsonObject` indexers are case-insensitive too (`API/Datastructures/JsonObject.cs:55`).
- Classic `[JsonProperty]` members on `AiTaskBase`:
  - `Id`, `Priority`, `priorityForCancel`, `Slot`, `ExecutionChance` (default 1)
  - `TagsAppliedToEntity`
  - `MinCooldownMs` / `Mincooldown` (default 0), `MaxCooldownMs` / `Maxcooldown` (default 100), `MinCooldownHours`, `MaxCooldownHours`
  - `soundRange`, `soundStartMs`, `soundRepeatMs`, `soundChance`
  - `WhenSwimming`, `WhenInEmotionStates` / `WhenInEmotionState` (string, split on `|`), `WhenNotInEmotionStates` / `WhenNotInEmotionState`
  - `duringDayTimeFrames` (`DayTimeFrame[]`, JSON `[{ fromHour: 20, toHour: 6 }]`), `DayTimeFrameInaccuracy` (default 0.15 h)
  - `EntityLightLevels` (default `[0,32]`), `EntityLightLevelType`, `TemperatureRange`
- Keys read by hand in the constructor:
  - `id`, `initialMinCoolDown`, `initialMaxCoolDown` (lines 221-224)
  - `minGeneration`, `maxGeneration` (226-227)
  - `timeoutSec` (default 30, line 229)
  - `animation`, `animationSpeed` (231-261). The animation is looked up in `Properties.Client.Animations` by lowercase code. If none is found, a new `AnimationMetaData` with ease speeds of 1 is created.
  - `sound`, `finishSound` (263-272)
- Subclasses read their own keys in their constructors, e.g. `taskConfig["movespeed"].AsFloat(0.02f)`.
- `protected virtual void SetDefaultValues()` (line 287) runs **before** populate, so subclasses can change defaults. For example, seek, flee and throw set `ExecutionChance = 0.1` (`AiTaskSeekEntity.cs:102-106`, `AiTaskFleeEntity.cs:63-68`, `AiTaskThrowAtEntity.cs:50-54`).
- A JSON property with a `*ByType` suffix (for example `mincooldownMsByType`, `damageByType`) is resolved per variant by the entity type loader. Vanilla uses this inside aitasks (`chicken-adult.json:310-311`, `drifter.json:236-249`).

**Refactored configuration** (`AiTaskBaseR` constructor, `TasksRefactored/AiTaskBase.cs:406-436`):

- The constructor calls `LoadConfig<TConfig>(entity, taskConfig, aiConfig)`, which does `taskConfig.AsObject<TConfig>()` and then `config.Init(entity, taskConfig, aiConfig)` (lines 581-622). A subclass replaces `baseConfig` with its own config type, e.g. `baseConfig = LoadConfig<AiTaskSeekEntityConfig>(...)` (`TasksRefactored/AiTaskSeekEntity.cs:217-220`).
- Key renames (documented at lines 17-25):
  - `mincooldown` → `minCooldownMs`
  - `initialMinCoolDown` → `initialMinCooldownMs`
  - `whenInEmotionState` becomes a `string[]`
  - day frames use `duringDayTimeFramesHours: [[20,6]]` (line 196)
  - `DayTimeFrameInaccuracy` defaults to **3** hours (line 203)
  - `ExecutionChance` defaults to **0.1** (line 219)
- New fields:
  - `StopOnHurt`, `MinDurationMs` / `MaxDurationMs`, `WhenFeetInLiquid`, `StopIfOutOfDayTimeFrames`
  - `RecentlyAttackedTimeoutMs` (default 30000), `DontExecuteIfRecentlyAttacked`
  - `RandomizePitch`, `SoundVolume`

### 1.4 Gating: cooldowns, chance, day time, emotions, generation

**Classic `PreconditionsSatisfied()`** (`AiTaskBase.cs:319-330`) checks, in order:

1. `WhenSwimming`
2. `WhenInEmotionStates`
3. `WhenNotInEmotionStates`
4. `minGeneration` / `maxGeneration` against `WatchedAttributes.GetInt("generation")`
5. temperature
6. light level
7. `IsInValidDayTimeHours(true)`

**It does not check the cooldown or `ExecutionChance`.** Each task checks those itself in `ShouldExecute`:

- seek: `rand.NextDouble() > ExecutionChance` (line 114) and `cooldownUntilMs` (line 126)
- melee: line 63
- idle: line 99
- throw: lines 60 and 65
- **`AiTaskFleeEntity.ShouldExecute` never checks the cooldown** (`AiTaskFleeEntity.cs:74-152`)

A custom classic task should call `IsOnCooldown()` (`AiTaskBase.cs:292-295`) and test `ExecutionChance` explicitly.

**Cooldowns:**

- The cooldown is reset in `FinishExecute` (`ResetCooldown()`, lines 297-301 and 427). It is randomized between min and max in milliseconds (`World.ElapsedMilliseconds`) and in game hours (`Calendar.TotalHours`).
- The initial cooldown is set at construction (line 224).
- The cooldown is reset again when the entity becomes Active (`OnStateChanged`, lines 452-460).

**Day-time frames:**

- `DayTimeFrame.Matches` handles wrap past midnight (`AiTaskBase.cs:12-22`).
- Hours are normalized to a 24 h day: `Calendar.HourOfDay / Calendar.HoursPerDay * 24` (line 337).
- Classic `ContinueExecute` returns false once the time leaves the frame (lines 420 and 156 in Idle). Classic tasks that override `ContinueExecute` re-check this themselves (e.g. seek line 359, melee 118, throw 113).

**Emotion gating** uses `EntityBehaviorEmotionStates.IsInEmotionState(code)` (`ESS/Entity/Behavior/BehaviorEmotionStates.cs:152-155`). If the entity has no emotionstates behavior, `IsInEmotionState` returns false (`AiTaskBase.cs:352-362`). A task with `whenInEmotionState` therefore never runs on such an entity.

**Refactored `PreconditionsSatisficed()`** (sic, `TasksRefactored/AiTaskBase.cs:633-649`) does include execution chance, cooldowns, entity state, emotions, light, generation, day frames, temperature and the recently-attacked check.

### 1.5 Priority, priorityForCancel, slot; how the manager picks and interrupts tasks

`AiTaskManager` (`ESS/Entity/AI/AiTaskManager.cs:99-532`) has `ActiveTasksSlotsNumber = 8` (line 111). `OnGameTick` runs `StartNewTasks()` and then `ProcessRunningTasks(dt)` (lines 139-141).

**`StartNewTasks`** (lines 448-473) iterates the tasks **in list order (JSON order, or shuffled if `shuffle: true`)**. It does not sort by priority. For each task:

```csharp
if (task.Priority < 0) continue;
IAiTask? oldTask = activeTasksBySlot[slot];
if ((oldTask == null || task.Priority > oldTask.PriorityForCancel) && task.ShouldExecute() && ShouldExecuteTask(task))
{ oldTask?.FinishExecute(true); ... activeTasksBySlot[slot] = task; task.StartExecute(); ... }
```

What follows from this:

- A task in a slot can be pre-empted only by a task whose `Priority` is strictly greater than the running task's `PriorityForCancel`. The default `PriorityForCancel` equals `Priority` (classic line 47, refactored line 259).
- Tasks in different slots run concurrently. Vanilla runs melee in slot 1 alongside seek in slot 0 (`fox-adult.json:154-180`, `wolf-adult.json:187-234`).
- `ShouldExecute()` runs before the delegates, so the task's target fields are already set when `OnShouldExecuteTask` delegates run.
- The running task itself is also evaluated. If its `Priority > PriorityForCancel` (only possible when you set a lower `priorityForCancel`) and its `ShouldExecute()` returns true, it gets restarted. Keep `priorityForCancel >= priority`.
- A newly started task gets its first `ContinueExecute` in the same tick.

**`ProcessRunningTasks`** (lines 488-508):

- If `CanContinueExecute()` returns false, that tick is skipped. Pathing tasks use this to wait for the async path, as in `pathTraverser.Ready`.
- If `ContinueExecute(dt)` returns false, it calls `FinishExecute(false)` and clears the slot.

**Events** (lines 103-109):

```csharp
public event Action<IAiTask>? OnTaskStarted;
public event Action<IAiTask>? OnTaskStopped;
public event ActionBoolReturn<IAiTask>? OnShouldExecuteTask;   // all must return true
public event OnExecuteTaskDelegate? OnExecuteTask;             // bool (IAiTask taskToExecute, IAiTask? activeTask), only for ExecuteTask()
```

Vanilla uses `OnShouldExecuteTask` to veto tasks:

- `EntityBehaviorPettable` blocks all tasks while being petted (`SURV/Entity/Behavior/BehaviorPettable.cs:40-58`).
- `EntityBehaviorSemiTamed` blocks flee tasks targeting players (`ESS/Entity/Behavior/EntityBehaviorSemiTamed.cs:20-33`). I found no `RegisterEntityBehaviorClass("semitamed", ...)` call in the open repos, so it looks unused, but VintagestoryLib may register it (unverified).

**Forcing tasks from code:**

- `ExecuteTask(IAiTask task, int slot)` (line 171), `ExecuteTask<TTask>()` (209), `ExecuteTask(string id)` (223)
- `StopTask(...)` overloads (240-298), `StopTasks()` (303)
- `IsTaskActive(string id)` / `IsTaskActive<TTask>()` (319-337)
- `GetTask<TTask>()`, `GetTask(string id)`, `GetTasks<TTask>()`, and `AllTasks` / `ActiveTasksBySlot`

`Notify(key, data)` (340-399):

- `"starttask"` / `"stoptask"` with a task id force-start or stop a task.
- Any other key is offered to every task's `Notify`. A task that returns true starts if `Priority > active.PriorityForCancel`.
- `EntityBehaviorTaskAI.Notify` forwards to it (`BehaviorTaskAI.cs:133`), so `entity.Notify("starttask", "myTaskId")` works from anywhere.

**AI only runs when the entity is Active and alive:** `if (entity.State != EnumEntityState.Active || !entity.Alive) return;` (`BehaviorTaskAI.cs:115`). The world config `runAiTasks` can disable all AI (`AiTaskManager.cs:91-95, 120-131`).

### 1.6 Registering classes from a mod (exact calls)

- **AI task.** There are two equivalent ways:
  - The extension `public static void RegisterAiTask<TTask>(this ICoreServerAPI serverAPI, string code) where TTask : IAiTask` (`AiTaskManager.cs:16`, namespace `Vintagestory.GameContent`).
  - Direct `AiTaskRegistry.Register<TTask>(string code)` (line 27). Vanilla survival calls this directly in `StartServerSide`, e.g. `AiTaskRegistry.Register<AiTaskThrowAtEntity>("throwatentity");` (`SURV/Systems/Core.cs:211-217`).
  - **Every concrete task type that gets added to a manager must be registered.** `AddTask` does `AiTaskRegistry.TaskCodes[task.GetType()]` (line 149), which throws `KeyNotFoundException` for an unregistered type. This also applies to a subclass of a vanilla task.
- **Entity behavior:** `void RegisterEntityBehaviorClass(string className, Type entityBehavior);` (`API/Common/API/ICoreAPI.cs:53`). Register it on both sides (in `Start`). The constructor must be `(Entity entity)` (`API/Common/Entity/EntityBehavior.cs:22`) and must override `public abstract string PropertyName();` (line 75). For an example see `ESS/Core.cs:144-178`.
- **Entity class:** `void RegisterEntity(string className, Type entity);` (`ICoreAPI.cs:46`). Register it on both sides and reference it from JSON `class: "..."`. Vanilla creatures use `class: "EntityAgent"` (`wolf-adult.json:3`). Task AI requires an `EntityAgent` subclass.

A minimal ModSystem. This is illustrative and I have not compiled it:

```csharp
public class JumpingSpidersMod : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterEntity("EntityJumpingSpider", typeof(EntityJumpingSpider));          // : EntityAgent
        api.RegisterEntityBehaviorClass("jsdomestication", typeof(EntityBehaviorJsDomestication));
    }
    public override void StartServerSide(ICoreServerAPI sapi)
    {
        sapi.RegisterAiTask<AiTaskJsLeap>("js-leapattack");     // ctor (EntityAgent, JsonObject, JsonObject)
    }
}
```

---

## 2. Existing wind-up, throw and leap tasks

### 2.1 Drifter rock throw: `AiTaskThrowAtEntity` (`SURV/Entity/AITask/AiTaskThrowAtEntity.cs`)

This is the cleanest "wind-up, then action at time T, then finish" template. The Eidolon slam (`SURV/Lore/ResoArchives/Eidolon/AiTaskEidolonSlam.cs:98-99, 159-238`) uses the identical `durationMs` / `releaseAtMs` / `accum` pattern.

**Configuration** (lines 33-48): `durationMs` (default 1500), `releaseAtMs` (default 1000), `projectileDamage`, `projectileDamageTier`, `maxDist` (15), `yawInaccuracy`, `projectileCode` (`"thrownstone-{rock}"`), `immobile`, `maxTurnAngleDeg`, `maxOffAngleThrowDeg`. Drifter values are `durationMs: 1200`, `releaseAtMs: 700`, `priority: 1.5`, **`priorityForCancel: 9`** (so nothing interrupts the throw), `mincooldown: 1000`, `maxcooldown: 10000` and `animation: "standthrowleft"` (`ASSETS/entities/lore/drifter.json:267-283`).

**`ShouldExecute`** (57-73):

1. chance gate, bypassed when the emotion state matches (line 60)
2. `PreconditionsSatisfied()`
3. a search throttle of `searchWaitMs` = 2000 (line 63)
4. `cooldownUntilMs` (line 65)
5. a target search with `partitionUtil.GetNearestEntity(entity.Pos.XYZ, range, e => IsTargetableEntity(e, range) && hasDirectContact(e, range, range/2f) && aimableDirection(e), EnumEntitySearchType.Creatures)` (line 70)

**`StartExecute`** (84-105): calls `base.StartExecute()`, which starts the `animation`, plays the sound and records `ExecutionStartTimeMs`. It then resets `accum` and `didThrow` and computes the turn speed from `server.attributes.pathfinder.minTurnAnglePerSec` / `maxTurnAnglePerSec`.

**`ContinueExecute(dt)`** (109-184), each tick:

- Turns yaw toward the target, clamped by `curTurnRadPerSec * dt` (115-120).
- Re-starts the animation if needed (122-127).
- Does `accum += dt` (129).
- **When `accum > releaseAtMs / 1000f && !didThrow`** (131): spawns the projectile, sets `Pos.Motion` to a normalized aim vector multiplied by a distance-based speed (`Math.Pow(dist², 0.1)` clamped to [0.1, 1]; 161-177), and calls `World.SpawnPriorityEntity`.
- Returns `accum < durationMs / 1000f` (183).

`FinishExecute` is inherited. It resets the cooldown and stops the animation unless its code is `"attack"` or `"idle"` (`AiTaskBase.cs:425-445`).

Timing uses the summed `dt` of AI ticks, not animation frames. There is `IAnimationManager.RegisterFrameCallback(AnimFrameCallback)` (`API/Common/Model/Animation/IAnimator.cs:225`, `AnimFrameCallback { float Frame; string Animation; Action Callback; }` at `API/Common/Model/Animation/AnimatorBase.cs:15-20`). Whether server-side animators tick frames for ordinary creatures is unverified. Every vanilla attack task times itself in ms, so do the same.

### 2.2 Melee with a hit delay: `AiTaskMeleeAttack` (`ESS/Entity/AI/Tasks/AiTaskMeleeAttack.cs`)

The wolf uses it with `animation: "attack-withwindup"`, `attackDurationMs: 1200` and `damagePlayerAtMs: 900` (`wolf-adult.json:186-201`).

- **Configuration** (39-58): `damage`, `knockbackStrength` (defaults to `sqrt(damage/4)`), `attackAngleRangeDeg` (20), `attackDurationMs` (1500), `damagePlayerAtMs` (1000), `minDist` (2), `minVerDist` (1), `damageType`, `damageTier`. **`attackRange` is fixed at 3 (line 36) and is not read from JSON.**
- **`ShouldExecute`** (60-101):
  - `elapsed - lastCheckOrAttackMs < attackDurationMs || cooldownUntilMs > elapsed` returns false.
  - Then the preconditions, then generation taming (section 4.4).
  - Then retaliation: `ShouldRetaliateForRange(15) && hasDirectContact(attackedByEntity, ...)`.
  - Otherwise it searches `World.GetNearestEntity(pos, attackRange*fearReductionFactor, ...)` for `IsTargetableEntity(e,15) && hasDirectContact(e, minDist, minVerDist)`.
- **`StartExecute`** (107-113) defers `base.StartExecute()`, and so the animation, until the creature is facing the target (135-139).
- **`ContinueExecute`** (115-152): turns toward the target. When `lastCheckOrAttackMs + damagePlayerAtMs` has passed, it calls `attackTarget()` once (144-148). It ends at `attackDurationMs`.
- **`attackTarget()`** (155-183): re-checks `hasDirectContact`, then calls `targetEntity.ReceiveDamage(new DamageSource{ Source = EnumDamageSource.Entity, SourceEntity = entity, Type = damageType, DamageTier = damageTier, KnockbackStrength = knockbackStrength }, damage * GlobalConstants.CreatureDamageModifier)`. It calls `IMeleeAttackListener.DidAttack(targetEntity)` if the entity class implements it (line 173). On a kill of a non-player it sets `WatchedAttributes "lastMealEatenTotalHours"` and triggers the `saturated` emotion (178-182).
- **`hasDirectContact(Entity, float minDist, float minVerDist)`** (`AiTaskBaseTargetable.cs:293-331`) does a box-distance check from a point in front of the attacker's face, then up to three raycasts with `World.RayTraceForSelection`. Reuse it for the "did we land on the target" check.

### 2.3 Existing leap: `seekentity` with `leapAtTarget`

**Classic** (`ESS/Entity/AI/Tasks/AiTaskSeekEntity.cs`):

- **Configuration** (72-100): `leapAtTarget` (default false), `leapAnimation` (default `"jump"`; null means no animation, line 78), `leapChance` (default 1), `leapHeightMul` (default 1). The wolf uses `leapAtTarget: true, leapAnimation: null, leapChance: 0.01` (`wolf-adult.json:256-259`).
- **The jump** (409-442), evaluated every tick while seeking. If `rand < leapChance`, distance is in (0.5, 4), there was no jump in the last 3000 ms and `targetEntity.Pos.Y + 0.1 >= entity.Pos.Y`:

  ```csharp
  double dx = (targetEntity.Pos.X + targetEntity.Pos.Motion.X * 80 - entity.Pos.X) / 30;
  double dz = (targetEntity.Pos.Z + targetEntity.Pos.Motion.Z * 80 - entity.Pos.Z) / 30;
  entity.Pos.Motion.Add(dx, leapHeightMul * GameMath.Max(0.13, (targetEntity.Pos.Y - entity.Pos.Y) / 30), dz);
  entity.Pos.Yaw = (float)Math.Atan2(dx, dz);
  ```

  After that it stops the `walk` and `run` animations and starts the leap animation (429-435). Mid-air, if the distance is below 0.5 and the entity has not collided, it halves the motion (438-441).
- **Damage** is not dealt by the leap. The separate `meleeattack` task in slot 1 deals it.

**Refactored** (`ESS/Entity/AI/TasksRefactored/AiTaskSeekEntity.cs`) is better structured and fixes a physics problem (see 3.3):

- **Configuration** (lines 34-175): `JumpAtTarget`, `JumpAnimationCode`, `JumpChance`, `JumpHeightFactor`, `JumpCooldownMs` (3000), `DistanceToTargetToJump` (`[0.5, 4]`), `MaxHeightDifferenceToJump` (0.1), `JumpMotionAnticipationFactor` (80), `JumpSpeedFactor`, `AfterJumpSpeedReduction` (0.5), `MaxVerticalJumpSpeed` (0.13), `AnimationToStopForJump` (`["walk","run"]`), `JumpAnimationTimeoutMs` (2000).
- **`PerformJump()`** (547-593):
  - `dx = (tx - x + tmx*80) * JumpSpeedFactor * 0.033 * 0.5`, and likewise for `dz`.
  - `dy = JumpHeightFactor * max(MaxVerticalJumpSpeed, (ty - y)*0.033)`.
  - Stores `jumpHorizontalVelocity = (dx + motionX, 0, dz + motionZ)`, adds `(dx, dy, dz)` to `Pos.Motion` and sets yaw.
  - On a jump it sets `updatedPathAfterLanding = false` and calls `pathTraverser.Stop()` (320-324).
- **`ContinueExecute`** (276-338):
  - While `!entity.OnGround` after a jump, it **re-applies `entity.Pos.Motion.X/Z = jumpHorizontalVelocity.X/Z` every AI tick** (305-309).
  - When `entity.OnGround && entity.Collided` after the jump it re-paths and marks `updatedPathAfterLanding` (295-300).

### 2.4 Recommended shape for a "wind-up then leap" task, built from the patterns above

Base it on `AiTaskBaseTargetable` (classic) and drive it as a state machine on accumulated `dt`:

1. **ShouldExecute.** Test cooldown and chance yourself. Call `PreconditionsSatisfied()`. Throttle the search like throw does (`searchWaitMs`). Find a target with `partitionUtil.GetNearestEntity(...)` using `IsTargetableEntity`. Require a distance window and line of sight (`hasDirectContact` with a large `minDist`, as throw does at line 70).
2. **StartExecute.** Call `pathTraverser.Stop()`, which zeroes `Controls.WalkVector` (`WaypointsTraverser.cs:566-576`). Call `base.StartExecute()` to start the JSON `animation` (the wind-up). Reset `accum`.
3. **ContinueExecute, wind-up phase.** Turn toward the target (throw lines 115-120). When `accum >= launchAtMs/1000f`, compute the launch motion (section 3), set `entity.Pos.Motion`, stop the wind-up animation and start the leap animation.
4. **ContinueExecute, airborne phase.** Re-assert the horizontal motion each tick while airborne (refactored seek lines 305-309). Detect take-off and landing (section 3.4). On landing, if `hasDirectContact(target, r, rv)`, apply `ReceiveDamage` as melee does. Then return false.
5. **Safety timeout.** Use `timeoutExceeded()` (`AiTaskBase.cs:497`, `timeoutSec` JSON, default 30 s) or your own maximum air time.
6. **Interruption.** Use a high `priorityForCancel`, as the drifter throw does with 9.

---

## 3. Server-side motion and physics

### 3.1 Which fields to set

- **`entity.Pos`** is the only position. `ServerPos` and `SidedPos` are `[Obsolete("Use Pos instead.")] => Pos` (`API/Common/Entity/Entity.cs:177-183`). **Set `entity.Pos.Motion` (a `Vec3d`) and `entity.Pos.Yaw`**, as vanilla leap code does.
- Physics for creatures comes from `EntityBehaviorControlledPhysics` (code `controlledphysics`, `ESS/Entity/Behavior/BehaviorControlledPhysics.cs:31`). It registers itself with `esapi.Server.AddPhysicsTickable(this)` (line 137).
- The tick runs `SetState`, then `MotionAndCollision` (the modules), then `ApplyTests` (the move and collision) in `OnPhysicsTick` (572-597).
- **The physics tick is a fixed 1/30 s.** In decompiled LIB (`Vintagestory.Server.PhysicsManager`) the constant is `private const float tickInterval = 1f / 30f;`, `DoWork` uses `float dt = 1f / 30f * rateModifier;`, and catch-up is capped at 0.4 s. `API/Common/Entity/IPhysicsTickable.cs:8` says "potentially 30 times per second".
- Physics runs on the main thread unless there are more than 800 tickables, in which case it uses worker threads (decompiled `PhysicsManager.ServerTick`: `if (tickables.Count > 800 && maxPhysicsThreads > 1)`).
- AI ticks come from entity `OnGameTick`. `ServerSystemEntitySimulation.GetUpdateInterval()` returns 20 ms (decompiled), so the AI and physics tick rates differ.

### 3.2 Units, gravity and drag

- **Motion is measured in blocks per 1/60 s.** `ApplyTests` computes `nextX = motion.X * dtFactor + pos.X` with `dtFactor = dt * 60` (`BehaviorControlledPhysics.cs:316, 442-444`). Speed in blocks per second is `motion × 60`. At a 30 Hz physics tick each step moves `2 × motion`.
- **Module order** (`SetModules`, 119-128): `PModuleWind`, `PModuleOnGround`, `PModuleInLiquid`, `PModuleInAir`, `PModuleGravity`, `PModuleMotionDrag`, `PModuleKnockback`.
- **Gravity** (`API/Common/Entity/Physics/Normal/PModuleGravity.cs:39-50`):
  - `motion.Y -= (gravityPerSecond + max(0, -0.015*motion.Y)) * (FeetInLiquid ? 0.33 : 1) * dt`
  - `GlobalConstants.GravityPerSecond = 0.37f` (`API/Config/GlobalConstants.cs:132`). That is 0.37 motion units per second, about 22.2 blocks/s², plus a small extra pull while falling.
  - It is skipped for `Habitat == Air`, for swimming sea creatures, while flying, while climbing, or if `entity.ApplyGravity` is false (25-37).
  - Configurable as `attributes.physics.gravityFactor` (line 17).
- **Air drag** applies always, on the ground too (`PModuleMotionDrag.cs:161-178`):
  - `motion.Scale(pow(airDragValue, dt*33))` with `AirDragAlways = 0.983f` (`GlobalConstants.cs:117`). That keeps about 0.983^33 ≈ 0.57 of the velocity per second (computed).
  - Water drag is 0.92 (`GlobalConstants.cs:127`).
  - Configurable: `attributes.physics.airDragFallingFactor` and `waterDragFactor`.
- **In-air steering** (`PModuleInAir.cs:97-111`): `motion += WalkVector * AirMovingStrength(0.05) * dt * 60`. Zero the WalkVector with `pathTraverser.Stop()` before a leap, or the path keeps steering the creature in the air. Configurable: `airMovingStrength`, `wallDragFactor`.
- **Ground** (`PModuleOnGround.cs:60-114`):
  - In 1/60 s sub-steps, motion moves toward `WalkVector × walkSpeedMultiplier` and then `motion.X/Z *= (1 - groundDragFactor)` with `groundDragFactor = 0.3`. That is a 0.7 multiplier per sub-step.
  - `controls.Jump` gives `Motion.Y = GlobalConstants.BaseJumpForce (8.2) / 60` (about 0.137, roughly a 1.5-block hop by calculation), at most once per 500 ms (lines 13, 100-113).
  - **The AI path traverser never sets `Controls.Jump`.**
  - **Coyote time:** `Applicable()` keeps the ground module active for 0.15 s after leaving the ground (`coyoteTimer = 0.15f`, lines 36-58). It is cut short only if `entity.Attributes "dmgkb" > 0`.
- **Physics config path:** the physics modules read `properties.Attributes["physics"]`, the entity-level `attributes: { physics: {...} }` (`BehaviorControlledPhysics.cs:194-198`).

### 3.3 Launching a ballistic arc

- Set `entity.Pos.Motion` once from the AI task. The physics behavior integrates it.
- **Coyote-time ground drag eats horizontal launch velocity.** I traced this through the code; I have not tested it in game. A launch from the ground keeps `PModuleOnGround` applying for about 0.15 s, which is about 9 sub-steps of ×0.7 on X/Z, roughly ×0.04 overall. The refactored seek task works around this by re-assigning `Motion.X/Z` from a stored vector on every AI tick while `!OnGround` (`TasksRefactored/AiTaskSeekEntity.cs:305-309`). Do the same.
  - The alternative is to subclass `EntityBehaviorControlledPhysics` and override `public virtual void SetModules()` (line 119) to swap `PModuleOnGround`. There is also a `protected readonly List<PModule> customModules` (line 43).
- **Aiming.** Two options:
  - **Re-assert the horizontal velocity** as the refactored seek does.
  - **Simulate the discrete loop.** Per physics step: gravity, then drag, then position plus `motion*dt*60`, in the module order above.

  As a first estimate, ignoring the small `0.015*|motionY|` term and the tick discretization, the continuous model is:
  - `g = 22.2 b/s²` and `k = -33·ln(0.983) ≈ 0.566 /s`
  - `vy(t) = (vy0 + g/k)e^(-kt) - g/k`
  - `y(t) = (vy0 + g/k)(1 - e^(-kt))/k - g t/k`
  - `x(t) = vx0 (1 - e^(-kt))/k`, where velocities are in blocks/s and motion = v/60

  These are my derivations, not vanilla code. Reaching about 4 blocks up needs `vy0` of roughly 14-15 b/s, so motion Y is roughly 0.23-0.25 (estimate).
- Collisions zero the motion component that was blocked (`BehaviorControlledPhysics.cs:497-499`).
- `EntityBehaviorRepulseAgents` (`repulseagents`) pushes overlapping creatures apart; it is in every vanilla creature JSON.

### 3.4 Detecting landing

- **Flags on `Entity`:** `OnGround` (`Entity.cs:247`), `CollidedVertically` (283), `CollidedHorizontally` (288), `Collided => CollidedVertically || CollidedHorizontally` (427), `FeetInLiquid` (252) and `Swimming` (278). These are server-side only for most entities (doc at 425-427).
- **How `OnGround` is set:** `entity.OnGround = (entity.CollidedVertically && falling && !controls.IsClimbing) || controls.IsStepping;` with `falling = prevYMotion <= 0` (`BehaviorControlledPhysics.cs:502-517`). Right after the AI writes `Motion.Y > 0`, `OnGround` stays true until the next physics tick. Use a "left ground, then on ground again" state machine; the refactored seek uses `updatedPathAfterLanding` for this.
- **Landing callback:**
  - `if (!onGroundBefore && entity.OnGround) entity.OnFallToGround(Math.Min(prevPrevYMotion, prevYMotion));` (line 533-536).
  - `Entity.OnFallToGround(double motionY)` (`Entity.cs:1335-1341`) calls `EntityBehavior.OnFallToGround(Vec3d lastTerrainContact, double withYMotion)` (`EntityBehavior.cs:100`) on every behavior. A custom behavior or entity class can hook it.
  - It runs on the physics tick, which can be a worker thread on busy servers (3.1).
- `PositionBeforeFalling` is updated whenever the entity is not falling, is on the ground or is climbing (line 543). **While rising it tracks the apex**, so fall distance is measured from the apex.

### 3.5 Fall damage and how to exempt a creature

- `EntityBehaviorHealth.OnFallToGround` (`ESS/Entity/Behavior/BehaviorHealth.cs:372-425`):
  - Returns if `!entity.Properties.FallDamage`.
  - `yDistance = |PositionBeforeFalling.Y - Pos.Y|`. No damage if it is under `3.5 * Stats["fallDamageThreshold"]` (`FallDamageFallenDistanceThreshold = 3.5f`, line 93) or if `withYMotion > -0.19 * stat` (line 92).
  - Otherwise `damage = yDistance*FallDamageMultiplier - 3.5`, reduced by an expected-motion fudge, times `Stats["fallDamageFactor"]`.
  - It is applied as `DamageSource{ Source = EnumDamageSource.Fall, Type = EnumDamageType.Gravity, IgnoreInvFrames = true }`.
- **Exemption options:**
  1. Entity JSON `fallDamageMultiplier: 0`. This sets `FallDamage = FallDamageMultiplier > 0` (`ESS/Loading/EntityType.cs:127, 241-242`). It also lets the pathfinder plan drops of up to 8 blocks (`WaypointsTraverser.cs:194`).
  2. A behavior that zeroes `damage` in `OnEntityReceiveDamage(DamageSource, ref float damage)` when `damageSource.Source == EnumDamageSource.Fall`. Behaviors run inside `Entity.ReceiveDamage` before damage is applied (`Entity.cs:907-915`).
  3. Raise the blended stats `fallDamageThreshold` or lower `fallDamageFactor`.

### 3.6 Normal step height and jumping for pathing entities

- `controlledphysics` reads `stepHeight` (default 0.6), `stepUpSpeed` (0.07), `climbUpSpeed` (0.07), `climbDownSpeed` (0.035) and `climbAnywhereStepThreshold` (0.375) from its behavior JSON (`BehaviorControlledPhysics.cs:182-199`). Vanilla puts it in both the client and server behavior lists, e.g. `{ code: "controlledphysics", stepHeight: 1.1251 }` (`wolf-adult.json:43,121`).
- Stepping is `HandleSteppingOnBlocks` / `TryStep` (682-731). It only happens while `WalkVector` is non-zero and the entity is on the ground or swimming. It raises `pos.Y += stepUpSpeed * dtFac` per tick.
- Climbing uses the entity JSON fields `canClimb`, `canClimbAnywhere`, `climbTouchDistance` (0.5) and `rotateModelOnClimb` (`EntityType.cs:112-141`). The climbing logic is at `BehaviorControlledPhysics.cs:335-420`.
- **Pathing creatures have no jump.** The AI never sets `Controls.Jump`, and the A* only plans steps of at most one block (section 5).

---

## 4. Seeking, melee and fleeing

### 4.1 Target selection (classic `AiTaskBaseTargetable`)

- **`entityCodes`** is a string array. A trailing `*` means a prefix match, and a lone `*` matches everything. **The default is `["player"]` when the key is omitted** (`AiTaskBaseTargetable.cs:84`). Codes are matched against `e.Code.Path` (223-240).
- Optional `entityTags` (`ComplexTagCondition`, 94-98) with `minTargetWeight` / `maxTargetWeight` (relative to own weight), and `minTargetInjury` / `maxTargetInjury` (205-221).
- **`skipEntityCodes`** is an array of `AssetLocation`s (wildcards allowed via `WildcardUtil.Match`). It is applied in `CanSense`, **but only for non-players**; players return earlier through `CanSensePlayer` (242-256).
- `TargetOnlyInteractableEntities` defaults to true (68). Flee sets it to false (`AiTaskFleeEntity.cs:67`).
- **Players** (`CanSensePlayer`, 258-280):
  - For aggressive tasks (`AggressiveTargeting == true && !friendlyTarget`), the world config `creatureHostility` applies: `"off"` never targets players, and `"passive"` targets them only while in emotion state `aggressiveondamage` or `aggressivearoundentities` (74-80, 260-264).
  - Flee has `AggressiveTargeting => false` (`AiTaskFleeEntity.cs:39`), so it ignores this setting.
  - Range is multiplied by the player stat `animalSeekingRange`, and by 0.6 while the player sneaks on the ground.
  - Creative and spectator players and not-yet-playing connections are excluded (`targetablePlayerMode`, 282-285).
- **Retaliation:** `retaliateAttacks` defaults to true (52). `OnEntityHurt` records `attackedByEntity = source.GetCauseEntity()` (435-440), and `RecentlyAttacked` is a 30 s window (64). `ShouldRetaliateForRange` requires the attacker to be alive, interactable, sensed by `CanSense` (so the player hostility rules apply) and not tolerated (`entity.ToleratesDamageFrom`, 448-461).
  - Seek and melee therefore target the attacker **even if the attacker is not in `entityCodes`** (seek lines 111, 137-143; melee 80-87). Set `retaliateAttacks: false` on prey-only tasks to prevent this; the wolf does so at `wolf-adult.json:230`.

### 4.2 Hostile only after being attacked: emotion states (`emotionstates`)

`EntityBehaviorEmotionStates` (`ESS/Entity/Behavior/BehaviorEmotionStates.cs`) works as follows:

- **JSON** is a `states` array of `EmotionState` (21-46): `code`, `duration`, `chance`, `slot`, `priority`, `accumType` (`sum`/`max`/`noaccum`), `maxGeneration`, `whenHealthRelBelow`, `whenSourceUntargetable`, `entityCodes`, `notifyRange` (12), `belowTempDuration`, `belowTempThreshold`. Slots and priorities resolve conflicts between states (230-253).
- **On damage** (`OnEntityReceiveDamage`, 110-149), fall and drown damage are ignored, and nothing happens when `creatureHostility == "off"`. Otherwise it tries `alarmherdondamage` (which pushes `aggressiveondamage` to herd members with the same `HerdId`), then `aggressiveondamage` (then `aggressivealarmondamage`), then `fleeondamage` (then `fleealarmondamage`). Each one triggers only if `rand <= chance` and the health condition passes.
- **Every 0.33 s** it decrements each state's remaining duration by `10 * deltaTime` and removes expired states (290-332). **Durations are therefore not seconds.** The real-time length depends on the entity tick `dt` (unverified mapping). `aggressivearoundentities` is retried every check when hostility is `"aggressive"` (307-310).
- **API:** `IsInEmotionState(string)` (152), `TryTriggerState(string statecode, long sourceEntityId)` (167), `TryTriggerState(string, double rndValue, long sourceEntityId)` (172), `GetActiveEmotionState(string)` (161), `ClearStates()` (157), and `ActiveStatesByCode` (58).

**Vanilla pattern for "passive until attacked": the fox** (`fox-adult.json:137-229`):

- States: `aggressiveondamage` (duration 15, chance 0.6, noaccum), `fleeondamage` (duration 10, chance 1) and `saturated`.
- `meleeattack` and `seekentity` with `entityCodes: ["player"]` and `whenInEmotionState: "aggressiveondamage"` (priorities 3 and 2.5).
- `fleeentity` from player, wolf, hyena and bear at priority 2.
- Prey `meleeattack` and `seekentity` (chicken, hare) with `whenNotInEmotionState: "saturated"` at 1.6 and 1.5.

This is almost exactly the spider's needs. Melee runs in slot 1 so it works while seek moves in slot 0.

### 4.3 Fleeing (`AiTaskFleeEntity`, classic)

- **Configuration** (43-61): `movespeed`, `seekingRange` (25), `fleeingDistance` (seekingRange + 15), `fleeDurationMs` (9000), `instafleeOnDamageChance`, `cancelOnHurt`, `intelligentFlee` (uses `Flee_Async` A*), `minDayLight`, `ignoreDeepDayLight`, `aiCreatureType`.
- **ShouldExecute** (74-152): chance, then `PreconditionsSatisfied`. The search range is `fearReductionFactor * seekingRange` (102-106). Without intelligent flee, `updateTargetPosFleeMode` picks a straight, left, right or back direction 10 blocks away that passes `traversable` (`AiTaskBaseTargetable.cs:335-381`).
- `InstaFleeFrom(Entity)` (292-296) forces a flee on the next `ShouldExecute`. It is used when damaged (285-289, with `instafleeOnDamageChance`) and by seek when it cannot reach its target (`AiTaskSeekEntity.cs:269-272, 492`).

### 4.4 The "generation" attribute

- **Storage:** `entity.WatchedAttributes` int `"generation"`. It is saved and synced. The info text shows `Generation: N` when above 0 (`API/Common/Entity/Entity.cs:2473-2477`).
- **Written by:**
  - `EntityBehaviorMultiply.GiveBirth`: `childEntity.WatchedAttributes.SetInt("generation", generation + 1)` (`BehaviorMultiply.cs:211, 227`).
  - Egg laying: chick data `generation + 1` (`ESS/Entity/AI/Tasks/AiTaskSeekBlockAndLay.cs:213`, `SURV/BlockEntity/BEHenBox.cs:149, 283`).
  - Growth copies it unchanged (`BehaviorGrow.cs:163`).
  - Spawn JSON `attributes: { generation: N }` through `Entity.SetAttributesFrom` (`Entity.cs:2608-2620`).
- **Read by:**
  - `GetOwnGeneration()` returns generation, plus 10 if the entity type's `attributes.tamed` is true (`AiTaskBaseTargetable.cs:422-427`).
  - `minGeneration` / `maxGeneration` on any task (`AiTaskBase.cs:226-227, 324-325`).
  - Emotion state `maxGeneration` (`BehaviorEmotionStates.cs:255`).
  - `pettable` and `ropetieable` `minGeneration` (`wolf-adult.json:404-405`; `SURV/Entity/Behavior/BehaviorPettable.cs:40`, `BehaviorRopeTieable.cs:166`).
  - `followleadholder` (`SURV/Systems/Riding/AiTaskFollowLeadHolder.cs:27`), health healing (`BehaviorHealth.cs:530`), cleaver, milkable.
- **What it changes,** with `tamingGenerations` defaulting to 10 (`AiTaskBaseTargetable.cs:57`; seek reads it at line 76, idle at 44):
  - **Flee:** `fearReductionFactor = max(0, (tamingGenerations - gen)/tamingGenerations)`, or 1 if `whenInEmotionState` is set. The flee search range scales linearly, so at generation 10 the creature flees nothing, **including predators** (`AiTaskFleeEntity.cs:102-106`).
  - **Melee:** the same factor scales the search range (3 × factor). **At factor ≤ 0 melee never runs, even against prey, unless `whenInEmotionState` is set.** When fully tamed it skips non-attacking players (`AiTaskMeleeAttack.cs:72-95`).
  - **Seek:** when `gen >= tamingGenerations` it ignores players that did not attack it (`AiTaskSeekEntity.cs:146-153, 180-201`).
  - **Idle:** `stopRange` is scaled by the factor once, at construction (`AiTaskIdle.cs:87-91`).
- **Recommendation** for "flee players until domesticated": give the player-flee task `maxGeneration: N`, or veto it with a behavior on `TaskManager.OnShouldExecuteTask`. Keep a separate predator-flee task without generation gating, and set a large `tamingGenerations` on hunting tasks.

---

## 5. Pathfinding

### 5.1 Traversers

- `public abstract class PathTraverserBase` (`ESS/Entity/Pathfinding/PathTraverserBase.cs:9`) has the fields `Action OnGoalReached`, `Action OnStuck`, `bool Active`, `float curTurnRadPerSec`, `float TargetDistance` and `virtual Vec3d CurrentTarget`, `virtual bool Ready`.
- `public class WaypointsTraverser : PathTraverserBase` (`ESS/Entity/Pathfinding/Astar/WaypointsTraverser.cs:13`, namespace `Vintagestory.Essentials`) is the one every creature gets.
- `ESS/Entity/Pathfinding/StraightLineTraverser.cs` also exists.

`WaypointsTraverser` signatures:

```csharp
public override bool NavigateTo(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, Action onNoPath = null, bool giveUpWhenNoPath = false, int searchDepth = 999, int mhdistanceTolerance = 0, EnumAICreatureType? creatureType = null)   // synchronous A* (line 85)
public override bool NavigateTo_Async(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, Action onNoPath = null, int searchDepth = 999, int mhdistanceTolerance = 0, EnumAICreatureType? creatureType = null)   // line 133
public override bool Flee_Async(Vec3d fleeFromPos, float fleeDistance, float movingSpeed, Action OnGoalReached, Action OnStuck, Action onNoPath = null, int searchDepth = 999, EnumAICreatureType? creatureType = null)   // line 160
public override bool FleeFrom(...)   // line 108, sync
public override bool WalkTowards(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, EnumAICreatureType creatureType = EnumAICreatureType.Default)   // straight line, no A* (314)
public void FollowRoute(List<Vec3d> swoopPath, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck)   // 79
public PathfinderTask PreparePathfinderTask(BlockPos startBlockPos, BlockPos targetBlockPos, float modeMinFleeDistance, int searchDepth = 999, int mhdistanceTolerance = 0, EnumAICreatureType? creatureType = null)   // 210
public override void Stop();     // 566: Active=false, Forward=false, WalkVector=0, cancels async
public override void Retarget(); // 578
public Action OnFoundPath;       // 56
```

How the traverser behaves:

- **`Ready`** is `waypoints != null && asyncSearchObject == null` (46-52). Tasks return it from `CanContinueExecute`.
- **`NavigateTo_Async`** returns false immediately if another async search is pending (line 135). Check the return value.
- **No path:** `HandleNoPath` still walks straight toward the target. If an `onNoPath` callback is given, it sets `Active = false` and invokes it (278-311).
- **Arrival:** after the last waypoint it calls `Stop()` and then `OnGoalReached` (377-382).
- **Stuck:** it accumulates `stuckCounter` from horizontal collisions, no progress for more than 2 s per waypoint, oscillation and similar. Above 60 it calls `Stop()` and then `OnStuck` (386-432).
- **Steering:** it sets `controls.WalkVector` from the yaw and `movingSpeed` and turns at `curTurnRadPerSec × dt × movingSpeed` (437-470). `movingSpeed` is the JSON `movespeed`, typically 0.006-0.06.
- **Turn rate:** `server.attributes.pathfinder.minTurnAnglePerSec` / `maxTurnAnglePerSec` (defaults 250/450; 64-72).
- **Ticking:** `EntityBehaviorTaskAI.OnGameTick` ticks the traverser before the tasks (`BehaviorTaskAI.cs:118`).

### 5.2 The A* entry points, and calling them from a mod

- **`PathfindSystem : ModSystem`** is server-only (`ESS/Entity/Pathfinding/Astar/Pathfind.cs:10-83`). Its `public AStar astar` is null on the client.

  ```csharp
  public List<PathNode> FindPath(BlockPos start, BlockPos end, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, EnumAICreatureType creatureType = EnumAICreatureType.Default)   // searchDepth hard-coded 9999 (44-47)
  public List<Vec3d> FindPathAsWaypoints(BlockPos start, BlockPos end, float modeMinimumFleeDistance, int maxFallHeight, float stepHeight, Cuboidf entityCollBox, int searchDepth = 9999, int mhdistanceTolerance = 0, EnumAICreatureType creatureType = EnumAICreatureType.Default)   // 62
  public virtual List<PathNode> FindEscapePath(...)   // 39
  ```

  A mod can call `sapi.ModLoader.GetModSystem<PathfindSystem>().FindPathAsWaypoints(...)` synchronously on the main thread for a reachability check. A null result means unreachable.
- **`AStar`** (`ESS/Entity/Pathfinding/Astar/AStar.cs:13`) exposes `FindPathAsWaypoints` (38), `FindPath` (44) and `FindPathOrEscapePath` (62). They return null once `NodesChecked > searchDepth` (90-93).
- **Async:** `PathfindingAsync : ModSystem` (`ESS/Entity/Pathfinding/PathfindingAsync.cs:11`) provides `public void EnqueuePathfinderTask(PathfinderTask task)` (113). `PathfinderTask` (`API/Server/PathfinderTask.cs:9-36`) has the constructor `(BlockPos startBlockPos, BlockPos targetBlockPos, float modeMinFleeDistance, int maxFallHeight, float stepHeight, Cuboidf collisionBox, int searchDepth, int mhdistanceTolerance = 0, EnumAICreatureType creatureType = EnumAICreatureType.Default)`. Poll `task.Finished` and then read `task.waypoints` (null means no path). `EntityBehaviorEmotionStates.TryTarget` uses this pattern (`BehaviorEmotionStates.cs:201-217, 292-302`).
- **`EnumAICreatureType`** (`API/Common/Entity/EnumAICreatureType.cs:8`): `Default`, `LandCreature`, `Humanoid` and others. It affects `Block.GetTraversalCost`.
  - Set it at behavior level with `aiCreatureType: "LandCreature"` (a string).
  - **Per task in classic seek and flee it is read with `AsInt`**, so a string such as `"LandCreature"` silently becomes `Default` (`AiTaskSeekEntity.cs:96-99`, `JsonObject.AsInt` at `API/Datastructures/JsonObject.cs:265-285`).

### 5.3 How step height and climbing work in A*

`AStar.traversable(...)` (167-328) works like this:

- If the box fits at the node, it searches downward for a floor. Each block of descent uses up `maxFallHeight`. If the box collides one block lower without a floor, that counts as wall-walking and fails.
- If the box does not fit, it tries stepping up onto that block: it tests the box at `node.Y + stepHeight + (topOfCollisionBox - 1)` and then **raises the node by at most `(int)(1 + steponHeightAdjust)`, i.e. one block** (293-325).
- Liquids and blocks with traversal cost above 10000 are rejected.
- **A* never plans climbing or jumps.** A perch more than one block up is unreachable to it.

`maxFallHeight` from the traverser is `FallDamage ? min(8, round(3.51/FallDamageMultiplier)) - (int)(movingSpeed*30) : 8` for sync and A* (194), and `4 - (int)(movingSpeed*30)` or 12 if `reckless` for async (`PreparePathfinderTask`, 214-215).

### 5.4 Testing whether an entity's box fits at a position

- **`CollisionTester`** (`API/Math/CollisionTester.cs:20`):

  ```csharp
  public bool IsColliding(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, bool alsoCheckTouch = true)   // 245
  public Block GetCollidingBlock(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, bool alsoCheckTouch = true)   // 248
  public Cuboidd GetCollidingCollisionBox(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, bool alsoCheckTouch = true)   // 307
  public void ApplyTerrainCollision(Entity entity, EntityPos entityPos, float dtFactor, ref Vec3d newPosition, float stepHeight = 1, float yExtra = 1)   // 49
  ```

  The shared instance is `world.CollisionTester` (`API/Common/API/IWorldAccessor.cs:145`). Vanilla calls `world.CollisionTester.IsColliding(world.BlockAccessor, entity.SelectionBox, pos, false)` (`AiTaskBaseTargetable.cs:375-381`; `AiTaskSeekEntity.cs:302`; `BehaviorGrow.cs:127`). The shared instance has scratch fields, so on other threads use `new CollisionTester()`.
- Static `EntityBehaviorControlledPhysics.GetCollidingCollisionBox(IBlockAccessor, Cuboidf entityBoxRel, Vec3d pos, out CachedCuboidList blocks, BlockPos tmpPos, bool alsoCheckTouch = true)` (`BehaviorControlledPhysics.cs:743`).
- `AiTaskBaseTargetable.traversable(Vec3d)` (protected, 375-381) checks the box at a position or one step up.
- Line of sight: `World.RayTraceForSelection(IWorldIntersectionSupplier, Vec3d from, Vec3d to, ref BlockSelection, ref EntitySelection)`, as used in `hasDirectContact`.

---

## 6. Death and loot

### 6.1 Reduced carcass drops when killed by a non-player

`EntityBehaviorHarvestable.dropQuantityMultiplier` (`ESS/Entity/Behavior/BehaviorHarvestable.cs:164-187`):

```csharp
if (GotCrushed) return 0.5f;          // deathReason == Fall or deathDamageType == Crushing (113-121)
if (GotAcidified) return 0.25f;
string deathByEntityCode = entity.WatchedAttributes.GetString("deathByEntity");
if (deathByEntityCode != null && !entity.WatchedAttributes.HasAttribute("deathByPlayer"))
{
    EntityProperties type = Api.World.GetEntityType(new AssetLocation(deathByEntityCode));
    float mult = type?.Attributes?["deathByMultiplier"]?.AsFloat(0.4f) ?? 0.4f;
    return mult;
}
return 1f;
```

- The multiplier comes from the **killer's** entity type attribute `deathByMultiplier`, default 0.4 (documented at line 92). Put `attributes: { deathByMultiplier: 1 }` in the spider's JSON to keep full drops from spider kills.
- The killer's `killedByInfoText` attribute changes the carcass text (612-623). The wolf uses `"deadcreature-eaten-large"` (`wolf-adult.json:27`).
- It is applied in `GenerateDrops` as `dstack.GetNextItemStack(this.dropQuantityMultiplier * dropQuantityMultiplier * extraMul)` (521). Food drops are also multiplied by `animalWeight` (515-519), and the harvesting player's `animalLootDropRate` applies (488-492).
- The death attributes are written in `Entity.Die` (`API/Common/Entity/Entity.cs:2222-2271`): `deathReason`, `deathDamageType`, `deathByEntity = GetCauseEntity().Code.ToString()`, and `deathByPlayer` (player name) when the cause entity is an `EntityPlayer`.

### 6.2 Observing "X was killed by Y"

- **Global event:** `event EntityDeathDelegate OnEntityDeath;` on `IEventAPI` (`API/Common/API/IEventAPI.cs:118`), with `public delegate void EntityDeathDelegate(Entity entity, DamageSource damageSource);` (`API/Common/API/Delegates.cs:135`). It is fired by `Api.Event.TriggerEntityDeath(this, damageSourceForDeath)` at the start of `Die` when `reason == Death` (`Entity.cs:2228-2230`). Subscribe with `sapi.Event.OnEntityDeath += (victim, src) => { var killer = src?.GetCauseEntity(); ... };`.
- **On the victim:** `EntityBehavior.OnEntityDeath(DamageSource damageSourceForDeath)` (`EntityBehavior.cs:174`), called at `Entity.cs:2262-2265`.
- **On the attacker:**
  - A task can compare `targetEntity.Alive` before and after `ReceiveDamage`, as melee does at lines 159 and 178.
  - Classic melee calls `IMeleeAttackListener.DidAttack(Entity targetEntity)` if the attacker's entity class implements it (`AiTaskMeleeAttack.cs:12-15, 173-176`).
  - `EntityAgent.DidAttack(DamageSource, EntityAgent)` and the behavior hook `DidAttack(DamageSource source, EntityAgent targetEntity, ref EnumHandling handled)` (`EntityAgent.cs:518-528`, `EntityBehavior.cs:229`) are only called from the player-interaction attack path (`EntityAgent.cs:477-480`), **not from AI melee**.
- **`DamageSource`** (`API/Common/Combat/DamageSource.cs:16-112`) has the fields `Source` (`EnumDamageSource`), `Type`, `HitPosition`, `SourceEntity`, `CauseEntity`, `SourceBlock`, `SourcePos`, `DamageTier`, `KnockbackStrength`, `Duration`, `TicksPerDuration`, `DamageOverTimeType`, `YDirKnockbackDiv`, `IgnoreInvFrames`.
  - `public Entity GetCauseEntity()` returns `CauseEntity ?? SourceEntity`; use it to get the real attacker for both melee and projectiles.
  - `public Vec3d GetSourcePosition()`.

---

## 7. Breeding and growth

### 7.1 `multiply` / `multiplybase`

- **Base** `EntityBehaviorMultiplyBase` (`ESS/Entity/Behavior/BehaviorMultiplyBase.cs`, code `multiplybase`):
  - JSON is populated by `JsonUtil.Populate`: `multiplyCooldownDaysMin` (6), `multiplyCooldownDaysMax` (12), `portionsEatenForMultiply` (3), `eatAnyway` (lines 28-52).
  - State lives in the `WatchedAttributes "multiply"` tree, e.g. `totalDaysCooldownUntil` (42-46, 92-103).
  - `ShouldEat` is `eatAnyway || (saturation < PortionsEatenForMultiply && cooldown over)` (54-66).
  - Saturation is read from **`WatchedAttributes["hunger"].saturation`** (106-112).
- **`EntityBehaviorMultiply`** (`BehaviorMultiply.cs`, code `multiply`):
  - JSON: `spawnEntityCodes` (array of `{code}`, or the legacy `spawnEntityCode`), `requiresNearbyEntityCode(s)`, `requiresNearbyEntityRange` (5), `spawnQuantityMin` (1), `spawnQuantityMax` (2), `pregnancyDays` (3).
  - A server callback runs every 3000 ms (153, 166). `TryGetPregnant` (260-301):
    1. passes with 6% chance per check
    2. requires the cooldown to be over
    3. requires `saturation >= PortionsEatenForMultiply`
    4. requires a nearby required male (`EntityCanMate`: alive, and not an eater or saturation ≥ 1)
    5. then has a 20% chance of just losing 1 saturation
    6. otherwise subtracts `PortionsEatenForMultiply` from the female and 1 from the male, and sets `isPregnant` and `totalDaysPregnancyStart`
  - After `PregnancyDays` it calls `GiveBirth(q)` (191-202).
  - **Offspring:** `childEntity.Attributes.SetString("origin", "reproduction"); childEntity.WatchedAttributes.SetInt("generation", generation + 1);` (226-227). No other attributes are copied.

### 7.2 What counts as "eaten"

- `AiTaskSeekFoodAndEat` (classic, `ESS/Entity/AI/Tasks/AiTaskSeekFoodAndEat.cs`):
  - Sets `WatchedAttributes "doesEat" = true` at construction (55).
  - Requires an entity-type `attributes.creatureDiet` (`CreatureDiet`: `FoodCategories`, `FoodTags`, `WeightedFoodTags`, `SkipFoodTags`; `ESS/Systems/POIRegistry.cs:33-105`) (65).
  - Skips when `multiply.ShouldEat` is false, except with a 0.4% chance (108).
  - Searches loose `EntityItem`s within 10 blocks if `eatLooseItems` is set (115-127). Otherwise it uses POIs of type `"food"` that implement `IAnimalFoodSource` within 48 blocks through `POIRegistry.GetNearestPoi` (131-146).
  - After `eatTime` seconds at the target (234-252):

    ```csharp
    float sat = targetPoi.ConsumeOnePortion(entity);
    tree.SetFloat("saturation", sat + tree.GetFloat("saturation", 0));   // tree = WatchedAttributes "hunger"
    entity.WatchedAttributes.SetDouble("lastMealEatenTotalHours", entity.World.Calendar.TotalHours);
    entity.WatchedAttributes.MarkPathDirty("hunger");
    ```
- **Troughs:** `BlockEntityTrough : ..., IAnimalFoodSource` (`SURV/BlockEntity/BETrough.cs:51`) registers itself with `POIRegistry.AddPOI(this)` (around line 190). `ConsumeOnePortion` removes `QuantityPerFillLevel` items and **returns 1f** (158-172).
- `IAnimalFoodSource` (`POIRegistry.cs:112-121`) defines `bool IsSuitableFor(Entity entity, CreatureDiet diet); float ConsumeOnePortion(Entity entity);`. `IPointOfInterest` (17-25) defines `Vec3d Position { get; } string Type { get; }`.
- **A kill is not a meal for breeding.** Classic melee only sets `lastMealEatenTotalHours` and the `saturated` emotion (`AiTaskMeleeAttack.cs:178-182`). It does not touch `hunger.saturation`. `lastMealEatenTotalHours` feeds the harvestable `animalWeight` simulation (`BehaviorHarvestable.cs:285-305`).

### 7.3 Crediting a meal from code, without a trough

Mirror the eat task (server side):

```csharp
ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("hunger");
if (tree == null) entity.WatchedAttributes["hunger"] = tree = new TreeAttribute();
tree.SetFloat("saturation", tree.GetFloat("saturation", 0) + 1f);   // 1 portion, same as a trough portion
entity.WatchedAttributes.SetDouble("lastMealEatenTotalHours", entity.World.Calendar.TotalHours);
entity.WatchedAttributes.MarkPathDirty("hunger");   // needed: editing a sub-tree does not mark the root key dirty
entity.GetBehavior<EntityBehaviorEmotionStates>()?.TryTriggerState("saturated", preyId);   // optional, like melee
```

`EntityAgent.ReceiveSaturation(...)` (`API/Common/Entity/EntityAgent.cs:566-577`) only forwards to behaviors' `OnEntityReceiveSaturation`. For animals nothing in the open repos writes `hunger.saturation` from it, so do not rely on it.

### 7.4 `grow`

`EntityBehaviorGrow` (`ESS/Entity/Behavior/BehaviorGrow.cs`):

- **JSON:** `hoursToGrow` (96), `orPortionsEatenForGrowing` (12), `adultEntityCodes`, `fedAdultEntityCodes` (76-79).
- **State:** `WatchedAttributes "grow"` with `timeSpawned` and `age`.
- **Check** every 3000 ms (102-158). The creature grows when `TotalHours >= timeSpawned + HoursToGrow`, or when `fedAdultEntityCodes` is non-empty and `hunger.saturation >= orPortionsEatenForGrowing`. Growth waits while the adult box would collide.
- **`protected virtual void BecomeAdult(Entity adult, bool keepTextureIndex)`** (160-173) copies only `generation`, `birthTotalDays` and `textureIndex`. It then sets `wasFedToAdulthood` in `Attributes` (140), calls `entity.Die(EnumDespawnReason.Expire, null)` and spawns the adult. **Custom data does not survive growth or birth unless you copy it yourself.**

---

## 8. Sleeping and idling at night in vanilla

- Vanilla sleep is just the classic `idle` task with a sleep animation and a time window. Chicken (`chicken-adult.json:297-310`):

  ```json5
  { code: "idle", animation: "Sleep", priority: 1.25, priorityForCancel: 1.38,
    minduration: 5000000, maxduration: 5000000, mincooldown: 5000, maxcooldown: 15000,
    whenNotInEmotionState: "aggressiveondamage",
    duringDayTimeFrames: [ { fromHour: 20, toHour: 6 } ],
    stopOnNearbyEntityCodes: ["player", "wolf-*", "fox-*"], stopRange: 8, stopOnHurt: true }
  ```

  The wolf sleeps `[10-18]` and `[1-3]` (`wolf-adult.json:324-338`). The hare uses the same pattern (`hare-adult.json:188-202`).
- **`AiTaskIdle`** (`ESS/Entity/AI/Tasks/AiTaskIdle.cs`):
  - JSON: `minduration` (2000), `maxduration` (4000; -1 means forever), `onBlockBelowCode`, `stopRange`, `stopOnHurt`, `stopOnNearbyEntityCodes` (**default `["player"]`**), `tamingGenerations` (36-94).
  - **`ShouldExecute` requires the block below to have a solid top face**: `if (!belowBlock.SideSolid[BlockFacing.UP.Index]) return false;` (117-119). This applies before the `onBlockBelowCode` test. A hammock block without a solid top therefore never hosts a vanilla idle; the refactored idle has `CheckForSolidUpSide` (`TasksRefactored/AiTaskIdle.cs:50`). It also refuses while the feet of a land creature are in liquid (101).
  - `ContinueExecute` (139-161) stops when an entity from the stop list is in range (checked about every 1.5 s), when the time frame ends, or after `idleUntilMs`.
  - It sets `entity.IdleSoundChanceModifier = 0` while idling (135, 167).
- **Animation mechanics:**
  - The task's `animation` code must match an entry in the entity's `client.animations`, e.g. `{ code: "sleep", animation: "sleep", easeInSpeed: 4, easeOutSpeed: 4, weight: 5, blendMode: "Average" }` (`wolf-adult.json:73-80`).
  - The base task starts it in `StartExecute` (`AnimManager.StartAnimation(animMeta)`) and stops it in `FinishExecute`, except codes `attack` and `idle` (`AiTaskBase.cs:378-383, 429-433`).
  - A `triggeredBy: { defaultAnim: true }` animation (idle) plays when nothing else runs, and `onControls: ["dead"]` drives death (`wolf-adult.json:64,71`).
  - `IAnimationManager` (`API/Common/Model/Animation/IAnimator.cs`): `bool StartAnimation(AnimationMetaData animdata)` (157), `bool StartAnimation(string configCode)` (164), `bool TryStartAnimation(AnimationMetaData)` (150), `void StopAnimation(string code)` (170), `bool IsAnimationActive(params string[] anims)` (141), `RunningAnimation GetAnimationState(string anim)` (143), and `event Action<string> OnAnimationStopped`.
- **Going to a specific block to sleep** has no vanilla equivalent. The closest template is `AiTaskSeekBlockAndLay`: it finds a POI (`IAnimalNest`) with `POIRegistry.GetNearestPoi`, walks there with `NavigateTo_Async`, occupies it with `SetOccupier`, then plays `sitAnimation` (`ESS/Entity/AI/Tasks/AiTaskSeekBlockAndLay.cs:106-177, 223-340`). A hammock block entity can register itself as a POI as the trough does (`POIRegistry.AddPOI` / `RemovePOI`, `POIRegistry.cs:283-297`).

---

## 9. Persistent per-entity data

| Store | Saved to disk | Synced to clients | Source |
|---|---|---|---|
| `entity.WatchedAttributes` (`SyncedTreeAttribute`) | yes | yes, dirty paths are sent | doc comment `Entity.cs:297-300`; `ToBytes` writes it always (`Entity.cs:2137`) |
| `entity.Attributes` (`SyncedTreeAttribute`) | yes (`if (!forClient) Attributes.ToBytes(writer)`, `Entity.cs:2148-2151`; read back only `if (!isSync)`, 2061-2064) | **no**, it is local to each side | doc `Entity.cs:307-310` |
| `entity.DebugAttributes` | no | debug mode only | `Entity.cs:302-305` |

- Top-level setters on a `SyncedTreeAttribute` mark the key dirty. **Changing a value inside a sub-tree requires `MarkPathDirty("treeKey")`** for clients to see it (`API/Datastructures/AttributeTree/Other/SyncedTreeAttribute.cs:81`; vanilla does this at `AiTaskSeekFoodAndEat.cs:245` and `BehaviorMultiply.cs:106`).
- `RegisterModifiedListener(string path, Action listener)` (line 32) lets the client react to changes.
- BlockPos helpers (`API/Datastructures/AttributeTree/TreeAttribute.cs:25-44`): `GetBlockPos(this ITreeAttribute tree, string code, BlockPos defaultValue = null)` and `SetBlockPos(this ITreeAttribute tree, string code, BlockPos value)`. They store `codeX`, `codeY` and `codeZ` ints. Y is written as `InternalY`, which includes the dimension, and the 3-int `BlockPos` constructor splits it back out (`API/Math/BlockPos.cs:60-66`).

Example. Server-only state goes in `Attributes`; anything the client UI or renderer needs goes in `WatchedAttributes`:

```csharp
// write (server)
ITreeAttribute js = entity.WatchedAttributes.GetOrAddTreeAttribute("jumpingspider");
js.SetBlockPos("hammock", hammockPos);        // ints hammockX/Y/Z
js.SetBool("domesticated", true);
entity.WatchedAttributes.MarkPathDirty("jumpingspider");

// read
ITreeAttribute t = entity.WatchedAttributes.GetTreeAttribute("jumpingspider");
BlockPos home = t?.GetBlockPos("hammock");    // null if never set
bool tame = t?.GetBool("domesticated") ?? false;

// server-only alternative (not synced)
entity.Attributes.SetBool("jsAirborne", true);
```

`ITreeAttribute GetOrAddTreeAttribute(string key);` exists (`API/Datastructures/AttributeTree/ITreeAttribute.cs:254`, implemented at `TreeAttribute.cs:718`). Remember that `grow` and `multiply` do not copy this data to the new entity (7.1, 7.4).

---

## Surprises and pitfalls

1. **There are two task families.** The refactored "-r" family has different JSON key names, defaults and config classes. No vanilla creature uses it in 1.22.7; all reference JSONs use classic tasks. The refactored code also has apparent bugs:
   - `if (tagsAppliedOnStart.IsEmpty)` is inverted, so `TagsAppliedToEntity` never applies (`TasksRefactored/AiTaskBase.cs:507`).
   - Passive hostility allows targeting players when *not* in the aggressive states, the reverse of classic (`TasksRefactored/AiTaskBaseTargetable.cs:472`).
   - Refactored tasks use a per-task `CreatureHostility` and ignore the world's `creatureHostility` setting.

   A builder learning from vanilla JSON should use the classic family.
2. **`ServerPos` and `SidedPos` are obsolete aliases of `Pos`** (`Entity.cs:179-183`). Older mods' `ServerPos.Motion` still compiles but triggers obsolete warnings.
3. **Task scheduling is by list order, not by sorted priority.** Pre-emption needs a strictly greater `Priority` than the running task's `PriorityForCancel`. The classic base does not enforce `mincooldown` / `maxcooldown` or `executionChance`: each task does, and `fleeentity` ignores cooldowns entirely.
4. **`entityCodes` defaults to `["player"]`** in classic targetable tasks. `skipEntityCodes` does not apply to players. `retaliateAttacks` defaults to true, so a prey-only seek or melee task also chases whoever hurt the creature.
5. A per-task `aiCreatureType: "LandCreature"` string is silently treated as `Default` in classic seek and flee. Set it at the `taskai` level.
6. **Generation side effects:**
   - At `generation >= tamingGenerations` (default 10), classic melee **stops attacking everything**, not just players, unless `whenInEmotionState` is set.
   - The melee search radius and the flee radius shrink linearly with generation, **for all targets**, so a domesticated spider would also stop fleeing wolves.
   - Use `maxGeneration` on the player-flee task, or an `OnShouldExecuteTask` veto, plus a large `tamingGenerations` on hunting tasks.
   - In vanilla only breeding increments generation; there is no "domesticate" action.
7. **Kills by the spider leave 0.4× carcass drops** unless the spider's type has `attributes.deathByMultiplier`. A kill also does not feed the breeding saturation; credit it manually (7.3).
8. **Ballistic leaps fight the physics.** I traced this through the code; it is not tested in game.
   - Ground drag continues for about 0.15 s of coyote time after take-off and wipes most of the horizontal launch velocity.
   - Air drag (0.983^(33·dt)) always applies.
   - The path traverser's WalkVector keeps steering in the air.
   - Stop the traverser and re-assert the horizontal velocity every AI tick while airborne, as `AiTaskSeekEntityR` does, or provide a custom physics module list.
   - The AI tick (20 ms system interval) and the physics tick (fixed 1/30 s) are not in lockstep, and `OnGround` lags one physics tick after a launch.
9. **Reaching perches about 4 blocks up is impossible for the stock pathfinder.** A* steps up at most one block per node and never plans jumps or climbs. Reachability checks against perches must be done by hand: an apex and landing-spot box test with `CollisionTester.IsColliding`, plus a line-of-sight ray. A* can still check the approach to the take-off spot.
   - Alternative: `canClimb` / `canClimbAnywhere` give real wall-climbing physics. They suit a spider, but A* ignores them.
10. **Fall damage is measured from the apex,** because `PositionBeforeFalling` is updated while rising, with a 3.5-block threshold. A leap from a perch down onto prey will hurt unless the creature is exempt; set `fallDamageMultiplier: 0` or cancel Fall damage in a behavior. Exemption also makes the pathfinder accept 8-block drops.
11. **The vanilla `idle` task refuses to start unless the block below has a solid top face.** A hammock block needs `SideSolid` up, or the spider needs a custom sleep task modelled on `seekblockandlay` with a POI.
12. **AI and pathing run only while the entity is Active** (players nearby). A sleeping spider far from players does not move to or from its hammock. Plan for that, for example by teleporting home when the spider becomes Active at night (an idea; vanilla has no such feature).
13. Emotion-state `duration` is decremented by `10*dt` per 0.33 s check, so it is **not in seconds**. Its real-time length depends on the tick rate (unverified). Emotion states appear to be held in memory (`ActiveStatesByCode`); whether they survive a save and reload is unverified.
14. `NavigateTo_Async` returns false when a search is already pending (one traverser per entity is shared by all tasks). Check the return value.
15. AI melee never calls `EntityAgent.DidAttack` or the behaviors' `DidAttack`. Use the return value of `ReceiveDamage`, `IMeleeAttackListener`, or `Event.OnEntityDeath`.
16. Any concrete task class added to a task manager, including a subclass of a vanilla task, must be registered in `AiTaskRegistry`, or `AddTask` throws.
17. `OnFallToGround` and other physics callbacks can run on physics worker threads when a server has more than 800 physics tickables. Keep handlers thread-safe, or just set a flag that the AI task reads.
18. **Not verified:**
    - in-game ballistic numbers and the coyote-drag effect (both from code reading only)
    - whether server animators fire `RegisterFrameCallback`
    - the real-time length of emotion durations
    - whether `semitamed` is registered in VintagestoryLib
    - client-side smoothness of a server-driven leap (the `interpolateposition` behavior)

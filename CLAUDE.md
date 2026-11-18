# CLAUDE.md

Guidance for AI agents working in this repository. People should start with `README.md`.

## What this is

**Jumping Spiders** is a code mod for Vintage Story **1.22.7** (.NET 10, C#). It adds:

- the **Jumping Spider**, a passive predator that hunts with planned parabolic jumps, flees from players until
  bred in captivity, sleeps and naps in hammocks, and builds them;
- the **Silk Hammock** block, which spiders sleep in and players harvest for flax twine.

The author is Aurora_Luciri. A port to game version 1.23 and PetAI integration are on the roadmap, not started.

`README.md` is the reference for what the mod does: features, every knob and its default, the `/jspider` debug
commands, the in-game test checklist and "Behaviour worth knowing". Read it before changing behaviour, and
update it in the same change. This file covers how to work here.

## Who you are working with

- The author knows Java-style OOP and is expert in GDScript, but is new to C# and to Vintage Story modding, and
  wants to be able to read and change this code. That drives the code style below.
- The author makes all final models, textures and animations in VS Model Creator and tests in the game.
- **Ask before assuming design intent.** When a request is ambiguous in a way that changes behaviour, ask. When
  you pick a number yourself, make it a named knob and say that it is your placeholder.
- No game client can run in an agent's environment. Always say what you verified on the headless server and
  what only the author can check in game (anything visual, anything involving a connected player).

## Rules

### 1. Assets: placeholders only

Agents may create visual and user-facing assets **only as placeholders, and must flag them**. Final assets are
made by a person.

- This covers models (shapes), textures, animations, sounds, icons and player-facing text such as item names
  and descriptions in `lang/`.
- Flag every placeholder three ways: put the word `PLACEHOLDER` in a comment where a JSON file references it,
  list it in the table below, and list it under "Assets to make" in `README.md`. `grep -rn PLACEHOLDER` must
  find every one.
- Never replace, regenerate, restyle or "improve" an asset a person made. When a person's asset arrives, wire
  it in and remove the placeholder and its flags.
- Mechanical edits to the author's shape files are fine **when the author asks for them**: mirroring keyframes
  to the other side, laying out UVs, putting a texture code on faces. Use the scripts in `tools/model/`, which
  keep VS Model Creator's file layout, and report exactly what changed.

Current placeholders:

| Asset | Where | Notes |
|---|---|---|
| Spiderling model and animations | `shapes/entity/land/jumpingspider/baby.json` | Box model with a vanilla hare texture. The author plans a real one based on the adult model |
| All player-facing text | `lang/en.json` | Names, descriptions and info text were written by agents |
| Adult `idle`, `hurt`, `die` animations | not made yet | The adult model has `walk`, `run`, `jumpwindup`, `jump`, `sleep`; the entity JSON already asks for the other codes |

### 2. Code style

Plain C# that a Java or GDScript programmer can read:

- Explicit types, never `var`. No LINQ. No lambdas where a named method works (pass a named method when the API
  needs a delegate). No pattern matching, switch expressions, tuples, records, expression-bodied members,
  `async`, or generics and extension methods of our own.
- Explicit `if (x == null)` checks, not `?.` and `??` chains. Nullable annotations are off; do not write
  `string?`.
- One class per file, file name equals class name. Every class starts with a summary of its job and how the
  game calls into it.
- Comments explain why, and explain a Vintage Story concept the first time it appears in a file. Do not
  narrate obvious lines.
- No magic numbers. Every tunable is a named knob with a comment giving its unit: global ones in
  `Config/JumpingSpidersConfig.cs`, per-task ones in the entity JSON next to the task.
- Logic that needs no game types goes in `JumpingSpiders/Logic/` with xUnit tests in `JumpingSpiders.Tests/`
  (the test project compiles `Logic/**` directly).

### 3. Git

- The author commits models, textures and animations to the same branch you work on. Fetch and fast-forward
  before you start and again before you commit. Never rewrite or revert their commits.
- Do not open a pull request unless asked. Do not put AI model names in commits, code or docs.
- Leave no temporary test code in the tree (see "Testing without a client").

## Layout

`README.md` has the folder table ("Where things are"). In short: `Config/` knobs, `Logic/` pure and
unit-tested, `Jumping/` flies one jump, `AiTasks/` the spider's five tasks, `Spider/` entity behaviours and
saved attributes, `Hammock/` the block, `Spawning/` wild spawns, `Commands/` `/jspider`, and
`assets/jumpingspiders/` JSON, shapes, textures and lang. `tools/model/` holds scripts for shape files.
`docs/engine-notes/` holds reference notes on 1.22.7 internals gathered from the game's sources at the start of
the project; they describe the engine, not this mod's current design.

## Build, test, run

`VINTAGE_STORY` must point at a 1.22.7 install (the folder with `VintagestoryAPI.dll`).

```bash
dotnet build JumpingSpiders/JumpingSpiders.csproj -c Release
dotnet test JumpingSpiders.Tests/JumpingSpiders.Tests.csproj
```

Smoke test on the dedicated server (use a fresh data folder each time):

```bash
cd "$VINTAGE_STORY" && (sleep 70; echo "/stop") | timeout 170 dotnet VintagestoryServer.dll \
  --dataPath "$DATA" --addModPath "$REPO/JumpingSpiders/bin/Release/Mods" \
  --addOrigin "$REPO/JumpingSpiders/assets" > "$DATA/stdout.txt"
```

- Console commands can be piped in before `/stop`. `/jspider spawn nohammock day` and `/jspider info` work from
  the console; vanilla `/entity spawn` does not.
- Expect no errors or warnings from the mod. "Server overloaded" warnings during world generation are normal.
- Two servers at once need different `--port` values and data folders.
- The official sources are at github.com/anegostudios (`vsapi`, `vsessentialsmod`, `vssurvivalmod`); check out
  the 1.22.7 tag and read them instead of guessing API behaviour. The client and server cores are closed; decompile
  `VintagestoryLib.dll` with `ilspycmd` when you need them.

## Testing without a client

No player can connect to the headless server, and **the engine runs no AI and no physics for entities with no
player nearby**. To exercise behaviour:

1. Add a temporary `ModSystem` file with a console command.
2. In it, force-load chunks (`sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded
   = true, OnLoaded = ... })`), build a small scene, spawn entities, then drive them by hand: the entity's
   game tick, its physics tick (jumps depend on the per-physics-tick callback), and the calendar for time of day.
3. Log what happened, compare with what you expected, and **delete the file before you finish**. Put
   `{"DebugLogging": true}` in the data folder's `ModConfig/jumpingspiders.json` to get the spiders' own account of
   what they decided in `Logs/server-debug.log`; it is off by default.

A hare can stand in for a player in flee and attack tests. Things this cannot show: rendering, animation
blending, interpolation between server ticks, and anything that needs a real `IPlayer`.

## Design decisions made with the author

1. **Taming is by generation.** A spider is domesticated when its `generation` attribute is at least
   `DomesticatedFromGeneration` (1). Wild-caught spiders (generation 0) never become domesticated; feeding them
   only lets them breed. The entity JSON gates tasks with `minGeneration` / `maxGeneration`, so the config knob
   and those JSON values must change together.
2. **Meals.** Only a farm-born chicken or hare (generation 1 or more) that the spider kills counts for breeding.
   Any kill makes the spider sated, and spiders hunt only when hungry.
3. **Wild spiders** flee from players (fleeing outranks hunting) and attack a player only after being hit, or
   when a player breaks the hammock they sleep in. **Domesticated spiders** do not flee from players; when hit
   they flee and never attack. An angry spider goes only for whoever angered it, never a bystander.
4. **Same-sex adults fight** when hungry. Babies never hunt and are never targets.
5. **Jumping only.** Spiders reach high places by jumping, up to `MaxJumpUp` (4) blocks, in plans of at most two
   jumps. No climbing, except the brief hold on a wall or ceiling while building a hammock.
6. **Wind-up.** Attack, explore and into-the-hammock jumps wind up for 350 to 900 ms. Flee jumps are instant.
7. **Sleeping.** The spider stays an entity, sits in the hammock's cell and faces out of its opening. Wild
   spiders go to bed at dusk; domesticated ones stay up a little, by curiosity. All spiders may nap by day. A
   sleeping spider does not flee from a player walking up.
8. **Hammocks.** One model serves walls, ceilings and floors, turned by the block JSON. Only domesticated adults
   build: one hammock per kill, attempted hourly while sated, at a random spot with no height preference, at
   most one hammock per 3x3x3, and only from within `HammockBuildRange` (0.5) of the spot. Wild spiders have one
   home hammock on a tree trunk and never build.
9. **Wild spawning** is done by mod code, not JSON spawn conditions or world generation, in "spider chunks" (a
   fixed share of chunk columns chosen from the world seed).
10. **Enclosures** are meant to need tall fences or a roof.

## Engine facts that cost time

**Entities and AI**

- Physics runs at 30 ticks a second; `Motion` is in blocks per 1/60 s. Do not set `attributes.physics` on the
  spider: the jump model was measured against default physics. `fallDamageMultiplier` must stay 0.
- A jump is planned once (`Logic/JumpPlanner`), then `Jumping/JumpController` flies it by setting the motion
  after every physics tick so the next tick ends on the planned arc. Plan when a decision is made, never every
  tick: one search checks about 1,900 cells.
- The vanilla pathfinder never plans jumps or climbs.
- The task manager starts the first task in list order that wants to run; a task pre-empts a running one only
  with a higher priority than the running task's `priorityForCancel`. The classic `AiTaskBase` family is used
  (constructor `(EntityAgent, JsonObject taskConfig, JsonObject aiConfig)`); it does not enforce cooldowns for you.
- "Night" is `SpiderAttributes.IsNight` (sun below the horizon) everywhere.
- Breeding uses vanilla `multiply`, which reads `WatchedAttributes["hunger"].saturation`. A kill does not feed
  an entity by itself; the mod credits meals and reverts saturation that was not earned by a valid kill.
- Texture variants come from `alternates` on the entity's **first** texture only.

**JSON**

- In `*ByType` maps the first matching key wins, and `*-north` also matches `silkhammock-up-north`. Put specific
  keys first.
- Do not rename existing block or entity codes: placed hammocks and living spiders in saved worlds use them.
  `silkhammock-up` and `silkhammock-down` mean "opens west" for that reason.
- A changed default in `JumpingSpidersConfig.cs` does not reach players who already have
  `ModConfig/jumpingspiders.json`; say so in the README when you change one.
- Shears have no mining speed in 1.22.7; the hammock's shears bonus is in `BlockSilkHammock.OnGettingBroken`.
- Blocks draw half-transparent pixels only with `renderpass: "Transparent"`.

**Shape files (VS Model Creator)**

- The front of an entity model is -X and its left is +Z. One block is 16 units.
- In an animation, every keyframe element must set all six of `offsetX/Y/Z` and `rotationX/Y/Z`, or the server
  fails to load it.
- The editor names a texture code after its file (`male.png` gives `#male`) and writes `#null` on untextured
  faces. The entity, block and item JSON map those codes to texture paths.
- With `"autoUnwrap": false` on an element and `"autoUv": false` on a face, the editor keeps hand-set UVs.
- Both adult sexes use `male.json`, `male.png` and `male2.png` (names kept for the author's editor project). The
  female's smaller pedipalps come from `elementScalesByType` in the adult JSON, applied in
  `EntityBehaviorJumpingSpider.OnTesselation` (client).
- A loaded entity shape is one object per file, shared by every entity of every type that uses it. A behavior's
  `OnTesselation` that changes it must clone it and set `shapeIsCloned` (the game then builds the mesh and a
  per-entity animator from the clone), and after changing an element's scale or position clear
  `inverseModelTransform` on it and its children: clones copy the cached inverse rest pose, and a stale one makes
  the animator apply the change twice.
- The editor sometimes writes a trailing comma. The game accepts it; strict JSON parsers do not.

## Model tooling

`tools/model/` (Python 3; `check_animation.py` needs numpy, and matplotlib and pillow for previews):

| Script | Use |
|---|---|
| `mirror_keys.py` | Give the right legs the left legs' keyframes, mirrored, on the same frames |
| `replay_leg.py` | Replay the left front leg on all eight legs as an alternating gait |
| `unwrap_slabs.py` | Lay out UVs for a model made of flat boxes and put one texture on every face |
| `check_animation.py` | Play an animation with the game's maths, check left/right symmetry, write a preview GIF |
| `vsmc_writer.py` | Read and write shape files in the editor's own layout; the other scripts use it |

The scripts refuse to touch a file they cannot write back byte for byte, so a scripted change shows up as a small
diff. Keep it that way: extend `vsmc_writer.py` when the editor starts writing something new.

## Not done

- No one has reviewed the mod's behaviour in the game client end to end; `README.md` has the checklist.
- PetAI integration, and a port to game version 1.23.

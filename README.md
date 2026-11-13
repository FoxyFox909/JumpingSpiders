# Jumping Spiders

A Vintage Story code mod (game version 1.22.7) that adds the Jumping Spider, a passive predator that leaps onto
its prey, and the Silk Hammock block it sleeps in.

All models, textures and animations in this repository are placeholders (boxes with vanilla hare and linen
textures). See [Assets to make](#assets-to-make).

## Build and run

Set the `VINTAGE_STORY` environment variable to the game's install folder (the one holding `VintagestoryAPI.dll`).

```bash
dotnet build JumpingSpiders/JumpingSpiders.csproj -c Release
```

The mod lands in `JumpingSpiders/bin/Release/Mods/mod`. To play with it straight from the build output, start
the game with the two extra arguments the project's launch profiles use (`JumpingSpiders/Properties/launchSettings.json`):

```bash
dotnet "$VINTAGE_STORY/Vintagestory.dll" --addModPath "$PWD/JumpingSpiders/bin/Release/Mods" --addOrigin "$PWD/JumpingSpiders/assets"
```

To make a zip for the game's `Mods` folder instead:

```bash
./build.sh
```

The zip appears in `Releases/`.

Unit tests (pure logic only: jump maths, jump planning, spider chunks, meal and despawn rules):

```bash
dotnet test JumpingSpiders.Tests/JumpingSpiders.Tests.csproj
```

## Where things are

| Folder | What is in it |
|---|---|
| `JumpingSpiders/Config` | `JumpingSpidersConfig.cs`: every global knob, each with a comment |
| `JumpingSpiders/Logic` | Pure C# with no game types: jump maths, the jump planner, spider chunks, rules. Unit-tested |
| `JumpingSpiders/Jumping` | `JumpController`: launches and steers one jump of one spider |
| `JumpingSpiders/AiTasks` | The four AI tasks: jump attack, jump flee, explore, sleep |
| `JumpingSpiders/Spider` | The spider's entity behaviors (meals, taming, growing up) and its saved attributes |
| `JumpingSpiders/Hammock` | The Silk Hammock block, its block entity, and hammock finding and building |
| `JumpingSpiders/Spawning` | Wild spawning in spider chunks, and wild hammocks on tree trunks |
| `JumpingSpiders/Commands` | The `/jspider` debug commands |
| `JumpingSpiders/assets/jumpingspiders` | Entity, block and item JSON, shapes, language file |

## Knobs

Global knobs are in `ModConfig/jumpingspiders.json` inside the game's data folder. The file is created with the
defaults on first start; `Config/JumpingSpidersConfig.cs` explains each one. Restart the world after changing it.
An existing file keeps the values it has when a new version of the mod changes a default (only settings that are new
get added), so edit or delete the file to get new defaults. This version changed `HammockHeightBias` (2 to 4),
`HammockBuildSamples` (60 to 150) and `HammockBuildMaxPlanAttempts` (6 to 20), and added
`CuriosityStayUpHoursMax` and `CuriosityStayUpMaxNightShare`.

Per-task knobs are in the entity JSON (`assets/jumpingspiders/entities/land/jumpingspider-adult.json` and
`-baby.json`), next to the task they belong to:

| Knob | Where | Default |
|---|---|---|
| Wind-up time before an attack jump | `windUpMinMs` / `windUpMaxMs` on the jump attack tasks | 500 to 1250 ms |
| Jump cooldown | `mincooldown` / `maxcooldown` on each task | 1.5 to 3 s for attacks |
| Bite damage | `damage` on the jump attack tasks | 3 to prey, 2 to players, 1 for babies |
| Jump angle, maximum speed | `JumpAngleDeg`, `MaxJumpSpeed` in the config | 50 degrees, 18 |
| How high, far and deep a jump goes | `MaxJumpUp`, `MaxJumpHorizontal`, `MaxJumpDown` | 4, 6, 6 blocks |
| Jumps per plan | `MaxPlannedJumps` | 2 |
| Curiosity (rolled per spider per day) | `CuriosityMin`, `CuriosityMax`, `CuriosityNightMultiplier` | 0.2 to 1.0, times 0.2 at night |
| How long a domesticated spider stays up after dusk | `CuriosityStayUpHoursMax`, `CuriosityStayUpMaxNightShare` | curiosity times 3 in-game hours, at most half the night |
| Hours a spider stays sated after a kill | `SatedHoursAfterKill` | 12 |
| One hammock per this many blocks cubed | `HammockSpacing` | 4 |
| How strongly hammock sites favour height | `HammockHeightBias` (built by spiders), `WildHammockHeightBias` (wild, on trunks) | 4, 2 |
| How hard a spider looks for a high hammock site | `HammockBuildSamples`, `HammockBuildMaxPlanAttempts` | 150 block columns, 20 reach checks |
| Share of chunks that are spider chunks | `SpiderChunkFraction` | 0.04 |
| Chance a wild spider comes with a hammock | `WildHammockChance` | 0.75 |
| Carcass yield of the spider's victims | `deathByMultiplier` in the adult JSON | 0.2 |
| Breeding | the `multiply` behavior in the adult JSON | 2 meals, 4 days pregnant, 1 to 3 babies |
| Growing up | `hoursToGrow` in the baby JSON | 120 in-game hours |

`DomesticatedFromGeneration` in the config and the `maxGeneration: 0` / `minGeneration: 1` values in the entity
JSON must be changed together.

## Debug commands

All need the `controlserver` privilege (the world's owner has it). They act on the spider you look at, or the
nearest one.

| Command | What it does |
|---|---|
| `/jspider chunk` | Is this a spider chunk? Shows climate, its wild hammock, and the nearest spider chunks |
| `/jspider info` | Generation, wild or domesticated, hungry, asleep, curiosity and how long it stays up after dusk, home hammock |
| `/jspider hungry` | Makes the spider hungry so it hunts |
| `/jspider sated [hours]` | Makes it sated |
| `/jspider generation <n>` | Sets its generation. 0 is wild, 1 or more is domesticated |
| `/jspider spawn [auto\|hammock\|nohammock] [now\|day\|night]` | Tries a wild spawn in this chunk and says why it failed |
| `/jspider build` | Makes a domesticated adult try to build a hammock now |

Spiders and the hammock are also in the creative inventory. A spider placed from there counts as kept by the
player and never despawns.

## What to test in the game

None of this has been seen by a person yet. The logic was exercised on a headless server with hand-driven
ticks and stand-ins for the player, so everything a real player does or sees is untested.

Use creative mode, `/time set` to move between day and night, and `/jspider info` to see a spider's state.

**Spawning**
1. Walk through a forest with `/jspider chunk` until you stand in a spider chunk, or go to one it lists.
   Use `/jspider spawn hammock night`: a hammock appears on a tree trunk, with a spider asleep in it.
2. `/jspider spawn hammock day`: the spider appears within 10 blocks of the hammock, not in it.
3. `/jspider spawn` again in the same chunk: it reuses the existing hammock and does not make a second one.
4. Without commands: wild spiders appear by themselves in spider chunks 24 to 80 blocks from you. This is slow
   by design (a check every 20 seconds, 12 hours of cooldown per chunk).
5. Walk more than 32 blocks from a wild spider for 10 real minutes: it is gone, and its hammock is still there.

**Wild behaviour**
6. Walk up to an awake wild spider: it jumps away, with no wind-up.
7. Put hares, chickens or raccoons near a hungry wild spider (`/jspider hungry`): it raises its front legs,
   leaps and bites. The carcass gives much less meat than usual.
8. Step aside from the prey's position during the wind-up: the spider can miss a moving target.
9. Hit a wild spider: it attacks you, and only you.
10. Break the hammock of a sleeping wild spider: it wakes and attacks you. Break it while the spider is away:
    nothing happens.
11. At dusk a wild spider returns to its own hammock and sleeps at once. At dawn it leaves.
12. Bait a reed basket trap with raw meat near a wild spider: it gets caught about half the time.

**Hammock block**
13. Place a hammock on a wall, a floor and a ceiling. Break the block holding it: the hammock breaks too.
14. Break hammocks by hand and with shears: 2 to 4 flax twine each time, and shears are about five times faster.

**Taming and breeding**
15. Release a trapped male and female into a pen with farm-born chickens or hares (generation 1 or more).
    They hunt when hungry. After 2 valid kills by the female and 1 by the male, the female becomes pregnant.
16. Wild chickens, wild hares, raccoons, troughs and meat on the ground give no breeding progress.
17. A caught wild spider still flees from you, never builds a hammock, and adopts a free hammock near the pen.
18. The babies are generation 1: they do not flee from you. As adults, each valid meal makes them build a
    hammock, never closer than one per 4x4x4 blocks, and they sleep in a free one at night.
19. Where they build: in a pen with walls 5 high and nothing else, almost always on a wall, mostly 3 blocks up
    (the highest they can jump into). With a post or perch 3 to 4 high, mostly high up its side, on top of it, or
    on the wall top they can reach from it. On bare ground, on the ground. `/jspider build` builds one at once; break it and repeat to see the spread.
20. At dusk, domesticated spiders stay up for a while before they go to bed: up to 3 in-game hours for a very
    curious one, about half an hour for the least curious (`/jspider info` shows today's curiosity and how long).
    Meanwhile they hop around less than by day and hunt when hungry. A spider already asleep stays asleep.
21. Hit a domesticated spider: it flees and never attacks. Break its hammock while it sleeps: no attack.
22. Two adult males or two adult females fight when hungry. A male and a female do not. Babies are left alone.

**Jumping and enclosures**
23. Domesticated spiders hop onto high blocks out of curiosity, up to 4 blocks per jump, two jumps in a row.
24. A pen with walls 5 blocks high and nothing to stand on inside holds them. Anything they can perch on
    lowers the wall they need to clear; a roof always works.
25. Watch whether jumps look right: the arc, the landing, and whether spiders get stuck or jitter.

Please also keep an eye on server performance with many spiders, and on spiders near water.

## Behaviour worth knowing

- Only spiders born in captivity (generation 1 or more) are domesticated. Feeding wild-caught spiders only
  lets them breed.
- A meal is a kill of a farm-born chicken or hare. Spiders only hunt when hungry.
- Wild hammocks appear when a spider first spawns in a spider chunk, not when the world is generated.
- Night is when the sun is below the horizon, so its length follows the season and latitude.
- A sleeping wild spider does not flee when a player walks up, only when attacked or when its hammock breaks.
- A hammock that breaks because its support was removed (a felled tree) angers nobody.
- Wild spiders go to bed at dusk. Domesticated ones stay up for their daily curiosity times
  `CuriosityStayUpHoursMax` (3) in-game hours, but never more than half the night, exploring at the lower night
  curiosity and hunting if hungry. This only delays going to bed: a spider that is asleep is never woken by it, and
  everyone wakes at dawn as before. A day's curiosity is rolled at midnight, so a spider still up after midnight
  goes on with the new day's roll.
- A domesticated spider builds its hammock at the highest spot it can jump into within 8 blocks, most of the time
  (the choice is random, weighted strongly by height). It builds at floor level only when nothing higher is in
  reach, or now and then when the high spots are few. If none of the 20 spots it checks is reachable, it builds
  nothing for that meal (in a bare pen with walls 5 high about one meal in 25).
- A spider with no free hammock within 24 blocks sleeps where it stands.
- Spiders take no fall damage.

## Assets to make

Shapes go in `assets/jumpingspiders/shapes`, textures in `assets/jumpingspiders/textures`. Keep the file names,
or change the paths in the entity, block and item JSON.

| File | Notes |
|---|---|
| `shapes/entity/land/jumpingspider/male.json`, `female.json`, `baby.json` | Texture code `skin` |
| `shapes/block/silkhammock-wall.json` | Drawn against the south side of its cell; the JSON rotates it |
| `shapes/block/silkhammock-floor.json`, `silkhammock-ceiling.json` | Texture codes `silk`, `strand` |

Animations each spider shape needs, with these exact lowercase codes:

| Code | When it plays |
|---|---|
| `idle` | Standing |
| `walk`, `run` | Moving, stalking |
| `jumpwindup` | Front legs raised before an attack or exploring jump. Held for 0.5 to 1.25 s |
| `jump` | In the air |
| `sleep` | In the hammock at night |
| `hurt`, `die` | Played by the game itself |

In a shape file, every keyframe element must set all six of `offsetX/Y/Z` and `rotationX/Y/Z`, or the server
fails to load the animation.

The collision box is 0.8 wide and 0.6 high for adults (`hitboxSize` in the entity JSON). The jump planner uses it, so
change it there if the model ends up a very different size.

## Not in this version

- PetAI integration.
- A port to game version 1.23.

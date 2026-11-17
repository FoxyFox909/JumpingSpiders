# Jumping Spiders

A Vintage Story code mod (game version 1.22.7) that adds the Jumping Spider, a passive predator that leaps onto
its prey, and the Silk Hammock block it sleeps in.

The adult male has its real model, with walk, run and jump animations and a texture that is still being
painted. Every other model, texture and animation in this repository is a placeholder (boxes with vanilla hare
and linen textures). See
[Assets to make](#assets-to-make).

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

Unit tests (pure logic only: jump maths, jump planning, spider chunks, meal and despawn rules, hammock building and
naps):

```bash
dotnet test JumpingSpiders.Tests/JumpingSpiders.Tests.csproj
```

## Where things are

| Folder | What is in it |
|---|---|
| `JumpingSpiders/Config` | `JumpingSpidersConfig.cs`: every global knob, each with a comment |
| `JumpingSpiders/Logic` | Pure C# with no game types: jump maths, the jump planner, spider chunks, rules. Unit-tested |
| `JumpingSpiders/Jumping` | `JumpController`: launches and steers one jump of one spider |
| `JumpingSpiders/AiTasks` | The five AI tasks: jump attack, jump flee, explore, sleep (and daytime naps), build hammock |
| `JumpingSpiders/Spider` | The spider's entity behaviors (meals, taming, growing up) and its saved attributes |
| `JumpingSpiders/Hammock` | The Silk Hammock block, its block entity, and hammock finding and building |
| `JumpingSpiders/Spawning` | Wild spawning in spider chunks, and wild hammocks on tree trunks |
| `JumpingSpiders/Commands` | The `/jspider` debug commands |
| `JumpingSpiders/assets/jumpingspiders` | Entity, block and item JSON, shapes, language file |

## Knobs

Global knobs are in `ModConfig/jumpingspiders.json` inside the game's data folder. The file is created with the
defaults on first start; `Config/JumpingSpidersConfig.cs` explains each one. Restart the world after changing it.
An existing file keeps the values it has when a new version of the mod changes a default (only settings that are new
get added), so edit or delete the file to get new defaults.
This version reworked hammock building: it removed `HammockHeightBias`, `HammockBuildSamples` and
`HammockBuildMaxPlanAttempts`, changed `HammockSpacing` (4 to 3), `HammockBuildSearchRadius` (8 to 6),
`HammockBuildSearchUp` (8 to 5) and `HammockBuildSearchDown` (3 to 2), and added `HammockBuildIntervalHours` and
`HammockBuildRange`. It changed `CuriosityStayUpHoursMax` (3 to 1.8) and added the nap knobs `NapSlotHours`,
`NapChanceMax`, `NapMinMinutes` and `NapMaxMinutes`.

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
| How long a domesticated spider stays up after dusk | `CuriosityStayUpHoursMax`, `CuriosityStayUpMaxNightShare` | curiosity times 1.8 in-game hours, at most half the night |
| Daytime naps | `NapSlotHours`, `NapChanceMax`, `NapMinMinutes`, `NapMaxMinutes` | one roll per 2-hour slot, chance 0.3 for the least curious falling to 0 for the most curious, 30 to 60 in-game minutes |
| Hours a spider stays sated after a kill | `SatedHoursAfterKill` | 12 |
| One hammock per this many blocks cubed | `HammockSpacing` | 3 |
| Hammock building: how often, how close the spider must be, where it picks | `HammockBuildIntervalHours`, `HammockBuildRange`, `HammockBuildSearchRadius`, `HammockBuildSearchUp`, `HammockBuildSearchDown` | one attempt per in-game hour, 2 blocks, within 6 blocks, 5 up to 2 down |
| How one build attempt is spread over ticks, and how long building takes | `pickTriesPerTick`, `maxPlanSearches`, `clingChecksPerTick`, `approachTimeoutMs`, `buildTimeMs` on the `jumpingspiders.buildhammock` task | 40 cells, 2 planner searches, 4 jump tests, 20 s, 1.5 s |
| How strongly wild hammock sites on trunks favour height | `WildHammockHeightBias` | 2 |
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
| `/jspider info` | Generation, wild or domesticated, hungry, asleep, curiosity and how long it stays up after dusk, nap chance per slot and whether it is napping, whether it owes a hammock and when its next build attempt comes, home hammock |
| `/jspider hungry` | Makes the spider hungry so it hunts |
| `/jspider sated [hours]` | Makes it sated |
| `/jspider generation <n>` | Sets its generation. 0 is wild, 1 or more is domesticated |
| `/jspider spawn [auto\|hammock\|nohammock] [now\|day\|night]` | Spawns a wild spider 2 to 5 blocks from you, in any chunk, and lists the rules that would stop a natural spawn here. `hammock`: on a tree within 10 blocks (asleep in it at `night`), or none if no tree there works |
| `/jspider build` | Makes a domesticated adult start a build attempt now, ignoring its hourly timer and hunger. Says which spot it picked; the result follows in chat |

Spiders and the hammock are also in the creative inventory. A spider placed from there counts as kept by the
player and never despawns.

## What to test in the game

None of this has been seen by a person yet. The logic was exercised on a headless server with hand-driven
ticks and stand-ins for the player, so everything a real player does or sees is untested.

Use creative mode, `/time set` to move between day and night, and `/jspider info` to see a spider's state.

**Spawning**
1. Stand next to a few trees, in any chunk, and use `/jspider spawn hammock night`: a hammock appears on a
   trunk within 10 blocks, with a spider asleep in it. The reply lists the natural rules it skipped (for example
   "not a spider chunk"); `/jspider chunk` shows the same facts.
2. `/jspider spawn hammock day`: the spider appears on the ground 2 to 5 blocks from you, with that hammock as its
   home (`/jspider info`).
3. `/jspider spawn` again in the same chunk: it reuses the existing hammock and does not make a second one.
   On open grassland or sand, `/jspider spawn hammock` still gives a spider, and the reply says there was no
   usable tree, so it has no hammock. `/jspider spawn nohammock` never makes one.
4. Without commands: walk through a forest with `/jspider chunk` until you stand in a spider chunk, or go to one
   it lists. Wild spiders appear by themselves in spider chunks 24 to 80 blocks from you. This is slow by design
   (a check every 20 seconds, 12 hours of cooldown per chunk; `/jspider spawn` there starts that cooldown too).
5. Walk more than 32 blocks from a wild spider for 10 real minutes: it is gone, and its hammock is still there.

**Wild behaviour**
6. Walk up to an awake wild spider: it jumps away, with no wind-up.
7. Put hares, chickens or raccoons near a hungry wild spider (`/jspider hungry`): it raises its front legs,
   leaps and bites. The carcass gives much less meat than usual.
8. Step aside from the prey's position during the wind-up: the spider can miss a moving target.
9. Hit a wild spider: it attacks you, and only you.
10. Break the hammock of a sleeping wild spider: it wakes and attacks you. Break it while the spider is away:
    nothing happens.
11. At dusk a wild spider returns to its own hammock and sleeps at once, lying in the cradle (on the base in a
    floor hammock) and looking out of the opening. At dawn it leaves.
12. Bait a reed basket trap with raw meat near a wild spider: it gets caught about half the time.

**Hammock block**
13. Place a hammock on each of the four sides of a block, on a floor and under a ceiling. On a wall the back of
    the hammock is against the wall and it opens away from it; under a ceiling and on the floor it opens to the
    west, and the floor one is the same model upside down (a tent over a base). Break the block holding it: the
    hammock breaks too.
14. Break hammocks by hand and with shears: 2 to 4 flax twine each time, and shears are about five times faster.

**Taming and breeding**
15. Release a trapped male and female into a pen with farm-born chickens or hares (generation 1 or more).
    They hunt when hungry. After 2 valid kills by the female and 1 by the male, the female becomes pregnant.
16. Wild chickens, wild hares, raccoons, troughs and meat on the ground give no breeding progress.
17. A caught wild spider still flees from you, never builds a hammock, and adopts a free hammock near the pen.
18. The babies are generation 1: they do not flee from you. As adults, each kill earns one hammock: while
    sated they make one build attempt per in-game hour until one succeeds, then none until the next kill
    (`/jspider info` shows whether one is owed and when the next attempt comes). No two hammocks closer than one
    per 3x3x3 blocks; they sleep in a free one at night.
19. Watch an attempt with `/jspider build` (break the hammock and repeat to see the spread): the spider picks a
    spot it can see within 6 blocks, at any height, then gets within 2 blocks of it: it builds standing where it
    is, or hops or walks closer, or jumps at the wall or ceiling next to the spot, holds on there for a moment
    while it builds, and drops. A spot too close to another hammock, or one it cannot get to, fails, and the next
    try comes an hour later. Look for jitter while it holds on, and whether the server hitches.
20. At dusk, domesticated spiders stay up for a while before they go to bed: up to 1.8 in-game hours for a very
    curious one, about 20 minutes for the least curious (`/jspider info` shows today's curiosity and how long).
    Meanwhile they hop around less than by day and hunt when hungry. A spider already asleep stays asleep.
    By day, spiders that are not very curious take naps of 30 to 60 in-game minutes in a hammock (their home
    hammock for wild ones), at different times from each other, and hop out afterwards as in the morning.
    `/jspider info` shows the nap chance per 2-hour slot and whether it is napping. A spider with no hammock it
    can get to never naps. Walk up to a napping wild spider: it stays put; break its hammock: it attacks you.
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
- A sleeping wild spider (at night or napping) does not flee when a player walks up, only when attacked or when its
  hammock breaks.
- A hammock that breaks because its support was removed (a felled tree) angers nobody.
- Wild spiders go to bed at dusk. Domesticated ones stay up for their daily curiosity times
  `CuriosityStayUpHoursMax` (1.8) in-game hours, but never more than half the night, exploring at the lower night
  curiosity and hunting if hungry. This only delays going to bed: a spider that is asleep is never woken by it, and
  everyone wakes at dawn as before. A day's curiosity is rolled at midnight, so a spider still up after midnight
  goes on with the new day's roll.
- A kill earns a domesticated adult one hammock (any kill; set `HammockNeedsFarmPrey` to require a farm-born
  chicken or hare). While sated it makes one build attempt per in-game hour until
  one succeeds. An attempt picks one spot at random among the cells within 6 blocks that could hold a hammock and
  that it can see, every one equally likely whatever its height (in a pen with walls 5 high about 4 in 10 end up at
  floor level and the rest spread over the four wall heights, simply because the floor has the most such cells; under a
  roof many hang from the ceiling). If another hammock is too close (one per 3x3x3), the
  attempt fails. Otherwise the spider must get within 2 blocks of the spot (from the middle of its body to the
  nearest point of the cell, with nothing solid in between), standing, after a hop or a walk, or holding on to the
  wall or ceiling next to it after a jump; from the floor that reaches spots up to 5 blocks up. If it cannot get
  there within 20 seconds, the attempt fails. A failed attempt waits for the next hour. In tests in walled pens,
  about 98 attempts in 100 that passed the spacing rule built a hammock, two thirds standing, the rest holding on to
  a wall or under the roof. As a pen fills up, more attempts fail on the spacing rule.
- Spiders nap by day: once per 2-hour slot (shifted per spider by a random amount each day) a spider rolls for a
  nap with a chance of `NapChanceMax` (0.3) times how far its curiosity for the day is below the maximum, so the
  least curious nap about twice a day and the most curious never. A nap lasts 30 to 60 in-game minutes in a
  hammock, never on the bare ground; one that runs into dusk becomes the night's sleep.
- A spider with no free hammock within 24 blocks sleeps where it stands.
- Spiders take no fall damage.

## Assets to make

Shapes go in `assets/jumpingspiders/shapes`, textures in `assets/jumpingspiders/textures`. Keep the file names,
or change the paths in the entity, block and item JSON.

| File | Notes |
|---|---|
| `shapes/entity/land/jumpingspider/male.json` | The real adult male model, with `walk`, `run`, `jumpwindup` and `jump`. Its texture is `textures/male.png`, still being painted |
| `shapes/entity/land/jumpingspider/female.json`, `baby.json` | Placeholders. Texture code `skin` |
| `shapes/block/silkhammock-wall.json` | The one hammock model for all six variants. Back panel on the east (+X) side, opening to the west (-X); the block JSON turns it to each wall and turns it upside down for the floor. Texture code `silk`; the cradle's untextured faces (`#null`) are mapped to the same placeholder linen in the block JSON until they get a texture |

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

The male model has its own texture, `textures/male.png` (96 x 96 pixels for the model's 48 x 48 texture units).
VS Model Creator names a texture's code after its file, so the faces use `#male`, and the entity JSON and
`itemtypes/creature.json` map `male` to `jumpingspiders:male`. If the file is renamed or moved, change both.

The collision box is set in the entity JSON (`hitboxSize`): 0.8 wide and 0.4 high for both adults, to fit the
male's model (the female will get the same proportions), and 0.4 by 0.3 for the baby. The jump planner uses it,
so change it there if a model ends up a very different size. It must stay at most 0.4375 high: a spider sleeping
in a wall or ceiling hammock stands 9/16 above the bottom of the cell, and the block above a ceiling hammock is
solid. `eyeHeight` next to it must stay inside the box.

Where a sleeping spider's feet are is `sleepFeetHeight` in the block JSON: 9/16 above the bottom of the cell for
walls and the ceiling (the cradle's bottom) and 1/16 for the floor (its base). A spider lands on the hammock's
collision box: for walls a slab up to the cradle's bottom, for the floor the base. Under a ceiling there is too
little room to jump into the cradle, so the ceiling hammock has a thin floor at the bottom of its cell; the spider
lands there and is lifted into the cradle as it falls asleep (and put back down when it wakes). If the model's
cradle moves, change `sleepFeetHeight` and the wall slab to match.

## Not in this version

- PetAI integration.
- A port to game version 1.23.

# Jumping Spiders

A Vintage Story code mod (game version 1.22.7) that adds the Jumping Spider, a passive predator that leaps onto
its prey, and the Silk Hammock block it sleeps in.

Both adult sexes share the real adult model, with walk, run and jump animations, and two texture variants that
are still being painted; the female draws her pedipalps thinner. The spiderling's model and texture are
placeholders (a box with a vanilla hare texture). See [Assets to make](#assets-to-make).

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
| `JumpingSpiders/Jumping` | `JumpController`: launches one jump of one spider and flies it along the planned arc, tick by tick |
| `JumpingSpiders/AiTasks` | The five AI tasks: jump attack, jump flee, explore, sleep (and daytime naps), build hammock |
| `JumpingSpiders/Spider` | The spider's entity behaviors (meals, taming, growing up) and its saved attributes |
| `JumpingSpiders/Hammock` | The Silk Hammock block, its block entity, and hammock finding and building |
| `JumpingSpiders/Spawning` | Wild spawning in spider chunks, and wild hammocks on tree trunks |
| `JumpingSpiders/Commands` | The `/jspider` debug commands |
| `JumpingSpiders/assets/jumpingspiders` | Entity, block and item JSON, shapes, language file |
| `tools/model` | Python scripts for shape files: mirror keyframes, lay out UVs, check animations |
| `.asset_workshop` | Working files for the art (editor projects, UV maps). Version-controlled, not shipped with the mod |
| `docs/engine-notes` | Reference notes on Vintage Story 1.22.7 internals. `CLAUDE.md` is the guide for AI agents |

## Knobs

Global knobs are in `ModConfig/jumpingspiders.json` inside the game's data folder. The file is created with the
defaults on first start; `Config/JumpingSpidersConfig.cs` explains each one. Restart the world after changing it.
An existing file keeps the values it has when a new version of the mod changes a default (only settings that are new
get added), so edit or delete the file to get new defaults.
This version reworked hammock building: it removed `HammockHeightBias`, `HammockBuildSamples` and
`HammockBuildMaxPlanAttempts`, changed `HammockSpacing` (4 to 3), `HammockBuildSearchRadius` (8 to 6),
`HammockBuildSearchUp` (8 to 5) and `HammockBuildSearchDown` (3 to 2), and added `HammockBuildIntervalHours` and
`HammockBuildRange`. `HammockBuildRange` has since changed from 2 to 0.5: a `jumpingspiders.json` written by the
version before keeps 2 (spiders then build from farther off), so set it to 0.5 there or delete the file. It changed `CuriosityStayUpHoursMax` (3 to 1.8) and added the nap knobs `NapSlotHours`,
`NapChanceMax`, `NapMinMinutes` and `NapMaxMinutes`. The latest version adds `DebugLogging` (see [Debug log](#debug-log)).

Per-task knobs are in the entity JSON (`assets/jumpingspiders/entities/land/jumpingspider-adult.json` and
`-baby.json`), next to the task they belong to:

| Knob | Where | Default |
|---|---|---|
| Wind-up time before a jump (attack, exploring, into the hammock; fleeing jumps have none) | `windUpMinMs` / `windUpMaxMs` on the jump attack, explore and sleep tasks | 350 to 900 ms |
| Jump cooldown | `mincooldown` / `maxcooldown` on each task | 1.5 to 3 s for attacks |
| Bite damage | `damage` on the jump attack tasks | 3 to prey, 2 to players, 1 for babies |
| Jump angle, maximum speed | `JumpAngleDeg`, `MaxJumpSpeed` in the config | 50 degrees, 18 |
| How high, far and deep a jump goes | `MaxJumpUp`, `MaxJumpHorizontal`, `MaxJumpDown` | 4, 6, 6 blocks |
| How fast a jump is flown (same arc, same landing spot; 1 = the game's natural gravity) | `JumpFlightSpeedMultiplier` in the config | 1.45 |
| Jumps per plan | `MaxPlannedJumps` | 2 (3 or more makes one plan search cost tens of milliseconds) |
| Curiosity (rolled per spider per day) | `CuriosityMin`, `CuriosityMax`, `CuriosityNightMultiplier` | 0.2 to 1.0, times 0.2 at night |
| How long a domesticated spider stays up after dusk | `CuriosityStayUpHoursMax`, `CuriosityStayUpMaxNightShare` | curiosity times 1.8 in-game hours, at most half the night |
| Daytime naps | `NapSlotHours`, `NapChanceMax`, `NapMinMinutes`, `NapMaxMinutes` | one roll per 2-hour slot, chance 0.3 for the least curious falling to 0 for the most curious, 30 to 60 in-game minutes (in a hammock, else on the spot) |
| Hours a spider stays sated after a kill | `SatedHoursAfterKill` | 12 |
| One hammock per this many blocks cubed | `HammockSpacing` | 3 |
| Hammock building: how often, how close the spider must be, where it picks | `HammockBuildIntervalHours`, `HammockBuildRange`, `HammockBuildSearchRadius`, `HammockBuildSearchUp`, `HammockBuildSearchDown` | one attempt per in-game hour, 0.5 blocks, within 6 blocks, 5 up to 2 down |
| How one build attempt is spread over ticks, and how long building takes | `pickTriesPerTick`, `maxPlanSearches`, `clingChecksPerTick`, `approachTimeoutMs`, `buildTimeMs` on the `jumpingspiders.buildhammock` task | 40 cells, 2 planner searches, 4 jump tests, 20 s, 1.5 s |
| The last bit onto the building spot | `sidleSpeed`, `maxSidleDistance`, `maxCrawlDistance` on the `jumpingspiders.buildhammock` task | 1.5 blocks per second, sidles up to 0.8 blocks, crawls up to 1 block |
| Flax twine from a broken hammock | `drops` in `blocktypes/silkhammock.json` | 4 to 6 |
| How strongly wild hammock sites on trunks favour height | `WildHammockHeightBias` | 2 |
| Share of chunks that are spider chunks | `SpiderChunkFraction` | 0.04 |
| Chance a wild spider comes with a hammock | `WildHammockChance` | 0.75 |
| Carcass yield of the spider's victims | `deathByMultiplier` in the adult JSON | 0.2 |
| Breeding | the `multiply` behavior in the adult JSON | 2 meals, 4 days pregnant, 1 to 3 babies |
| Growing up | `hoursToGrow` in the baby JSON | 120 in-game hours |
| Model parts drawn at another size or shape, per sex | `elementScalesByType` on the client `jumpingspiders.spider` behavior in the adult JSON | female `PedipalpL`, `PedipalpLB`, `PedipalpR`, `PedipalpRB` `[1, 0.6, 0.6]` (as long, 0.6 as thick; placeholders to tune by eye), male as modelled |

`DomesticatedFromGeneration` in the config and the `maxGeneration: 0` / `minGeneration: 1` values in the entity
JSON must be changed together.

A setting in `jumpingspiders.json` that would break the mod (a negative search radius, an interval or spacing of 0, a
share or chance outside 0 to 1, a maximum below its minimum, and the like) is corrected when the world starts, with a
warning in the server log naming the setting and the value used instead. The file keeps what you wrote, so the
warning comes back until you fix it there.

## Debug log

Set `"DebugLogging": true` in `ModConfig/jumpingspiders.json` and restart the world to have every spider write what it
decides and why (each jump, hunt, flee, bedtime, nap and build attempt, with how long the attempt took) to
`Logs/server-debug.log` in the game's data folder, lines starting with `[jumpingspiders]`. Wild spawning logs there
too. It is off by default, and then the mod does not even put those lines together. Please switch it on, reproduce
the problem and attach that log when you report one. The `/jspider` replies work either way.

## Debug commands

All need the `controlserver` privilege (the world's owner has it). They act on the spider you look at, or the
nearest one.

| Command | What it does |
|---|---|
| `/jspider chunk` | Is this a spider chunk? Shows climate, its wild hammock, and the nearest spider chunks |
| `/jspider info` | Generation, wild or domesticated, texture variant, hungry, asleep, curiosity and how long it stays up after dusk, nap chance per slot and whether it is napping, whether it owes a hammock and when its next build attempt comes, home hammock |
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
5. Walk more than 96 blocks from a wild spider for 10 real minutes: it is gone, and its hammock is still there.

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
    the hammock is against the wall and it opens away from it; under a ceiling and on the floor it opens towards
    you, or to another side when a wall is right in front of that side (it is placed anyway when all four sides
    are walled in), and the floor one is the same model upside down (a tent over a base). Place one under a
    ceiling and one on the floor from each of the four sides: check that each opens where it should, and that
    its outline (what the cursor highlights) matches the model. Break the block holding it: the hammock breaks too.
14. Break hammocks by hand and with shears: 4 to 6 flax twine each time, and shears are about five times faster.

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
    spot it can see within 6 blocks, at any height, then gets right next to it (within half a block): it builds
    standing beside it, after a hop or a walk and a short sidle, or jumps at the wall or ceiling next to the spot,
    holds on there (crawling the last bit if it landed a little off), builds for a moment, and drops. Hammocks
    spiders build on the floor or under a ceiling open towards a side that is not walled in. A spot too close to another hammock, or one it cannot get to, fails, and the next
    try comes an hour later. Look for jitter while it holds on, and whether the server hitches.
20. At dusk, domesticated spiders stay up for a while before they go to bed: up to 1.8 in-game hours for a very
    curious one, about 20 minutes for the least curious (`/jspider info` shows today's curiosity and how long).
    Meanwhile they hop around less than by day and hunt when hungry. A spider already asleep stays asleep.
    By day, spiders that are not very curious take naps of 30 to 60 in-game minutes in a hammock (their home
    hammock for wild ones), at different times from each other, and hop out afterwards as in the morning.
    `/jspider info` shows the nap chance per 2-hour slot and whether it is napping. A spider with no hammock it
    can get to naps on the ground where it stands, and hops off afterwards. Walk up to a napping wild spider (in
    a hammock or on the ground): it stays put; break its hammock: it attacks you.
21. Hit a domesticated spider: it flees and never attacks. Break its hammock while it sleeps: no attack.
22. Two adult males or two adult females fight when hungry. A male and a female do not. Babies are left alone.

**Jumping and enclosures**
23. Domesticated spiders hop onto high blocks out of curiosity, up to 4 blocks per jump, two jumps in a row.
24. A pen with walls 5 blocks high and nothing to stand on inside holds them. Anything they can perch on
    lowers the wall they need to clear; a roof always works.
25. Watch whether jumps look right: the arc, the landing, and whether spiders get stuck or jitter.

**Looks**
26. Spawn several adults of each sex (`/jspider spawn`): about half wear `male.png` and half `male2.png`
    (`/jspider info` shows the variant: 0 or 1). A spider keeps its variant after a save and reload and after a
    trip in a basket trap. A spiderling that grows up gets a new random variant (the baby has no variants yet).
27. Females have pedipalps as long as the male's but thinner (0.6 as thick), both the upper part and the claw,
    still attached at the base and at the bend, and they move with every animation without stretching or leaning
    out of shape (watch the claw in the jump wind-up, the jump and sleep). The texture on them is squeezed to the
    thinner faces. Males look exactly as modelled. The client log shows no warning from `jumpingspiders`.
    The female's creature item in the inventory shows the male's pedipalps (items cannot change parts).

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
  attempt fails. Otherwise the spider must get within half a block of the spot (`HammockBuildRange`, from the middle
  of its body to the nearest point of the cell, with nothing solid in between): standing on the floor right beside
  a spot at floor level, or holding on to the wall or ceiling right beside or below a higher spot after a jump; from
  the floor that reaches spots up to 4 blocks up. It sidles or crawls the last few tenths of a block onto the exact
  spot. If it cannot get there within 20 seconds, the attempt fails. A failed attempt waits for the next hour. In
  tests in walled pens about 98 attempts in 100 that passed the spacing rule built a hammock (open pens: four in ten
  standing, the rest holding on to a wall; roofed pens: four in ten standing, the rest holding on to a wall or under
  the roof). As a pen fills up, more attempts fail on the spacing rule.
- Floor and ceiling hammocks open towards one of the four sides: towards the player who places one (or the next
  side that is not walled in), towards a random free side when a spider builds one. Wall hammocks open away from
  their wall.
- Spiders nap by day: once per 2-hour slot (shifted per spider by a random amount each day) a spider rolls for a
  nap with a chance of `NapChanceMax` (0.3) times how far its curiosity for the day is below the maximum, so the
  least curious nap about twice a day and the most curious never. A nap lasts 30 to 60 in-game minutes in a
  hammock, or on the ground where the spider stands when it has no free hammock within 24 blocks or cannot get to
  the one it chose (as at night); one that runs into dusk becomes the night's sleep. A spider rolls only while it
  stands on dry ground. A nap may interrupt a hammock build attempt; the next attempt comes an hour later.
- A spider with no free hammock within 24 blocks sleeps where it stands.
- A spider hit while it holds on to a wall or ceiling to build a hammock lets go and drops, and that attempt fails (the
  next one comes an hour later).
- Spiders take no fall damage.
- A dead spider disappears once it has been harvested, or after 4 in-game days (2 for a spiderling). It leaves no
  bone pile behind.

## Assets to make

Shapes go in `assets/jumpingspiders/shapes`, textures in `assets/jumpingspiders/textures`. Keep the file names,
or change the paths in the entity, block and item JSON.

| File | Notes |
|---|---|
| `shapes/entity/land/jumpingspider/male.json` | The real adult model, for both sexes despite its name, with `walk`, `run`, `jumpwindup`, `jump` and `sleep`. Its textures are `textures/male.png` and `textures/male2.png` (both sexes), still being painted |
| `shapes/entity/land/jumpingspider/baby.json` | Placeholder box model with a vanilla hare texture (code `skin`). The spiderling will get its own model and texture, based on the adult model and its animations |
| `shapes/block/silkhammock-wall.json` | The one hammock model for all twelve variants: seven boxes, two roof plates, four cradle panels and a back. Back on the east (+X) side, opening to the west (-X); the block JSON turns it to each wall, to each opening direction under a ceiling, and upside down and to each opening direction on the floor. Every face uses the texture `textures/silk_hammock.png` (texture code `silk_hammock`) |

Animations each spider shape needs, with these exact lowercase codes:

| Code | When it plays |
|---|---|
| `idle` | Standing |
| `walk`, `run` | Moving, stalking |
| `jumpwindup` | Front legs raised before an attack, exploring or hammock jump. Held for 0.35 to 0.9 s |
| `jump` | In the air |
| `sleep` | In the hammock at night |
| `hurt`, `die` | Played by the game itself |

In a shape file, every keyframe element must set all six of `offsetX/Y/Z` and `rotationX/Y/Z`, or the server
fails to load the animation.

**One adult model for both sexes.** `male.json`, `male.png` and `male2.png` keep their names because the editor
project uses them, but they are the adult model and textures for males and females alike. The textures are 96 x 96
pixels for the model's 48 x 48 texture units. VS Model Creator names a texture's code after its file, so the faces
use `#male`, and the entity JSON and `itemtypes/creature.json` map `male` to `jumpingspiders:male`. If a file is
renamed or moved, change both.

**Texture variants.** In the adult entity JSON the `male` texture lists `male2` under `alternates`; every file there
must have the same UV layout. The server picks one variant at random (`textureIndex`: 0 for `male.png`, 1 for
`male2.png`) when a spider first enters the world, from the wild spawner, `/jspider spawn` or a creature item, and
saves it with the spider, so it survives a reload and a basket trap. The game counts variants on the entity's
first texture only, so `male` must stay the first texture listed. To add a third variant, add a file with the same
layout to `alternates`. A spiderling has no variants yet, so a spider gets a new random one when it grows up; once
the baby's texture has as many alternates as the adult's, growing up keeps the baby's variant (vanilla behaviour).
The creature item always shows `male.png`.

**The female's thinner pedipalps** are made from the same model file, with no second model and no extra
keyframes. `elementScalesByType` on the client `jumpingspiders.spider` behavior in the adult JSON lists, per sex,
model elements to draw at another size or shape. Element names are the ones in VS Model Creator (exact spelling and
case). Each takes one of two kinds of value:

- **One number**, a uniform scale, such as `"PedipalpL": 0.6`. The element shrinks toward its rotation origin, its
  child elements shrink with it, and keyframe offsets on it are scaled too.
- **Three numbers `[x, y, z]`**, one factor per axis of the element's own box: the X, Y and Z of its Size in the
  editor, whatever the element's rotation. The box shrinks toward the rotation origin on each axis. Only that
  element changes: its children keep their size and are moved so they stay attached at their joints, so list each
  child that should change too, in its own axes. Keyframe offsets are not scaled.

The female's default is `[1, 0.6, 0.6]` on `PedipalpL`, `PedipalpLB`, `PedipalpR` and `PedipalpRB`: in all four, X is
the length and Y and Z are the thickness, so the pedipalps keep the male's length and are 0.6 as thick. Why
three numbers reshape the box instead of setting the element's scale: the game applies an element's scale after
its children's rotation, so a scale that differs per axis on `PedipalpL` would make the claw (`PedipalpLB`, turned
90 degrees inside it) shorter instead of thinner, and skew it out of a box shape whenever an animation turns it (the
jump and sleep animations do). A reshaped box has no scale in its matrix, so nothing below it can skew.

Any element can be listed, for either sex. Before drawing a spider the game hands the model to the behavior, which
changes those elements on a copy of it: the loaded model is shared by every spider of both sexes, so the male is
never touched. The server does not draw models and keeps the model only for animation timing, so it needs none of
this. If an element in the list is renamed in the editor, the client log warns about it.

The collision box is set in the entity JSON (`hitboxSize`): 0.8 wide and 0.4 high for both adults, to fit the
adult model, and 0.4 by 0.3 for the baby. The jump planner uses it,
so change it there if a model ends up a very different size. It must stay at most 0.4375 high: a spider sleeping
in a wall or ceiling hammock stands 9/16 above the bottom of the cell, and the block above a ceiling hammock is
solid. `eyeHeight` next to it must stay inside the box.

Where a sleeping spider's feet are is `sleepFeetHeight` in the block JSON: 9/16 above the bottom of the cell for
walls and the ceiling and 1/16 for the floor (its base). In the model the cradle's bottom is lower than that, at
about 0.45: the gap is room for the spider's legs. A spider lands on the hammock's collision box: for walls a slab
up to the sleeping height, for the floor the base. Under a ceiling there is too
little room to jump into the cradle, so the ceiling hammock has a thin floor at the bottom of its cell; the spider
lands there and is lifted into the cradle as it falls asleep (and put back down when it wakes). If the model's
cradle moves a lot, change `sleepFeetHeight` and the wall slab (keep the two equal) so the legs still clear the silk.

## Not in this version

- PetAI integration.
- A port to game version 1.23.

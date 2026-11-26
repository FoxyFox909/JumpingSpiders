> Reference notes on Vintage Story 1.22.7 internals, gathered from the game sources when this mod was started.
> They describe the engine, not the mod's current design (see `CLAUDE.md` and `README.md` for that). File paths in
> them are from the machine they were written on.

# JumpingSpiders: official docs and the JSON side of creatures (VS 1.22.7)

Research date: 2026-11-12. Read-only research; nothing under `/home/user/JumpingSpiders` was changed.

## How to read this file

Path abbreviations:

- `VS/` is `/home/user/vs-game/1.22.7/` (the installed dedicated server; vanilla JSON is under `VS/assets/survival` and `VS/assets/game`).
- `API/`, `ESS/`, `SURV/` are `/home/user/anegostudios/vsapi`, `vsessentialsmod` and `vssurvivalmod` (each at commit "Updated to Version 1.22.7").
- `TPL/` is `/home/user/JumpingSpiders/` (the mod template).
- Wiki pages are cited as `[Wiki: Page title]` (https://wiki.vintagestory.at, raw wikitext fetched 2026-11-12).

Confidence tags:

- **[files]** means the claim is checked against 1.22.7 assets or source.
- **[wiki]** means the claim comes from the wiki only and was not cross-checked.
- **[strings]** means the claim was checked only by finding the literal string in the 1.22.7 binaries (`VintagestoryLib.dll` is closed source). The name exists, but the behaviour was not run.
- **[unverified]** means the claim is a reasoned guess. Treat it as such.

Two things could not be run here:

- No `dotnet` is installed in this container, so nothing was built or launched.
- The server install has no entity textures or sounds (only 46 PNG files in total), so texture paths cannot be checked on disk.

---

## 1. Project setup and conventions

### 1.1 What the wiki prescribes

- Use the NuGet template: `dotnet new install VintageStory.Mod.BasicTemplate`. Point the `VINTAGE_STORY` environment variable at the game folder (the folder that holds `assets`, `Mods` and `Lib`). Install the **.NET 10 SDK**. [Wiki: Modding:Preparing For Code Mods] (marked 1.19.8, but the page text already says .NET 10).
- The engine agrees: `VS/VintagestoryServer.runtimeconfig.json` has `"tfm": "net10.0"`, and the 1.22 notes say "migrated ... from .NET 8 to .NET 10". [files] [Wiki: Modding:Modding API Updates § 1.22]. The template targets `net10.0` (`TPL/JumpingSpiders/JumpingSpiders.csproj`).
- Linux notes: [Wiki: Modding:Setting up your Development Environment] (marked Outdated). It says `VINTAGE_STORY` may need to go in `/etc/environment` for Rider to see it. The repo contains a `.idea` folder, so the author probably uses Rider.

### 1.2 modinfo.json

The fields map 1:1 to `API/Common/API/ModInfo.cs` [files]. [Wiki: Modding:Modinfo] (1.19.8) lists the same fields.

| field | notes |
|---|---|
| `type` | `"Code"` (parsed into an enum, case-insensitive; the template writes `"Code"`) |
| `modid` | Lowercase letters and digits only, and it must start with a letter (`ModInfo.IsValidModID`). It is also the asset **domain**. Keep it `jumpingspiders`. |
| `name`, `version`, `description`, `authors`, `contributors`, `website` | `version` is used for dependency resolution |
| `networkVersion` | Defaults to `version`. Bump it to lock out clients running an older version. |
| `side` | `Universal` (default), `Server` or `Client` |
| `requiredOnClient`, `requiredOnServer` | Both default to true |
| `iconPath` | Since 1.21. Defaults to `modicon.png` in the mod root. |
| `dependencies` | `{ "modid": "version" }`. The version is a **minimum** ("The minimum version requirement", `API/Common/API/ModDependency.cs`). An empty string or `"*"` means any version [Wiki: Modding:Modinfo]. There is no upper bound. |

**Game version for 1.22.7.** The template currently says `"game": "1.21.0"` (`TPL/JumpingSpiders/modinfo.json`), and that needs to change.

- Real 1.22 mods use `"game": "1.22.0"`: PetAI 5.1.2 and WolfTaming 5.0.1 (`modinfo.json` in their GitHub repos).
- Recommendation: use `"game": "1.22.7"` until the mod has been tested on an earlier 1.22.x patch, then relax it to `"1.22.0"`.
- The API version is `major.minor.0` (`API/Config/GameVersion.cs`: `APIVersion = "1.22.0"`). Anego says it changes the API on a stable branch only "in extraordinary circumstances" [Wiki: Modding:Mod-Engine Compatibility].
- Because the dependency is only a minimum, a 1.23 client will still try to load the mod.

Example (safe for 1.22.7):

```json
{
  "$schema": "https://moddbcdn.vintagestory.at/schema/modinfo.latest.json",
  "type": "Code",
  "modid": "jumpingspiders",
  "name": "Jumping Spiders",
  "authors": ["..."],
  "description": "...",
  "version": "1.0.0",
  "dependencies": { "game": "1.22.7" }
}
```

Loader messages worth knowing [strings]:

- `Is a Code mod, but no ModSystems found`
- `Multiple mods share the mod ID '{0}'... Will only load the highest version`
- `Could not resolve some dependencies: {0} - Missing / Version mismatch (has {1})`
- `Please restart the game. The mod's .dll was already loaded and cannot be reloaded` (this happens when you switch mod versions without restarting the game)

### 1.3 Asset folder layout inside a code mod

Assets live in `TPL/JumpingSpiders/assets/<domain>/<category>/...`. The folder name directly under `assets/` is the domain [Wiki: Modding:Domains]. The template already has `assets/jumpingspiders/lang/en.json`.

Suggested layout. The categories are vanilla's; the subfolders follow the 1.21+ vanilla convention `entity/animal/...` [files: `VS/assets/survival/entities/animal/...`, `shapes/entity/animal/...`]:

```
assets/jumpingspiders/
  entities/animal/arthropod/jumpingspider-adult.json      (code "jumpingspider", age adult)
  entities/animal/arthropod/jumpingspider-baby.json       (code "jumpingspider", age baby)
  itemtypes/creature.json                                  (ItemCreature spawner items)
  blocktypes/silkhammock.json
  shapes/entity/animal/arthropod/jumpingspider/{male,female,baby}.json
  shapes/block/silkhammock.json  (+ optional silkhammock-inventory.json)
  textures/entity/animal/arthropod/jumpingspider/*.png
  textures/block/silkhammock/*.png
  sounds/creature/jumpingspider/*.ogg                      (optional)
  lang/en.json
  patches/...                                              (JSON patches, e.g. optional PetAI compat)
  config/...                                               (e.g. remapentities.json if codes ever change)
```

Category-to-side mapping: `entities` is "server only" (sent to clients by the server), while `blocktypes`, `itemtypes` and `lang` are universal, and `shapes` and `textures` are used by both sides [Wiki: Modding:Asset System]. Mods can also add `assets/<mod>/config/remapentities.json`, which is merged with vanilla since 1.22 [Wiki: Modding:Modding API Updates § 1.22].

### 1.4 Asset domains (important; read with section 8)

- Vanilla domain: all three folders `VS/assets/game`, `VS/assets/survival` and `VS/assets/creative` load as domain **`game`**. Evidence: survival JSON references `"game:egg-chicken-raw"`; `egg.json` lives in `assets/survival/itemtypes`; entity names for survival creatures are in `assets/game/lang/en.json`; no `survival:` or `creative:` reference exists anywhere in the assets [files].
  - **Disagreement:** [Wiki: Modding:Domains] says "The base game introduces the creative, game and survival domains". The files win: refer to vanilla content as `game:`.
  - The survival *mod id* is `survival`, but that is not a domain (`SURV/Properties/AssemblyInfo.cs`).
- A code without a prefix gets a default domain, and that default depends on the field. See the table in section 8, pitfall 1. Rule of thumb: **in our JSON, write `game:` for vanilla things and `jumpingspiders:` for ours, everywhere**.
- In our own `lang/en.json`, write keys without a domain; in C#, use `Lang.Get("jumpingspiders:key")` [Wiki: Modding:Asset Type - Language] (marked 1.22.7, WIP).
- 1.22 changed asset loading: "Loading of assets is now fully case insensitive internally" [Wiki: Modding:Modding API Updates § 1.22]. Still, keep every code and path lowercase.

### 1.5 Build and packaging (Cake), verified from `TPL/ZZCakeBuild/Program.cs`

How to run it:

- Run `./build.sh` (or `build.ps1`). This calls `dotnet run --project ZZCakeBuild/CakeBuild.csproj -- <args>`.
- Arguments the code reads: `--configuration=Release` (the default) and `--skipJsonValidation=true`.
- Default task chain: `Default` → `Package` → `Build` → `ValidateJson`. Standard Cake Frosting also accepts `--target=Build` [unverified for this template, but it is standard Frosting].
- `CakeBuild.csproj` references `$(VINTAGE_STORY)/VintagestoryAPI.dll`, which it uses to read `modinfo.json` as `ModInfo`. So `VINTAGE_STORY` must be set for packaging too.

What each task does:

1. **ValidateJson** runs `JToken.Parse` (Newtonsoft) on every `JumpingSpiders/assets/**/*.json`.
   - It checks syntax only, not schema.
   - The game also parses assets with Newtonsoft (`API/Util/JsonUtil.cs`), so vanilla-style JSON5-ish files (unquoted keys, trailing commas, `//` comments) are accepted by both.
   - [Wiki: Modding:Setting up your Development Environment] describes it the same way: "at least parsable by Newtonsoft.Json".
2. **Build** runs `dotnet clean` and then `dotnet publish` on `JumpingSpiders.csproj`. The output goes to `JumpingSpiders/bin/<cfg>/Mods/mod/publish/`.
3. **Package** does the following:
   - empties `Releases/`;
   - copies `publish/*`, which contains `JumpingSpiders.dll`, `.pdb`, `.deps.json` and `modinfo.json`, to `Releases/jumpingspiders/`;
   - copies `JumpingSpiders/assets` to `Releases/jumpingspiders/assets`, plus `modinfo.json` and `modicon.png` if present;
   - zips the result to **`Releases/jumpingspiders_<version>.zip`**. The name comes from `modinfo.modid` and `modinfo.version`.
   - The layout matches [Wiki: Modding:Mod Packaging]: DLL, PDB and `modinfo.json` at the zip root, assets under `assets/<domain>/`.

Note: the `.csproj` copies only `modinfo.json` and `modicon.png` into `bin/.../Mods/mod`, **not the assets**. During development the assets come in through `--addOrigin` (see 1.6). The only artifact that contains everything is the release zip.

The game references are `HintPath`s into `$(VINTAGE_STORY)`, all with `Private=false`, so they are not copied: `VintagestoryAPI`, `VSSurvivalMod`, `VSEssentials`, `VSCreativeMod`, `Newtonsoft.Json`, `0Harmony`, `protobuf-net`, `cairo-sharp` and `Microsoft.Data.Sqlite`. All of these files exist in `VS/` and `VS/Lib` [files].

### 1.6 Launching the game with the mod

`TPL/JumpingSpiders/Properties/launchSettings.json` defines two profiles, and both run from `$(VINTAGE_STORY)`:

- Client: `dotnet "$(VINTAGE_STORY)/Vintagestory.dll" --tracelog --addModPath "$(ProjectDir)/bin/$(Configuration)/Mods" --addOrigin "$(ProjectDir)/assets"`
- Server: the same, with `VintagestoryServer.dll`.

The Client profile needs a full game install. `VS/` here is server-only and has no `Vintagestory.dll`.

Useful startup flags. They come from [Wiki: Client startup parameters] and [Wiki: Server startup parameters] (both 1.19.8); the names were confirmed in `VintagestoryLib.dll` [strings]:

- `--addModPath <dir>`, `--addOrigin <dir>`, `--dataPath <dir>`, `--tracelog`, `--logPath`
- Client only: `-o/--openWorld <name>` and `-p/--playStyle creativebuilding` (example from [Wiki: Modding:Modding Efficiently]: `VintageStory.exe -oTestWorld -pcreativebuilding`)
- Server only: `--genconfig`, `--setconfig="{...}"`, `--withconfig="{...}"`, `--standby`, `--port`

For a multiplayer-like test, [Wiki: Modding:Server-Client Considerations § Testing a Divided Server and Client] says: run the dedicated server with its own `--dataPath`, give it a copy of the mod, and connect the client to `localhost`.

Logs are in `<dataPath>/Logs/`: `server-main.log`, `server-debug.log`, `server-build.log`, `server-audit.log`, `server-chat.log`, `client-main.log` and `client-debug.log` [strings]. The wiki still calls them `.txt` ([Wiki: Modding:Modding Efficiently], 1.19.8), which is out of date.

### 1.7 In-game debug commands (entity and worldgen work)

Prefixes: `/` is a server command and `.` is a client command. Use a creative test world.

| purpose | command | status |
|---|---|---|
| spawn entities at your position | `/entity spawn <type> [amount] [attributes JSON]`. The type parser is `new AssetLocation(str)` (`API/Common/API/ChatCommand/CommandArgumentParsers.cs` `EntityTypeArgParser`), so **mod entities need the prefix**: `/entity spawn jumpingspiders:jumpingspider-adult-female 1`. The 1.22.0 notes say spawn "can optionally also specify the generation, eg. generation=3"; the binary help text says the attributes are in JSON format. | [files] for the prefix; [strings] for the attributes syntax; the exact generation syntax is [unverified] |
| spawn at a position or radius | `/entity spawnat <type> <amount> <pos> <radius>` | [wiki: List of server commands/entity], [strings] |
| list loaded entity types | `/entity list [type=jumpingspider*]` (new in 1.22) | [strings] |
| count, find, remove | `/entity count`, `/entity countg`, `/entity find <filter>`, `/entity remove <filter>`, `/entity cmd e[] wipeall` | [strings] |
| filters | `s[]` self, `l[]` looked-at, `p[]` players, `e[]` all; for example `e[type=hare*,range=10,alive=true]` | [wiki] |
| read or set attributes | `/entity cmd l[] attr <name>`, `/entity cmd l[] setattr <datatype> <name> <value>` [Wiki: Modding:Entity instance attributes] | [strings] |
| set generation | `/entity cmd l[] setgen 3`, or `/debug setgen <n>` | [strings] for `setgen` |
| AI and animation control | `/entity cmd l[] starttask <id>`, `stoptask <id>`, `stopanim <anim>`, `kill`, `birth`, `setlact`, `rmbh <behavior>` | [strings] |
| AI debug overlay | Server: `/entity debug 1`. Client: `.clientconfig showentitydebuginfo 1`. Shows active tasks with priorities, emotion state and running animations above entities [Wiki: Modding:Entity Behavior taskai § Debugging]. **1.22.7 only allows entity debug mode in creative mode** (1.22.7 release notes). | [wiki] plus release notes |
| dump an animation state to the log | `/debug dumpanimstate <target entity>` | [strings] |
| stop all AI globally | `/worldconfig runAiTasks false` (and `runAiActivities`). Read every 250 ms by `AiRuntimeConfig`. | [files] `ESS/Entity/AI/AiTaskManager.cs`; that `/worldconfig` accepts this key is [unverified] |
| hitbox wireframes | `.debug wireframe entity` (selection boxes in blue, collision boxes in red) | [wiki: List of client commands], [strings] |
| reload assets (client) | `.reload shapes`, `.reload textures`, `.reload lang` (alias of `.dev reload <category>`); `.reload shaders` | [wiki], [strings]. Entity type JSON (`entities/`) is server-side: leave and rejoin the world instead (Ctrl+F1 reloads the world) [Wiki: Modding:Modding Efficiently] |
| missing translations | `/debug expclang` writes `collectiblelang.json` (blocks and items only) | [wiki], [strings] |
| time | `/time`, `/time set day\|night\|<hours>`, `/time stop`, `/time resume`, `/time add <h>:<m>`, `/time speed <n>`, `/time calendarspeedmul <f>`, `/time setmonth <mon>` [Wiki: List of server commands/time] | [strings] for the subcommand names |
| worldgen | `/wgen regen <radius>`, `regenr`, `pregen`, `pos <...>`, `testmap ...` [Wiki: List of server commands/wgen]; `/entity spawndebug 1`; `/debug spawnheatmap ...` (writes `spawnheatmap.png`) | [wiki], [strings] (syntax unverified) |
| error popup | `/errorreporter 1` (per world) [Wiki: Modding:Modding Efficiently] | [strings] |

The command lists on the wiki ([Wiki: List of server commands], [Wiki: List of client commands]) are marked GameVersion 1.15 and Outdated. Prefer `/help <command>` in game.

---

## 2. Anatomy of a creature JSON in 1.22.7

Source files: `VS/assets/survival/entities/animal/`:

- `bird/chicken-adult.json`, `bird/chicken-baby.json`
- `mammal/hare-adult.json`, `hare-baby.json`
- `mammal/raccoon-adult.json`, `raccoon-baby.json`
- `mammal/fox-adult.json`, `fox-baby.json`
- `mammal/wolf-adult.json`, `wolf-baby.json`

The schema is `ESS/Loading/EntityType.cs` [files]. Official JSON reference: https://apidocs.vintagestory.at/json-docs/jsondocs/Vintagestory.ServerMods.NoObf.EntityType.html (linked from [Wiki: Modding:Entity Json Properties], which is itself marked Outdated).

### 2.1 Top-level keys (`EntityType`)

- `code`, `class`, `tags`, `variantgroups`, `weight`, `habitat` (default Land)
- `hitboxSize` sets both the collision box and the selection box (`{x: width, y: height}`, in blocks). `deadHitboxSize` does the same for the dead entity; `collisionBoxSize` and `selectionBoxSize` set them separately.
- `eyeHeight`, `swimmingEyeHeight`, `fallDamageMultiplier` (0 means no fall damage)
- **`canClimb`, `canClimbAnywhere`, `climbTouchDistance`, `rotateModelOnClimb`**. These matter for a spider. The vanilla example is `lore/locust.json`: `canClimb: true, canClimbAnywhere: true, climbTouchDistance: 0.1, rotateModelOnClimb: true`.
- `knockbackResistance`, `attributes`, `behaviorConfigs` (shared config that is merged into same-named client and server behaviors), `client`, `server`, `sounds`, `idleSoundChance`, `drops`

`class` is `"EntityAgent"` for every one of the five reference animals. Wolftaming's pet dog also uses `EntityAgent`.

`tags` (1.21+) look like `["animal", "huntable", "adult", "predator", "habitat-land"]`. Wolf and fox add `"predator"`, and wolf adds `"ferocious"`. Adult files carry `"adult"`; baby files leave it out.

### 2.2 Variant groups: how male, female and baby are expressed

The entity code is `code` plus `-state` for each variant group, in the order the groups are declared. Confirmed by the lang keys `item-creature-hare-arctic-adult-male` and so on in `VS/assets/game/lang/en.json` [files].

- **Hare, raccoon, wolf, fox:** two files share the same `code`. The adult file has `age: ["adult"]` and the baby file has `age: ["baby"]`, so the codes never collide. Since 1.21.0 the vanilla pattern is one file per age [Wiki: Modding:Modding API Updates § 1.21.0 "Fox, moose, deer and goat entity jsons split into separate 'baby' and 'adult' jsons"].

  ```json5
  // hare-adult.json
  code: "hare", class: "EntityAgent",
  variantgroups: [
      { code: "type", states: ["arctic", "cape", "european", "indian", "jackblack", "scrub"]},
      { code: "age", states: ["adult"] },
      { code: "gender", states: ["male", "female"] }
  ],
  ```
- **Chicken** uses one `age` group whose states mix stage and sex (`["hen", "henpoult", "rooster", "roosterpoult"]`). The chick is a separate file with **no** variant groups (`code: "chicken-baby"`).
- Per-variant values use `xxxByType` keys with wildcard patterns matched against the code path. **The first matching key wins**, and if no key matches, the property is removed entirely (`ESS/Loading/RegistryObjectType.cs` `solveByType`). This applies recursively, including inside behaviors and AI tasks (`enabledByType`, `hoursToGrowByType` and so on). `{type}` and `{gender}` placeholders in strings are filled from the variant.

  ```json5
  { code: "multiply", enabledByType: { "*-female": true, "*": false }, ... }
  shapeByType: { "chicken-hen*": { base: "entity/animal/bird/chicken/chicken-hen" }, "*": { base: ".../chicken-rooster" } }
  ```
- `enabled: false` (or `enabledByType` that resolves to false) skips a behavior (`API/Common/Entity/EntityProperties.cs`) or an AI task (`ESS/Entity/AI/BehaviorTaskAI.cs`).
- Babies grow through the server behavior `grow`:

  ```json5
  { code: "grow", hoursToGrow: 168, adultEntityCodes: ["hare-{type}-adult-{gender}"] }          // hare-baby
  { code: "grow", hoursToGrow: 128, adultEntityCodes: ["chicken-henpoult", ... , "chicken-roosterpoult"] }  // chicken-baby, 9:1 weighting by repetition
  ```

  The client sets `sizeGrowthFactor: 0.2` (or `sizeByType` / `sizeGrowthFactorByType`) so the baby model grows. Breeding is the server behavior `multiply` (or `multiplybase` for chickens, plus the AI task `seekblockandlay`), with `spawnEntityCodes` and `requiresNearbyEntityCode`.

**Recommended for the spider** (it follows hare, raccoon and wolf):

- Use `code: "jumpingspider"` with groups `age` (adult or baby) and `gender` (male or female), in `jumpingspider-adult.json` and `jumpingspider-baby.json`.
- That gives these codes: `jumpingspiders:jumpingspider-adult-male`, `-adult-female`, `-baby-male`, `-baby-female`.
- If the baby should not have a sex, drop gender from the baby file (as `chicken-baby` does) and let `grow` pick from `adultEntityCodes`.

### 2.3 Client section

```json5
client: {
    renderer: "Shape",                                  // vanilla renderers: Item, Dummy, BlockFalling, Shape, PlayerShape, EchoChamber (ClientEntityConfig doc)
    shape: { base: "entity/animal/mammal/raccoon/{type}-{gender}" },   // path under shapes/, no ".json"
    texture: { base: "entity/animal/mammal/raccoon/{type}-{gender}1" }, // single texture; sets key "all"
    // or keyed textures that match the shape's texture codes:
    // textures: { "skin": { base: "entity/animal/mammal/hare/{type}-{gender}*" } },  // "*" = random among matching files
    // alternates: textureByType ... { base: ".../hen1", alternates: [ { base: ".../hen2" }, ... ] }
    sizeGrowthFactor: 0.2, size: 0.9,
    behaviors: [
        { code: "repulseagents" },
        { code: "controlledphysics", stepHeight: 1.1251 },
        { code: "floatupwhenstuck", onlyWhenDead: true },
        { code: "interpolateposition" },
        { code: "harvestable" },
        { code: "despawn", minPlayerDistance: 8, belowLightLevel: 8, minSeconds: 300 },
        { code: "ropetieable", minGeneration: 2 }
    ],
    animations: [ ... ]
}
```

Textures:

- A singular `texture` is stored as `Textures["all"]` (`ESS/Loading/EntityType.cs` `OnDeserializedMethod`). Vanilla relies on "all" overriding whatever codes the shape uses: the raccoon shape's texture code is `raccoon` and the wolf's is `wolf5`, yet both entity files set only `texture`. This is inferred from the files [files].
- Keyed `textures` must match the shape's texture codes (hare: `skin`).

Animations are declared as `AnimationMetaData` (`API/Common/Model/Animation/AnimationMetaData.cs`):

```json5
{ code: "hurt", animation: "hurt", animationSpeed: 2.2, weight: 10, blendMode: "AddAverage" },
{ code: "die",  animation: "death", animationSpeed: 1.25, weight: 10, blendMode: "Average", triggeredBy: { onControls: ["dead"] } },
{ code: "idle", animation: "idle", blendMode: "AddAverage", easeOutSpeed: 4, triggeredBy: { defaultAnim: true } },
{ code: "sleep", animation: "sleep", easeInSpeed: 4, easeOutSpeed: 4, blendMode: "Average" },
{ code: "walk", animation: "walk", animationSounds: [ { frame: 2, range: 10, location: "creature/wolf/footsteps/dirt/footstep-wolf-dirt*", pitch: {avg: 1.0, var: 0.15} } ] }  // wolf; 1.22: animationSound -> animationSounds[]
```

Fields:

- `code` is the meta code you reference.
- `animation` is the shape's animation code; it is lowercased on load.
- Also available: `animationSpeed`, `weight`, `elementWeight`, `blendMode` (`Add` default, `Average`, `AddAverage`), `elementBlendMode`, `easeInSpeed` and `easeOutSpeed` (both default 10), `mulWithWalkSpeed`, `supressDefaultAnimation`, `clientSide`, `animationSounds[]`, `adjustCollisionBox`, `attributes`.
- `triggeredBy` takes `onControls` (EnumEntityActivity: `idle`, `move`, `sprintmode`, `sneakmode`, `fly`, `swim`, `jump`, `fall`, `climb`, `floorsitting`, `dead`, `break`, `place`, `glide`, `mounted`), plus `matchExact` and `defaultAnim`.

How animations get started [files]:

1. **By the engine.** It calls `AnimManager.StartAnimation("die")` on death (`API/Common/Entity/Entity.cs`) and `StartAnimation("hurt")` on damage greater than 0.05 (`Entity.cs`; `BehaviorHealth.HurtAnimationCode` also defaults to `"hurt"`). These are **meta codes**: they only play if `client.animations` declares a meta with that code (`AnimationManager.StartAnimation(string)` looks up `AnimationsByMetaCode` and silently does nothing otherwise).
2. **By `triggeredBy`.** `defaultAnim: true` plays when nothing else is playing (idle). `onControls` plays while that activity is set, for example `dead`.
3. **By AI tasks.** Each task's `"animation": "Run"` is lowercased. The task first looks for a meta with exactly that code, which is a **case-sensitive** compare. If none exists, it plays the shape animation with that code directly, using speed 1 and ease 1 (`ESS/Entity/AI/AiTaskBase.cs`, lines 231-258). So AI tasks can name shape animations that have no meta entry.
4. **By C#.** Code calls `entity.AnimManager.StartAnimation("code")` (it needs a declared meta) or `StartAnimation(new AnimationMetaData{...})`.

### 2.4 Server section, the behaviors list and the task AI block

Typical server behaviors, in vanilla order (hare):

```
repulseagents, controlledphysics {stepHeight}, despawn, health {currenthealth, maxhealth},
deaddecay {hoursToDecay, decayedBlock: "carcass-tiny"}, floatupwhenstuck, harvestable {drops: [...]},
breathe, multiply / grow, emotionstates {states: [...]}, taskai {...}, ropetieable, pettable, entitystatetags
```

About `health` on the client:

- The 1.22 notes say "For the red tinting hurt overlay of creatures to work you now need to add 'Health' behavior to the list of client side behavior as well" [Wiki: Modding:Modding API Updates § 1.22].
- In 1.22.7, `EntityType.CreateProperties` **auto-appends** the server `health` config to the client behaviors when it is missing, and the deprecation warning is commented out (`ESS/Loading/EntityType.cs` lines 255-263).
- No vanilla prey or predator file declares client `health` (only `elk-tamed` and `elk-semitamed` do).
- Recommendation: declare it on both sides anyway, because that is what the notes ask for.

The task AI block (from wolf-adult, which already hunts our prey):

```json5
{ code: "taskai", aiCreatureType: "LandCreature",     // Default | LandCreature | ... (EnumAICreatureType)
  aitasks: [
    { code: "meleeattack", entityCodes: ["chicken-*", "hare-*", "pig-eurasian-baby-*", "raccoon-*"],
      priority: 2, damage: 8, damageTier: 2, damageType: "SlashingAttack", slot: 1,
      mincooldown: 1500, maxcooldown: 1500, attackDurationMs: 800, damagePlayerAtMs: 500,
      retaliateAttacks: false, animation: "Attack-Low", animationSpeed: 1.5, sound: "creature/wolf/attack*" },
    { code: "seekentity", entityCodes: ["player", "chicken-*", ..., "hare-*", "deer-*", "raccoon-*"],
      priority: 1.5, movespeed: 0.045, seekingRange: 15, animation: "Canter",
      leapAtTarget: true, leapAnimation: null, leapChance: 0.01, whenNotInEmotionState: "saturated" },
    { code: "seekfoodandeat", priority: 1.3, eatItemCategories: ["protein"], animation: "Eat", mincooldownHours: 1, maxcooldownHours: 4 },
    { code: "idle", animation: "Sleep", priority: 1.35, priorityForCancel: 1.55, duringDayTimeFrames: [ { fromHour: 10, toHour: 18 } ], stopOnNearbyEntityCodes: ["player"], stopRange: 10, stopOnHurt: true },
    { code: "wander", animation: "Walk", priority: 1.0, priorityForCancel: 1.35, movespeed: 0.006, preferredLightLevel: 20 },
    { code: "lookaround", priority: 0.5 }
  ] }
```

Scheduling [Wiki: Modding:Entity Behavior taskai]:

- Slots run from 0 to 7, and only one task per slot runs at a time.
- The highest `priority` that can run wins.
- A running task is interrupted only by a task whose priority beats its `priorityForCancel`.

Matching and task options:

- **`entityCodes` matching** (`ESS/Entity/AI/AiTaskBaseTargetable.cs` `InitializeTargetCodes` / `IsTargetEntity`) compares against `entity.Code.Path` only, so the domain is ignored. Only a **trailing** `*` (prefix match) or an exact string works. `"*"` alone matches everything. `skipEntityCodes` uses full wildcard matching with the domain.
- Targetable tasks also accept `entityTags`, `minTargetWeight` and `maxTargetWeight` (relative to own weight), `minTargetInjury` and `maxTargetInjury`, and `TargetOnlyInteractableEntities` (new in 1.22).
- **`seekentity` already has a leap.** It reads `leapAtTarget`, `leapChance` (default 1), `leapHeightMul`, and `leapAnimation` (default **`"jump"`**; set it to `null` for no animation) (`ESS/Entity/AI/Tasks/AiTaskSeekEntity.cs` lines 77-88). See [Wiki: Modding:AI Task seekentity].
- `meleeattack` keys: `damage`, `knockbackStrength`, `attackAngleRangeDeg`, `attackDurationMs`, `damagePlayerAtMs`, `minDist`, `minVerDist`, `damageType`, `damageTier`.
- Renamed in 1.22: `chance`, `wanderChance` and `searchFrequency` became **`executionChance`** [Wiki: Modding:Modding API Updates § 1.22] (`AiTaskWander.wanderChance` is now only an alias property in code).
- Gating keys common to all tasks: `whenInEmotionState`, `whenNotInEmotionState`, `duringDayTimeFrames` (all tasks since 1.21), `minGeneration`, `maxGeneration`, and `mincooldown`/`maxcooldown` (or the `...Hours` variants).
- Registered task codes (`ESS/Entity/AI/AiTaskManager.cs` static constructor):
  - `wander`, `lookaround`, `meleeattack`, `seekentity`, `fleeentity`, `stayclosetoentity`, `getoutofwater`, `idle`, `seekfoodandeat`, `seekblockandlay`, `useinventory`, `meleeattacktargetingentity`, `seektargetingentity`, `stayclosetoguardedentity`, `jealousmeleeattack`, `jealousseekentity`, `lookatentity`, `gotoentity`
  - plus "refactored" variants with a `-r` suffix (`wander-r`, `seekentity-r`, `meleeattack-r`, `fleeentity-r`, `shootatentity-r`, `stayinrange-r`, ...). **No vanilla 1.22.7 entity uses the `-r` tasks** (searched all assets).
- An unknown task code is logged with `Task with code X for entity Y does not exist, will skip it.` (`ESS/Entity/AI/BehaviorTaskAI.cs`). Task instances are created through `Activator.CreateInstance(type, entityAgent, taskConfig, aiConfig)`, so a custom task needs that constructor.
- An unknown behavior code is logged as a Notification: `Entity behavior {0} for entity {1} not found, will not load it.` (`API/Common/Entity/EntityProperties.cs`).

`emotionstates` (wolf):

```json5
{ code: "aggressiveondamage", duration: 30, chance: 1, slot: 2, priority: 2, accumType: "noaccum" },
{ code: "fleeondamage", duration: 60, chance: 0.75, slot: 2, priority: 5, accumType: "max", whenHealthRelBelow: 0.3 },
{ code: "saturated", duration: 1200, chance: 1, slot: 0, priority: 1, accumType: "sum" }
```

`saturated` is what makes the wolf stop hunting after eating (`whenNotInEmotionState: "saturated"`).

Spawning (raccoon). There is a world-generation block, a runtime block and a climate block. Hare and wolf use `spawnconditionsByType` so that only the adult male spawns, bringing `companions` with it.

```json5
spawnconditions: {
    worldgen: { TriesPerChunk: { avg: 0.06, var: 0 }, tryOnlySurface: true, groupSize: { dist: "verynarrowgaussian", avg: 2, var: 3 },
                insideBlockCodes: ["air", "tallgrass-*", "snowlayer-1"], companions: ["raccoon-{type}-adult-female", "raccoon-{type}-baby-male", "raccoon-{type}-baby-female"] },
    runtime:  { group: "neutral", chance: 0.0009, maxQuantity: 4, maxLightLevel: 7, ... },
    climate:  { minTemp: -4, maxTemp: 24, minRain: 0.25, minForest: 0.25, maxForest: 1.0 }
}
```

Without `spawnconditions` an entity never spawns naturally (`ServerEntityConfig` doc comment).

### 2.5 Attributes, hitboxes, drops, sounds

- `attributes` (type level) examples:
  - `creatureDiet { foodCategories, weightedFoodTags }`
  - `trappable { small/large: { trapChance, trapDestroyChance } }`
  - `handbook { groupcode, trappableGroupCode }`
  - `killedByInfoText`, `newWorldSpawnDelayHours`, `harshWinterSpawnRate`, `eggTypes`
  - `minGenerationToAllowHealing`, `tamed` (see 2.7), `frostOverlayThreshold` (1.22)
- `server.attributes.pathfinder { minTurnAnglePerSec, maxTurnAnglePerSec }` (chicken).
- `hitboxSize: { x: 0.75, y: 0.5 }`, `deadHitboxSize: { x: 0.75, y: 0.4 }` (raccoon); chicken baby is `{ x: 0.3, y: 0.2 }`. The defaults are 0.5x0.5 and 0.5x0.25 (`EntityType`).
- Drops: top-level `drops: []` is empty on all five animals. Loot comes from the server `harvestable` behavior:

  ```json5
  { code: "harvestable", drops: [ { type: "item", code: "bushmeat-raw", quantity: { avg: 2, var: 1 } }, { type: "item", code: "hide-raw-raccoon-common", quantity: { avg: 1, var: 0 } } ] }
  ```

  When dead, the body decays via `deaddecay { hoursToDecay: 96, decayedBlock: "carcass-small" }`.
- Sounds:

  ```json5
  sounds: { hurt: "creature/fox/hurt", death: "creature/fox/die", idle: { path: "creature/fox/yip", pitch: { avg: 1, var: 0.25 } } }, idleSoundChance: 0.03
  ```

  The keys are `hurt`, `death` and `idle` (`swim` and `eat` are player only). AI tasks have their own `sound`, `soundRange` and `finishSound`.

### 2.6 Exact prey codes in 1.22.7 (domain `game`)

Derived from the variant groups in the files above and cross-checked against `VS/assets/game/lang/en.json` (`item-creature-*`) [files].

**Hare** (24 codes). The pattern is `hare-{type}-{age}-{gender}`, with type in arctic, cape, european, indian, jackblack, scrub:

```
hare-arctic-adult-male     hare-arctic-adult-female     hare-arctic-baby-male     hare-arctic-baby-female
hare-cape-adult-male       hare-cape-adult-female       hare-cape-baby-male       hare-cape-baby-female
hare-european-adult-male   hare-european-adult-female   hare-european-baby-male   hare-european-baby-female
hare-indian-adult-male     hare-indian-adult-female     hare-indian-baby-male     hare-indian-baby-female
hare-jackblack-adult-male  hare-jackblack-adult-female  hare-jackblack-baby-male  hare-jackblack-baby-female
hare-scrub-adult-male      hare-scrub-adult-female      hare-scrub-baby-male      hare-scrub-baby-female
```

Note: `hare-adult.json` has `spawnconditionsByType` only for `*-arctic-adult-male`, `*-cape-adult-male` and `*-european-adult-male`. **Indian, jackblack and scrub hares exist but never spawn naturally in 1.22.7.** You get them only from creature items or commands.

**Raccoon** (4 codes):

```
raccoon-common-adult-male  raccoon-common-adult-female  raccoon-common-baby-male  raccoon-common-baby-female
```

**Chicken** (5 codes):

```
chicken-hen  chicken-henpoult  chicken-rooster  chicken-roosterpoult  chicken-baby
```

Prey-list advice:

- Use `["hare-*", "raccoon-*", "chicken-*"]`, which is exactly what vanilla wolf does.
- Vanilla fox lists `["chicken-rooster", "chicken-hen", "chicken-baby", "hare-*"]`, which leaves out the poults. Do not copy that.
- Prefix matching ignores the domain, so `hare-*` would also catch modded entities whose code starts with `hare-`.
- Historic codes were renamed (for example `hare-male-arctic` became `hare-arctic-adult-male`). See `VS/assets/game/config/remapentities.json`. Expect renames again.

### 2.7 How generation and domestication are exposed to JSON

- Generation is the int `WatchedAttributes["generation"]`, defaulting to 0. Offspring get the parent's generation + 1 (`ESS/Entity/Behavior/BehaviorMultiply.cs`, `AiTaskSeekBlockAndLay.cs`). `grow` copies the generation onto the adult (`BehaviorGrow.cs`). Entities with generation > 0 show "Generation: N" in their info text (`Entity.GetInfoText`).
- JSON knobs that read it [files]:
  - `minGeneration` and `maxGeneration` on any AI task (`AiTaskBase.cs` lines 324-325) and on behaviors such as `ropetieable` (minGeneration 2) and `pettable` (minGeneration 1)
  - `followleadholder` (minGeneration 2)
  - `tamingGenerations` (default 10) on `idle`, `fleeentity` and targetable tasks; fear and flee range scale by `(tamingGenerations - generation) / tamingGenerations`
  - emotion state `maxGeneration` (`BehaviorEmotionStates.cs`)
  - type attribute `minGenerationToAllowHealing` (`BehaviorHealth.cs`)
  - type attribute `tamed: true`, which adds 10 to the effective generation in targetable tasks (`AiTaskBaseTargetable.GetOwnGeneration`)
- The cleaver kills creatures of generation 3 or more instantly (lang `item-handbooktext-cleaver-*`).
- An entity tag `domesticated` for generation 10 and above exists only as commented-out code in `API/Common/Entity/Entity.cs`.
- `Entity.SetAttributesFrom(JToken)` currently only applies `generation` ("in 1.21.1 for vanilla, that's only generation"). This is presumably what `/entity spawn ... {generation:..}` uses [unverified].
- Commands: `/entity cmd l[] setgen N`, `/debug setgen N`.

---

## 3. What the asset author must deliver

### 3.1 Files and paths (all under `JumpingSpiders/assets/jumpingspiders/`)

| deliverable | path | referenced from |
|---|---|---|
| adult male shape | `shapes/entity/animal/arthropod/jumpingspider/male.json` | `client.shape.base` or `shapeByType`: `"entity/animal/arthropod/jumpingspider/{gender}"` |
| adult female shape | `.../female.json` | same |
| baby shape | `.../baby.json` | baby file `client.shape` |
| textures (PNG) | `textures/entity/animal/arthropod/jumpingspider/male1.png`, `female1.png`, `baby1.png` (more for alternates) | `client.textures` (key = the shape's texture code) or `client.texture` |
| hammock block shape | `shapes/block/silkhammock.json` (optional `silkhammock-inventory.json`, compare `shapeinventoryByType` in vanilla `blocktypes/wood/bed.json`) | `blocktypes/silkhammock.json` `shape` |
| hammock texture(s) | `textures/block/silkhammock/*.png` | block `textures` |
| spawner item | none extra: vanilla `itemtypes/creature/creature.json` (`class: "ItemCreature"`) reuses the **entity shapes and textures** for the item. Our `itemtypes/creature.json` uses `variantgroups: [{code: "type", states: ["jumpingspider-adult-male", ...]}]`. `ItemCreature` spawns `<item domain>:<code minus first part>` (`SURV/Item/ItemCreature.cs` line 141), so `jumpingspiders:creature-jumpingspider-adult-male` spawns `jumpingspiders:jumpingspider-adult-male`. | |
| sounds (optional) | `sounds/creature/jumpingspider/*.ogg` | `sounds`, AI task `sound`, `animationSounds` |

Lang entries in `lang/en.json` (no domain prefix inside our own file; wildcards are allowed in keys, as vanilla `"block-bed-wood-head-*"` shows):

```json
{
  "item-creature-jumpingspider-adult-male": "Jumping spider (male)",
  "item-creature-jumpingspider-adult-female": "Jumping spider (female)",
  "item-creature-jumpingspider-baby-*": "Jumping spiderling",
  "block-silkhammock-*": "Silk hammock"
}
```

Entity display names come from `Lang.GetMatching(domain + ":item-creature-" + code.Path)` (`API/Common/Entity/Entity.cs` line 2444), so the same key names both the creature item and the entity. Optional extras: `creaturegroup-...` (used by `attributes.handbook.groupcode`) and `item-handbooktext-...`.

### 3.2 Animation names the code and JSON will reference

Agree the list with the code team. Shape animation **codes** must be lowercase without spaces. If the `code` is missing, the engine derives it from `name` (`ToLowerInvariant().Replace(" ", "")`, `API/Common/Model/Shape/Shape.cs` line 121). VSMC has a separate Code field [Wiki: Modding:Animation].

| shape anim code | meta code in `client.animations` | why |
|---|---|---|
| `idle` | `idle` with `triggeredBy: { defaultAnim: true }` | default pose |
| `death` | `die` with `triggeredBy: { onControls: ["dead"] }` | the engine starts meta `die` on death |
| `hurt` | `hurt` | the engine and `BehaviorHealth` start meta `hurt` |
| `walk`, `run` | optional metas | AI `wander`, `seekentity`, `fleeentity` `animation:` |
| `jump` (or `leap`) | optional | `seekentity` `leapAnimation` (defaults to `"jump"`) |
| `attack` | optional | `meleeattack` `animation:` |
| `eat`, `sleep`, `sit` (and any idle variants) | optional | `seekfoodandeat` `eatAnimation`, `idle` tasks |
| `swim` | optional | `getoutofwater` |

Semantics of the shape's `onActivityStopped` and `onAnimationEnd`, as vanilla uses them (hare shape):

- Looping locomotion uses `Stop` + `Repeat`.
- One-shots use `PlayTillEnd` + `Stop`.
- Death uses `PlayTillEnd` + `Hold`.
- Idle, sit and sleep use `EaseOut` + `Repeat`.

The enums are `PlayTillEnd`, `Rewind`, `Stop`, `EaseOut` and `Repeat`, `Hold`, `Stop`, `EaseOut` (`API/Common/Model/Animation/Animation.cs`). Animations run at 30 fps with linear keyframes [Wiki: Modding:VSMC2].

### 3.3 Constraints the shape format puts on animations [files]

- **Element names must be unique within a shape.** Elements are collected into a name-keyed dictionary that silently overwrites duplicates (`Shape.CollectElements`), and keyframes address elements by name. A keyframe naming a missing element logs `Shape {0} has a key frame element for which the referencing shape element {1} cannot be found.`
- **Every animation needs at least one keyframe.** `Animation.GenerateAllFrames` throws `Animation 'x' has no keyframes, this will cause other errors every time it is ticked` (`API/Common/Model/Animation/Animation.cs` line 112).
- **Animated joint cap.** It throws if the number of joints reaches `GlobalConstants.MaxAnimatedElements` (default `46*5`, but it can come from the client setting `maxAnimatedElements`; the error text suggests that 46 "works for most GPUs"). Keep animated elements well under 46 to be safe. A spider with 8 legs of 2-3 segments each fits.
- An animation code requested but absent from the shape logs once at Debug level and is skipped (`AnimatorBase`: `attempted to play an animation code which its shape does not have`). This does not crash.
- Attachment point codes must be unique (a warning since 1.22).
- Shape texture codes in VSMC2 must be lowercase letters, 3 or more characters [Wiki: Modding:VSMC2] (a tool constraint). `textureWidth` and `textureHeight` define UV space, not pixel size.
- There are two editors: the classic VSMC ([Wiki: Modding:VS Model Creator], MarkForRewrite) and **VSMC2** (https://github.com/anegostudios/vsmc2, page at 0.3.0). VSMC2 loads VSMC1 shapes and previews animations exactly as the game plays them. It does not expose `RotShortestDistance*` yet.

### 3.4 Minimal valid placeholder shapes

Entity placeholder: one element, a texture code `skin`, and one keyframe per animation. Field names come from `ShapeElement`, `Animation` and `AnimationKeyFrameElement` in `API/Common/Model/...`. This mirrors vanilla structure (compare `VS/assets/survival/shapes/entity/animal/mammal/hare/baby.json`) but has **not been loaded in a running game** [unverified at runtime].

```json
{
  "textureWidth": 16, "textureHeight": 16,
  "textures": { "skin": "jumpingspiders:entity/animal/arthropod/jumpingspider/placeholder" },
  "elements": [
    { "name": "body", "from": [6, 0, 5], "to": [10, 3, 11], "rotationOrigin": [8, 0, 8],
      "faces": {
        "north": { "texture": "#skin", "uv": [0, 0, 4, 3] }, "east":  { "texture": "#skin", "uv": [0, 0, 6, 3] },
        "south": { "texture": "#skin", "uv": [0, 0, 4, 3] }, "west":  { "texture": "#skin", "uv": [0, 0, 6, 3] },
        "up":    { "texture": "#skin", "uv": [0, 0, 4, 6] }, "down":  { "texture": "#skin", "uv": [0, 0, 4, 6] } } }
  ],
  "animations": [
    { "name": "Idle",  "code": "idle",  "quantityframes": 2,  "onActivityStopped": "EaseOut",     "onAnimationEnd": "Repeat",
      "keyframes": [ { "frame": 0, "elements": { "body": { "offsetY": 0.0 } } } ] },
    { "name": "Walk",  "code": "walk",  "quantityframes": 2,  "onActivityStopped": "Stop",        "onAnimationEnd": "Repeat",
      "keyframes": [ { "frame": 0, "elements": { "body": { "offsetY": 0.0 } } } ] },
    { "name": "Death", "code": "death", "quantityframes": 10, "onActivityStopped": "PlayTillEnd", "onAnimationEnd": "Hold",
      "keyframes": [ { "frame": 0, "elements": { "body": { "rotationZ": 0.0 } } }, { "frame": 9, "elements": { "body": { "rotationZ": 180.0 } } } ] },
    { "name": "Hurt",  "code": "hurt",  "quantityframes": 5,  "onActivityStopped": "PlayTillEnd", "onAnimationEnd": "Stop",
      "keyframes": [ { "frame": 0, "elements": { "body": { "offsetY": 0.0 } } } ] }
  ]
}
```

Add `run`, `jump`, `attack` and so on in the same way. Shape units are 1/16 block, with the model centred near (8, 0, 8).

Block placeholder: the smallest vanilla block shape is `VS/assets/game/shapes/block/basic/cube.json`. It has one element, `textures: {"all": "unknown"}`, and faces with `#north`, `#east` and so on. The engine requires that file to exist ("Shape shapes/block/basic/cube.json not found, it is required to run the game" [strings]). Until the real hammock shape exists, the hammock block JSON can use `shape: { base: "game:block/basic/cube" }` with `textures: { all: { base: "jumpingspiders:block/silkhammock/placeholder" } }`.

Texture placeholder: any PNG. Or reference an existing vanilla texture with the `game:` prefix, for example `game:block/creature/spiderwebs/normal1`, which `VS/assets/survival/blocktypes/spiderweb.json` uses. That texture is not present in this server install, but it is referenced by vanilla JSON.

### 3.5 Does a missing shape or texture break loading?

These are the messages in 1.22.7 [strings, plus the files noted]:

- Missing or invalid entity shape: `Entity shape for entity {0} not found or errored, was supposed to be at shapes/{1}.json. Entity will be invisible!` This is a logged error; the entity still exists.
- Missing texture: `Texture asset '{0}' not found (defined in {1}).`; a bitmap of 0x0 gives `... Will use unknown texture.`
- Missing block shape: `Shape for block ... not found` style messages; tesselation exceptions say "Block will be invisible".
- Missing shape overlay: `Entity {0} defines a shape overlay {1}, but no such file found. Will ignore.` (`API/Common/Entity/Entity.cs`)
- A bad shape file is caught and logged: `Exception thrown when trying to load shape file {0}` (`Shape.TryGet`).
- The 1.22 notes add "Make the game engine more tolerant towards missing files" [Wiki: Modding:Modding API Updates § 1.22].
- **However**, [Wiki: Modding:Content Tutorial Entity Creation Part 1] (1.21.0) says the game *crashes* when the client rendering properties (`renderer`, `shape`) are not defined at all. So always declare `renderer: "Shape"` and a `shape`, even if it points at a placeholder.
- An animation with zero keyframes throws (see 3.3), so placeholder animations must have a keyframe.

---

## 4. Further reading on the wiki (for the mod author)

The wiki has **no page on writing a custom entity behavior or a custom AI task in C#**. A search of all 194 pages in the Modding namespace found no `RegisterEntityBehaviorClass`, `RegisterAiTask` or `AiTaskRegistry`. For those, read the source: `ESS/Entity/Behavior/*.cs`, `ESS/Entity/AI/Tasks/*.cs`, `ESS/Entity/AI/AiTaskManager.cs` (registration: `AiTaskRegistry.Register<T>("code")` or the extension `sapi.RegisterAiTask<T>("code")`), and PetAI (section 6) as a working third-party example.

| topic | page | status |
|---|---|---|
| entity behaviors (JSON list) | Modding:Entity Behaviors | **Outdated** banner |
| task AI explained, plus per-task pages | Modding:Entity Behavior taskai; Modding:AI Task seekentity; Modding:AI Task meleeattack; Modding:AiTaskBaseTargetable | no version tag; already uses `executionChance` (looks current) |
| other behavior pages | Modding:Entity Behavior harvestable; Modding:Entity Behavior controlledphysics | no version tag |
| entity JSON | Modding:Entity Json Properties (**Outdated**, points to apidocs JSON reference); Modding:Asset Type - Entities (1.19.8) | |
| entity tutorial (JSON) | Modding:Content Tutorial Entity Creation Part 1 and Part 2 (1.21.0): registering, rendering, animations, AI, spawning | most current entity tutorial |
| entity attributes | Modding:Entity instance attributes (WatchedAttributes, debug commands) | no version tag |
| block classes | Modding:Block Classes | **Outdated** |
| block entity tutorial | Modding:Block Entity | 1.19.8, MarkForRewrite |
| block entity classes and behaviors | Modding:Block Entity Classes; Modding:Block Entity Behaviors | both **Outdated** |
| block behaviors | Modding:Adding Block Behavior (1.19.8, MarkForRewrite); Modding:Json Block Behaviors (**Outdated**); Modding:Block Json Properties (**Outdated**, points to apidocs) | |
| simple code block | Modding:Code Tutorial Simple Block (1.19.8); Modding:Code Tutorial Simple Item (1.20.3) | |
| world access | Modding:World Access | 1.19.8, MarkForRewrite |
| server/client split and sync | Modding:Server-Client Considerations (1.19.8); Modding:Synchronization (no tag); Modding:Network API (1.19.8) | |
| persistence | Modding:TreeAttribute, Modding:Serialization Formats, Modding:SaveGame ModData, Modding:Chunk Moddata | all 1.19.8 |
| mod config | Modding:ModConfig (1.20.3): `api.LoadModConfig<T>("x.json")` / `api.StoreModConfig(cfg, "x.json")`, wrapped in try/catch; files land in `VintagestoryData/ModConfig/`; server-to-client via WorldConfig | API names verified in `API/Common/API/ICoreAPI.cs` |
| JSON patching | Modding:JSON Patching; Modding:JSON Patch Reference (includes `dependsOn: [{modid, invert}]`, verified in `ESS/Loading/JsonPatchLoader.cs`) | |
| optional mod integration | Modding:Optional Mod Compatibility (`api.ModLoader.IsModEnabled("id")` plus isolated class; Harmony via `AccessTools.TypeByName`) | no tag |
| Harmony | Modding:Monkey patching (1.19.8) | |
| load order | Modding:Load Order (`ModSystem.ExecuteOrder()`) | GameVersion "?.??" |
| tags | Modding:Using Tags (Content), Modding:Using Tags (Code) | no tag; tags still evolving |
| model and animation | Modding:VSMC2 (VSMC2 0.3.0); Modding:VS Model Creator and Modding:Animation (both MarkForRewrite) | |
| productivity | Modding:Modding Efficiently (1.19.8): reload commands, hot reload works with the template as a compiled mod (Rider on Linux too), EntityPartitioning for entity searches, "do not hold static references" | |
| debugging | Modding:Debugging Content (1.19.8) | |

The pages under "Category:Modding:Old Code Tutorials" (Block Entity, Network API, World Access, Commands, Monkey patching, and so on) are explicitly the older tutorials.

---

## 5. Version compatibility and porting

What is published:

- [Wiki: Modding:Modding API Updates] (tagged 1.22.-pre.1, with sections up to 1.22) lists every API change from 1.19 onward. The recommended workflow: find the version your mod targeted, then read every section above it.
- [Wiki: Modding:Mod-Engine Compatibility] (1.19.8) describes the release cycle: pre (about 90% of API changes done), rc (API complete), stable (API changes only in extraordinary cases). JSON-heavy mods are "likely safe across several major updates"; code breakage is "minimal and fixable within an hour or two".

1.21 to 1.22 changes that touch creatures [Wiki: Modding:Modding API Updates § 1.22; 1.22.0 release notes r441]:

- .NET 8 to .NET 10.
- `entity.ServerPos` and `SidedPos` now redirect to `entity.Pos`.
- The `health` behavior should also be on the client.
- `animationSound: {}` became `animationSounds: [ ]`.
- AI task `chance`, `wanderChance` and `searchFrequency` became `executionChance`.
- `GetBehavior()` no longer throws NREs.
- `EntityBehaviorMultiply` accepts `requiresNearbyEntityCodes[]`.
- Targetable tasks gained `TargetOnlyInteractableEntities`, `minTargetInjury` and `maxTargetInjury`.
- Worldgen code moved from survival to essentials.
- The tag system was reworked (tag sets; mods can register tag types).
- 1.21.0 had already "heavily reorganized the folder structure for entities and their models and textures", introduced `-r` refactored AI tasks, and announced "transitioning all Entity AI to a tag based system instead of using entity codes ... Likely to be completed for 1.22".
- In 1.22.7 that transition is **not** done: vanilla still uses `entityCodes` and only the non-`-r` tasks [files]. Expect it in a later version.

1.23: the only official information is the June 2026 development update (vintagestory.at news r446). It says 1.23 will focus on **combat**: "tighter, more visceral hit detection; more varied attack animations; possibly rework of armor and weapon tiers; a few more types of weapons". It gives no ETA and no modding API notes. Nothing on the wiki mentions 1.23 (version history shows 1.22.7, 2026-08-16, as the latest stable). This is a risk for any custom melee or leap code.

Practices that keep porting cheap (derived from the above):

- Keep behaviour in JSON wherever vanilla behaviors and tasks suffice (for example `seekentity` with `leapAtTarget` plus `meleeattack`).
- Keep the prey list in **one** place: a shared `behaviorConfigs` entry or a single JSON attribute read by code. Then the planned switch to entity tags is a one-line change.
- Do not depend on vanilla shape or texture paths. They moved in 1.21, and remaps exist for entity codes.
- Subclass vanilla classes through public or protected members only, and do not Harmony-patch vanilla unless necessary.
- Fully qualify every asset code (`game:` or `jumpingspiders:`).
- Set `"game"` in modinfo to the version actually tested.
- Re-read the API Updates page when moving to a new minor version; renamed JSON keys are logged or simply ignored.

---

## 6. PetAI (https://mods.vintagestory.at/petai)

What it is:

- Mod id **`petai`**, author Gerste (G3rste). Version 5.1.2 supports 1.22.0-1.22.7.
- Source: https://github.com/G3rste/petai. It is built with Cake, like our template, and its modinfo says `"game": "1.22.0"`.
- It is a library: on its own it adds nothing to the world. Wolf Taming, Cats, Fox, Rabbit and Chicken Taming and others build on it.
- Config file: `ModConfig/petconfig.json`.

How other mods integrate (from the README, `PetAI/src/Systems/PetAI.cs`, and WolfTaming's `dog-adult.json`):

- Add dependency `"petai": "5.0.0"` (WolfTaming's modinfo) and keep the entity as **`class: "EntityAgent"`**.
- Add entity behaviors in JSON:
  - `tameable` with `size` (small, medium or large), `treat[] { code, domain, progress, cooldown }`, `disobediencePerDay`, and optional `tameEntityCode`, which converts the entity to another code on taming and copies `grow` and `textureIndex`
  - `receivecommand { availablecommands: [...] }`
  - also `petinventory`, `givecommand` and `pettableextended`
- Add AI tasks to `taskai`: `petmeleeattack`, `petseekentity` (subclasses of vanilla `AiTaskMeleeAttack` and `AiTaskSeekEntity`), `followmaster` (subclass of `AiTaskStayCloseToEntity`), `stay`, `simplecommand`, `seeknest`, `happydance` and `avoidfire`. They are registered with `AiTaskRegistry.Register<T>(code)`.
- PetAI stores state in `WatchedAttributes["domesticationstatus"]` (`owner`, `domesticationLevel`: WILD, TAMING or DOMESTICATED) and reads vanilla `generation`.
- PetAI Harmony-patches these vanilla methods: `EntityBehaviorMultiply.TryGetPregnant`, `EntityBehaviorMortallyWoundable.OnEntityReceiveDamage`, `EntityBehaviorNameTag.GetName`, `EntityBehaviorGrow.BecomeAdult`, and `AiTaskStayCloseToEntity.OnNoPath`.
- Its README says the old `raisable` behavior is obsolete: "use vanilla grow instead".

To keep a later integration easy, avoid these now:

- Do not give the spider a custom `Entity` subclass unless it is unavoidable. If you do, derive from `EntityAgent`.
- Do not replace vanilla `grow`, `multiply` or `generation` semantics, and do not invent our own owner or domestication attributes. Never use the key `domesticationstatus`.
- If we write custom hunting tasks, derive them from vanilla `AiTaskSeekEntity` and `AiTaskMeleeAttack` (the same bases as PetAI's tasks; they are not the `-r` tasks). Keep them configurable through `taskai` JSON, so PetAI tasks can later be added beside them through an optional JSON patch with `dependsOn: [{ modid: "petai" }]`, without a hard dependency.
- Do not Harmony-patch the five methods PetAI patches.
- Do not compile against PetAI. If code integration is ever needed, use `api.ModLoader.IsModEnabled("petai")` with an isolated class [Wiki: Modding:Optional Mod Compatibility].

---

## 7. Testing

Official material:

- Nothing official covers **unit testing** mods. A scan of all 194 Modding pages found no "unit test", xunit, nunit or mstest; the only "headless" hit is about Java for VSMC.
- The vanilla source shows Anego has internal test assemblies that are not published: `InternalsVisibleTo("VSSurvivalModTests")` in `SURV/` and `ESS/Properties/AssemblyInfo.cs`, and `InternalsVisibleTo("VSTests")` in `API/Localization/TranslationService.cs`.
- The only official testing guidance is manual:
  - [Wiki: Modding:Server-Client Considerations § Testing a Divided Server and Client]: run `VintagestoryServer` with its own `--dataPath`, put the mod in that data path's `Mods` (or use `--addModPath`), and join via `localhost`.
  - The debugging advice in [Wiki: Modding:Debugging Content] and [Wiki: Modding:Modding Efficiently]: read the logs, use `--tracelog`, `/errorreporter 1`, and developer mode with "Extended Debug Info".

A headless smoke check is possible with this install (my own recipe, not official, and not run here because there is no `dotnet`):

```bash
DATA=$(mktemp -d)
cd /home/user/vs-game/1.22.7
dotnet VintagestoryServer.dll --dataPath "$DATA" \
  --addModPath /home/user/JumpingSpiders/JumpingSpiders/bin/Release/Mods \
  --addOrigin /home/user/JumpingSpiders/JumpingSpiders/assets
# or: copy Releases/jumpingspiders_<ver>.zip into "$DATA/Mods"
# wait until server-main.log contains "Dedicated Server now running!" ([strings]: "{0} now running{1}!"),
# then type /stop on stdin; grep "$DATA/Logs/server-main.log" and server-debug.log for jumpingspiders errors
```

Notes on the recipe:

- `serverconfig.json` has `DieAboveErrorCount` ("More then {0} errors detected. Shutting down now" [strings]), and `--withconfig` / `--setconfig` let a script override config values.
- The server parses entity JSON, AI task configs and shapes, so it catches most JSON mistakes.
- Texture and render problems only show up on a client.
- Pure C# logic (target filtering, config parsing) can be unit-tested with a normal xunit project that references `VintagestoryAPI.dll`. That is outside the wiki's scope [unverified].

---

## 8. Surprises and pitfalls

1. **Domain defaults differ between fields.** Write every code fully qualified. Verified [files]:

   | field | default domain if unprefixed |
   |---|---|
   | top-level entity fields (`client.shape`, `textures`, `sounds`, ...) | the entity's domain (`RegistryObjectTypeLoader`: `CreateSerializerForDomain(baseType.Code.Domain)`) |
   | `harvestable.drops` codes | **game** (`AsObject<BlockDropItemStack[]>()` with no domain; implicit `string → AssetLocation`) |
   | `deaddecay.decayedBlock` | **game** (`new AssetLocation(string)`) |
   | `grow.adultEntityCodes`, `multiply.spawnEntityCodes` | **game** (`AssetLocation.toLocations` / `new AssetLocation`) |
   | AI task `sound` / `finishSound` | the entity's domain |
   | AI task `entityCodes` | domain **ignored** (matches `Code.Path`; only a trailing `*`) |
   | `/entity spawn <type>` | **game** |

   Consequence: `adultEntityCodes: ["jumpingspider-adult-{gender}"]` would point at `game:` and break. Write `jumpingspiders:jumpingspider-adult-{gender}`. A vanilla sound in our AI task needs `game:creature/...`.
2. **AI-task animation names are lowercased, then compared case-sensitively with meta codes.** Use lowercase meta `code`s only. Vanilla wolf declares metas `"Walk"`, `"Run"` and `"Canter"` with footstep sounds. AI tasks would not match them and fall back to the bare shape animation; whether wolf footsteps actually play was not tested.
3. **`StartAnimation("x")` from C# only works if `client.animations` declares meta code `x`**; otherwise it silently returns false. The engine itself needs metas `die` and `hurt`.
4. **An animation with zero keyframes throws.** Duplicate element names silently break animation targeting. There is also an animated-joint cap (see 3.3).
5. **The template is behind.**
   - `modinfo.json` says `"game": "1.21.0"`.
   - `JumpingSpiders.csproj` does not copy `assets/` into `bin/`; it works only through `--addOrigin` in `launchSettings.json`.
   - The Client launch profile needs `Vintagestory.dll`, which this server-only install lacks.
6. **Client `health`.** The 1.22 notes say it is required on the client, but 1.22.7 silently auto-adds it and vanilla does not declare it. Add it on both sides anyway.
7. **Prey details.**
   - Indian, jackblack and scrub hares never spawn naturally in 1.22.7.
   - Chicken poults (`henpoult`, `roosterpoult`) are real entities; vanilla fox forgets them. Use `chicken-*`.
   - `hare-*` and the others also match other mods' entities with the same prefix.
8. **`xxxByType` takes the first matching key, and drops the property if nothing matches.** Order keys from specific to `"*"`.
9. **The wiki is often stale.**
   - Domains page: says survival and creative are domains.
   - Command lists: GameVersion 1.15 and Outdated.
   - Log names: `.txt` instead of `.log`.
   - Entity Behaviors and Block Classes / Block Entity pages: Outdated.
   - Creating A Code Mod page: still says modinfo `"game": ""`.
   - Custom C# entity behaviors and AI tasks have no wiki page at all.
10. **The AI system is mid-migration.** The `-r` refactored tasks and tag-based targeting exist in 1.22.7 but are unused by vanilla. Expect changes in 1.23 or later. 1.23 is announced as a combat rework, which may touch melee and hit detection.
11. **1.22.7 restricts `/entity debug` to creative mode.** A loaded DLL cannot be reloaded without restarting the game, though hot reload works per the wiki. Entity JSON changes need a world rejoin, not `.reload`.
12. **Climbing is built in** (`canClimb`, `canClimbAnywhere`, `rotateModelOnClimb`; see `lore/locust.json`), and so is leaping (`seekentity` `leapAtTarget`, default `leapAnimation: "jump"`). Both are worth trying before writing custom physics.

### Could not verify

- Nothing was built or run: no `dotnet` in the container, no client install, and no textures or sounds in the server install.
- Not checked at runtime: the placeholder shape loading; the exact `/entity spawn` attribute syntax for generation; `/worldconfig runAiTasks`; the domain used for texture paths written *inside* shape files (that resolution happens in closed `VintagestoryLib`, so set textures in the entity or block JSON or prefix them); and the default client `maxAnimatedElements` value.

> Reference notes on Vintage Story 1.22.7 internals, gathered from the game sources when this mod was started.
> They describe the engine, not the mod's current design (see `CLAUDE.md` and `README.md` for that). File paths in
> them are from the machine they were written on.

# Blocks, block entities, items and the basket trap (Vintage Story 1.22.7)

Research reference for the JumpingSpiders mod. Everything below was checked against the 1.22.7 sources and assets on this machine:

- `VSAPI` = `/home/user/anegostudios/vsapi` (git HEAD "Updated to Version 1.22.7")
- `VSE` = `/home/user/anegostudios/vsessentialsmod` (HEAD "Updated to Version 1.22.7")
- `VSS` = `/home/user/anegostudios/vssurvivalmod` (HEAD "Updated to Version 1.22.7")
- `ASSETS` = `/home/user/vs-game/1.22.7/assets/survival` (vanilla JSON; this folder is the `game:` domain)
- `LIB` = decompiled `/home/user/vs-game/1.22.7/VintagestoryLib.dll` (engine internals, decompiled with ilspycmd into the scratchpad folder `decomp/lib`). Line numbers for LIB refer to that decompiled output, not to an official source.

To check that the signatures are correct, a scratch project (`scratchpad/sigcheck/Sketch.cs`) using every override and API call shown in sections 6 to 9 was built against the shipped 1.22.7 DLLs (`VintagestoryAPI.dll`, `Mods/VSSurvivalMod.dll`, `Mods/VSEssentials.dll`). It built with 0 errors. I also decompiled `ItemShears` from the shipped `VSSurvivalMod.dll` and it matches the source.

Line numbers are 1-based and refer to the files named.

---

## 1. Basket trap

### 1.1 Where it lives

| Piece | File | Notes |
|---|---|---|
| Block JSON | `ASSETS/blocktypes/reed/baskettrap.json` | code `baskettrap-{reed,papyrus}` |
| Block class | `VSS/Block/BlockAnimalTrap.cs` | registered as `"BlockBasketTrap"` (`VSS/Systems/Core.cs:626`: `api.RegisterBlockClass("BlockBasketTrap", typeof(BlockAnimalTrap));`) |
| Block entity | `VSS/BlockEntity/BEAnimalTrap.cs` | `public class BlockEntityAnimalTrap : BlockEntityDisplay, IAnimalFoodSource, IPointOfInterest` (line 42), registered as `"BasketTrap"` (`Core.cs:914`) |
| Capture/release logic | `VSS/BlockBehavior/BehaviorCreatureContainer.cs` | `BlockBehaviorCreatureContainer` (statics `CatchCreature`, `ReleaseCreature`) |
| Container block that holds the creature | `ASSETS/blocktypes/reed/reedchest.json` (code `stationarybasket`, has `{name: "CreatureContainer" }` at line 74) | |
| Large variant | `ASSETS/blocktypes/wood/trapcrate.json` (`traptype: "large"`, line 13) | |
| Who attracts the animal | `VSE/Entity/AI/Tasks/AiTaskSeekFoodAndEat.cs` (`"seekfoodandeat"`) and `VSE/Entity/AI/TasksRefactored/AiTaskSeekFoodAndEat.cs` (`"seekfoodandeat-r"`) | registered in `VSE/Entity/AI/AiTaskManager.cs:43` and `:68` |

Basket trap JSON (relevant parts, `baskettrap.json:1-36`, verbatim):

```json5
{
	code: "baskettrap",
	class: "BlockBasketTrap",
	entityclass: "BasketTrap",
	behaviors: [
		{name: "Lockable"},
		{name: "UnstableFalling"}
	],
	entityBehaviors: [{ name: "Animatable" }],
	attributes: {
		auctionable: true,
		canStep: false,
		traptype: "small",
		foodTagMinWeight: 0.6,
		...
		creatureContainer: { type: "block", code: "stationarybasket-east", attributes: { type: "{material}" } },
		excludeFoodTags: ["nibbleCrop", "sweetBerryBush"],
		destroyedDrops: [ ... cattailtops / papyrustops, quantity: { avg: 6, var: 2 } ],
```

### 1.2 What makes a creature trappable

There are exactly two checks, both data-driven. I found no hardcoded entity list.

1. **`trappable` attribute on the entity type**, at the top-level `attributes` (that is `entity.Properties.Attributes`), keyed by trap type (`BEAnimalTrap.cs:31-39`):

```csharp
public static Dictionary<string, TrapChances> FromEntityAttr(Entity entity)
{
    return entity.Properties.Attributes?["trappable"].AsObject<Dictionary<string, TrapChances>>();
}

public static bool IsTrappable(Entity entity, string traptype)
{
    return entity.Properties.Attributes?["trappable"]?[traptype].Exists == true;
}
```

   `TrapChances` has two fields, `public float TrapChance;` and `public float TrapDestroyChance;` (lines 28-29). Trap type comes from the block, `Block.Attributes["traptype"].AsString("small")` (line 82). The basket trap is `"small"` and the trap crate is `"large"`.

   - `IsTrappable` only checks that the key **exists**. An entity with `trapChance: 0` is still lured into the trap. It then either breaks the trap (with probability `trapDestroyChance`) or just takes the bait. This is how wolves and bears break basket traps.
   - The capture roll happens in `TrapAnimal` (lines 211-242): `if (Api.World.Rand.NextDouble() < trapMeta.TrapChance)` captures the animal. Otherwise the bait is consumed (`inv[0].Itemstack = null`), and with chance `TrapDestroyChance` the trap becomes `Destroyed`. If it is not destroyed it still becomes `Trapped`, but empty.

2. **Diet must match the bait**, through `IsSuitableFor` (`BEAnimalTrap.cs:196-203`):

```csharp
public bool IsSuitableFor(Entity entity, CreatureDiet diet)
{
    if (TrapState != EnumTrapState.Ready) return false;
    if (inv[0]?.Itemstack == null || diet == null) return false;
    bool catchable = TrapChances.IsTrappable(entity, traptype);
    bool dietMatches = diet.Matches(inv[0].Itemstack, false, foodTagMinWeight);
    return catchable && dietMatches;
}
```

   `checkCategory` is **false**, so `foodCategories` in the diet are ignored. Only food **tags** count, and a weighted tag must have `weight >= foodTagMinWeight` (0.6 for the basket trap). See `CreatureDiet.Matches` in `VSE/Systems/POIRegistry.cs:66-104`. `FoodTags` (unweighted) are converted to weight 1 on deserialisation (lines 54-63). `SkipFoodTags` is a blacklist that is tested first.

**Size limits:** none. Neither `BEAnimalTrap.cs`, `BlockAnimalTrap.cs` nor `BehaviorCreatureContainer.cs` reads hitbox size. The only size-related check is at **release**: `type.SpawnCollisionBox.OmniNotDownGrowBy(0.1f)` must not collide at the release spot (`BehaviorCreatureContainer.cs:329-330`).

**Implicit requirement:** the creature must actually walk to the trap and "eat" it. That only happens through the `seekfoodandeat` / `seekfoodandeat-r` AI task (section 1.4). An entity without that task is never trapped. The task must also keep `doConsumePortion` true (the default, `AiTaskSeekFoodAndEat.cs:62`; refactored config `DoConsumePortion = true` at `TasksRefactored/AiTaskSeekFoodAndEat.cs:129`), because the capture happens inside `ConsumeOnePortion`.

**Hand-catching** (right-click an animal while holding a basket or reed chest with `CreatureContainer`) has two extra gates (`BehaviorCreatureContainer.cs:263-273`): `trapChance > 0` for the container's traptype (default `"small"`), and `entity.WatchedAttributes.GetAsInt("generation") >= (entity.Properties.Attributes?["trapPickupGeneration"].AsInt(5) ?? 5)`. The basket **trap** does not check generation.

### 1.3 Bait: which items count

Putting bait in (`BEAnimalTrap.tryReadyTrap`, lines 173-194) needs both of these (`BlockAnimalTrap.cs:141-154`):

```csharp
public bool IsAppetizingBait(ICoreAPI api, ItemStack baitStack)
{
    var collobj = baitStack.Collectible;

    return (collobj.GetNutritionProperties(api.World, baitStack, null) != null || collobj.Attributes?["foodTags"].Exists == true) &&
        api.World.EntityTypes.Any(type => type.Attributes?["creatureDiet"].AsObject<CreatureDiet>()?.Matches(baitStack, true, 0.5f) == true);
}

public bool CanFitBait(ICoreAPI api, ItemStack baitStack)
{
    var collobj = baitStack.Collectible;

    return Attributes?["excludeFoodTags"].AsArray<string>()?.Any(tag => collobj.Attributes?["foodTags"].AsArray<string>()?.Contains(tag) == true) != true;
}
```

So bait must be food or carry `attributes.foodTags`, must match at least one loaded entity type's top-level `creatureDiet` (weight >= 0.5, categories allowed here), and must not carry an excluded tag (`nibbleCrop`, `sweetBerryBush`).

Which bait catches which animal is decided only by `foodTags` against each creature's `weightedFoodTags` with weight >= 0.6. Vanilla tag sources:

| Tag | Items (`ASSETS/itemtypes/...`) |
|---|---|
| `meat` | `food/redmeat.json:8`, `food/poultry.json:8`, `food/bushmeat.json:8`, `food/fishfillet.json:48`, `food/fishchunk.json:43` |
| `grain` + `{type}` | `food/grain.json:14` |
| `fruit` + `{fruit}` | `food/fruit.json:32` |
| `egg` | `food/egg.json:17` |
| `tastyvegetable` etc. | `food/vegetable.json` |
| `fruitmash` | `resource/pressedmash.json:19` |

`foodTags` is set on the whole item, so **raw, cooked and cured meat all carry `meat`**. Raw meat has no `nutritionProps`, but it passes `IsAppetizingBait` through `foodTags`. Vanilla already has a meat eater (fox, `meat` weight 0.9), so meat is accepted as bait even before the mod adds anything.

For the spider, put this in the entity's **top-level** `attributes`:

```json5
attributes: {
    creatureDiet: { weightedFoodTags: [ { weight: 1, code: "meat" } ] },   // weight must be >= 0.6
    trappable: { "small": { trapChance: 0.5, trapDestroyChance: 0.01 } }
}
```

(This is the shape of the vanilla fox diet, `ASSETS/entities/animal/mammal/fox-adult.json:17-26`, plus the trappable block copied from chicken or hare.)

### 1.4 How the creature is attracted

- The trap registers itself as a POI on the server: `sapi.ModLoader.GetModSystem<POIRegistry>().AddPOI(this);` (`BEAnimalTrap.cs:106`). It removes itself in `OnBlockRemoved` and `OnBlockUnloaded` (lines 245-263).
- `Position => Pos.ToVec3d().Add(0.5, 0.25, 0.5)`, and `Type => inv.Empty ? "nothing" : "food"` (lines 60-61).
- `AiTaskSeekFoodAndEat.ShouldExecute` (`VSE/Entity/AI/Tasks/AiTaskSeekFoodAndEat.cs:100-150`) works like this:
  - it runs with `ExecutionChance` (default 0.995) and at most once every 15 s;
  - it respects cooldown hours and `EntityBehaviorMultiplyBase.ShouldEat` (99.6 % skip when not hungry, line 108);
  - it first looks for **loose dropped items** within 10 blocks that match the diet (lines 115-127);
  - it then calls `porregistry.GetNearestPoi(entity.Pos.XYZ, 48, ...)`, filtered on `poi.Type == "food"` and `IsSuitableFor(entity, Diet)`, and skips targets it failed to reach 4 times in the last 60 s.
- The entity paths to the POI (`NavigateTo_Async`, line 173). Once it is within `MinDistanceToTarget()` (`Math.Max(extraTargetDist + 0.6f, entity.SelectionBox.XSize / 2 + 0.05f)`, line 162), it eats for `eatTime` and then calls `targetPoi.ConsumeOnePortion(entity)` (line 241).
- The trap's `ConsumeOnePortion` (lines 205-209) queues `TrapAnimal(entity)` on the main thread and returns `1f`.
- The refactored task (`seekfoodandeat-r`) behaves the same way. It uses `poiRegistry.GetNearestPoi(..., Config.PoiSearchRange, poi => poi.Type == Config.PoiType ...)` (`TasksRefactored/AiTaskSeekFoodAndEat.cs:259-265`) with defaults `PoiSearchRange = 48`, `PoiType = "food"` and `LooseItemsSearchRange = 10`. Diet: `Diet ??= entity.Properties.Attributes["creatureDiet"]...` (line 175), so it can also be given in the task config. However, `IsAppetizingBait` and the handbook only read the entity-level `creatureDiet`.

Side effect: a spider with a `meat` diet and `eatLooseItems` true (the default) will also eat raw meat that players drop within 10 blocks.

### 1.5 How the creature is stored and released

Capture (`BehaviorCreatureContainer.cs:275-303`):

```csharp
public static void CatchCreature(ItemSlot slot, Entity entity)
{
    if (entity.World.Side == EnumAppSide.Client) return;
    var stack = slot.Itemstack;
    using (MemoryStream ms = new MemoryStream())
    {
        BinaryWriter writer = new BinaryWriter(ms);
        entity.ToBytes(writer, false);
        stack.Attributes.SetString("classname", entity.Api.ClassRegistry.GetEntityClassName(entity.GetType()));
        stack.Attributes.SetString("creaturecode", entity.Code.ToShortString());
        stack.Attributes.SetBytes("animalSerialized", ms.ToArray());
        ...
        stack.Attributes.SetDouble("totalDaysCaught", entity.World.Calendar.TotalDays - unrecoveredDays);
    }
    entity.Die(EnumDespawnReason.PickedUp);
}
```

- The trap's inventory slot gets the `creatureContainer` stack (`stationarybasket-east` with `type` attribute) holding the animal (`BEAnimalTrap.cs:220-223`).
- `Die(PickedUp)` does not spawn drops: drops only spawn for `reason == EnumDespawnReason.Death` (`VSAPI/Common/Entity/Entity.cs:2228-2240`).
- Right-clicking the trapped trap gives the player that basket and removes the trap block (`BEAnimalTrap.Interact`, lines 154-168). A basket containing an animal can only go in backpack slots (`GetStorageFlags` returns `EnumItemStorageFlags.Backpack`, `BehaviorCreatureContainer.cs:135-143`).
- The creature survives `CreatureSurvivalDays = 2` in-game days in the basket (field default, line 31; nothing in JSON sets it). After that the info text says "dead" and on release it is spawned and immediately killed with `EnumDamageType.Hunger` (lines 343-350).

Release (`ReleaseCreature`, lines 305-359): it creates `world.Api.ClassRegistry.CreateEntity(classname)`, calls `entity.FromBytes(reader, false, RemappedEntities)`, sets the position next to the clicked face, and then:

```csharp
entity.Attributes.SetString("origin", "playerplaced");
entity.Attributes.SetDouble("totalDaysCaught", stack.Attributes.GetDouble("totalDaysCaught"));
entity.Attributes.SetDouble("totalDaysReleased", world.Calendar.TotalDays);
world.SpawnEntity(entity);
```

### 1.6 What survives capture and release

`Entity.ToBytes(writer, forClient:false)` (`VSAPI/Common/Entity/Entity.cs:2113-2173`) is the same full save-game serialisation used for chunk saving. It writes:

- the game version and `EntityId`;
- **`WatchedAttributes`** (after behaviours' `ToBytes` hooks run and `Stats.ToTreeAttributes`);
- `Pos` and `PositionBeforeFalling`;
- `Code`;
- **`Attributes`** (the non-synced tree, only when `!forClient`);
- animation state.

`FromBytes` (lines 2016-2073) reads them back. As a result:

- **Kept:** everything in `WatchedAttributes`, including `generation` (read via `WatchedAttributes.GetAsInt("generation")`, `BehaviorCreatureContainer.cs:272`), health, hunger, `extraInfoText`, and any custom mod keys there. Everything in `Attributes` is also kept, except that `origin`, `totalDaysCaught` and `totalDaysReleased` are overwritten.
- **Not kept: `EntityId`.** `SpawnEntity` assigns a new id: `long num = ++SaveGameData.LastEntityId; ... entity.EntityId = num;` (`LIB/Vintagestory.Server/ServerMain.cs:2953-2955`). Anything that references the spider by entity id (for example a hammock BE storing "my owner") goes stale after capture and release.
- In-memory fields of C# behaviours or AI tasks that are not mirrored into attributes are lost, as they would be on any chunk reload. Store custom spider state in `WatchedAttributes` or `Attributes`.
- `FromBytes` calls `bh.FromBytes(isSync)` only if `Properties?.Server?.Behaviors` is set (lines 2066-2072). On a freshly created entity, `Properties` is not set yet when `ReleaseCreature` calls `FromBytes`. Behaviours therefore re-read their state from attributes when `SpawnEntity` initialises them. I verified this from the code path only, not at runtime.
- If the entity code was remapped, `RemappedEntities` is applied (lines 313-316).

### 1.7 Vanilla chicken and hare JSON that enables trapping

`ASSETS/entities/animal/bird/chicken-adult.json:17-28` (verbatim):

```json5
	attributes: {
		harshWinterSpawnRate: 0,
		creatureDiet: {
			foodCategories: ["Grain"],
			weightedFoodTags: [
				{ weight: 0.9, code: "fruitmash" },
				{ weight: 1, code: "grain" }
			],
		},
		trappable: {
			"small": { trapChance: 0.5, trapDestroyChance: 0.01 }
		},
```

and its AI task (`chicken-adult.json:276-285`):

```json5
						{
							code: "seekfoodandeat",
							priority: 1.35,
							eatSound: "creature/animal-eat-small",
							eatTime: 1.5,
							movespeed: 0.0065,
							animation: "Walk",
							mincooldownHours: 1,
							maxcooldownHours: 4,
						},
```

`ASSETS/entities/animal/mammal/hare-adult.json:13-23`:

```json5
		creatureDiet: {
			foodCategories: ["Vegetable"],
			weightedFoodTags: [
				{ weight: 0.5, code: "nibbleCrop" }, 
				{ weight: 1, code: "tastyvegetable" },
				{ weight: 0.2, code: "cassava" }
			]
		},
		trappable: {
			"small": { trapChance: 0.5, trapDestroyChance: 0.01 }
		},
```

The hare's "Vegetable" category does not help in the trap (categories are ignored there). Only `tastyvegetable` (weight 1 >= 0.6) does. `nibbleCrop` is excluded by the trap anyway.

Handbook: `BlockAnimalTrap.AddTrappableHandbookInfo` (lines 156-188) lists every entity with `trappable.<type>.trapChance > 0`. It uses the lang key `attributes.handbook.trappableGroupCode` or `"<domain>:item-creature-<code>"`. Basket info text uses `Lang.Get("item-creature-" + creaturecode)` (`BehaviorCreatureContainer.cs:375`), so add an `item-creature-<spidercode>` lang entry.

---

## 2. Bee skep: harvest by breaking

Files: `ASSETS/blocktypes/reed/skep.json`, `VSS/Block/BlockSkep.cs`, `VSS/BlockEntity/BEBeehive.cs`.

`skep.json` (verbatim excerpts):

```json5
	class: "BlockSkep",                                   // line 4
	entityClassByType: { "*-populated-*": "Beehive" },    // lines 10-12
	behaviors: [{name: "HorizontalOrientable", properties: {  dropBlockFace: "east" }}],   // line 18
	blockmaterial: "Other",                               // line 41
	resistance: 1.25,                                     // line 44
	dropsByType: {                                        // lines 53-62
		"skep-reed-*": [ 
			{ type: "item", code: "cattailtops", quantity: { avg: 6, var: 8, dist: "invexp" } },
			{ type: "item", code: "honeycomb", quantity: { avg: 3 } }
		],
		"*": [ 
			{ type: "item", code: "papyrustops", quantity: { avg: 6, var: 8, dist: "invexp" } },
			{ type: "item", code: "honeycomb", quantity: { avg: 3 } }
		]
	},
```

How it works:

- The `drops` array describes **only the harvest result**. `BlockSkep.GetDrops` (lines 94-102) returns the skep block itself unless the block is populated **and** `BlockEntityBeehive.Harvestable` is true:

```csharp
public override ItemStack[]? GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
{
    if (IsEmpty() || GetBlockEntity<BlockEntityBeehive>(pos)?.Harvestable != true)
    {
        return [new(world.BlockAccessor.GetBlock(CodeWithVariant("side", "east")))];
    }
    return getHarvestableDrops(world, pos, byPlayer, dropQuantityMultiplier);
}
```

- `getHarvestableDrops` (lines 104-121) loops over `Drops`, honours each drop's `Tool` requirement, and calls `Drops[i].GetNextItemStack(dropQuantityMultiplier)`.
- The special break step: `OnBlockBroken` (lines 65-82) first calls `base.OnBlockBroken(...)`, which spawns drops and removes the block. Then, **only on the server**, and with 40 % chance (`beemobSpawnChance`, attribute, default 0.4) for non-empty skeps, it spawns a `beemob` entity at the block centre with `entity.Attributes.SetString("origin", "brokenbeehive")`. This is the template for "spider turns aggressive when its hammock is broken": the hook has `IPlayer byPlayer`.
- Right-click picks the skep up (`OnBlockInteractStart`, lines 29-53). Left-click (break) harvests. The interaction help shows `blockhelp-skep-harvest` only when harvestable (lines 141-164).
- `Harvestable` is a BE field, set by a 3 s tick (`BEBeehive.cs:75, 199`) and saved in `ToTreeAttributes`. The handbook shows harvest drops via a `GetDropsForHandbook` override (lines 85-92).
- BEBeehive is also an `IAnimalFoodSource` (`Type => "food"`). When a bear or raccoon "eats" it, `ConsumeOnePortion` calls `Api.World.BlockAccessor.BreakBlock(Pos, null, 1f)` (`BEBeehive.cs:542-546`), so `OnBlockBroken` then receives `byPlayer == null`.

---

## 3. Tool speed

### 3.1 The algorithm

1. `Block.OnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)` (`VSAPI/Common/Collectible/Block/Block.cs:1028-1073`) runs **client side**, every ~40 ms while the player holds left-click:

```csharp
int requiredMiningTier = GetRequiredMiningTier(api.World, blockSel.Position);
if (requiredMiningTier == 0)
{
    if (dt > 0)
    {
        foreach (BlockBehavior behavior in BlockBehaviors)
        {
            dt *= behavior.GetMiningSpeedModifier(api.World, blockSel.Position, player);
            // This will also affect tool mining speed if stack != null, and that's OK
        }
    }
    resistance -= dt;
}

if (stack != null)
{
    resistance = stack.Collectible.OnBlockBreaking(player, blockSel, itemslot, remainingResistance, dt, counter);
}
```

   Bare hand: speed 1 (resistance is in seconds). When **anything** is held, the held collectible decides.

2. `CollectibleObject.OnBlockBreaking` (`VSAPI/Common/Collectible/Collectible.cs:622-695`) ends with `return remainingResistance - GetMiningSpeed(itemslot.Itemstack, blockSel, block, player) * dt;`. The exception is a block with `requiredMiningTier > 0` that the tool's tier or `MiningSpeed` table can't mine, which makes no progress (`cantMine`).

3. `GetMiningSpeed` (lines 763-797):

```csharp
if (miningSpeeds == null || !miningSpeeds.ContainsKey(material))
{
    toolMiningSpeed *= traitRate;
}
else
{
    toolMiningSpeed *= miningSpeeds[material] * traitRate * GlobalConstants.ToolMiningSpeedModifier;
}
```

   `material = block.GetBlockMaterial(...)`. `traitRate` is the player stat `miningSpeedMul`, applied only to Stone and Ore. `GlobalConstants.ToolMiningSpeedModifier` comes from world config (`ASSETS/config/general.json:7` `toolMiningSpeedModifier: 1`, patched by `ASSETS/patches/globmodifiers.json:39-42`). `CollectibleBehavior.GetMiningSpeed` and `GetMiningSpeeds` can override this.

4. Per-item tables come from JSON `miningspeed` / `miningspeedbytype` (field `public Dictionary<EnumBlockMaterial, float> MiningSpeed;`, `Collectible.cs:105`). Materials are `EnumBlockMaterial` (`VSAPI/Common/Collectible/Block/EnumBlockMaterial.cs:15-36`): Air, Soil, Gravel, Sand, Wood, Leaves, Stone, Ore, Water, Snow, Ice, Metal, Mantle, Plant, Glass, Ceramic, Cloth, Lava, Brick, Fire, Meta, Other.

Mining-speed materials per vanilla tool (from `ASSETS/itemtypes/tool/*.json`, `miningspeedbytype` keys):

| Tool file | materials with a speed |
|---|---|
| axe-metal.json (line 186), axe-stone.json (line 84) | wood, plant, leaves |
| saw.json (line 59) | wood, leaves |
| knife.json (line 228) | plant |
| hoe.json (line 88) | soil |
| shovel.json (line 93) | gravel, sand, snow, soil |
| pickaxe.json (line 63) | ceramic, ice, metal, ore, stone |
| prospectingpick.json (line 63) | metal, ore, stone |
| **shears.json** | **none** |

No vanilla tool lists `Cloth` or `Other`, so on a block made of those, every tool and the bare hand break at speed 1.

### 3.2 Shears

`ASSETS/itemtypes/tool/shears.json` (verbatim, lines 1-7 and 18-20). It has **no `miningspeed` and no `tooltier`** anywhere in the file:

```json5
{
	code: "shears",
	class: "ItemShears",
	tags: ["tool-shears", "tool-trimming", "tool"],
	damagedby: ["blockbreaking", "attacking"],
	tool: "shears",
	...
	variantgroups: [
		{ code: "metal", states: ["copper", "tinbronze", "bismuthbronze", "blackbronze", "gold", "silver", "iron", "meteoriciron", "steel" ] },
	],
```

I found no JSON patch that adds one (grep over `ASSETS/patches` and `game/patches` for "shears": nothing). `ItemShears` (`VSS/Item/ItemShears.cs`) does not override `GetMiningSpeed`. It adds two things:

- `CanMultiBreak(block)` returns `block.BlockMaterial == EnumBlockMaterial.Leaves` (lines 16-19). `OnBlockBreaking` applies the same damage to up to 5 neighbouring leaf blocks (lines 22-53), and `OnBlockBrokenWith` breaks them too (lines 59-92).
- `OnBlockBrokenWith` does **not** call the base method (line 67 is commented out). It breaks the target with `api.World.BlockAccessor.BreakBlock(pos, plr)` (line 96) and only calls `DamageItem` for the extra leaf blocks. **Breaking a non-leaf block with shears costs no durability.**

So in 1.22.7 **shears break every block at bare-hand speed.** No vanilla block is broken faster per block with shears. The only block where shears have an edge is **leaves** (`blockmaterial: "Leaves"`, e.g. `ASSETS/blocktypes/plant/leaves/normal.json:69`, `resistance: 0.5` line 120), through the 5-block multi-break. Even there, axes (leaves 1.6-3+) and saws (1.6-3.2) are faster per block. Shears also act as a non-breaking tool for berry-bush pruning (`EnumTool.Shears` check, `VSS/Legacy/Block/BlockBerryBush.cs:75`) and in recipes (`tags: ["tool-shears"]`).

### 3.3 What the hammock must declare so shears are fastest

No JSON-only setting on the block does this. The options, all checked to compile:

1. **Override `OnGettingBroken` in the hammock Block class** and scale `dt` when shears are held. This only affects the hammock:

```csharp
public override float OnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
{
    if (player.InventoryManager.ActiveTool == EnumTool.Shears) dt *= 4f; // EnumTool? ActiveTool { get; } — VSAPI/Common/Entity/Player/IPlayerInventoryManager.cs:23
    return base.OnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter);
}
```

   Because `dt` is passed on to `stack.Collectible.OnBlockBreaking(... dt ...)`, the multiplier also applies on the tool path.

2. A small **BlockBehavior** overriding `public virtual float GetMiningSpeedModifier(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)` (`VSAPI/Common/Collectible/Block/BlockBehavior.cs:366`). Vanilla precedent: `VSS/BlockBehavior/BehaviorReinforcable.cs:71-80` returns 0.6. It is only applied when the block's `requiredMiningTier == 0` (Block.cs:1033), which is the default.

3. A **JSON patch on `game:itemtypes/tool/shears.json`** adding e.g. `miningspeed: { "cloth": 4 }`. This also speeds shears up on every vanilla block with that material. It does go through the vanilla `ToolMiningSpeedModifier`.

In every case, give the hammock a `blockmaterial` that no other tool lists (`"Cloth"` or `"Other"`). Do not use `"Plant"` (knife and axe are faster there) or `"Leaves"` (axe and saw are faster, and shears would multi-break neighbouring leaves). Also leave `requiredMiningTier` at 0.

The server does **not** re-check break time. The block-break packet handler only checks the mining tier (`LIB/Vintagestory.Server/ServerSystemBlockSimulation.cs:586-591`) and then calls `itemstack.Collectible.OnBlockBrokenWith(...)` or `block.OnBlockBroken(...)` (lines 597-607). A client-side speed change is therefore sufficient, but the mod must be installed on the client.

To restrict drops to a tool, `BlockDropItemStack` has `public EnumTool? Tool;` (`VSAPI/Common/Collectible/Block/BlockDropItemStack.cs:74`, "If set, then the given tool is required to make this block drop anything"), enforced in `ToRandomItemstackForPlayer` (line 230). Vanilla example, `ASSETS/blocktypes/plant/tallgrass.json:148-149`:

```json5
			{ type: "item", code: "drygrass", quantity: { avg: 1 }, tool: "knife"  },
			{ type: "item", code: "drygrass", quantity: { avg: 1 }, tool: "scythe"  }
```

---

## 4. Drops

### 4.1 Random quantity 2 to 4

Block JSON:

```json5
	drops: [
		{ type: "item", code: "game:flaxtwine", quantity: { avg: 3, var: 1 } }
	],
```

The math, verified in source:

- `NatFloat` default distribution is `UNIFORM` (`VSAPI/Math/NatFloat.cs:114`), and `nextFloat` returns `offset + multiplier * (avg + rnd * 2 * var)` with `rnd` in [-0.5, 0.5) (lines 252-254). With avg 3 and var 1 that is a float in [2, 4).
- `BlockDropItemStack.GetNextItemStack` rounds with `GameMath.RoundRandom(random, Quantity.nextFloat(dropQuantityMultiplier))` (`BlockDropItemStack.cs:157`). `RoundRandom` is `(int)value + ((rand.NextDouble() < (value - (int)value)) ? 1 : 0)` (`VSAPI/Math/GameMath.cs:345-348`).
- Result: **2 with 25 %, 3 with 50 %, 4 with 25 %, never below 2 or above 4** (when `dropQuantityMultiplier == 1`).
- An evenly distributed 2/3/4 can't be expressed this way: `var: 1.5` would allow 1 and 5. For that, override `GetDrops` and use `world.Rand.Next(2, 5)`.

**Domain:** codes in a mod's block JSON are parsed with the mod's domain as default (`JsonUtil.CreateSerializerForDomain(baseType.Code.Domain)`, `VSE/Loading/RegistryObjectTypeLoader.cs:440`, and `AssetLocationJsonParser`, `VSAPI/Util/JsonUtil.cs:143-159`). So write **`game:flaxtwine`**, not `flaxtwine`. (`loadFromProperties` in `variantgroups` is deserialised without the domain converter, `VSE/Loading/RegistryObjectType.cs:206`, so `"abstract/horizontalorientation"` resolves to `game:` without a prefix.)

`dropsByType` works as in the skep. `GetDrops` and `GetDropsForHandbook` can be overridden as in `BlockSkep`.

### 4.2 Item codes (1.22.7)

| What | Code | Source |
|---|---|---|
| Flax twine | `game:flaxtwine` (no variants) | `ASSETS/itemtypes/resource/flaxtwine.json:2`; lang `"item-flaxtwine": "Flax twine"` (`assets/game/lang/en.json:2218`) |
| Raw red meat | `game:redmeat-raw` | `ASSETS/itemtypes/food/redmeat.json:2,57` (`type: raw, cooked, vintage, cured`) |
| Raw poultry | `game:poultry-raw` | `food/poultry.json:2,54` (`type: raw, cooked, cured`); chicken drops `code: "poultry-raw"` (`chicken-adult.json:129`) |
| Raw bushmeat | `game:bushmeat-raw` | `food/bushmeat.json:2,54` (`type: raw, cooked, cured`) |

All three carry `foodTags: ["meat"]` on every variant, including cooked and cured. To make only raw meat attract spiders, a weighted diet tag can't tell them apart; use `skipFoodTags` or a custom tag patched onto the raw variants only.

---

## 5. Wall-attached and supported blocks

### 5.1 `HorizontalAttachable`

`VSS/BlockBehavior/BehaviorHorizontalAttachable.cs`, registered as `"HorizontalAttachable"` (`VSS/Systems/Core.cs:646`).

- It requires a `side` variant group from `abstract/horizontalorientation` (`north, east, south, west`; `ASSETS/worldproperties/abstract/horizontalorientation.json`). **The side must be the last code part**, because `CanBlockStay` does `block.Code.Path.Split('-')` and takes the last part (lines 202-212).
- **Meaning of the variant:** the side code is the direction from the attached block **to the supporting block**. `TryAttachTo` computes `oppositeFace = blockSel.Face.Opposite; attachingBlockPos = blockSel.Position.AddCopy(oppositeFace); orientedBlock = block.CodeWithParts(oppositeFace.Code)` (lines 182-200), and `CanBlockStay` looks at `pos.AddCopy(facing)` with `facing = FromCode(lastPart)`. `BEMycelium.generateSideGrowingMushrooms` confirms it (`VSS/BlockEntity/BEMycelium.cs:309-328`): it walks from the log `f` to an air block and places the mushroom with `facing = f.Opposite`, i.e. pointing back at the log.
- Placement by a player tries the clicked horizontal face first, then all four (lines 115-137).
- The face check is `attachingBlock.CanAttachBlockAt(world.BlockAccessor, block, attachingBlockPos, blockSel.Face, attachmentArea)`. The face passed is the supporting block's face that touches the attachment, i.e. `facing.Opposite` in `CanBlockStay`.
- **When the support is removed:** `OnNeighbourBlockChange` calls `if (!CanBlockStay(world, pos)) world.BlockAccessor.BreakBlock(pos, null);` (lines 171-179). The block is broken **with `byPlayer == null`**, so drops spawn and `OnBlockBroken` runs, with no player. This also happens when a tree is felled: `ItemAxe` breaks logs with `world.BlockAccessor.BreakBlock(pos, byPlayer, ...)` (`VSS/Item/ItemAxe.cs:135`), and `BreakBlock` calls `worldmap.TriggerNeighbourBlockUpdate(pos)` (`LIB/Vintagestory.Common/BlockAccessorBase.cs:557-561`).
- By default `handleDrops = true` (line 60). The behaviour then **replaces** drops with the block's `dropBlockFace` variant (default `north`) or `dropBlock` (lines 139-156). For a hammock that drops twine, set `properties: { handleDrops: false }` as the side mushroom does.
- Nothing can attach to a HorizontalAttachable block: its `CanAttachBlockAt` returns false with `PreventDefault` (lines 215-219).
- `attachmentAreas` (per side, `RotatableCube` in voxels 0..15) are passed through to `CanAttachBlockAt`. Without them, `attachmentArea` is null.

Other attach behaviours: `OmniAttachable` (any of 6 faces, variant `up/down` + horizontal; `VSS/BlockBehavior/BehaviorOmniAttachable.cs`, uses the same `BreakBlock(pos, null)` on loss of support at lines 138-146; used by `ASSETS/blocktypes/metal/lantern.json:11`), and `CanAttach` (`Core.cs:672`, lets other things attach to a block that isn't side-solid).

### 5.2 The face test

`Block.CanAttachBlockAt` (`VSAPI/Common/Collectible/Block/Block.cs:766-787`):

```csharp
public virtual bool CanAttachBlockAt(IBlockAccessor blockAccessor, Block block, BlockPos pos, BlockFacing blockFace, Cuboidi attachmentArea = null)
{
    ... // block behaviors may decide
    if (preventDefault) return result;
    return SideSolid[blockFace.Index];
}
```

So by default the support's `sidesolid` flag for that face decides. Defaults: `public SmallBoolArray SideSolid = new SmallBoolArray(SmallBoolArray.OnAllSides);` (Block.cs:289), and JSON `sidesolid` only overrides listed faces (`VSE/Loading/BlockType.cs:842-844`).

### 5.3 Simplest vanilla examples

- `ASSETS/blocktypes/wood/canvas.json` (51 lines; `enabled: false`, so not in game, but it is the cleanest syntax):

```json5
	behaviors: [{name: "HorizontalAttachable"}],
	variantgroups: [
		{ code:"side", loadFromProperties: "abstract/horizontalorientation" }
	],
	shapebytype: {
		"*-north": { base: "block/wood/canvas", rotateY: 180 },
		"*-east": { base: "block/wood/canvas", rotateY: 90 },
		"*-south": { base: "block/wood/canvas", rotateY: 0 },
		"*-west": { base: "block/wood/canvas", rotateY: 270 }
	},
	...
	collisionbox: null,
	selectionbox: {
		x1: 0, y1: 0, z1: 0, 
		x2: 1, y2: 1, z2: 0.0625,
		rotateYByType: { "*-east": 270, "*-south": 180, "*-west": 90, "*-north": 0 }
	},
```

- **Closest analogue for "on the side of a tree trunk":** `ASSETS/blocktypes/plant/mushroom-side.json` (side mushrooms on logs). It uses `behaviors: [{name: "HorizontalAttachable", properties: { handleDrops: false } }]` (line 8) and `side` as the last variant group (line 6). It is generated onto `BlockLog` trunks by `BEMycelium.generateSideGrowingMushrooms` (`VSS/BlockEntity/BEMycelium.cs:291-332`), which is a worked example of code placement next to a trunk.

### 5.4 Can an entity stand inside a non-solid block?

Yes. Entity physics and the pathfinder only use `Block.GetCollisionBoxes(...)` (which returns `CollisionBoxes`, Block.cs:632-635), via `CollisionTester` (`VSAPI/Math/CollisionTester.cs:228, 279, 338, 398, 518`) and `AStar` (`VSE/Entity/Pathfinding/Astar/AStar.cs:205, 295`).

- With `collisionbox: null` the space is free: entities walk through it and can stand inside it. Example: spider web, `ASSETS/blocktypes/spiderweb.json:21` (also `walkspeedmultiplier: 0.25`).
- With a thin partial box the entity stands on top of the box, inside the block's 1x1x1 cell. The pathfinder adds the box height (`steponHeightAdjust`, AStar.cs:293-299). Vanilla precedent: the hen stands **in** the henbox, `collisionbox: {x1: 0.25, y1: 0, z1: 0.25, x2: 0.75, y2: 0.08, z2: 0.75}` (`ASSETS/blocktypes/wood/henbox.json:36`). `AiTaskSeekBlockAndLay` wants a horizontal squared distance of <= 0.01 to `Pos + (0.5, 0.5, 0.5)`.
- `Block.OnEntityInside(IWorldAccessor world, Entity entity, BlockPos pos)` fires for any entity in the 1x1x1 cell regardless of boxes (Block.cs:1575-1584).
- Placement is only blocked by intersecting entities if the block has collision boxes: `if ((CollisionBoxes != null && CollisionBoxes.Length > 0) && world.GetIntersectingEntities(...).Length != 0)` gives failure `entityintersecting` (Block.cs:928-932).
- The pathfinder refuses blocks with `CanStep == false` (AStar.cs:183, 283). The basket trap sets `canStep: false` in attributes (baskettrap.json:12). Its collision box is at Block level, but I did not trace how `CanStep` is loaded from attributes.

---

## 6. Custom Block + BlockEntity pattern (1.22.7)

### 6.1 Registration

`ICoreAPICommon` (`VSAPI/Common/API/ICoreAPI.cs`):

```csharp
void RegisterBlockClass(string className, Type blockType);                         // line 60
void RegisterBlockEntityClass(string className, Type blockentityType);             // line 75
void RegisterBlockBehaviorClass(string className, Type blockBehaviorType);         // line 97
```

The doc comments say "Must happen before any blocks are loaded. Be sure to register it on the client and server side", so call them in `ModSystem.Start(ICoreAPI api)`. Vanilla does this in `Start` (`VSS/Systems/Core.cs:106-123`).

The registries are plain `Dictionary<string, Type>` (`LIB/Vintagestory.Common/ClassRegistry.cs:20, 44`): **case-sensitive, and a duplicate name silently overwrites** (`BlockClassToTypeMapping[blockClass] = block;`, line 162). Prefix names with the modid, e.g. `"jumpingspiders.BlockSilkHammock"`. Some block entity names are reserved and throw (`legacyBlockEntityClassNames`, line 48: `"Chest"`, `"Basket"`, `"Axle"`, ...).

Block JSON:

```json5
{
	code: "silkhammock",
	class: "jumpingspiders.BlockSilkHammock",
	entityClass: "jumpingspiders.SilkHammock",    // vanilla uses both "entityClass" (henbox.json:4) and "entityclass" (baskettrap.json:4)
	behaviors: [ { name: "HorizontalAttachable", properties: { handleDrops: false } } ],
	variantgroups: [ { code: "side", loadFromProperties: "abstract/horizontalorientation" } ],
	blockmaterial: "Cloth",
	...
}
```

### 6.2 Block hooks: exact signatures (`VSAPI/Common/Collectible/Block/Block.cs`)

| Line | Signature | Who calls / when |
|---|---|---|
| 1028 | `public virtual float OnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)` | client, while mining |
| 1090 | `public virtual void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)` | **The player who broke it.** Called on the **server** (`ServerSystemBlockSimulation.cs:602/606` via `OnBlockBrokenWith` or directly) **and on the breaking client** (`LIB/Vintagestory.Client.NoObf/ClientMain.cs:1832/1836`). `byPlayer` is **null** for neighbour-support loss, `BreakBlock(pos, null)`, animals eating the block, etc. Base implementation runs behaviours and then `SpawnDropsAndRemoveBlock` (lines 1106-1147) |
| 1276 | `public virtual ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)` | from `SpawnDropsAndRemoveBlock`, server only, skipped for creative players (line 1117) |
| 1350 | `public virtual void OnBlockRemoved(IWorldAccessor world, BlockPos pos)` | any removal (no player). The base removes the BE (line 1367) |
| 1379 | `public virtual void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null)` | any placement. The base spawns the BE (line 1383): call `base` first |
| 1409 | `public virtual void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)` | neighbour changed |
| 2523 | `public virtual void OnBlockExploded(IWorldAccessor world, BlockPos pos, BlockPos explosionCenter, EnumBlastType blastType, string ignitedByPlayerUid)` | explosions. Calls `GetDrops(world, pos, null)` and then `entity.OnBlockBroken()` with no player (lines 2525-2566) |

Order inside `SpawnDropsAndRemoveBlock`: `blockEntity.OnBlockBroken(byPlayer)`, then (server, non-creative) `GetDrops` and spawn, then break particles, then `SetBlock(0, pos)`. That last call triggers `OnBlockRemoved` and the BE's `OnBlockRemoved()`. So **during `OnBlockBroken` before calling `base`, and in `BE.OnBlockBroken(byPlayer)`, the BE still exists and its data is readable**. After `base.OnBlockBroken` returns, the BE is gone. Read the BE first, as in the sketch below.

Server-wide alternative with the player: `IServerEventAPI.BreakBlock` (`event BlockBreakDelegate BreakBlock;`, `VSAPI/Server/API/IServerEventAPI.cs:265`) and `DidBreakBlock` (line 270). Delegates (`VSAPI/Common/API/Delegates.cs:153, 156`):

```csharp
public delegate void BlockBrokenDelegate(IServerPlayer byPlayer, int oldblockId, BlockSelection blockSel);
public delegate void BlockBreakDelegate(IServerPlayer byPlayer, BlockSelection blockSel, ref float dropQuantityMultiplier, ref EnumHandling handling);
```

### 6.3 BlockEntity hooks (`VSAPI/Common/Collectible/Block/BlockEntity.cs`)

| Line | Signature | Notes |
|---|---|---|
| 72 | `public virtual void Initialize(ICoreAPI api)` | after spawn or chunk load. On load, `FromTreeAttributes` runs **before** it (doc lines 67-69). Call base (sets `Api`) |
| 130 | `public virtual long RegisterGameTickListener(Action<float> onGameTick, int millisecondInterval, int initialDelayOffsetMs = 0)` | auto-unregistered on remove and unload (lines 212-226, 261-285). Runs on whichever side registered it, so guard with `api.Side == EnumAppSide.Server` |
| 182 | `public virtual long RegisterDelayedCallback(Action<float> OnDelayedCallbackTick, int millisecondInterval)` | |
| 212 | `public virtual void OnBlockRemoved()` | always on removal; call base |
| 242 | `public virtual void OnBlockBroken(IPlayer byPlayer = null)` | survival break or explosion; **has the player** when broken by one |
| 261 | `public virtual void OnBlockUnloaded()` | chunk unload; call base |
| 290 | `public virtual void OnBlockPlaced(ItemStack byItemStack = null)` | only on fresh placement, after `Initialize` |
| 302 | `public virtual void ToTreeAttributes(ITreeAttribute tree)` | save and sync to client; call base |
| 332 | `public virtual void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)` | load and client sync; `Api` may be null here; call base |
| 451 | `public virtual void MarkDirty(bool redrawOnClient = false, IPlayer skipPlayer = null)` | server: resend tree to clients |

`SpawnBlockEntity` on the server is synchronous: create, `CreateBehaviors`, `Initialize`, `AddBlockEntity`, `OnBlockPlaced` (`LIB/Vintagestory.Server/ServerWorldMap.cs:471-489`).

### 6.4 Minimal sketch (compiles against the 1.22.7 DLLs)

```csharp
public class BlockSilkHammock : Block
{
    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        var be = GetBlockEntity<BESilkHammock>(pos);          // read BEFORE base: base removes it
        long occupant = be?.OccupantEntityId ?? 0;
        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        if (world.Side != EnumAppSide.Server || byPlayer == null) return;
        // look up occupant, make it hostile to byPlayer.Entity ...
    }
}

public class BESilkHammock : BlockEntity, IPointOfInterest
{
    public long OccupantEntityId;
    public Vec3d Position => Pos.ToVec3d().Add(0.5, 0.0, 0.5);
    public string Type => "jumpingspiders:hammock";

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
        {
            api.ModLoader.GetModSystem<POIRegistry>().AddPOI(this);
            RegisterGameTickListener(OnServerTick, 2000);
        }
    }
    void OnServerTick(float dt) { }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        if (Api.Side == EnumAppSide.Server) Api.ModLoader.GetModSystem<POIRegistry>().RemovePOI(this);
    }
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        if (Api?.Side == EnumAppSide.Server) Api.ModLoader.GetModSystem<POIRegistry>().RemovePOI(this);
    }
    public override void ToTreeAttributes(ITreeAttribute tree) { base.ToTreeAttributes(tree); tree.SetLong("occupant", OccupantEntityId); }
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve) { base.FromTreeAttributes(tree, worldAccessForResolve); OccupantEntityId = tree.GetLong("occupant"); }
}
```

To make the spider hostile to the breaker, the emotion-state API is `public bool TryTriggerState(string statecode, long sourceEntityId)` and `public bool TryTriggerState(string statecode, double rndValue, long sourceEntityId)` (`VSE/Entity/Behavior/BehaviorEmotionStates.cs:167, 172`). Passing `rndValue = 0` bypasses the state's `chance`. Vanilla calls it with `"aggressiveondamage"` (line 140). Whether the attack and seek AI tasks then target that specific source entity is an AI-task question I did **not** verify here.

---

## 7. Points of interest (POI)

All in `VSE/Systems/POIRegistry.cs` (namespace `Vintagestory.GameContent`, assembly VSEssentials):

```csharp
public delegate bool PoiMatcher(IPointOfInterest poi);                 // line 15
public interface IPointOfInterest { Vec3d Position { get; } string Type { get; } }   // 17-25
public interface IAnimalFoodSource : IPointOfInterest                  // 112-121
{   bool IsSuitableFor(Entity entity, CreatureDiet diet);
    float ConsumeOnePortion(Entity entity); }
public interface IAnimalNest : IPointOfInterest                        // 124-141
{   bool IsSuitableFor(Entity entity, string[] nestTypes);
    bool Occupied(Entity entity);
    void SetOccupier(Entity entity);
    bool TryAddEgg(ItemStack egg);
    float DistanceWeighting { get; } }
```

`POIRegistry : ModSystem` ("not synchronized to client", line 146). It stores `Dictionary<Vec2i, List<IPointOfInterest>>` keyed by **chunk column** of `Position` at insertion time.

- `public void AddPOI(IPointOfInterest poi)` (line 283), `public void RemovePOI(IPointOfInterest poi)` (line 297). RemovePOI recomputes the chunk column from the **current** `Position`, so `Position` must not change while registered.
- `public IPointOfInterest GetNearestPoi(Vec3d centerPos, float radius, PoiMatcher matcher = null)` (line 194).
- `public IPointOfInterest GetWeightedNearestPoi(Vec3d centerPos, float radius, PoiMatcher matcher = null)` (line 232). Distance is multiplied by `IAnimalNest.DistanceWeighting`.
- `public void WalkPois(Vec3d centerPos, float radius, PoiMatcher callback = null)` (line 165).
- The `Type` string is free-form. Vanilla uses `"food"`, `"nest"` and `"nothing"`. A custom type such as `"jumpingspiders:hammock"` won't collide with vanilla tasks because they all filter on `Type`.
- Registration is the block entity's job, on the server, in `Initialize`, with removal in **both** `OnBlockRemoved` and `OnBlockUnloaded`. The registry is not persisted; BEs re-add themselves when the chunk loads.

**Worked example 1: hen box and laying** (`VSS/BlockEntity/BEHenBox.cs`, `VSE/Entity/AI/Tasks/AiTaskSeekBlockAndLay.cs`, task code `"seekblockandlay"`, `AiTaskManager.cs:44`):

- `BlockEntityHenBox : BlockEntityDisplay, IAnimalNest` (line 29). `Position => Pos.ToVec3d().Add(0.5, 0.5, 0.5)` and `Type => "nest"` (36-37). `IsSuitableFor` compares `BlockHenbox.NestType` (from block attribute `nestType`, `VSS/Block/BlockHenbox.cs:16-18`) with the task's `nestTypes` (57-61).
- Occupancy: `Occupied(entity)` is `occupier != null && occupier != entity`, and `SetOccupier` stores the **Entity object** in a field (39, 63-76). It is not saved (only a bool `isOccupied` for the client, line 258).
- Server side it does `AddPOI(this)` and `RegisterGameTickListener(On1500msTick, 1500)` (214-215), and `RemovePOI` in `OnBlockRemoved` and `OnBlockUnloaded` (232-250). The tick hatches chicks with `childEntity.WatchedAttributes.SetInt("generation", ...)` (149).
- Task flow:
  - `ShouldExecute`: 3 % chance per tick, every 15 s max, cooldown, and `DidConsumeFood(...)`. It then calls `FindPOI(42)` = `porregistry.GetWeightedNearestPoi(entity.Pos.XYZ, radius, poi => poi.Type == "nest" && !Occupied && IsSuitableFor && failedAttempts < 4 ...)` (lines 104-155).
  - `StartExecute`: `pathTraverser.NavigateTo_Async(targetPoi.Position, moveSpeed, MinDistanceToTarget() - 0.1f, OnGoalReached, OnStuck, null, 1000, 1)` (line 177).
  - `ContinueExecute`: once `pos.HorizontalSquareDistanceTo(...) <= 0.01` it stops the path, calls `targetPoi.SetOccupier(entity)`, plays the sit animation, and after `layTime` calls `targetPoi.TryAddEgg(MakeEggItem(chickCode))`. It also stops when `TotalDays >= sitEndDay` (lines 223-320).
- Path traverser API (`VSE/Entity/Pathfinding/PathTraverserBase.cs`):
  - line 45: `public virtual bool NavigateTo(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, Action onNoPath = null, bool giveUpWhenNoPath = false, int searchDepth = 10000, int mhdistanceTolerance = 0, EnumAICreatureType? creatureType = null)`
  - line 50: `public virtual bool NavigateTo_Async(Vec3d target, float movingSpeed, float targetDistance, Action OnGoalReached, Action OnStuck, Action OnNoPath = null, int searchDepth = 10000, int mhdistanceTolerance = 0, EnumAICreatureType? creatureType = null)`

**Worked example 2: trough** (`VSS/BlockEntity/BETrough.cs`): `BlockEntityTrough : BlockEntityContainer, ITexPositionSource, IAnimalFoodSource` (line 51), `Position => Pos.ToVec3d().Add(0.5, 0.5, 0.5)`, `Type => "food"` (60-61).

- `IsSuitableFor` (113-127) uses `diet.Matches(contentResolvedItemstack)`, i.e. **with** categories and min weight 0, unlike the trap.
- `ConsumeOnePortion` (158-173) takes `QuantityPerFillLevel` items and returns 1.
- A two-block trough registers a second dummy POI at the other half (`DoubleTroughPoiDummy`, 27-49, 190-197) and removes both (227-246). This is the pattern for one block entity exposing several POI positions.

**Worked example 3: basket trap**, see section 1.4. Same `IAnimalFoodSource` path, with the capture inside `ConsumeOnePortion`.

---

## 8. World access from code

`IBlockAccessor` (`VSAPI/Common/API/IBlockAccessor.cs`):

| Line | Signature | Notes |
|---|---|---|
| 262 | `Block GetBlock(BlockPos pos);` | never null (air id 0) |
| 282 | `Block GetBlock(BlockPos pos, int layer);` | `BlockLayersAccess.Solid/Fluid/MostSolid/Default` |
| 372 | `void WalkBlocks(BlockPos minPos, BlockPos maxPos, Action<Block, int, int, int> onBlock, bool centerOrder = false);` | "Currently NOT dimensionally aware"; may call twice per position if a fluid is present |
| 382 | `void SearchBlocks(BlockPos minPos, BlockPos maxPos, ActionConsumable<Block, BlockPos> onBlock, Action<int, int, int> onChunkMissing = null);` | return false to stop |
| 415 / 432 | `void SetBlock(int blockId, BlockPos pos);` / `void SetBlock(int blockId, BlockPos pos, ItemStack byItemstack);` | calls old `OnBlockRemoved` and new `OnBlockPlaced` (so the BE is spawned) **only if the id changes** (`LIB/.../BlockAccessorBase.cs:151-157`); it does **not** trigger neighbour updates |
| 438 | `void ExchangeBlock(int blockId, BlockPos pos);` | no place/remove hooks; BE kept |
| 447 | `void BreakBlock(BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f);` | `OnBlockBroken` plus neighbour updates (`BlockAccessorBase.cs:557-561`) |
| 510 | `T GetBlockEntity<T>(BlockPos position) where T : BlockEntity;` | |
| 838 | `bool IsSideSolid(int x, int y, int z, BlockFacing facing);` | tests solid and fluid layers |

Placing and getting the BE (server):

```csharp
Block hammock = world.GetBlock(new AssetLocation("jumpingspiders", "silkhammock-" + towardTrunk.Code));
world.BlockAccessor.SetBlock(hammock.BlockId, airPos);
var be = world.BlockAccessor.GetBlockEntity<BESilkHammock>(airPos);   // available immediately (synchronous spawn)
```

`Block.GetBlockEntity<T>(BlockPos)` also exists on the Block (used throughout `BlockAnimalTrap`). Code-variant helpers live in `VSAPI/Common/Registry/RegistryObject.cs`:

- line 102: `public AssetLocation CodeWithParts(params string[] components)` replaces the last N parts;
- line 122: `public AssetLocation CodeWithVariant(string type, string value)`;
- line 213: `public string LastCodePart(int posFromRight = 0)`.

Searching a volume: `SearchBlocks` loads the chunks once and indexes chunk data directly (`BlockAccessorBase.cs:396-437`). Bounds are **inclusive** and clamped to the map. **It reuses one `BlockPos` instance for every callback**, so call `pos.Copy()` before storing it. It reads the solid layer. For "find hammocks near me", use the POI registry instead of scanning.

Face solidity: `block.SideSolid[facing.Index]` or `block.SideSolid.OnSide(facing)` (`VSAPI/Datastructures/SmallBoolArray.cs:42, 84`), `blockAccessor.IsSideSolid(x, y, z, facing)`, or, for attachment, `support.CanAttachBlockAt(ba, myBlock, supportPos, faceOfSupport, null)` (honours behaviours).

**Tree logs.** Grown trunks are `game:log-grown-{wood}-{ud|ns|we}` (`ASSETS/blocktypes/wood/woodtyped/log.json`, `class: "BlockLog"`, `variantgroups type: grown|placed`, `blockmaterial: "Wood"`, `requiredMiningTier: 1`, attribute `treeFellingGroupCodeByType: { "log-grown-*": "{wood}" }`). `public class BlockLog : Block, IBlockLog, ICustomChiselMaterialName` (`VSS/Block/BlockLog.cs:12`), and `IBlockLog` is an empty marker interface (`VSE/Systems/WorldGen/Standard/IBlockLog.cs:7-9`). The gotchas:

- Resin logs `log-resin-{pine,acacia}-ud` / `log-resinharvested-*` (`log-withresin.json`, also code `"log"`) **have no `class`, so they are plain `Block`, not `BlockLog`**. They do carry `treeFellingGroupCode`.
- Cypress `lognarrow-grown-cypress-*` has no class and `sidesolid: { all: false }` (`log-narrow.json:26-28`), so **nothing can attach to it with the default `CanAttachBlockAt`**.
- Redwood uses `logsection-*` with `class: "BlockLogSection"` (`VSS/Block/BlockLogSection.cs:8`, `: Block`, not BlockLog).
- Big-tree quad logs `logquad-*` have no class.
- A robust trunk test: `block.BlockMaterial == EnumBlockMaterial.Wood && block.Attributes?["treeFellingGroupCode"].Exists == true` (attribute present on grown, resin, narrow and section logs). Alternatively `block is BlockLog && block.Variant["type"] == "grown"` for ordinary trunks only. `BEMycelium` uses `block is BlockLog` (`BEMycelium.cs:307`).

**Leaves.** Codes and collision:

| Block | Code | Class | Collision | Can stand on / attach to? |
|---|---|---|---|---|
| `leaves/normal.json` | `leaves-{grown..grown7,placed}-{wood}` | `BlockLeaves` | `collisionbox: null` (121), `sidesolid all false` (108-110) | **No**: entities fall through and `CanAttachBlockAt` is false |
| `leaves/narrow.json` | `leavesnarrow-*` | `BlockLeavesNarrow : BlockLeaves` | `collisionbox: null` (79), sidesolid false (66) | No |
| `leaves/bamboo.json` | `bambooleaves-*` | `BlockWithLeavesMotion` | `collisionbox: null` (67) | No |
| `leaves/branchy.json` | `leavesbranchy-{grown..,placed}-{wood}` | `BlockLeaves` | **no `collisionbox` and no `sidesolid` key**, so defaults apply: full cube (`DefaultCollisionBoxR`, `VSE/Loading/BlockType.cs:353`) and all sides solid (Block.cs:289) | **Yes** (derived from defaults, not runtime-tested) |
| `leaves/branchy-birch-static.json` | `leavesbranchystatic-grown-birch` | `Block` | (not inspected in detail) | |
| `leaves/fallen.json` | `fallenleaves` | | `collisionbox: null` | |

`BlockLeaves` / `BlockWithLeavesMotion` do not override collision or attachment (only colour maps, ticks, `OnPickBlock`, `DisplacesLiquids`, tessellation: `VSS/Block/BlockLeaves.cs:17-65`, `BlockWithLeavesMotion.cs:16, 56`). All leaves are `blockmaterial: "Leaves"`. Treegen configs name a branchy-leaves block (`leavesBranchyBlockCode`, e.g. `ASSETS/worldgen/treegen/acacia.json:84`).

---

## 9. Mod configuration

`ICoreAPICommon` (`VSAPI/Common/API/ICoreAPI.cs`):

```csharp
void StoreModConfig<T>(T jsonSerializeableData, string filename);   // line 146
void StoreModConfig(JsonObject jobj, string filename);              // line 154
T LoadModConfig<T>(string filename);                                // line 164
JsonObject LoadModConfig(string filename);                          // line 173
```

Implementation (`LIB/Vintagestory.Common/APIBase.cs:94-125`):

- the path is `Path.Combine(GamePaths.ModConfig, filename)`, with `GamePaths.ModConfig => Path.Combine(DataPath, "ModConfig")` (`VSAPI/Config/GamePaths.cs:45`);
- loading is Newtonsoft `JsonConvert.DeserializeObject<T>(File.ReadAllText(path))` and returns `default` (**null**) if the file is missing;
- storing is `JsonConvert.SerializeObject(obj, Formatting.Indented)` and creates the folder.

The documentation says "Be aware that these configs are not synchronized between client and server". It also recommends try/catch around load.

Standard pattern (compiled):

```csharp
public class SpiderConfig { public int TwineMin = 2; public int TwineMax = 4; public float ShearsSpeedMul = 4f; }

public override void Start(ICoreAPI api)
{
    try { Config = api.LoadModConfig<SpiderConfig>("jumpingspiders.json") ?? new SpiderConfig(); }
    catch (Exception e) { Mod.Logger.Error("Bad config, using defaults: {0}", e); Config = new SpiderConfig(); }
    api.StoreModConfig(Config, "jumpingspiders.json");   // writes defaults / adds new fields
}
```

Values that matter on both sides (e.g. the shears speed multiplier used in the client-side `OnGettingBroken`) are read from each machine's own file unless the mod syncs them over the network. On a multiplayer server, the client's local file decides break speed. Vanilla usage: `VSS/Systems/Handbook/Tutorial/TutorialBase.cs:171, 178`.

---

## Surprises and pitfalls

1. **Shears have no mining speed in 1.22.7.** `shears.json` has no `miningspeed`, so shears mine every block at bare-hand speed. Only leaves get the 5-block multi-break. Breaking a non-leaf block with shears costs no durability (`ItemShears.OnBlockBrokenWith` skips base and `DamageItem`). "Fastest with shears" needs code (`OnGettingBroken` override or `GetMiningSpeedModifier` behaviour) or a JSON patch on the shears. Use material `Cloth` or `Other` so knife, axe and saw have no listed speed.
2. **The trap ignores `foodCategories`.** It checks `foodTags` only, with weight >= 0.6 (`foodTagMinWeight`). A diet like `foodCategories: ["Protein"]` will never be trapped. Use `weightedFoodTags: [{ code: "meat", weight: >= 0.6 }]` at the entity's top-level `attributes`.
3. **Trapping needs the vanilla `seekfoodandeat(-r)` task** with `doConsumePortion` true. A custom hunting AI never triggers traps unless it calls `IAnimalFoodSource.ConsumeOnePortion`. `trappable.small` merely *existing* lets the trap lure the animal, even with `trapChance: 0`.
4. **Capture keeps attributes but changes `EntityId`.** `WatchedAttributes` (generation etc.) and `Attributes` survive, `origin` becomes `"playerplaced"`, and `SpawnEntity` assigns a new id. Don't link hammocks to spiders by entity id alone; store a stable custom id in the spider's attributes. The animal dies after 2 in-game days in the basket.
5. **`Block.OnBlockBroken` also runs on the breaking client** (`ClientMain.cs:1836`). Guard aggression and spawning with `world.Side == EnumAppSide.Server`, as `BlockSkep` does. The BE is destroyed inside `base.OnBlockBroken`, so read its data first.
6. **`byPlayer` is often null.** Support loss (`HorizontalAttachable` calls `BreakBlock(pos, null)`), tree felling (the hammock breaks via neighbour update, not by the axe-wielder), explosions, and animals "eating" the block all pass null. The "aggressive toward the player who broke it" rule only applies to direct breaks.
7. **`HorizontalAttachable` details:** the variant names the direction **to the support**; `side` must be the **last** code part; `handleDrops` defaults to **true** and replaces your drops with the block itself (set `handleDrops: false`). Normal leaves, narrow cypress logs and anything with `sidesolid: false` can't support it, but branchy leaves can (defaults). Resin logs are not `BlockLog`. Use `treeFellingGroupCode` / material Wood to detect trunks.
8. **POI registry bugs:** `WalkPois` iterates `cx < maxcx` and `cz < maxcz` (exclusive, `POIRegistry.cs:174-176`), so it **skips the last chunk column and visits nothing when the search area sits inside a single chunk column**. Use `GetNearestPoi`, which uses `<=`. `GetNearestPoi` and `GetWeightedNearestPoi` call `matcher(pois[i])` without a null check, so always pass a matcher. The registry is server-only and not persisted. Remove the POI on both remove and unload, and keep `Position` constant.
9. **`SearchBlocks` reuses its `BlockPos`.** Copy it before storing. `SearchBlocks` and `WalkBlocks` are not dimension-aware. `SetBlock` does not fire neighbour updates (`BreakBlock` does).
10. **Mod JSON domains:** item codes in a mod's block JSON default to the **mod** domain (`"game:flaxtwine"`, `"game:redmeat-raw"` need the prefix), but `loadFromProperties: "abstract/horizontalorientation"` resolves to `game:` without one.
11. **Class names are global and silently overwritten.** Prefix with the modid. Some block entity names are reserved and throw.
12. **Raw versus cooked meat:** `foodTags: ["meat"]` is on all variants of redmeat, poultry and bushmeat, so cooked and cured meat are equally good bait. Spiders with `eatLooseItems` (default true) will also eat meat dropped on the ground.
13. **2 to 4 drops** via `{avg: 3, var: 1}` gives 25/50/25 %, never outside 2..4. A uniform distribution needs a `GetDrops` override.
14. **Reaching high hammocks is hard with the stock pathfinder.** `AStar` only walks and steps up (step height) or falls. It doesn't plan climbing (AStar.cs:180-330). Vanilla climbing exists only as physics (`Block.OnEntityCollide` with `CanClimb` / `CanClimbAnywhere`, Block.cs:1598-1600 (method at 1598); used by locusts, `ASSETS/entities/lore/locust.json:8`). A spider walking to a hammock on a trunk side needs custom movement such as jumping, climbing or teleport-like leaps. `NavigateTo_Async` alone will report stuck or no-path. I did not verify this in-game; it is inferred from the AStar code.
15. **Hen-box occupancy is in-memory only.** `occupier` is an `Entity` field and is not saved. A hammock that tracks its sleeping spider has to persist something itself, and must re-validate after load and capture/release (pitfall 4).
16. The project's `modinfo.json` declares `"dependencies": { "game": "1.21.0" }` (in `/home/user/JumpingSpiders/JumpingSpiders/modinfo.json`), even though the target is 1.22.7.

### Not verified

- Runtime behaviour was not tested in a running game: branchy-leaf standability, the exact release-time behaviour re-initialisation, and the climb limitation are all derived from code and JSON.
- Whether the vanilla seek and melee tasks, after `TryTriggerState("aggressiveondamage", playerEntityId)`, target that specific player rather than any matching entity (this belongs to the AI-task research).
- How the attribute `canStep` (baskettrap.json:12) is mapped to `Block.CanStep`.
- `branchy-birch-static.json` collision was not inspected line by line.

# Relic System and Randomized Relic-Stat Generator

## Objective
Add a data-driven relic system where each relic definition is a template, each owned relic carries its own deterministic-random stat roll, and three stat-scaling relics (Loaded Dice, Clockwork Core, Boulder Shield) exercise the combat integration.

## Changes
- `Assets/Scripts/Items/RelicDefinition.cs` - (create) `RelicDefinition : ItemData` template + `RelicRarity`, `RelicConcentration`, `RelicEffect`, `RelicOverlapRule` enums
- `Assets/Scripts/Items/RelicInstance.cs` - (create) `RelicInstance` (the roll) + `RelicScaling` (base + stat x rate)
- `Assets/Scripts/Items/RelicSettings.cs` - (create) per-tier budgets, concentration weights/profiles, negative chance, overlap rule
- `Assets/Scripts/Items/RelicGenerator.cs` - (create) seedable roll `System.Random`: budgets -> concentration -> affinity-weighted distribution -> merge
- `Assets/Scripts/Items/RelicEffects.cs` - (create) static helpers: Loaded Dice chance, Clockwork Core chance, Boulder Shield block
- `Assets/Scripts/Items/ItemData.cs` - `ItemInstance` gains `relic` roll + instance-aware `BonusSummary()`
- `Assets/Scripts/Items/Inventory.cs` - `Add(ItemInstance)` overload; `Add(ItemData)` rolls relics; `GetBonuses` sums template + roll
- `Assets/Scripts/Core/GameSession.cs` - shop racks hold `ItemInstance`; `FillRack` rolls relics; `BuyShopItem(ItemInstance)`
- `Assets/Scripts/UI/ShopPanel.cs`, `ShopEntryView.cs`, `ItemRowView.cs` - bind/display the rolled instance
- `Assets/Scripts/Combat/CombatManager.cs` - 3 hooks (conjure, construct fire, turn start)
- `Assets/Editor/CardGameSceneBuilder.cs` - author relic templates into `Assets/GameData/Relics/` + `RelicSettings.asset`
- `Assets/Editor/RelicProbe.cs` - (create) seeded generate 1/100/1000 + distribution report

## Relevant Assets and Quirks
- **Enum order is the indexing contract.** `RelicRarity { TierIII = 0, TierII = 1, TierI = 2, Unique = 3 }` - ascending power, so `(int)rarity` indexes the per-tier settings array directly and the array reads in design order. It also makes `default(RelicRarity)` the **weakest** tier, so a relic authored without an explicit tier is weak rather than accidentally Unique-strength. This reverses the earlier "Unique = 0" reading deliberately. Values are pinned explicitly so inserting a future tier cannot silently re-map existing serialized assets, and every lookup still bounds-checks.
- **Tier I is the strongest tier**, not the weakest: Tier I gets guaranteed stats and the 6-12 budget; Tier III is 1-3.
- **`ItemRarity` is not touched.** `RelicDefinition.OnValidate()` mirrors the tier onto the inherited `rarity` (`Rare` for `TierI`/`Unique`, `Common` for `II`/`III`), so `ItemRarityStyle` colour and `ShopPricing.ItemPrice` keep working with zero edits to those files, and a designer only ever sets the tier. `OnValidate` runs on ScriptableObjects in the inspector and the builder sets the same value, so headless authoring is correct too. The true tier is also shown as text (`UNIQUE`, `TIER I`).
- `ItemData.GetBonus(StatType)` (line 48) already maps a stat to a flat value - reuse it verbatim for **guaranteed stats**, so a Tier I relic's `strength = 5` is guaranteed on top of the random budget.
- **Relics are charms**: `slot = Carried`, so `Inventory.ActiveItems()` (line 179) picks them up from the bag with no change.
- `Inventory.Add(ItemData)` (line 67) constructs a bare `ItemInstance`. If the roll is not created here, relics granted by `EventEffect.grantItem` / `randomItemPool` / `db.startingItems` arrive with **zero stats silently**. Centralise the roll in `Inventory.Add`.
- The shop must roll **before** display, so `ShopRelics` becomes `IReadOnlyList<ItemInstance>`; the single consumer is `ShopPanel` line 124.
- `ItemInstance.slotIndex` means "equipped" (`>= 0`). A relic on the shop rack is not yet owned, so its instance stays at `-1` and must not be inserted into `Inventory` until purchase.
- Conventions: cards resolve a produced value with `Mathf.FloorToInt` (`CardData.Finish`, line 225) and per-stat-point maths with `FloorToInt(stat * per)` (`CombatManager` line 1098). Use that same `FloorToInt` for Boulder Shield's `STR / 3`; do not invent a rounding rule.
- Scaling reads `PlayerStats.GetStat` (permanent + item), matching the gold/permanent-change precedent. Note a relic's own rolled stat feeds its own scaling - intended, but worth knowing when tuning.
- Combat choke points: `CombatManager.GenerateRandomCard()` (line 948) is the only place random cards are conjured; `FireBoard()` (line 787) is the construct pass; `BeginPlayerTurn()` sets `Stats.Block = 0` at line 761, so Boulder Shield is granted **after** that or it is wiped.
- `RelicSettings` is loaded like `GameDatabase` (`Resources.Load<RelicSettings>("RelicSettings")`, cf. `GameSession` line 52) with a code fallback so the system works before the asset exists. `RelicSettings.asset` is created under `Assets/Resources/`.
- Repo has no assembly definitions, so `Assets/Editor` can reference the runtime types directly.

## Steps
- [x] 1. Create `RelicDefinition.cs`: the four enums with explicit pinned values, and `RelicDefinition : ItemData` carrying tier, positive/negative budget min-max, negative chance, positive/negative affinity weights per `StatType`, concentration weights, `RelicEffect`, `RelicScaling`, plus the `OnValidate` rarity mirror. Also create `RelicInstance.cs` with `RelicInstance` (signed per-stat values, `TierLabel()`, `DescribeLines()`) and `RelicScaling` resolving `(base + stat x perStatPoint) / divisor` with percentages clamped 0-100. [depends: none]
- [x] 2. Create `RelicSettings.cs`: `RelicTierSettings[] tiers` indexed by `(int)RelicRarity` (seed Tier I 6-12 / Tier II 5-10 / Tier III 1-3 positive budgets, negative ranges, per-tier negative chance) and `RelicConcentrationProfile[] concentrations` indexed by `(int)RelicConcentration` (`Even`/`Mixed`/`Focused`/`Extreme`, each with `maxStats`, `primaryShare`, `spillChance`), the `RelicOverlapRule`, and a bounds-checked `For(RelicRarity)` accessor so no caller indexes the array raw. [depends: 1]
- [x] 3. Create `RelicGenerator.cs`: `Roll(RelicDefinition, int seed)` using `System.Random` (not `UnityEngine.Random`) for reproducible rolls; roll positive and negative budgets separately from the tier settings, roll concentration independently for each budget via the configured weights, distribute each budget by affinity-weighted selection honouring the profile, apply the overlap rule (`Combine` sums to one signed value, `RedealNegatives` re-rolls to avoid collisions), then add guaranteed stats. `Debug.Log` each roll when verbose and `Debug.LogWarning` when a distribution cannot be satisfied. [depends: 1,2]
- [x] 4. Create `RelicEffects.cs` and wire the integration: add `public RelicInstance relic;` plus an instance-aware `BonusSummary()` to `ItemInstance`, and an `Inventory.Add(ItemInstance)` overload; make `Inventory.Add(ItemData)` roll via `RelicGenerator` when `data is RelicDefinition`; extend `Inventory.GetBonuses` to add `item.data.GetBonus(type)` plus the instance roll so `PlayerStats` needs no change. [depends: 3]
- [x] 5. Wire the three combat hooks in `CombatManager.cs`, each with explicit `Log(...)` output: Loaded Dice re-enters `GenerateRandomCard()` from inside itself (recursive, self-limiting, guarded by a generous depth cap - `LogWarning` when the guard trips, `Debug.LogError` if `RelicSettings` is missing); Clockwork Core is rolled once per construct per turn inside `FireBoard()`'s loop with a per-card per-turn re-fire cap; Boulder Shield grants `FloorToInt(STR / 3)` Block in `BeginPlayerTurn()` immediately after `Stats.Block = 0`, so it is once-per-turn by construction. All three re-check `CombatOver`/`PickActive` before acting. [depends: 4]
- [x] 6. Change the shop to stock rolled instances: `GameSession.shopRelics`/`shopEquipment` become `List<ItemInstance>`, `FillRack` rolls a `RelicInstance` for each relic template, `BuyShopItem(ItemInstance)` spends gold then calls `Inventory.Add(instance)` so the roll survives purchase; update `ShopPanel.FillItemRack` and `ShopEntryView.BindItem`/`ItemRowView.Refresh` to render the instance's combined signed stats and tier label so the roll is visible before buying. [depends: 5]
- [x] 7. Author content and the debug harness: add a `BuildRelics()` to `CardGameSceneBuilder` creating `RelicDefinition` assets in `Assets/GameData/Relics/` (Titan's Grip, Goblet of Power with guaranteed +5 Strength, Loaded Dice, Clockwork Core, Boulder Shield, plus one Tier II/III example) merged into `database.allItems`, create `Assets/Resources/RelicSettings.asset`, and add `RelicProbe.cs` with menu items generating 1 / 100 / 1000 seeded rolls and printing average positive and negative totals, concentration distribution, negative-roll frequency and the most common stat distributions. [depends: 3,6]

## Verify
- `check_compile_errors` reports no errors after each file lands.
- Run the `1000` roll probe: Tier I positive totals average inside 6-12 with guaranteed stats excluded, Tier III inside 1-3, negative frequency matches the configured per-tier chance, and the concentration histogram matches the configured weights. Re-run with the same seed and confirm byte-identical output.
- Change a relic's tier in the inspector and confirm its colour and shop price follow with no manual `ItemRarity` edit.
- In the shop, the three relics show distinct rolls and tier labels, negatives are visible rather than hidden, and buying one carries its exact roll into the character menu (attribute reading `base + item`).
- Grant a relic via an event/starting item and confirm it arrives **rolled**, not at zero stats.
- In combat: a Generate-Card card with Loaded Dice conjures extra cards and logs each recursion; Boulder Shield shows Block immediately after the turn starts (not wiped by the reset); Clockwork Core logs an extra construct trigger and stops at the cap.
- Confirm removing all relics restores current behaviour exactly (no relics equipped = no Loaded Dice lines, Block unchanged).

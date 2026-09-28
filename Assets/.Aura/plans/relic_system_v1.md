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
- **Tier I is the strongest tier**, not the weakest: Tier I gets guaranteed stats and the 6-12 budget; Tier III is 1-3. Encode as `RelicRarity { Unique = 0, TierI = 1, TierII = 2, TierIII = 3 }` per the user's "unique = rarity 0". `default(RelicRarity)` is therefore `Unique`, so the serialized field must initialise explicitly to `TierIII` and the generator must never index arrays by `(int)rarity`.
- **`ItemRarity` stays `{ Common, Rare }`** - not renumbered. `RelicDefinition.rarity` is set to `Rare` for `TierI`/`Unique` and `Common` for `II`/`III` so `ItemRarityStyle` colour and `ShopPricing.ItemPrice` keep working untouched. The real tier is shown as text (`UNIQUE`, `TIER I`).
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
- [ ] 1. Create `RelicDefinition.cs` (`RelicDefinition : ItemData` with tier, positive/negative budget min-max, negative chance, positive/negative affinity weights per `StatType`, concentration weights, `RelicEffect`, `RelicScaling`) and `RelicInstance.cs` (`RelicInstance` holding signed per-stat values + `TierLabel()` + `DescribeLines()`, and `RelicScaling` resolving `(base + stat x perStatPoint) / divisor` with percent clamped 0-100). [depends: none]
- [ ] 2. Create `RelicSettings.cs`: a `[Serializable] RelicTierSettings` per tier (explicit `tierI`/`tierII`/`tierIII`/`unique` fields, never array-indexed) seeding Tier I 6-12 / Tier II 5-10 / Tier III 1-3 positive, negative ranges and chance per tier, plus four `RelicConcentrationProfile` entries (`Even`/`Mixed`/`Focused`/`Extreme` each with `maxStats`, `primaryShare`, `spillChance`) and the `RelicOverlapRule`. [depends: 1]
- [ ] 3. Create `RelicGenerator.cs`: `Roll(RelicDefinition, int seed)` using `System.Random` (not `UnityEngine.Random`) for reproducible rolls; roll positive and negative budgets separately from the tier settings, roll concentration independently for each budget via the configured weights, distribute each budget by affinity-weighted selection honouring the profile, apply the overlap rule (`Combine` sums to one signed value, `RedealNegatives` re-rolls to avoid collisions), then add guaranteed stats. `Debug.Log` each roll when a verbose flag is set and `Debug.LogWarning` when a distribution cannot be satisfied. [depends: 1,2]
- [ ] 4. Create `RelicEffects.cs` and wire the integration: add `public RelicInstance relic;` plus `BonusSummary()` to `ItemInstance`, and an `Inventory.Add(ItemInstance)` overload; make `Inventory.Add(ItemData)` roll via `RelicGenerator` when `data is RelicDefinition`; extend `Inventory.GetBonuses` to add `item.data.GetBonus(type)` plus the instance roll so `PlayerStats` needs no change. [depends: 3]
- [ ] 5. Wire the three combat hooks in `CombatManager.cs`, each with explicit `Log(...)` output: Loaded Dice re-enters `GenerateRandomCard()` from inside itself (recursive, self-limiting, guarded by a generous depth cap - `LogWarning` when the guard trips, `Debug.LogError` if `RelicSettings` is missing); Clockwork Core is rolled once per construct per turn inside `FireBoard()`'s loop with a per-card per-turn re-fire cap; Boulder Shield grants `FloorToInt(STR / 3)` Block in `BeginPlayerTurn()` immediately after `Stats.Block = 0`, so it is once-per-turn by construction. All three re-check `CombatOver`/`PickActive` before acting. [depends: 4]
- [ ] 6. Change the shop to stock rolled instances: `GameSession.shopRelics`/`shopEquipment` become `List<ItemInstance>`, `FillRack` rolls a `RelicInstance` for each relic template, `BuyShopItem(ItemInstance)` spends gold then calls `Inventory.Add(instance)` so the roll survives purchase; update `ShopPanel.FillItemRack` and `ShopEntryView.BindItem`/`ItemRowView.Refresh` to render the instance's combined signed stats and tier label so the roll is visible before buying. [depends: 5]
- [ ] 7. Author content and the debug harness: add a `BuildRelics()` to `CardGameSceneBuilder` creating `RelicDefinition` assets in `Assets/GameData/Relics/` (Titan's Grip, Goblet of Power with guaranteed +5 Strength, Loaded Dice, Clockwork Core, Boulder Shield, plus one Tier II/III example) merged into `database.allItems`, create `Assets/Resources/RelicSettings.asset`, and add `RelicProbe.cs` with menu items generating 1 / 100 / 1000 seeded rolls and printing average positive and negative totals, concentration distribution, negative-roll frequency and the most common stat distributions. [depends: 3,6]

## Verify
- `check_compile_errors` reports no errors after each file lands.
- Run the `1000` roll probe: Tier I positive totals average inside 6-12 with guaranteed stats excluded, Tier III inside 1-3, negative frequency matches the configured per-tier chance, and the concentration histogram matches the configured weights. Re-run with the same seed and confirm byte-identical output.
- In the shop, the three relics show distinct rolls and tier labels, negatives are visible rather than hidden, and buying one carries its exact roll into the character menu (attribute reading `base + item`).
- Grant a relic via an event/starting item and confirm it arrives **rolled**, not at zero stats.
- In combat: a Generate-Card card with Loaded Dice conjures extra cards and logs each recursion; Boulder Shield shows Block immediately after the turn starts (not wiped by the reset); Clockwork Core logs an extra construct trigger and stops at the cap.
- Confirm removing all relics restores current behaviour exactly (no relics equipped = no Loaded Dice lines, Block unchanged).

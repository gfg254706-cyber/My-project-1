# Remove the Hollow Crown Relic

## Objective
Delete the Unique relic "Hollow Crown" from the project so it can no longer appear on the shop relic rack, leaving the rest of the relic system untouched.

## Changes
- 'Assets/Editor/CardGameSceneBuilder.cs' - delete the Hollow Crown block from `BuildRelics()`; print the surviving relic roster in the completion log of `BuildGameScenes()`
- 'Assets/GameData/Relics/HollowCrown.asset' - (delete) the Unique relic definition asset, plus its `.meta`
- 'Assets/Resources/GameDatabase.asset' - untouched by hand; its `allItems` list is rebuilt by the builder, which drops the stale reference (guid `6ca6489cbacc16f4e850f5d85c1fe99d`)

## Relevant Assets and Quirks
- Hollow Crown is the **only** `RelicRarity.Unique` relic. It is authored in code at `CardGameSceneBuilder.cs:897`, not rolled: guaranteed Intellect +4 and Strength −3, a custom budget (positive 8–14, negative 2–6 at 85% chance), Intellect affinity 8.
- It reuses `RelicEffect.ClockworkCore` — the same effect "Clockwork Core" carries, at a stronger curve (15% + 0.4/Intellect versus 10% + 0.2). `RelicEffects.ClockworkCoreChance` sums `RelicEffect.ClockworkCore` across carried relics, so removal only drops one contributing source; Clockwork Core itself is unaffected.
- It is **not** guaranteed to appear in the shop. `GameSession.FillRack` (line 847) draws 3 distinct entries uniformly from every `relic == true` item — the 7 `RelicDefinition`s plus Warded Sigil, Bloodstone, Hunter's Talon and Twin Fang Totem — so roughly a 1-in-4 chance per shop room. No code special-cases it.
- `BuildDatabase()` reassigns `database.allItems = new List<ItemData>(items)` wholesale, so a builder re-run is what purges the database reference. Without one, `Assets/Resources/GameDatabase.asset` line 88 would still point at the deleted asset and Unity would report a missing reference.
- `RelicRarity.Unique`, its `RelicSettings` tier table entry and `RelicProbe.TierOrder()` all stay valid with no Unique relic authored: the probe does `if (!tiers.TryGetValue(rarity, out tally)) continue;`, so the UNIQUE block simply stops printing. No code change needed there.
- `CardGameSceneBuilder` is the single source of truth for content, prefabs and the six scenes. Deleting the asset alone would let the next `Build Game Scenes` resurrect the relic, and any hand-edit to `Assets/Scenes/*.unity` or `Assets/Prefabs/*.prefab` is overwritten by that same run.
- `EnsureRelicSettings()` deliberately never overwrites `Assets/Resources/RelicSettings.asset` (live balance data), so the Unique tier's tuning survives this change untouched.

## Steps
- [ ] 1. In `BuildRelics()` (`Assets/Editor/CardGameSceneBuilder.cs`, the `// --- Unique:` comment through `relics.Add(crown);`, lines 896-927) delete the entire Hollow Crown block, leaving the `foreach` tidy loop and `return relics;` intact [depends: none]
- [ ] 2. Append the surviving relic names and count to the completion `Debug.Log` in `BuildGameScenes()` so every rebuild prints the exact relic roster it produced [depends: 1]
- [ ] 3. Delete `Assets/GameData/Relics/HollowCrown.asset` and `Assets/GameData/Relics/HollowCrown.asset.meta` [depends: 1]
- [ ] 4. Run `Tools > Dungeon Cards > Build Game Scenes` so `BuildDatabase()` rewrites `allItems` without the crown and all six scenes and prefabs are regenerated consistently [depends: 2,3]
- [ ] 5. Verify against the live database: enumerate every `RelicDefinition` in `GameDatabase.allItems`, log each name, and `Debug.LogError` if any is named "Hollow Crown" or if the roster is not 6 [depends: 4]

## Verify
- Console after the rebuild shows no compile errors, and the completion log lists six relics with no "Hollow Crown".
- `Assets/GameData/Relics` holds six assets; `Assets/Resources/GameDatabase.asset` has 22 `allItems` entries and no guid `6ca6489cbacc16f4e850f5d85c1fe99d`.
- `Tools > Dungeon Cards > Relics > Generate 100` prints six relics and no UNIQUE tier row.
- In play, a shop room's relic rack never offers Hollow Crown, while carrying Clockwork Core still produces its "Clockwork Core triggers ... again" log line in combat.

using System.Collections.Generic;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// Reads the relics that do something rather than only moving attributes.
    ///
    /// Every number here is resolved against the player's live attributes, so a relic's effect grows with
    /// the build carrying it and the same relic is worth different amounts in different hands. A relic
    /// with no effect, and a player carrying none, both come back as zero, so having no relics equipped
    /// costs nothing and changes nothing.
    ///
    /// Everything is read at the moment it is asked for rather than cached. The character menu can be
    /// opened mid fight and an item sold out of the bag, so a cached relic would go on paying out after
    /// it had left the player's hands.
    /// </summary>
    public static class RelicEffects
    {
        /// <summary>
        /// Every relic the player is currently working, which for a relic means every one in the bag:
        /// relics are charms and need no equipment slot.
        /// </summary>
        public static IEnumerable<ItemInstance> ActiveRelicItems()
        {
            GameSession session = GameSession.Instance;
            if (session == null || session.Inventory == null) yield break;

            foreach (ItemInstance item in session.Inventory.ActiveItems())
            {
                if (item == null) continue;
                if (!(item.data is RelicDefinition)) continue;

                yield return item;
            }
        }

        /// <summary>How many relics with the given effect the player has working.</summary>
        public static int CountEffect(RelicEffect effect)
        {
            int count = 0;

            foreach (ItemInstance item in ActiveRelicItems())
            {
                var definition = item.data as RelicDefinition;
                if (definition != null && definition.effect == effect) count++;
            }

            return count;
        }

        /// <summary>
        /// The first relic the player carries that does this thing, or null when none of them does. The
        /// relic is returned rather than a number so the fight can read the count it waits for, the second
        /// payout it hands over, and the attribute it designates, all from the same asset.
        /// </summary>
        public static RelicDefinition Definition(RelicEffect effect)
        {
            foreach (ItemInstance item in ActiveRelicItems())
            {
                var definition = item.data as RelicDefinition;
                if (definition != null && definition.effect == effect) return definition;
            }

            return null;
        }

        /// <summary>True when the player is carrying a relic that does this thing.</summary>
        public static bool Has(RelicEffect effect)
        {
            return Definition(effect) != null;
        }

        /// <summary>
        /// The effect's own number at the player's attributes, as a whole number of points. Zero when the
        /// relic is not carried, so a caller can add this without asking whether it should.
        /// </summary>
        public static int Value(RelicEffect effect, PlayerStats stats)
        {
            RelicDefinition definition = Definition(effect);
            return definition != null ? definition.scaling.ResolveInt(stats) : 0;
        }

        /// <summary>The count the effect waits for or spends. Zero when the relic is not carried.</summary>
        public static int Threshold(RelicEffect effect)
        {
            RelicDefinition definition = Definition(effect);
            return definition != null ? definition.threshold : 0;
        }

        /// <summary>The effect's second payout. Zero when the relic is not carried or has only one.</summary>
        public static int Secondary(RelicEffect effect)
        {
            RelicDefinition definition = Definition(effect);
            return definition != null ? definition.secondaryMagnitude : 0;
        }

        /// <summary>
        /// True while the player carries The Broken Oath. Willpower is held at zero without it, and the
        /// relic is the only thing that lifts that floor, which is what a negative Willpower build is.
        /// </summary>
        public static bool BrokenOathActive { get { return Has(RelicEffect.BrokenOath); } }

        /// <summary>
        /// Loaded Dice: the chance that one conjured card turns up a further one. Summed across every
        /// Loaded Dice carried and clamped, so two of them are more likely than one but never certain.
        /// </summary>
        public static float LoadedDiceChance(PlayerStats stats)
        {
            return SumPercent(RelicEffect.LoadedDice, stats);
        }

        /// <summary>Clockwork Core: the chance that a construct fires a second time in the same turn.</summary>
        public static float ClockworkCoreChance(PlayerStats stats)
        {
            return SumPercent(RelicEffect.ClockworkCore, stats);
        }

        /// <summary>
        /// Boulder Shield: the shielding granted at the start of the turn, summed across every Boulder
        /// Shield carried. Rounded the way a card rounds a calculated number, so a third of a point of
        /// Strength is worth nothing rather than being carried forward.
        /// </summary>
        public static int BoulderShieldBlock(PlayerStats stats)
        {
            int total = 0;

            foreach (ItemInstance item in ActiveRelicItems())
            {
                var definition = item.data as RelicDefinition;
                if (definition == null || definition.effect != RelicEffect.BoulderShield) continue;

                total += definition.scaling.ResolveInt(stats);
            }

            return total;
        }

        /// <summary>
        /// The sources of one effect, named, so a log line can say which relic paid out rather than
        /// attributing it to the player.
        /// </summary>
        public static string DescribeSources(RelicEffect effect, PlayerStats stats)
        {
            var parts = new List<string>();

            foreach (ItemInstance item in ActiveRelicItems())
            {
                var definition = item.data as RelicDefinition;
                if (definition == null || definition.effect != effect) continue;

                parts.Add(definition.itemName + " (" + definition.scaling.Resolve(stats).ToString("0.#") + "%)");
            }

            return parts.Count == 0 ? "no relic" : string.Join(", ", parts.ToArray());
        }

        static float SumPercent(RelicEffect effect, PlayerStats stats)
        {
            float total = 0f;

            foreach (ItemInstance item in ActiveRelicItems())
            {
                var definition = item.data as RelicDefinition;
                if (definition == null || definition.effect != effect) continue;

                total += definition.scaling.Resolve(stats);
            }

            return Mathf.Clamp(total, 0f, 100f);
        }
    }
}

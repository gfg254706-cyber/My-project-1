using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// Rolls a relic's stats. The settings it reads come from RelicSettings, which holds the per-tier
    /// budgets and the concentration odds so none of that is written into this file.
    ///
    /// Rolls a relic's stats.

    ///
    /// A roll is three separate decisions: how big the positive package is, how big the negative package
    /// is, and how tightly each one is packed. Those are rolled independently, so the same template can
    /// produce +8 STR / +1 INT one time and a flat spread the next, and the negative package can fall on
    /// a stat the positive one also raised.
    ///
    /// Nothing here judges a result. A negative stat is part of the loot, not a mark against the relic:
    /// a build can be aiming at it, so the generator's job is to produce the roll the tables describe
    /// rather than the roll it thinks the player wants.
    ///
    /// Every roll takes a seed and uses System.Random rather than UnityEngine.Random, so a roll can be
    /// reproduced exactly while debugging instead of only described after the fact.
    /// </summary>
    public static class RelicGenerator
    {
        /// <summary>A seed for a roll nobody has to reproduce.</summary>
        public static int RandomSeed()
        {
            return UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        }

        public static RelicInstance Roll(RelicDefinition definition)
        {
            return Roll(definition, RandomSeed());
        }

        public static RelicInstance Roll(RelicDefinition definition, int seed)
        {
            return Roll(definition, seed, RelicSettings.Instance);
        }

        /// <summary>
        /// Rolls one relic. Guaranteed stats are added after the packages have met, so a Tier I relic's
        /// guaranteed Strength is independent of whatever the random budget did.
        /// </summary>
        public static RelicInstance Roll(RelicDefinition definition, int seed, RelicSettings settings)
        {
            var instance = new RelicInstance();
            instance.EnsureSize();

            if (definition == null) return instance;

            instance.seed = seed;
            instance.tier = definition.tier;

            if (settings == null) settings = RelicSettings.Instance;
            settings.EnsureConfigured();

            definition.EnsureWeights();

            RelicTierSettings tier = settings.For(definition.tier);
            var rng = new System.Random(seed);

            // --- the two budgets, rolled separately -------------------------------------------------
            int positiveBudget = definition.useCustomBudget
                ? RollRange(rng, definition.positiveMin, definition.positiveMax)
                : RollRange(rng, tier.positiveMin, tier.positiveMax);

            int negativeMin = definition.useCustomNegativeBudget ? definition.negativeMin : tier.negativeMin;
            int negativeMax = definition.useCustomNegativeBudget ? definition.negativeMax : tier.negativeMax;
            float negativeChance = definition.negativeChance >= 0f ? definition.negativeChance : tier.negativeChance;

            int negativeBudget = 0;
            if (negativeMax > 0 && negativeChance > 0f && rng.NextDouble() < negativeChance)
            {
                negativeBudget = RollRange(rng, negativeMin, negativeMax);
            }

            instance.positiveTotal = positiveBudget;
            instance.negativeTotal = negativeBudget;

            // --- the shape of each, rolled separately ------------------------------------------------
            // Separate concentration rolls are what make +8 STR / +1 INT / -3 AGI reachable: the positive
            // package can be Focused while the negative one is Even.
            instance.positiveConcentration = RollConcentration(rng, settings.ConcentrationWeights(definition, true));
            instance.negativeConcentration = RollConcentration(rng, settings.ConcentrationWeights(definition, false));

            // --- distribution ------------------------------------------------------------------------
            Distribute(rng, positiveBudget, definition.positiveAffinity,
                       settings.ProfileFor(instance.positiveConcentration), definition, instance.positiveAllocation);

            if (negativeBudget > 0)
            {
                Distribute(rng, negativeBudget, definition.negativeAffinity,
                           settings.ProfileFor(instance.negativeConcentration), definition, instance.negativeAllocation);
            }

            // --- the overlap rule, applied before anything is combined --------------------------------
            if (settings.overlapRule == RelicOverlapRule.RedealNegatives && positiveBudget > 0 && negativeBudget > 0)
            {
                RedealOverlaps(rng, definition, instance);
            }

            // --- guaranteed stats, then the final signed value ----------------------------------------
            // The guaranteed fields are read with GetBonus so the inherited item fields stay the single
            // place a guaranteed stat is authored.
            for (int i = 0; i < RelicInstance.StatCount; i++)
            {
                var type = (StatType)i;
                instance.stats[i] = definition.GetBonus(type)
                                    + instance.positiveAllocation[i]
                                    - instance.negativeAllocation[i];
            }

            if (settings.verboseRolls) Debug.Log(Describe(definition, instance));

            return instance;
        }

        // ------------------------------------------------------------------ budgets and shape

        static int RollRange(System.Random rng, int min, int max)
        {
            // A range authored the wrong way round is read as intended rather than returning nonsense.
            if (max < min)
            {
                int swap = min;
                min = max;
                max = swap;
            }

            if (max <= 0) return 0;
            if (min < 0) min = 0;

            return rng.Next(min, max + 1);
        }

        /// <summary>Picks a concentration level against the configured odds.</summary>
        static RelicConcentration RollConcentration(System.Random rng, float[] weights)
        {
            if (weights == null || weights.Length != 4) return RelicConcentration.Mixed;

            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += Mathf.Max(0f, weights[i]);
            if (total <= 0f) return RelicConcentration.Mixed;

            double roll = rng.NextDouble() * total;
            float running = 0f;

            for (int i = 0; i < weights.Length; i++)
            {
                float weight = Mathf.Max(0f, weights[i]);
                if (weight <= 0f) continue;

                running += weight;
                if (roll < running) return (RelicConcentration)i;
            }

            return RelicConcentration.Mixed;
        }

        // ------------------------------------------------------------------ distribution

        /// <summary>
        /// Spreads one budget over the stats.
        ///
        /// The concentration profile decides how many stats the budget may touch and how much of it the
        /// first one takes; the affinity weights decide which stats those are. The weights are a bias and
        /// never a rule, so a Strength relic can still turn up an odd hybrid spread.
        /// </summary>
        static void Distribute(System.Random rng, int budget, float[] affinity, RelicConcentrationProfile profile,
                               RelicDefinition definition, int[] into)
        {
            if (budget <= 0 || into == null) return;

            if (profile == null) profile = RelicSettings.Instance.ProfileFor(RelicConcentration.Mixed);
            if (affinity == null || affinity.Length != RelicInstance.StatCount) affinity = RelicDefinition.EvenWeights();

            // How many stats this roll opens. Rolled before anything is placed, because a budget cannot
            // fill more stats than it has points.
            int maxStats = Mathf.Clamp(profile.maxStats, 1, RelicInstance.StatCount);
            int slots = 1;
            while (slots < maxStats && rng.NextDouble() < profile.spillChance) slots++;
            slots = Mathf.Min(slots, budget);

            List<int> picked = PickDistinct(rng, affinity, slots, definition);
            if (picked.Count == 0) return;

            // The first stat takes its share, but never so much that the stats opened beside it get
            // nothing: one point is held back for each of them.
            int primaryAmount = Mathf.Clamp(Mathf.RoundToInt(budget * Mathf.Clamp01(profile.primaryShare)), 1, budget);
            primaryAmount = Mathf.Min(primaryAmount, budget - (picked.Count - 1));

            into[picked[0]] += primaryAmount;
            int remaining = budget - primaryAmount;

            // What is left is spread evenly over the spilled stats, so "Even" reads as even rather than
            // as one big number followed by a scatter of ones.
            for (int i = 1; i < picked.Count && remaining > 0; i++)
            {
                int slotsLeft = picked.Count - i;
                int share = Mathf.Clamp(remaining / slotsLeft, 1, remaining);

                into[picked[i]] += share;
                remaining -= share;
            }

            // Rounding leaves a point or two over. They go where the budget already leaned.
            if (remaining > 0) into[picked[0]] += remaining;
        }

        /// <summary>
        /// Chooses distinct stats, each pick weighted by its affinity and then removed from the pool so
        /// one stat cannot be chosen twice.
        /// </summary>
        static List<int> PickDistinct(System.Random rng, float[] affinity, int count, RelicDefinition definition)
        {
            var picked = new List<int>();
            var weights = new float[RelicInstance.StatCount];

            for (int i = 0; i < weights.Length; i++) weights[i] = Mathf.Max(0f, affinity[i]);

            for (int n = 0; n < count; n++)
            {
                float total = 0f;
                for (int i = 0; i < weights.Length; i++) total += weights[i];

                if (total <= 0f) break;

                double roll = rng.NextDouble() * total;
                int chosen = -1;
                float running = 0f;

                for (int i = 0; i < weights.Length; i++)
                {
                    if (weights[i] <= 0f) continue;

                    running += weights[i];
                    if (roll < running)
                    {
                        chosen = i;
                        break;
                    }
                }

                // Floating point can leave the roll a hair past the last weight. Take the last stat that
                // is still available rather than dropping the point.
                if (chosen < 0)
                {
                    for (int i = weights.Length - 1; i >= 0; i--)
                    {
                        if (weights[i] > 0f)
                        {
                            chosen = i;
                            break;
                        }
                    }
                }

                if (chosen < 0) break;

                picked.Add(chosen);
                weights[chosen] = 0f;
            }

            // Every weight was zero, which is a relic that cannot raise anything at all. Rather than
            // silently throwing the budget away, place it somewhere and say so.
            if (picked.Count == 0)
            {
                int fallback = rng.Next(0, RelicInstance.StatCount);
                picked.Add(fallback);

                Debug.LogWarning("[Relic] " + (definition != null ? definition.itemName : "A relic") +
                                 " has no usable affinity weights; its budget was placed evenly instead.");
            }

            return picked;
        }

        // ------------------------------------------------------------------ overlap

        /// <summary>
        /// Moves a negative off any stat the roll already raised, so the two packages stay legible as two
        /// separate things. When there is nowhere clean to move it, the point is put back and the two
        /// combine: an unusual roll is a real result, not a reason to refuse.
        /// </summary>
        static void RedealOverlaps(System.Random rng, RelicDefinition definition, RelicInstance instance)
        {
            for (int i = 0; i < RelicInstance.StatCount; i++)
            {
                if (instance.negativeAllocation[i] == 0 || instance.positiveAllocation[i] == 0) continue;

                int move = instance.negativeAllocation[i];
                instance.negativeAllocation[i] = 0;

                int target = -1;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    int candidate = rng.Next(0, RelicInstance.StatCount);
                    if (instance.positiveAllocation[candidate] > 0) continue;

                    target = candidate;
                    break;
                }

                if (target >= 0)
                {
                    instance.negativeAllocation[target] += move;
                }
                else
                {
                    instance.negativeAllocation[i] += move;

                    Debug.LogWarning("[Relic] " + definition.itemName +
                                     " had nowhere to redeal its negative points; they combined with the positive ones instead.");
                }
            }
        }

        // ------------------------------------------------------------------ reporting

        /// <summary>A roll written out in full, for the verbose log and for the debug probe.</summary>
        public static string Describe(RelicDefinition definition, RelicInstance instance)
        {
            if (instance == null) return "no relic";

            var sb = new StringBuilder();
            sb.Append(definition != null ? definition.itemName : "Relic");
            sb.Append("  [").Append(RelicInstance.TierLabel(instance.tier)).Append(']');
            sb.Append("  seed ").Append(instance.seed);

            sb.Append("\n   final    ").Append(instance.DescribeInline());
            sb.Append("\n   positive ").Append(instance.positiveTotal).Append(" pts, ")
              .Append(RelicInstance.ConcentrationLabel(instance.positiveConcentration));
            sb.Append("\n   negative ").Append(instance.negativeTotal).Append(" pts, ")
              .Append(RelicInstance.ConcentrationLabel(instance.negativeConcentration));

            return sb.ToString();
        }
    }
}

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DungeonCards.EditorTools
{
    /// <summary>
    /// Debug probe for the relic generator. Rolls the real relic definitions through the real generator and
    /// reports what came out, so the tables can be tuned against measurements rather than impressions.
    ///
    /// Every run takes a fixed seed, and the generator's own random source is seeded from it, so a report
    /// can be reproduced exactly. A run that differs from the same seed earlier means a balance table or a
    /// definition changed, which is the point: it makes an unintended change visible.
    ///
    /// It runs from Tools > Dungeon Cards > Relics, and Run can also be called directly with no window.
    /// </summary>
    public static class RelicProbe
    {
        const string DatabasePath = "Assets/Resources/GameDatabase.asset";
        const int DefaultTrials = 1000;
        const int DefaultSeed = 20260922;

        /// <summary>How many of the most common roll shapes and rolls to list.</summary>
        const int TopCount = 5;

        [MenuItem("Tools/Dungeon Cards/Relics/Generate One")]
        public static void GenerateOne()
        {
            Debug.Log(RunOne(DefaultSeed));
        }

        [MenuItem("Tools/Dungeon Cards/Relics/Generate 100")]
        public static void GenerateHundred()
        {
            Debug.Log(Run(100, DefaultSeed));
        }

        [MenuItem("Tools/Dungeon Cards/Relics/Generate 1000")]
        public static void GenerateThousand()
        {
            Debug.Log(Run(1000, DefaultSeed));
        }

        /// <summary>Entry point for a caller that wants the report as text rather than in the console.</summary>
        public static string Execute()
        {
            return Run(DefaultTrials, DefaultSeed);
        }

        // ------------------------------------------------------------------ reports

        /// <summary>One relic, written out in full, with the settings that produced it.</summary>
        public static string RunOne(int seed)
        {
            var sb = new StringBuilder();

            GameDatabase database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            List<RelicDefinition> relics = Relics(database);

            sb.AppendLine("RELIC ROLL PROBE - one relic");
            sb.AppendLine("   database   " + (database != null ? DatabasePath : "MISSING"));
            sb.AppendLine("   relics     " + relics.Count);
            sb.AppendLine("   seed       " + seed);
            sb.AppendLine();

            if (relics.Count == 0)
            {
                sb.AppendLine("No RelicDefinition assets found. Run Tools > Dungeon Cards > Build Game Scenes.");
                return sb.ToString();
            }

            RelicDefinition definition = relics[0];
            RelicInstance instance = RelicGenerator.Roll(definition, seed);

            sb.AppendLine(RelicGenerator.Describe(definition, instance));
            sb.AppendLine();
            sb.AppendLine("   guaranteed " + GuaranteedSummary(definition));
            sb.AppendLine("   effect     " + (definition.effect == RelicEffect.None ? "none" : definition.EffectSummary(null)));
            sb.AppendLine();
            sb.AppendLine(instance.DescribeLines());

            return sb.ToString();
        }

        /// <summary>
        /// The whole roster, rolled many times each, reported per relic and then per tier.
        ///
        /// The tier block is the one that answers whether the tables are doing what they say: it is where
        /// the positive budget, the negative frequency and the concentration odds can be read against the
        /// settings that were supposed to produce them.
        /// </summary>
        public static string Run(int trials, int seed)
        {
            if (trials < 1) trials = 1;

            var sb = new StringBuilder();

            GameDatabase database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            List<RelicDefinition> relics = Relics(database);

            RelicSettings settings = RelicSettings.Instance;
            settings.EnsureConfigured();

            sb.AppendLine("RELIC ROLL PROBE");
            sb.AppendLine("   database   " + (database != null ? DatabasePath : "MISSING"));
            sb.AppendLine("   relics     " + relics.Count);
            sb.AppendLine("   trials     " + trials + " per relic");
            sb.AppendLine("   seed       " + seed);
            sb.AppendLine("   overlap    " + settings.overlapRule);
            sb.AppendLine();

            if (relics.Count == 0)
            {
                sb.AppendLine("No RelicDefinition assets found. Run Tools > Dungeon Cards > Build Game Scenes.");
                return sb.ToString();
            }

            // Per tier, accumulated across every relic of that tier.
            var tiers = new Dictionary<RelicRarity, Tally>();

            int checksum = 17;

            foreach (RelicDefinition definition in relics)
            {
                var tally = new Tally();

                for (int i = 0; i < trials; i++)
                {
                    // The seed advances with the trial, so a run is reproducible and two different trials
                    // are not the same roll repeated.
                    RelicInstance instance = RelicGenerator.Roll(definition, seed + i);
                    tally.Add(instance);

                    checksum = checksum * 31 + instance.seed;
                    for (int s = 0; s < RelicInstance.StatCount; s++) checksum = checksum * 31 + instance.stats[s];
                }

                if (!tiers.ContainsKey(definition.tier)) tiers[definition.tier] = new Tally();
                tiers[definition.tier].Merge(tally);

                sb.AppendLine(Report(definition, tally));
            }

            sb.AppendLine("BY TIER");
            foreach (RelicRarity rarity in TierOrder())
            {
                Tally tally;
                if (!tiers.TryGetValue(rarity, out tally)) continue;

                RelicTierSettings table = settings.For(rarity);

                sb.AppendLine("   " + RelicInstance.TierLabel(rarity).PadRight(10) +
                              "configured positive " + table.positiveMin + "-" + table.positiveMax +
                              ", negatives " + Percent(table.negativeChance) +
                              "   |   rolled avg positive " + tally.AveragePositive().ToString("0.00") +
                              " (min " + tally.MinPositive + ", max " + tally.MaxPositive + ")" +
                              ", negatives " + Percent(tally.NegativeFrequency()));
            }

            sb.AppendLine();
            sb.AppendLine("   checksum   " + checksum + "   (same seed and same tables give the same number)");

            return sb.ToString();
        }

        // ------------------------------------------------------------------ shaping

        static string Report(RelicDefinition definition, Tally tally)
        {
            var sb = new StringBuilder();

            sb.AppendLine(definition.itemName + "   [" + RelicInstance.TierLabel(definition.tier) + "]   " +
                          tally.Count + " rolls");
            sb.AppendLine("   guaranteed     " + GuaranteedSummary(definition));

            sb.AppendLine("   positive total min " + tally.MinPositive +
                          "   avg " + tally.AveragePositive().ToString("0.00") +
                          "   max " + tally.MaxPositive);

            sb.AppendLine("   negative       " + Percent(tally.NegativeFrequency()) + " of rolls" +
                          (tally.NegativeCount > 0
                              ? ", avg " + tally.AverageNegative().ToString("0.00") + " pts when it rolls"
                              : ""));

            sb.AppendLine("   concentration  " + tally.ConcentrationReport());

            sb.AppendLine("   stats touched  avg " + tally.AverageTouched().ToString("0.00") + " of " +
                          RelicInstance.StatCount);

            sb.AppendLine("   commonest      " + tally.TopReport(tally.Rolls, TopCount));
            sb.AppendLine("   shapes         " + tally.TopReport(tally.Shapes, TopCount));
            sb.AppendLine();

            return sb.ToString();
        }

        static string GuaranteedSummary(RelicDefinition definition)
        {
            var parts = new List<string>();

            foreach (StatType type in RelicInstance.StatOrder)
            {
                int value = definition.GetBonus(type);
                if (value != 0) parts.Add(RelicInstance.Signed(value, RelicInstance.StatLabel(type)));
            }

            if (definition.maxHealthBonus != 0) parts.Add(RelicInstance.Signed(definition.maxHealthBonus, "MaxHealth"));
            if (definition.energyBonus != 0) parts.Add(RelicInstance.Signed(definition.energyBonus, "Energy"));

            return parts.Count == 0 ? "none" : string.Join(", ", parts.ToArray());
        }

        static IEnumerable<RelicRarity> TierOrder()
        {
            yield return RelicRarity.TierIII;
            yield return RelicRarity.TierII;
            yield return RelicRarity.TierI;
            yield return RelicRarity.Unique;
        }

        static List<RelicDefinition> Relics(GameDatabase database)
        {
            var relics = new List<RelicDefinition>();
            if (database == null || database.allItems == null) return relics;

            foreach (ItemData item in database.allItems)
            {
                var relic = item as RelicDefinition;
                if (relic != null) relics.Add(relic);
            }

            return relics;
        }

        static string Percent(float fraction)
        {
            return (fraction * 100f).ToString("0") + "%";
        }

        // ------------------------------------------------------------------ tallies

        /// <summary>
        /// Everything one relic's rolls add up to. Accumulated rather than kept as a list of rolls, so a
        /// thousand trials cost a fixed amount of memory whatever they contain.
        /// </summary>
        class Tally
        {
            public int Count;
            public int MinPositive = int.MaxValue;
            public int MaxPositive;
            public long PositiveTotal;
            public int NegativeCount;
            public long NegativeTotal;
            public long TouchedTotal;

            public readonly int[] Concentrations = new int[4];

            /// <summary>How often each exact final roll came up.</summary>
            public readonly Dictionary<string, int> Rolls = new Dictionary<string, int>();

            /// <summary>How often each allocation shape came up, ignoring the sizes.</summary>
            public readonly Dictionary<string, int> Shapes = new Dictionary<string, int>();

            public void Add(RelicInstance instance)
            {
                Count++;

                if (instance.positiveTotal < MinPositive) MinPositive = instance.positiveTotal;
                if (instance.positiveTotal > MaxPositive) MaxPositive = instance.positiveTotal;
                PositiveTotal += instance.positiveTotal;

                if (instance.negativeTotal > 0)
                {
                    NegativeCount++;
                    NegativeTotal += instance.negativeTotal;
                }

                TouchedTotal += instance.TouchedStatCount();

                int concentration = (int)instance.positiveConcentration;
                if (concentration >= 0 && concentration < Concentrations.Length) Concentrations[concentration]++;

                Bump(Rolls, instance.DescribeInline());
                Bump(Shapes, Shape(instance));
            }

            public void Merge(Tally other)
            {
                if (other.Count == 0) return;

                if (other.MinPositive < MinPositive) MinPositive = other.MinPositive;
                if (other.MaxPositive > MaxPositive) MaxPositive = other.MaxPositive;

                Count += other.Count;
                PositiveTotal += other.PositiveTotal;
                NegativeCount += other.NegativeCount;
                NegativeTotal += other.NegativeTotal;
                TouchedTotal += other.TouchedTotal;

                for (int i = 0; i < Concentrations.Length; i++) Concentrations[i] += other.Concentrations[i];

                foreach (KeyValuePair<string, int> entry in other.Rolls) Add(Rolls, entry.Key, entry.Value);
                foreach (KeyValuePair<string, int> entry in other.Shapes) Add(Shapes, entry.Key, entry.Value);
            }

            public float AveragePositive()
            {
                return Count == 0 ? 0f : (float)PositiveTotal / Count;
            }

            public float AverageNegative()
            {
                return NegativeCount == 0 ? 0f : (float)NegativeTotal / NegativeCount;
            }

            public float NegativeFrequency()
            {
                return Count == 0 ? 0f : (float)NegativeCount / Count;
            }

            public float AverageTouched()
            {
                return Count == 0 ? 0f : (float)TouchedTotal / Count;
            }

            public string ConcentrationReport()
            {
                var sb = new StringBuilder();

                for (int i = 0; i < Concentrations.Length; i++)
                {
                    if (i > 0) sb.Append("   ");
                    sb.Append(RelicInstance.ConcentrationLabel((RelicConcentration)i));
                    sb.Append(' ');
                    sb.Append(Percent(Count == 0 ? 0f : (float)Concentrations[i] / Count));
                }

                return sb.ToString();
            }

            /// <summary>The most common entries in one of the dictionaries, largest first.</summary>
            public string TopReport(Dictionary<string, int> source, int top)
            {
                if (source.Count == 0) return "none";

                var sorted = new List<KeyValuePair<string, int>>(source);
                sorted.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
                {
                    // By count, then by name so equal counts always print in the same order and a repeat run
                    // of the same seed is identical text.
                    int byCount = b.Value.CompareTo(a.Value);
                    return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
                });

                var sb = new StringBuilder();
                int shown = Mathf.Min(top, sorted.Count);

                for (int i = 0; i < shown; i++)
                {
                    if (i > 0) sb.Append("   |   ");
                    sb.Append(sorted[i].Value).Append("x ").Append(sorted[i].Key);
                }

                if (sorted.Count > shown) sb.Append("   (+").Append(sorted.Count - shown).Append(" more)");

                return sb.ToString();
            }

            /// <summary>Which stats were raised and which were lowered, ignoring by how much.</summary>
            static string Shape(RelicInstance instance)
            {
                var raised = new List<string>();
                var lowered = new List<string>();

                foreach (StatType type in RelicInstance.StatOrder)
                {
                    int value = instance.Get(type);
                    if (value > 0) raised.Add(RelicInstance.StatLabel(type));
                    else if (value < 0) lowered.Add(RelicInstance.StatLabel(type));
                }

                string up = raised.Count == 0 ? "none" : string.Join("", raised.ToArray());
                string down = lowered.Count == 0 ? "none" : string.Join("", lowered.ToArray());

                return "+" + up + " / -" + down;
            }

            static void Bump(Dictionary<string, int> counts, string key)
            {
                Add(counts, key, 1);
            }

            static void Add(Dictionary<string, int> counts, string key, int amount)
            {
                int existing;
                counts[key] = counts.TryGetValue(key, out existing) ? existing + amount : amount;
            }
        }
    }
}
#endif

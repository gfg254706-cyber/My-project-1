using System;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// The balance of one tier. These are the numbers the generator reads instead of rules written into
    /// it, so a tier can be re-tuned without touching code.
    /// </summary>
    [Serializable]
    public class RelicTierSettings
    {
        public string label = "TIER";

        [Header("Positive package")]
        public int positiveMin = 1;
        public int positiveMax = 3;

        [Header("Negative package")]
        [Tooltip("How often a relic of this tier rolls a negative package at all. Zero means it never does.")]
        [Range(0f, 1f)] public float negativeChance;

        public int negativeMin;
        public int negativeMax;
    }

    /// <summary>
    /// The shape of one concentration level. This is what separates an Even roll from an Extreme one:
    /// how many stats the budget is allowed to touch, and how much of it the first stat takes.
    /// </summary>
    [Serializable]
    public class RelicConcentrationProfile
    {
        public string label = "Even";

        [Tooltip("The most different stats this roll may touch.")]
        [Range(1, 5)] public int maxStats = 5;

        [Tooltip("The share of the budget the first stat takes.")]
        [Range(0f, 1f)] public float primaryShare = 0.25f;

        [Tooltip("The chance of opening one more stat, rolled per extra stat. Zero keeps everything in " +
                 "the first stat, which is what makes a roll Extreme.")]
        [Range(0f, 1f)] public float spillChance = 1f;
    }

    /// <summary>
    /// The whole relic generator's tuning, in one asset so the inspector can reach it.
    ///
    /// Per-tier budgets, the chance of a negative package, and the odds of each concentration all live
    /// here rather than in the generator. Adding a relic is then a matter of authoring a definition, and
    /// rebalancing a tier is a matter of editing this.
    /// </summary>
    [CreateAssetMenu(fileName = "RelicSettings", menuName = "Dungeon Cards/Relic Settings")]
    public class RelicSettings : ScriptableObject
    {
        public const string ResourceName = "RelicSettings";

        [Tooltip("Indexed by RelicRarity: TierIII, TierII, TierI, Unique.")]
        public RelicTierSettings[] tiers = new RelicTierSettings[4];

        [Tooltip("Indexed by RelicConcentration: Even, Mixed, Focused, Extreme.")]
        public RelicConcentrationProfile[] concentrations = new RelicConcentrationProfile[4];

        [Tooltip("Used when a relic rolls concentration with no weights of its own.")]
        public float[] defaultConcentrationWeights = { 1f, 1f, 1f, 1f };

        [Tooltip("What happens when the positive and negative packages land on the same stat. Combine " +
                 "adds them into one signed value; RedealNegatives tries to move the negative elsewhere.")]
        public RelicOverlapRule overlapRule = RelicOverlapRule.Combine;

        [Tooltip("Writes every roll to the console. Useful while tuning, noisy in a real run.")]
        public bool verboseRolls;

        [Tooltip("A crash guard, not a balance lever. Loaded Dice stops conjuring past this depth.")]
        public int maxLoadedDiceDepth = 16;

        [Tooltip("A crash guard: the most extra triggers Clockwork Core may force on one construct in a turn.")]
        public int maxClockworkExtraTriggers = 8;

        static RelicSettings cached;

        /// <summary>
        /// The settings the game runs on: the asset when there is one, and a code default when there is
        /// not, so the relic system works in a scene played straight from the editor before the asset has
        /// been created. Loaded the same way the game database is.
        /// </summary>
        public static RelicSettings Instance
        {
            get
            {
                if (cached == null)
                {
                    cached = Resources.Load<RelicSettings>(ResourceName);
                    if (cached == null) cached = CreateDefault();
                }
                return cached;
            }
        }

        /// <summary>Drops the cached settings, so the next read goes back to Resources.</summary>
        public static void ClearCache()
        {
            cached = null;
        }

        /// <summary>
        /// The settings for a tier. Indexed by the enum's own value, with a bounds check and a fallback:
        /// a lookup can never index past the array whatever a definition was saved with.
        /// </summary>
        public RelicTierSettings For(RelicRarity rarity)
        {
            EnsureConfigured();

            int index = (int)rarity;
            if (index < 0 || index >= tiers.Length) index = (int)RelicRarity.TierIII;

            RelicTierSettings tier = tiers[index];
            if (tier != null) return tier;

            Debug.LogWarning("[Relic] RelicSettings has no entry for " + RelicInstance.TierLabel(rarity) +
                             "; using the code default.");
            return CreateDefault().tiers[index];
        }

        public RelicConcentrationProfile ProfileFor(RelicConcentration concentration)
        {
            EnsureConfigured();

            int index = (int)concentration;
            if (index < 0 || index >= concentrations.Length) index = (int)RelicConcentration.Mixed;

            RelicConcentrationProfile profile = concentrations[index];
            if (profile != null) return profile;

            return CreateDefault().concentrations[index];
        }

        /// <summary>
        /// The concentration odds to roll a relic against: its own when it authored any, the shared
        /// default otherwise. A relic is not required to have an opinion about its own shape.
        /// </summary>
        public float[] ConcentrationWeights(RelicDefinition definition, bool positive)
        {
            EnsureConfigured();

            if (definition != null && definition.useCustomConcentration)
            {
                float[] custom = positive ? definition.positiveConcentrationWeights : definition.negativeConcentrationWeights;
                if (custom != null && custom.Length == 4 && Total(custom) > 0f) return custom;
            }

            return defaultConcentrationWeights;
        }

        /// <summary>
        /// Repairs an asset that is missing entries, which is what a settings asset created empty from
        /// the menu looks like. Each missing slot is filled from the code default rather than left null
        /// to throw on first use.
        /// </summary>
        public void EnsureConfigured()
        {
            if (IsComplete()) return;

            RelicSettings defaults = CreateDefault();

            if (tiers == null || tiers.Length != 4) tiers = defaults.tiers;
            else
            {
                for (int i = 0; i < tiers.Length; i++)
                {
                    if (tiers[i] == null) tiers[i] = defaults.tiers[i];
                }
            }

            if (concentrations == null || concentrations.Length != 4) concentrations = defaults.concentrations;
            else
            {
                for (int i = 0; i < concentrations.Length; i++)
                {
                    if (concentrations[i] == null) concentrations[i] = defaults.concentrations[i];
                }
            }

            if (defaultConcentrationWeights == null || defaultConcentrationWeights.Length != 4)
                defaultConcentrationWeights = defaults.defaultConcentrationWeights;
        }

        bool IsComplete()
        {
            if (tiers == null || tiers.Length != 4) return false;
            if (concentrations == null || concentrations.Length != 4) return false;
            if (defaultConcentrationWeights == null || defaultConcentrationWeights.Length != 4) return false;

            for (int i = 0; i < 4; i++)
            {
                if (tiers[i] == null) return false;
                if (concentrations[i] == null) return false;
            }

            return true;
        }

        static float Total(float[] weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += Mathf.Max(0f, weights[i]);
            return total;
        }

        /// <summary>
        /// The opening tuning, written down once so both the default asset and the code fallback use the
        /// same numbers. Positive budgets follow the intended ranges: Tier III small, Tier II middle,
        /// Tier I the largest and the only one that leans on guaranteed stats, and Unique left open to be
        /// tuned on its own.
        /// </summary>
        public void Configure()
        {
            tiers = new RelicTierSettings[4];

            tiers[(int)RelicRarity.TierIII] = new RelicTierSettings
            {
                label = "TIER III",
                positiveMin = 1,
                positiveMax = 3,
                negativeChance = 0.1f,
                negativeMin = 1,
                negativeMax = 2
            };

            tiers[(int)RelicRarity.TierII] = new RelicTierSettings
            {
                label = "TIER II",
                positiveMin = 5,
                positiveMax = 10,
                negativeChance = 0.25f,
                negativeMin = 1,
                negativeMax = 3
            };

            tiers[(int)RelicRarity.TierI] = new RelicTierSettings
            {
                label = "TIER I",
                positiveMin = 6,
                positiveMax = 12,
                negativeChance = 0.5f,
                negativeMin = 1,
                negativeMax = 4
            };

            // Unique is not "Tier I but stronger": it is where the rule-changing relics live, so its
            // budget is deliberately modest and is expected to be overridden per relic.
            tiers[(int)RelicRarity.Unique] = new RelicTierSettings
            {
                label = "UNIQUE",
                positiveMin = 4,
                positiveMax = 10,
                negativeChance = 0.5f,
                negativeMin = 1,
                negativeMax = 4
            };

            concentrations = new RelicConcentrationProfile[4];

            concentrations[(int)RelicConcentration.Even] = new RelicConcentrationProfile
            {
                label = "Even",
                maxStats = 5,
                primaryShare = 0.3f,
                spillChance = 1f
            };

            concentrations[(int)RelicConcentration.Mixed] = new RelicConcentrationProfile
            {
                label = "Mixed",
                maxStats = 3,
                primaryShare = 0.5f,
                spillChance = 0.9f
            };

            concentrations[(int)RelicConcentration.Focused] = new RelicConcentrationProfile
            {
                label = "Focused",
                maxStats = 2,
                primaryShare = 0.8f,
                spillChance = 0.75f
            };

            concentrations[(int)RelicConcentration.Extreme] = new RelicConcentrationProfile
            {
                label = "Extreme",
                maxStats = 1,
                primaryShare = 1f,
                spillChance = 0f
            };

            defaultConcentrationWeights = new float[] { 1f, 1.4f, 1.2f, 0.6f };
        }

        /// <summary>A settings object built in code, used when no asset has been authored yet.</summary>
        public static RelicSettings CreateDefault()
        {
            RelicSettings settings = CreateInstance<RelicSettings>();
            settings.name = "RelicSettings (code default)";
            settings.Configure();
            return settings;
        }
    }
}

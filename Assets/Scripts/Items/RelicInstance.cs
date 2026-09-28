using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// One relic's roll.
    ///
    /// The definition is the template and never changes; this is the individual object the player owns,
    /// so two Titan's Grips can carry completely different numbers. The final signed value per stat is
    /// what everything downstream reads, and the positive and negative allocations are kept beside it so
    /// a roll can be explained as well as shown.
    /// </summary>
    [Serializable]
    public class RelicInstance
    {
        /// <summary>The five attributes in enum order, so an array can be indexed by (int)StatType.</summary>
        public static readonly StatType[] StatOrder =
        {
            StatType.Strength,
            StatType.Agility,
            StatType.Intellect,
            StatType.Vitality,
            StatType.Willpower
        };

        public const int StatCount = 5;

        public RelicRarity tier;

        /// <summary>The seed that produced this roll, so a bad number can be reproduced and examined.</summary>
        public int seed;

        /// <summary>The final signed value per stat, after the positives, the negatives and the guarantees meet.</summary>
        public int[] stats = new int[StatCount];

        /// <summary>What the positive package put on each stat, before it met anything else.</summary>
        public int[] positiveAllocation = new int[StatCount];

        /// <summary>What the negative package took off each stat, as a positive count.</summary>
        public int[] negativeAllocation = new int[StatCount];

        /// <summary>The positive budget that was rolled, guaranteed stats excluded.</summary>
        public int positiveTotal;

        /// <summary>The negative budget that was rolled. Zero is the common case at the low tiers.</summary>
        public int negativeTotal;

        public RelicConcentration positiveConcentration;
        public RelicConcentration negativeConcentration;

        /// <summary>
        /// Makes sure the arrays are the right length. An instance deserialized from an older shape, or
        /// built by hand, is repaired rather than throwing on the first read.
        /// </summary>
        public void EnsureSize()
        {
            if (stats == null || stats.Length != StatCount) stats = new int[StatCount];
            if (positiveAllocation == null || positiveAllocation.Length != StatCount) positiveAllocation = new int[StatCount];
            if (negativeAllocation == null || negativeAllocation.Length != StatCount) negativeAllocation = new int[StatCount];
        }

        /// <summary>The relic's final value in one stat. This is the number that reaches the player.</summary>
        public int Get(StatType type)
        {
            EnsureSize();
            int index = (int)type;
            return (index >= 0 && index < stats.Length) ? stats[index] : 0;
        }

        public int GetPositive(StatType type)
        {
            EnsureSize();
            int index = (int)type;
            return (index >= 0 && index < positiveAllocation.Length) ? positiveAllocation[index] : 0;
        }

        public int GetNegative(StatType type)
        {
            EnsureSize();
            int index = (int)type;
            return (index >= 0 && index < negativeAllocation.Length) ? negativeAllocation[index] : 0;
        }

        /// <summary>How many of the four (or five) stats this roll actually moved.</summary>
        public int TouchedStatCount()
        {
            EnsureSize();

            int count = 0;
            for (int i = 0; i < stats.Length; i++)
            {
                if (stats[i] != 0) count++;
            }
            return count;
        }

        /// <summary>The roll as a plain string, negative values and zeros included.</summary>
        public string DescribeInline()
        {
            var parts = new List<string>();
            foreach (StatType type in StatOrder)
            {
                parts.Add(StatLabel(type) + " " + Signed(Get(type)));
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// One line per stat, every stat listed even when it is zero, so what a relic costs is as
        /// legible as what it pays.
        /// </summary>
        public string DescribeLines()
        {
            var sb = new StringBuilder();
            foreach (StatType type in StatOrder)
            {
                sb.Append(StatLabel(type));
                sb.Append(' ');
                sb.Append(Signed(Get(type)));
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        public static string StatLabel(StatType type)
        {
            switch (type)
            {
                case StatType.Strength: return "STR";
                case StatType.Agility: return "AGI";
                case StatType.Intellect: return "INT";
                case StatType.Vitality: return "VIT";
                case StatType.Willpower: return "WIL";
            }
            return type.ToString().ToUpperInvariant();
        }

        public static string TierLabel(RelicRarity rarity)
        {
            switch (rarity)
            {
                case RelicRarity.Unique: return "UNIQUE";
                case RelicRarity.TierI: return "TIER I";
                case RelicRarity.TierII: return "TIER II";
            }
            return "TIER III";
        }

        public static string ConcentrationLabel(RelicConcentration concentration)
        {
            return concentration.ToString();
        }

        /// <summary>A signed value on its own, with the plus kept for the sake of the column.</summary>
        public static string Signed(int value)
        {
            return (value > 0 ? "+" : "") + value;
        }

        /// <summary>A signed value with its stat after it, which is how an inline summary reads.</summary>
        public static string Signed(int value, string label)
        {
            return Signed(value) + " " + label;
        }
    }
}

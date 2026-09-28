using System;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// How good a relic is meant to be.
    ///
    /// The values are pinned so the number doubles as an index into the per-tier settings and the order
    /// reads the way the tiers are talked about: weakest first, Unique last. Pinning them also means
    /// inserting a tier later cannot silently re-map relics that are already authored.
    ///
    /// One consequence worth knowing: default(RelicRarity) is TierIII, the weakest. A relic saved before
    /// it was given a tier therefore lands at the bottom rather than arriving as a Unique.
    /// </summary>
    public enum RelicRarity
    {
        TierIII = 0,
        TierII = 1,
        TierI = 2,
        Unique = 3
    }

    /// <summary>
    /// How tightly a budget is packed. This is the shape of a roll rather than its size: an Even roll
    /// spreads across the stats and an Extreme one puts nearly everything in a single place.
    /// </summary>
    public enum RelicConcentration
    {
        Even = 0,
        Mixed = 1,
        Focused = 2,
        Extreme = 3
    }

    /// <summary>
    /// What a relic does beyond moving attributes. Everything a relic can do is named here.
    ///
    /// The values are pinned and grouped by the band the relic belongs to, so a relic authored before a
    /// new effect was added cannot be silently re-mapped onto it. The grouping is only for legibility:
    /// nothing reads the numbers, and a relic's band is its RelicRarity, not its effect.
    /// </summary>
    public enum RelicEffect
    {
        None = 0,

        // ---- the stat-scaling relics: the effect is worth a number that grows with the build ----------
        /// <summary>Loaded Dice: a chance that one conjured card turns up another, which can chain.</summary>
        LoadedDice = 1,

        /// <summary>Clockwork Core: a chance a construct fires a second time in the same turn.</summary>
        ClockworkCore = 2,

        /// <summary>Boulder Shield: shielding equal to a slice of a stat, once per turn.</summary>
        BoulderShield = 3,

        // ---- the minor relics: one small thing, once --------------------------------------------------
        /// <summary>Whetstone: the first Attack of the fight hits harder.</summary>
        Whetstone = 10,

        /// <summary>Bloodstained Thread: the first health lost in the fight draws a card.</summary>
        BloodstainedThread = 11,

        /// <summary>Bent Lens: the first mana gained in the fight also grants shielding.</summary>
        BentLens = 12,

        /// <summary>Lucky Charm: the first card conjured in the fight costs less.</summary>
        LuckyCharm = 13,

        /// <summary>Traveler's Coin: the first card discarded in the fight pays gold.</summary>
        TravelersCoin = 14,

        // ---- the build relics: an engine that pays on a cadence ---------------------------------------
        /// <summary>War Trophy: the first kill of the fight grants energy.</summary>
        WarTrophy = 20,

        /// <summary>Duelist's Ribbon: one card per turn is cheaper, at the position the relic names.</summary>
        DuelistsRibbon = 21,

        /// <summary>Architect's Lens: the first ongoing card of the fight lasts longer.</summary>
        ArchitectsLens = 22,

        /// <summary>Blood Sigil: losing enough health in one turn grants energy, once per fight.</summary>
        BloodSigil = 23,

        /// <summary>Scrap Satchel: discarding enough cards in a turn grants mana.</summary>
        ScrapSatchel = 24,

        /// <summary>Broken Crown: the lowest attribute reads higher to every card formula.</summary>
        BrokenCrown = 25,

        // ---- the major relics: they change how a fight is played -------------------------------------
        /// <summary>Goblet: a designated attribute is bought a point at a time, at a price in cards.</summary>
        Goblet = 30,

        /// <summary>Titan's Grip: the first Attack of each turn resolves twice, riders included.</summary>
        TitansGrip = 31,

        /// <summary>Grand Orrery: the first construct to run out each turn fires one final time.</summary>
        GrandOrrery = 32,

        /// <summary>Blood Crown: falling below half health pays out energy and an attribute.</summary>
        BloodCrown = 33,

        /// <summary>Loaded Grimoire: the first conjure of each turn turns up two cards instead of one.</summary>
        LoadedGrimoire = 34,

        /// <summary>Reaper's Ledger: giving up an ongoing card pays energy and draws.</summary>
        ReapersLedger = 35,

        /// <summary>Mirage Engine: the turn's last card conjures a free one.</summary>
        MirageEngine = 36,

        // ---- polarity ----------------------------------------------------------------------------------
        /// <summary>
        /// The Broken Oath: Willpower is allowed to fall below zero, which is what a negative Willpower
        /// build is built out of, and what lets a Willpower card invert.
        /// </summary>
        BrokenOath = 40
    }

    /// <summary>What happens when the positive and negative budgets land on the same stat.</summary>
    public enum RelicOverlapRule
    {
        /// <summary>They cancel into one signed value: +5 Strength and -2 Strength is +3 Strength.</summary>
        Combine = 0,

        /// <summary>The negative is moved onto a stat the roll did not already raise, when there is one.</summary>
        RedealNegatives = 1
    }

    /// <summary>
    /// What a relic's effect is worth at the player's current attributes.
    ///
    /// One shape covers every relic that scales: base + stat x rate, then divided. Boulder Shield is the
    /// same shape read with a divisor, and the percentages are the same shape read without one.
    /// </summary>
    [Serializable]
    public class RelicScaling
    {
        [Tooltip("Which attribute the effect grows with.")]
        public StatType stat = StatType.Strength;

        [Tooltip("What the effect is worth at zero.")]
        public float baseValue;

        [Tooltip("How much one point of the stat adds.")]
        public float perStatPoint;

        [Tooltip("Divides the total. Boulder Shield's Strength / 3 is base 0, per point 1, divisor 3.")]
        public float divisor = 1f;

        [Tooltip("A chance. The result is clamped so a scaling relic can never promise more than certainty.")]
        public bool isPercent;

        [Tooltip("The most a percentage may reach.")]
        public float percentMax = 100f;

        /// <summary>The effect's value right now, before it is rounded to a whole point of anything.</summary>
        public float Resolve(PlayerStats stats)
        {
            return Resolve(stats, 0);
        }

        /// <summary>
        /// The same value with points of the scaling stat added on top of the player's own.
        ///
        /// This is what lets the shop quote a relic that has not been bought yet: a relic on the rack is not
        /// in the Inventory, so its own stat is not inside PlayerStats and its effect would otherwise
        /// advertise the number it produces in nobody's hands. An owned relic passes nothing, because
        /// Inventory.GetBonuses has already folded its stats into the player.
        /// </summary>
        public float Resolve(PlayerStats stats, int extraStat)
        {
            float value = baseValue;

            if (perStatPoint != 0f)
            {
                int total = (stats != null ? stats.GetStat(stat) : 0) + extraStat;
                value += total * perStatPoint;
            }

            if (divisor != 0f && divisor != 1f) value /= divisor;
            if (isPercent) value = Mathf.Clamp(value, 0f, percentMax);

            return value;
        }

        /// <summary>
        /// The effect's value as a whole number of points, using the same rounding a card uses to turn a
        /// calculated number into an integer. Never negative: an effect that scaled below zero is worth
        /// nothing rather than a debt.
        /// </summary>
        public int ResolveInt(PlayerStats stats)
        {
            return Mathf.Max(0, Mathf.FloorToInt(Resolve(stats)));
        }
    }

    /// <summary>
    /// A relic template. It describes what a relic is and how its numbers are generated, but it never
    /// holds a roll of its own: the roll lives on the ItemInstance that carries it, so two relics made
    /// from this template can be completely different objects.
    ///
    /// Guaranteed stats are the inherited flat fields (strength, agility, and so on). They are added on
    /// top of the random package and are independent of it, which is how a Tier I relic keeps a strong
    /// identity across runs while still producing a different build every time.
    /// </summary>
    [CreateAssetMenu(fileName = "NewRelic", menuName = "Dungeon Cards/Relic")]
    public class RelicDefinition : ItemData
    {
        [Header("Relic")]
        [Tooltip("Tier I is the strongest ordinary relic. Unique is for rule-changing relics, which set " +
                 "their own budgets below rather than borrowing a tier's.")]
        public RelicRarity tier = RelicRarity.TierIII;

        [Header("Positive budget")]
        [Tooltip("Roll this relic's own positive range instead of its tier's. Tiers are the default so " +
                 "balance lives in one asset, but a Unique relic is expected to override it.")]
        public bool useCustomBudget;

        public int positiveMin = 1;
        public int positiveMax = 3;

        [Header("Negative budget")]
        [Tooltip("How many points of negative package the relic hands out. A negative stat is not a " +
                 "penalty: it is part of the loot, and a build can be aiming at it.")]
        public bool useCustomNegativeBudget;

        [Tooltip("0 to 1. Below zero uses the tier's own chance.")]
        public float negativeChance = -1f;

        public int negativeMin;
        public int negativeMax;

        [Header("Affinity weights")]
        [Tooltip("A bias, not a rule. A weight of zero makes the stat impossible; leaving a little on " +
                 "every stat you would accept is what keeps a relic's identity from becoming a promise.")]
        public float[] positiveAffinity = { 1f, 1f, 1f, 1f, 1f };

        [Tooltip("The same bias for the negative package, which is rolled separately from the positive one.")]
        public float[] negativeAffinity = { 1f, 1f, 1f, 1f, 1f };

        [Header("Concentration")]
        [Tooltip("Roll this relic's own concentration odds. The positive and negative packages roll " +
                 "these separately, so +8 STR / +1 INT / -3 AGI is reachable.")]
        public bool useCustomConcentration;

        [Tooltip("Even, Mixed, Focused, Extreme. Normalised, so any positive numbers work.")]
        public float[] positiveConcentrationWeights = { 1f, 1f, 1f, 1f };

        public float[] negativeConcentrationWeights = { 1f, 1f, 1f, 1f };

        [Header("Effect")]
        public RelicEffect effect = RelicEffect.None;

        [Tooltip("What the effect is worth at the player's attributes: base + stat x rate, then divisor. " +
                 "For an effect with two payouts this is the one the effect is named for, and the stat it " +
                 "names is the attribute the effect designates where it designates one.")]
        public RelicScaling scaling = new RelicScaling();

        [Tooltip("The count an effect waits for or spends: the position of the card Duelist's Ribbon " +
                 "discounts, the health Blood Sigil waits to see lost, the cards Scrap Satchel counts per " +
                 "mana, the cards a Goblet claims from the opening hand. Zero where the effect has no count.")]
        public int threshold;

        [Tooltip("The second payout, for the effects that hand over two different things: the attribute " +
                 "Blood Crown grants beside its energy, the cards Reaper's Ledger draws, the cards a " +
                 "Grimoire turns up. Zero where there is only one.")]
        public int secondaryMagnitude;


        /// <summary>
        /// Fills in the two fields a relic can never choose for itself. The inherited rarity is only
        /// there so colour and price keep working, so it follows the tier rather than being authored a
        /// second time and drifting from it.
        /// </summary>
        void OnValidate()
        {
            relic = true;
            slot = EquipmentSlot.Carried;
            rarity = (tier == RelicRarity.TierI || tier == RelicRarity.Unique) ? ItemRarity.Rare : ItemRarity.Common;

            EnsureWeights();
        }

        /// <summary>
        /// Guards the weight arrays. A relic asset saved before a weight was added, or one authored with
        /// an empty array, still generates instead of dropping its whole budget on the floor.
        /// </summary>
        public void EnsureWeights()
        {
            // A missing affinity array is filled evenly rather than with zeros: an empty array is a
            // relic that was never given an identity, not a relic that can never raise anything. An
            // array that is the right length but entirely zero is left alone, because that one is a
            // deliberate statement and the generator says so when it has to work around it.
            if (positiveAffinity == null || positiveAffinity.Length != RelicInstance.StatCount)
                positiveAffinity = EvenWeights();
            if (negativeAffinity == null || negativeAffinity.Length != RelicInstance.StatCount)
                negativeAffinity = EvenWeights();

            if (positiveConcentrationWeights == null || positiveConcentrationWeights.Length != 4)
                positiveConcentrationWeights = new float[] { 1f, 1f, 1f, 1f };

            if (negativeConcentrationWeights == null || negativeConcentrationWeights.Length != 4)
                negativeConcentrationWeights = new float[] { 1f, 1f, 1f, 1f };
        }

        /// <summary>Affinity weights that bias nothing, used when a relic has no usable array.</summary>
        public static float[] EvenWeights()
        {
            var weights = new float[RelicInstance.StatCount];
            for (int i = 0; i < weights.Length; i++) weights[i] = 1f;
            return weights;
        }

        /// <summary>The tier's name as the player sees it, so the relic's real standing is never hidden.</summary>
        public string TierLabel()

        {
            return RelicInstance.TierLabel(tier);
        }

        /// <summary>
        /// What this relic's effect does at the player's current attributes. Written from the numbers
        /// rather than typed into the description, so a scaling relic cannot advertise a value it no
        /// longer produces.
        /// </summary>
        public string EffectSummary(PlayerStats stats)
        {
            return EffectSummary(stats, 0);
        }

        /// <summary>
        /// The same line, resolved as though the relic were already being carried.
        ///
        /// The shop passes the relic's own points of its scaling stat, so the tooltip quotes what the
        /// player would actually get rather than what the relic produces in an empty inventory.
        /// </summary>
        public string EffectSummary(PlayerStats stats, int extraStat)
        {
            float value = scaling.Resolve(stats, extraStat);
            int whole = Mathf.FloorToInt(value);

            switch (effect)
            {
                case RelicEffect.LoadedDice:
                    return "Loaded Dice: " + value.ToString("0.#") + "% chance of an extra card";

                case RelicEffect.ClockworkCore:
                    return "Clockwork Core: " + value.ToString("0.#") + "% chance to trigger again";

                case RelicEffect.BoulderShield:
                    return "Boulder Shield: " + whole + " shielding each turn";

                case RelicEffect.Whetstone:
                    return "Whetstone: your first attack each combat deals +" + whole + " damage";

                case RelicEffect.BloodstainedThread:
                    return "Bloodstained Thread: the first time you lose health each combat, draw " +
                           Count(whole);

                case RelicEffect.BentLens:
                    return "Bent Lens: the first mana you gain each combat also grants " + whole + " shielding";

                case RelicEffect.LuckyCharm:
                    return "Lucky Charm: the first card conjured each combat costs " + whole + " less";

                case RelicEffect.TravelersCoin:
                    return "Traveler's Coin: the first card you discard each combat pays " + whole + " gold";

                case RelicEffect.WarTrophy:
                    return "War Trophy: the first kill each combat grants " + whole + " energy";

                case RelicEffect.DuelistsRibbon:
                    return "Duelist's Ribbon: the " + Ordinal(Mathf.Max(1, threshold)) +
                           " card you play each turn costs " + whole + " less";

                case RelicEffect.ArchitectsLens:
                    return "Architect's Lens: the first ongoing card you play each combat lasts " +
                           whole + (whole == 1 ? " turn" : " turns") + " longer";

                case RelicEffect.BloodSigil:
                    return "Blood Sigil: the first time you lose " + Mathf.Max(1, threshold) +
                           " or more health in a turn, gain " + whole + " energy";

                case RelicEffect.ScrapSatchel:
                    return "Scrap Satchel: every " + Mathf.Max(1, threshold) +
                           " cards you discard in a turn grants " + whole + " mana";

                case RelicEffect.BrokenCrown:
                    return "Broken Crown: your lowest attribute counts as " + whole +
                           " higher for card effects";

                case RelicEffect.Goblet:
                    return itemName + ": +" + whole + " " + scaling.stat + " each turn, at the cost of " +
                           Count(Mathf.Max(1, threshold)) + " from your opening hand";

                case RelicEffect.TitansGrip:
                    return "Titan's Grip: the first attack you play each turn lands twice";

                case RelicEffect.GrandOrrery:
                    return "Grand Orrery: the first ongoing card to expire each turn fires one last time";

                case RelicEffect.BloodCrown:
                    return "Blood Crown: the first time you fall below half health each combat, gain " +
                           whole + " energy and " + Mathf.Max(1, secondaryMagnitude) + " " + scaling.stat;

                case RelicEffect.LoadedGrimoire:
                    return "Loaded Grimoire: the first card you conjure each turn comes as two, and you keep one";

                case RelicEffect.ReapersLedger:
                    return "Reaper's Ledger: sacrificing an ongoing card grants " + whole + " energy and draws " +
                           Count(Mathf.Max(1, secondaryMagnitude));

                case RelicEffect.MirageEngine:
                    return "Mirage Engine: the " + Ordinal(Mathf.Max(1, threshold)) +
                           " card you play each turn conjures a free random card";

                case RelicEffect.BrokenOath:
                    return "The Broken Oath: Willpower may fall below zero, and your Willpower cards may invert";
            }

            return "";
        }

        /// <summary>A count with its noun, so a relic that hands over one card reads as a card.</summary>
        static string Count(int amount)
        {
            return amount + (amount == 1 ? " card" : " cards");
        }

        /// <summary>The count with its ordinal suffix, matching the way a card prints the same count.</summary>
        static string Ordinal(int number)
        {
            int lastTwo = number % 100;
            if (lastTwo >= 11 && lastTwo <= 13) return number + "th";

            switch (number % 10)
            {
                case 1: return number + "st";
                case 2: return number + "nd";
                case 3: return number + "rd";
                default: return number + "th";
            }
        }
    }
}


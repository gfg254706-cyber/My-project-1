using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// An item that can sit in the player's bag or on their body. A charm, which is an item with no
    /// equipment slot, works while it sits in the bag. Everything else works only while it is worn.
    /// </summary>
    [CreateAssetMenu(fileName = "NewItem", menuName = "Dungeon Cards/Item")]
    public class ItemData : ScriptableObject
    {
        public string itemName = "Item";
        [TextArea] public string description = "";

        [Header("Kind")]
        [Tooltip("Where the item is worn. Carried means it is a charm: it works from a bag slot and is never equipped.")]
        public EquipmentSlot slot = EquipmentSlot.Carried;

        public ItemRarity rarity = ItemRarity.Common;

        [Tooltip("A potion. It is drunk from the bag and is gone afterwards.")]
        public bool consumable;

        [Tooltip("A relic. It has no equipment slot, it works from the bag like a charm, and it is what the " +
                 "shop's relic rack sells. Relics are never sold as ordinary equipment.")]
        public bool relic;

        [Tooltip("Health restored when this item is drunk.")]
        public int healAmount;

        [Header("Attribute bonuses")]
        public int strength;
        public int agility;
        public int intellect;
        public int vitality;
        public int willpower;

        [Header("Direct bonuses")]
        public int maxHealthBonus;
        public int energyBonus;

        [Header("On a cadence")]
        [Tooltip("Adds a card drawn from the card pool every this many encounters. 0 disables it.")]
        public int grantsCardEveryEncounters;

        public int GetBonus(StatType type)
        {
            switch (type)
            {
                case StatType.Strength: return strength;
                case StatType.Agility: return agility;
                case StatType.Intellect: return intellect;
                case StatType.Vitality: return vitality;
                case StatType.Willpower: return willpower;
            }
            return 0;
        }

        /// <summary>True when the item works from a bag slot rather than an equipment slot.</summary>
        public bool IsCharm { get { return slot == EquipmentSlot.Carried; } }

        public string SlotLabel()
        {
            return slot == EquipmentSlot.Carried ? "CHARM" : slot.ToString().ToUpperInvariant();
        }

        public string BonusSummary()
        {
            var parts = new List<string>();
            if (strength != 0) parts.Add(Format(strength, "Strength"));
            if (agility != 0) parts.Add(Format(agility, "Agility"));
            if (intellect != 0) parts.Add(Format(intellect, "Intellect"));
            if (vitality != 0) parts.Add(Format(vitality, "Vitality"));
            if (willpower != 0) parts.Add(Format(willpower, "Willpower"));
            if (maxHealthBonus != 0) parts.Add(Format(maxHealthBonus, "Max Health"));
            if (energyBonus != 0) parts.Add(Format(energyBonus, "Energy"));
            if (healAmount != 0) parts.Add("restores " + healAmount + " health");
            if (grantsCardEveryEncounters > 0) parts.Add("a card every " + grantsCardEveryEncounters + " encounters");
            return parts.Count == 0 ? "No bonuses" : string.Join(", ", parts.ToArray());
        }

        static string Format(int value, string label)
        {
            return (value > 0 ? "+" : "") + value + " " + label;
        }
    }

    /// <summary>
    /// A concrete item the player owns, so equipment state is not written back into the shared asset.
    /// For a relic this is also where the roll lives: the definition is the template, and the numbers on
    /// this object are that one relic's own.
    /// </summary>
    [Serializable]
    public class ItemInstance
    {
        public ItemData data;

        /// <summary>The rolled stat package, for a relic. Null for everything else.</summary>
        public RelicInstance relic;

        /// <summary>Index into Inventory.SlotOrder while worn, or -1 while it sits in a bag slot.</summary>
        public int slotIndex = -1;

        public ItemInstance() { }

        public ItemInstance(ItemData data)
        {
            this.data = data;
            slotIndex = -1;
        }

        public bool Equipped { get { return slotIndex >= 0; } }

        /// <summary>True when this instance carries a rolled stat package.</summary>
        public bool IsRolledRelic { get { return relic != null; } }

        /// <summary>
        /// The item's bonus in one attribute: what the definition guarantees plus what this instance
        /// rolled. This is the one place the two halves are added together, so a relic's identity and its
        /// roll can never be counted twice or dropped.
        /// </summary>
        public int GetStat(StatType type)
        {
            int value = data != null ? data.GetBonus(type) : 0;
            if (relic != null) value += relic.Get(type);

            return value;
        }

        /// <summary>Just the rolled half, which is what a debug line wants to show.</summary>
        public int GetRolledStat(StatType type)
        {
            return relic != null ? relic.Get(type) : 0;
        }

        /// <summary>A relic's tier, or an empty string when this is not a relic.</summary>
        public string RelicTierLabel()
        {
            return relic != null ? RelicInstance.TierLabel(relic.tier) : "";
        }

        /// <summary>
        /// One line per attribute, every one of them listed even at zero, so a relic's costs are as
        /// legible as its benefits. Empty for anything that did not roll.
        /// </summary>
        public string StatLines()
        {
            return relic != null ? relic.DescribeLines() : "";
        }

        /// <summary>
        /// The compact summary the bag, the shop and the sell list all show. A rolled relic sums its
        /// guaranteed stats with its roll; everything else falls back to the definition's own summary,
        /// so no item has to pretend it has a roll.
        /// </summary>
        public string BonusSummary()
        {
            if (data == null) return "";
            if (relic == null) return data.BonusSummary();

            var parts = new List<string>();

            foreach (StatType type in RelicInstance.StatOrder)
            {
                int value = GetStat(type);
                if (value != 0) parts.Add(RelicInstance.Signed(value, RelicInstance.StatLabel(type)));
            }

            if (data.maxHealthBonus != 0) parts.Add((data.maxHealthBonus > 0 ? "+" : "") + data.maxHealthBonus + " Max Health");
            if (data.energyBonus != 0) parts.Add((data.energyBonus > 0 ? "+" : "") + data.energyBonus + " Energy");
            if (data.grantsCardEveryEncounters > 0) parts.Add("a card every " + data.grantsCardEveryEncounters + " encounters");

            // A relic's effect is part of what it is, so a row that only listed the attributes would be
            // describing half the object.
            var definition = data as RelicDefinition;
            if (definition != null && definition.effect != RelicEffect.None)
            {
                string effect = definition.EffectSummary(LiveStats);
                if (!string.IsNullOrEmpty(effect)) parts.Add(effect);
            }

            return parts.Count == 0 ? "No bonuses" : string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// The player's attributes, so a scaling relic advertises the number it would actually produce.
        /// Read from the session rather than passed in, because the rows that draw these summaries are
        /// bound without one.
        /// </summary>
        static PlayerStats LiveStats
        {
            get
            {
                GameSession session = GameSession.Instance;
                return session != null ? session.Stats : null;
            }
        }
    }



    /// <summary>Shared presentation for item rarity, so the bag and the body agree on what rare looks like.</summary>
    public static class ItemRarityStyle
    {
        /// <summary>Warm gold for rare items, so a rare drop reads as special in any list.</summary>
        public static readonly Color RareColor = new Color(0.95f, 0.76f, 0.30f, 1f);

        public static readonly Color CommonColor = new Color(0.72f, 0.76f, 0.84f, 1f);

        public static Color ColorFor(ItemRarity rarity)
        {
            return rarity == ItemRarity.Rare ? RareColor : CommonColor;
        }

        public static string LabelFor(ItemRarity rarity)
        {
            return rarity == ItemRarity.Rare ? "RARE" : "COMMON";
        }

        /// <summary>Prefixes a rare item's name with a marker, so rarity survives plain-text lists too.</summary>
        public static string Decorate(ItemData item)
        {
            if (item == null) return "";
            return item.rarity == ItemRarity.Rare ? "* " + item.itemName : item.itemName;
        }
    }
}

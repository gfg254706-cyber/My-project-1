using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// Run-persistent player state: health, energy, mana, the four attributes and combat status effects.
    /// Attribute values come from a base value plus item bonuses pushed in from the Inventory.
    /// </summary>
    public class PlayerStats
    {
        public const int BaseMaxHealth = 30;
        public const int BaseEnergy = 3;
        public const int StartingHandSize = 4;
        public const int CardsDrawnPerTurn = 1;
        public const int HealthPerVitality = 2;

        /// <summary>
        /// How many constructs can stand on the board at once. The board is drawn as a row of squares, so
        /// this is the width of that row: four to begin with, and ten at the very most.
        /// </summary>
        public const int BaseBoardSlots = 4;
        public const int MaxBoardSlots = 10;

        /// <summary>Raised whenever a value the UI displays changes.</summary>
        public event Action Changed;

        readonly Dictionary<StatType, int> baseStats = new Dictionary<StatType, int>();
        readonly Dictionary<StatType, int> itemStats = new Dictionary<StatType, int>();
        readonly List<StatusEffect> statuses = new List<StatusEffect>();

        /// <summary>
        /// Points of an attribute bought for this combat only, by a relic rather than by a card. Unlike a
        /// status these are not one of a fixed set of kinds, because a Goblet designates whichever
        /// attribute its asset names rather than one a status effect was written for.
        /// </summary>
        readonly Dictionary<StatType, int> combatStats = new Dictionary<StatType, int>();

        /// <summary>
        /// Points an attribute reads as being worth to a card formula and to nothing else. This is Broken
        /// Crown: the lowest attribute counts higher, which should move the numbers printed on cards and
        /// must not move the health pool or the size of the board.
        /// </summary>
        readonly Dictionary<StatType, int> effectStats = new Dictionary<StatType, int>();

        int itemMaxHealthBonus;
        int itemEnergyBonus;
        int permanentMaxHealthBonus;
        int boardSlotBonus;

        /// <summary>
        /// True while something in the player's hands lets Willpower fall below zero. Without it Willpower
        /// has a floor at zero, which is what keeps a sacrifice build from turning its price into a gain.
        /// </summary>
        bool negativeWillpowerAllowed;

        public int Health { get; private set; }

        /// <summary>Combat-only shielding. Absorbs incoming damage and resets at the start of each turn.</summary>
        public int Block { get; set; }

        /// <summary>
        /// The Intellect archetype's second resource. Gained through cards at roughly 3 energy to 1 mana,
        /// spent on board cards, and banked across the turns of a single fight rather than wiped each turn.
        /// </summary>
        public int Mana { get; private set; }

        public bool CanAffordMana(int mana) { return Mana >= mana; }

        public void AddMana(int amount)
        {
            if (amount == 0) return;
            Mana = Mathf.Max(0, Mana + amount);
            Raise();
        }

        public void SpendMana(int amount)
        {
            if (amount <= 0) return;
            Mana = Mathf.Max(0, Mana - amount);
            Raise();
        }

        public IReadOnlyList<StatusEffect> Statuses { get { return statuses; } }

        public PlayerStats()
        {
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                baseStats[type] = 0;
                itemStats[type] = 0;
                combatStats[type] = 0;
                effectStats[type] = 0;
            }
            Health = MaxHealth;
        }

        /// <summary>Health pool: base, plus Vitality, plus item and event bonuses.</summary>
        public int MaxHealth
        {
            get
            {
                return Mathf.Max(1, BaseMaxHealth
                                     + GetStat(StatType.Vitality) * HealthPerVitality
                                     + itemMaxHealthBonus
                                     + permanentMaxHealthBonus);
            }
        }

        /// <summary>
        /// Energy per turn: the base, plus whatever items grant. Intellect deliberately does not feed it.
        /// Nothing turns an attribute into a resource on its own: a stat is only ever the variable part of
        /// a card's formula, which is what CardData.CalculateValue reads, so the conversion lives on cards
        /// and on items rather than in the rules.
        /// </summary>
        public int MaxEnergy
        {
            get { return Mathf.Max(1, BaseEnergy + itemEnergyBonus); }
        }

        /// <summary>Board slots: the base row, plus whatever has widened it.</summary>
        public int BoardSlots
        {
            get { return Mathf.Clamp(BaseBoardSlots + boardSlotBonus, 1, MaxBoardSlots); }
        }

        /// <summary>Widens the board for good, the way an event widens the health pool.</summary>
        public void AddPermanentBoardSlots(int amount)
        {
            boardSlotBonus = Mathf.Max(0, boardSlotBonus + amount);
            Raise();
        }

        public bool IsAlive { get { return Health > 0; } }

        /// <summary>
        /// Attribute value including equipped item bonuses and anything a relic has bought for the length
        /// of this combat.
        /// </summary>
        public int GetStat(StatType type)
        {
            return GetBaseStat(type) + GetItemStat(type) + GetCombatStat(type);
        }

        /// <summary>
        /// The attribute as a card formula reads it: everything GetStat carries, plus the points that only
        /// a card is meant to see. Kept separate from GetStat so Broken Crown can lift the number a card
        /// calculates with without lifting the health pool alongside it.
        /// </summary>
        public int GetCardStat(StatType type)
        {
            int value;
            return GetStat(type) + (effectStats.TryGetValue(type, out value) ? value : 0);
        }

        /// <summary>
        /// The attribute as the cards read it: the permanent value plus any combat status pointing the same
        /// way. Only Agility has one, because Adrenaline and Overclock buy Agility the way Furor buys
        /// Strength, and a card that scales off Agility has to see it.
        /// </summary>
        public int GetEffectiveStat(StatType type)
        {
            int value = GetCardStat(type);
            if (type == StatType.Agility) value += GetStatusMagnitude(StatusEffectType.Agility);
            return value;
        }

        public int GetCombatStat(StatType type)
        {
            int value;
            return combatStats.TryGetValue(type, out value) ? value : 0;
        }

        public int GetEffectStat(StatType type)
        {
            int value;
            return effectStats.TryGetValue(type, out value) ? value : 0;
        }

        /// <summary>Buys a point of an attribute for this combat, as a Goblet or a Blood Crown does.</summary>
        public void AddCombatStatBonus(StatType type, int amount)
        {
            if (amount == 0) return;
            combatStats[type] = GetCombatStat(type) + amount;
            Raise();
        }

        /// <summary>
        /// Replaces what every attribute reads as to a card formula. Cleared and rebuilt in one call rather
        /// than adjusted, because what Broken Crown lifts is whichever attribute is lowest, and that
        /// changes as the other relics and the rest of the fight move the others.
        /// </summary>
        public void SetEffectStatBonuses(Dictionary<StatType, int> bonuses)
        {
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                int value;
                effectStats[type] = (bonuses != null && bonuses.TryGetValue(type, out value)) ? value : 0;
            }
            Raise();
        }

        /// <summary>The attribute with the lowest value right now, ties going to the first one.</summary>
        public StatType LowestStat()
        {
            StatType lowest = RelicInstance.StatOrder[0];
            int lowestValue = GetStat(lowest);

            for (int i = 1; i < RelicInstance.StatOrder.Length; i++)
            {
                StatType candidate = RelicInstance.StatOrder[i];
                int value = GetStat(candidate);
                if (value >= lowestValue) continue;

                lowest = candidate;
                lowestValue = value;
            }

            return lowest;
        }

        /// <summary>
        /// Lifts or restores the floor on Willpower. Only The Broken Oath lifts it; without the relic a
        /// sacrifice card's price can never turn into something a build profits from.
        /// </summary>
        public void SetNegativeWillpowerAllowed(bool allowed)
        {
            if (negativeWillpowerAllowed == allowed) return;
            negativeWillpowerAllowed = allowed;
            Raise();
        }


        public int GetBaseStat(StatType type)
        {
            int value;
            return baseStats.TryGetValue(type, out value) ? value : 0;
        }

        public int GetItemStat(StatType type)
        {
            int value;
            return itemStats.TryGetValue(type, out value) ? value : 0;
        }

        /// <summary>
        /// Raises an attribute permanently, as a level-up or an event reward would.
        ///
        /// Willpower is the one attribute with a floor: a sacrifice card pays with it, and without a floor
        /// the price would eventually turn into a payout. The Broken Oath is the only thing that lifts it.
        /// </summary>
        public void SetBaseStat(StatType type, int value)
        {
            if (type == StatType.Willpower && !negativeWillpowerAllowed) value = Mathf.Max(0, value);

            int previousMaxHealth = MaxHealth;
            baseStats[type] = value;
            ApplyMaxHealthChange(previousMaxHealth);
        }

        /// <summary>Raises the health pool permanently, as an event reward would.</summary>
        public void AddPermanentMaxHealth(int amount)
        {
            int previousMaxHealth = MaxHealth;
            permanentMaxHealthBonus += amount;

            int gained = MaxHealth - previousMaxHealth;
            if (gained > 0) Health += gained;

            Health = Mathf.Clamp(Health, 0, MaxHealth);
            Raise();
        }

        /// <summary>Called by the Inventory whenever items are equipped or unequipped.</summary>
        public void SetItemBonuses(Dictionary<StatType, int> statBonuses, int maxHealthBonus, int energyBonus)
        {
            int previousMaxHealth = MaxHealth;

            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                int value;
                itemStats[type] = (statBonuses != null && statBonuses.TryGetValue(type, out value)) ? value : 0;
            }
            itemMaxHealthBonus = maxHealthBonus;
            itemEnergyBonus = energyBonus;

            ApplyMaxHealthChange(previousMaxHealth);
        }

        /// <summary>Keeps the player at full health when the pool grows, and never above it when it shrinks.</summary>
        void ApplyMaxHealthChange(int previousMaxHealth)
        {
            int newMaxHealth = MaxHealth;

            if (Health >= previousMaxHealth) Health = newMaxHealth;                                   // was at full health
            else Health = Mathf.Clamp(Health + Mathf.Max(0, newMaxHealth - previousMaxHealth), 0, newMaxHealth);

            Raise();
        }

        /// <summary>
        /// Clears combat-only state at the start of an encounter. Health carries over between rooms, but
        /// mana does not: each fight is its own ramp.
        /// </summary>
        public void StartCombat()
        {
            Block = 0;
            Mana = 0;
            StatusEffects.Clear(statuses);

            // What a relic bought for one fight is not carried into the next one, and neither is what it
            // told the card formulas to read.
            foreach (StatType type in Enum.GetValues(typeof(StatType)))
            {
                combatStats[type] = 0;
                effectStats[type] = 0;
            }

            Raise();
        }

        public void TakeDamage(int amount)
        {
            if (amount <= 0) return;
            Health = Mathf.Max(0, Health - amount);
            Raise();
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;
            Health = Mathf.Min(MaxHealth, Health + amount);
            Raise();
        }

        public void FullHeal()
        {
            Health = MaxHealth;
            Raise();
        }

        /// <summary>Applies the player's Vulnerable status to an incoming attack.</summary>
        public int ResolveIncomingDamage(int rawDamage)
        {
            int damage = Mathf.Max(0, rawDamage);
            if (GetStatusMagnitude(StatusEffectType.Vulnerable) > 0) damage = Mathf.RoundToInt(damage * 1.5f);
            return damage;
        }

        public void AddStatus(StatusEffectType type, int magnitude, int duration)
        {
            StatusEffects.Add(statuses, type, magnitude, duration);
            Raise();
        }

        public int GetStatusMagnitude(StatusEffectType type)
        {
            return StatusEffects.Magnitude(statuses, type);
        }

        public void ClearStatuses()
        {
            StatusEffects.Clear(statuses);
            Raise();
        }

        public void TickStatuses()
        {
            StatusEffects.Tick(statuses);
        }

        /// <summary>Applies poison at the start of the player's turn and returns the damage dealt.</summary>
        public int TakePoisonDamage()
        {
            int poison = GetStatusMagnitude(StatusEffectType.Poison);
            if (poison > 0) TakeDamage(poison);
            return poison;
        }

        public string StatusSummary()
        {
            return StatusEffects.Describe(statuses);
        }

        void Raise()
        {
            if (Changed != null) Changed();
        }
    }
}

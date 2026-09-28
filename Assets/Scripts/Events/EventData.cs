using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// What an event option does when chosen. Every field is optional, and each option normally pairs
    /// an upside with a downside.
    /// </summary>
    [Serializable]
    public class EventEffect
    {
        [Header("Health")]
        [Tooltip("Positive heals, negative damages.")]
        public int healthChange;
        [Tooltip("Permanent change to the health pool, on top of Vitality and items.")]
        public int permanentMaxHealth;

        [Header("Attribute")]
        public StatType attribute = StatType.Strength;
        public int attributeChange;

        [Header("Experience")]
        [Tooltip("Positive grants experience, negative spends it (never below zero).")]
        public int experienceChange;

        [Header("Gold")]
        [Tooltip("Positive hands over gold. Chests are one of the only sources of it.")]
        public int goldChange;

        [Header("Cards and items")]
        public ItemData grantItem;
        public CardData grantCard;
        public int grantCardCount;
        public CardData removeCard;
        public int removeCardCount;

        [Header("Rewards rolled at random")]
        [Tooltip("One entry is picked at random and handed over. Use this for chests and other loot.")]
        public List<ItemData> randomItemPool = new List<ItemData>();
        [Tooltip("How many copies of the rolled item are handed over.")]
        public int randomItemAmount = 1;
        [Tooltip("When set, a card drawn from the game database's pool is added to the deck.")]
        public bool grantRandomCard;
        public int grantRandomCardCount = 1;

        [Header("Trouble")]
        [Tooltip("When set, taking this option starts a fight against the given enemy.")]
        public EnemyData startCombat;

        /// <summary>Applies the effect and returns a short description of what actually happened.</summary>
        public string Apply(PlayerStats stats, GameSession session)
        {
            var parts = new List<string>();

            if (healthChange > 0)
            {
                stats.Heal(healthChange);
                parts.Add("+" + healthChange + " health");
            }
            else if (healthChange < 0)
            {
                stats.TakeDamage(-healthChange);
                parts.Add(healthChange + " health");
            }

            if (permanentMaxHealth != 0)
            {
                stats.AddPermanentMaxHealth(permanentMaxHealth);
                parts.Add(Format(permanentMaxHealth, "max health"));
            }

            if (attributeChange != 0)
            {
                stats.SetBaseStat(attribute, stats.GetBaseStat(attribute) + attributeChange);
                parts.Add(Format(attributeChange, attribute.ToString()));
            }

            if (experienceChange > 0)
            {
                session.AddExperience(experienceChange);
                parts.Add("+" + experienceChange + " XP");
            }
            else if (experienceChange < 0)
            {
                int spent = session.SpendExperienceUpTo(-experienceChange);
                parts.Add(spent > 0 ? "-" + spent + " XP" : "no experience to spend");
            }

            if (goldChange > 0)
            {
                session.AddGold(goldChange);
                parts.Add("+" + goldChange + " gold");
            }

            if (grantItem != null) parts.Add(GrantItem(session, grantItem, 1));

            if (randomItemPool != null && randomItemPool.Count > 0)
            {
                ItemData rolled = RollFromPool(randomItemPool);
                if (rolled != null) parts.Add(GrantItem(session, rolled, Mathf.Max(1, randomItemAmount)));
            }

            if (grantRandomCard && grantRandomCardCount > 0)
            {
                CardData card = session.RandomCard();
                if (card != null)
                {
                    session.GrantCard(card, grantRandomCardCount);

                    parts.Add("+" + grantRandomCardCount + " " + card.cardName);
                }
            }

            if (grantCard != null && grantCardCount > 0)
            {
                session.GrantCard(grantCard, grantCardCount);

                parts.Add("+" + grantCardCount + " " + grantCard.cardName);
            }

            if (removeCard != null && removeCardCount > 0)
            {
                int removed = session.LoseCard(removeCard, removeCardCount);

                parts.Add(removed > 0 ? "-" + removed + " " + removeCard.cardName : "no " + removeCard.cardName + " to lose");
            }

            // The fight is only recorded here. Whoever is showing the room starts it once the player
            // has read the outcome.
            if (startCombat != null) session.PendingEncounter = startCombat;

            if (parts.Count == 0) return "Nothing happens.";
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>Hands over an item, or says so plainly when the player has nowhere to put it.</summary>
        static string GrantItem(GameSession session, ItemData item, int amount)
        {
            int taken = 0;
            for (int i = 0; i < amount; i++)
            {
                if (!session.Inventory.Add(item)) break;
                taken++;
            }

            if (taken == 0) return "no room for the " + item.itemName;
            return "gained " + ItemRarityStyle.Decorate(item);
        }

        static ItemData RollFromPool(List<ItemData> pool)
        {
            var candidates = new List<ItemData>();
            foreach (ItemData item in pool)
            {
                if (item != null) candidates.Add(item);
            }

            return candidates.Count == 0 ? null : candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        static string Format(int value, string label)
        {
            return (value > 0 ? "+" : "") + value + " " + label;
        }
    }

    /// <summary>
    /// One possible result of taking an option, chosen against its siblings by weight. This is what
    /// lets a chest be a chest most of the time and a mimic the rest of it.
    /// </summary>
    [Serializable]
    public class EventOutcome
    {
        [Tooltip("Relative chance of this outcome among the option's outcomes.")]
        public int weight = 1;
        [TextArea] public string resultText = "";
        public EventEffect effect = new EventEffect();
    }

    /// <summary>One choice in an event, with the text shown once it has been taken.</summary>
    [Serializable]
    public class EventOption
    {
        public string label = "Option";
        [TextArea] public string resultText = "";
        public EventEffect effect = new EventEffect();

        [Tooltip("Rolled at random when the option is taken. Leave empty for a single guaranteed outcome.")]
        public List<EventOutcome> outcomes = new List<EventOutcome>();

        /// <summary>Picks this option's outcome, falling back to the option's own text and effect.</summary>
        public EventOutcome Roll()
        {
            if (outcomes == null || outcomes.Count == 0)
            {
                return new EventOutcome { resultText = resultText, effect = effect };
            }

            int total = 0;
            foreach (EventOutcome outcome in outcomes)
            {
                if (outcome != null) total += Mathf.Max(0, outcome.weight);
            }

            if (total <= 0) return new EventOutcome { resultText = resultText, effect = effect };

            int roll = UnityEngine.Random.Range(0, total);
            foreach (EventOutcome outcome in outcomes)
            {
                if (outcome == null) continue;
                roll -= Mathf.Max(0, outcome.weight);
                if (roll < 0) return outcome;
            }

            foreach (EventOutcome outcome in outcomes)
            {
                if (outcome != null) return outcome;
            }

            return new EventOutcome { resultText = resultText, effect = effect };
        }
    }

    /// <summary>A text-based event room offering two to four choices, each with upsides and downsides.</summary>
    [CreateAssetMenu(fileName = "NewEvent", menuName = "Dungeon Cards/Event")]
    public class EventData : ScriptableObject
    {
        public string eventName = "Event";
        [TextArea] public string body = "";
        public List<EventOption> options = new List<EventOption>();
    }
}

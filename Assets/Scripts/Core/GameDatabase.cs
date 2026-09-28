using System.Collections.Generic;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// Content lookup for a run: every card, enemy and item, plus what the player starts with.
    /// Loaded from Resources so any scene works when played directly from the editor.
    /// </summary>
    [CreateAssetMenu(fileName = "GameDatabase", menuName = "Dungeon Cards/Game Database")]
    public class GameDatabase : ScriptableObject
    {
        [Header("Content")]
        public List<CardData> allCards = new List<CardData>();
        public List<ItemData> allItems = new List<ItemData>();
        public List<EnemyData> enemies = new List<EnemyData>();
        public List<EventData> events = new List<EventData>();

        [Tooltip("The enemy waiting at the bottom of every dungeon, in the final room.")]
        public EnemyData bossEnemy;

        [Header("Starting run")]
        [Tooltip("The opening deck, dealt exactly as written and in this order. When this holds any cards the " +
                 "pool below is not consulted at all, so leave it empty to go back to a rolled opening deck.")]
        public List<CardData> startingDeck = new List<CardData>();

        [Tooltip("The deck is drawn at random from this pool when a run starts, so every run opens differently. " +
                 "Only used when no deck has been authored above.")]
        public List<CardData> startingDeckPool = new List<CardData>();

        [Tooltip("How many cards the starting deck holds.")]
        public int startingDeckSize = 10;

        [Tooltip("How many of those are guaranteed to cost 1 energy or less, so the first fight is playable.")]
        public int startingDeckGuaranteedCheap = 4;

        public List<ItemData> startingItems = new List<ItemData>();

        [Header("Progression")]
        [Tooltip("Experience needed per level. Level N needs N times this value in total.")]
        public int experiencePerLevel = 100;

        public CardData FindCard(string cardName)
        {
            foreach (CardData card in allCards)
            {
                if (card != null && card.cardName == cardName) return card;
            }
            return null;
        }

        public EnemyData FindEnemy(string enemyName)
        {
            foreach (EnemyData enemy in enemies)
            {
                if (enemy != null && enemy.enemyName == enemyName) return enemy;
            }
            return null;
        }

        /// <summary>True when the card can be paid for with one energy, which makes it an opening play.</summary>
        public static bool IsCheapOpener(CardData card)
        {
            return card != null && card.costType == CardCostType.Energy && card.cost <= 1;
        }
    }
}

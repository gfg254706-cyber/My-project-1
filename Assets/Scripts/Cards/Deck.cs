using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>The player's card pool. Combat copies this into a shuffled draw pile.</summary>
    public class Deck
    {
        public const int StandardSize = 40;

        /// <summary>Raised when cards are added or removed.</summary>
        public event Action Changed;

        readonly List<CardData> cards = new List<CardData>();

        public IReadOnlyList<CardData> Cards { get { return cards; } }

        public int Count { get { return cards.Count; } }

        /// <summary>The deck may hold any number of cards; the standard size is only a reference point.</summary>
        public bool IsStandardSize { get { return cards.Count == StandardSize; } }

        public void Add(CardData card, int amount = 1)
        {
            if (card == null || amount <= 0) return;
            for (int i = 0; i < amount; i++) cards.Add(card);
            NotifyChanged();
        }

        public bool Remove(CardData card, int amount = 1)
        {
            if (card == null || amount <= 0) return false;

            bool removedAny = false;
            for (int i = 0; i < amount; i++)
            {
                int index = cards.IndexOf(card);
                if (index < 0) break;
                cards.RemoveAt(index);
                removedAny = true;
            }

            if (removedAny) NotifyChanged();
            return removedAny;
        }

        /// <summary>Removes up to the requested number of copies and reports how many were actually lost.</summary>
        public int RemoveUpTo(CardData card, int amount)
        {
            if (card == null || amount <= 0) return 0;

            int removed = 0;
            for (int i = 0; i < amount; i++)
            {
                int index = cards.IndexOf(card);
                if (index < 0) break;
                cards.RemoveAt(index);
                removed++;
            }

            if (removed > 0) NotifyChanged();
            return removed;
        }

        public int CountOf(CardData card)
        {
            if (card == null) return 0;
            int count = 0;
            foreach (CardData entry in cards)
            {
                if (entry == card) count++;
            }
            return count;
        }

        public void Clear()
        {
            cards.Clear();
            NotifyChanged();
        }

        /// <summary>A shuffled copy of the deck, used as the draw pile for one encounter.</summary>
        public List<CardData> CreateShuffledPile()
        {
            var pile = new List<CardData>(cards);
            for (int i = pile.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                CardData temp = pile[i];
                pile[i] = pile[j];
                pile[j] = temp;
            }
            return pile;
        }

        public void NotifyChanged()
        {
            if (Changed != null) Changed();
        }
    }
}

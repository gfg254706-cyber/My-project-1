using System.Collections.Generic;
using System.Text;

namespace DungeonCards
{
    /// <summary>
    /// The keyword glossary behind the card tooltips. Descriptions live here rather than on the cards so
    /// a keyword reads identically wherever it appears, and adding one is a single entry.
    ///
    /// Every entry is deliberately one cursory sentence: enough for the player to know what a keyword is
    /// for, without spelling out numbers or timings they are better off discovering by playing. Anything
    /// that has to be exact belongs on the card itself, not here.
    /// </summary>
    public static class CardKeywords
    {
        public const string Strength = "Strength";
        public const string Agility = "Agility";
        public const string Intellect = "Intellect";
        public const string Vitality = "Vitality";
        public const string Willpower = "Willpower";
        public const string Mana = "Mana";
        public const string Energy = "Energy";
        public const string Shielding = "Shielding";
        public const string Draw = "Draw";
        public const string Board = "Board";

        public const string Weak = "Weak";
        public const string Vulnerable = "Vulnerable";
        public const string Poison = "Poison";
        public const string Temporary = "Temporary";
        public const string Purge = "Purge";

        static readonly Dictionary<string, string> glossary = new Dictionary<string, string>
        {
            // ---- attributes
            { Strength,   "Your attack power, and it lifts the attack cards that scale with it." },
            { Agility,    "Your speed, and it lifts the cheap cards that strike and draw." },
            { Intellect,  "Your focus, and it lifts energy and mana cards." },
            { Vitality,   "Your toughness, and it raises your maximum health." },
            { Willpower,  "Your resolve, and it is what a sacrifice card pays with." },

            // ---- resources
            { Energy,     "Pays for most cards, and refills each turn." },
            { Mana,       "A slower resource that builds up over a fight." },
            { Shielding,  "Soaks incoming damage before it reaches your health, then wears off." },
            { Draw,       "Moves cards from your draw pile into your hand." },
            { Board,      "Stays in play and fires again each turn." },

            // ---- status effects. Strength is declared once above, under attributes.
            { Weak,       "Its attacks deal less damage." },
            { Vulnerable, "It takes extra damage." },
            { Poison,     "It loses health at the start of its turn." },
            { Temporary,  "If this card is not played this turn, it is discarded." },
            { Purge,      "This card is permanently removed after being played." }
        };

        /// <summary>The glossary entry for a keyword, or an empty string if it is not one.</summary>
        public static string Describe(string keyword)
        {
            string description;
            return (!string.IsNullOrEmpty(keyword) && glossary.TryGetValue(keyword, out description))
                ? description
                : "";
        }

        public static bool IsKeyword(string keyword)
        {
            return !string.IsNullOrEmpty(keyword) && glossary.ContainsKey(keyword);
        }

        /// <summary>Maps a status effect onto the keyword that explains it.</summary>
        public static string ForStatus(StatusEffectType type)
        {
            switch (type)
            {
                case StatusEffectType.Strength: return Strength;
                case StatusEffectType.Weak: return Weak;
                case StatusEffectType.Vulnerable: return Vulnerable;
                case StatusEffectType.Poison: return Poison;
            }
            return null;
        }

        /// <summary>
        /// The keywords that actually apply to this card, in the order a tooltip should list them:
        /// what it does to the board first, then the resources it touches, then what scales it.
        /// </summary>
        public static List<string> For(CardData card)
        {
            var keywords = new List<string>();
            if (card == null) return keywords;

            switch (card.effect)
            {
                case CardEffect.Block:
                case CardEffect.DamageEqualToBlock:
                    Add(keywords, Shielding);
                    break;
                case CardEffect.Draw:
                    Add(keywords, Draw);
                    break;
                case CardEffect.GainEnergy:
                    Add(keywords, Energy);
                    break;
                case CardEffect.GainMana:
                    Add(keywords, Mana);
                    break;
                case CardEffect.TemporaryStrength:
                    Add(keywords, Strength);
                    break;
            }

            if (card.costType == CardCostType.Mana) Add(keywords, Mana);
            if (card.persistent) Add(keywords, Board);
            if (card.temporary) Add(keywords, Temporary);
            if (card.purge) Add(keywords, Purge);

            if (card.appliesStatus != StatusEffectType.None) Add(keywords, ForStatus(card.appliesStatus));

            // The scaling tag's names are the same words the glossary uses, so this needs no translation table.
            if (card.ScalesWithStat && card.valuePerStatPoint != 0f) Add(keywords, card.scaling.ToString());

            return keywords;
        }

        /// <summary>Renders the tooltip body for a card: one line per keyword that applies to it.</summary>
        public static string BuildTooltipBody(CardData card)
        {
            List<string> keywords = For(card);
            if (keywords.Count == 0)
            {
                return card != null ? card.BuildDescription(null) : "";
            }

            var builder = new StringBuilder();
            for (int i = 0; i < keywords.Count; i++)
            {
                if (builder.Length > 0) builder.Append('\n').Append('\n');
                builder.Append(keywords[i]).Append(": ").Append(Describe(keywords[i]));
            }
            return builder.ToString();
        }

        static void Add(List<string> keywords, string keyword)
        {
            if (string.IsNullOrEmpty(keyword)) return;
            if (keywords.Contains(keyword)) return;
            if (!glossary.ContainsKey(keyword)) return;
            keywords.Add(keyword);
        }
    }
}

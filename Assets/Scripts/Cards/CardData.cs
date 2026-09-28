using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// A playable card. Its effect value is a base plus a contribution from one player attribute, where
    /// the contribution can be fractional so a stat pays off at a legible breakpoint: 0.2 per Intellect
    /// means you get the extra mana at the fifth point.
    ///
    /// A persistent card is played onto the board and fires again at the start of every one of your
    /// turns, instead of being spent. That is what the Intellect archetype is built on: expensive and
    /// slow to set up, but it keeps paying.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCard", menuName = "Dungeon Cards/Card")]
    public class CardData : ScriptableObject
    {
        public string cardName = "Strike";
        [TextArea] public string description = "";

        [Header("Identity")]
        [Tooltip("The build this card is designed to support.")]
        public CardArchetype archetype = CardArchetype.Neutral;

        [Tooltip("How the card is drawn and coloured. Attack, Block or Skill.")]
        public CardType cardType = CardType.Attack;

        [Tooltip("What the card actually does.")]
        public CardEffect effect = CardEffect.Damage;

        [Tooltip("Played onto the board, where it fires again at the start of every one of your turns.")]
        public bool persistent;

        [Header("Presentation")]
        [Tooltip("Optional card art. A flat colour is drawn in the art window when this is empty.")]
        public Sprite art;

        [Header("Cost")]
        public CardCostType costType = CardCostType.Energy;
        [Tooltip("Paid in energy, or in mana if the cost type says so.")]
        public int cost = 1;

        [Header("Effect value")]
        [Tooltip("Value at zero attribute scaling.")]
        public int baseValue = 3;

        [Header("Tags")]
        [Tooltip("The attribute this card's formula reads, or None. Archetype is who the card belongs to; " +
                 "this is which attribute moves its numbers. A construct is tagged to a line and reads " +
                 "nothing, which is what None is for.")]
        public CardStatScaling scaling = CardStatScaling.Strength;

        [Tooltip("Fractional rates are intended: 0.2 gives the first extra point at 5 in the stat.")]
        public float valuePerStatPoint = 1f;

        /// <summary>
        /// True when the card's formula actually reads an attribute.
        ///
        /// There is no separate flag for this: the tag is the flag. The pair of fields this replaces had
        /// scalesWithStat written alongside a scalingStat that was always set, so a construct carried
        /// StatType.Strength and was held harmless only by a zero rate sitting next to it.
        /// </summary>
        public bool ScalesWithStat { get { return scaling != CardStatScaling.None; } }

        /// <summary>
        /// The tag read as an attribute, for the handful of places that have to look a stat up: a hit count
        /// per point, a permanent price per point, gold per point. Only meaningful when the tag is not None,
        /// and only ever asked by a card whose formula already reads one.
        /// </summary>
        public StatType ScalingStat
        {
            get
            {
                switch (scaling)
                {
                    case CardStatScaling.Agility: return StatType.Agility;
                    case CardStatScaling.Intellect: return StatType.Intellect;
                    case CardStatScaling.Vitality: return StatType.Vitality;
                    case CardStatScaling.Willpower: return StatType.Willpower;
                    default: return StatType.Strength;
                }
            }
        }

        [Header("Status effect (optional)")]
        public StatusEffectType appliesStatus = StatusEffectType.None;
        public int statusMagnitude = 0;
        [Tooltip("Turns the effect lasts. Zero or less lasts until the end of the combat.")]
        public int statusDuration = 0;
        public bool statusTargetsSelf = false;

        [Header("Rarity")]
        [Tooltip("1 Rare, 2 Uncommon, 3 Common. The number is the pool weight, so Commons are offered most.")]
        public CardRarity rarity = CardRarity.Common;

        [Header("Repeats")]
        [Tooltip("How many times the effect resolves. Shielding soaks each hit separately, so repeats are " +
                 "weaker against a target that blocks than one big hit of the same total.")]
        public int hits = 1;

        [Tooltip("The number of hits comes from the scaling attribute instead of the fixed count above.")]
        public bool hitsScaleWithStat;

        [Header("Where the number comes from")]
        [Tooltip("Fixed reads the printed value and the attribute. The others read the state of the fight.")]
        public CardValueSource valueSource = CardValueSource.Fixed;

        [Tooltip("Lets the card resolve to a negative number, so a card can ask for the absence of " +
                 "something rather than only its presence. Everything else is clamped at zero.")]
        public bool allowsNegativeValue;

        [Tooltip("While Willpower is below zero this card reads backwards: the same formula with the sign " +
                 "turned round. Willpower is held at zero unless The Broken Oath is carried, so this flag " +
                 "is the card's half of a polarity build.")]
        public bool invertWhenWillpowerNegative;

        [Header("Riders")]
        [Tooltip("Cards drawn when this resolves, on top of what the effect does.")]
        public int drawsCards;

        [Tooltip("Energy gained when this resolves, on top of what the effect does. Momentum Loop.")]
        public int energyGain;

        [Tooltip("Extra value while this is the first card played this turn. Quickdraw.")]
        public int bonusIfFirstCard;

        [Tooltip("Throws the whole hand away when this resolves.")]
        public bool discardsHand;

        [Tooltip("Energy gained for each card this card discarded. Loose Grip.")]
        public int energyPerCardDiscarded;

        [Tooltip("Random cards conjured into the hand for each card this card discarded. Vanishing Act.")]
        public int generatedPerCardDiscarded;

        [Tooltip("Random cards conjured into the hand outright. Wild Card.")]
        public int generatedCards;

        [Tooltip("Returns to the hand instead of being spent, so it can be played again this turn.")]
        public bool returnsToHand;

        [Tooltip("Playable once a turn without spending energy. Split Second.")]
        public bool freeAction;

        [Tooltip("If this card is not played this turn, it is discarded. Temporary.")]
        public bool temporary;

        [Tooltip("This card is permanently removed after being played. Purge.")]
        public bool purge;

        [Tooltip("Gold gained if this card is the one that kills the enemy. Glory.")]
        public int bonusGoldOnKill;

        [Tooltip("Extra gold on the kill, per point of the scaling attribute. Glory.")]
        public float goldPerStatPoint;

        [Tooltip("Damage doubled once this many cards have already been played this turn, this one " +
                 "included. Backstab.")]
        public int doubleIfCardsPlayed;

        [Tooltip("A random card is thrown away at the start of every turn while this is running. Overclock.")]
        public bool discardRandomEachTurn;

        [Tooltip("Health paid at the top of every turn while this is running.")]
        public int selfDamagePerTurn;

        [Header("Permanent price")]
        [Tooltip("Written to the run for good when the card is played. Negative is a price paid, positive " +
                 "is a gain kept. This is the only way a card changes an attribute outside a level-up.")]
        public int permanentStatChange;

        [Tooltip("Extra permanent change for every point of the scaling attribute. Exhaust takes more " +
                 "Strength off the more Willpower it was played with, so the price is the half of a " +
                 "sacrifice card that scales.")]
        public float permanentStatChangePerStatPoint;

        public StatType permanentStatTarget = StatType.Strength;

        [Header("Construct trigger")]
        [Tooltip("What this construct waits for. Only meaningful on a persistent card.")]
        public CardTrigger trigger = CardTrigger.StartOfTurn;

        [Tooltip("Cards that must have been played last turn before a StartOfTurnAfterMany trigger fires. " +
                 "Momentum Loop.")]
        public int triggerCardsPlayedLastTurn;

        [Tooltip("How many of your turns this construct is good for. Zero or less is the rest of the fight, " +
                 "which is what every construct did before durations existed. The Tribunal lasts four.")]
        public int constructDuration;

        /// <summary>
        /// Which construct from the neutral pool this is, and therefore which rulebook drives it. None for
        /// every ordinary card, including the persistent ones written before the pool existed.
        ///
        /// A card with a kind here is authored as CardEffect.Construct and reads no attribute: its numbers
        /// come out of ConstructRules rather than off a stat, which is why its scaling tag is None however
        /// firmly its archetype places it in a line.
        /// </summary>
        [Tooltip("Which neutral-pool construct this is. None for every ordinary card.")]
        public ConstructKind construct = ConstructKind.None;

        /// <summary>
        /// True when the player may give this construct up by hand, by clicking its square on the board.
        ///
        /// This is a fact about the card rather than a fact about a kind, which is the whole point of it. It
        /// used to be a name check in ConstructRules that listed Fuse and Forbidden Engine, and a list like
        /// that works exactly until the next hinge construct is written: a growing family of constructs whose
        /// payoff is the moment they leave belongs on the cards, not in a switch somebody has to remember to
        /// extend. The board asks the card, so a construct is sacrificial the moment its author says it is.
        ///
        /// It is not offered on the whole pool. A board square is where a running construct is read, and
        /// making every one of them a button that destroys the card would be a trap rather than a choice: a
        /// construct is only offered up when giving it up is something it is for.
        /// </summary>
        [Tooltip("The player may click this construct's square to give it up by hand. Fuse and Forbidden Engine.")]
        public bool sacrificable;

        /// <summary>Final effect value for the given player, with no fight state to read.</summary>
        public int CalculateValue(PlayerStats stats)
        {
            return CalculateValue(stats, null);
        }

        /// <summary>
        /// Final effect value for the given player. A stat only ever moves a card whose own formula lists
        /// it: the number is the printed base plus this card's scaling, and nothing else is folded in.
        ///
        /// A card whose wording is about the fight rather than about the player reads its number from the
        /// context instead, which is why the two paths are separate: a Chain Reaction has no printed value
        /// to scale, it has a count of cards.
        /// </summary>
        public int CalculateValue(PlayerStats stats, CardContext context)
        {
            float value = baseValue;

            if (valueSource != CardValueSource.Fixed)
            {
                int count = context != null ? context.cardsInHand : 0;
                int played = context != null ? context.cardsPlayedThisTurn : 0;

                switch (valueSource)
                {
                    case CardValueSource.PerCardInHand: value = baseValue * count; break;
                    case CardValueSource.PerCardPlayed: value = baseValue * played; break;
                    case CardValueSource.EmptyHand: value = baseValue - count; break;
                }

                return Finish(value);
            }

            // Quickdraw pays a little more while it is the turn's opening move.
            if (bonusIfFirstCard != 0 && context != null && context.cardsPlayedThisTurn <= 1)
            {
                value += bonusIfFirstCard;
            }

            if (stats != null)
            {
                // Agility scaling reads the effective value, so Adrenaline moves the cards it was played
                // for. Strength is not folded in here: it is applied below, and only to damage.
                if (ScalesWithStat)
                {
                    // GetCardStat rather than GetStat: Broken Crown lifts what a formula reads without
                    // lifting the health pool, and an Agility formula also has to see Adrenaline.
                    StatType attributeStat = ScalingStat;
                    int attribute = attributeStat == StatType.Agility
                        ? stats.GetEffectiveStat(attributeStat)
                        : stats.GetCardStat(attributeStat);
                    value += attribute * valuePerStatPoint;
                }
                // A stat is the variable part of a card's formula and nothing more, so Strength is not a
                // blanket damage bonus: an attack that does not list Strength hits for the same whether
                // the player has Strength or not. Temporary Strength from a card like Furor therefore
                // only moves the attacks that do list it, which is what keeps an Agility attack an
                // Agility attack. Weak is a condition on the attacker rather than a stat, so it does
                // soften every attack.
                if (effect == CardEffect.Damage || effect == CardEffect.DamageToBlock)
                {
                    if (ScalesWithStat && ScalingStat == StatType.Strength)
                    {
                        value += stats.GetStatusMagnitude(StatusEffectType.Strength);
                    }

                    if (stats.GetStatusMagnitude(StatusEffectType.Weak) > 0) value *= 0.75f;
                }
            }

            // A card that inverts reads backwards while Willpower is below zero: the same formula, with
            // the sign turned round, which is what a negative Willpower build is buying. Only a card that
            // asked for it does this, so an ordinary Willpower card is unaffected.
            if (stats != null && IsInverted(stats)) return -Mathf.FloorToInt(value);

            return Finish(value);
        }

        /// <summary>
        /// True when this card is currently reading backwards. The relic only lifts the floor on
        /// Willpower; which cards do something with a negative reading is a decision each card makes for
        /// itself, and this is the flag it makes it with.
        /// </summary>
        public bool IsInverted(PlayerStats stats)
        {
            return invertWhenWillpowerNegative && stats != null && stats.GetStat(StatType.Willpower) < 0;
        }

        /// <summary>
        /// The final number, and the one place the sign is decided. Everything is clamped at zero except a
        /// card that explicitly asked to be allowed to go negative, which is a card whose wording is about
        /// the absence of something.
        /// </summary>
        int Finish(float value)
        {
            int whole = Mathf.FloorToInt(value);
            return allowsNegativeValue ? whole : Mathf.Max(0, whole);
        }

        /// <summary>How many separate times this card resolves. Dice reads its count off Strength.</summary>
        public int HitsFor(PlayerStats stats, CardContext context)
        {
            if (!hitsScaleWithStat) return Mathf.Max(1, hits);

            int attribute = stats != null ? stats.GetEffectiveStat(ScalingStat) : 0;
            return Mathf.Max(1, attribute);
        }

        /// <summary>
        /// How much the card is gaining from stats and effects above its printed value. The card UI reads
        /// this to decide whether the number is boosted, and therefore whether to colour and bounce it.
        /// </summary>
        public int StatBonus(PlayerStats stats)
        {
            return Mathf.Max(0, CalculateValue(stats) - baseValue);
        }

        /// <summary>
        /// The number the card will actually produce right now. A Ripost deals your current shielding,
        /// which is not anything printed on the card, so it cannot come from CalculateValue.
        /// </summary>
        public int ValueFor(PlayerStats stats)
        {
            return ValueFor(stats, null);
        }

        /// <summary>
        /// The number the card will actually produce right now. A Ripost deals your current shielding and a
        /// Sunder is aimed at the enemy's, neither of which is anything printed on the card, so neither can
        /// come out of CalculateValue alone.
        /// </summary>
        public int ValueFor(PlayerStats stats, CardContext context)
        {
            if (effect == CardEffect.DamageEqualToBlock) return stats != null ? stats.Block : 0;
            return CalculateValue(stats, context);
        }

        /// <summary>The one-line form of the card's effect at the given value.</summary>
        public string Summary(int value)
        {
            switch (effect)
            {
                case CardEffect.Damage:
                    // Repeats are printed on the line, because "deal 7 damage, twice" is a different card
                    // from "deal 14 damage" the moment the enemy can block one of them.
                    return hits > 1 && !hitsScaleWithStat
                        ? "Deal " + value + " damage, " + hits + " times"
                        : "Deal " + value + " damage";

                case CardEffect.Block: return "Gain " + value + " shielding";
                case CardEffect.Draw: return "Draw " + value + (value == 1 ? " card" : " cards");
                case CardEffect.GainEnergy: return "Gain " + value + " energy";
                case CardEffect.GainMana: return "Gain " + value + " mana";
                case CardEffect.TemporaryStrength: return "Gain " + value + " Strength this turn";
                case CardEffect.DamageEqualToBlock: return "Deal " + value + " damage, equal to your shielding";
                case CardEffect.Heal: return "Restore " + value + " health";
                case CardEffect.DamageToBlock: return "Strip " + value + " shielding off the enemy";
                case CardEffect.ReduceEnemyAttack: return "The enemy's next attack is worth nothing";
                case CardEffect.StealStrength: return "Steal " + value + " Strength from the enemy";
                case CardEffect.PruneDrawPile:
                    return "Discard every card in your draw pile that does not scale with Strength, " +
                           "gaining 1 Strength for each";
                case CardEffect.DiscardHand: return "Discard your hand";
                case CardEffect.DiscardOne: return "Discard a card, then draw a card";
                case CardEffect.GenerateCard: return "Generate " + value + (value == 1 ? " random card" : " random cards");
                case CardEffect.LookAtTopCards:
                    return "Turn up the top " + value + " cards of your draw pile: keep one, shuffle one " +
                           "back, discard the rest";

                case CardEffect.NextAttackBonus:
                    return "Your next attack deals " + value + " extra damage";

                case CardEffect.LookAtTopCardsKeepOne:
                    return "Reveal the top " + value + " cards of your draw pile. Choose 1 to draw into " +
                           "your hand. The other " + (value - 1) + " are discarded";

                case CardEffect.StatusOnly:
                    return (statusTargetsSelf ? "Gain " : "Apply ") + statusMagnitude + " " + appliesStatus +
                           (statusDuration == 1 ? " this turn" : " for the rest of the fight");
            }
            return description;
        }

        /// <summary>Text shown on the card, with the effect number already resolved for this player.</summary>
        public string BuildDescription(PlayerStats stats)
        {
            return BuildDescription(stats, null);
        }

        /// <summary>
        /// The card's description with the effect number substituted for markup, so the card UI can colour
        /// the total. The number is located by replacing its own printed value inside the effect line,
        /// which keeps every effect on a single code path.
        /// </summary>
        public string BuildDescription(PlayerStats stats, string valueMarkup)
        {
            return BuildDescription(stats, valueMarkup, null);
        }

        /// <summary>The same description, with the fight state available for the cards that read it.</summary>
        public string BuildDescription(PlayerStats stats, string valueMarkup, CardContext context)
        {
            return BuildDescription(stats, valueMarkup, context, ValueFor(stats, context));
        }

        /// <summary>
        /// The same description for a number that has already been worked out.
        ///
        /// A card is read in two states: at rest it shows the value printed on it, and under the pointer it
        /// shows what the player's attributes make of it. Both go through here, so the second state can
        /// never word itself differently from the first.
        /// </summary>
        public string BuildDescription(PlayerStats stats, string valueMarkup, CardContext context, int value)
        {
            return BuildDescription(stats, valueMarkup, context, value, true);
        }

        /// <summary>
        /// The same description for a construct standing on the board, where the authored duration line is
        /// left off.
        ///
        /// A card in hand has not started counting, so "lasts four turns" is the whole truth about it. A
        /// construct in play has, and the row states how many are actually left, so keeping the authored line
        /// as well would print the same duration twice - once frozen at what it was played for, once as what
        /// remains - and read as a contradiction the moment they differ.
        /// </summary>
        public string BuildDescriptionOnBoard(PlayerStats stats)
        {
            return BuildDescription(stats, null, null, ValueFor(stats, null), false);
        }

        /// <summary>The same description, told whether to state the duration it was played for.</summary>
        public string BuildDescription(PlayerStats stats, string valueMarkup, CardContext context, int value, bool stateDuration)
        {
            string text = Summary(value);

            if (!string.IsNullOrEmpty(valueMarkup) && value > 0)
            {
                text = text.Replace(value.ToString(), valueMarkup);
            }

            // A construct that waits for a free card is not on a cadence, so it must not be told it runs
            // "each turn" the way a sigil does. The Tribunal's line is a whole instruction rather than a
            // number, so it carries its own cadence instead of having one bolted onto the end of it.
            //
            // A construct from the neutral pool is the same case for a stronger reason: its timing is one
            // part of what it does rather than the whole of it, so Overflowing Archive watches the end of
            // the turn while Archive watches the start of it, and neither is "every turn".
            if (persistent && trigger == CardTrigger.StartOfTurn && effect != CardEffect.LookAtTopCardsKeepOne &&
                effect != CardEffect.Construct)
            {
                text += effect == CardEffect.Damage || effect == CardEffect.Block ? " each turn" : " every turn";
            }

            // The tag rather than the attribute: a card's tag names the same word the glossary does, and a
            // card that reads nothing never reaches here.
            if (ScalesWithStat && valuePerStatPoint != 0f)
            {
                text += "\n" + scaling + " +" + valuePerStatPoint.ToString("0.##") + "/pt";
            }

            if (hitsScaleWithStat) text += "\nhits once per point of " + ScalingStat;

            // Where the number comes from when it is not the printed value. Without this a card that
            // counts cards reads as a fixed number, and the player cannot tell that it moves.
            switch (valueSource)
            {
                case CardValueSource.PerCardInHand: text += "\nOne per card in your hand"; break;
                case CardValueSource.PerCardPlayed: text += "\nOne per card played this turn"; break;
                case CardValueSource.EmptyHand: text += "\nOne fewer per card in your hand"; break;
            }

            if (drawsCards > 0) text += "\nDraw " + drawsCards + (drawsCards == 1 ? " card" : " cards");
            if (freeAction) text += "\nFree action, once a turn";

            // The hand is thrown away either by the flag or by the effect itself, and the rider lines
            // below belong to whichever did it. Keying off the flag alone left Loose Grip and Vanishing
            // Act advertising nothing but the discard, with their payoffs unstated.
            if (discardsHand || effect == CardEffect.DiscardHand)
            {
                // An effect-driven discard already says the hand goes on its Summary line, so the line is
                // only added for a card that throws the hand away as a rider on some other effect.
                if (effect != CardEffect.DiscardHand) text += "\nDiscard your hand";

                if (energyPerCardDiscarded > 0) text += "\n" + energyPerCardDiscarded + " energy per card discarded";
                if (generatedPerCardDiscarded > 0) text += "\n" + generatedPerCardDiscarded + " random card per card discarded";
            }

            if (generatedCards > 0 && effect != CardEffect.GenerateCard)
            {
                text += "\nGenerate " + generatedCards + " random cards";
            }

            if (returnsToHand) text += "\nReturns to your hand";
            if (doubleIfCardsPlayed > 0) text += "\nDoubles as your " + Ordinal(doubleIfCardsPlayed) + " card this turn";
            if (bonusGoldOnKill > 0) text += "\n+" + bonusGoldOnKill + " gold if this kills";

            // Glory's second half, and the reason it is a gold engine rather than a bad attack: the payout
            // grows with the attribute the deck is already committed to.
            if (goldPerStatPoint != 0f)
            {
                text += "\n+" + goldPerStatPoint.ToString("0.##") + " gold per point of " + ScalingStat +
                        " when this kills";
            }

            if (bonusIfFirstCard != 0) text += "\n+" + bonusIfFirstCard + " more as your first card of the turn";
            if (energyGain > 0) text += "\nGain " + energyGain + " energy";
            if (allowsNegativeValue) text += "\nCan go negative, which reverses the effect";
            if (invertWhenWillpowerNegative)
            {
                text += IsInverted(stats)
                    ? "\nINVERTED: your Willpower is below zero, so this reads backwards"
                    : "\nInverts if your Willpower falls below zero";
            }
            if (discardRandomEachTurn) text += "\nDiscard a random card each turn";
            if (selfDamagePerTurn > 0) text += "\nLose " + selfDamagePerTurn + " health each turn";
            if (permanentStatChange != 0 || permanentStatChangePerStatPoint != 0f)
            {
                int permanent = PermanentStatChange(stats);
                text += "\nPermanently " + (permanent < 0 ? "" : "+") + permanent + " " + permanentStatTarget;

                // The price is the half of a sacrifice card that scales, so the rate is printed the way a
                // scaled effect prints its own.
                if (permanentStatChangePerStatPoint != 0f)
                {
                    text += "\n" + ScalingStat + " " +
                            (permanentStatChangePerStatPoint > 0f ? "+" : "") +
                            permanentStatChangePerStatPoint.ToString("0.##") + " per point";
                }
            }

            if (stateDuration && persistent && constructDuration > 0) text += "\nLasts " + constructDuration + " turns";
            if (persistent && trigger == CardTrigger.ZeroCostCardPlayed) text += "\nwhenever you play a free card";
            if (persistent && trigger == CardTrigger.StartOfTurnAfterMany)
            {
                text += "\nif you played " + triggerCardsPlayedLastTurn + "+ cards last turn";
            }

            // A status-only card already says this on its first line, so it is not said twice.
            if (effect != CardEffect.StatusOnly && appliesStatus != StatusEffectType.None && statusMagnitude != 0)
            {
                text += "\n";
                text += statusTargetsSelf ? "Gain " : "Apply ";
                text += appliesStatus + " " + statusMagnitude;
                if (statusDuration > 0) text += " for " + statusDuration + " turns";
            }

            return text;
        }

        /// <summary>
        /// The permanent change this card makes to an attribute, with the scaling attribute folded in and
        /// the total rounded to a whole point of the stat.
        ///
        /// This is the one place a Willpower card scales, and it scales the price rather than the effect: a
        /// sacrifice card buys its payoff outright and pays for it out of the run, so Willpower is what
        /// decides how much of you there is to spend.
        /// </summary>
        public int PermanentStatChange(PlayerStats stats)
        {
            if (permanentStatChangePerStatPoint == 0f || stats == null) return permanentStatChange;

            return Mathf.RoundToInt(permanentStatChange + stats.GetCardStat(ScalingStat) * permanentStatChangePerStatPoint);
        }

        /// <summary>True when the card stays on the board instead of being spent.</summary>
        public bool IsBoardCard { get { return persistent; } }

        /// <summary>
        /// The count with its ordinal suffix, so a card that doubles as the third one reads "3rd" rather
        /// than "3th". The count is authored per card, so the wording cannot be hard-coded.
        /// </summary>
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

    /// <summary>
    /// The state of the fight that a card's number can be read from. Only the cards whose wording is about
    /// the fight rather than about the player use it: how full the hand is, how many cards have already
    /// been played this turn, and how much shielding is standing in the way.
    /// </summary>
    public class CardContext
    {
        public int cardsInHand;
        public int cardsPlayedThisTurn;
        public int enemyBlock;
    }
}

using System.Collections.Generic;

namespace DungeonCards
{
    /// <summary>
    /// A fight in progress, held by the GameSession so that it outlives the scene showing it.
    ///
    /// The combat scene is unloaded whenever the player opens the character menu, and a fresh
    /// CombatManager is built when they come back. Without this the encounter would be rolled again:
    /// the enemy would be back to full health, the hand would be redrawn and the board would be gone.
    /// Keeping the fight here is what lets the character menu be the one screen it is everywhere else
    /// rather than a cut-down version of itself.
    /// </summary>
    /// <summary>
    /// A card sitting in play, and how much longer it is good for. This is what a construct with a duration
    /// is: the card is spent once its turns are used up, where a sigil runs until the fight ends.
    /// </summary>
    public class BoardCard
    {
        /// <summary>How long a construct with no duration of its own is good for.</summary>
        public const int RestOfFight = -1;

        public CardData card;

        /// <summary>Turns still to come, or RestOfFight. It only ever counts down, so it is spent at zero.</summary>
        public int turnsLeft = RestOfFight;

        // ---- the neutral construct pool's own state.
        //
        // These live on the card standing in play rather than on the card asset, for the same reason a
        // relic's roll lives on the item: two Fuses are two different objects, and a card is shared by
        // every copy of it in the game.

        /// <summary>
        /// What the construct has stored up. What a Charge is worth is the construct's own business: an
        /// energy each for the Generator, a swing each for the Timers.
        /// </summary>
        public int charges;

        /// <summary>Overheated Core's escalating price for staying useful.</summary>
        public int heat;

        /// <summary>
        /// How many times this construct has done its own thing since it was played. Overheated Core reads
        /// it to know that its first turn is free, and it is kept for any construct that needs to know
        /// whether it has fired yet.
        /// </summary>
        public int triggers;

        /// <summary>
        /// Set on a construct the Relay Node has already woken this turn. One construct, once a turn, which
        /// is what stops two relays from bouncing a single construct back and forth.
        /// </summary>
        public bool relayTriggeredThisTurn;

        /// <summary>
        /// The archetypes Adaptive Core has already been paid for. Held per construct rather than per fight,
        /// because the construct only sees the cards played after it arrived and cannot be paid twice for
        /// the same archetype.
        /// </summary>
        public readonly HashSet<CardArchetype> chargedArchetypes = new HashSet<CardArchetype>();
    }

    /// <summary>One card's cost, moved by a relic for as long as the discount lasts.</summary>
    public class CardCostModifier
    {
        public CardData card;
        public int delta;
    }

    public class CombatState
    {
        public EnemyData enemy;

        public int enemyHealth;
        public int enemyBlock;
        public int enemyTurnIndex;
        public int playerEnergy;

        /// <summary>
        /// Cards played this turn, and the count the previous turn finished on. Several cards read one or
        /// the other, so both are kept rather than being derived from the discard pile, which cannot tell
        /// one turn from the next.
        /// </summary>
        public int cardsPlayedThisTurn;
        public int cardsPlayedLastTurn;

        /// <summary>Cards played by the free action, which is allowed once a turn.</summary>
        public bool freeActionUsed;

        /// <summary>
        /// Strength taken off the enemy by Intimidate. It is subtracted from every attack the enemy makes
        /// for the rest of the fight, because an enemy has no attributes of its own to reduce.
        /// </summary>
        public int enemyAttackReduction;

        /// <summary>Set by Zone. The enemy's next attack is worth nothing, then this clears.</summary>
        public bool enemyAttackNullified;

        /// <summary>
        /// Health paid at the top of every turn for as long as the fight lasts, accumulated from cards that
        /// buy something now and bill you later. Reckless Stance.
        /// </summary>
        public int upkeepDamage;

        /// <summary>True while a card like Overclock is running, which throws a card away every turn.</summary>
        public bool discardEachTurn;

        /// <summary>
        /// Damage banked for the next attack card played, and the card that promised it. It is read by the
        /// card that collects it, so it never survives the attack it was bought for.
        /// </summary>
        public int nextAttackBonus;
        public string nextAttackBonusSource;

        /// <summary>
        /// The relics whose once-a-combat trigger has already paid out in this fight, held as the effect's
        /// own value. The fight remembers that a relic fired without having to know anything about which
        /// relic it was, and a fresh CombatState is what makes every fight start with none of them spent.
        /// </summary>
        public readonly HashSet<int> relicFiredThisCombat = new HashSet<int>();

        /// <summary>
        /// The same for the relics allowed to pay out once a turn. The turn boundary clears it, so "once a
        /// turn" is the shape of the call rather than a flag anyone has to remember to reset.
        /// </summary>
        public readonly HashSet<int> relicFiredThisTurn = new HashSet<int>();

        /// <summary>
        /// One-off discounts on individual cards, in energy.
        ///
        /// They hang off the fight rather than off the card, because a card is a shared asset: a Goblet
        /// discounting this run's copy of Strike must not rewrite Strike for every other run. A modifier
        /// is matched by reference and spent on the play it paid for, so two copies of the same card in
        /// one hand share the discount between them rather than each claiming it.
        /// </summary>
        public readonly List<CardCostModifier> costModifiers = new List<CardCostModifier>();

        /// <summary>Health lost so far this turn, for the relics that wait to see a big enough hit.</summary>
        public int hpLostThisTurn;

        /// <summary>Cards discarded so far this turn, for the relics that pay by the handful.</summary>
        public int discardsThisTurn;

        /// <summary>
        /// The same count for the turn that has just ended. Reclamation Engine reads it at the top of the
        /// turn, which is the one moment the previous turn's total still exists as a separate number.
        /// </summary>
        public int discardsLastTurn;

        /// <summary>
        /// The last ordinary card played this turn, for Temporal Anchor to resolve again at the top of the
        /// next one. A construct is deliberately not recorded: repeating one would mean standing a second
        /// copy on the board, which is not what repeating the effect of a card means.
        /// </summary>
        public CardData lastCardPlayedThisTurn;

        /// <summary>
        /// True while a card is being resolved by something other than being played. Temporal Anchor reads
        /// it so a repeat cannot open a second repeat, which is a crash guard rather than a rule.
        /// </summary>
        public bool replayingCard;

        /// <summary>
        /// True while a construct's last act is being re-run by a Successor Protocol, for the same reason and
        /// in the same shape as replayingCard above.
        ///
        /// A copy is itself a construct triggering, so without this a second Successor Protocol would hear the
        /// first one's copy and copy that, and two of them on one board would hand each other work until the
        /// chain depth cut it off. The pool is protected twice over today - Successor Protocol has no final
        /// trigger of its own to be copied, and only a departure is ever copied - and both of those are facts
        /// about which cards happen to exist rather than a rule. The hinge family this pool is being built
        /// towards is exactly the family where a construct's leaving is its payoff, so the clamp is written
        /// down here rather than left resting on the roster.
        /// </summary>
        public bool copyingFinalTrigger;

        /// <summary>True when the card a standing pick is keeping is meant to be free. Mirage Engine.</summary>
        public bool pickCostsNothing;

        /// <summary>
        /// Cards removed from the game entirely (purged). These are placed in a hidden removed pile
        /// and are not in any other pile (draw, discard, board).
        /// </summary>
        public readonly List<CardData> removedPile = new List<CardData>();


        /// <summary>
        /// The cards an interactive pick has turned up, and which decision it is waiting on. Held here rather
        /// than on the CombatManager because the combat scene is unloaded whenever the character menu is
        /// opened: a pick waiting on a click has to still be waiting when the player comes back.
        /// </summary>
        public readonly List<CardData> pickPending = new List<CardData>();

        public PendingPick pickStage;

        /// <summary>The card that opened the standing pick, so the pick's own lines are attributed to it.</summary>
        public string pickSource;

        public bool isPlayerTurn;
        public bool combatOver;

        /// <summary>True once the fight has been won or lost, so the next one starts fresh.</summary>
        public bool finished;

        /// <summary>
        /// True while the enemy's turn is waiting on its delay. That delay is real time the player can
        /// leave through, and the coroutine that would have spent it is destroyed with the scene, so the
        /// turn is picked back up from here rather than being silently skipped.
        /// </summary>
        public bool enemyTurnPending;

        public bool started;

        public readonly List<CardData> hand = new List<CardData>();
        public readonly List<CardData> drawPile = new List<CardData>();
        public readonly List<CardData> discardPile = new List<CardData>();
        public readonly List<BoardCard> board = new List<BoardCard>();
        public readonly List<StatusEffect> enemyStatuses = new List<StatusEffect>();
        public readonly List<string> log = new List<string>();
    }
}

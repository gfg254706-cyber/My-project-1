namespace DungeonCards
{
    /// <summary>The four player attributes. Items, cards and status effects read these.</summary>
    public enum StatType
    {
        Strength,
        Agility,
        Intellect,
        Vitality,

        /// <summary>
        /// The sacrifice archetype's attribute. It is not a resource and it lifts no effect directly: a
        /// Willpower card scales what it takes off you, so committing to the stat buys a bigger thing to
        /// give up rather than a bigger number on the same thing.
        /// </summary>
        Willpower
    }

    /// <summary>Temporary combat effects applied to a combatant.</summary>
    public enum StatusEffectType
    {
        None,
        Strength,   // +1 damage per point on attack cards while active
        Agility,    // +1 to every Agility scaling card's formula while active
        Weak,       // attacks deal 25% less damage while active
        Vulnerable, // takes 50% more damage while active
        Poison      // loses N health at the start of the owner's turn
    }

    public enum CardType
    {
        Attack,
        Block,
        Skill
    }

    /// <summary>
    /// What a card actually does when it resolves. CardType is how the card is drawn; this is the
    /// mechanic behind it.
    /// </summary>
    public enum CardEffect
    {
        Damage,              // deals the card's value, applied against the enemy's shielding first
        Block,               // grants the card's value as shielding
        Draw,                // draws the card's value in cards
        GainEnergy,          // adds to this turn's energy
        GainMana,            // adds to the combat's mana
        TemporaryStrength,   // adds Strength for the rest of this turn only
        DamageEqualToBlock,  // deals damage equal to your current shielding
        Heal,

        // ---- the set added for the Strength and Agility lines
        DamageToBlock,       // strips the card's value off the enemy's shielding, and never touches health
        ReduceEnemyAttack,   // the enemy's next attack is worth nothing
        StealStrength,       // takes Strength off the enemy and keeps it, for the rest of the fight
        PruneDrawPile,       // removes the unscaled cards from the draw pile, one Strength each
        DiscardHand,         // empties your hand, paying out per card thrown away
        DiscardOne,          // throws one card away, then draws (Read the Room)
        GenerateCard,        // conjures random cards straight into the hand
        LookAtTopCards,      // reads the top of the draw pile and keeps the best of it
        StatusOnly,          // the card is entirely its status: Adrenaline, Overclock

        // ---- the set added for the construct and Willpower lines
        NextAttackBonus,     // banks damage for the next attack card played; Reckless Stance
        LookAtTopCardsKeepOne, // reads the top of the draw pile and draws the one worth keeping;
                             // The Tribunal

        /// <summary>
        /// A construct from the neutral pool, whose whole behaviour is the ConstructKind on its card rather
        /// than anything here.
        ///
        /// Nothing in ResolveEffect reads this, and nothing should: a construct's numbers turn on its own
        /// Charges, on the size of the hand and on what the rest of the turn has done, so there is no single
        /// value to resolve. Driving one is the board pass's job. This member exists so such a card cannot be
        /// mistaken for one whose effect was never set.
        /// </summary>
        Construct
    }

    /// <summary>
    /// Which construct from the neutral pool a card is, and therefore which rulebook drives it.
    ///
    /// A construct is authored as CardEffect.Construct and named here, rather than getting one CardEffect
    /// member each. The difference matters because a construct is not a single resolution: Fuse charges on
    /// one timing and detonates on another, and Overheated Core does something different on each of its four
    /// turns. One member per construct keeps that whole sequence in one readable place instead of spreading
    /// it over an effect switch and four trigger flags.
    ///
    /// Constructs scale with nothing. They are the neutral pool the rest of a deck is built around, so a
    /// construct that grew with an attribute would belong to that attribute's line instead.
    /// </summary>
    public enum ConstructKind
    {
        /// <summary>Not a construct. Every ordinary card, including the persistent ones written before this pool.</summary>
        None,

        // ---- the Generator
        DormantEngine,

        // ---- the Amplifier
        RelayNode,

        // ---- the Timers
        Fuse,
        OverheatedCore,

        // ---- the Reactors
        ReclamationEngine,
        TemporalAnchor,

        // ---- the Converter
        OverflowingArchive,

        // ---- the Conditional and Puzzle constructs
        Archive,
        PerfectAlignment,
        AdaptiveCore,
        Prototype,

        // ---- the Sacrifice constructs
        ForbiddenEngine,
        SuccessorProtocol,

        // ---- the Gambler
        RouletteCore
    }

    /// <summary>
    /// What a construct is being asked to do, which is not the same as when it is allowed to do it: Fuse
    /// charges at the start of the turn and detonates when it leaves the board, and both are the same card.
    ///
    /// Everything that wakes a construct goes through one of these, so a construct is written against a
    /// timing rather than against whichever piece of the fight happens to call it.
    /// </summary>
    public enum ConstructTiming
    {
        /// <summary>The board pass at the top of the player's turn, before the draw.</summary>
        StartOfTurn,

        /// <summary>The moment the player hands the turn over, before anything is cleared.</summary>
        EndOfTurn,

        /// <summary>A card has just been played and paid for.</summary>
        CardPlayed,

        /// <summary>Another construct did something. Relay Node.</summary>
        OtherTriggered,

        /// <summary>Another construct left the board, by expiring or by being sacrificed.</summary>
        OtherLeft,

        /// <summary>Its own duration ran out.</summary>
        Expire,

        /// <summary>The player gave it up by hand.</summary>
        Sacrifice
    }

    /// <summary>
    /// How rare a card is. The number is the pool weight, so Common is the one that shows up most and the
    /// scale reads as a cost: the rarer a card, the fewer of them there are to find.
    /// </summary>
    public enum CardRarity
    {
        Rare = 1,
        Uncommon = 2,
        Common = 3
    }

    /// <summary>
    /// Where a card's number comes from when it is not simply its printed value. A card whose wording is
    /// about the state of the fight rather than about the player's attributes reads its number from here.
    /// </summary>
    public enum CardValueSource
    {
        Fixed,               // baseValue plus the scaling attribute
        PerCardInHand,       // value per card currently held (Fight or Flight, Chain Reaction's mirror)
        PerCardPlayed,       // value per card already played this turn (Chain Reaction)
        EmptyHand            // baseValue minus the cards in hand, never below zero (Empty Pockets)
    }

    /// <summary>
    /// What makes a construct fire. A construct is a card that stays in play; the difference between them
    /// is what they are waiting for.
    /// </summary>
    public enum CardTrigger
    {
        StartOfTurn,           // the plain board card: fires every turn, like every Intellect sigil
        ZeroCostCardPlayed,    // Reflex Guard, Twin Blades: paid out every time a free card is played
        StartOfTurnAfterMany   // Momentum Loop: only fires if enough cards were played last turn
    }

    /// <summary>
    /// A decision a card has opened that the fight is waiting on. While one of these is standing, the
    /// cards it turned up are the only thing that can be clicked.
    /// </summary>
    public enum PendingPick
    {
        None,

        /// <summary>Palm Trick: one of the cards turned up is kept in hand.</summary>
        KeepToHand,

        /// <summary>Palm Trick: one of the cards left over is put back into the draw pile.</summary>
        Reshuffle,

        /// <summary>
        /// The Tribunal: one of the cards turned up is drawn into the hand and every other one of them is
        /// spent. There is no second question, so the pick closes the moment this one is answered.
        /// </summary>
        DrawOneDiscardRest,

        /// <summary>
        /// Loaded Grimoire: two cards were conjured rather than one, and one of them is kept. The cards
        /// came from nowhere rather than off the pile, so the pick holds them directly.
        /// </summary>
        KeepGeneratedOne
    }

    /// <summary>Which resource a card is paid with.</summary>
    public enum CardCostType
    {
        Energy,
        Mana
    }

    /// <summary>
    /// The build a card belongs to. A card of any archetype can be in any deck; this is what the card
    /// is designed to support, and what the reward screen leans on to keep a deck coherent.
    /// </summary>
    public enum CardArchetype
    {
        Neutral,
        Strength,
        Agility,
        Intellect,
        Willpower
    }

    /// <summary>
    /// Which attribute a card's formula reads, or nothing at all.
    ///
    /// This is deliberately a different question from CardArchetype. An archetype is who a card belongs to:
    /// which build it is designed to support, and the line it is grouped under. This is which attribute moves
    /// its numbers. The two come apart the moment constructs exist, because every construct in the neutral
    /// pool belongs to a line and scales with nothing: a Relay Node is an Intellect card, and no attribute
    /// moves a single number on it.
    ///
    /// The pair of fields this replaces could not say that. A construct recorded StatType.Strength and relied
    /// on a zero rate to mean "nothing", so its asset read as a Strength card that happened to have gone
    /// quiet - which is the raw stat-scaling this tag exists to retire.
    /// </summary>
    public enum CardStatScaling
    {
        /// <summary>The formula reads no attribute: every construct, and every card that counts cards instead.</summary>
        None,

        Strength,
        Agility,
        Intellect,
        Vitality,
        Willpower
    }

    /// <summary>What waits inside a room on the dungeon map.</summary>
    public enum RoomType
    {
        Combat,
        Event,
        Vantage,
        Shop,
        Exit,
        Boss
    }

    /// <summary>Where a piece of equipment is worn. Carried means it is in a bag slot instead.</summary>
    public enum EquipmentSlot
    {
        Carried,
        Helmet,
        Chest,
        Trousers,
        Boots,
        Ring
    }

    /// <summary>How rare an item is. Rare items are drawn in their own colour.</summary>
    public enum ItemRarity
    {
        Common,
        Rare
    }
}

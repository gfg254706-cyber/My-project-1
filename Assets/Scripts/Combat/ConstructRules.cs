namespace DungeonCards
{
    /// <summary>
    /// The rulebook for the neutral construct pool: every number the fourteen constructs are made of, and
    /// the small decisions about what wakes them.
    ///
    /// The numbers live here rather than as fields on CardData because a construct is a sequence rather than
    /// a value. Fuse is a rate and a detonation, Overheated Core is a gain that turns into a cost on its
    /// second turn, and Prototype pays two currencies per Charge. Spreading those over a dozen loosely named
    /// fields would put half of each card's behaviour on the asset and half in the fight, so instead the
    /// asset carries what a card always has - its cost, its duration, its kind - and its rulebook is one
    /// readable block here.
    ///
    /// Nothing in this pool scales with an attribute. That is the point of it: a construct is a fixed thing
    /// the rest of a deck is built around, so a construct that grew with Strength would be a Strength card.
    /// </summary>
    public static class ConstructRules
    {
        /// <summary>
        /// How deep a chain of constructs waking each other may go before it is cut off.
        ///
        /// The relay waking a construct that wakes the relay is the mechanic, not a mistake, so this is not a
        /// rule: it is a guard so that a board the player has stacked cannot spin forever. It is set well
        /// above anything the pool can reach honestly.
        /// </summary>
        public const int MaxChainDepth = 8;

        // ------------------------------------------------------------------ Generator

        /// <summary>Dormant Engine: Charges stored per turn, whatever the hand is doing.</summary>
        public const int DormantChargePerTurn = 1;

        /// <summary>
        /// Dormant Engine: it stays quiet while the hand is at least this full. Holding cards is how the
        /// stock is built, so the card wants a hand too big to be cashed in.
        /// </summary>
        public const int DormantHandCeiling = 5;

        /// <summary>Dormant Engine: what one stored Charge is worth when it finally cashes in.</summary>
        public const int DormantEnergyPerCharge = 1;

        // ------------------------------------------------------------------ Amplifier

        /// <summary>Relay Node: Charges gained each time another construct does something.</summary>
        public const int RelayChargePerTrigger = 1;

        /// <summary>Relay Node: the count that spends itself on an extra trigger.</summary>
        public const int RelayChargeTarget = 3;

        // ------------------------------------------------------------------ Timers

        /// <summary>Fuse: Charges gained for each turn it survives.</summary>
        public const int FuseChargePerTurn = 1;

        /// <summary>Fuse: what one Charge is worth when it goes off, by expiry or by the player's hand.</summary>
        public const int FuseDamagePerCharge = 8;

        /// <summary>Overheated Core: mana handed over at the top of every turn.</summary>
        public const int OverheatedManaPerTurn = 2;

        /// <summary>Overheated Core: Heat gained each turn after its first, which is the turn that is free.</summary>
        public const int OverheatedHeatPerTurn = 1;

        /// <summary>Overheated Core: the Heat it can stand before it starts burning the player.</summary>
        public const int OverheatedHeatThreshold = 3;

        /// <summary>Overheated Core: what it burns the player for, every turn, once it is at that Heat.</summary>
        public const int OverheatedDamage = 10;

        // ------------------------------------------------------------------ Reactors

        /// <summary>Reclamation Engine: how much has to have been thrown away last turn to be worth a card.</summary>
        public const int ReclamationDiscardsRequired = 3;

        /// <summary>Reclamation Engine: how many cards come back out of the discard pile.</summary>
        public const int ReclamationCardsReturned = 1;

        // ------------------------------------------------------------------ Converter

        /// <summary>Overflowing Archive: the hand size above which the surplus is worth mana.</summary>
        public const int OverflowHandThreshold = 7;

        /// <summary>Overflowing Archive: mana per card held above that size.</summary>
        public const int OverflowManaPerCard = 1;

        // ------------------------------------------------------------------ Conditional and Puzzle

        /// <summary>Archive: the exact hand size it wants to see.</summary>
        public const int ArchiveHandExactly = 3;

        /// <summary>Archive: what it draws when the hand is exactly that.</summary>
        public const int ArchiveDrawCount = 2;

        /// <summary>Perfect Alignment: the exact hand size it wants to see.</summary>
        public const int AlignmentHandExactly = 5;

        /// <summary>Perfect Alignment: what it draws when the hand is exactly that.</summary>
        public const int AlignmentDrawCount = 2;

        /// <summary>Perfect Alignment: turns of duration lost each time the hand is wrong.</summary>
        public const int AlignmentDurationLost = 1;

        /// <summary>Adaptive Core: the Charges that spend themselves on a payout.</summary>
        public const int AdaptiveChargeTarget = 4;

        /// <summary>Adaptive Core: what one Charge is worth when it pays out.</summary>
        public const int AdaptiveEnergyPerCharge = 1;

        /// <summary>Prototype: the energy cost a played card has to reach to be worth a Charge.</summary>
        public const int PrototypeEnergyCostThreshold = 2;

        /// <summary>Prototype: what one Charge is worth when it is finished, in energy.</summary>
        public const int PrototypeEnergyPerCharge = 3;

        /// <summary>Prototype: the same Charge read as mana.</summary>
        public const int PrototypeManaPerCharge = 2;

        // ------------------------------------------------------------------ Sacrifice

        /// <summary>Forbidden Engine: mana handed over at the top of every turn.</summary>
        public const int ForbiddenManaPerTurn = 2;

        /// <summary>Forbidden Engine: shielding bought by giving it up.</summary>
        public const int ForbiddenBlockOnSacrifice = 15;

        /// <summary>Forbidden Engine: energy bought by giving it up.</summary>
        public const int ForbiddenEnergyOnSacrifice = 2;

        // ------------------------------------------------------------------ Gambler

        /// <summary>Roulette Core: the shielding it can roll.</summary>
        public const int RouletteBlock = 3;

        /// <summary>Roulette Core: the damage it can roll.</summary>
        public const int RouletteDamage = 5;

        /// <summary>Roulette Core: the cards it can roll.</summary>
        public const int RouletteDraws = 1;

        // ------------------------------------------------------------------ questions about a construct

        // Whether a construct may be given up by hand is deliberately not asked here. It used to be - a name
        // check naming Fuse and Forbidden Engine - and it now lives on the card as CardData.sacrificable,
        // because the answer is a property of the construct rather than of the pool's roster. A hinge
        // construct whose payoff is the moment it leaves is a family this pool is being built towards, and a
        // family cannot be a list in a switch.

        /// <summary>
        /// True when this construct does something as it leaves the board, either by running out or by being
        /// given up. This is what Successor Protocol has to be able to copy, so a construct with nothing to
        /// copy is not offered to it.
        /// </summary>
        public static bool HasFinalTrigger(ConstructKind kind)
        {
            return kind == ConstructKind.Fuse ||
                   kind == ConstructKind.Prototype ||
                   kind == ConstructKind.ForbiddenEngine;
        }

        /// <summary>
        /// True when this construct can be woken on its own rather than only by something else.
        ///
        /// Relay Node and Successor Protocol are the two that cannot: neither has a turn of its own, they only
        /// answer other constructs. It is what keeps them out of the Relay Node's own random target, and out
        /// of the relic that offers a construct a chance to fire again.
        /// </summary>
        public static bool HasOwnTrigger(ConstructKind kind)
        {
            return kind != ConstructKind.None &&
                   kind != ConstructKind.RelayNode &&
                   kind != ConstructKind.SuccessorProtocol;
        }

        /// <summary>
        /// What fires this construct when something wakes it out of turn, which is what the Relay Node's extra
        /// trigger does.
        ///
        /// For almost all of them it is the start-of-turn pass, because that is where their own cadence is.
        /// The three that read something else are named here so the relay does not have to know about them:
        /// an Adaptive Core can only be woken by a card, an Overflowing Archive by the end of the turn, and a
        /// Timer by its own start-of-turn pulse rather than by its detonation, so that a relay feeding a Fuse
        /// banks it another Charge instead of spending the construct.
        /// </summary>
        public static ConstructTiming PrimaryTiming(ConstructKind kind)
        {
            switch (kind)
            {
                case ConstructKind.AdaptiveCore:
                case ConstructKind.Prototype:
                    return ConstructTiming.CardPlayed;

                case ConstructKind.OverflowingArchive:
                    return ConstructTiming.EndOfTurn;

                default:
                    return ConstructTiming.StartOfTurn;
            }
        }
    }
}

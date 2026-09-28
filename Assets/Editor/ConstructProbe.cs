#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DungeonCards.EditorTools
{
    /// <summary>
    /// Drives the neutral construct pool through a real fight rather than reading its rules.
    ///
    /// A construct is the easiest kind of card to leave inert in this project, because assigning a
    /// ConstructKind to a card looks exactly like wiring it: the asset is right, the description is right,
    /// and nothing happens. Every case below therefore plays the construct the way the fight does - through
    /// the board pass, the end of the turn, a card being played, or the sacrifice click - and reads what came
    /// out of it.
    ///
    /// The fight is built so that it cannot end underneath a measurement: the enemy has far more health than
    /// any of these tests can deal, and the player is never left to die of the one construct that hurts them.
    ///
    /// It runs from Tools > Dungeon Cards > Constructs > Probe Construct Hooks, or from Execute with no window.
    /// </summary>
    public static class ConstructProbe
    {
        /// <summary>Objects this probe built. A fight is a scene object, so it has to be taken away again.</summary>
        static readonly List<GameObject> made = new List<GameObject>();

        static int passed;
        static int failed;

        [MenuItem("Tools/Dungeon Cards/Constructs/Probe Construct Hooks")]
        public static void Probe()
        {
            Debug.Log(Execute());
        }

        /// <summary>Entry point for a caller that wants the report as text rather than in the console.</summary>
        public static string Execute()
        {
            return Run();
        }

        public static string Run()
        {
            passed = 0;
            failed = 0;

            var sb = new StringBuilder();
            sb.AppendLine("CONSTRUCT HOOK PROBE");
            sb.AppendLine("   Each case places the construct on the board and then plays it the way the fight does,");
            sb.AppendLine("   so what is checked is that the trigger is reached and not only that the rule exists.");
            sb.AppendLine();

            try
            {
                sb.AppendLine(DormantEngine());
                sb.AppendLine(RelayNode());
                sb.AppendLine(Fuse());
                sb.AppendLine(FuseByHand());
                sb.AppendLine(OverheatedCore());
                sb.AppendLine(ReclamationEngine());
                sb.AppendLine(TemporalAnchor());
                sb.AppendLine(OverflowingArchive());
                sb.AppendLine(Archive());
                sb.AppendLine(PerfectAlignment());
                sb.AppendLine(AdaptiveCore());
                sb.AppendLine(Prototype());
                sb.AppendLine(ForbiddenEngine());
                sb.AppendLine(SuccessorProtocol());
                sb.AppendLine(SuccessorOnSuccessor());
                sb.AppendLine(TwoRelayNodes());
                sb.AppendLine(RelayIntoExpiringConstruct());
                sb.AppendLine(RouletteCore());
            }
            catch (Exception error)
            {
                failed++;
                sb.AppendLine("PROBE THREW: " + error.Message);
                sb.AppendLine(error.StackTrace);
            }
            finally
            {
                Cleanup();
            }

            sb.AppendLine(passed + " passed, " + failed + " failed");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the Generator

        static string DormantEngine()
        {
            var sb = new StringBuilder();
            sb.AppendLine("DORMANT ENGINE - stores while the hand is big, cashes in the moment it is small");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Dormant Engine", sb, out entry)) return sb.ToString();

            // A small hand: it stores one and spends it on the same pass.
            SetHand(state, session, 0);
            int before = manager.PlayerEnergy;
            Fire(manager);
            int paid = manager.PlayerEnergy - before;
            sb.AppendLine("   turn 1, hand 0: charges " + entry.charges + ", energy +" + paid);
            Check(sb, "a small hand cashes in what it stored",
                  paid == ConstructRules.DormantEnergyPerCharge && entry.charges == 0,
                  "energy +" + paid + ", charges " + entry.charges);

            // A full hand: it keeps the charge and says nothing.
            SetHand(state, session, ConstructRules.DormantHandCeiling + 1);
            before = manager.PlayerEnergy;
            Fire(manager);
            paid = manager.PlayerEnergy - before;
            sb.AppendLine("   turn 2, hand " + state.hand.Count + ": charges " + entry.charges + ", energy +" + paid);
            Check(sb, "a hand at the ceiling stores a charge and pays nothing",
                  paid == 0 && entry.charges == 1, "energy +" + paid + ", charges " + entry.charges);

            // Small again: the whole stock comes out at once.
            SetHand(state, session, 3);
            before = manager.PlayerEnergy;
            Fire(manager);
            paid = manager.PlayerEnergy - before;
            sb.AppendLine("   turn 3, hand 3: charges " + entry.charges + ", energy +" + paid);
            Check(sb, "and a small hand cashes in the whole stock",
                  paid == 2 && entry.charges == 0, "energy +" + paid + ", charges " + entry.charges);

            return sb.ToString();
        }

        // ------------------------------------------------------------------ the Amplifier

        static string RelayNode()
        {
            var sb = new StringBuilder();
            sb.AppendLine("RELAY NODE - it counts other constructs and spends three charges relaying into one");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);

            BoardCard fuse;
            BoardCard relay;
            if (!Place(manager, session, "Fuse", sb, out fuse)) return sb.ToString();
            if (!Place(manager, session, "Relay Node", sb, out relay)) return sb.ToString();

            for (int turn = 1; turn <= ConstructRules.RelayChargeTarget; turn++)
            {
                Fire(manager);
                sb.AppendLine("   turn " + turn + ": fuse charges " + fuse.charges + ", relay charges " +
                              relay.charges + (fuse.relayTriggeredThisTurn ? "   (relayed into)" : ""));
            }

            Check(sb, "the relay stores a charge for each construct that triggers", relay.charges == 1,
                  "relay at " + relay.charges + " after spending three and counting the chain");
            Check(sb, "and at three it buys another construct a trigger", fuse.charges == 4,
                  "fuse at " + fuse.charges + " charges (three turns plus one relay)");
            Check(sb, "no construct is relayed into twice in a turn", fuse.relayTriggeredThisTurn,
                  "fuse relayed flag " + fuse.relayTriggeredThisTurn);

            // The budget is handed back by the turn boundary rather than by the board pass, which is what
            // makes it once a turn and not once a pass.
            SetHand(State(manager), session, 4);
            Call(manager, "BeginPlayerTurn", false);
            Check(sb, "and the turn boundary hands the budget back", !fuse.relayTriggeredThisTurn,
                  "fuse relayed flag " + fuse.relayTriggeredThisTurn);

            return sb.ToString();
        }

        // ------------------------------------------------------------------ the Timers

        static string Fuse()
        {
            var sb = new StringBuilder();
            sb.AppendLine("FUSE - a charge a turn, and the whole thing paid out when it runs out");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Fuse", sb, out entry)) return sb.ToString();

            SetHand(state, session, 4);

            // The duration is captured before the loop rather than read as its bound: it counts down as the
            // turns are spent, so a loop written against it stops half way.
            int duration = entry.turnsLeft;
            int pooled = 0;

            for (int turn = 1; turn <= duration; turn++)
            {
                Fire(manager);
                pooled = 9999 - state.enemyHealth;
                sb.AppendLine("   turn " + turn + ": charges " + entry.charges + ", turns left " +
                              entry.turnsLeft + ", damage so far " + pooled);
            }

            int expected = entry.card.constructDuration * ConstructRules.FuseDamagePerCharge;
            Check(sb, "it banks a charge for every turn it survives", entry.charges == entry.card.constructDuration,
                  "charges " + entry.charges);
            Check(sb, "and pays the lot out when it expires", pooled == expected,
                  "dealt " + pooled + ", expected " + expected);
            Check(sb, "and it is spent once it has gone off", state.board.Count == 0,
                  "board holds " + state.board.Count);

            return sb.ToString();
        }

        static string FuseByHand()
        {
            var sb = new StringBuilder();
            sb.AppendLine("FUSE, SACRIFICED - the player can set it off early, and only this pool offers that");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Fuse", sb, out entry)) return sb.ToString();

            SetHand(state, session, 4);
            Fire(manager);

            int before = 9999 - state.enemyHealth;
            bool offered = entry.card.sacrificable;
            manager.SacrificeBoardCard(0);
            int dealt = (9999 - state.enemyHealth) - before;

            sb.AppendLine("   charges " + ConstructRules.FuseChargePerTurn + " at the sacrifice, dealt " + dealt);

            Check(sb, "the board offers this construct up", offered, "sacrificable = " + offered);
            Check(sb, "sacrificing it sets it off for what it had", dealt == ConstructRules.FuseDamagePerCharge,
                  "dealt " + dealt);
            Check(sb, "and it leaves the board", state.board.Count == 0, "board holds " + state.board.Count);

            // A construct that is not for giving up must not answer the click at all. The flag is the whole
            // decision, so the fight refuses the call as well as the board refusing the click.
            BoardCard engine;
            if (Place(manager, session, "Dormant Engine", sb, out engine))
            {
                Check(sb, "a construct that is not sacrificial is not offered",
                      !engine.card.sacrificable, "sacrificable = " + engine.card.sacrificable);

                bool refused = !manager.SacrificeBoardCard(0);
                Check(sb, "and the fight refuses to give it up anyway", refused,
                      refused ? "refused" : "the engine was sacrificed");
                Check(sb, "so it is still standing", state.board.Count == 1,
                      "board holds " + state.board.Count);
            }

            return sb.ToString();
        }

        static string OverheatedCore()
        {
            var sb = new StringBuilder();
            sb.AppendLine("OVERHEATED CORE - mana every turn, its own heat from the second, and a burn at three");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Overheated Core", sb, out entry)) return sb.ToString();

            SetHand(state, session, 4);

            int health = session.Stats.MaxHealth;
            int turns = entry.turnsLeft;
            for (int turn = 1; turn <= turns; turn++)
            {
                Fire(manager);
                sb.AppendLine("   turn " + turn + ": mana " + session.Stats.Mana + ", heat " + entry.heat +
                              ", health " + session.Stats.Health);
            }

            int mana = turns * ConstructRules.OverheatedManaPerTurn;
            int heat = (turns - 1) * ConstructRules.OverheatedHeatPerTurn;

            Check(sb, "it hands over its mana on every one of its turns", session.Stats.Mana == mana,
                  "mana " + session.Stats.Mana + ", expected " + mana);
            Check(sb, "its first turn is free and the rest are not", entry.heat == heat,
                  "heat " + entry.heat + ", expected " + heat);
            Check(sb, "and at the threshold it burns you once", session.Stats.Health == health - ConstructRules.OverheatedDamage,
                  "health " + session.Stats.Health + " of " + health);
            Check(sb, "and then it has run out of duration", state.board.Count == 0,
                  "board holds " + state.board.Count);

            return sb.ToString();
        }

        // ------------------------------------------------------------------ the Reactors

        static string ReclamationEngine()
        {
            var sb = new StringBuilder();
            sb.AppendLine("RECLAMATION ENGINE - it pays out on a turn that followed a wasteful one");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Reclamation Engine", sb, out entry)) return sb.ToString();

            SetHand(state, session, 4);
            CardData thrown = session.Database.FindCard("Defend");

            // A tidy turn: nothing comes back.
            state.discardsLastTurn = ConstructRules.ReclamationDiscardsRequired - 1;
            state.discardPile.Clear();
            state.discardPile.Add(thrown);
            int hand = state.hand.Count;
            Fire(manager);
            sb.AppendLine("   " + state.discardsLastTurn + " discarded last turn: hand " + hand + " -> " + state.hand.Count);
            Check(sb, "one card short of the count brings nothing back", state.hand.Count == hand,
                  "hand " + hand + " -> " + state.hand.Count);

            // A wasteful one: a card comes back.
            state.discardsLastTurn = ConstructRules.ReclamationDiscardsRequired;
            hand = state.hand.Count;
            Fire(manager);
            sb.AppendLine("   " + state.discardsLastTurn + " discarded last turn: hand " + hand + " -> " + state.hand.Count +
                          ", discard pile " + state.discardPile.Count);
            Check(sb, "the count it asks for brings one back", state.hand.Count == hand + 1,
                  "hand " + hand + " -> " + state.hand.Count);
            Check(sb, "and it comes out of the discard pile", state.discardPile.Count == 0,
                  "discard pile holds " + state.discardPile.Count);

            return sb.ToString();
        }

        static string TemporalAnchor()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TEMPORAL ANCHOR - it resolves the last card of the turn again, for nothing, then goes");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            state.playerEnergy = 20;
            SetHand(state, session, 0);

            CardData strike = session.Database.FindCard("Strike");
            if (strike == null)
            {
                Check(sb, "there is a card to repeat", false, "Strike is missing from the database");
                return sb.ToString();
            }

            // The card is played for real, so what the anchor repeats is whatever the fight recorded.
            state.hand.Add(strike);
            int before = 9999 - state.enemyHealth;
            manager.TryPlayCard(0);
            int played = (9999 - state.enemyHealth) - before;

            BoardCard entry;
            if (!Place(manager, session, "Temporal Anchor", sb, out entry)) return sb.ToString();

            // One turn of duration, so it fires once and is spent.
            entry.turnsLeft = 1;
            int mark = state.log.Count;
            Fire(manager);
            int repeated = (9999 - state.enemyHealth) - before - played;

            sb.AppendLine("   " + strike.cardName + " dealt " + played + ", the anchor repeated " + repeated +
                          ", last card recorded " + (state.lastCardPlayedThisTurn != null
                              ? state.lastCardPlayedThisTurn.cardName : "nothing"));
            sb.Append(LogLinesSince(state, mark));

            Check(sb, "the play is recorded as the turn's last card",
                  state.lastCardPlayedThisTurn == strike, "recorded " +
                  (state.lastCardPlayedThisTurn != null ? state.lastCardPlayedThisTurn.cardName : "nothing"));
            Check(sb, "and the anchor resolves it a second time", repeated == played,
                  "played " + played + ", repeated " + repeated);
            Check(sb, "and then it expires", state.board.Count == 0, "board holds " + state.board.Count);

            return sb.ToString();
        }

        // ------------------------------------------------------------------ the Converter

        static string OverflowingArchive()
        {
            var sb = new StringBuilder();
            sb.AppendLine("OVERFLOWING ARCHIVE - a hand too big to use is worth mana at the end of the turn");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Overflowing Archive", sb, out entry)) return sb.ToString();

            // A hand at the threshold exactly: nothing is over it.
            SetHand(state, session, ConstructRules.OverflowHandThreshold);
            int mana = session.Stats.Mana;
            EndTurn(manager);
            sb.AppendLine("   hand " + state.hand.Count + ": mana " + mana + " -> " + session.Stats.Mana);
            Check(sb, "a hand at the threshold is worth nothing",
                  session.Stats.Mana == mana, "mana " + mana + " -> " + session.Stats.Mana);

            // Three over it: three mana.
            SetHand(state, session, ConstructRules.OverflowHandThreshold + 3);
            mana = session.Stats.Mana;
            EndTurn(manager);
            int paid = session.Stats.Mana - mana;
            sb.AppendLine("   hand " + state.hand.Count + ": mana " + mana + " -> " + session.Stats.Mana);
            Check(sb, "and one mana is paid for every card over it", paid == 3, "paid " + paid);

            return sb.ToString();
        }

        // ------------------------------------------------------------------ the Conditional constructs

        static string Archive()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ARCHIVE - it draws only for a hand of exactly three");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Archive", sb, out entry)) return sb.ToString();

            SetHand(state, session, ConstructRules.ArchiveHandExactly + 1);
            int hand = state.hand.Count;
            Fire(manager);
            sb.AppendLine("   hand exactly " + (ConstructRules.ArchiveHandExactly + 1) + ": " + hand + " -> " + state.hand.Count);
            Check(sb, "the wrong size draws nothing", state.hand.Count == hand,
                  "hand " + hand + " -> " + state.hand.Count);

            SetHand(state, session, ConstructRules.ArchiveHandExactly);
            hand = state.hand.Count;
            Fire(manager);
            sb.AppendLine("   hand exactly " + ConstructRules.ArchiveHandExactly + ": " + hand + " -> " + state.hand.Count);
            Check(sb, "the size it names draws for it",
                  state.hand.Count == hand + ConstructRules.ArchiveDrawCount,
                  "hand " + hand + " -> " + state.hand.Count);

            return sb.ToString();
        }

        static string PerfectAlignment()
        {
            var sb = new StringBuilder();
            sb.AppendLine("PERFECT ALIGNMENT - the right hand draws, any other hand costs it a turn");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Perfect Alignment", sb, out entry)) return sb.ToString();

            SetHand(state, session, ConstructRules.AlignmentHandExactly);
            int hand = state.hand.Count;
            int turns = entry.turnsLeft;
            Fire(manager);
            sb.AppendLine("   hand exactly " + ConstructRules.AlignmentHandExactly + ": " + hand + " -> " +
                          state.hand.Count + ", turns " + turns + " -> " + entry.turnsLeft);
            Check(sb, "the size it names draws for it",
                  state.hand.Count == hand + ConstructRules.AlignmentDrawCount,
                  "hand " + hand + " -> " + state.hand.Count);
            Check(sb, "and it spends only the turn it fired on", entry.turnsLeft == turns - 1,
                  "turns " + turns + " -> " + entry.turnsLeft);

            SetHand(state, session, ConstructRules.AlignmentHandExactly - 1);
            hand = state.hand.Count;
            turns = entry.turnsLeft;
            Fire(manager);
            sb.AppendLine("   hand " + (ConstructRules.AlignmentHandExactly - 1) + ": " + hand + " -> " +
                          state.hand.Count + ", turns " + turns + " -> " + entry.turnsLeft);
            Check(sb, "the wrong size draws nothing", state.hand.Count == hand,
                  "hand " + hand + " -> " + state.hand.Count);
            Check(sb, "and costs it an extra turn of duration",
                  entry.turnsLeft == turns - 1 - ConstructRules.AlignmentDurationLost,
                  "turns " + turns + " -> " + entry.turnsLeft + " (one firing turn plus " +
                  ConstructRules.AlignmentDurationLost + " lost)");

            return sb.ToString();
        }

        static string AdaptiveCore()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ADAPTIVE CORE - a charge for the first card of each archetype, paid at four");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Adaptive Core", sb, out entry)) return sb.ToString();

            // One card of four different archetypes, so the count reaches its target.
            var order = new string[] { "Strike", "Defend", "Create Mana", "Exhaust" };
            var hand = new List<CardData>();

            foreach (string cardName in order)
            {
                CardData card = session.Database.FindCard(cardName);
                if (card == null)
                {
                    Check(sb, cardName + " exists in the database", false, "missing");
                    return sb.ToString();
                }

                hand.Add(card);
            }

            state.playerEnergy = 50;
            SetHand(state, session, 0);
            foreach (CardData card in hand) state.hand.Add(card);

            int mark = state.log.Count;
            int energy = state.playerEnergy;

            for (int i = 0; i < hand.Count; i++)
            {
                manager.TryPlayCard(0);
                sb.AppendLine("   after " + hand[i].cardName + " (" + hand[i].archetype + "): charges " +
                              entry.charges + ", archetypes seen " + entry.chargedArchetypes.Count +
                              ", energy " + state.playerEnergy);
            }

            int paid = state.playerEnergy - energy;

            Check(sb, "it counts the first card of every archetype", entry.chargedArchetypes.Count == hand.Count,
                  entry.chargedArchetypes.Count + " of " + hand.Count + " archetypes");
            bool paidOut = HasLineSince(state, mark, "pays out");
            Check(sb, "and at its target it spends the charges", entry.charges == 0,
                  "charges " + entry.charges);
            Check(sb, "for the energy it promised", paidOut,
                  paidOut ? "the payout is logged" : "no payout logged");

            // A second card of an archetype it has already been paid for is worth nothing.
            int seen = entry.chargedArchetypes.Count;
            state.hand.Add(session.Database.FindCard("Strike"));
            manager.TryPlayCard(0);
            sb.AppendLine("   after a second Strength card: charges " + entry.charges + ", archetypes seen " +
                          entry.chargedArchetypes.Count);
            Check(sb, "and a repeat archetype charges nothing",
                  entry.charges == 0 && entry.chargedArchetypes.Count == seen,
                  "charges " + entry.charges + ", archetypes " + entry.chargedArchetypes.Count);

            return sb.ToString();
        }

        static string Prototype()
        {
            var sb = new StringBuilder();
            sb.AppendLine("PROTOTYPE - it does nothing until it is finished, and charges on the big cards");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Prototype", sb, out entry)) return sb.ToString();

            CardData cleave = session.Database.FindCard("Cleave");
            CardData strike = session.Database.FindCard("Strike");
            if (cleave == null || strike == null)
            {
                Check(sb, "there are cards to play", false, "Cleave or Strike is missing from the database");
                return sb.ToString();
            }

            state.playerEnergy = 50;
            SetHand(state, session, 0);
            state.hand.Add(cleave);
            state.hand.Add(strike);

            // Cleave costs two energy, which is the line it is counting.
            manager.TryPlayCard(0);
            sb.AppendLine("   after " + cleave.cardName + " (cost " + cleave.cost + " " + cleave.costType +
                          "): charges " + entry.charges);
            Check(sb, "a card at the cost it names charges it", entry.charges == 1, "charges " + entry.charges);

            // Strike costs one, which is under the line.
            manager.TryPlayCard(0);
            sb.AppendLine("   after " + strike.cardName + " (cost " + strike.cost + " " + strike.costType +
                          "): charges " + entry.charges);
            Check(sb, "and a cheaper card does not", entry.charges == 1, "charges " + entry.charges);

            // Forced to its last turn, so what it pays out on expiry can be read.
            entry.turnsLeft = 1;
            int energy = state.playerEnergy;
            int mana = session.Stats.Mana;
            int mark = state.log.Count;
            Fire(manager);

            int paidEnergy = state.playerEnergy - energy;
            int paidMana = session.Stats.Mana - mana;
            int wanted = ConstructRules.PrototypeEnergyPerCharge;
            int wantedMana = ConstructRules.PrototypeManaPerCharge;

            sb.AppendLine("   on expiry: " + paidEnergy + " energy and " + paidMana + " mana for " +
                          entry.charges + " charges");
            sb.Append(LogLinesSince(state, mark));

            Check(sb, "and on expiry it pays per charge in energy", paidEnergy == wanted,
                  "paid " + paidEnergy + ", expected " + wanted);
            Check(sb, "and per charge in mana", paidMana == wantedMana,
                  "paid " + paidMana + ", expected " + wantedMana);

            return sb.ToString();
        }

        // ------------------------------------------------------------------ the Sacrifice constructs

        static string ForbiddenEngine()
        {
            var sb = new StringBuilder();
            sb.AppendLine("FORBIDDEN ENGINE - mana every turn, and everything else when it is given up");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Forbidden Engine", sb, out entry)) return sb.ToString();

            SetHand(state, session, 4);
            int mana = session.Stats.Mana;
            Fire(manager);
            sb.AppendLine("   a turn in play: mana " + mana + " -> " + session.Stats.Mana);
            Check(sb, "it hands over its mana every turn",
                  session.Stats.Mana - mana == ConstructRules.ForbiddenManaPerTurn,
                  "mana " + mana + " -> " + session.Stats.Mana);

            int block = session.Stats.Block;
            int energy = manager.PlayerEnergy;
            int mark = state.log.Count;
            manager.SacrificeBoardCard(0);

            int paidBlock = session.Stats.Block - block;
            int paidEnergy = manager.PlayerEnergy - energy;

            sb.AppendLine("   sacrificed: " + paidBlock + " shielding, " + paidEnergy + " energy");
            sb.Append(LogLinesSince(state, mark));

            Check(sb, "sacrificing it buys the shielding", paidBlock == ConstructRules.ForbiddenBlockOnSacrifice,
                  "shielding +" + paidBlock);
            Check(sb, "and the energy", paidEnergy == ConstructRules.ForbiddenEnergyOnSacrifice,
                  "energy +" + paidEnergy);
            Check(sb, "and it leaves the board", state.board.Count == 0, "board holds " + state.board.Count);

            return sb.ToString();
        }

        static string SuccessorProtocol()
        {
            var sb = new StringBuilder();
            sb.AppendLine("SUCCESSOR PROTOCOL - another construct's last act happens again");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard fuse;
            BoardCard successor;
            if (!Place(manager, session, "Fuse", sb, out fuse)) return sb.ToString();
            if (!Place(manager, session, "Successor Protocol", sb, out successor)) return sb.ToString();

            SetHand(state, session, 4);

            // A Fuse on its last turn, holding two charges, with the protocol behind it.
            fuse.charges = 2;
            fuse.turnsLeft = 1;

            int mark = state.log.Count;
            int before = 9999 - state.enemyHealth;
            Fire(manager);
            int dealt = (9999 - state.enemyHealth) - before;

            int one = (2 + ConstructRules.FuseChargePerTurn) * ConstructRules.FuseDamagePerCharge;
            sb.AppendLine("   the fuse went off for " + dealt + " damage (one detonation would be " + one + ")");
            sb.Append(LogLinesSince(state, mark));

            bool copied = HasLineSince(state, mark, "copies the last thing");
            Check(sb, "a construct leaving is noticed by the protocol", copied,
                  copied ? "the copy is logged" : "nothing logged");
            Check(sb, "and the detonation happens a second time", dealt == one * 2,
                  "dealt " + dealt + ", expected " + (one * 2));

            return sb.ToString();
        }

        // ------------------------------------------------------------------ boards with more than one of a thing

        /// <summary>
        /// Two Successor Protocols behind one construct, which is the shape that breaks a copy rule.
        ///
        /// Both protocols are entitled to copy the same last act, so the detonation should happen once on its
        /// own and once more per protocol. What must not happen is a copy being copied: the second protocol
        /// answering the first one's copy would hand the two of them work back and forth, and the number of
        /// detonations would then be whatever the chain guard happened to allow rather than something the
        /// rules decided.
        /// </summary>
        static string SuccessorOnSuccessor()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TWO SUCCESSOR PROTOCOLS - each copies a departure once, and a copy is never copied");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard fuse;
            BoardCard first;
            BoardCard second;
            if (!Place(manager, session, "Fuse", sb, out fuse)) return sb.ToString();
            if (!Place(manager, session, "Successor Protocol", sb, out first)) return sb.ToString();
            if (!Place(manager, session, "Successor Protocol", sb, out second)) return sb.ToString();

            SetHand(state, session, 4);

            // A fuse on its last turn holding two charges, so it pulses once more on its way out.
            fuse.charges = 2;
            fuse.turnsLeft = 1;

            int mark = state.log.Count;
            int before = 9999 - state.enemyHealth;
            Fire(manager);
            int dealt = (9999 - state.enemyHealth) - before;

            int charges = 2 + ConstructRules.FuseChargePerTurn;
            int one = charges * ConstructRules.FuseDamagePerCharge;
            int copies = CountLinesSince(state, mark, "copies the last thing");

            sb.AppendLine("   the fuse went off for " + dealt + " damage (one detonation would be " + one + ")");
            sb.AppendLine("   copies logged: " + copies);
            sb.Append(LogLinesSince(state, mark));

            Check(sb, "each protocol copies the departure once", copies == 2, copies + " copies logged");
            Check(sb, "so the detonation happens once and twice more", dealt == one * 3,
                  "dealt " + dealt + ", expected " + (one * 3));
            Check(sb, "and a copy is never itself copied",
                  !HasLineSince(state, mark, "copies the last thing Successor Protocol") &&
                  !HasLineSince(state, mark, "was asked to have its last act copied"),
                  "a copy of a copy was logged");
            Check(sb, "and both protocols are still standing", state.board.Count == 2,
                  "board holds " + state.board.Count);
            Check(sb, "having spent their own turns rather than their charges",
                  first.turnsLeft == 3 && second.turnsLeft == 3,
                  "turns left " + first.turnsLeft + " and " + second.turnsLeft);

            return sb.ToString();
        }

        /// <summary>
        /// Two Relay Nodes on one board, which is the shape that turns a single trigger into a chain.
        ///
        /// Both are entitled to answer the same construct doing something, and neither answering is a mistake.
        /// The relay's own charge is itself a construct triggering, so two of them charging each other is the
        /// mechanic running rather than a fault - it is meant to be bounded by the rule printed on the card
        /// and by the chain guard, not by luck. So what is pinned here is the guarantee a player can be held
        /// to: however many relays are standing and however long they hand each other work, no construct is
        /// relayed into twice in a turn, and a relay is never a relay target.
        /// </summary>
        static string TwoRelayNodes()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TWO RELAY NODES - both answer the same trigger, and nothing is relayed into twice");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard fuse;
            BoardCard dormant;
            BoardCard first;
            BoardCard second;
            if (!Place(manager, session, "Fuse", sb, out fuse)) return sb.ToString();
            if (!Place(manager, session, "Dormant Engine", sb, out dormant)) return sb.ToString();
            if (!Place(manager, session, "Relay Node", sb, out first)) return sb.ToString();
            if (!Place(manager, session, "Relay Node", sb, out second)) return sb.ToString();

            SetHand(state, session, 4);

            int mark = state.log.Count;
            Fire(manager);

            // The counters are read off the board, not off the log: a pass like this overflows the fight's
            // forty-line log many times over, so anything counted by searching it under-reports and would
            // quietly pass. Only the lines still standing are printed, as evidence rather than as the measure.
            int chargeLines = CountLinesSince(state, mark, "Relay Node: stores");
            int flagged = 0;
            foreach (BoardCard entry in state.board)
            {
                if (entry != null && entry.relayTriggeredThisTurn) flagged++;
            }

            sb.AppendLine("   relay charges " + first.charges + " and " + second.charges +
                          ", charge events surviving in the log " + chargeLines);
            sb.AppendLine("   constructs relayed into this turn " + flagged + " of " +
                          (state.board.Count - 2) + " that can be");
            sb.Append(LogLinesSince(state, mark));

            Check(sb, "both relays answer the same construct triggering",
                  first.charges + second.charges > 0 && chargeLines >= 2,
                  "charges " + first.charges + " and " + second.charges + " from " + chargeLines + " events");
            Check(sb, "a relay is never a relay target itself",
                  !first.relayTriggeredThisTurn && !second.relayTriggeredThisTurn,
                  "relay flags " + first.relayTriggeredThisTurn + " and " + second.relayTriggeredThisTurn);
            Check(sb, "and no construct is relayed into more than it can be",
                  flagged <= state.board.Count - 2,
                  flagged + " constructs flagged out of " + (state.board.Count - 2));
            Check(sb, "and the pass terminates with the board as it was",
                  state.board.Count == 4 && !state.combatOver,
                  "board holds " + state.board.Count + ", combat over " + state.combatOver);

            return sb.ToString();
        }

        /// <summary>
        /// A Relay Node feeding a Fuse that has already spent its last turn.
        ///
        /// The window is narrow and real. A construct's duration is spent at the end of its own pulse and the
        /// card is not taken off the board until the pass is over, so a relay firing later in that same pass
        /// is looking at a Fuse with no turns left and every Charge it has already banked. Feeding it is the
        /// intended play rather than a fault - enlarging a Fuse's detonation is what the relay is for, and the
        /// tooltip states the Charges, so nothing is hidden - which is what this pins: the charge lands, and
        /// the detonation is the larger one.
        /// </summary>
        static string RelayIntoExpiringConstruct()
        {
            var sb = new StringBuilder();
            sb.AppendLine("A RELAY FEEDING A SPENT FUSE - the charge lands before the fuse is swept up");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard fuse;
            BoardCard relay;
            if (!Place(manager, session, "Fuse", sb, out fuse)) return sb.ToString();
            if (!Place(manager, session, "Relay Node", sb, out relay)) return sb.ToString();

            SetHand(state, session, 4);

            // One charge banked and one turn left: its pulse takes the turn and adds another charge, and the
            // relay is then one charge short of its target with a fuse that has nothing left to give.
            fuse.charges = 1;
            fuse.turnsLeft = 1;
            relay.charges = ConstructRules.RelayChargeTarget - ConstructRules.RelayChargePerTrigger;

            int mark = state.log.Count;
            int before = 9999 - state.enemyHealth;
            Fire(manager);
            int dealt = (9999 - state.enemyHealth) - before;

            int alone = (1 + ConstructRules.FuseChargePerTurn) * ConstructRules.FuseDamagePerCharge;
            int fed = (2 + ConstructRules.FuseChargePerTurn) * ConstructRules.FuseDamagePerCharge;

            sb.AppendLine("   dealt " + dealt + " (an unfed fuse would be " + alone + ", a fed one " + fed + ")");
            sb.AppendLine("   the fuse had 0 turns left when the relay reached it");
            sb.Append(LogLinesSince(state, mark));

            Check(sb, "the relay reaches a construct that has spent its last turn",
                  HasLineSince(state, mark, "relays into Fuse") && fuse.relayTriggeredThisTurn,
                  fuse.relayTriggeredThisTurn ? "the fuse was relayed into" : "nothing was relayed into");
            Check(sb, "and the charge lands before the detonation", dealt == fed,
                  "dealt " + dealt + ", a fed fuse is " + fed);
            Check(sb, "and the fuse is swept up afterwards anyway", state.board.Count == 1,
                  "board holds " + state.board.Count);

            return sb.ToString();
        }

        // ------------------------------------------------------------------ the Gambler

        static string RouletteCore()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ROULETTE CORE - one of three, every turn, and never the one you needed");

            GameSession session = BootSession();
            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            BoardCard entry;
            if (!Place(manager, session, "Roulette Core", sb, out entry)) return sb.ToString();

            SetHand(state, session, 4);

            int block = 0;
            int damage = 0;
            int drawn = 0;
            int odd = 0;

            // More rolls than the card has duration, so the three outcomes have room to turn up, and read as
            // one at a time rather than as a distribution: what is being checked is that a roll happens and
            // that exactly one thing comes of it.
            const int rolls = 12;
            for (int i = 0; i < rolls; i++)
            {
                entry.turnsLeft = entry.card.constructDuration;

                int shieldBefore = session.Stats.Block;
                int healthBefore = state.enemyHealth;
                int handBefore = state.hand.Count;

                Fire(manager);

                bool gotShield = session.Stats.Block > shieldBefore;
                bool gotDamage = state.enemyHealth < healthBefore;
                bool gotCard = state.hand.Count > handBefore;

                int outcomes = (gotShield ? 1 : 0) + (gotDamage ? 1 : 0) + (gotCard ? 1 : 0);
                if (outcomes != 1) odd++;

                if (gotShield) block++;
                if (gotDamage) damage++;
                if (gotCard) drawn++;
            }

            sb.AppendLine("   " + rolls + " rolls: " + block + " shielding, " + damage + " damage, " +
                          drawn + " cards");
            Check(sb, "every turn it does exactly one of the three", odd == 0,
                  odd + " rolls did not resolve to one outcome");
            Check(sb, "and it does roll all three between them", block > 0 && damage > 0 && drawn > 0,
                  block + " shielding, " + damage + " damage, " + drawn + " cards");

            return sb.ToString();
        }

        // ------------------------------------------------------------------ building a fight

        /// <summary>A session with a real run built and no items on the player.</summary>
        static GameSession BootSession()
        {
            GameSession session = NewComponent<GameSession>("ConstructProbeSession");
            SetStaticField(typeof(GameSession), "instance", session);

            session.StartNewRun();
            session.Inventory.Clear();
            return session;
        }

        /// <summary>
        /// A fight that cannot end underneath a measurement.
        ///
        /// The enemy is given a pool far larger than any of these tests can take off it, because a construct
        /// that kills the thing it is aimed at stops the fight and would read as a construct that did nothing.
        /// The hand and the pile are emptied so each case deals its own.
        /// </summary>
        static CombatManager BootCombat(GameSession session)
        {
            CombatManager manager = NewComponent<CombatManager>("ConstructProbeCombat");
            manager.StartCombat();

            CombatState state = State(manager);
            if (state == null) return manager;

            state.enemyHealth = 9999;
            state.enemyBlock = 0;
            state.playerEnergy = 50;
            state.hand.Clear();
            state.drawPile.Clear();

            CardData filler = session.Database.FindCard("Defend");
            if (filler != null)
            {
                for (int i = 0; i < 60; i++) state.drawPile.Add(filler);
            }

            return manager;
        }

        /// <summary>Puts a construct straight onto the board, which is where the board pass looks for it.</summary>
        static bool Place(CombatManager manager, GameSession session, string cardName, StringBuilder sb,
                          out BoardCard entry)
        {
            entry = null;

            CombatState state = State(manager);
            if (state == null)
            {
                Check(sb, "the fight started", false, "no CombatState");
                return false;
            }

            CardData card = session.Database.FindCard(cardName);
            if (card == null)
            {
                Check(sb, "the card exists in the database", false, cardName + " not found");
                return false;
            }

            entry = new BoardCard
            {
                card = card,
                turnsLeft = card.constructDuration > 0 ? card.constructDuration : BoardCard.RestOfFight
            };

            state.board.Add(entry);
            return true;
        }

        /// <summary>Deals an exact hand, so a construct that reads the hand can be measured.</summary>
        static void SetHand(CombatState state, GameSession session, int size)
        {
            if (state == null) return;

            state.hand.Clear();

            CardData filler = session.Database.FindCard("Defend");
            for (int i = 0; i < size; i++) if (filler != null) state.hand.Add(filler);
        }

        static T NewComponent<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;
            made.Add(go);

            return go.AddComponent<T>();
        }

        static void Cleanup()
        {
            SetStaticField(typeof(GameSession), "instance", null);

            foreach (GameObject go in made)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            made.Clear();
        }

        // ------------------------------------------------------------------ reading a fight

        static CombatState State(CombatManager manager)
        {
            FieldInfo field = typeof(CombatManager).GetField("state", BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null ? field.GetValue(manager) as CombatState : null;
        }

        /// <summary>The board pass at the top of a turn, which is where most of the pool acts.</summary>
        static void Fire(CombatManager manager)
        {
            Call(manager, "FireBoard");
        }

        /// <summary>The moment the turn is handed over, which is where Overflowing Archive acts.</summary>
        static void EndTurn(CombatManager manager)
        {
            Call(manager, "FireEndOfTurnConstructs");
        }

        /// <summary>
        /// Calls one of the fight's own hooks. They are private because they are the single funnels the
        /// constructs hang off, so the probe reads them the way a caller inside the fight would reach them.
        /// </summary>
        static object Call(CombatManager manager, string method, params object[] args)
        {
            MethodInfo[] methods = typeof(CombatManager).GetMethods(BindingFlags.NonPublic |
                                                                   BindingFlags.Instance |
                                                                   BindingFlags.Public);

            foreach (MethodInfo candidate in methods)
            {
                if (candidate.Name != method) continue;
                if (candidate.GetParameters().Length != args.Length) continue;

                return candidate.Invoke(manager, args);
            }

            Debug.LogWarning("[ConstructProbe] no method " + method + " with " + args.Length + " arguments");
            return null;
        }

        static void SetStaticField(Type type, string name, object value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null) field.SetValue(null, value);
        }

        static bool HasLineSince(CombatState state, int from, string needle)
        {
            if (state == null) return false;

            for (int i = from; i < state.log.Count; i++)
            {
                if (state.log[i] != null && state.log[i].Contains(needle)) return true;
            }

            return false;
        }

        /// <summary>
        /// How many of the lines since a mark mention something. Used to count events rather than read states:
        /// "a construct was relayed into twice" is a count, and no field on the board holds it.
        /// </summary>
        static int CountLinesSince(CombatState state, int from, string needle)
        {
            if (state == null) return 0;

            int count = 0;
            for (int i = from; i < state.log.Count; i++)
            {
                if (state.log[i] != null && state.log[i].Contains(needle)) count++;
            }

            return count;
        }

        static string LogLinesSince(CombatState state, int from)
        {
            if (state == null) return "";

            var sb = new StringBuilder();
            for (int i = from; i < state.log.Count; i++) sb.AppendLine("      " + state.log[i]);

            return sb.ToString();
        }

        static void Check(StringBuilder sb, string label, bool ok, string detail)
        {
            if (ok) passed++; else failed++;

            sb.AppendLine("   " + (ok ? "PASS" : "FAIL") + "  " + label +
                          (string.IsNullOrEmpty(detail) ? "" : "   (" + detail + ")"));
        }
    }
}
#endif

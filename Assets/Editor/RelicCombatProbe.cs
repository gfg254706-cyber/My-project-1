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
    /// Drives the relic hooks through a real fight rather than through their arithmetic.
    ///
    /// Every relic is carried with its rolled package pinned to zero, so each line below is the relic's own
    /// effect and nothing else: a number that moves between runs then means the code moved, not the dice.
    /// That is the whole reason this exists as a separate probe from RelicProbe, which is about the rolls.
    ///
    /// The fight is driven through the production entry points - TryPlayCard, the conjure funnel, the health
    /// funnel, the turn boundary - because what is being checked is that the hooks are reached. An effect
    /// that computes correctly and is never called reads as working to a test of the arithmetic alone, and
    /// that is the failure this project keeps producing.
    ///
    /// It runs from Tools > Dungeon Cards > Relics > Probe Relic Hooks, or from Execute with no window.
    /// </summary>
    public static class RelicCombatProbe
    {
        /// <summary>Objects this probe built. A fight is a scene object, so it has to be taken away again.</summary>
        static readonly List<GameObject> made = new List<GameObject>();

        static int passed;
        static int failed;

        [MenuItem("Tools/Dungeon Cards/Relics/Probe Relic Hooks")]
        public static void ProbeHooks()
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
            sb.AppendLine("RELIC HOOK PROBE");
            sb.AppendLine("   Every relic below is carried with its rolled package pinned to zero, so each number is the");
            sb.AppendLine("   relic's own effect rather than the dice it arrived with.");
            sb.AppendLine();

            try
            {
                sb.AppendLine(MirageEngine());
                sb.AppendLine(BoulderShield());
                sb.AppendLine(Goblet());
                sb.AppendLine(BrokenCrown());
                sb.AppendLine(BloodCrown());
                sb.AppendLine(GrimoirePick());
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

        // ------------------------------------------------------------------ the cases

        /// <summary>
        /// Mirage Engine: the card at the named position in the turn conjures a free one.
        ///
        /// Measured by hand size rather than by the log alone, because the two possible failures are
        /// different: a conjure that never fires leaves the hand one card shorter, and a conjure that fires
        /// but is not free leaves a card the turn cannot pay for.
        /// </summary>
        static string MirageEngine()
        {
            var sb = new StringBuilder();
            sb.AppendLine("MIRAGE ENGINE - the third card played each turn conjures a free one");

            GameSession session = BootSession();
            if (!Carry(session, "Mirage Engine", sb)) return sb.ToString();

            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);
            if (state == null)
            {
                Check(sb, "the fight started", false, "no CombatState");
                return sb.ToString();
            }

            CardData defend = session.Database.FindCard("Defend");
            if (defend == null)
            {
                Check(sb, "a card to play", false, "Defend is missing from the database");
                return sb.ToString();
            }

            Check(sb, "carried, waiting for card " + RelicEffects.Threshold(RelicEffect.MirageEngine),
                  RelicEffects.Has(RelicEffect.MirageEngine), "threshold " + RelicEffects.Threshold(RelicEffect.MirageEngine));

            // A hand of cards that deal no damage, so the fight cannot end underneath the measurement and
            // nothing in it conjures on its own.
            state.hand.Clear();
            for (int i = 0; i < 4; i++) state.hand.Add(defend);

            sb.AppendLine("   hand " + state.hand.Count + " of " + defend.cardName + " (cost " + manager.CostOf(defend) +
                          "), energy " + manager.PlayerEnergy);

            var firedOn = new List<int>();
            var before = new List<int>();
            var after = new List<int>();

            for (int play = 1; play <= 3; play++)
            {
                int mark = state.log.Count;
                int from = state.hand.Count;

                bool played = manager.TryPlayCard(0);
                int to = state.hand.Count;
                bool fired = HasLineSince(state, mark, "Mirage Engine conjures");

                before.Add(from);
                after.Add(to);
                if (fired) firedOn.Add(play);

                sb.AppendLine("   play " + play + " (" + (played ? "taken" : "refused") + "): played " +
                              state.cardsPlayedThisTurn + ", hand " + from + " -> " + to +
                              " [" + HandNames(state) + "]" + (fired ? "   <- mirage fired" : ""));
                sb.Append(LogLinesSince(state, mark));
            }

            Check(sb, "nothing conjures before the named card", !firedOn.Contains(1) && !firedOn.Contains(2),
                  "fired on " + Plays(firedOn));
            Check(sb, "and the third card conjures one", firedOn.Contains(3), "fired on " + Plays(firedOn));

            // One card leaves the hand to be played, so a turn that conjures has to end up back where it
            // started rather than one lower. A conjured card that costs nothing on its own prints draws
            // another, which is why this is a floor rather than an equality.
            bool replaced = after.Count == 3 && after[2] >= before[2];
            Check(sb, "the hand is not left a card short", replaced,
                  "hand " + (after.Count == 3 ? before[2] + " -> " + after[2] : "?"));

            if (state.hand.Count > 0)
            {
                CardData arrived = state.hand[state.hand.Count - 1];
                Check(sb, "what arrived costs nothing to play", manager.CostOf(arrived) == 0,
                      arrived.cardName + " costs " + manager.CostOf(arrived) + " (printed " + arrived.cost + ")");
            }

            return sb.ToString();
        }

        /// <summary>Boulder Shield: shielding equal to a slice of an attribute, once at the top of every turn.</summary>
        static string BoulderShield()
        {
            var sb = new StringBuilder();
            sb.AppendLine("BOULDER SHIELD - a third of Strength as shielding, every turn");

            GameSession session = BootSession();
            if (!Carry(session, "Boulder Shield", sb)) return sb.ToString();

            // The one attribute under test, and nothing else, so a third of nine is three and no other
            // number in the fight can be mistaken for it.
            session.Stats.SetBaseStat(StatType.Strength, 9);

            int predicted = RelicEffects.BoulderShieldBlock(session.Stats);
            sb.AppendLine("   strength " + session.Stats.GetStat(StatType.Strength) + " (base 9, relic guarantees " +
                          session.Stats.GetItemStat(StatType.Strength) + "), health pool " + session.Stats.MaxHealth);

            CombatManager manager = BootCombat(session);

            Check(sb, "9 / 3 reads as 3", predicted == 3, "got " + predicted);
            Check(sb, "and the turn is fought with 3 shielding", session.Stats.Block == 3,
                  "block " + session.Stats.Block);

            if (State(manager) != null) sb.Append(LogLinesSince(State(manager), 0));
            return sb.ToString();
        }

        /// <summary>
        /// Goblet: the designated attribute is bought a point at every turn, and paid for out of the opening
        /// hand. Both halves are measured here, because a Goblet that paid nothing, or took nothing, is the
        /// same failure from the player's side.
        /// </summary>
        static string Goblet()
        {
            var sb = new StringBuilder();
            sb.AppendLine("GOBLET OF POWER - +5 Strength guaranteed, +1 more every turn, for 4 cards off the opener");

            GameSession session = BootSession();
            if (!Carry(session, "Goblet of Power", sb)) return sb.ToString();

            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);

            int guaranteed = session.Stats.GetItemStat(StatType.Strength);
            int bought = session.Stats.GetCombatStat(StatType.Strength);
            int total = session.Stats.GetStat(StatType.Strength);

            sb.AppendLine("   guaranteed " + guaranteed + ", bought this combat " + bought + ", total " + total);

            Check(sb, "the guarantee is 5 Strength", guaranteed == 5, "got " + guaranteed);
            Check(sb, "and one point is bought at the top of the turn", bought == 1, "got " + bought);
            Check(sb, "so the turn is fought at 6", total == 6, "got " + total);

            if (state != null)
            {
                Check(sb, "the opening hand is claimed for it", state.hand.Count == 0,
                      state.hand.Count + " left of " + PlayerStats.StartingHandSize);
                Check(sb, "and the claim is what was paid", HasLineSince(state, 0, "claims"),
                      HasLineSince(state, 0, "claims") ? "the claim is logged" : "nothing logged");
                sb.Append(LogLinesSince(state, 0));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Broken Crown: the lowest attribute counts higher to a card formula and not to the health pool.
        ///
        /// Vitality is made the lowest attribute on purpose, because that is the only arrangement in which
        /// the two claims can be told apart: a lift that leaked into the health pool would move MaxHealth,
        /// and with any other attribute lowest nothing would move whatever the code did.
        /// </summary>
        static string BrokenCrown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("BROKEN CROWN - whatever is lowest counts 2 higher to a card, and only to a card");

            GameSession session = BootSession();
            if (!Carry(session, "Broken Crown", sb)) return sb.ToString();

            session.Stats.SetBaseStat(StatType.Strength, 6);
            session.Stats.SetBaseStat(StatType.Agility, 6);
            session.Stats.SetBaseStat(StatType.Intellect, 6);
            session.Stats.SetBaseStat(StatType.Vitality, 2);
            session.Stats.SetBaseStat(StatType.Willpower, 6);

            PlayerStats stats = session.Stats;
            int pool = stats.MaxHealth;
            CombatManager manager = BootCombat(session);

            sb.AppendLine("   vitality " + stats.GetStat(StatType.Vitality) + ", lowest " + stats.LowestStat() +
                          ", health pool " + stats.MaxHealth + " (base 30 + vitality)");

            Check(sb, "Vitality is the lowest", stats.LowestStat() == StatType.Vitality, "lowest is " + stats.LowestStat());
            Check(sb, "a card reads it as 4", stats.GetCardStat(StatType.Vitality) == 4,
                  "got " + stats.GetCardStat(StatType.Vitality) + " from " + stats.GetStat(StatType.Vitality));
            Check(sb, "the attribute itself is untouched", stats.GetStat(StatType.Vitality) == 2,
                  "got " + stats.GetStat(StatType.Vitality));
            Check(sb, "and the health pool does not see the lift", stats.MaxHealth == pool,
                  "pool " + pool + " -> " + stats.MaxHealth);

            // Which attribute is lowest moves as the rest of the fight moves the others, so the lift has to
            // be rebuilt rather than carried. This is the same call ApplyPermanentChange makes after a card.
            stats.SetBaseStat(StatType.Strength, 1);
            Call(manager, "RefreshBrokenCrown", false);

            Check(sb, "a new lowest is lifted instead", stats.GetCardStat(StatType.Strength) == 3,
                  "strength reads " + stats.GetCardStat(StatType.Strength));
            Check(sb, "and the one it left is handed back", stats.GetCardStat(StatType.Vitality) == 2,
                  "vitality reads " + stats.GetCardStat(StatType.Vitality));

            return sb.ToString();
        }

        /// <summary>
        /// Blood Crown: once a fight, health below half is answered with energy and with the attribute the
        /// relic names.
        ///
        /// Both sides are measured: a fight that is below half when it is already hurt pays out, and one
        /// that is not yet hurt does not.
        /// </summary>
        static string BloodCrown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("BLOOD CROWN - at below half health, 2 energy and 2 Willpower, once a fight");

            // The side where nothing should happen: full health, one point taken, still above half.
            GameSession sound = BootSession();
            if (!Carry(sound, "Blood Crown", sb)) return sb.ToString();

            CombatManager soundManager = BootCombat(sound);
            int soundEnergy = soundManager.PlayerEnergy;
            int soundWill = sound.Stats.GetStat(StatType.Willpower);

            Call(soundManager, "LoseHealth", 1, "probe: one point off a full pool");

            Check(sb, "above half health it pays nothing",
                  soundManager.PlayerEnergy == soundEnergy && sound.Stats.GetStat(StatType.Willpower) == soundWill,
                  "energy " + soundEnergy + " -> " + soundManager.PlayerEnergy + ", willpower " + soundWill +
                  " -> " + sound.Stats.GetStat(StatType.Willpower) + " (pool " + sound.Stats.MaxHealth + ")");

            // The side where it should: the same fight, entered already hurt.
            GameSession hurt = BootSession();
            Carry(hurt, "Blood Crown", sb);

            hurt.Stats.TakeDamage(20);
            sb.AppendLine("   entering at " + hurt.Stats.Health + " of " + hurt.Stats.MaxHealth);

            CombatManager manager = BootCombat(hurt);
            int energy = manager.PlayerEnergy;
            int will = hurt.Stats.GetStat(StatType.Willpower);

            Call(manager, "LoseHealth", 1, "probe: one point off a hurt pool");

            int paidEnergy = manager.PlayerEnergy - energy;
            int paidWill = hurt.Stats.GetStat(StatType.Willpower) - will;

            Check(sb, "below half it pays 2 energy", paidEnergy == 2, "got " + paidEnergy);
            Check(sb, "and 2 Willpower for the rest of the fight", paidWill == 2, "got " + paidWill);
            Check(sb, "the Willpower is this fight's, not the run's", hurt.Stats.GetItemStat(StatType.Willpower) == 3,
                  "item stat " + hurt.Stats.GetItemStat(StatType.Willpower) + ", combat stat " +
                  hurt.Stats.GetCombatStat(StatType.Willpower));

            Call(manager, "LoseHealth", 1, "probe: a second point off the same pool");

            Check(sb, "and it is paid once a fight, not once a hit",
                  manager.PlayerEnergy - energy == 2 && hurt.Stats.GetStat(StatType.Willpower) - will == 2,
                  "energy +" + (manager.PlayerEnergy - energy) + ", willpower +" +
                  (hurt.Stats.GetStat(StatType.Willpower) - will));

            sb.Append(LogLinesSince(State(manager), 0));
            return sb.ToString();
        }

        /// <summary>
        /// Loaded Grimoire: the first conjure of the turn turns up two cards and the hand grows only when one
        /// is kept.
        ///
        /// This is the case that a conjure is read wrong without: a Mirage Engine measured by hand size while
        /// a Grimoire is carried looks broken, because the card it turns up is standing in a choice rather
        /// than in the hand.
        /// </summary>
        static string GrimoirePick()
        {
            var sb = new StringBuilder();
            sb.AppendLine("LOADED GRIMOIRE - the first conjure of the turn offers two and keeps one");

            GameSession session = BootSession();
            if (!Carry(session, "Loaded Grimoire", sb)) return sb.ToString();

            CombatManager manager = BootCombat(session);
            CombatState state = State(manager);
            if (state == null)
            {
                Check(sb, "the fight started", false, "no CombatState");
                return sb.ToString();
            }

            int hand = state.hand.Count;
            Call(manager, "GenerateRandomCard", true);

            Check(sb, "a conjure while it is carried opens a choice",
                  state.pickStage == PendingPick.KeepGeneratedOne, "stage " + state.pickStage);
            Check(sb, "two cards are turned up", state.pickPending.Count == 2, "got " + state.pickPending.Count);
            Check(sb, "and the hand waits on the answer", state.hand.Count == hand,
                  "hand " + hand + " -> " + state.hand.Count);
            sb.Append(LogLinesSince(state, 0));

            int turned = state.pickPending.Count;
            Call(manager, "PickCard", 0);

            Check(sb, "keeping one puts one in the hand", state.hand.Count == hand + 1,
                  "hand " + hand + " -> " + state.hand.Count + " (turned up " + turned + ")");
            Check(sb, "the choice closes", state.pickStage == PendingPick.None, "stage " + state.pickStage);

            if (state.hand.Count > 0)
            {
                CardData kept = state.hand[state.hand.Count - 1];
                Check(sb, "and the card it conjured is free", manager.CostOf(kept) == 0,
                      kept.cardName + " costs " + manager.CostOf(kept) + " (printed " + kept.cost + ")");
            }

            sb.Append(LogLinesSince(state, 0));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ building a fight

        /// <summary>
        /// A session with a real run built and no items on the player, so every number in a case is the one
        /// the case set rather than one a starting item contributed.
        /// </summary>
        static GameSession BootSession()
        {
            GameSession session = NewComponent<GameSession>("RelicProbeSession");
            SetStaticField(typeof(GameSession), "instance", session);

            session.StartNewRun();
            session.Inventory.Clear();
            return session;
        }

        /// <summary>
        /// Takes a relic into the bag with its rolled package pinned to zero.
        ///
        /// A relic rolls its own stat package when it is picked up, which is the whole point of the generator
        /// and is exactly what makes a hook unmeasurable: the numbers move between runs and every assertion
        /// written against them is a coin toss. The definition's guaranteed stats still apply, so what a case
        /// reads is the relic's identity with its roll held still.
        /// </summary>
        static bool Carry(GameSession session, string relicName, StringBuilder sb)
        {
            RelicDefinition definition = FindRelic(session.Database, relicName);
            if (definition == null)
            {
                Check(sb, "the relic exists in the database", false, relicName + " not found");
                return false;
            }

            var instance = new ItemInstance(definition);
            instance.relic = new RelicInstance
            {
                tier = definition.tier,
                stats = new int[RelicInstance.StatCount],
                positiveAllocation = new int[RelicInstance.StatCount],
                negativeAllocation = new int[RelicInstance.StatCount]
            };

            if (!session.Inventory.Add(instance))
            {
                Check(sb, "the relic fits in the bag", false, "Inventory refused " + relicName);
                return false;
            }

            return RelicEffects.Has(definition.effect);
        }

        static CombatManager BootCombat(GameSession session)
        {
            CombatManager manager = NewComponent<CombatManager>("RelicProbeCombat");
            manager.StartCombat();
            return manager;
        }

        static RelicDefinition FindRelic(GameDatabase db, string relicName)
        {
            if (db == null || db.allItems == null) return null;

            foreach (ItemData item in db.allItems)
            {
                var relic = item as RelicDefinition;
                if (relic != null && relic.itemName == relicName) return relic;
            }

            return null;
        }

        static T NewComponent<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;
            made.Add(go);

            return go.AddComponent<T>();
        }

        /// <summary>Takes the fight back out of the scene. Nothing here is meant to outlive the report.</summary>
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

        /// <summary>
        /// Calls one of the fight's own hooks. These are private on purpose - they are the single funnels the
        /// relics hang off, and a public entry point for each would be a second way to lose health that skips
        /// them - so the probe reads them the way a caller inside the fight would reach them.
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

        static string LogLinesSince(CombatState state, int from)
        {
            if (state == null) return "";

            var sb = new StringBuilder();
            for (int i = from; i < state.log.Count; i++) sb.AppendLine("      " + state.log[i]);

            return sb.ToString();
        }

        static string HandNames(CombatState state)
        {
            if (state == null) return "";

            var names = new List<string>();
            foreach (CardData card in state.hand) names.Add(card != null ? card.cardName : "null");

            return string.Join(", ", names.ToArray());
        }

        static string Plays(List<int> plays)
        {
            if (plays.Count == 0) return "nothing";

            var parts = new List<string>();
            foreach (int play in plays) parts.Add(play.ToString());

            return "plays " + string.Join(", ", parts.ToArray());
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

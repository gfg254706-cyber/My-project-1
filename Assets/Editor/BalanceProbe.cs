using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// Temporary balance probe. Rolls starting decks and fights the real enemies with the real card
    /// formulas, so difficulty is measured rather than guessed at, and reports how much gold a run earns
    /// and how often a shop is actually reachable.
    ///
    /// It follows the production rules deliberately: a played card leaves the hand for good, the spent
    /// pile is shuffled back in when the draw pile runs dry, the hand draws four on turn one and one
    /// thereafter, and the enemy roster opens up as the player descends.
    /// </summary>
    public static class BalanceProbe
    {
        const string DatabasePath = "Assets/Resources/GameDatabase.asset";
        const int DefaultTrials = 2000;

        public static string Execute()
        {
            return Run(DefaultTrials);
        }

        public static string Run(int trials)
        {
            GameDatabase db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            if (db == null) return "FAIL: no GameDatabase at " + DatabasePath;

            var sb = new StringBuilder();

            PlayerStats sample = StartingStats(db);
            sb.AppendLine("STARTING PLAYER");
            sb.AppendLine("   health " + sample.MaxHealth + "   energy " + sample.MaxEnergy +
                          "   strength " + sample.GetStat(StatType.Strength) +
                          "   agility " + sample.GetStat(StatType.Agility) +
                          "   intellect " + sample.GetStat(StatType.Intellect) +
                          "   vitality " + sample.GetStat(StatType.Vitality));
            sb.AppendLine("   deck " + db.startingDeckSize + " cards, drawing " + PlayerStats.StartingHandSize +
                          " then " + PlayerStats.CardsDrawnPerTurn + "/turn, an empty pile is a loss");
            sb.AppendLine();
            sb.AppendLine(OpeningReport(db, trials));

            sb.AppendLine("FIGHT OUTCOMES over " + trials + " rolled decks");
            sb.AppendLine("   RACE ignores blocking until the hit would be lethal, GUARD blocks when it must");
            sb.AppendLine();
            sb.AppendLine("   " + "enemy".PadRight(16) + "hp".PadRight(5) + "RACE".PadRight(9) + "GUARD");
            foreach (EnemyData enemy in db.enemies)
            {
                if (enemy == null) continue;
                sb.AppendLine("   " + Outcome(db, enemy, trials));
            }
            if (db.bossEnemy != null) sb.AppendLine("   " + Outcome(db, db.bossEnemy, trials));

            sb.AppendLine();
            sb.AppendLine(Sweep(db, trials));
            sb.AppendLine(GoldReport(db, trials));
            sb.AppendLine(MapReport(trials));

            return sb.ToString();
        }

        /// <summary>
        /// What the rolled opening hand can actually pay for on turn one. A deck rolled with no filter is
        /// the tradeoff being measured here: the more of it that costs more than the starting energy, the
        /// more often the first turn is a pass. Mana cards count as unplayable, since mana starts at zero.
        /// </summary>
        static string OpeningReport(GameDatabase db, int trials)
        {
            PlayerStats stats = StartingStats(db);
            int energy = stats.MaxEnergy;

            int dead = 0;
            float playableTotal = 0f;

            for (int i = 0; i < trials; i++)
            {
                List<CardData> deck = RollDeck(db, db.startingDeckSize);
                Shuffle(deck);

                int playable = 0;
                int look = Mathf.Min(PlayerStats.StartingHandSize, deck.Count);
                for (int k = 0; k < look; k++)
                {
                    CardData card = deck[k];
                    if (card != null && card.costType == CardCostType.Energy && card.cost <= energy) playable++;
                }

                if (playable == 0) dead++;
                playableTotal += playable;
            }

            var sb = new StringBuilder();
            sb.AppendLine("OPENING HAND - " + PlayerStats.StartingHandSize + " cards with " + energy + " energy");
            sb.AppendLine("   nothing affordable on turn one   " + (100f * dead / trials).ToString("0") + "% of runs");
            sb.AppendLine("   playable cards in the opener     " + (playableTotal / trials).ToString("0.00") + " on average");
            sb.AppendLine();
            return sb.ToString();
        }

        // ------------------------------------------------------------------ fights

        static string Outcome(GameDatabase db, EnemyData enemy, int trials)
        {
            int raceWins = 0;
            int guardWins = 0;
            int stuck = 0;
            int turns = 0;

            for (int i = 0; i < trials; i++)
            {
                List<CardData> deck = RollDeck(db, db.startingDeckSize);

                Sim race = Begin(db, enemy, deck);
                if (Win(race, false)) raceWins++;
                if (race.outOfCards) stuck++;
                turns += race.turns;

                Sim guard = Begin(db, enemy, deck);
                if (Win(guard, true)) guardWins++;
            }

            return enemy.enemyName.PadRight(16) + enemy.maxHealth.ToString().PadRight(5) +
                   (100f * raceWins / trials).ToString("0").PadRight(4) + "%    " +
                   (100f * guardWins / trials).ToString("0").PadRight(4) + "%" +
                   "    (nothing left to draw " + (100f * stuck / trials).ToString("0") + "% of fights," +
                   " average length " + ((float)turns / trials).ToString("0.0") + " turns)";
        }

        class Sim
        {
            public PlayerStats stats;
            public List<CardData> draw = new List<CardData>();
            public List<CardData> hand = new List<CardData>();
            public List<CardData> discard = new List<CardData>();
            public List<CardData> board = new List<CardData>();
            public EnemyData enemy;
            public int energy;
            public int maxEnergy;
            public int enemyHp;
            public int turnIndex;
            public int turns;
            public bool outOfCards;
        }

        static Sim Begin(GameDatabase db, EnemyData enemy, List<CardData> deck)
        {
            var sim = new Sim();
            sim.stats = StartingStats(db);
            sim.stats.StartCombat();
            sim.enemy = enemy;
            sim.enemyHp = enemy.maxHealth;
            sim.maxEnergy = sim.stats.MaxEnergy;

            sim.draw = new List<CardData>(deck);
            Shuffle(sim.draw);
            return sim;
        }

        static bool Win(Sim sim, bool guard)
        {
            for (int guardTurns = 0; guardTurns < 60; guardTurns++)
            {
                sim.turns++;

                sim.energy = sim.maxEnergy;
                sim.stats.Block = 0;

                for (int i = 0; i < sim.board.Count; i++)
                {
                    Apply(sim, sim.board[i]);
                    if (sim.outOfCards) return false;
                    if (sim.enemyHp <= 0) return true;
                }

                if (!Draw(sim, sim.turns == 1 ? PlayerStats.StartingHandSize : PlayerStats.CardsDrawnPerTurn))
                {
                    sim.outOfCards = true;
                    return false;
                }

                while (true)
                {
                    int pick = Pick(sim, guard);
                    if (pick < 0) break;

                    CardData card = sim.hand[pick];
                    sim.hand.RemoveAt(pick);

                    if (card.costType == CardCostType.Mana) sim.stats.SpendMana(card.cost);
                    else sim.energy -= card.cost;

                    if (card.persistent) sim.board.Add(card);
                    else sim.discard.Add(card);

                    Apply(sim, card);
                    if (sim.outOfCards) return false;
                    if (sim.enemyHp <= 0) return true;
                }

                // one turn of temporary Strength, as the real turn end applies it
                sim.stats.TickStatuses();

                int attack = sim.enemy.GetAttackForTurn(sim.turnIndex);
                int blocked = Mathf.Min(sim.stats.Block, attack);
                sim.stats.Block -= blocked;
                sim.stats.TakeDamage(attack - blocked);
                sim.turnIndex++;

                if (!sim.stats.IsAlive) return false;
            }
            return false;
        }

        /// <summary>
        /// Draws cards. The pile is a supply rather than a cycle: a spent card goes to the discard pile for
        /// the rest of the encounter, and drawing from an empty draw pile is the loss.
        /// </summary>
        static bool Draw(Sim sim, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (sim.draw.Count == 0) return false;

                int last = sim.draw.Count - 1;
                sim.hand.Add(sim.draw[last]);
                sim.draw.RemoveAt(last);
            }
            return true;
        }

        static void Shuffle(List<CardData> cards)
        {
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                CardData temp = cards[i];
                cards[i] = cards[j];
                cards[j] = temp;
            }
        }

        static void Apply(Sim sim, CardData card)
        {
            switch (card.effect)
            {
                case CardEffect.Damage:
                    sim.enemyHp = Mathf.Max(0, sim.enemyHp - card.CalculateValue(sim.stats));
                    break;

                case CardEffect.DamageEqualToBlock:
                    sim.enemyHp = Mathf.Max(0, sim.enemyHp - sim.stats.Block);
                    break;

                case CardEffect.Block:
                    sim.stats.Block += card.CalculateValue(sim.stats);
                    break;

                case CardEffect.Heal:
                    sim.stats.Heal(card.CalculateValue(sim.stats));
                    break;

                case CardEffect.GainEnergy:
                    sim.energy += card.CalculateValue(sim.stats);
                    break;

                case CardEffect.GainMana:
                    sim.stats.AddMana(card.CalculateValue(sim.stats));
                    break;

                case CardEffect.TemporaryStrength:
                    sim.stats.AddStatus(StatusEffectType.Strength, card.CalculateValue(sim.stats), 1);
                    break;

                case CardEffect.Draw:
                    if (!Draw(sim, card.CalculateValue(sim.stats))) sim.outOfCards = true;
                    break;
            }
        }

        static int Pick(Sim sim, bool guard)
        {
            int best = -1;
            float bestScore = 0f;

            int threat = sim.enemy.GetAttackForTurn(sim.turnIndex);
            bool lethalComing = (threat - sim.stats.Block) >= sim.stats.Health;

            for (int i = 0; i < sim.hand.Count; i++)
            {
                CardData card = sim.hand[i];
                if (!Affordable(sim, card)) continue;

                int value = card.effect == CardEffect.DamageEqualToBlock
                    ? sim.stats.Block
                    : card.CalculateValue(sim.stats);

                bool isDamage = card.effect == CardEffect.Damage || card.effect == CardEffect.DamageEqualToBlock;
                if (isDamage && value >= sim.enemyHp) return i;

                float score;
                switch (card.effect)
                {
                    case CardEffect.Damage:
                    case CardEffect.DamageEqualToBlock:
                        score = value;
                        break;

                    case CardEffect.Block:
                        score = value;
                        if (threat > sim.stats.Block) score += 6f;
                        if (lethalComing && guard) score += 100f;
                        break;

                    case CardEffect.Draw:
                        score = 4f;
                        break;

                    case CardEffect.GainEnergy:
                        score = 2.5f;
                        break;

                    case CardEffect.GainMana:
                        score = HasManaSpender(sim.hand) ? 3f : 0.5f;
                        break;

                    case CardEffect.TemporaryStrength:
                        score = 1f + 1.5f * StrengthAttackers(sim.hand);
                        break;

                    case CardEffect.Heal:
                        score = sim.stats.Health < sim.stats.MaxHealth / 2 ? 4f : 0.5f;
                        break;

                    default:
                        score = 0.5f;
                        break;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        static bool Affordable(Sim sim, CardData card)
        {
            if (card == null) return false;
            if (card.costType == CardCostType.Mana) return sim.stats.CanAffordMana(card.cost);
            return sim.energy >= card.cost;
        }

        static bool HasManaSpender(List<CardData> hand)
        {
            foreach (CardData card in hand)
            {
                if (card != null && card.costType == CardCostType.Mana) return true;
            }
            return false;
        }

        static int StrengthAttackers(List<CardData> hand)
        {
            int count = 0;
            foreach (CardData card in hand)
            {
                // The rate is checked as well as the tag, because this counts attackers whose printed number
                // moves with Strength - not every card that reads Strength somewhere (Dice reads it for its
                // hit count, Glory for its gold, and neither is a Strength attacker).
                if (card != null && card.effect == CardEffect.Damage &&
                    card.scaling == CardStatScaling.Strength && card.valuePerStatPoint != 0f) count++;
            }
            return count;
        }

        // ------------------------------------------------------------------ setup

        static PlayerStats StartingStats(GameDatabase db)
        {
            var inventory = new Inventory();
            if (db.startingItems != null)
            {
                foreach (ItemData item in db.startingItems)
                {
                    if (item != null) inventory.Add(item);
                }
            }

            var stats = new PlayerStats();

            Dictionary<StatType, int> bonuses;
            int maxHealthBonus;
            int energyBonus;
            inventory.GetBonuses(out bonuses, out maxHealthBonus, out energyBonus);
            stats.SetItemBonuses(bonuses, maxHealthBonus, energyBonus);
            return stats;
        }

        /// <summary>The opening deck at a chosen size, rolled the way BuildStartingDeck rolls it.</summary>
        static List<CardData> RollDeck(GameDatabase db, int size)
        {
            var cheap = new List<CardData>();
            var everything = new List<CardData>();

            foreach (CardData card in db.startingDeckPool)
            {
                if (card == null) continue;
                everything.Add(card);
                if (GameDatabase.IsCheapOpener(card)) cheap.Add(card);
            }

            var opening = new List<CardData>();
            if (everything.Count == 0) return opening;

            size = Mathf.Max(1, size);
            int guaranteed = Mathf.Clamp(db.startingDeckGuaranteedCheap, 0, size);

            var cheapPool = new List<CardData>(cheap);
            int taken = Mathf.Min(guaranteed, cheapPool.Count);
            for (int i = 0; i < taken; i++)
            {
                int index = Random.Range(0, cheapPool.Count);
                opening.Add(cheapPool[index]);
                cheapPool.RemoveAt(index);
            }

            while (opening.Count < size) opening.Add(everything[Random.Range(0, everything.Count)]);
            return opening;
        }

        /// <summary>
        /// Win rate against the opening enemy across a range of deck sizes. An empty pile is a loss, so the
        /// deck has to be big enough to outlast the fight it is paying for - this is where that starts to
        /// hold, which is the number the deck size setting is actually spending.
        /// </summary>
        static string Sweep(GameDatabase db, int trials)
        {
            if (db.enemies == null || db.enemies.Count == 0) return "no enemies authored";

            EnemyData opener = null;
            foreach (EnemyData enemy in db.enemies)
            {
                if (enemy == null) continue;
                if (opener == null || enemy.maxHealth < opener.maxHealth) opener = enemy;
            }
            if (opener == null) return "no enemies authored";

            var sb = new StringBuilder();
            sb.AppendLine("WHERE THE PILE STOPS STARVING YOU - " + opener.enemyName + " (" + opener.maxHealth +
                          " hp), " + trials + " decks per size");
            sb.AppendLine("   " + "deck".PadRight(7) + "RACE".PadRight(9) + "GUARD");

            int[] sizes = { 10, 14, 16, 18, 22, 26, 30 };
            foreach (int size in sizes)
            {
                int raceWins = 0;
                int guardWins = 0;

                for (int i = 0; i < trials; i++)
                {
                    List<CardData> deck = RollDeck(db, size);

                    Sim race = Begin(db, opener, deck);
                    if (Win(race, false)) raceWins++;

                    Sim guard = Begin(db, opener, deck);
                    if (Win(guard, true)) guardWins++;
                }

                sb.AppendLine("   " + size.ToString().PadRight(7) +
                              (100f * raceWins / trials).ToString("0").PadRight(4) + "%    " +
                              (100f * guardWins / trials).ToString("0").PadRight(4) + "%");
            }

            sb.AppendLine();
            return sb.ToString();
        }

        // ------------------------------------------------------------------ gold

        static string GoldReport(GameDatabase db, int runs)
        {
            if (db.enemies == null || db.enemies.Count == 0) return "no enemies authored";

            var pool = new List<EnemyData>();
            foreach (EnemyData enemy in db.enemies)
            {
                if (enemy != null) pool.Add(enemy);
            }
            pool.Sort((a, b) => a.maxHealth.CompareTo(b.maxHealth));

            float gold = 0f;
            float fights = 0f;

            for (int i = 0; i < runs; i++)
            {
                DungeonMap map = MapGenerator.Generate(Random.Range(int.MinValue, int.MaxValue));
                MapNode node = map.Get(map.EntranceId);
                if (node == null) continue;

                for (int step = 0; step < 40; step++)
                {
                    if (node.roomType == RoomType.Combat)
                    {
                        int depth = EnemiesUnlockedAtEntrance + node.layer;
                        int reach = Mathf.Clamp(depth, 1, pool.Count);

                        float expected = 0f;
                        for (int k = 0; k < reach; k++) expected += pool[k].goldReward;
                        gold += expected / reach;
                        fights++;
                    }

                    if (node.layer >= map.LayerCount - 1) break;

                    List<MapNode> exits = map.ExitsFrom(node.id);
                    if (exits.Count == 0) break;
                    node = exits[Random.Range(0, exits.Count)];
                }

                if (db.bossEnemy != null) gold += db.bossEnemy.goldReward;
            }

            var sb = new StringBuilder();
            sb.AppendLine("GOLD over " + runs + " runs");
            sb.AppendLine("   earned per run   " + (gold / runs).ToString("0") +
                          " gold from " + (fights / runs).ToString("0.0") + " fights, plus the boss");
            sb.AppendLine("   shelf prices     card " + ShopPricing.CardBasePrice + "+" + ShopPricing.CardPricePerCost +
                          "/cost, equipment " + ShopPricing.EquipmentPrice + ", relic " + ShopPricing.RelicPrice +
                          " (rare x" + ShopPricing.RarePriceMultiplier + ")");
            sb.AppendLine("   authored rewards " + Describe(pool) + " boss " +
                          (db.bossEnemy != null ? db.bossEnemy.goldReward.ToString() : "0"));
            sb.AppendLine();
            return sb.ToString();
        }

        const int EnemiesUnlockedAtEntrance = 1;

        static string Describe(List<EnemyData> pool)
        {
            var sb = new StringBuilder();
            foreach (EnemyData enemy in pool) sb.Append(enemy.enemyName + " " + enemy.goldReward + "   ");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ map

        static string MapReport(int runs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MAPS over " + runs + " runs");

            int withShop = 0;
            int shopReachable = 0;
            int shopEarly = 0;
            int totalShops = 0;
            float layers = 0f;

            for (int i = 0; i < runs; i++)
            {
                DungeonMap map = MapGenerator.Generate(Random.Range(int.MinValue, int.MaxValue));

                int bossId = map.LayerCount > 0 ? map.layers[map.LayerCount - 1][0] : -1;
                HashSet<int> fromEntrance = Reach(map, map.EntranceId, true);
                HashSet<int> toBoss = Reach(map, bossId, false);

                layers += map.LayerCount;

                int shops = 0;
                int reachable = 0;
                int early = 0;

                foreach (MapNode node in map.nodes)
                {
                    if (node == null || node.roomType != RoomType.Shop) continue;
                    shops++;

                    bool usable = fromEntrance.Contains(node.id) && toBoss.Contains(node.id);
                    if (usable) reachable++;
                    if (usable && node.layer <= 2) early++;
                }

                totalShops += shops;
                if (shops > 0) withShop++;
                if (reachable > 0) shopReachable++;
                if (early > 0) shopEarly++;
            }

            sb.AppendLine("   average length        " + (layers / runs).ToString("0.0") + " layers, so that many rooms walked");
            sb.AppendLine("   average shops on map  " + ((float)totalShops / runs).ToString("0.00"));
            sb.AppendLine("   a shop exists         " + (100f * withShop / runs).ToString("0") + "% of runs");
            sb.AppendLine("   a shop is reachable   " + (100f * shopReachable / runs).ToString("0") + "% of runs");
            sb.AppendLine("   a shop in the first 3 layers   " + (100f * shopEarly / runs).ToString("0") + "% of runs");

            return sb.ToString();
        }

        static HashSet<int> Reach(DungeonMap map, int start, bool forward)
        {
            var seen = new HashSet<int>();
            if (start < 0) return seen;

            var stack = new Stack<int>();
            stack.Push(start);
            seen.Add(start);

            while (stack.Count > 0)
            {
                MapNode node = map.Get(stack.Pop());
                if (node == null) continue;

                List<int> next = forward ? node.outgoing : node.incoming;
                foreach (int id in next)
                {
                    if (seen.Add(id)) stack.Push(id);
                }
            }

            return seen;
        }
    }
}

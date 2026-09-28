using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonCards
{
    /// <summary>
    /// Owns the run: player stats, inventory, deck, experience, the generated dungeon map and the room
    /// the player is standing in. Survives scene loads, and creates itself when a scene is played
    /// directly from the editor.
    /// </summary>
    public class GameSession : MonoBehaviour
    {
        public const string MainMenuScene = "MainMenu";
        public const string CombatScene = "Combat";
        public const string RewardScene = "Reward";
        public const string PathSelectionScene = "PathSelection";
        public const string RoomScene = "Room";
        public const string CharacterScene = "Character";

        /// <summary>How many rooms ahead a vantage map reads. It is measured from where the player stands.</summary>
        public const int MapRevealLayers = 3;

        /// <summary>How many encounters a vantage map stays usable for.</summary>
        public const int MapRevealEncounters = 3;

        /// <summary>How many cards are offered after a fight. The player takes one, or none.</summary>
        public const int RewardChoiceCount = 3;

        static GameSession instance;

        public static GameSession Instance
        {
            get
            {
                if (instance == null) instance = FindExistingSession();
                if (instance == null)
                {
                    var go = new GameObject("GameSession");
                    instance = go.AddComponent<GameSession>();
                }
                return instance;
            }
        }

        [SerializeField] GameDatabase database;

        public GameDatabase Database
        {
            get
            {
                if (database == null) database = Resources.Load<GameDatabase>("GameDatabase");
                return database;
            }
        }

        public PlayerStats Stats { get; private set; }
        public Inventory Inventory { get; private set; }
        public Deck[] Decks { get; private set; }
        public int ActiveDeckIndex { get; set; }

        /// <summary>
        /// The currently active deck (Decks[ActiveDeckIndex]). This is where the drawpile comes from during combat.
        /// </summary>
        public Deck Deck
        {
            get
            {
                if (Decks == null || Decks.Length == 0)
                {
                    Decks = new Deck[3];
                    ActiveDeckIndex = 0;
                    for (int i = 0; i < Decks.Length; i++) Decks[i] = new Deck();
                }
                int index = Mathf.Clamp(ActiveDeckIndex, 0, Decks.Length - 1);
                if (Decks[index] == null) Decks[index] = new Deck();
                return Decks[index];
            }
        }

        /// <summary>
        /// Every card the player has ever been granted, including copies they have since taken back out
        /// of the deck. This is the ceiling the deck view edits against: a card can never be added more
        /// times than it was granted, and taking one out and putting it back is lossless.
        /// </summary>
        readonly List<CardData> owned = new List<CardData>();

        public IReadOnlyList<CardData> Owned { get { return owned; } }

        /// <summary>Hands the player a card: it joins the collection and the deck together.</summary>
        public void GrantCard(CardData card, int amount = 1)
        {
            if (card == null || amount <= 0) return;

            for (int i = 0; i < amount; i++) owned.Add(card);
            if (Deck != null) Deck.Add(card, amount);
        }

        /// <summary>
        /// Takes copies away for good, as an event that confiscates a card does. Ownership drops with the
        /// deck, so a lost card cannot simply be added straight back from the deck view.
        /// </summary>
        public int LoseCard(CardData card, int amount)
        {
            if (card == null || amount <= 0 || Deck == null) return 0;

            int lost = Deck.RemoveUpTo(card, amount);
            for (int i = 0; i < lost; i++)
            {
                int index = owned.IndexOf(card);
                if (index < 0) break;
                owned.RemoveAt(index);
            }

            if (Decks != null)
            {
                int remaining = OwnedCount(card);
                for (int i = 0; i < Decks.Length; i++)
                {
                    if (Decks[i] != null)
                    {
                        while (Decks[i].CountOf(card) > remaining)
                        {
                            Decks[i].Remove(card, 1);
                        }
                    }
                }
            }

            return lost;
        }

        /// <summary>How many copies of a card the player owns, whether or not they are in the deck.</summary>
        public int OwnedCount(CardData card)
        {
            if (card == null) return 0;

            int count = 0;
            foreach (CardData entry in owned)
            {
                if (entry == card) count++;
            }
            return count;
        }

        public DungeonMap Map { get; private set; }
        public MapReveal MapReveal { get; private set; }

        public int Experience { get; private set; }
        public int Level { get; private set; }
        public int RoomsExplored { get; private set; }
        public int EncountersCleared { get; private set; }
        public int LastExperienceReward { get; private set; }

        /// <summary>
        /// The gold the last defeated enemy paid. The purse itself is Gold; this is only the last deposit,
        /// so the reward screen can show what the fight earned alongside what it taught.
        /// </summary>
        public int LastGoldReward { get; private set; }

        /// <summary>
        /// The run's purse. It survives scene loads with the rest of the session, so it is the one number
        /// the shop and the sell screen both read and write.
        /// </summary>
        public int Gold { get; private set; }

        public void AddGold(int amount)
        {
            if (amount <= 0) return;
            Gold += amount;
        }

        /// <summary>Takes gold out of the purse if there is enough of it. Returns false when there is not.</summary>
        public bool SpendGold(int amount)
        {
            if (amount <= 0) return false;
            if (Gold < amount) return false;

            Gold -= amount;
            return true;
        }

        public EnemyData CurrentEnemy { get; set; }

        /// <summary>An enemy an event has forced the player into fighting. Takes priority over the room's own roll.</summary>
        public EnemyData PendingEncounter { get; set; }

        public string LastDeathReason { get; private set; }

        /// <summary>True once the boss has fallen, so the exit screen is shown instead of the room the player is standing in.</summary>
        public bool RunWon { get; private set; }

        /// <summary>The one seed this run was built from. Event rolls mix it in so the same room is not the same event every run.</summary>
        public int RunSeed { get; private set; }

        /// <summary>Encounters cleared since an equipped item last handed over a card.</summary>
        public int EncountersSinceCardGrant { get; private set; }

        /// <summary>Set when an equipped item just granted a card, so the next screen can mention it.</summary>
        public CardData LastGrantedCard { get; private set; }

        /// <summary>A one-shot line describing something an item did, drained by whichever screen shows next.</summary>
        public string ItemNotice { get; private set; }

        /// <summary>Reads the pending item notice without consuming it.</summary>
        public string ItemNoticeText { get { return ItemNotice; } }

        /// <summary>True once the current event room's choice has been made, so returning to the room keeps its result.</summary>
        public bool RoomResolved { get; set; }

        /// <summary>Text describing what the resolved room did, shown when the room is revisited.</summary>
        public string LastRoomResultText { get; set; }

        /// <summary>True when the resolved room's outcome killed the player.</summary>
        public bool LastRoomFatal { get; set; }

        /// <summary>Where the character menu and other side screens return to.</summary>
        public string ReturnScene { get; set; }

        bool mapExpiredNoticePending;

        public int ExperiencePerLevel
        {
            get { return (Database != null && Database.experiencePerLevel > 0) ? Database.experiencePerLevel : 100; }
        }

        /// <summary>Experience needed to buy the next level. Spent through the character menu.</summary>
        public int ExperienceForNextLevel { get { return ExperiencePerLevel * Mathf.Max(1, Level); } }

        public MapNode CurrentNode { get { return Map != null ? Map.Current : null; } }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            if (Stats == null) StartNewRun();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        static GameSession FindExistingSession()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<GameSession>(true);
                if (found != null) return found;
            }
            return null;
        }

        // ------------------------------------------------------------------ run setup

        /// <summary>Builds a fresh run without loading a scene.</summary>
        public void StartNewRun()
        {
            Stats = new PlayerStats();
            Inventory = new Inventory();
            Inventory.Changed -= ApplyInventoryBonuses;
            Inventory.Changed += ApplyInventoryBonuses;
            Decks = new Deck[3];
            ActiveDeckIndex = 0;
            for (int i = 0; i < Decks.Length; i++) Decks[i] = new Deck();

            // The collection belongs to the run, not to the session. It is the ceiling the deck view and the
            // shop both read, so leaving it standing would let a new run be refilled from the cards the last
            // run collected, on top of its opening deck.
            owned.Clear();

            GameDatabase db = Database;
            if (db != null)
            {
                BuildStartingDeck(db);
                if (db.startingItems != null)
                {
                    foreach (ItemData item in db.startingItems)
                    {
                        if (item != null) Inventory.Add(item);
                    }
                }
            }
            else
            {
                Debug.LogWarning("[GameSession] No GameDatabase found. Expected Assets/Resources/GameDatabase.asset.");
            }

            ApplyInventoryBonuses();

            // The whole dungeon is laid out now, from one seed, and never re-rolled as you play.
            RunSeed = Random.Range(int.MinValue, int.MaxValue);
            Map = MapGenerator.Generate(RunSeed);
            Map.CurrentNodeId = Map.EntranceId;
            MapReveal = new MapReveal();

            Experience = 0;
            Level = 1;
            RoomsExplored = 0;
            EncountersCleared = 0;
            LastExperienceReward = 0;
            LastGoldReward = 0;
            Gold = 0;

            // The shelves belong to the room, not the run, so a new run starts with them empty.
            shopNodeId = -1;
            shopCards.Clear();
            shopRelics.Clear();
            shopEquipment.Clear();
            CurrentEnemy = null;
            PendingEncounter = null;
            LastDeathReason = null;
            RoomResolved = false;
            LastRoomResultText = null;
            LastRoomFatal = false;
            ReturnScene = MainMenuScene;
            mapExpiredNoticePending = false;
            RunWon = false;
            EncountersSinceCardGrant = 0;
            LastGrantedCard = null;
            ItemNotice = null;
            RewardChoices = new List<CardData>();

            // A new run is not the middle of a fight, whatever the old run was doing.
            Combat = null;
        }

        /// <summary>
        /// Builds the opening deck. An authored deck is dealt exactly as written, because its composition is
        /// the whole point of it; otherwise the pool is rolled, with a handful of cheap cards guaranteed so
        /// the first fight is winnable rather than a hand of unaffordable setup.
        /// </summary>
        void BuildStartingDeck(GameDatabase db)
        {
            // Taken verbatim rather than sampled. Rolling the authored cards would deal a different mix of
            // the same few cards every run, which is the thing authoring the deck exists to stop.
            if (db.startingDeck != null && db.startingDeck.Count > 0)
            {
                foreach (CardData card in db.startingDeck)
                {
                    if (card != null) GrantCard(card);
                }
                return;
            }

            if (db.startingDeckPool == null || db.startingDeckPool.Count == 0) return;

            var cheap = new List<CardData>();
            var everything = new List<CardData>();
            foreach (CardData card in db.startingDeckPool)
            {
                if (card == null) continue;
                everything.Add(card);
                if (GameDatabase.IsCheapOpener(card)) cheap.Add(card);
            }

            if (everything.Count == 0) return;

            int size = Mathf.Max(1, db.startingDeckSize);
            int guaranteed = Mathf.Clamp(db.startingDeckGuaranteedCheap, 0, size);

            var opening = new List<CardData>();

            // Opening plays first, taken without repeats so the guaranteed cards are genuinely varied.
            var cheapPool = new List<CardData>(cheap);
            int guaranteedTaken = Mathf.Min(guaranteed, cheapPool.Count);
            for (int i = 0; i < guaranteedTaken; i++)
            {
                int index = Random.Range(0, cheapPool.Count);
                opening.Add(cheapPool[index]);
                cheapPool.RemoveAt(index);
            }

            // The rest is an unfiltered roll, repeats included, which is what makes one run lean
            // Strength and the next lean Intellect.
            while (opening.Count < size) opening.Add(everything[Random.Range(0, everything.Count)]);

            for (int i = opening.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                CardData temp = opening[i];
                opening[i] = opening[j];
                opening[j] = temp;
            }

            foreach (CardData card in opening) GrantCard(card);

        }

        /// <summary>
        /// The cards offered after a fight, from which the player takes one.
        ///
        /// The offer is drawn evenly from the whole pool. It deliberately does not lean toward whatever
        /// the deck already carries: that made runs converge on one build and handed the player a safer
        /// deck than they earned, which removed the decision the offer exists to create.
        /// </summary>
        public List<CardData> RollCardRewards(int count)
        {
            var offers = new List<CardData>();
            GameDatabase db = Database;
            if (db == null || db.allCards == null || db.allCards.Count == 0) return offers;

            for (int i = 0; i < count; i++)
            {
                CardData pick = PickUnused(db.allCards, offers);
                if (pick == null) break;

                offers.Add(pick);
            }

            return offers;
        }

        /// <summary>
        /// A random card from the pool that has not already been offered, drawn with the rarity as the
        /// weight: a Common turns up three times as often as a Rare and twice as often as an Uncommon. That
        /// is the whole of what rarity does, and it is what makes finding one feel like something.
        /// </summary>
        static CardData PickUnused(List<CardData> pool, List<CardData> alreadyOffered)
        {
            var available = new List<CardData>();
            int totalWeight = 0;

            foreach (CardData card in pool)
            {
                if (card == null || alreadyOffered.Contains(card)) continue;
                available.Add(card);
                totalWeight += Mathf.Max(1, (int)card.rarity);
            }

            if (available.Count == 0) return null;

            int roll = Random.Range(0, totalWeight);
            for (int i = 0; i < available.Count; i++)
            {
                roll -= Mathf.Max(1, (int)available[i].rarity);
                if (roll < 0) return available[i];
            }

            return available[available.Count - 1];
        }

        /// <summary>Starts a fresh run and drops the player into the entrance room.</summary>
        public void StartRun()
        {
            StartNewRun();
            EnterNode(Map.EntranceId);
            LoadCurrentRoom();
        }

        /// <summary>Records why the run ended, so the main menu can report it.</summary>
        public void RecordDeath(string reason)
        {
            LastDeathReason = reason;
        }

        /// <summary>Ends the run and returns to the main menu. Used by every death and by the exit room.</summary>
        public void AbandonRun(string reason = null)
        {
            if (!string.IsNullOrEmpty(reason)) LastDeathReason = reason;
            StartNewRun();
            LoadScene(MainMenuScene);
        }

        void ApplyInventoryBonuses()
        {
            if (Stats == null || Inventory == null) return;

            Dictionary<StatType, int> statBonuses;
            int maxHealthBonus;
            int energyBonus;
            Inventory.GetBonuses(out statBonuses, out maxHealthBonus, out energyBonus);
            Stats.SetItemBonuses(statBonuses, maxHealthBonus, energyBonus);

            // Willpower's floor is lifted by a relic rather than by an attribute, so it is settled here
            // with everything else the bag decides. The floor is read when the stat is written, and a
            // sacrifice card can move it at any time.
            Stats.SetNegativeWillpowerAllowed(RelicEffects.BrokenOathActive);
        }

        // ------------------------------------------------------------------ progression

        public void AddExperience(int amount)
        {
            Experience += Mathf.Max(0, amount);
        }

        /// <summary>Spends experience if available. Experience is not levelled automatically; the player buys levels.</summary>
        public int SpendExperienceUpTo(int amount)
        {
            int spent = Mathf.Min(Mathf.Max(0, amount), Experience);
            Experience -= spent;
            return spent;
        }

        /// <summary>Buys one level in the chosen attribute. Costs the current level's experience price.</summary>
        public bool SpendExperienceOnStat(StatType type)
        {
            int cost = ExperienceForNextLevel;
            if (Experience < cost) return false;

            Experience -= cost;
            Level++;
            Stats.SetBaseStat(type, Stats.GetBaseStat(type) + 1);
            return true;
        }

        /// <summary>
        /// How far down the dungeon the player has got. The entrance room is 0, and it rises by one with
        /// every room descended, which is the only measure of progress the run has.
        /// </summary>
        public int Depth
        {
            get { return CurrentNode != null ? CurrentNode.layer : 0; }
        }

        /// <summary>
        /// An enemy for this room, drawn from as much of the roster as the player's depth has opened up.
        ///
        /// The roster is ordered by how much of a fight each enemy is, and the window opens one enemy at a
        /// time as the player descends. Rolling flat over the whole roster put the Cave Lurker in the very
        /// first room as often as the Goblin, which killed runs in the opening fight before the deck had
        /// grown at all.
        /// </summary>
        public EnemyData RandomEnemy()
        {
            GameDatabase db = Database;
            if (db == null || db.enemies == null || db.enemies.Count == 0) return null;

            var pool = new List<EnemyData>();
            foreach (EnemyData enemy in db.enemies)
            {
                if (enemy != null) pool.Add(enemy);
            }
            if (pool.Count == 0) return null;

            pool.Sort(CompareEnemyDifficulty);

            // One enemy at the entrance, one more per room descended, never closing again once open.
            int reach = Mathf.Clamp(EnemiesUnlockedAtEntrance + Depth, 1, pool.Count);
            return pool[Random.Range(0, reach)];
        }

        /// <summary>How much of the roster is in play in the entrance room. One, so the opening fight is fair.</summary>
        public const int EnemiesUnlockedAtEntrance = 1;

        static int CompareEnemyDifficulty(EnemyData a, EnemyData b)
        {
            int health = a.maxHealth.CompareTo(b.maxHealth);
            if (health != 0) return health;
            return string.CompareOrdinal(a.enemyName, b.enemyName);
        }


        /// <summary>
        /// Drinks a consumable. Health is restored and the item is gone, which is the whole point of
        /// carrying one. Returns false for anything that is not a potion.
        /// </summary>
        public bool UseItem(ItemInstance item)
        {
            if (item == null || item.data == null || !item.data.consumable) return false;

            int before = Stats.Health;
            Stats.Heal(item.data.healAmount);
            int healed = Stats.Health - before;

            Inventory.Remove(item);

            Report(healed > 0
                ? "You drink the " + item.data.itemName + " and recover " + healed + " health."
                : "You drink the " + item.data.itemName + ", but you are already at full health.");
            return true;
        }

        // ------------------------------------------------------------------ map and rooms

        /// <summary>Moves the player into a room, marking it explored and ticking the vantage map.</summary>
        public void EnterNode(int nodeId)
        {
            if (Map == null) return;

            MapNode node = Map.Get(nodeId);
            if (node == null) return;

            Map.CurrentNodeId = nodeId;
            node.visited = true;
            RoomsExplored++;

            RoomResolved = false;
            LastRoomResultText = null;
            LastRoomFatal = false;

            if (MapReveal.IsActive && MapReveal.Tick()) mapExpiredNoticePending = true;

            // The vantage room hands over a fresh map after the old one has been ticked down.
            if (node.roomType == RoomType.Vantage) MapReveal.Grant(MapRevealEncounters);
        }

        /// <summary>Loads the scene that matches the room the player is standing in.</summary>
        public void LoadCurrentRoom()
        {
            MapNode node = CurrentNode;
            if (node == null)
            {
                LoadScene(MainMenuScene);
                return;
            }

            switch (node.roomType)
            {
                case RoomType.Combat:
                    CurrentEnemy = ResolveEncounter();
                    LoadScene(CombatScene);
                    break;
                case RoomType.Boss:
                    CurrentEnemy = BossEnemy();
                    PendingEncounter = null;
                    LoadScene(CombatScene);
                    break;
                case RoomType.Shop:
                    // The shelves are laid out on the way in, the same way a fight is armed on the way in,
                    // so the shop scene always has stock by the time it reads it. The guard inside
                    // PrepareShop keeps whatever is already on the shelves when the player comes back
                    // from the character menu, so a bought-out rack stays bought out.
                    PrepareShop(node.id);
                    LoadScene(RoomScene);
                    break;
                default:
                    LoadScene(RoomScene);
                    break;
            }
        }

        /// <summary>The enemy for this room: an event-forced fight if one is queued, otherwise a roll.</summary>
        EnemyData ResolveEncounter()
        {
            EnemyData enemy = PendingEncounter;
            PendingEncounter = null;
            return enemy != null ? enemy : RandomEnemy();
        }

        /// <summary>The enemy that waits at the bottom of the dungeon.</summary>
        public EnemyData BossEnemy()
        {
            GameDatabase db = Database;
            if (db != null && db.bossEnemy != null) return db.bossEnemy;
            return RandomEnemy();
        }

        /// <summary>Sends the player into a fight an event started, rather than the room's own enemy.</summary>
        public void StartEncounter(EnemyData enemy)
        {
            PendingEncounter = enemy;
            LoadScene(CombatScene);
        }

        /// <summary>Called when the boss falls. The run is over, so the exit screen is shown.</summary>
        public void MarkRunWon()
        {
            RunWon = true;
        }

        /// <summary>Walks through one of the current room's exits.</summary>
        public void ChooseExit(int nodeId)
        {
            EnterNode(nodeId);
            LoadCurrentRoom();
        }

        /// <summary>Called once a room has been dealt with, to show the next junction.</summary>
        public void CompleteRoom()
        {
            LoadScene(PathSelectionScene);
        }

        /// <summary>Called by the combat manager when the enemy is defeated.</summary>
        public void CompleteCombat(EnemyData defeated)
        {
            LastExperienceReward = defeated != null ? defeated.experienceReward : 0;
            AddExperience(LastExperienceReward);

            // Gold is the shop's currency, and a fight is the only dependable way to earn any. Without it
            // a merchant was a window the player could look through but never buy from.
            LastGoldReward = defeated != null ? defeated.goldReward : 0;
            if (LastGoldReward > 0) AddGold(LastGoldReward);

            EncountersCleared++;
            TickItemCardGrants();


            // The choice that makes the deck yours. Rolled now so the reward screen only has to show it.
            RewardChoices = RollCardRewards(RewardChoiceCount);
            RewardClaimed = false;
            LastRewardCard = null;
        }

        /// <summary>The cards offered after the last fight, one of which the player may take.</summary>
        public IReadOnlyList<CardData> RewardChoices { get; private set; }

        /// <summary>
        /// True once one of this fight's offers has been taken.
        ///
        /// It lives on the session rather than on the reward screen because the screen is rebuilt whenever
        /// the player steps into the character menu to read their deck, and a rebuilt screen must not hand
        /// out a second card.
        /// </summary>
        public bool RewardClaimed { get; private set; }

        /// <summary>The card taken from this fight's offer, so a rebuilt screen can still name it.</summary>
        public CardData LastRewardCard { get; private set; }

        /// <summary>
        /// Takes one of the fight's offers. Only the first call per fight does anything, and only for a card
        /// that was actually offered, so the reward cannot be claimed twice or from somewhere else.
        /// </summary>
        public bool ClaimReward(CardData card)
        {
            if (RewardClaimed || card == null || RewardChoices == null) return false;

            bool offered = false;
            for (int i = 0; i < RewardChoices.Count; i++)
            {
                if (RewardChoices[i] == card)
                {
                    offered = true;
                    break;
                }
            }
            if (!offered) return false;

            GrantCard(card);
            LastRewardCard = card;
            RewardClaimed = true;
            return true;
        }

        /// <summary>
        /// Counts encounters for items that pay out a card on a cadence. Only equipped items count, so
        /// taking such an item off pauses it.
        /// </summary>
        void TickItemCardGrants()
        {
            EncountersSinceCardGrant++;

            if (Inventory == null) return;

            int interval = Inventory.CardGrantInterval();
            if (interval <= 0 || EncountersSinceCardGrant < interval) return;

            CardData card = RandomCard();
            EncountersSinceCardGrant = 0;
            if (card == null) return;

            GrantCard(card);
            LastGrantedCard = card;

            ItemNotice = "Your " + Inventory.CardGrantSourceName() + " yielded a " + card.cardName + ".";
        }

        /// <summary>
        /// A card drawn from the database's pool, weighted by rarity exactly as the fight's offers are, for
        /// items that hand one over and for the cards that conjure one out of nothing.
        /// </summary>
        public CardData RandomCard()
        {
            GameDatabase db = Database;
            if (db == null || db.allCards == null || db.allCards.Count == 0) return null;

            return PickUnused(db.allCards, nothingOffered);
        }

        static readonly List<CardData> nothingOffered = new List<CardData>();

        /// <summary>True once, on the screen shown after the vantage map runs out.</summary>
        public bool ConsumeMapExpiredNotice()
        {
            if (!mapExpiredNoticePending) return false;
            mapExpiredNoticePending = false;
            return true;
        }

        /// <summary>True once, on the screen shown after an item has done something worth reporting.</summary>
        public bool ConsumeItemNotice()
        {
            if (string.IsNullOrEmpty(ItemNotice)) return false;
            ItemNotice = null;
            return true;
        }

        /// <summary>Records a one-shot line for the next screen to show.</summary>
        public void Report(string message)
        {
            ItemNotice = message;
        }

        // ------------------------------------------------------------------ combat

        /// <summary>
        /// The fight in progress, or null when the player is not in one.
        ///
        /// It lives here rather than in the combat scene because the player can open the character menu
        /// mid fight, which unloads that scene. A fresh CombatManager reads this back, so the encounter
        /// carries on instead of being rolled again.
        /// </summary>
        public CombatState Combat { get; private set; }

        /// <summary>True while a fight is running and has not yet been won or lost.</summary>
        public bool InCombat { get { return Combat != null && Combat.started && !Combat.finished; } }

        /// <summary>Starts a fresh fight. The combat scene calls this when there is nothing to resume.</summary>
        public CombatState BeginCombat()
        {
            Combat = new CombatState();
            return Combat;
        }

        /// <summary>
        /// Marks the fight as over, so the next combat scene builds a new encounter rather than picking
        /// this one back up. The state is kept rather than cleared: the reward screen still reads it.
        /// </summary>
        public void FinishCombat()
        {
            if (Combat == null) return;
            Combat.finished = true;
            Combat.combatOver = true;
            Combat.enemyTurnPending = false;
        }

        // ------------------------------------------------------------------ shop

        /// <summary>How many cards the shopkeeper lays out.</summary>
        public const int ShopCardCount = 5;

        /// <summary>How many relics the shopkeeper keeps on the rack.</summary>
        public const int ShopRelicCount = 3;

        /// <summary>How many pieces of equipment the shopkeeper has out.</summary>
        public const int ShopEquipmentCount = 2;

        /// <summary>
        /// The shop room whose stock is on the shelves right now. Stock is rolled once per room and kept,
        /// because the character menu leaves and re-enters this scene: without this the shop would re-roll
        /// its whole inventory, and everything the player had already bought would be back on sale.
        /// </summary>
        int shopNodeId = -1;

        readonly List<CardData> shopCards = new List<CardData>();

        /// <summary>
        /// The racks hold instances rather than definitions, because a relic's roll is part of what is
        /// being sold: the player is choosing between this Titan's Grip and that one, and the numbers have
        /// to be on screen before the gold is spent.
        ///
        /// An instance on a rack is not owned yet. It stays out of the Inventory until it is bought, so a
        /// shelf full of relics cannot leak into the player's carry weight or their attributes.
        /// </summary>
        readonly List<ItemInstance> shopRelics = new List<ItemInstance>();
        readonly List<ItemInstance> shopEquipment = new List<ItemInstance>();

        public IReadOnlyList<CardData> ShopCards { get { return shopCards; } }
        public IReadOnlyList<ItemInstance> ShopRelics { get { return shopRelics; } }
        public IReadOnlyList<ItemInstance> ShopEquipment { get { return shopEquipment; } }

        /// <summary>Fills the shelves for a shop room, or leaves the stock alone if it is already laid out.</summary>
        public void PrepareShop(int nodeId)
        {
            if (shopNodeId == nodeId) return;
            shopNodeId = nodeId;

            shopCards.Clear();
            shopRelics.Clear();
            shopEquipment.Clear();

            // The card rack uses the same uniform, unbiased draw as the post-fight offer.
            shopCards.AddRange(RollCardRewards(ShopCardCount));

            FillRack(shopRelics, true, ShopRelicCount);
            FillRack(shopEquipment, false, ShopEquipmentCount);
        }

        /// <summary>
        /// Draws distinct entries for a rack from the item list, split by whether the entry is a relic.
        /// Relics are charms: they work from a bag slot and are what the relic rack trades in.
        /// </summary>
        void FillRack(List<ItemInstance> rack, bool relics, int count)
        {
            GameDatabase db = Database;
            if (db == null || db.allItems == null) return;

            var pool = new List<ItemData>();
            foreach (ItemData item in db.allItems)
            {
                if (item == null) continue;
                if (item.relic != relics) continue;

                // The equipment rack sells things you wear. Charms, potions and relics are not equipment.
                if (!relics && (item.consumable || item.slot == EquipmentSlot.Carried)) continue;

                pool.Add(item);
            }

            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int pick = Random.Range(0, pool.Count);
                ItemData template = pool[pick];
                pool.RemoveAt(pick);

                var instance = new ItemInstance(template);

                // Rolled here rather than at the till, so the number the rack advertises is the number the
                // player receives. Rolling at purchase would make the shelf a lie.
                var definition = template as RelicDefinition;
                if (definition != null)
                {
                    instance.relic = RelicGenerator.Roll(definition, RelicGenerator.RandomSeed());
                }

                rack.Add(instance);
            }
        }

        /// <summary>
        /// Buys a card off the rack: the price is paid and the card joins the deck, and it leaves the shelf
        /// so it cannot be bought twice. Returns false when it is not on sale or the purse is too light.
        /// </summary>
        public bool BuyShopCard(CardData card)
        {
            if (card == null) return false;
            if (!shopCards.Contains(card)) return false;
            if (!SpendGold(ShopPricing.CardPrice(card))) return false;

            GrantCard(card);
            shopCards.Remove(card);
            return true;
        }

        /// <summary>
        /// Buys a relic or a piece of equipment. Nothing is paid unless the player has somewhere to put it,
        /// so a full bag cannot quietly eat the gold. Returns false when it cannot be bought or carried.
        /// </summary>
        public bool BuyShopItem(ItemInstance instance)
        {
            if (instance == null || instance.data == null) return false;
            if (!shopRelics.Contains(instance) && !shopEquipment.Contains(instance)) return false;
            if (!Inventory.CanAccept(instance.data)) return false;
            if (!SpendGold(ShopPricing.ItemPrice(instance.data))) return false;

            // The instance itself is handed over rather than its definition, so the roll the rack showed
            // is the roll the player walks away with.
            Inventory.Add(instance);
            shopRelics.Remove(instance);
            shopEquipment.Remove(instance);
            return true;
        }

        /// <summary>Sells a loose or worn item for gold. The item is gone for good.</summary>
        public bool SellItem(ItemInstance instance)
        {
            if (instance == null || instance.data == null) return false;
            if (!Inventory.Remove(instance)) return false;

            AddGold(ShopPricing.SellPrice(instance.data));
            return true;
        }

        /// <summary>
        /// True when the player is standing in a shop. That is the only place the character menu offers to
        /// sell anything, because the merchant is the only thing that pays.
        /// </summary>
        public bool AtShop
        {
            get { return CurrentNode != null && CurrentNode.roomType == RoomType.Shop; }
        }

        /// <summary>
        /// Sells one copy of a card for gold. Refused when the player does not own it, and refused for the
        /// last card in the deck, so a run cannot be sold out of the deck it needs to keep playing.
        /// </summary>
        public bool SellCard(CardData card)
        {
            if (card == null || Deck == null) return false;
            if (OwnedCount(card) <= 0) return false;
            if (Deck.Count <= 1) return false;

            // Taken out of the deck as well as out of the collection: what was sold is gone, and the deck
            // view cannot hand it back from a copy the player no longer has.
            if (Deck.CountOf(card) > 0) Deck.Remove(card, 1);
            owned.Remove(card);

            if (Decks != null)
            {
                int remaining = OwnedCount(card);
                for (int i = 0; i < Decks.Length; i++)
                {
                    if (Decks[i] != null)
                    {
                        while (Decks[i].CountOf(card) > remaining)
                        {
                            Decks[i].Remove(card, 1);
                        }
                    }
                }
            }

            AddGold(ShopPricing.CardSellPrice(card));
            return true;
        }

        /// <summary>Every card the player could sell, one entry per distinct card owned.</summary>
        public List<CardData> SellableCards()
        {
            return DeckSummary.Distinct(owned);
        }

        /// <summary>Every item the player could sell: loose ones and worn ones, in that order.</summary>
        public List<ItemInstance> SellableItems()
        {
            var items = new List<ItemInstance>();
            if (Inventory == null) return items;

            foreach (ItemInstance item in Inventory.Carried)
            {
                if (item != null && item.data != null) items.Add(item);
            }

            for (int i = 0; i < Inventory.SlotCount; i++)
            {
                ItemInstance item = Inventory.GetSlot(i);
                if (item != null && item.data != null) items.Add(item);
            }

            return items;
        }

        public void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            SceneManager.LoadScene(sceneName);
        }
    }
}

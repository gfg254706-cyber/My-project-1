using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// Turn-based card combat. The player starts each combat with a 4 card hand, draws 1 card per
    /// turn, and spends energy (and mana, for board cards) to play cards. The enemy attacks on its own turn.
    ///
    /// Persistent cards are played onto the board and fire again at the start of every player turn, so an
    /// Intellect build spends its early turns setting up and is paid back for the rest of the fight. A
    /// construct with a duration is the exception: it works for a fixed number of turns and is then spent.
    /// </summary>
    public class CombatManager : MonoBehaviour
    {
        public static CombatManager Instance { get; private set; }

        [Header("Encounter")]
        [Tooltip("Optional. When empty the encounter is taken from the GameSession, or rolled from the database.")]
        [SerializeField] EnemyData enemyOverride;

        [Header("Hand")]
        [SerializeField] CardView cardViewPrefab;
        [SerializeField] Transform handContainer;
        [Tooltip("Fans the hand out. Found on the hand container when it is not set here.")]
        [SerializeField] HandFan handFan;

        [Header("Enemy display")]
        [SerializeField] Text enemyNameText;
        [SerializeField] Text enemyHealthText;
        [SerializeField] Text enemyBlockText;
        [SerializeField] Text enemyStatusText;

        [Header("Player display")]
        [SerializeField] Text playerHealthText;
        [SerializeField] Text playerEnergyText;
        [SerializeField] Text playerManaText;
        [SerializeField] Text playerBlockText;
        [SerializeField] Text playerStatusText;

        [Header("Board")]
        [SerializeField] Text boardText;
        [Tooltip("The squares constructs are played into, in board order, filled from the left.")]
        [SerializeField] BoardSlotView[] boardSlots;
        [Tooltip("The box the squares sit in. Its height follows how many lines are in use.")]
        [SerializeField] RectTransform boardSlotsRect;
        [Tooltip("The line of names under the squares, which follows them down as the row grows.")]
        [SerializeField] RectTransform boardTextRect;

        [Header("Other display")]
        [SerializeField] Text turnText;
        [SerializeField] Text pileText;
        [SerializeField] Text logText;

        [Header("Buttons")]
        [SerializeField] Button endTurnButton;
        [SerializeField] Button characterButton;

        [Header("Defeat")]
        [SerializeField] GameObject defeatPanel;
        [SerializeField] Text defeatReasonText;
        [SerializeField] Button returnToMenuButton;

        [Header("Timing")]
        [SerializeField] float enemyTurnDelay = 0.6f;
        [SerializeField] float victoryDelay = 1.25f;

        /// <summary>
        /// The fight itself, owned by the GameSession rather than by this scene. The scene is unloaded
        /// whenever the player opens the character menu, so the fight has to live somewhere that outlives
        /// it. Everything below is a view onto this.
        /// </summary>
        CombatState state;

        public EnemyData Enemy { get; private set; }

        public int EnemyHealth
        {
            get { return state != null ? state.enemyHealth : 0; }
            private set { if (state != null) state.enemyHealth = value; }
        }

        public int EnemyBlock
        {
            get { return state != null ? state.enemyBlock : 0; }
            private set { if (state != null) state.enemyBlock = value; }
        }

        public int PlayerEnergy
        {
            get { return state != null ? state.playerEnergy : 0; }
            private set { if (state != null) state.playerEnergy = value; }
        }

        public bool IsPlayerTurn
        {
            get { return state != null && state.isPlayerTurn; }
            private set { if (state != null) state.isPlayerTurn = value; }
        }

        public bool CombatOver
        {
            get { return state != null && state.combatOver; }
            private set { if (state != null) state.combatOver = value; }
        }

        public IReadOnlyList<CardData> Hand { get { return hand; } }

        /// <summary>The cards currently in play, fired at the start of each player turn.</summary>
        public IReadOnlyList<BoardCard> Board { get { return board; } }

        /// <summary>Edge of one board square, and the gap between two of them.</summary>
        public const float SlotSize = 52f;
        public const float SlotSpacing = 8f;

        /// <summary>Where the squares begin, measured down from the top of the player panel.</summary>
        public const float SlotRowTop = -224f;

        /// <summary>
        /// How many squares go on one line. A board can be widened to ten, and ten squares abreast would run
        /// out of panel, so past this the row carries on underneath itself.
        /// </summary>
        public const int SlotsPerRow = 5;

        // Each pile is reached through the state rather than held here, so there is exactly one copy of it
        // and the copy that survives the scene load is the one being read and written. The empty lists are
        // the answer before the first fight exists, when there is nothing to point at yet.
        static readonly List<CardData> noCards = new List<CardData>();
        static readonly List<BoardCard> noBoard = new List<BoardCard>();
        static readonly List<StatusEffect> noStatuses = new List<StatusEffect>();
        static readonly List<string> noLog = new List<string>();

        List<CardData> hand { get { return state != null ? state.hand : noCards; } }
        List<CardData> drawPile { get { return state != null ? state.drawPile : noCards; } }
        List<CardData> discardPile { get { return state != null ? state.discardPile : noCards; } }
        List<BoardCard> board { get { return state != null ? state.board : noBoard; } }
        List<StatusEffect> enemyStatuses { get { return state != null ? state.enemyStatuses : noStatuses; } }
        List<string> log { get { return state != null ? state.log : noLog; } }

        int enemyTurnIndex
        {
            get { return state != null ? state.enemyTurnIndex : 0; }
            set { if (state != null) state.enemyTurnIndex = value; }
        }

        bool started
        {
            get { return state != null && state.started; }
            set { if (state != null) state.started = value; }
        }

        readonly List<CardView> handViews = new List<CardView>();

        PlayerStats Stats { get { return GameSession.Instance.Stats; } }

        void Awake()
        {
            Instance = this;

            // Self healing, like the hover wiring on a card: a hand container that carries the fan but was
            // never wired here still fans, instead of stacking every card on the middle of the strip.
            if (handFan == null && handContainer != null) handFan = handContainer.GetComponent<HandFan>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (characterButton != null)
            {
                characterButton.onClick.RemoveAllListeners();
                characterButton.onClick.AddListener(OpenCharacterMenu);
            }

            StartCombat();
        }

        void Update()
        {
            // I opens the character menu: the same screen the rest of the game uses. The fight is held in
            // the GameSession, so stepping out to read the deck does not disturb it.
            if (Hotkeys.CharacterPressed()) OpenCharacterMenu();
        }

        /// <summary>
        /// Opens the character menu over the fight. One screen serves the whole game: the encounter is
        /// held in the GameSession, so looking at the deck mid fight is not a separate, cut down view.
        /// </summary>
        public void OpenCharacterMenu()
        {
            if (CombatOver) return;

            GameSession session = GameSession.Instance;
            session.ReturnScene = GameSession.CombatScene;
            session.LoadScene(GameSession.CharacterScene);
        }

        public void StartCombat()
        {
            GameSession session = GameSession.Instance;

            EnemyData wanted = enemyOverride != null ? enemyOverride : session.CurrentEnemy;
            if (wanted == null) wanted = session.RandomEnemy();
            if (wanted == null)
            {
                Debug.LogError("[CombatManager] No EnemyData available. Assign an enemy override or add enemies to the GameDatabase.");
                return;
            }

            session.CurrentEnemy = wanted;
            Enemy = wanted;

            // A fight that is still live is the one to carry on with. This is the case that matters after
            // a trip to the character menu: same enemy, unfinished, so nothing is rerolled.
            CombatState existing = session.Combat;
            if (existing != null && existing.started && !existing.finished && existing.enemy == wanted)
            {
                state = existing;
                WireButtons();

                // The enemy's delay is real time the player can walk out through, and the coroutine that
                // would have spent it died with the last scene. The turn is run again from the top rather
                // than being lost, which is safe because the flag only stays set until the attack lands.
                if (!CombatOver && state.enemyTurnPending) StartCoroutine(EnemyTurnRoutine());

                RefreshUI();
                return;
            }

            state = session.BeginCombat();
            state.enemy = wanted;
            state.enemyHealth = wanted.maxHealth;
            state.enemyBlock = wanted.startingBlock;
            state.enemyTurnIndex = 0;
            state.cardsPlayedThisTurn = 0;
            state.cardsPlayedLastTurn = 0;
            state.freeActionUsed = false;
            state.enemyAttackReduction = 0;
            state.enemyAttackNullified = false;
            state.upkeepDamage = 0;
            state.discardEachTurn = false;
            state.pickPending.Clear();
            state.pickStage = PendingPick.None;
            state.pickSource = null;
            state.nextAttackBonus = 0;
            state.nextAttackBonusSource = null;

            Stats.StartCombat();

            state.drawPile.AddRange(session.Decks[session.ActiveDeckIndex].CreateShuffledPile());

            state.combatOver = false;
            state.started = true;

            if (state.drawPile.Count == 0)
            {
                Log("Your deck is empty.");
                OutOfCards();
                return;
            }

            if (defeatPanel != null) defeatPanel.SetActive(false);

            WireButtons();

            Log("A " + Enemy.enemyName + " blocks your path.");
            BeginPlayerTurn(true);

            if (CombatOver) return;

            // Wider than a turn: Broken Crown reads the attributes the rest of the fight is fought with, so
            // it is settled once the fight is under way and again whenever a card moves one of them.
            RefreshBrokenCrown(true);

            // After the turn, because both of these are about the hand: a Goblet claims cards out of the one
            // that was just dealt, and there is nothing to claim before there is a hand.
            ApplyCombatStartRelics();
            RefreshUI();
        }

        /// <summary>
        /// Reconnects the scene's buttons. A resumed fight is running in a scene that was only just built,
        /// so its buttons carry no listeners yet, exactly as they did not on the first load.
        /// </summary>
        void WireButtons()
        {
            if (returnToMenuButton != null)
            {
                returnToMenuButton.onClick.RemoveAllListeners();
                returnToMenuButton.onClick.AddListener(ReturnToMenu);
            }
            if (endTurnButton != null)
            {
                endTurnButton.onClick.RemoveAllListeners();
                endTurnButton.onClick.AddListener(EndPlayerTurn);
            }
        }

        // ---------------------------------------------------------------- playing cards

        /// <summary>
        /// What the card costs to play right now, which is not always what is printed on it: a relic can
        /// move the price of one particular card, or of one position in the turn.
        ///
        /// Everything that pays for a card reads the price from here, so a discount is visible on the card
        /// itself, is checked by the affordability test, and is what actually leaves the energy pool.
        /// </summary>
        public int CostOf(CardData card)
        {
            if (card == null) return 0;

            int cost = card.cost;

            if (state != null)
            {
                foreach (CardCostModifier modifier in state.costModifiers)
                {
                    if (modifier.card == card) cost += modifier.delta;
                }

                // Duelist's Ribbon names the position in the turn it discounts, and the count is already
                // standing at one less than that while the card is being paid for.
                RelicDefinition ribbon = RelicEffects.Definition(RelicEffect.DuelistsRibbon);
                if (ribbon != null && state.cardsPlayedThisTurn == Mathf.Max(1, ribbon.threshold) - 1)
                {
                    cost -= Mathf.Max(1, ribbon.scaling.ResolveInt(Stats));
                }
            }

            return Mathf.Max(0, cost);
        }

        /// <summary>Hangs a one-off discount on a card. The play it paid for is what spends it.</summary>
        void AddCostModifier(CardData card, int delta)
        {
            if (card == null || delta == 0 || state == null) return;
            state.costModifiers.Add(new CardCostModifier { card = card, delta = delta });
        }

        /// <summary>Spends one discount on a card, which is what keeps it to a single play.</summary>
        void ConsumeCostModifier(CardData card)
        {
            if (card == null || state == null) return;

            for (int i = 0; i < state.costModifiers.Count; i++)
            {
                if (state.costModifiers[i].card != card) continue;
                state.costModifiers.RemoveAt(i);
                return;
            }
        }

        /// <summary>True when the player can pay the card's cost right now, in the right resource.</summary>
        public bool CanAfford(CardData card)
        {
            if (card == null) return false;
            if (card.costType == CardCostType.Mana) return Stats.CanAffordMana(CostOf(card));
            return PlayerEnergy >= CostOf(card);
        }

        /// <summary>
        /// Says out loud why a card cannot be paid for, in the resource the card actually costs. An
        /// unaffordable card has its button switched off, so the click never reaches TryPlayCard and this
        /// is the only thing that tells the player mana is what the number on the card was asking for.
        /// </summary>
        public void ReportUnaffordable(CardData card)
        {
            if (card == null || CanAfford(card)) return;

            bool mana = card.costType == CardCostType.Mana;
            int held = mana ? Stats.Mana : PlayerEnergy;
            Log("Not enough " + (mana ? "mana" : "energy") + " for " + card.cardName
                + " (costs " + CostOf(card) + " " + (mana ? "mana" : "energy") + ", you have " + held + ").");
            RefreshUI();
        }

        /// <summary>
        /// The decision the fight is waiting on, if a card has opened one. While this is anything other than
        /// None, the cards the pick turned up are the only thing that can be clicked.
        /// </summary>
        public PendingPick ActivePick { get { return state != null ? state.pickStage : PendingPick.None; } }

        public bool PickActive { get { return ActivePick != PendingPick.None; } }

        /// <summary>The card that opened the standing pick, so the pick's own lines are attributed to it.</summary>
        string PickName
        {
            get { return state != null && !string.IsNullOrEmpty(state.pickSource) ? state.pickSource : "The pick"; }
        }

        /// <summary>
        /// True when the fight will take a card from the player: their turn, no pick waiting on a click, and
        /// no outcome already reached. Every path that plays a card or ends the turn goes through this, so a
        /// pick cannot be stepped over.
        /// </summary>
        public bool AcceptsPlays
        {
            get { return state != null && !CombatOver && IsPlayerTurn && !PickActive; }
        }

        /// <summary>What the pick is asking for. Empty when nothing is being asked.</summary>
        string PickPrompt()
        {
            switch (ActivePick)
            {
                case PendingPick.KeepToHand: return "Choose a card to keep in hand";
                case PendingPick.Reshuffle: return "Choose a card to shuffle back";
                case PendingPick.DrawOneDiscardRest: return "Choose a card to draw into your hand";
                case PendingPick.KeepGeneratedOne: return "Choose a card to keep";
                default: return "";
            }
        }

        /// <summary>
        /// The fight state a card's number can be read from. A card whose wording is about the fight rather
        /// than about the player reads its number out of this: how full the hand is, how many cards have
        /// gone this turn, and how much shielding is standing in the way.
        /// </summary>
        public CardContext Context()
        {
            var context = new CardContext();
            context.cardsInHand = hand.Count;
            context.cardsPlayedThisTurn = state != null ? state.cardsPlayedThisTurn : 0;
            context.enemyBlock = EnemyBlock;
            return context;
        }

        /// <summary>What the card would do if played now. A Ripost reads your current shielding.</summary>
        public int PreviewValue(CardData card)
        {
            if (card == null) return 0;
            if (card.effect == CardEffect.DamageEqualToBlock) return Stats.Block;
            return card.ValueFor(Stats, Context());
        }

        /// <summary>Spends the card's cost and resolves the card at the given position in the hand.</summary>
        public bool TryPlayCard(int handIndex)
        {
            if (!AcceptsPlays) return false;
            if (handIndex < 0 || handIndex >= hand.Count) return false;

            CardData card = hand[handIndex];
            if (card == null) return false;

            // A free action is taken on top of the turn rather than paid for out of it, and there is one of
            // them a turn. That is the whole of Split Second: not a discount, an extra slot.
            if (card.freeAction && state != null && state.freeActionUsed)
            {
                Log("You have already taken your free action this turn.");
                RefreshUI();
                return false;
            }

            if (!CanAfford(card))
            {
                ReportUnaffordable(card);
                return false;
            }

            int cost = CostOf(card);
            if (card.costType == CardCostType.Mana) Stats.SpendMana(cost);
            else PlayerEnergy -= cost;

            // The discount belonged to this play rather than to the card, so it is spent here.
            ConsumeCostModifier(card);

            if (card.freeAction && state != null) state.freeActionUsed = true;

            hand.RemoveAt(handIndex);

            // A construct stays in play and keeps firing, a card that comes back is put straight into the
            // hand again so it can be played this turn, and everything else is spent for the encounter.
            if (card.persistent) board.Add(new BoardCard { card = card, turnsLeft = TurnsOnBoardFor(card) });
            else if (card.returnsToHand) hand.Add(card);
            else Spent(card);

            // Drawn straight away rather than waiting for the next refresh: a construct appearing is the
            // moment the board changed, and the square filling in is what says it landed.
            if (card.persistent) RefreshBoardDisplay();
            // Counted before the card resolves, because Chain Reaction and Backstab both count the card
            // being played as one of the turn's cards.
            if (state != null) state.cardsPlayedThisTurn++;

            ResolveEffect(card, false);

            // Titan's Grip: the first Attack of the turn resolves a second time, and the whole effect does
            // rather than a second damage number being added to it, so everything the attack was carrying
            // lands again. Checked after the swing so a card that ended the fight is not swung again.
            RelicDefinition grip = RelicEffects.Definition(RelicEffect.TitansGrip);
            if (grip != null && card.cardType == CardType.Attack && !CombatOver && EnemyHealth > 0 &&
                ClaimOnceThisTurn(RelicEffect.TitansGrip))
            {
                Log("Titan's Grip swings " + card.cardName + " a second time.");
                ResolveEffect(card, false);
            }

            // Mirage Engine: the card at the named position in the turn conjures a free one, which is what
            // lets an Agility turn keep paying for itself.
            RelicDefinition mirage = RelicEffects.Definition(RelicEffect.MirageEngine);
            if (mirage != null && state != null && !CombatOver &&
                state.cardsPlayedThisTurn == Mathf.Max(1, mirage.threshold))
            {
                Log("Mirage Engine conjures a card. (" + state.cardsPlayedThisTurn + " played this turn)");
                GenerateRandomCard(true);
            }

            if (card.appliesStatus != StatusEffectType.None && card.statusMagnitude != 0)
            {
                int duration = card.statusDuration > 0 ? card.statusDuration : -1;
                if (card.statusTargetsSelf)
                {
                    Stats.AddStatus(card.appliesStatus, card.statusMagnitude, duration);
                    Log(card.cardName + ": you gain " + card.appliesStatus + " " + card.statusMagnitude + ".");
                }
                else
                {
                    StatusEffects.Add(enemyStatuses, card.appliesStatus, card.statusMagnitude, duration);
                    Log(card.cardName + ": " + Enemy.enemyName + " gains " + card.appliesStatus + " " + card.statusMagnitude + ".");
                }
            }

            // The run-long price is settled after the effect, so a card that kills with its damage still
            // lands before anything else happens.
            ApplyPermanentChange(card);

            // Reckless Stance is banked rather than spent: the next attack card played collects it, whether
            // it was played before this one or after it.
            if (state != null && card.effect == CardEffect.NextAttackBonus)
            {
                int promised = card.ValueFor(Stats, Context());
                if (promised <= 0)
                {
                    Log(card.cardName + " promises nothing: you have no Strength to spend on it.");
                }
                else
                {
                    state.nextAttackBonus += promised;
                    state.nextAttackBonusSource = card.cardName;
                    Log(card.cardName + " promises your next attack " + promised + " extra damage.");
                }
            }

            if (state != null)
            {
                if (card.selfDamagePerTurn > 0) state.upkeepDamage += card.selfDamagePerTurn;
                if (card.discardRandomEachTurn) state.discardEachTurn = true;
            }

            // The constructs that watch cards being played are told here, once, rather than each of them being
            // wired into the play path: an Adaptive Core counts the archetype, a Prototype counts the cost.
            NotifyCardPlayed(card, cost);

            // The constructs that wait for a free card go last, so they read a turn that has already moved.
            FireConstructs(cost == 0);

            RefreshUI();
            CheckEnemyDefeated();
            return true;
        }

        /// <summary>
        /// Runs a card's effect. The same path serves a card just played and a board card firing on its
        /// own, which is what makes a persistent card behave identically every turn.
        /// </summary>
        void ResolveEffect(CardData card, bool fromBoard)
        {
            string prefix = fromBoard ? card.cardName + " : " : "";
            CardContext context = Context();

            switch (card.effect)
            {
                case CardEffect.Damage:
                {
                    int value = card.ValueFor(Stats, context);
                    int hits = card.HitsFor(Stats, context);

                    // Whetstone: the first attack of the fight hits harder. It is added before the repeats
                    // so a card that swings twice takes it on both, the way a printed number would be.
                    RelicDefinition whetstone = RelicEffects.Definition(RelicEffect.Whetstone);
                    if (whetstone != null && card.cardType == CardType.Attack && EnemyHealth > 0 &&
                        ClaimOnceThisCombat(RelicEffect.Whetstone))
                    {
                        int bonus = Mathf.Max(1, whetstone.scaling.ResolveInt(Stats));
                        value += bonus;
                        Log(prefix + "Whetstone adds " + bonus + " damage to " + card.cardName + ".");
                    }

                    // Backstab is paid double once the turn is deep enough, counting this card as one of
                    // the cards played.
                    if (card.doubleIfCardsPlayed > 0 && state != null &&
                        state.cardsPlayedThisTurn >= card.doubleIfCardsPlayed)
                    {
                        value *= 2;
                        Log(prefix + card.cardName + " catches " + Enemy.enemyName + " off guard.");
                    }

                    int dealt = 0;
                    for (int i = 0; i < hits; i++)
                    {
                        if (EnemyHealth <= 0) break;
                        dealt += DealDamageToEnemy(value);
                    }

                    string repeat = hits > 1 ? " in " + hits + " hits, " + dealt + " getting through" : "";
                    Log(prefix + card.cardName + " deals " + value + " damage to " + Enemy.enemyName + repeat + ".");

                    // Reckless Stance is collected by the first card that swings, as one more blow: a card
                    // that hits several times still only takes it once.
                    string promisedBy;
                    int promised = TakeNextAttackBonus(out promisedBy);
                    if (promised > 0 && EnemyHealth > 0)
                    {
                        dealt += DealDamageToEnemy(promised);
                        Log(promisedBy + " adds " + promised + " damage to the attack.");
                    }

                    // Glory's rider. Paid here rather than at the end of the fight, because the wording is
                    // about this card being the one that finished it.
                    if (EnemyHealth <= 0) AwardKillGold(card);
                    break;
                }

                case CardEffect.Block:
                {
                    int value = card.ValueFor(Stats, context);
                    Stats.Block += value;
                    Log(prefix + card.cardName + " grants " + value + " shielding. (Total " + Stats.Block + ")");
                    break;
                }

                case CardEffect.Draw:
                {
                    int value = card.ValueFor(Stats, context);

                    // Overdraw is the one card that reads this way round: with Agility driven negative it
                    // hands back cards instead of finding more, mirroring what negative Strength does.
                    if (value >= 0)
                    {
                        Log(prefix + card.cardName + " draws " + value + (value == 1 ? " card." : " cards."));
                        DrawCards(value);
                    }
                    else
                    {
                        int thrown = Mathf.Min(-value, hand.Count);
                        for (int i = 0; i < thrown; i++)
                        {
                            int index = UnityEngine.Random.Range(0, hand.Count);
                            CardData gone = hand[index];
                            hand.RemoveAt(index);
                            DiscardCard(gone, card.cardName + " throws it away.");
                        }
                        Log(prefix + card.cardName + " throws away " + thrown + " cards instead.");
                    }
                    break;
                }

                case CardEffect.GainEnergy:
                {
                    int value = card.ValueFor(Stats, context);
                    PlayerEnergy += value;
                    Log(prefix + card.cardName + " grants " + value + " energy. (Total " + PlayerEnergy + ")");
                    break;
                }

                case CardEffect.GainMana:
                {
                    int value = card.ValueFor(Stats, context);
                    GainMana(value, prefix + card.cardName + " grants " + value + " mana");
                    break;
                }

                case CardEffect.TemporaryStrength:
                {
                    // Fight or Flight reads its number off the hand, so it is worth playing while the hand
                    // is still full rather than after it has been spent.
                    int value = card.ValueFor(Stats, context);
                    Stats.AddStatus(StatusEffectType.Strength, value, 1);
                    Log(prefix + card.cardName + " grants " + value + " Strength until the end of the turn.");
                    break;
                }

                case CardEffect.DamageEqualToBlock:
                {
                    int value = Stats.Block;
                    DealDamageToEnemy(value);
                    Log(prefix + card.cardName + " returns your " + value + " shielding as damage to " + Enemy.enemyName + ".");
                    if (EnemyHealth <= 0) AwardKillGold(card);
                    break;
                }

                case CardEffect.Heal:
                {
                    int value = card.ValueFor(Stats, context);
                    Stats.Heal(value);
                    Log(prefix + card.cardName + " restores " + value + " health.");
                    break;
                }

                case CardEffect.DamageToBlock:
                {
                    // Sunder is the answer to an enemy that turtles: it takes shielding off and does
                    // nothing else, so it is worthless against a target that is not blocking.
                    int value = card.ValueFor(Stats, context);
                    int stripped = Mathf.Min(EnemyBlock, Mathf.Max(0, value));
                    EnemyBlock -= stripped;
                    Log(prefix + card.cardName + " strips " + stripped + " shielding off " + Enemy.enemyName +
                        ". (" + EnemyBlock + " left)");
                    break;
                }

                case CardEffect.ReduceEnemyAttack:
                {
                    if (state != null) state.enemyAttackNullified = true;
                    Log(prefix + card.cardName + " leaves " + Enemy.enemyName + " with nothing to swing.");
                    break;
                }

                case CardEffect.StealStrength:
                {
                    // An enemy has no attributes to take from, so what is taken off is its attack for the
                    // rest of the fight. The player keeps it, which is what makes this a Strength card.
                    int value = card.ValueFor(Stats, context);
                    Stats.AddStatus(StatusEffectType.Strength, value, -1);
                    if (state != null) state.enemyAttackReduction += value;
                    Log(prefix + card.cardName + " steals " + value + " Strength from " + Enemy.enemyName + ".");
                    break;
                }

                case CardEffect.PruneDrawPile:
                {
                    // Jungle Might: the cards that pay for it are the ones that do not scale with Strength,
                    // so putting conjured cards into the pile makes this worth more.
                    int removed = 0;
                    for (int i = drawPile.Count - 1; i >= 0; i--)
                    {
                        CardData held = drawPile[i];
                        if (held == null) continue;

                        // Anything that does not scale with Strength is what pays for this card, so those are
                        // the ones thrown away. A Strength card left in the pile is one this card declines to
                        // spend, which is what keeps it from eating the deck it is meant to feed.
                        if (held.scaling == CardStatScaling.Strength && held.valuePerStatPoint != 0f) continue;

                        // Thrown on the discard pile rather than erased, so the cards are spent rather than
                        // deleted and the two pile counts stay honest against the deck.
                        drawPile.RemoveAt(i);
                        DiscardCard(held, card.cardName + " throws it away.");
                        removed++;
                    }

                    // Duration -1 is the rest of the fight, which is what "this combat" means everywhere else.
                    Stats.AddStatus(StatusEffectType.Strength, removed, -1);
                    Log(prefix + card.cardName + " discards " + removed + " cards and gains " + removed + " Strength.");
                    break;
                }

                case CardEffect.DiscardHand:
                {
                    int thrown = hand.Count;
                    int energy = thrown * card.energyPerCardDiscarded;
                    int conjured = thrown * card.generatedPerCardDiscarded;

                    foreach (CardData gone in hand) DiscardCard(gone, card.cardName + " throws it away.");
                    hand.Clear();

                    if (energy > 0) PlayerEnergy += energy;

                    string tail = energy > 0 ? ", gaining " + energy + " energy" : "";
                    Log(prefix + card.cardName + " throws away " + thrown + " cards" + tail + ".");

                    for (int i = 0; i < conjured; i++) GenerateRandomCard();
                    break;
                }

                case CardEffect.DiscardOne:
                {
                    // The card thrown away is chosen for the player. Picking one would need a menu on top of
                    // the fight, which costs more attention than the card is worth.
                    if (hand.Count > 0)
                    {
                        int index = UnityEngine.Random.Range(0, hand.Count);
                        CardData gone = hand[index];
                        hand.RemoveAt(index);
                        DiscardCard(gone, card.cardName + " throws away " + gone.cardName + ".");
                        DrawCards(1);
                    }
                    break;
                }

                case CardEffect.GenerateCard:
                {
                    int count = Mathf.Max(1, card.ValueFor(Stats, context));
                    for (int i = 0; i < count; i++) GenerateRandomCard();
                    break;
                }

                case CardEffect.LookAtTopCards:
                case CardEffect.LookAtTopCardsKeepOne:
                {
                    // The count is the card's own number, so a card that scales it turns up more without
                    // this knowing anything about it. Palm Trick asks a second question about the cards it
                    // did not take; the Tribunal's reveal is answered and over in the one click.
                    int count = Mathf.Max(1, card.ValueFor(Stats, context));
                    BeginPick(card, count, card.effect == CardEffect.LookAtTopCards
                        ? PendingPick.KeepToHand
                        : PendingPick.DrawOneDiscardRest);
                    break;
                }

                case CardEffect.NextAttackBonus:
                {
                    // Nothing to resolve: TryPlayCard banks the number for the next card that swings, and
                    // this path exists so the card does not fall through the switch and look like it did
                    // nothing.
                    break;
                }

                case CardEffect.StatusOnly:
                {
                    // Nothing to resolve: TryPlayCard applies the status itself, and this path exists so the
                    // card does not fall through the switch and look like it did nothing.
                    break;
                }
            }

            // Riders on top of the effect, which is how a damage card also draws a card and a construct
            // hands over energy on the same beat.
            if (card.drawsCards > 0) DrawCards(card.drawsCards);
            if (card.energyGain > 0)
            {
                PlayerEnergy += card.energyGain;
                Log(prefix + card.cardName + " grants " + card.energyGain + " energy. (Total " + PlayerEnergy + ")");
            }
        }

        /// <summary>Applies one hit, returning how much of it got through to the enemy's health.</summary>
        int DealDamageToEnemy(int value)
        {
            int remaining = Mathf.Max(0, value);
            int healthBefore = EnemyHealth;

            if (EnemyBlock > 0)
            {
                int blocked = Mathf.Min(EnemyBlock, remaining);
                EnemyBlock -= blocked;
                remaining -= blocked;
                Log(Enemy.enemyName + " blocks " + blocked + " damage.");
            }

            EnemyHealth = Mathf.Max(0, EnemyHealth - remaining);
            return healthBefore - EnemyHealth;
        }

        // ---------------------------------------------------------------- relics

        /// <summary>
        /// Claims a relic's once-a-combat trigger. True the first time and false every time after, so a
        /// caller reads as "if this has not fired yet, fire it" and keeps no flag of its own.
        /// </summary>
        bool ClaimOnceThisCombat(RelicEffect effect)
        {
            return state != null && state.relicFiredThisCombat.Add((int)effect);
        }

        /// <summary>The same, for the triggers the turn boundary hands back.</summary>
        bool ClaimOnceThisTurn(RelicEffect effect)
        {
            return state != null && state.relicFiredThisTurn.Add((int)effect);
        }

        /// <summary>A card spent because it was played or ran its course. Not a discard.</summary>
        void Spent(CardData card)
        {
            if (card != null)
            {
                if (card.purge)
                {
                    // Purge: remove card from game entirely
                    state.removedPile.Add(card);
                }
                else
                {
                    // Normal spent: goes to discard pile
                    discardPile.Add(card);
                }
            }
        }

        /// <summary>
        /// A card thrown away out of the hand, which is the event the discard relics are paid on.
        ///
        /// A card spent by playing it is deliberately not a discard: if it were, the first card played every
        /// fight would hand over a Traveler's Coin, and three cards played would pay out Scrap Satchel.
        /// Neither relic says that.
        /// </summary>
        void DiscardCard(CardData card, string message)
        {
            if (card == null) return;

            discardPile.Add(card);
            if (state != null) state.discardsThisTurn++;

            if (!string.IsNullOrEmpty(message)) Log(message);

            RelicDefinition coin = RelicEffects.Definition(RelicEffect.TravelersCoin);
            if (coin != null && ClaimOnceThisCombat(RelicEffect.TravelersCoin))
            {
                int gold = Mathf.Max(1, coin.scaling.ResolveInt(Stats));
                GameSession.Instance.AddGold(gold);
                Log("Traveler's Coin pays out " + gold + " gold.");
            }

            // Scrap Satchel pays at every multiple of its count rather than once a turn: the relic is named
            // for how much you throw away, so throwing more away has to be worth more.
            RelicDefinition satchel = RelicEffects.Definition(RelicEffect.ScrapSatchel);
            if (satchel == null || state == null) return;

            int step = Mathf.Max(1, satchel.threshold);
            if (state.discardsThisTurn % step != 0) return;

            int mana = Mathf.Max(1, satchel.scaling.ResolveInt(Stats));
            GainMana(mana, "Scrap Satchel pays out " + mana + " mana (" + state.discardsThisTurn +
                           " discarded this turn)");
        }

        /// <summary>
        /// Mana arriving, with Bent Lens listening. Everything that hands mana over comes through here, so
        /// the relic hangs off the mana itself rather than off whichever card happens to grant it.
        /// </summary>
        void GainMana(int amount, string message)
        {
            if (amount == 0) return;

            Stats.AddMana(amount);
            Log(message + ". (Total " + Stats.Mana + ")");

            if (amount <= 0) return;

            RelicDefinition lens = RelicEffects.Definition(RelicEffect.BentLens);
            if (lens == null || !ClaimOnceThisCombat(RelicEffect.BentLens)) return;

            int block = Mathf.Max(1, lens.scaling.ResolveInt(Stats));
            Stats.Block += block;
            Log("Bent Lens turns the first mana into " + block + " shielding. (Total " + Stats.Block + ")");
        }

        /// <summary>
        /// Health coming off the player, with the relics that watch for it listening.
        ///
        /// Every loss goes through here however it was caused, so nothing can take health off the player by
        /// a route that skips the relics that were bought to notice.
        /// </summary>
        void LoseHealth(int amount, string message)
        {
            if (amount <= 0) return;

            int before = Stats.Health;
            Stats.TakeDamage(amount);
            int lost = before - Stats.Health;

            if (!string.IsNullOrEmpty(message)) Log(message);
            if (lost <= 0) return;

            if (state != null) state.hpLostThisTurn += lost;

            RelicDefinition thread = RelicEffects.Definition(RelicEffect.BloodstainedThread);
            if (thread != null && ClaimOnceThisCombat(RelicEffect.BloodstainedThread))
            {
                int cards = Mathf.Max(1, thread.scaling.ResolveInt(Stats));
                Log("Bloodstained Thread draws you " + cards + (cards == 1 ? " card." : " cards."));
                DrawCards(cards);
            }

            RelicDefinition sigil = RelicEffects.Definition(RelicEffect.BloodSigil);
            if (sigil != null && state != null && state.hpLostThisTurn >= Mathf.Max(1, sigil.threshold) &&
                ClaimOnceThisCombat(RelicEffect.BloodSigil))
            {
                int sigilEnergy = Mathf.Max(1, sigil.scaling.ResolveInt(Stats));
                PlayerEnergy += sigilEnergy;
                Log("Blood Sigil pays out " + sigilEnergy + " energy for the " + state.hpLostThisTurn +
                    " health taken this turn.");
            }

            RelicDefinition crown = RelicEffects.Definition(RelicEffect.BloodCrown);
            if (crown == null || Stats.Health <= 0) return;
            if (Stats.Health * 2 >= Stats.MaxHealth) return;
            if (!ClaimOnceThisCombat(RelicEffect.BloodCrown)) return;

            int energy = Mathf.Max(1, crown.scaling.ResolveInt(Stats));
            int attribute = Mathf.Max(1, crown.secondaryMagnitude);
            PlayerEnergy += energy;
            Stats.AddCombatStatBonus(crown.scaling.stat, attribute);
            Log("Blood Crown: you gain " + energy + " energy and " + attribute + " " + crown.scaling.stat +
                " for the rest of the fight.");
        }

        /// <summary>
        /// Goblet: the designated attribute is bought a point at a time, at the top of every turn. It is a
        /// combat stat rather than a status effect, because the artifact names the attribute while the
        /// status set only has kinds written for the cards that came before it.
        /// </summary>
        void GrantGoblet()
        {
            RelicDefinition goblet = RelicEffects.Definition(RelicEffect.Goblet);
            if (goblet == null) return;

            int amount = Mathf.Max(1, goblet.scaling.ResolveInt(Stats));
            Stats.AddCombatStatBonus(goblet.scaling.stat, amount);
            Log(goblet.itemName + " grants " + amount + " " + goblet.scaling.stat + " this combat. (Total " +
                Stats.GetStat(goblet.scaling.stat) + ")");
        }

        /// <summary>
        /// What a Goblet costs to pick up: cards out of the hand the fight just dealt it. It runs after the
        /// opening hand is dealt, because there is nothing to claim before there is a hand.
        /// </summary>
        void ApplyCombatStartRelics()
        {
            RelicDefinition goblet = RelicEffects.Definition(RelicEffect.Goblet);
            if (goblet == null) return;

            int claimed = 0;
            int count = Mathf.Max(0, goblet.threshold);
            if (count <= 0) return;

            for (int i = 0; i < count && hand.Count > 0; i++)
            {
                int index = UnityEngine.Random.Range(0, hand.Count);
                CardData gone = hand[index];
                hand.RemoveAt(index);
                DiscardCard(gone, null);
                claimed++;
            }

            if (claimed > 0) Log(goblet.itemName + " claims " + claimed + " cards from your opening hand.");
        }

        /// <summary>
        /// Broken Crown: whichever attribute is lowest counts higher to every card formula.
        ///
        /// Rebuilt rather than adjusted, because which attribute is lowest moves as the relics, the roll and
        /// the fight's own permanent changes move the others around it.
        /// </summary>
        void RefreshBrokenCrown(bool announce)
        {
            RelicDefinition crown = RelicEffects.Definition(RelicEffect.BrokenCrown);

            if (crown == null)
            {
                Stats.SetEffectStatBonuses(null);
                return;
            }

            int amount = Mathf.Max(1, crown.scaling.ResolveInt(Stats));
            StatType lowest = Stats.LowestStat();

            var bonuses = new Dictionary<StatType, int>();
            bonuses[lowest] = amount;
            Stats.SetEffectStatBonuses(bonuses);

            if (announce)
            {
                Log("Broken Crown: your " + lowest + " is the lowest of your attributes, and counts as " +
                    amount + " higher while a card is being read.");
            }
        }

        /// <summary>
        /// Gives up an ongoing card by the player's own hand rather than letting it run its course.
        ///
        /// Only a construct whose card says it is sacrificial can be given up at all, and the card is asked
        /// rather than a list of kinds: a hinge construct whose payoff is the moment it leaves is a family
        /// this pool is being built towards, so the answer has to belong to the construct. The board only
        /// makes a square clickable when the same flag is set, so this is the second half of one decision
        /// rather than a second decision.
        ///
        /// It is also what pays Reaper's Ledger, which is the relic written against the moment.
        /// </summary>
        public bool SacrificeBoardCard(int index)
        {
            if (state == null || index < 0 || index >= board.Count) return false;

            BoardCard entry = board[index];
            if (entry == null || entry.card == null) return false;
            if (!entry.card.sacrificable) return false;

            // A construct from the neutral pool is asked before it goes: what Fuse does when it is given up is
            // its own detonation, and Forbidden Engine's whole payout is this moment. Asked while the card is
            // still standing, so neither is read off a construct that has already been forgotten.
            if (entry.card.construct != ConstructKind.None && !CombatOver)
            {
                TriggerConstruct(entry, ConstructTiming.Sacrifice);
            }

            board.RemoveAt(index);
            Log(entry.card.cardName + " is sacrificed.");
            Spent(entry.card);

            RelicDefinition ledger = RelicEffects.Definition(RelicEffect.ReapersLedger);
            if (ledger != null)
            {
                int energy = Mathf.Max(1, ledger.scaling.ResolveInt(Stats));
                int cards = Mathf.Max(1, ledger.secondaryMagnitude);
                PlayerEnergy += energy;
                Log("Reaper's Ledger pays out " + energy + " energy.");
                DrawCards(cards);
            }

            RefreshBoardDisplay();
            RefreshUI();
            return true;
        }

        // ---------------------------------------------------------------- the neutral construct pool

        /// <summary>
        /// How deep a run of constructs waking each other has gone this instant.
        ///
        /// A Relay Node can wake a construct that wakes the Relay Node again, and that is the mechanic rather
        /// than a mistake, so this is a crash guard and not a rule. It is set well above what the pool can
        /// reach honestly, and hitting it warns rather than failing.
        /// </summary>
        int constructChainDepth;

        /// <summary>
        /// Wakes one construct at one of its timings, and tells the board that it did.
        ///
        /// Every construct in the pool goes through here whatever woke it, which is what makes "whenever
        /// another Construct triggers" a question the board can answer: the notification is sent once, from
        /// here, rather than being something each construct has to remember to do.
        ///
        /// Returns false only when the fight has ended underneath it, so a caller walking the board stops
        /// rather than resolving the rest of it into a defeat.
        /// </summary>
        bool TriggerConstruct(BoardCard entry, ConstructTiming timing, CardData triggeredBy = null, int paidCost = 0)
        {
            if (entry == null || entry.card == null) return true;
            if (CombatOver) return false;

            ConstructKind kind = entry.card.construct;
            if (kind == ConstructKind.None) return true;

            // A reveal has the fight's attention, so nothing behind it acts.
            if (PickActive && timing == ConstructTiming.StartOfTurn) return true;

            string prefix = entry.card.cardName + ": ";
            bool fired = false;

            switch (kind)
            {
                case ConstructKind.DormantEngine:
                    if (timing == ConstructTiming.StartOfTurn) fired = DormantEnginePulse(entry, prefix);
                    break;

                case ConstructKind.RelayNode:
                    // It only ever answers something else, so it has no timing of its own to be woken at.
                    if (timing == ConstructTiming.OtherTriggered) fired = RelayNodeCharge(entry, prefix);
                    break;

                case ConstructKind.Fuse:
                    if (timing == ConstructTiming.StartOfTurn) fired = FusePulse(entry, prefix);
                    else if (timing == ConstructTiming.Expire || timing == ConstructTiming.Sacrifice)
                        fired = PayFinalTrigger(entry, prefix);
                    break;

                case ConstructKind.OverheatedCore:
                    if (timing == ConstructTiming.StartOfTurn) fired = OverheatedCorePulse(entry, prefix);
                    break;

                case ConstructKind.ReclamationEngine:
                    if (timing == ConstructTiming.StartOfTurn) fired = ReclamationEnginePulse(entry, prefix);
                    break;

                case ConstructKind.TemporalAnchor:
                    if (timing == ConstructTiming.StartOfTurn) fired = TemporalAnchorPulse(entry, prefix);
                    break;

                case ConstructKind.OverflowingArchive:
                    if (timing == ConstructTiming.EndOfTurn) fired = OverflowingArchivePulse(entry, prefix);
                    break;

                case ConstructKind.Archive:
                    if (timing == ConstructTiming.StartOfTurn) fired = ArchivePulse(entry, prefix);
                    break;

                case ConstructKind.PerfectAlignment:
                    if (timing == ConstructTiming.StartOfTurn) fired = PerfectAlignmentPulse(entry, prefix);
                    break;

                case ConstructKind.AdaptiveCore:
                    if (timing == ConstructTiming.CardPlayed) fired = AdaptiveCoreSees(entry, prefix, triggeredBy);
                    break;

                case ConstructKind.Prototype:
                    if (timing == ConstructTiming.CardPlayed)
                    {
                        // The cost that counts is what was actually paid for the card, so a card a relic made
                        // free is not a card that charged the Prototype.
                        if (triggeredBy != null && triggeredBy.costType == CardCostType.Energy &&
                            paidCost >= ConstructRules.PrototypeEnergyCostThreshold)
                        {
                            fired = PrototypeCharge(entry, prefix);
                        }
                    }
                    else if (timing == ConstructTiming.Expire || timing == ConstructTiming.Sacrifice)
                    {
                        fired = PayFinalTrigger(entry, prefix);
                    }
                    break;

                case ConstructKind.ForbiddenEngine:
                    if (timing == ConstructTiming.StartOfTurn) fired = ForbiddenEnginePulse(entry, prefix);
                    else if (timing == ConstructTiming.Sacrifice) fired = PayFinalTrigger(entry, prefix);
                    break;

                case ConstructKind.SuccessorProtocol:
                    // It never acts on its own: it copies what another construct did as that one leaves, which
                    // NotifyConstructTriggered drives above the switch.
                    break;

                case ConstructKind.RouletteCore:
                    if (timing == ConstructTiming.StartOfTurn) fired = RouletteCorePulse(entry, prefix);
                    break;
            }

            if (CombatOver) return false;
            if (!aliveForConstructs) return false;

            // Told after the construct has done its thing, so what is answered is a construct that acted
            // rather than one that was merely asked.
            if (fired) NotifyConstructTriggered(entry, timing);

            return !CombatOver && aliveForConstructs;
        }

        /// <summary>True while the player is still standing, and ends the fight itself when they are not.</summary>
        bool aliveForConstructs
        {
            get
            {
                if (Stats == null || Stats.IsAlive) return true;

                Defeat();
                return false;
            }
        }

        /// <summary>
        /// Tells the board that a construct did something, which is what the two constructs that answer other
        /// constructs are built on.
        ///
        /// Both readings live here rather than on the constructs they answer: Relay Node hears every kind of
        /// trigger, Successor Protocol hears only a construct leaving. Wiring either of them into the cards it
        /// reacts to would mean every new construct had to know about the relics-like cards that watch it.
        /// </summary>
        void NotifyConstructTriggered(BoardCard source, ConstructTiming timing)
        {
            if (state == null || source == null || source.card == null) return;

            if (constructChainDepth >= ConstructRules.MaxChainDepth)
            {
                Debug.LogWarning("[Construct] The construct chain reached its depth guard (" +
                                 ConstructRules.MaxChainDepth + ") on " + source.card.cardName +
                                 ". This is a crash guard, not a rule.");
                return;
            }

            constructChainDepth++;
            try
            {
                RelayNodeWakes(source);

                // Leaving the board is the one thing Successor Protocol copies, and it is read before the card
                // is spent so the copy still has the Charges the original was holding.
                if (timing == ConstructTiming.Expire || timing == ConstructTiming.Sacrifice)
                {
                    SuccessorProtocolCopies(source);
                }
            }
            finally
            {
                constructChainDepth--;
            }
        }

        /// <summary>
        /// Relay Node: another construct has just done something, so it stores a Charge, and at its target it
        /// spends the lot relaying into a construct that has not been relayed into yet this turn.
        /// </summary>
        void RelayNodeWakes(BoardCard source)
        {
            for (int i = 0; i < board.Count; i++)
            {
                BoardCard entry = board[i];
                if (entry == null || entry == source || entry.card == null) continue;
                if (entry.card.construct != ConstructKind.RelayNode) continue;

                if (!TriggerConstruct(entry, ConstructTiming.OtherTriggered)) return;
            }
        }

        bool RelayNodeCharge(BoardCard entry, string prefix)
        {
            entry.charges += ConstructRules.RelayChargePerTrigger;
            Log(prefix + "stores " + entry.charges + " of " + ConstructRules.RelayChargeTarget + " charges.");

            if (entry.charges < ConstructRules.RelayChargeTarget) return true;

            entry.charges = 0;

            BoardCard target = PickRelayTarget(entry);
            if (target == null)
            {
                Log(prefix + "has nothing left to relay into this turn.");
                return true;
            }

            // Marked before it is woken, so a chain that comes back round cannot pick it a second time.
            target.relayTriggeredThisTurn = true;
            Log(prefix + "relays into " + target.card.cardName + ", which triggers again.");

            TriggerConstruct(target, ConstructRules.PrimaryTiming(target.card.construct));
            return true;
        }

        /// <summary>
        /// A construct the relay has not already used this turn, picked at random out of the ones that can be
        /// woken at all.
        ///
        /// The Relay Node is not a candidate for its own relay: "another Construct" means another one. The
        /// construct that just triggered is a candidate, which is what makes a relay feeding a Fuse worth the
        /// setup.
        /// </summary>
        BoardCard PickRelayTarget(BoardCard relay)
        {
            var candidates = new List<BoardCard>();

            for (int i = 0; i < board.Count; i++)
            {
                BoardCard entry = board[i];
                if (entry == null || entry == relay || entry.card == null) continue;
                if (entry.relayTriggeredThisTurn) continue;
                if (!ConstructRules.HasOwnTrigger(entry.card.construct)) continue;

                candidates.Add(entry);
            }

            if (candidates.Count == 0) return null;
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// Successor Protocol: another construct has left, so it does the same thing again.
        ///
        /// A copy is never itself copied.
        ///
        /// The pool is protected twice over today, and neither of those is a rule. A copy is not a departure,
        /// so only the leaving is ever answered; and a Successor Protocol has no final trigger of its own to
        /// be copied. Both are facts about which cards currently exist. The hinge family this pool is being
        /// built towards is precisely the family where leaving the board is the payoff, so two protocols on
        /// one board would hand each other work back and forth the moment one of them stood behind a hinge
        /// construct. The clamp is written down here rather than left resting on the roster, in the same
        /// shape Temporal Anchor carries for the same reason.
        ///
        /// The guard is on entry, not per protocol, so every protocol standing behind one construct still
        /// copies that construct's last act. What is refused is a copy starting inside a copy.
        /// </summary>
        void SuccessorProtocolCopies(BoardCard source)
        {
            if (source.card == null) return;
            if (!ConstructRules.HasFinalTrigger(source.card.construct)) return;
            if (state == null) return;

            if (state.copyingFinalTrigger)
            {
                Debug.LogWarning("[Construct] " + source.card.cardName + " was asked to have its last act " +
                                 "copied while a copy was already running. This is a crash guard, not a rule.");
                return;
            }

            state.copyingFinalTrigger = true;
            try
            {
                for (int i = 0; i < board.Count; i++)
                {
                    BoardCard entry = board[i];
                    if (entry == null || entry == source || entry.card == null) continue;
                    if (entry.card.construct != ConstructKind.SuccessorProtocol) continue;

                    Log(entry.card.cardName + " copies the last thing " + source.card.cardName + " did.");

                    // Copying is itself a construct triggering, so the relay hears it too.
                    PayFinalTrigger(source, entry.card.cardName + ": ");
                    if (CombatOver) return;

                    NotifyConstructTriggered(entry, ConstructTiming.OtherLeft);
                }
            }
            finally
            {
                state.copyingFinalTrigger = false;
            }
        }

        /// <summary>
        /// What a construct does as it leaves the board, by running out of duration or by being given up.
        ///
        /// One method for both timings because the two cards that have one do the same thing either way: what
        /// Fuse is, is its detonation. It is also what Successor Protocol re-runs, which is why it is written
        /// against an entry rather than against the thing that removed it.
        /// </summary>
        bool PayFinalTrigger(BoardCard entry, string prefix)
        {
            if (entry == null || entry.card == null) return false;

            switch (entry.card.construct)
            {
                case ConstructKind.Fuse:
                {
                    int damage = Mathf.Max(0, entry.charges) * ConstructRules.FuseDamagePerCharge;
                    Log(prefix + "the fuse goes off for " + damage + " damage (" + entry.charges + " charges).");
                    if (damage <= 0) return true;

                    DealDamageToEnemy(damage);
                    if (EnemyHealth <= 0) CheckEnemyDefeated();
                    return true;
                }

                case ConstructKind.Prototype:
                {
                    int charges = Mathf.Max(0, entry.charges);
                    int energy = charges * ConstructRules.PrototypeEnergyPerCharge;
                    int mana = charges * ConstructRules.PrototypeManaPerCharge;

                    if (energy > 0) PlayerEnergy += energy;
                    Log(prefix + "the prototype is finished: " + energy + " energy and " + mana + " mana from " +
                        charges + " charges. (Energy " + PlayerEnergy + ")");

                    if (mana > 0) GainMana(mana, "The Prototype hands over " + mana + " mana");
                    return true;
                }

                case ConstructKind.ForbiddenEngine:
                {
                    Stats.Block += ConstructRules.ForbiddenBlockOnSacrifice;
                    PlayerEnergy += ConstructRules.ForbiddenEnergyOnSacrifice;
                    Log(prefix + "pays out " + ConstructRules.ForbiddenBlockOnSacrifice + " shielding and " +
                        ConstructRules.ForbiddenEnergyOnSacrifice + " energy. (Shielding " + Stats.Block +
                        ", energy " + PlayerEnergy + ")");
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ the Generator

        /// <summary>
        /// Dormant Engine: it stores a Charge every turn, and it will not spend them while the hand is big.
        ///
        /// The hand it reads is the one carried over rather than the one about to be dealt, because the board
        /// runs before the draw. That is the card: it is holding cards that keeps it quiet, so the turn it
        /// finally pays out is the turn after the hand was spent.
        /// </summary>
        bool DormantEnginePulse(BoardCard entry, string prefix)
        {
            entry.charges += ConstructRules.DormantChargePerTurn;
            Log(prefix + "stores a charge: " + entry.charges + " held, with " + hand.Count + " cards in hand.");

            if (hand.Count > ConstructRules.DormantHandCeiling) return true;
            if (entry.charges <= 0) return true;

            int energy = entry.charges * ConstructRules.DormantEnergyPerCharge;
            entry.charges = 0;
            PlayerEnergy += energy;
            Log(prefix + "the hand is down to " + hand.Count + ", so it cashes in for " + energy +
                " energy. (Energy " + PlayerEnergy + ")");
            return true;
        }

        // ------------------------------------------------------------------ the Timers

        /// <summary>Fuse: it banks a Charge for every turn it is still standing.</summary>
        bool FusePulse(BoardCard entry, string prefix)
        {
            entry.charges += ConstructRules.FuseChargePerTurn;
            Log(prefix + "burns down towards " + (entry.charges * ConstructRules.FuseDamagePerCharge) +
                " damage (" + entry.charges + " charges).");
            return true;
        }

        /// <summary>
        /// Overheated Core: mana on every turn, and the Heat starts on the second one, which is what makes its
        /// first turn free and its last turn expensive.
        /// </summary>
        bool OverheatedCorePulse(BoardCard entry, string prefix)
        {
            entry.triggers++;

            GainMana(ConstructRules.OverheatedManaPerTurn,
                     prefix + "runs hot for " + ConstructRules.OverheatedManaPerTurn + " mana");

            if (entry.triggers > 1)
            {
                entry.heat += ConstructRules.OverheatedHeatPerTurn;
                Log(prefix + "gains " + ConstructRules.OverheatedHeatPerTurn + " heat. (" + entry.heat + " total)");
            }

            if (entry.heat < ConstructRules.OverheatedHeatThreshold) return true;

            LoseHealth(ConstructRules.OverheatedDamage, prefix + "is at " + entry.heat + " heat and burns you for " +
                       ConstructRules.OverheatedDamage + " damage.");
            return true;
        }

        // ------------------------------------------------------------------ the Reactors

        /// <summary>
        /// Reclamation Engine: it pays out on a turn that followed a wasteful one. It reads the count the turn
        /// boundary banked rather than a running total, because by the time it fires this turn's count has been
        /// reset to zero.
        /// </summary>
        bool ReclamationEnginePulse(BoardCard entry, string prefix)
        {
            int thrown = state != null ? state.discardsLastTurn : 0;
            if (thrown < ConstructRules.ReclamationDiscardsRequired) return false;

            if (discardPile.Count == 0)
            {
                Log(prefix + "you threw " + thrown + " away last turn, but the discard pile is empty.");
                return true;
            }

            for (int i = 0; i < ConstructRules.ReclamationCardsReturned; i++)
            {
                if (discardPile.Count == 0) break;

                int index = UnityEngine.Random.Range(0, discardPile.Count);
                CardData back = discardPile[index];
                discardPile.RemoveAt(index);
                hand.Add(back);

                Log(prefix + "you threw " + thrown + " away last turn, so " + back.cardName +
                    " comes back to your hand.");
            }

            return true;
        }

        /// <summary>
        /// Temporal Anchor: it resolves the last ordinary card played this turn a second time, for nothing, and
        /// then its one turn of duration is gone.
        ///
        /// The repeat is not a play: it does not cost, it does not raise the turn's card count, and it does not
        /// feed the constructs that watch cards being played. A construct is never what gets repeated, because
        /// repeating one would mean standing a second copy on the board.
        /// </summary>
        bool TemporalAnchorPulse(BoardCard entry, string prefix)
        {
            CardData last = state != null ? state.lastCardPlayedThisTurn : null;

            if (last == null)
            {
                Log(prefix + "has nothing to repeat.");
                return false;
            }

            if (state.replayingCard)
            {
                Debug.LogWarning("[Construct] " + entry.card.cardName + " was asked to repeat while a repeat was " +
                                 "already running. This is a crash guard, not a rule.");
                return false;
            }

            if (PickActive)
            {
                Log(prefix + "waits: a card is already being chosen.");
                return false;
            }

            Log(prefix + "repeats " + last.cardName + ", at no cost.");

            state.replayingCard = true;
            try
            {
                ResolveEffect(last, true);
            }
            finally
            {
                state.replayingCard = false;
            }

            return true;
        }

        // ------------------------------------------------------------------ the Converter

        /// <summary>
        /// Overflowing Archive: it converts a hand too big to use into mana. Read at the moment the turn is
        /// handed over, so it is the hand the player chose to end on.
        /// </summary>
        bool OverflowingArchivePulse(BoardCard entry, string prefix)
        {
            int above = hand.Count - ConstructRules.OverflowHandThreshold;
            if (above <= 0) return false;

            int mana = above * ConstructRules.OverflowManaPerCard;
            Log(prefix + "the turn ended on " + hand.Count + " cards, " + above + " above " +
                ConstructRules.OverflowHandThreshold + ".");
            GainMana(mana, prefix + "converts the surplus into " + mana + " mana");
            return true;
        }

        // ------------------------------------------------------------------ the Conditional and Puzzle constructs

        /// <summary>Archive: it draws only for a hand of exactly the size it names.</summary>
        bool ArchivePulse(BoardCard entry, string prefix)
        {
            if (hand.Count != ConstructRules.ArchiveHandExactly) return false;

            Log(prefix + "the hand is exactly " + ConstructRules.ArchiveHandExactly + ", so it draws " +
                ConstructRules.ArchiveDrawCount + ".");
            DrawCards(ConstructRules.ArchiveDrawCount);
            return true;
        }

        /// <summary>
        /// Perfect Alignment: it draws for the right hand and costs itself a turn for any other.
        ///
        /// The extra turn is taken off here rather than by failing to fire, so a wrong hand is a price paid
        /// rather than a turn not spent, and the clamp keeps it from ever being left with a duration of less
        /// than zero, which the board takes as a card still standing.
        /// </summary>
        bool PerfectAlignmentPulse(BoardCard entry, string prefix)
        {
            if (hand.Count == ConstructRules.AlignmentHandExactly)
            {
                Log(prefix + "the hand is exactly " + ConstructRules.AlignmentHandExactly + ", so it draws " +
                    ConstructRules.AlignmentDrawCount + ".");
                DrawCards(ConstructRules.AlignmentDrawCount);
                return true;
            }

            if (entry.turnsLeft > 0)
            {
                entry.turnsLeft = Mathf.Max(0, entry.turnsLeft - ConstructRules.AlignmentDurationLost);
            }

            Log(prefix + "the hand holds " + hand.Count + " rather than " +
                ConstructRules.AlignmentHandExactly + ", so it loses a turn. (" + entry.turnsLeft + " left)");
            return true;
        }

        /// <summary>
        /// Adaptive Core: it counts the first card of each archetype this construct has seen, and pays the lot
        /// out at its target.
        ///
        /// The archetypes it has been paid for are held on the construct, so it starts empty when it is played
        /// and cannot be paid twice for the same one. It is deliberately charged for itself: it is a Neutral
        /// card, so playing it is the first Neutral card.
        /// </summary>
        bool AdaptiveCoreSees(BoardCard entry, string prefix, CardData played)
        {
            if (played == null) return false;
            if (!entry.chargedArchetypes.Add(played.archetype)) return false;

            entry.charges++;
            Log(prefix + "your first " + played.archetype + " card this fight: " + entry.charges + " of " +
                ConstructRules.AdaptiveChargeTarget + " charges.");

            if (entry.charges < ConstructRules.AdaptiveChargeTarget) return true;

            int energy = entry.charges * ConstructRules.AdaptiveEnergyPerCharge;
            entry.charges = 0;
            PlayerEnergy += energy;
            Log(prefix + "pays out " + energy + " energy. (Energy " + PlayerEnergy + ")");
            return true;
        }

        /// <summary>Prototype: it does nothing at all until it is finished; a big enough card charges it.</summary>
        bool PrototypeCharge(BoardCard entry, string prefix)
        {
            entry.charges++;
            Log(prefix + "charges: " + entry.charges + ".");
            return true;
        }

        // ------------------------------------------------------------------ the Sacrifice construct

        /// <summary>Forbidden Engine: mana every turn, and everything else when it is given up.</summary>
        bool ForbiddenEnginePulse(BoardCard entry, string prefix)
        {
            GainMana(ConstructRules.ForbiddenManaPerTurn,
                     prefix + "runs for " + ConstructRules.ForbiddenManaPerTurn + " mana");
            return true;
        }

        // ------------------------------------------------------------------ the Gambler

        /// <summary>Roulette Core: one of three, chosen for the player, every turn.</summary>
        bool RouletteCorePulse(BoardCard entry, string prefix)
        {
            switch (UnityEngine.Random.Range(0, 3))
            {
                case 0:
                    Stats.Block += ConstructRules.RouletteBlock;
                    Log(prefix + "rolls shielding: " + ConstructRules.RouletteBlock + ". (Shielding " +
                        Stats.Block + ")");
                    break;

                case 1:
                    DealDamageToEnemy(ConstructRules.RouletteDamage);
                    Log(prefix + "rolls damage: " + ConstructRules.RouletteDamage + " to " + Enemy.enemyName + ".");
                    break;

                default:
                    Log(prefix + "rolls a card.");
                    DrawCards(ConstructRules.RouletteDraws);
                    break;
            }

            return true;
        }

        // ------------------------------------------------------------------ the timings

        /// <summary>
        /// The five timings that are not the board pass, gathered so each one is wired into the fight once.
        ///
        /// A card is announced after it has resolved and been paid for, because what these constructs ask about
        /// is a card that happened: an Adaptive Core counting an archetype, or a Prototype counting a cost.
        /// </summary>
        void NotifyCardPlayed(CardData played, int paid)
        {
            if (state == null || played == null) return;

            // Nothing more of this turn matters once the enemy is down, and a construct paying out here would
            // be paying out after the fight.
            if (CombatOver || EnemyHealth <= 0) return;

            // Temporal Anchor repeats an ordinary card. A construct is not recorded, because repeating one
            // would mean standing a second copy on the board rather than repeating anything it did.
            if (played.construct == ConstructKind.None) state.lastCardPlayedThisTurn = played;

            for (int i = 0; i < board.Count; i++)
            {
                BoardCard entry = board[i];
                if (entry == null || entry.card == null) continue;
                if (entry.card.construct == ConstructKind.None) continue;

                if (!TriggerConstruct(entry, ConstructTiming.CardPlayed, played, paid)) return;
            }
        }

        /// <summary>Overflowing Archive reads the turn as it is handed over, before anything is cleared.</summary>
        void FireEndOfTurnConstructs()
        {
            for (int i = 0; i < board.Count; i++)
            {
                BoardCard entry = board[i];
                if (entry == null || entry.card == null) continue;
                if (entry.card.construct == ConstructKind.None) continue;

                if (!TriggerConstruct(entry, ConstructTiming.EndOfTurn)) return;
            }
        }

        /// <summary>
        /// The ordinary start-of-turn pass over one construct from the neutral pool.
        ///
        /// Clockwork Core is offered here exactly as it is to every other construct, because it is a relic
        /// about constructs firing and this pool is constructs. The ones that have no start-of-turn pass of
        /// their own are skipped rather than rolled for, so the relic does not log a re-fire that did nothing.
        /// </summary>
        bool WakeConstruct(BoardCard entry)
        {
            if (!TriggerConstruct(entry, ConstructTiming.StartOfTurn)) return false;

            if (ConstructRules.HasOwnTrigger(entry.card.construct) && !FireClockworkCoreOnConstruct(entry)) return false;

            // A turn is spent only by a construct that actually fired, and only after it has paid out, so the
            // duration counts the turns the card was good for rather than the turns that have gone by.
            if (entry.turnsLeft > 0) entry.turnsLeft--;
            return true;
        }

        /// <summary>Clockwork Core, for a construct whose trigger is its own rules rather than an effect number.</summary>
        bool FireClockworkCoreOnConstruct(BoardCard entry)
        {
            float chance = RelicEffects.ClockworkCoreChance(Stats);
            if (chance <= 0f) return true;

            int cap = Mathf.Max(1, RelicSettings.Instance.maxClockworkExtraTriggers);
            int fired = 0;

            while (fired < cap && !PickActive && UnityEngine.Random.value * 100f < chance)
            {
                fired++;

                Log("Clockwork Core triggers " + entry.card.cardName + " again. (" + fired + " of " + cap + ")");
                if (!TriggerConstruct(entry, ConstructTiming.StartOfTurn)) return false;

                if (EnemyHealth <= 0)
                {
                    CheckEnemyDefeated();
                    return false;
                }
            }

            if (fired >= cap)
            {
                Debug.LogWarning("[Relic] Clockwork Core reached its re-fire cap (" + cap + ") on " +
                                 entry.card.cardName + ". This is a crash guard, not a rule.");
            }

            return true;
        }

        // ---------------------------------------------------------------- player turn

        void BeginPlayerTurn(bool firstTurn)
        {
            if (CombatOver) return;

            // The turn that just ended becomes last turn for the constructs that count cards, the free
            // action is handed back, and the relics that are allowed once a turn are handed back with it.
            if (state != null)
            {
                state.cardsPlayedLastTurn = state.cardsPlayedThisTurn;
                state.cardsPlayedThisTurn = 0;
                state.freeActionUsed = false;
                state.relicFiredThisTurn.Clear();
                state.hpLostThisTurn = 0;

                // Banked before it is cleared: Reclamation Engine reads the turn that has just ended, and this
                // is the only moment that total exists as a separate number from this turn's.
                state.discardsLastTurn = state.discardsThisTurn;
                state.discardsThisTurn = 0;

                // The relay's once-a-turn budget is handed back with the rest of them: a construct is only
                // worth relaying into once per turn, and the turn boundary is what makes that true.
                for (int i = 0; i < board.Count; i++)
                {
                    if (board[i] != null) board[i].relayTriggeredThisTurn = false;
                }
            }

            if (!ApplyUpkeep()) return;

            int poison = Stats.GetStatusMagnitude(StatusEffectType.Poison);
            if (poison > 0) LoseHealth(poison, "Poison deals " + poison + " damage to you.");
            if (!Stats.IsAlive)
            {
                Defeat();
                return;
            }

            PlayerEnergy = Stats.MaxEnergy;
            Stats.Block = 0;
            IsPlayerTurn = true;

            Log("--- Your turn (energy " + PlayerEnergy + ") ---");

            // After the reset above, never before it: shielding granted at the top of the turn would
            // otherwise be wiped by the line that clears the last turn's shielding.
            GrantBoulderShield();

            // The Goblet is bought a point at a time at the top of every turn, including this one.
            GrantGoblet();

            // The board runs before the draw, so a card that hands over energy or shielding is usable
            // this turn rather than next.
            FireBoard();

            // A board card can end the fight or empty the pile on its own, so stop before drawing.
            if (CombatOver) return;

            // A construct whose reveal is this turn's draw replaces it rather than adding to it: the player is
            // already choosing what to take, so dealing them a card on top of that would make the choice mean
            // nothing. The turn waits here until they have answered for it.
            if (PickActive)
            {
                RefreshUI();
                return;
            }

            if (!DrawCards(firstTurn ? PlayerStats.StartingHandSize : PlayerStats.CardsDrawnPerTurn)) return;
            RefreshUI();
        }

        /// <summary>
        /// Boulder Shield: shielding equal to a slice of a stat, granted once per turn.
        ///
        /// This runs at the top of the player's turn, so "once per turn" is the shape of the call rather
        /// than a flag that has to be cleared afterwards: the turn boundary is the reset.
        /// </summary>
        void GrantBoulderShield()
        {
            int block = RelicEffects.BoulderShieldBlock(Stats);
            if (block <= 0) return;

            Stats.Block += block;
            Log("Boulder Shield grants " + block + " shielding. (Total " + Stats.Block + ")");
        }

        /// <summary>Fires every card in play, in the order they were played, and spends the turns they cost.</summary>
        void FireBoard()
        {
            if (board.Count == 0) return;

            RefreshBoardDisplay();

            for (int i = 0; i < board.Count; i++)
            {
                BoardCard entry = board[i];
                if (entry == null || entry.card == null) continue;

                CardData card = entry.card;

                // A construct waiting on a free card is not on a cadence: playing one is what fires it, so
                // the ordinary pass steps over it rather than paying it out every turn.
                if (card.trigger == CardTrigger.ZeroCostCardPlayed) continue;

                // Momentum Loop only keeps going while the turn before it was busy enough.
                if (card.trigger == CardTrigger.StartOfTurnAfterMany && state != null &&
                    state.cardsPlayedLastTurn < card.triggerCardsPlayedLastTurn)
                {
                    continue;
                }

                // A reveal has the fight's attention until it is answered, so nothing standing behind it
                // fires, and nothing behind it spends a turn of its duration waiting its turn.
                if (PickActive) continue;

                // A construct from the neutral pool is driven by its own rulebook rather than by an effect:
                // what it does turns on its own Charges and on the rest of the turn, so there is no single
                // number for ResolveEffect to run.
                if (card.construct != ConstructKind.None)
                {
                    if (!WakeConstruct(entry)) return;

                    if (EnemyHealth <= 0)
                    {
                        CheckEnemyDefeated();
                        return;
                    }

                    continue;
                }

                ResolveEffect(card, true);

                // Stop the moment the fight ends, so a lethal first sigil does not fire the rest.
                if (CombatOver) return;
                if (EnemyHealth <= 0)
                {
                    CheckEnemyDefeated();
                    return;
                }

                // Clockwork Core, if the player is carrying it: a construct that has just paid out gets a
                // chance to pay out again, and each extra trigger rolls again.
                if (!FireClockworkCore(card)) return;

                // A turn is spent only by a construct that actually fired, and only after it has paid out:
                // the duration counts the turns the card was good for, not the turns that have gone by.
                if (entry.turnsLeft > 0) entry.turnsLeft--;
            }

            ExpireBoardCards();
        }

        /// <summary>
        /// Takes the cards whose turns have run out off the board and spends them. The count only ever reaches
        /// zero from a duration, so a card with no duration of its own never leaves this way.
        /// </summary>
        void ExpireBoardCards()
        {
            for (int i = board.Count - 1; i >= 0; i--)
            {
                BoardCard entry = board[i];
                if (entry == null)
                {
                    board.RemoveAt(i);
                    continue;
                }

                if (entry.turnsLeft != 0) continue;

                board.RemoveAt(i);
                if (entry.card == null) continue;

                Log(entry.card.cardName + " has run its course and is spent.");

                // Grand Orrery: the first construct to run out each turn fires once more before it is spent.
                // Guarded, so a last swing that ends the fight does not carry on through the rest of the
                // board afterwards.
                RelicDefinition orrery = RelicEffects.Definition(RelicEffect.GrandOrrery);
                if (orrery != null && !CombatOver && EnemyHealth > 0 && ClaimOnceThisTurn(RelicEffect.GrandOrrery))
                {
                    Log("Grand Orrery lets " + entry.card.cardName + " fire one last time.");

                    // A construct from the neutral pool has no effect number to resolve, so the last firing is
                    // its own start-of-turn pass. One that has no such pass is offered nothing rather than
                    // being told it fired.
                    if (entry.card.construct != ConstructKind.None)
                    {
                        if (ConstructRules.HasOwnTrigger(entry.card.construct) &&
                            !TriggerConstruct(entry, ConstructTiming.StartOfTurn))
                        {
                            break;
                        }
                    }
                    else
                    {
                        ResolveEffect(entry.card, true);
                    }
                }

                // A construct from the neutral pool does something on the way out, and it is asked while the
                // Charges it is paid on are still its own. The board is told as part of this, so a Successor
                // Protocol standing behind it copies the same detonation.
                if (entry.card.construct != ConstructKind.None && !CombatOver)
                {
                    if (!TriggerConstruct(entry, ConstructTiming.Expire)) break;
                }

                Spent(entry.card);

                if (CombatOver) break;
            }

            RefreshBoardDisplay();
        }

        /// <summary>
        /// Draws from the draw pile. A played card is spent for the encounter rather than recycled, so the
        /// deck is a supply and running the pile dry is a loss.
        ///
        /// That is deliberate, and it is the counterweight to the deck being any size the player likes: a
        /// small deck is consistent from turn to turn and dies young, a large one is erratic from turn to
        /// turn and lasts. The pile is what pays for the consistency, so it must be able to run out.
        ///
        /// Returns false once the encounter has ended.
        /// </summary>
        bool DrawCards(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (drawPile.Count == 0)
                {
                    OutOfCards();
                    return false;
                }

                int index = drawPile.Count - 1;
                hand.Add(drawPile[index]);
                drawPile.RemoveAt(index);
            }
            return true;
        }

        /// <summary>
        /// The recurring bill on a card that bought something up front: health paid and cards thrown away at
        /// the top of every turn, for as long as the fight lasts. Returns false when paying it ended the
        /// fight, so the turn stops rather than carrying on into a defeat.
        /// </summary>
        bool ApplyUpkeep()
        {
            if (state == null) return true;

            if (state.upkeepDamage > 0)
            {
                LoseHealth(state.upkeepDamage, "Upkeep costs you " + state.upkeepDamage + " health.");

                if (!Stats.IsAlive)
                {
                    Defeat();
                    return false;
                }
            }

            if (state.discardEachTurn && hand.Count > 0)
            {
                int index = UnityEngine.Random.Range(0, hand.Count);
                CardData gone = hand[index];
                hand.RemoveAt(index);
                DiscardCard(gone, "Overclock throws away " + gone.cardName + ".");
            }

            return true;
        }

        /// <summary>
        /// Fires the constructs that are waiting for a free card, on the turn one is played. Playing a free
        /// card is what they are paid for, which is why they are skipped by the ordinary board pass.
        /// </summary>
        void FireConstructs(bool freeCardPlayed)
        {
            if (!freeCardPlayed || board.Count == 0) return;

            for (int i = 0; i < board.Count; i++)
            {
                BoardCard entry = board[i];
                if (entry == null || entry.card == null) continue;

                CardData card = entry.card;
                if (card.trigger != CardTrigger.ZeroCostCardPlayed) continue;

                ResolveEffect(card, true);

                if (CombatOver) return;
                if (EnemyHealth <= 0)
                {
                    CheckEnemyDefeated();
                    return;
                }
            }
        }

        /// <summary>Conjures one random card from the pool straight into the hand.</summary>
        void GenerateRandomCard()
        {
            GenerateRandomCard(false);
        }

        /// <summary>
        /// The same, with the relics that watch conjuring folded in.
        ///
        /// Loaded Grimoire turns the first conjure of a turn into a choice between two cards, and it is read
        /// here rather than on the cards that conjure, so every source of random cards is subject to it:
        /// Wild Card, Vanishing Act's per-discard conjures, a Mirage Engine, and anything authored later.
        ///
        /// The free flag is what Mirage Engine asks for: the card arrives costing nothing, which is a
        /// discount hung on this one card rather than a rewrite of the shared asset it was drawn from.
        /// </summary>
        void GenerateRandomCard(bool free)
        {
            RelicDefinition grimoire = RelicEffects.Definition(RelicEffect.LoadedGrimoire);
            if (grimoire != null && ClaimOnceThisTurn(RelicEffect.LoadedGrimoire))
            {
                BeginGeneratedPick(grimoire, free);
                return;
            }

            ConjureRandomCard(free);
        }

        /// <summary>Puts the card a conjure turned up into the hand, with what the relics add to arriving.</summary>
        void ConjureRandomCard(bool free)
        {
            GameSession session = GameSession.Instance;
            if (session == null) return;

            CardData card = session.RandomCard();
            if (card == null) return;

            hand.Add(card);
            Log("A " + card.cardName + " is conjured into your hand.");

            ApplyConjureBenefits(card, free);

            // Wild Card pays a card back when what it turned up was free. Read off the card's own printed
            // cost, because this is about what was turned up, not about what it is being charged.
            if (card.cost == 0) DrawCards(1);

            // Loaded Dice hangs off the conjure itself rather than off the card that asked for it, so every
            // source of random cards feeds it without having to be told about it.
            TryLoadedDice(free);
        }

        /// <summary>
        /// What a relic adds to a card the moment it arrives: Lucky Charm takes the price off the first
        /// conjure of a fight, and Mirage Engine's card arrives already free.
        /// </summary>
        void ApplyConjureBenefits(CardData card, bool free)
        {
            if (card == null) return;

            int discount = free ? Mathf.Max(0, card.cost) : 0;

            RelicDefinition charm = RelicEffects.Definition(RelicEffect.LuckyCharm);
            if (charm != null && ClaimOnceThisCombat(RelicEffect.LuckyCharm))
            {
                int off = Mathf.Max(1, charm.scaling.ResolveInt(Stats));
                discount += off;
                Log("Lucky Charm takes " + off + " off the cost of the " + card.cardName + ".");
            }

            if (discount > 0) AddCostModifier(card, -discount);
        }

        /// <summary>
        /// Loaded Grimoire: the first conjure of the turn turns up two cards and one of them is kept.
        ///
        /// These cards came from nowhere rather than off the pile, so they are held in the pick directly
        /// instead of being read off the top of the draw pile the way a reveal is.
        /// </summary>
        void BeginGeneratedPick(RelicDefinition source, bool free)
        {
            GameSession session = GameSession.Instance;
            if (session == null || state == null) return;

            // One decision at a time: a conjure that happens while the player is already looking at cards
            // simply arrives rather than opening a second question on top of the first.
            if (PickActive)
            {
                ConjureRandomCard(free);
                return;
            }

            int count = Mathf.Max(2, source.secondaryMagnitude);
            var turned = new List<CardData>();

            for (int i = 0; i < count; i++)
            {
                CardData card = session.RandomCard();
                if (card == null) break;
                turned.Add(card);
            }

            if (turned.Count == 0) return;

            if (turned.Count == 1)
            {
                hand.Add(turned[0]);
                Log(source.itemName + " turns up a single " + turned[0].cardName + ".");
                ApplyConjureBenefits(turned[0], free);
                TryLoadedDice(free);
                return;
            }

            state.pickPending.Clear();
            state.pickPending.AddRange(turned);
            state.pickStage = PendingPick.KeepGeneratedOne;
            state.pickSource = source.itemName;
            state.pickCostsNothing = free;

            Log(source.itemName + " turns up " + turned.Count + " cards. Choose one to keep.");
        }

        /// <summary>How deep the Loaded Dice chain has gone. A crash guard, not a gameplay limit.</summary>
        int loadedDiceDepth;

        /// <summary>
        /// Loaded Dice: a chance that the card just conjured turns up another one, which can itself turn
        /// up another.
        ///
        /// The recursion is the mechanic, so this is deliberately not limited to a single extra card. The
        /// depth guard exists only so a chance that has climbed to certainty cannot spin forever, and it
        /// is set generously: a player who stacked the relic up is meant to be rewarded with a chain, not
        /// cut off at two.
        /// </summary>
        void TryLoadedDice(bool free)
        {
            float chance = RelicEffects.LoadedDiceChance(Stats);
            if (chance <= 0f) return;

            int cap = Mathf.Max(1, RelicSettings.Instance.maxLoadedDiceDepth);

            if (loadedDiceDepth >= cap)
            {
                Debug.LogWarning("[Relic] Loaded Dice reached its recursion guard at depth " + cap +
                                 ". This is a crash guard, not a rule.");
                return;
            }

            if (UnityEngine.Random.value * 100f >= chance) return;

            loadedDiceDepth++;
            Log("Loaded Dice turns up another card. (" + chance.ToString("0.#") + "%, depth " + loadedDiceDepth + ")");

            GenerateRandomCard(free);

            loadedDiceDepth--;
        }

        /// <summary>
        /// Clockwork Core: a chance that a construct fires again on the same turn. Rolled once per
        /// construct that actually fires, and each extra trigger rolls again, which is what lets a board
        /// turn explosive.
        ///
        /// Returns false when the fight ended underneath it, so the caller stops walking the board.
        /// </summary>
        bool FireClockworkCore(CardData card)
        {
            float chance = RelicEffects.ClockworkCoreChance(Stats);
            if (chance <= 0f) return true;

            int cap = Mathf.Max(1, RelicSettings.Instance.maxClockworkExtraTriggers);
            int fired = 0;

            while (fired < cap && !PickActive && UnityEngine.Random.value * 100f < chance)
            {
                fired++;

                Log("Clockwork Core triggers " + card.cardName + " again. (" + fired + " of " + cap + ")");
                ResolveEffect(card, true);

                if (CombatOver) return false;
                if (EnemyHealth <= 0)
                {
                    CheckEnemyDefeated();
                    return false;
                }
            }

            if (fired >= cap)
            {
                Debug.LogWarning("[Relic] Clockwork Core reached its re-fire cap (" + cap + ") on " + card.cardName +
                                 ". This is a crash guard, not a rule.");
            }

            return true;
        }


        /// <summary>How many of the player's turns a construct that was just played is good for.</summary>
        static int BoardTurnsFor(CardData card)
        {
            return card.constructDuration > 0 ? card.constructDuration : BoardCard.RestOfFight;
        }

        /// <summary>
        /// The same, with Architect's Lens folded in.
        ///
        /// The lens is spent only on a card it can actually lengthen. A card with no duration of its own
        /// runs to the end of the fight either way, so it waits for one with a clock on it rather than
        /// being thrown away on a card that would read the same either way.
        /// </summary>
        int TurnsOnBoardFor(CardData card)
        {
            int turns = BoardTurnsFor(card);

            RelicDefinition lens = RelicEffects.Definition(RelicEffect.ArchitectsLens);
            if (lens == null) return turns;

            if (turns == BoardCard.RestOfFight)
            {
                Log("Architect's Lens waits: " + card.cardName + " runs until the fight ends either way.");
                return turns;
            }

            if (!ClaimOnceThisCombat(RelicEffect.ArchitectsLens)) return turns;

            int extra = Mathf.Max(1, lens.scaling.ResolveInt(Stats));
            Log("Architect's Lens extends " + card.cardName + " by " + extra +
                (extra == 1 ? " turn." : " turns."));
            return turns + extra;
        }

        /// <summary>
        /// Collects the damage the last Reckless Stance banked, along with the name of the card that promised
        /// it, so the hit that lands can say where it came from. It is read once and the store is cleared, so
        /// a bonus can never be collected twice.
        /// </summary>
        int TakeNextAttackBonus(out string source)
        {
            source = null;
            if (state == null || state.nextAttackBonus <= 0) return 0;

            int bonus = state.nextAttackBonus;
            source = state.nextAttackBonusSource;
            state.nextAttackBonus = 0;
            state.nextAttackBonusSource = null;
            return bonus;
        }

        /// <summary>
        /// A reveal, part one: turns up the top of the draw pile and hands the choice to the player. The
        /// cards leave the pile now and sit in the pick until each has been answered for, so the reveal cannot
        /// be walked away from halfway and the pile cannot be shuffled back underneath it.
        ///
        /// What the cards are turned up for is the stage rather than the card: Palm Trick asks which one to
        /// keep and then which one to shuffle back, and the Tribunal asks only which one to draw.
        /// </summary>
        void BeginPick(CardData source, int count, PendingPick stage)
        {
            if (state == null || source == null) return;

            // One decision at a time: a second reveal would replace the cards the player is already looking
            // at, so a construct that turns up another handful waits its turn.
            if (PickActive)
            {
                Log(source.cardName + " waits: a card is already being chosen.");
                return;
            }

            int available = Mathf.Min(count, drawPile.Count);
            if (available <= 0)
            {
                Log(source.cardName + " turns up nothing: your draw pile is empty.");
                return;
            }

            state.pickPending.Clear();
            for (int i = 0; i < available; i++)
            {
                int index = drawPile.Count - 1;
                state.pickPending.Add(drawPile[index]);
                drawPile.RemoveAt(index);
            }

            state.pickStage = stage;
            state.pickSource = source.cardName;
            Log(source.cardName + " turns up " + available + " card" + (available == 1 ? "" : "s") +
                (stage == PendingPick.DrawOneDiscardRest
                    ? ". Choose one to draw into your hand."
                    : ". Choose one to keep in your hand."));
        }

        /// <summary>
        /// Answers the pick with the card at the given position among the ones it turned up. What the click
        /// means is read from the stage rather than from the click, because the player is asked one question
        /// at a time and the same gesture answers all of them.
        /// </summary>
        void PickCard(int index)
        {
            if (state == null || state.pickStage == PendingPick.None) return;
            if (index < 0 || index >= state.pickPending.Count) return;

            CardData picked = state.pickPending[index];
            state.pickPending.RemoveAt(index);

            if (state.pickStage == PendingPick.KeepGeneratedOne)
            {
                // Loaded Grimoire's whole decision: what is kept joins the hand, and the other conjured
                // card is discarded with it on the same click.
                hand.Add(picked);
                Log(PickName + ": you keep " + picked.cardName + ".");

                ApplyConjureBenefits(picked, state.pickCostsNothing);

                bool free = state.pickCostsNothing;
                SettlePick();
                state.pickCostsNothing = false;
                TryLoadedDice(free);
                RefreshUI();
                return;
            }

            if (state.pickStage == PendingPick.DrawOneDiscardRest)
            {
                // The Tribunal's whole decision: what is taken is drawn, and everything else that was turned
                // up is spent on the same click.
                hand.Add(picked);
                Log(PickName + ": you draw " + picked.cardName + ".");
            }
            else if (state.pickStage == PendingPick.KeepToHand)
            {
                hand.Add(picked);
                Log(PickName + ": you keep " + picked.cardName + ".");
                state.pickStage = PendingPick.Reshuffle;
            }
            else
            {
                // Anywhere in the pile rather than back on top: it has already been read, so putting it back
                // where it came from would make this a second look at the top instead of a shuffle.
                drawPile.Insert(UnityEngine.Random.Range(0, drawPile.Count + 1), picked);
                Log(PickName + ": " + picked.cardName + " goes back into your draw pile.");
            }

            SettlePick();
            RefreshUI();
        }

        /// <summary>
        /// Closes the pick once there is nothing left to choose between, which is the point the card is spent.
        /// Whatever is still turned up is discarded: it has been seen, so putting it back would make this a
        /// free look at the top of the pile.
        ///
        /// Palm Trick asks twice, so it stays open while there is still a choice to make between the cards it
        /// turned up. The Tribunal asks once, so its leftovers are spent as soon as that one is answered.
        /// </summary>
        void SettlePick()
        {
            if (state == null) return;

            if (state.pickStage != PendingPick.DrawOneDiscardRest && state.pickPending.Count > 1) return;

            while (state.pickPending.Count > 0)
            {
                CardData spare = state.pickPending[0];
                state.pickPending.RemoveAt(0);
                DiscardCard(spare, PickName + ": " + spare.cardName + " is discarded.");
            }

            state.pickStage = PendingPick.None;
        }

        /// <summary>Glory's rider: a card that lands the kill pays out beyond the fight's own reward.</summary>
        void AwardKillGold(CardData card)
        {
            if (card == null || card.bonusGoldOnKill <= 0) return;

            int amount = card.bonusGoldOnKill +
                         Mathf.FloorToInt(Stats.GetStat(card.ScalingStat) * card.goldPerStatPoint);
            if (amount <= 0) return;

            GameSession.Instance.AddGold(amount);
            Log(card.cardName + " pays out " + amount + " gold.");
        }

        /// <summary>
        /// The run-long price on a card. This is the only place a card is allowed to move an attribute for
        /// good, which is what turns All Out and Exhaust from a discount into a decision about what the rest
        /// of the run is going to be. The price is read off the card rather than off the printed field,
        /// because on a Willpower card the price is the half that scales.
        /// </summary>
        void ApplyPermanentChange(CardData card)
        {
            if (card == null) return;

            int change = card.PermanentStatChange(Stats);
            if (change == 0) return;

            int target = Stats.GetBaseStat(card.permanentStatTarget) + change;
            Stats.SetBaseStat(card.permanentStatTarget, target);
            Log(card.cardName + " leaves you at " + Stats.GetBaseStat(card.permanentStatTarget) + " " +
                card.permanentStatTarget + " for the rest of the run.");

            // The attributes just moved, so which one Broken Crown lifts may no longer be the same one.
            RefreshBrokenCrown(false);
        }


        public void EndPlayerTurn()
        {
            // A pick has to be answered before the turn is handed over: the cards it turned up belong to
            // this turn and came out of the pile this turn.
            if (!AcceptsPlays) return;

            // The constructs that read the turn on its way out, before anything is cleared, so the hand and
            // the discards they are looking at are still this turn's rather than the next one's.
            FireEndOfTurnConstructs();
            if (CombatOver) return;

            // Temporary cards that were not played this turn are discarded, as their keyword says.
            DiscardTemporaryCards();

            // The hand carries over between turns: unplayed cards are kept, not discarded.
            IsPlayerTurn = false;
            Stats.TickStatuses();
            RefreshUI();

            StartCoroutine(EnemyTurnRoutine());
        }

        /// <summary>
        /// Throws away every temporary card still in hand. A temporary card is one drawn this turn that
        /// was not played, which is the whole of the keyword: it is a card that has to be used or lost.
        /// </summary>
        void DiscardTemporaryCards()
        {
            if (state == null) return;

            for (int i = hand.Count - 1; i >= 0; i--)
            {
                CardData card = hand[i];
                if (card != null && card.temporary)
                {
                    hand.RemoveAt(i);
                    DiscardCard(card, "Temporary: " + card.cardName + " was not played this turn.");
                }
            }
        }

        // ---------------------------------------------------------------- enemy turn

        IEnumerator EnemyTurnRoutine()
        {
            // Pending for exactly as long as this could still be lost to a scene load: from the moment the
            // turn opens until the delay is up and the attack is about to land.
            if (state != null) state.enemyTurnPending = true;
            yield return new WaitForSeconds(enemyTurnDelay);
            if (state != null) state.enemyTurnPending = false;

            if (CombatOver) yield break;

            // The enemy's shielding never wears off. It is raised and it stays until it is broken, which is
            // what makes an enemy that turtles a puzzle rather than a pause in the fight.
            int raised = Enemy.GetBlockForTurn(enemyTurnIndex);
            if (raised > 0)
            {
                EnemyBlock += raised;
                Log(Enemy.enemyName + " raises " + raised + " shielding. (" + EnemyBlock + " total)");
            }

            int poison = StatusEffects.Magnitude(enemyStatuses, StatusEffectType.Poison);
            if (poison > 0)
            {
                EnemyHealth = Mathf.Max(0, EnemyHealth - poison);
                Log(Enemy.enemyName + " takes " + poison + " poison damage.");
                if (EnemyHealth <= 0)
                {
                    Victory();
                    yield break;
                }
            }

            int raw = Enemy.GetAttackForTurn(enemyTurnIndex);
            if (StatusEffects.Magnitude(enemyStatuses, StatusEffectType.Weak) > 0) raw = Mathf.RoundToInt(raw * 0.75f);

            // What Intimidate has taken off the enemy for the rest of the fight.
            if (state != null && state.enemyAttackReduction > 0) raw = Mathf.Max(0, raw - state.enemyAttackReduction);

            // Zone, which is absolute and is spent the moment it is used.
            if (state != null && state.enemyAttackNullified)
            {
                state.enemyAttackNullified = false;
                raw = 0;
                Log(Enemy.enemyName + " has nothing left to swing with.");
            }

            int incoming = Stats.ResolveIncomingDamage(raw);
            int blocked = Mathf.Min(Stats.Block, incoming);
            Stats.Block -= blocked;
            int taken = incoming - blocked;

            string message = Enemy.enemyName + " attacks for " + incoming + " damage";
            if (blocked > 0) message += " (" + blocked + " blocked)";
            message += " - you take " + taken + ".";
            LoseHealth(taken, message);

            enemyTurnIndex++;
            StatusEffects.Tick(enemyStatuses);
            RefreshUI();

            if (!Stats.IsAlive)
            {
                Defeat();
                yield break;
            }

            BeginPlayerTurn(false);
        }

        // ---------------------------------------------------------------- outcomes

        void CheckEnemyDefeated()
        {
            if (CombatOver || EnemyHealth > 0) return;

            // War Trophy: the first kill of the fight pays out energy. Read before the victory, so the
            // payout is part of the fight that is still running rather than of the screen after it.
            RelicDefinition trophy = RelicEffects.Definition(RelicEffect.WarTrophy);
            if (trophy != null && ClaimOnceThisCombat(RelicEffect.WarTrophy))
            {
                int energy = Mathf.Max(1, trophy.scaling.ResolveInt(Stats));
                PlayerEnergy += energy;
                Log("War Trophy grants " + energy + " energy.");
            }

            Victory();
        }

        void Victory()
        {
            CombatOver = true;
            IsPlayerTurn = false;
            GameSession.Instance.FinishCombat();
            Log(Enemy.enemyName + " is defeated. Experience awarded: " + Enemy.experienceReward + ".");

            GameSession session = GameSession.Instance;
            MapNode node = session.CurrentNode;
            bool wasBoss = node != null && node.roomType == RoomType.Boss;

            session.CompleteCombat(Enemy);
            RefreshUI();

            // The boss is the end of the dungeon: there is no junction after it, only daylight.
            if (wasBoss)
            {
                session.MarkRunWon();
                StartCoroutine(LoadAfterDelay(GameSession.RoomScene, victoryDelay));
                return;
            }

            StartCoroutine(LoadAfterDelay(GameSession.RewardScene, victoryDelay));
        }

        void Defeat()
        {
            EndCombat("You have been defeated.");
        }

        /// <summary>Ends the run because the deck ran out of cards.</summary>
        void OutOfCards()
        {
            Log("You have no cards left to draw.");
            EndCombat("You ran out of cards.");
        }

        /// <summary>Shows the death panel. Both death causes return to the main menu from here.</summary>
        void EndCombat(string reason)
        {
            CombatOver = true;
            IsPlayerTurn = false;
            GameSession.Instance.FinishCombat();
            GameSession.Instance.RecordDeath(reason);

            Log(reason);
            if (defeatReasonText != null) defeatReasonText.text = reason;
            if (defeatPanel != null) defeatPanel.SetActive(true);

            RefreshUI();
        }

        public void ReturnToMenu()
        {
            GameSession.Instance.AbandonRun();
        }

        IEnumerator LoadAfterDelay(string sceneName, float delay)
        {
            yield return new WaitForSeconds(delay);
            GameSession.Instance.LoadScene(sceneName);
        }

        // ---------------------------------------------------------------- display

        string BoardSummary()
        {
            if (board.Count == 0) return "BOARD: nothing established";

            var names = new List<string>();
            foreach (BoardCard entry in board)
            {
                if (entry == null || entry.card == null) continue;

                // A construct on a clock says how many of your turns it has left. One with no duration says
                // nothing: running for the whole fight is what the board is for.
                names.Add(entry.turnsLeft > 0
                    ? entry.card.cardName + " (" + entry.turnsLeft + " turns)"
                    : entry.card.cardName);
            }
            return "BOARD: " + string.Join(", ", names.ToArray());
        }

        /// <summary>
        /// Redraws the board readout: the line of names, and the squares that show the same thing at a glance.
        /// One square per construct, filled from the left and carrying on underneath itself once a line is
        /// full. A square prints nothing; hovering one brings up the construct's card and its clock.
        /// </summary>
        void RefreshBoardDisplay()
        {
            SetText(boardText, BoardSummary());

            if (boardSlots == null || boardSlots.Length == 0) return;

            // The row is built as wide as a board can ever be and only the slots the player actually owns are
            // shown, so widening the board is a change of number rather than a change of layout.
            int capacity = Mathf.Clamp(Stats.BoardSlots, 0, boardSlots.Length);
            int rows = capacity <= 0 ? 0 : ((capacity - 1) / SlotsPerRow) + 1;
            float step = SlotSize + SlotSpacing;

            for (int i = 0; i < boardSlots.Length; i++)
            {
                BoardSlotView slot = boardSlots[i];
                if (slot == null) continue;

                bool owned = i < capacity;
                slot.gameObject.SetActive(owned);
                if (!owned) continue;

                // Where a square lands is decided here rather than in the scene. A board wider than one line
                // is the case the layout has to answer for, and one writer keeps the answer in one place.
                var rect = (RectTransform)slot.transform;
                rect.anchoredPosition = new Vector2((i % SlotsPerRow) * step, -(i / SlotsPerRow) * step);

                BoardCard entry = i < board.Count ? board[i] : null;
                bool filled = entry != null && entry.card != null;

                slot.Bind(filled ? entry.card : null, filled ? entry.turnsLeft : 0,
                          i, this,
                          filled && entry.card.sacrificable,
                          filled ? entry.charges : 0,
                          filled ? entry.heat : 0);
            }

            // The squares grow downwards and the names follow the lowest line, so a second line never lands
            // on top of them.
            float height = rows <= 0 ? SlotSize : rows * SlotSize + (rows - 1) * SlotSpacing;

            if (boardSlotsRect != null)
            {
                boardSlotsRect.sizeDelta = new Vector2(boardSlotsRect.sizeDelta.x, height);
            }

            if (boardTextRect != null)
            {
                boardTextRect.anchoredPosition =
                    new Vector2(boardTextRect.anchoredPosition.x, SlotRowTop - height - SlotSpacing);
            }
        }

        void RefreshUI()
        {
            if (!started) return;

            if (Enemy != null)
            {
                SetText(enemyNameText, Enemy.enemyName);
                SetText(enemyHealthText, "Health " + EnemyHealth + " / " + Enemy.maxHealth);
                SetText(enemyBlockText, EnemyBlock > 0 ? "Shield " + EnemyBlock : "");
                SetText(enemyStatusText, StatusEffects.Describe(enemyStatuses));
            }

            SetText(playerHealthText, "Health " + Stats.Health + " / " + Stats.MaxHealth);
            SetText(playerEnergyText, "Energy " + PlayerEnergy + " / " + Stats.MaxEnergy);
            SetText(playerManaText, "Mana " + Stats.Mana);
            SetText(playerBlockText, "Shield " + Stats.Block);
            SetText(playerStatusText, Stats.StatusSummary());
            RefreshBoardDisplay();
            SetText(pileText, "Draw " + drawPile.Count + "    Discard " + discardPile.Count + "    Board " + board.Count + "    Deck " + GameSession.Instance.Decks[GameSession.Instance.ActiveDeckIndex].Count);
            // A pick takes over the turn label: it is the one thing the player has to answer, and the label
            // is the only readout in the middle of the screen.
            string phase = CombatOver ? "Combat over" : (IsPlayerTurn ? "Your turn" : "Enemy turn");
            if (PickActive) phase = PickPrompt();
            SetText(turnText, phase);
            SetText(logText, string.Join("\n", log.ToArray()));

            if (endTurnButton != null) endTurnButton.interactable = AcceptsPlays;

            RebuildHand();
        }

        void RebuildHand()
        {
            if (handContainer == null || cardViewPrefab == null) return;

            foreach (CardView view in handViews)
            {
                if (view != null) Destroy(view.gameObject);
            }
            handViews.Clear();

            for (int i = 0; i < hand.Count; i++)
            {
                CardView view = Instantiate(cardViewPrefab, handContainer);
                view.Bind(hand[i], this, Stats, i);
                handViews.Add(view);
            }

            // A pick's cards go in the same strip as the hand and are drawn lifted, as though the pointer
            // were over all of them at once. They are bound as offers rather than as hand cards, so the click
            // that takes one comes back here instead of trying to play it.
            if (state != null)
            {
                for (int i = 0; i < state.pickPending.Count; i++)
                {
                    int index = i;

                    CardView view = Instantiate(cardViewPrefab, handContainer);
                    view.BindChoice(state.pickPending[i], Stats);
                    view.SetChoiceEnabled(true);
                    view.Chosen += chosen => PickCard(index);
                    Lift(view);
                    handViews.Add(view);
                }
            }

            // The fan is handed the list rather than reading the strip's children, so it never lays out the
            // cards that are being destroyed this frame.
            if (handFan != null) handFan.SetCards(handViews);
        }

        /// <summary>Shows a card as though the pointer were on it, without one being there.</summary>
        static void Lift(CardView view)
        {
            CardHoverEffect hover = view.GetComponent<CardHoverEffect>();
            if (hover != null) hover.SetForcedHover(true);
        }

        void Log(string message)
        {
            log.Add(message);
            if (log.Count > 40) log.RemoveAt(0);
        }

        static void SetText(Text target, string value)
        {
            if (target != null) target.text = value;
        }
    }
}

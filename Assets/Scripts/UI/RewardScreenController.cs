using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// Post-fight reward screen. The experience and gold are automatic, but the card is a choice: three
    /// offers, take one or take none, and taking one adds it to the deck.
    ///
    /// The offers are drawn as the cards themselves rather than as paragraphs about them. A card is
    /// already a readable object with its cost, its name, its effect and its number, so the screen
    /// instantiates the same prefab the hand and the deck view use and lets the player click the one they
    /// want. Reading the offer and choosing it are the same act.
    ///
    /// The choice is made with a deck in mind, so I opens the character menu from here exactly as it does
    /// from every other screen, and the deck is read from there. That means this screen can be left and
    /// rebuilt mid-decision, which is why what has been taken is recorded on the session rather than here.
    /// </summary>
    public class RewardScreenController : MonoBehaviour
    {
        /// <summary>
        /// Offers are drawn larger than a hand card, because here there is room and nothing else on screen
        /// competing for attention. The card's aspect fitter derives the height from this width.
        /// </summary>
        static readonly Vector2 RewardCardSize = new Vector2(260f, 390f);

        [SerializeField] Text titleText;
        [SerializeField] Text experienceText;
        [SerializeField] Text goldText;
        [SerializeField] Text rewardTitleText;
        [Tooltip("Holds the instantiated offer cards, laid out side by side.")]
        [SerializeField] RectTransform rewardContainer;
        [SerializeField] CardView cardPrefab;
        [Tooltip("Alpha applied to non-hovered offers when one is hovered.")]
        [SerializeField] float hoverDimAlpha = 0.35f;
        [SerializeField] Text rewardResultText;
        [SerializeField] Text summaryText;
        [SerializeField] Button continueButton;

        readonly List<CardView> views = new List<CardView>();
        readonly System.Collections.Generic.Dictionary<CardView, System.Action<bool>> hoverHandlers =
            new System.Collections.Generic.Dictionary<CardView, System.Action<bool>>();

        readonly List<CardData> offers = new List<CardData>();

        GameSession Session { get { return GameSession.Instance; } }

        void Start()
        {
            GameSession session = Session;

            if (titleText != null) titleText.text = "VICTORY";

            if (experienceText != null)
            {
                experienceText.text = "+" + session.LastExperienceReward + " XP";
                if (session.CurrentEnemy != null) experienceText.text += "    (" + session.CurrentEnemy.enemyName + " defeated)";
            }

            // What the room paid, next to what it taught. Gold is the shop's currency, so it is the number
            // that makes a shelf price mean something.
            if (goldText != null)
            {
                goldText.text = session.LastGoldReward > 0
                    ? "+" + session.LastGoldReward + " gold        purse " + session.Gold
                    : "no gold        purse " + session.Gold;
            }

            BuildOffers();
            RefreshSummary();

            if (rewardResultText != null) rewardResultText.text = "";

            if (continueButton != null)
            {
                continueButton.onClick.RemoveAllListeners();
                continueButton.onClick.AddListener(OnContinue);
            }

            if (session.ConsumeItemNotice() && rewardResultText != null) rewardResultText.text = session.ItemNoticeText;

            // Coming back from the character menu rebuilds this screen from scratch. If the card was already
            // taken before the player went to look at their deck, the choice must still read as made.
            if (session.RewardClaimed) ShowClaimed(session.LastRewardCard);
        }

        void Update()
        {
            // I reads the deck, which is the other half of choosing a card. The character menu holds the
            // VIEW DECK button, so this is the same route into it that the other screens use.
            if (!Hotkeys.CharacterPressed()) return;
            OnCharacter();
        }

        void OnDestroy()
        {
            ClearViews();
        }

        void OnCharacter()
        {
            GameSession session = Session;
            if (session == null) return;

            session.ReturnScene = GameSession.RewardScene;
            session.LoadScene(GameSession.CharacterScene);
        }

        /// <summary>
        /// Draws one card per offer, from the same prefab the hand and the deck grid use. The roll happens
        /// when the fight ends, so this screen only has to show what it was handed.
        /// </summary>
        void BuildOffers()
        {
            offers.Clear();
            foreach (CardData card in Session.RewardChoices)
            {
                if (card != null) offers.Add(card);
            }

            if (rewardTitleText != null)
            {
                rewardTitleText.text = offers.Count > 0 ? "TAKE A CARD" : "NO CARD OFFERED";
            }

            ClearViews();

            if (rewardContainer == null || cardPrefab == null) return;

            PlayerStats stats = Session.Stats;

            foreach (CardData card in offers)
            {
                CardView view = Instantiate(cardPrefab, rewardContainer);
                view.BindChoice(card, stats);
                view.Chosen += OnChosen;
                // Ensure we can dim non-hovered offers: give every offer a CanvasGroup and register a hover handler.
                var cg = view.gameObject.GetComponent<CanvasGroup>();
                if (cg == null) cg = view.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 1f;

                System.Action<bool> handler = (hovered) => OnOfferHovered(view, hovered);
                hoverHandlers[view] = handler;
                view.HoverChanged += handler;
                views.Add(view);

                // The side by side layout reads the card's own size, so an offer is sized here rather than
                // being left at the size a hand card has to fit into.
                RectTransform rect = view.transform as RectTransform;
                if (rect != null) rect.sizeDelta = RewardCardSize;
            }
        }

        /// <summary>Clicking an offer is the whole decision, so it is taken straight away.</summary>
        void OnChosen(CardView view)
        {
            if (view == null || view.Card == null) return;
            Take(view.Card);
        }

        void OnOfferHovered(CardView hoveredView, bool hovered)
        {
            // Dim all other offer views while one is hovered.
            foreach (CardView v in views)
            {
                if (v == null) continue;
                var cg = v.gameObject.GetComponent<CanvasGroup>();
                if (cg == null) continue;
                cg.alpha = (hovered && v != hoveredView) ? hoverDimAlpha : 1f;
            }
        }

        /// <summary>
        /// Adds the chosen card to the collection and the deck. The session is what decides whether that is
        /// allowed, so a second click and a rebuilt screen both land on the same answer.
        /// </summary>
        void Take(CardData card)
        {
            if (!Session.ClaimReward(card)) return;

            ShowClaimed(card);
            RefreshSummary();
        }

        /// <summary>
        /// Puts the screen into its settled state: one card taken, the rest still readable but no longer
        /// choices. Both the click and the return from the character menu come through here, so the two
        /// routes cannot end up looking different.
        /// </summary>
        void ShowClaimed(CardData card)
        {
            foreach (CardView view in views)
            {
                if (view != null) view.SetChoiceEnabled(false);
            }

            if (rewardTitleText != null) rewardTitleText.text = "CARD TAKEN";

            if (rewardResultText != null && card != null) rewardResultText.text = "Added " + card.cardName + " to your deck.";

            if (continueButton != null)
            {
                Text label = continueButton.GetComponentInChildren<Text>();
                if (label != null) label.text = "CONTINUE";
            }
        }

        void ClearViews()
        {
            foreach (CardView view in views)
            {
                if (view == null) continue;
                view.Chosen -= OnChosen;
                if (hoverHandlers.ContainsKey(view))
                {
                    view.HoverChanged -= hoverHandlers[view];
                    hoverHandlers.Remove(view);
                }
                Destroy(view.gameObject);
            }
            views.Clear();
        }

        void RefreshSummary()
        {
            if (summaryText == null) return;

            GameSession session = Session;
            summaryText.text = "Level " + session.Level +
                               "    XP " + session.Experience + " / " + session.ExperienceForNextLevel + " to level" +
                               "\nHealth " + session.Stats.Health + " / " + session.Stats.MaxHealth +
                               "\n" + DeckSummary.Describe(DeckSummary.Readable(session.Decks[session.ActiveDeckIndex])) +
                               "\nPress I, then VIEW DECK, to read the deck before choosing.";
        }

        public void OnContinue()
        {
            Session.CompleteRoom();
        }
    }
}

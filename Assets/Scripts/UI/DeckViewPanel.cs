using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace DungeonCards
{
    /// <summary>
    /// The full screen deck editor: every card the player owns, drawn as a card, in a scrolling grid.
    ///
    /// This is the replacement for the deck tab and its list of text rows. Each card carries how many
    /// copies of it are in the deck, and it is edited in place: left click adds a copy, right click takes
    /// one out. A card with no copies left in the deck stays on screen greyed out, so it can be put back.
    ///
    /// It can be opened from the character menu or mid combat. Editing mid combat is safe because the
    /// fight drew its pile when it started, so the current encounter cannot be disturbed.
    /// </summary>

    public class DeckViewPanel : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] Button closeButton;
        [SerializeField] RectTransform gridContent;
        [SerializeField] CardView cardPrefab;
        [SerializeField] Text titleText;
        [SerializeField] Text summaryText;
        [SerializeField] InputField searchInput;
        [SerializeField] Button deckButton1;
        [SerializeField] Button deckButton2;
        [SerializeField] Button deckButton3;

        readonly List<CardView> views = new List<CardView>();
        private int viewingDeckIndex = 0;

        GameSession Session { get { return GameSession.Instance; } }

        public bool IsOpen { get { return panel != null && panel.activeSelf; } }

        void Awake()
        {
            // The original scene did not contain the new search/deck controls, so create any missing
            // controls at runtime while still honoring Inspector-assigned references when they exist.
            EnsureControls();

            if (closeButton == null && panel != null)
                closeButton = panel.transform.Find("CloseButton")?.GetComponent<Button>();

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }

            if (searchInput != null)
            {
                searchInput.onValueChanged.RemoveAllListeners();
                searchInput.onValueChanged.AddListener(delegate { Rebuild(); });
                searchInput.onEndEdit.RemoveAllListeners();
                searchInput.onEndEdit.AddListener(delegate { Rebuild(); });
            }

            if (deckButton1 != null)
            {
                deckButton1.onClick.RemoveAllListeners();
                deckButton1.onClick.AddListener(() => SetViewingDeck(0));
            }
            if (deckButton2 != null)
            {
                deckButton2.onClick.RemoveAllListeners();
                deckButton2.onClick.AddListener(() => SetViewingDeck(1));
            }
            if (deckButton3 != null)
            {
                deckButton3.onClick.RemoveAllListeners();
                deckButton3.onClick.AddListener(() => SetViewingDeck(2));
            }
        }

        void EnsureControls()
        {
            if (panel == null) return;

            Transform root = panel.transform;

            if (searchInput == null)
                searchInput = root.Find("SearchInput")?.GetComponent<InputField>();
            if (deckButton1 == null)
                deckButton1 = root.Find("DeckButton1")?.GetComponent<Button>();
            if (deckButton2 == null)
                deckButton2 = root.Find("DeckButton2")?.GetComponent<Button>();
            if (deckButton3 == null)
                deckButton3 = root.Find("DeckButton3")?.GetComponent<Button>();

            if (searchInput == null) searchInput = CreateSearchInput(root);
            if (deckButton1 == null) deckButton1 = CreateDeckButton(root, "DeckButton1", "1", 590f);
            if (deckButton2 == null) deckButton2 = CreateDeckButton(root, "DeckButton2", "2", 680f);
            if (deckButton3 == null) deckButton3 = CreateDeckButton(root, "DeckButton3", "3", 770f);
        }

        static InputField CreateSearchInput(Transform parent)
        {
            GameObject go = new GameObject("SearchInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(40f, -145f);
            rect.sizeDelta = new Vector2(520f, 56f);

            Image background = go.GetComponent<Image>();
            background.color = new Color(0.08f, 0.10f, 0.16f, 1f);

            InputField input = go.GetComponent<InputField>();
            input.targetGraphic = background;
            input.lineType = InputField.LineType.SingleLine;

            Text text = CreateChildText(go.transform, "Text");
            text.alignment = TextAnchor.MiddleLeft;
            text.fontSize = 24;
            text.color = Color.white;
            text.raycastTarget = false;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 4f);
            textRect.offsetMax = new Vector2(-16f, -4f);
            input.textComponent = text;

            Text placeholder = CreateChildText(go.transform, "Placeholder");
            placeholder.text = "Search cards...";
            placeholder.fontSize = 24;
            placeholder.color = new Color(0.65f, 0.70f, 0.80f, 1f);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.raycastTarget = false;
            RectTransform placeholderRect = placeholder.rectTransform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(16f, 4f);
            placeholderRect.offsetMax = new Vector2(-16f, -4f);
            input.placeholder = placeholder;

            return input;
        }

        static Button CreateDeckButton(Transform parent, string objectName, string label, float x)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -145f);
            rect.sizeDelta = new Vector2(80f, 56f);

            Image background = go.GetComponent<Image>();
            background.color = new Color(0.22f, 0.30f, 0.48f, 1f);

            Button button = go.GetComponent<Button>();
            button.targetGraphic = background;

            Text text = CreateChildText(go.transform, "Text");
            text.text = label;
            text.fontSize = 26;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return button;
        }

        static Text CreateChildText(Transform parent, string objectName)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            Text text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return text;
        }

        void SetViewingDeck(int index)
        {
            int maxDecks = (Session != null && Session.Decks != null) ? Session.Decks.Length : 3;
            viewingDeckIndex = Mathf.Clamp(index, 0, maxDecks - 1);
            UpdateDeckButtons();
            Rebuild();
        }

        void UpdateDeckButtons()
        {
            Color activeColor = new Color(0.35f, 0.65f, 0.95f, 1f);
            Color inactiveColor = new Color(0.22f, 0.30f, 0.48f, 1f);

            int maxDecks = (Session != null && Session.Decks != null && Session.Decks.Length > 0)
                ? Session.Decks.Length
                : 3;

            SetButtonVisible(deckButton1, maxDecks > 0);
            SetButtonVisible(deckButton2, maxDecks > 1);
            SetButtonVisible(deckButton3, maxDecks > 2);

            SetButtonColor(deckButton1, viewingDeckIndex == 0 ? activeColor : inactiveColor);
            SetButtonColor(deckButton2, viewingDeckIndex == 1 ? activeColor : inactiveColor);
            SetButtonColor(deckButton3, viewingDeckIndex == 2 ? activeColor : inactiveColor);
        }

        static void SetButtonVisible(Button btn, bool visible)
        {
            if (btn != null) btn.gameObject.SetActive(visible);
        }

        static void SetButtonColor(Button btn, Color c)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = c;
        }

        void Update()
        {
            // Escape closes the deck view only; it must not fall through and leave the screen behind it.
            if (IsOpen && Hotkeys.CancelPressed()) Close();
        }

        public void Open()
        {
            if (panel == null) return;

            panel.SetActive(true);
            int maxDecks = (Session != null && Session.Decks != null && Session.Decks.Length > 0) ? Session.Decks.Length : 3;
            viewingDeckIndex = Session != null ? Mathf.Clamp(Session.ActiveDeckIndex, 0, maxDecks - 1) : 0;
            if (searchInput != null) searchInput.text = "";
            UpdateDeckButtons();
            Rebuild();
        }

        public void Close()
        {
            if (panel == null) return;
            if (Session != null) Session.ActiveDeckIndex = viewingDeckIndex;
            panel.SetActive(false);
        }

        void OnDisable()
        {
            if (Session != null) Session.ActiveDeckIndex = viewingDeckIndex;
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        void Rebuild()
        {
            Clear();

            if (gridContent == null || cardPrefab == null) return;

            GameSession session = Session;
            if (session == null) return;

            // The grid shows the whole collection rather than only what is in the deck: a card taken out
            // has to stay on screen, greyed out, or there would be no way to put it back.
            List<CardData> types = DeckSummary.Distinct(session.Owned);
            
            // Filter by search input
            string searchText = searchInput != null ? searchInput.text : "";
            if (!string.IsNullOrEmpty(searchText))
            {
                string searchTrimmed = searchText.Trim();
                if (searchTrimmed.Length > 0)
                {
                    string[] searchWords = searchTrimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    types = types.Where(card =>
                    {
                        if (card == null) return false;
                        if (!string.IsNullOrEmpty(card.cardName) &&
                            card.cardName.IndexOf(searchTrimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;

                        if (!string.IsNullOrEmpty(card.description))
                        {
                            if (card.description.IndexOf(searchTrimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                                return true;

                            for (int i = 0; i < searchWords.Length; i++)
                            {
                                if (card.description.IndexOf(searchWords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                                    return true;
                            }
                        }

                        return false;
                    }).ToList();
                }
            }
            
            PlayerStats stats = session.Stats;
            Deck viewedDeck = (session.Decks != null && viewingDeckIndex >= 0 && viewingDeckIndex < session.Decks.Length)
                ? session.Decks[viewingDeckIndex]
                : session.Deck;

            if (viewedDeck == null) return;

            foreach (CardData card in types)
            {
                CardView view = Instantiate(cardPrefab, gridContent);
                view.BindDeckEntry(card, stats, viewedDeck.CountOf(card));
                view.DeckClick += OnDeckClick;
                views.Add(view);
            }

            if (titleText != null) titleText.text = "DECK " + (viewingDeckIndex + 1);
            RefreshSummary();
        }

        void RefreshSummary()
        {
            if (summaryText == null) return;

            GameSession session = Session;
            if (session == null) return;

            Deck viewedDeck = (session.Decks != null && viewingDeckIndex >= 0 && viewingDeckIndex < session.Decks.Length)
                ? session.Decks[viewingDeckIndex]
                : session.Deck;

            if (viewedDeck == null) return;
            summaryText.text = DeckSummary.Describe(DeckSummary.Readable(viewedDeck));
        }

        /// <summary>
        /// Left click puts one more copy into the deck, right click takes one out.
        ///
        /// A card can never go in more times than the player owns, so clicking a card whose copies are all
        /// already committed simply does nothing rather than duplicating it.
        /// </summary>
        void OnDeckClick(CardView view, int button)
        {
            if (view == null || view.Card == null) return;

            GameSession session = Session;
            if (session == null) return;

            CardData card = view.Card;
            Deck viewedDeck = (session.Decks != null && viewingDeckIndex >= 0 && viewingDeckIndex < session.Decks.Length)
                ? session.Decks[viewingDeckIndex]
                : session.Deck;

            if (viewedDeck == null) return;

            if (button == 1)
            {
                if (viewedDeck.CountOf(card) <= 0) return;
                viewedDeck.Remove(card, 1);
            }
            else if (button == 0)
            {
                if (viewedDeck.CountOf(card) >= session.OwnedCount(card)) return;
                viewedDeck.Add(card, 1);
            }
            else
            {
                return;
            }

            view.RefreshDeckEntry(viewedDeck.CountOf(card));
            RefreshSummary();
        }

        void Clear()
        {
            foreach (CardView view in views)
            {
                if (view == null) continue;
                view.DeckClick -= OnDeckClick;
                Destroy(view.gameObject);
            }
            views.Clear();
        }
    }


    /// <summary>Shared deck reading helpers, so the deck view and the inventory agree on the numbers.</summary>
    public static class DeckSummary
    {
        /// <summary>The deck's cards in an order that is pleasant to read: energy first, then mana, cheapest first.</summary>
        public static List<CardData> Readable(Deck deck)
        {
            var cards = new List<CardData>();
            if (deck == null) return cards;

            foreach (CardData card in deck.Cards)
            {
                if (card != null) cards.Add(card);
            }

            cards.Sort(Compare);
            return cards;
        }

        /// <summary>
        /// The distinct card types in a collection, in the readable order. The deck grid wants one entry
        /// per card, but ownership is counted per copy.
        /// </summary>
        public static List<CardData> Distinct(IReadOnlyList<CardData> source)
        {
            var cards = new List<CardData>();
            if (source == null) return cards;

            foreach (CardData card in source)
            {
                if (card == null || cards.Contains(card)) continue;
                cards.Add(card);
            }

            cards.Sort(Compare);
            return cards;
        }

        static int Compare(CardData a, CardData b)

        {
            int typeA = a.costType == CardCostType.Mana ? 1 : 0;
            int typeB = b.costType == CardCostType.Mana ? 1 : 0;
            if (typeA != typeB) return typeA.CompareTo(typeB);

            if (a.cost != b.cost) return a.cost.CompareTo(b.cost);

            return string.Compare(a.cardName, b.cardName, StringComparison.Ordinal);
        }

        /// <summary>A one line reading of the deck: how big it is and how it leans, by card count.</summary>
        public static string Describe(List<CardData> cards)
        {
            if (cards == null || cards.Count == 0) return "The deck is empty.";

            var counts = new Dictionary<CardArchetype, int>();
            foreach (CardData card in cards)
            {
                int existing;
                counts.TryGetValue(card.archetype, out existing);
                counts[card.archetype] = existing + 1;
            }

            var parts = new List<string>();
            AddCount(parts, counts, CardArchetype.Strength);
            AddCount(parts, counts, CardArchetype.Agility);
            AddCount(parts, counts, CardArchetype.Intellect);
            AddCount(parts, counts, CardArchetype.Willpower);
            AddCount(parts, counts, CardArchetype.Neutral);

            var builder = new StringBuilder();
            builder.Append(cards.Count).Append(cards.Count == 1 ? " card" : " cards");
            if (parts.Count > 0) builder.Append("   -   ").Append(string.Join(", ", parts.ToArray()));

            return builder.ToString();
        }

        static void AddCount(List<string> parts, Dictionary<CardArchetype, int> counts, CardArchetype archetype)
        {
            int count;
            if (!counts.TryGetValue(archetype, out count) || count == 0) return;
            parts.Add(count + " " + archetype);
        }
    }
}

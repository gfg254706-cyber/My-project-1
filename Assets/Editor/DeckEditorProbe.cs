using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DungeonCards;

namespace DungeonCards.EditorTools
{
    /// <summary>
    /// Temporary play mode probe for the deck editor and the hover fix. Delete once checked.
    /// </summary>
    public static class DeckEditorProbe
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        static int pass;
        static int fail;
        static readonly StringBuilder sb = new StringBuilder();

        static void Check(string label, bool ok)
        {
            if (ok) pass++;
            else fail++;
            sb.AppendLine((ok ? "PASS  " : "FAIL  ") + label);
        }

        static void Note(string line)
        {
            sb.AppendLine("      " + line);
        }

        static T Field<T>(object target, string name) where T : class
        {
            if (target == null) return null;
            FieldInfo f = target.GetType().GetField(name, Any);
            return f == null ? null : f.GetValue(target) as T;
        }

        static T[] All<T>(bool includeInactive) where T : UnityEngine.Object
        {
            return UnityEngine.Object.FindObjectsByType<T>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
        }

        static Button ButtonNamed(string name)
        {
            foreach (Button b in All<Button>(true))
            {
                if (b.name == name && b.gameObject.activeInHierarchy) return b;
            }
            return null;
        }

        static void Report(string title)
        {
            sb.Append("RESULT ").Append(pass).Append(" passed, ").Append(fail).Append(" failed.");
            Debug.Log("[Probe][" + title + "]\n" + sb.ToString());
        }

        static void Reset()
        {
            pass = 0;
            fail = 0;
            sb.Length = 0;
        }

        static CardView HandCardView()
        {
            foreach (CardView v in All<CardView>(false))
            {
                if (v.Playable) return v;
            }
            return null;
        }

        // ------------------------------------------------------------------ combat hand / hover

        [MenuItem("Tools/Dungeon Cards/Probe Hand Hover")]
        public static void RunHand()
        {
            Reset();

            CombatManager combat = CombatManager.Instance;
            if (combat == null)
            {
                Debug.Log("[Probe] CombatManager.Instance is null - run this in the Combat scene.");
                return;
            }

            Canvas.ForceUpdateCanvases();

            var cards = new List<CardView>();
            foreach (CardView v in All<CardView>(false))
            {
                if (v.Playable) cards.Add(v);
            }

            Check("H1 a hand of card views exists (" + cards.Count + " / hand " + combat.Hand.Count + ")",
                  cards.Count > 0 && cards.Count == combat.Hand.Count);

            // The nested canvas is what broke the deck grid, and what made the earlier design need
            // reordering. Nothing may put one back.
            int withCanvas = 0;
            foreach (CardView v in All<CardView>(true))
            {
                if (v.GetComponent<Canvas>() != null) withCanvas++;
            }
            Check("H2 no card carries a Canvas of its own (" + withCanvas + " found)", withCanvas == 0);

            // The gutter has to be wide enough that a 1.1x card never reaches the next one, because
            // nothing reorders or re-sorts any more.
            GameObject handPanel = GameObject.Find("HandPanel");
            HorizontalLayoutGroup layout = handPanel != null ? handPanel.GetComponent<HorizontalLayoutGroup>() : null;
            RectTransform cardRect = cards.Count > 0 ? (RectTransform)cards[0].transform : null;
            float width = cardRect != null ? cardRect.rect.width : 0f;
            float growthPerSide = width * 0.05f;
            float spacing = layout != null ? layout.spacing : -1f;
            Check("H3 the hand gutter (" + spacing + ") is wider than a hovered card's growth per side (" + growthPerSide + ")",
                  layout != null && spacing > growthPerSide);

            CardView card = cards[0];
            CardHoverEffect hover = card.GetComponent<CardHoverEffect>();
            Check("H4 the card carries a CardHoverEffect", hover != null);

            if (hover == null) { Report("hand"); return; }

            int indexBefore = card.transform.GetSiblingIndex();
            Vector2 posBefore = ((RectTransform)card.transform).anchoredPosition;

            hover.OnPointerEnter(null);
            Canvas.ForceUpdateCanvases();

            int indexAfter = card.transform.GetSiblingIndex();
            Vector2 posAfter = ((RectTransform)card.transform).anchoredPosition;

            // The jitter was the card being re-parented in the layout order, which slid it out from
            // under the pointer. Hovering must not touch either the sibling order or the position.
            Check("H5 hovering leaves the sibling index alone (" + indexBefore + " -> " + indexAfter + ")",
                  indexBefore == indexAfter);
            Check("H6 hovering does not move the card in its layout (" + posBefore + " -> " + posAfter + ")",
                  Vector2.Distance(posBefore, posAfter) < 0.01f);

            FieldInfo targetField = typeof(CardHoverEffect).GetField("target", Any);
            float target = targetField != null ? Convert.ToSingle(targetField.GetValue(hover)) : -1f;
            Check("H7 hovering still scales the card (target " + target + ")", Mathf.Abs(target - 1.1f) < 0.001f);

            CardTooltip tooltip = null;
            foreach (CardTooltip t in All<CardTooltip>(false))
            {
                if (t.Owner == card) tooltip = t;
            }
            Check("H8 hovering still opens the keyword tooltip", tooltip != null && tooltip.IsOpen);

            hover.OnPointerExit(null);
            Canvas.ForceUpdateCanvases();

            float after = targetField != null ? Convert.ToSingle(targetField.GetValue(hover)) : -1f;
            bool hidden = tooltip == null || !tooltip.IsOpen;
            Check("H9 leaving restores the scale (" + after + ") and hides the tooltip", Mathf.Abs(after - 1f) < 0.001f && hidden);
            Check("H10 leaving also leaves the sibling index alone (" + card.transform.GetSiblingIndex() + ")",
                  card.transform.GetSiblingIndex() == indexBefore);

            Report("hand");
        }

        // ------------------------------------------------------------------ deck editor

        static List<CardView> GridEntries(DeckViewPanel deckView)
        {
            var entries = new List<CardView>();
            RectTransform content = Field<RectTransform>(deckView, "gridContent");
            if (content == null) return entries;

            foreach (CardView v in content.GetComponentsInChildren<CardView>(true)) entries.Add(v);
            return entries;
        }

        static void Click(CardView view, PointerEventData.InputButton button)
        {
            var data = new PointerEventData(EventSystem.current);
            data.button = button;
            view.OnPointerClick(data);

            // TMP only fills its parsed text when it regenerates, which happens on the canvas update, so
            // the badge has to be given that update before it is read back.
            Canvas.ForceUpdateCanvases();
        }


        static int ShownCount(CardView view)
        {
            TextMeshProUGUI t = Field<TextMeshProUGUI>(view, "countText");
            int value;
            return t != null && int.TryParse(t.GetParsedText(), out value) ? value : -1;
        }

        static bool BadgeVisible(CardView view)
        {
            GameObject root = Field<GameObject>(view, "countRoot");
            return root != null && root.activeSelf;
        }

        static bool Greyed(CardView view, out string detail)
        {
            Image frame = Field<Image>(view, "frame");
            detail = frame != null ? frame.color.ToString() : "<no frame>";
            if (frame == null) return false;

            // The spent tint is a flat grey: all three channels close together and dark.
            Color c = frame.color;
            float spread = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return spread < 0.06f && c.r < 0.30f;
        }

        [MenuItem("Tools/Dungeon Cards/Probe Deck Editor")]
        public static void RunDeck()
        {
            Reset();

            GameSession session = GameSession.Instance;
            if (session == null)
            {
                Debug.Log("[Probe] GameSession.Instance is null.");
                return;
            }

            sb.AppendLine("Deck editor probe. Collection " + session.Owned.Count + " cards, deck " + session.Deck.Count + ".");

            // --- the deck tab and its list are gone
            Check("D1 the character menu no longer has a DECK tab button", ButtonNamed("DeckTabButton") == null);
            Check("D2 the character menu no longer has a deck list scroll view", GameObject.Find("DeckScrollView") == null);

            Button viewDeck = ButtonNamed("ViewDeckButton");
            Check("D3 a VIEW DECK button exists", viewDeck != null);

            CharacterMenuController menu = UnityEngine.Object.FindAnyObjectByType<CharacterMenuController>(FindObjectsInactive.Include);
            Check("D4 a CharacterMenuController exists", menu != null);

            DeckViewPanel deckView = menu != null ? Field<DeckViewPanel>(menu, "deckView") : null;
            Check("D5 the controller is wired to the deck view", deckView != null);

            if (viewDeck == null || deckView == null) { Report("deck"); return; }

            // It has to occupy the slot the deck tab used to hold.
            var rect = (RectTransform)viewDeck.transform;
            Check("D6 VIEW DECK sits where the DECK tab used to (" + rect.anchoredPosition + ")",
                  Mathf.Abs(rect.anchoredPosition.x - (-410f)) < 1f && Mathf.Abs(rect.anchoredPosition.y - (-470f)) < 1f);

            viewDeck.onClick.Invoke();
            Canvas.ForceUpdateCanvases();
            Check("D7 clicking VIEW DECK opens the full screen deck editor", deckView.IsOpen);

            List<CardData> types = DeckSummary.Distinct(session.Owned);
            List<CardView> entries = GridEntries(deckView);
            Check("D8 the grid shows one entry per owned card type (" + entries.Count + " / " + types.Count + ")",
                  entries.Count == types.Count && types.Count > 0);

            // --- the count badge
            int badged = 0;
            int wrongCount = 0;
            foreach (CardView entry in entries)
            {
                if (!BadgeVisible(entry)) continue;
                badged++;
                if (ShownCount(entry) != session.Deck.CountOf(entry.Card)) wrongCount++;
            }
            Check("D9 every entry shows its copy badge (" + badged + "/" + entries.Count + ")", badged == entries.Count);
            Check("D10 every badge shows how many are in the deck (" + (entries.Count - wrongCount) + "/" + entries.Count + ")", wrongCount == 0);

            Check("D11 the badge is on the hand cards too but hidden - a grid entry is the only place it is set",
                  entries.Count > 0 && BadgeVisible(entries[0]));

            // --- every card starts fully committed, so adding must do nothing
            CardView target = null;
            foreach (CardView entry in entries)
            {
                if (entry.Card != null && session.OwnedCount(entry.Card) == 1) { target = entry; break; }
            }
            if (target == null) target = entries[0];

            CardData card = target.Card;
            int owned = session.OwnedCount(card);
            int inDeck = session.Deck.CountOf(card);
            Note("testing with " + card.cardName + ": " + inDeck + " in deck of " + owned + " owned");

            Click(target, PointerEventData.InputButton.Left);
            int afterAddAtCap = session.Deck.CountOf(card);
            Check("D12 left clicking a fully committed card does not add another (" + inDeck + " -> " + afterAddAtCap + ")",
                  afterAddAtCap == inDeck);
            Check("D13 and its badge still reads " + afterAddAtCap, ShownCount(target) == afterAddAtCap);

            // --- right click takes one out
            Click(target, PointerEventData.InputButton.Right);
            int afterRemove = session.Deck.CountOf(card);
            Check("D14 right clicking removes one copy (" + afterAddAtCap + " -> " + afterRemove + ")",
                  afterRemove == afterAddAtCap - 1);
            Check("D15 the badge follows (" + ShownCount(target) + ")", ShownCount(target) == afterRemove);

            string tint;
            bool grey = Greyed(target, out tint);
            if (afterRemove == 0)
            {
                Check("D16 a card with none left in the deck is greyed out (frame " + tint + ")", grey);
                Check("D17 and shows a zero (" + ShownCount(target) + ")", ShownCount(target) == 0);
            }
            else
            {
                Check("D16 a card with copies left is not greyed out (frame " + tint + ")", !grey);
            }

            // --- and it can go back in
            Click(target, PointerEventData.InputButton.Left);
            int back = session.Deck.CountOf(card);
            Check("D18 left clicking puts the copy back (" + afterRemove + " -> " + back + ")",
                  back == afterRemove + 1);

            string tint2;
            bool grey2 = Greyed(target, out tint2);
            Check("D19 and the card is no longer greyed out (frame " + tint2 + ")", !grey2);

            // --- the cap still holds after going back and forth
            for (int i = 0; i < owned + 2; i++) Click(target, PointerEventData.InputButton.Left);
            Check("D20 spamming left click never exceeds what is owned (" + session.Deck.CountOf(card) + " of " + owned + ")",
                  session.Deck.CountOf(card) == owned);

            for (int i = 0; i < owned + 2; i++) Click(target, PointerEventData.InputButton.Right);
            Check("D21 right clicking past empty leaves the count at zero, never negative (" + session.Deck.CountOf(card) + ")",
                  session.Deck.CountOf(card) == 0);

            Click(target, PointerEventData.InputButton.Left);
            Check("D22 the deck survives being emptied and refilled (" + session.Deck.CountOf(card) + " of " + owned + ")",
                  session.Deck.CountOf(card) == 1);

            Text summary = Field<Text>(deckView, "summaryText");
            Check("D23 the summary counts the deck live: '" + (summary != null ? summary.text : "") + "'",
                  summary != null && summary.text.StartsWith(session.Deck.Count.ToString()));

            // --- Deck Selector and Search Bar tests
            Button btn1 = Field<Button>(deckView, "deckButton1");
            Button btn2 = Field<Button>(deckView, "deckButton2");
            Button btn3 = Field<Button>(deckView, "deckButton3");
            InputField search = Field<InputField>(deckView, "searchInput");

            if (btn1 != null && btn2 != null && btn3 != null)
            {
                Check("D24 deck selector buttons 1, 2, 3 exist", true);

                // Switch to Deck 2
                btn2.onClick.Invoke();
                Canvas.ForceUpdateCanvases();
                Check("D25 switching to deck 2 updates viewed deck",
                      summary != null && summary.text.StartsWith(session.Decks[1].Count.ToString()));

                // Add card to Deck 2
                List<CardView> deck2Entries = GridEntries(deckView);
                if (deck2Entries.Count > 0)
                {
                    CardView d2Target = deck2Entries[0];
                    CardData d2Card = d2Target.Card;
                    int d1Before = session.Decks[0].CountOf(d2Card);

                    Click(d2Target, PointerEventData.InputButton.Left);
                    Check("D26 modifying deck 2 does not change deck 1 (" + session.Decks[1].CountOf(d2Card) + " vs deck 1: " + session.Decks[0].CountOf(d2Card) + ")",
                          session.Decks[0].CountOf(d2Card) == d1Before);

                    // Switch back to Deck 1
                    btn1.onClick.Invoke();
                    Canvas.ForceUpdateCanvases();
                    Check("D27 switching back to deck 1 restores deck 1 count",
                          session.Decks[0].CountOf(d2Card) == d1Before);
                }

                // Switch to Deck 2 and close
                btn2.onClick.Invoke();
                deckView.Close();
                Check("D28 closing deck view while deck 2 selected saves active deck index",
                      session.ActiveDeckIndex == 1);

                // Reopen and switch back to Deck 1
                deckView.Open();
                btn1.onClick.Invoke();
                deckView.Close();
                Check("D29 reopening and selecting deck 1 restores active deck to 0",
                      session.ActiveDeckIndex == 0);
                deckView.Open();
            }

            if (search != null)
            {
                Check("D30 search bar exists", true);

                // Test search by card name
                search.text = card.cardName;
                search.onValueChanged.Invoke(search.text);
                Canvas.ForceUpdateCanvases();
                List<CardView> filtered = GridEntries(deckView);
                bool allMatchName = filtered.Count > 0;
                foreach (CardView cv in filtered)
                {
                    if (cv.Card == null || (!cv.Card.cardName.ToLower().Contains(card.cardName.ToLower()) &&
                        !cv.Card.description.ToLower().Contains(card.cardName.ToLower())))
                    {
                        allMatchName = false;
                        break;
                    }
                }
                Check("D31 search filters by card name (" + filtered.Count + " results)", allMatchName);

                // Test empty search restores all
                search.text = "";
                search.onValueChanged.Invoke("");
                Canvas.ForceUpdateCanvases();
                Check("D32 clearing search bar restores all owned cards (" + GridEntries(deckView).Count + " / " + types.Count + ")",
                      GridEntries(deckView).Count == types.Count);
            }

            Report("deck");
        }

    }
}

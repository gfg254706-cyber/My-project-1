using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// Shared scene for the non-combat rooms. Event rooms present a text event with two to four choices,
    /// vantage rooms hand over the map, and the exit room ends the run.
    /// </summary>
    public class RoomController : MonoBehaviour
    {
        [Header("Common")]
        [SerializeField] Text noticeText;
        [SerializeField] Text roomTitleText;
        [SerializeField] Button mapButton;
        [SerializeField] Button characterButton;

        [Header("Event room")]
        [SerializeField] GameObject eventPanel;
        [SerializeField] Text eventTitleText;
        [SerializeField] Text eventBodyText;
        [SerializeField] Transform optionContainer;
        [SerializeField] Button optionButtonPrefab;
        [SerializeField] Text eventResultText;
        [SerializeField] Button eventContinueButton;

        [Header("Vantage room")]
        [SerializeField] GameObject vantagePanel;
        [SerializeField] Text vantageTitleText;
        [SerializeField] Text vantageBodyText;
        [SerializeField] Button vantageContinueButton;

        [Header("Exit room")]
        [SerializeField] GameObject exitPanel;
        [SerializeField] Text exitTitleText;
        [SerializeField] Text exitBodyText;
        [SerializeField] Button exitButton;

        [Header("Shop room")]
        [SerializeField] ShopPanel shopPanel;

        readonly List<Button> optionButtons = new List<Button>();
        EventData currentEvent;

        GameSession Session { get { return GameSession.Instance; } }

        void Start()
        {
            GameSession session = Session;

            if (mapButton != null) mapButton.onClick.AddListener(OnMap);
            if (characterButton != null) characterButton.onClick.AddListener(OnCharacter);

            RefreshNotice();

            // Only the shop room shows the shop, so it starts hidden and the shop case opens it.
            if (shopPanel != null) shopPanel.Close();

            // The boss dying sends the player here with the run already won, so there is no room to show.
            if (session.RunWon)
            {
                if (roomTitleText != null) roomTitleText.text = "THE EXIT";
                ShowExit();
                return;
            }

            MapNode node = session.CurrentNode;
            if (node == null)
            {
                session.LoadScene(GameSession.MainMenuScene);
                return;
            }

            if (roomTitleText != null) roomTitleText.text = RoomTypes.Label(node.roomType) + " ROOM";

            switch (node.roomType)
            {
                case RoomType.Event:
                    ShowEvent(node);
                    break;
                case RoomType.Vantage:
                    ShowVantage(node);
                    break;
                case RoomType.Shop:
                    ShowShop(node);
                    break;
                case RoomType.Exit:
                    ShowExit();
                    break;
                default:
                    // A combat room should never route here, but do not strand the player if it does.
                    session.CompleteRoom();
                    break;
            }
        }

        void Update()
        {
            if (Hotkeys.CharacterPressed()) OnCharacter();
        }

        void RefreshNotice()
        {
            if (noticeText == null) return;

            GameSession session = Session;

            if (session.ConsumeItemNotice())
            {
                noticeText.text = session.ItemNoticeText;
                return;
            }

            if (session.ConsumeMapExpiredNotice())
            {
                noticeText.text = "Your map has faded away.";
                return;
            }

            if (session.MapReveal != null && session.MapReveal.IsActive)
            {
                noticeText.text = "Your map reads the next " + GameSession.MapRevealLayers +
                                  " rooms. It fades after " + session.MapReveal.Remaining +
                                  (session.MapReveal.Remaining == 1 ? " more encounter." : " more encounters.");
                return;
            }

            noticeText.text = "Gold " + session.Gold + "    Press M for your map, I for your character.";
        }

        // ------------------------------------------------------------------ event room

        void ShowEvent(MapNode node)
        {
            if (eventPanel != null) eventPanel.SetActive(true);
            if (vantagePanel != null) vantagePanel.SetActive(false);
            if (exitPanel != null) exitPanel.SetActive(false);

            currentEvent = PickEvent(node);
            if (currentEvent == null)
            {
                if (eventTitleText != null) eventTitleText.text = "Empty room";
                if (eventBodyText != null) eventBodyText.text = "Nothing waits here. Add events to the GameDatabase to fill event rooms.";
                if (eventResultText != null) eventResultText.text = "";
                if (eventContinueButton != null)
                {
                    eventContinueButton.gameObject.SetActive(true);
                    eventContinueButton.onClick.RemoveAllListeners();
                    eventContinueButton.onClick.AddListener(OnContinue);
                }
                return;
            }

            if (eventTitleText != null) eventTitleText.text = currentEvent.eventName;
            if (eventBodyText != null) eventBodyText.text = currentEvent.body;

            // Returning to an event already resolved keeps the outcome instead of offering the choice again.
            if (GameSession.Instance.RoomResolved)
            {
                if (eventResultText != null) eventResultText.text = GameSession.Instance.LastRoomResultText;
                if (eventContinueButton != null)
                {
                    eventContinueButton.gameObject.SetActive(true);
                    eventContinueButton.onClick.RemoveAllListeners();
                    eventContinueButton.onClick.AddListener(OnContinue);
                }
                return;
            }

            if (eventResultText != null) eventResultText.text = "";
            if (eventContinueButton != null) eventContinueButton.gameObject.SetActive(false);
            BuildOptions();
        }

        EventData PickEvent(MapNode node)
        {
            GameSession session = Session;
            GameDatabase db = session.Database;
            if (db == null || db.events == null || db.events.Count == 0) return null;

            // Stable for this room within this run, so leaving and re-entering shows the same event,
            // but mixed with the run seed so the same room is not the same event on every run.
            // Masked rather than Mathf.Abs, which returns a negative for int.MinValue and would index
            // off the end of the list.
            int hash = unchecked(session.RunSeed * 486187739 + node.id * 31 + node.layer * 7);
            int index = (hash & 0x7FFFFFFF) % db.events.Count;
            return db.events[index];
        }

        void BuildOptions()
        {
            foreach (Button button in optionButtons)
            {
                if (button != null) Destroy(button.gameObject);
            }
            optionButtons.Clear();

            if (currentEvent == null || optionContainer == null || optionButtonPrefab == null) return;

            int count = Mathf.Clamp(currentEvent.options.Count, 0, 4);
            for (int i = 0; i < count; i++)
            {
                Button button = Instantiate(optionButtonPrefab, optionContainer);
                int index = i;

                Text label = button.GetComponentInChildren<Text>();
                if (label != null) label.text = currentEvent.options[i].label;

                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ChooseOption(index));
                optionButtons.Add(button);
            }
        }

        void ChooseOption(int index)
        {
            if (currentEvent == null || index < 0 || index >= currentEvent.options.Count) return;

            GameSession session = Session;
            EventOption option = currentEvent.options[index];

            // An option can branch: a chest is a chest most of the time and a mimic the rest of it.
            EventOutcome outcome = option.Roll();
            string summary = outcome.effect != null ? outcome.effect.Apply(session.Stats, session) : "";

            string text = outcome.resultText;
            if (!string.IsNullOrEmpty(summary)) text += (string.IsNullOrEmpty(text) ? "" : "\n\n") + summary;
            if (!session.Stats.IsAlive) text += "\n\nThe wound is mortal.";

            session.RoomResolved = true;
            session.LastRoomResultText = text;
            session.LastRoomFatal = !session.Stats.IsAlive;

            if (eventResultText != null) eventResultText.text = text;

            foreach (Button button in optionButtons)
            {
                if (button != null) button.interactable = false;
            }

            if (eventContinueButton != null)
            {
                eventContinueButton.gameObject.SetActive(true);
                eventContinueButton.onClick.RemoveAllListeners();
                eventContinueButton.onClick.AddListener(OnContinue);
            }
        }

        void OnContinue()
        {
            GameSession session = Session;

            // An event that emptied the last of the player's health ends the run.
            if (session.LastRoomFatal)
            {
                session.AbandonRun("You died in the dungeon.");
                return;
            }

            // An event can force a fight. It is fought before the player moves on.
            if (session.PendingEncounter != null)
            {
                session.StartEncounter(session.PendingEncounter);
                return;
            }

            session.CompleteRoom();
        }

        // ------------------------------------------------------------------ vantage room

        void ShowVantage(MapNode node)
        {
            if (eventPanel != null) eventPanel.SetActive(false);
            if (vantagePanel != null) vantagePanel.SetActive(true);
            if (exitPanel != null) exitPanel.SetActive(false);

            if (vantageTitleText != null) vantageTitleText.text = "VANTAGE POINT";
            if (vantageBodyText != null)
            {
                vantageBodyText.text =
                    "You climb above the dungeon floor and the shape of what lies ahead unfolds beneath you.\n\n" +
                    "You take a map. Press M and it will show you the next " + GameSession.MapRevealLayers +
                    " rooms from wherever you stand, but it will not last: the map fades after " +
                    GameSession.MapRevealEncounters + " encounters.\n\n" +
                    "Spend it where the paths are hardest to choose between.";
            }

            if (vantageContinueButton != null)
            {
                vantageContinueButton.onClick.RemoveAllListeners();
                vantageContinueButton.onClick.AddListener(OnContinue);
            }

            MapOverlay.OpenIfAvailable();
        }

        // ------------------------------------------------------------------ exit room

        void ShowExit()
        {
            if (eventPanel != null) eventPanel.SetActive(false);
            if (vantagePanel != null) vantagePanel.SetActive(false);
            if (exitPanel != null) exitPanel.SetActive(true);

            if (exitTitleText != null) exitTitleText.text = "YOU ESCAPED";
            if (exitBodyText != null)
            {
                GameSession session = Session;
                exitBodyText.text =
                    (session.RunWon
                        ? "The boss falls, and the way out is clear. You step into daylight.\n\n"
                        : "You step out of the dungeon into daylight.\n\n") +
                    "Rooms explored: " + session.RoomsExplored +
                    "    Combats won: " + session.EncountersCleared +
                    "    Level " + session.Level +
                    "\nDeck " + session.Decks[session.ActiveDeckIndex].Count + " cards" +
                    "    Bag " + session.Inventory.CarriedCount + " / " + Inventory.MaxCarriedSlots +
                    "    Experience " + session.Experience;
            }

            if (exitButton != null)
            {
                exitButton.onClick.RemoveAllListeners();
                exitButton.onClick.AddListener(OnLeave);
            }
        }

        void OnLeave()
        {
            Session.AbandonRun();
        }

        // ------------------------------------------------------------------ shop room

        void ShowShop(MapNode node)
        {
            if (eventPanel != null) eventPanel.SetActive(false);
            if (vantagePanel != null) vantagePanel.SetActive(false);
            if (exitPanel != null) exitPanel.SetActive(false);

            // The shop draws itself from the session and reports back when the player walks away, so the
            // room does not have to know what is on the shelves. Leaving finishes the room, exactly like
            // the continue button on an event.
            if (shopPanel != null) shopPanel.Open(OnContinue);
        }

        // ------------------------------------------------------------------ side screens

        void OnMap()
        {
            MapOverlay.OpenIfAvailable();
        }

        void OnCharacter()
        {
            GameSession session = Session;
            session.ReturnScene = GameSession.RoomScene;
            session.LoadScene(GameSession.CharacterScene);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// The junction after every room: the exits the map says are reachable from where the player stands.
    /// A room reaches only some of the next layer, so these are often narrow.
    /// </summary>
    public class PathSelectionController : MonoBehaviour
    {
        [SerializeField] Transform pathContainer;
        [SerializeField] PathButtonView pathButtonPrefab;
        [SerializeField] Text titleText;
        [SerializeField] Text infoText;
        [SerializeField] Text noticeText;
        [SerializeField] Button characterButton;
        [SerializeField] Button mapButton;
        [SerializeField] Button leaveButton;

        readonly List<PathButtonView> buttons = new List<PathButtonView>();

        GameSession Session { get { return GameSession.Instance; } }

        void Start()
        {
            GameSession session = Session;

            if (titleText != null) titleText.text = "Choose your path";
            if (infoText != null)
            {
                infoText.text = "Level " + session.Level + "    XP " + session.Experience + " / " + session.ExperienceForNextLevel +
                                "    Rooms explored " + session.RoomsExplored +
                                "\nHealth " + session.Stats.Health + " / " + session.Stats.MaxHealth +
                                "    Deck " + session.Decks[session.ActiveDeckIndex].Count + " cards";
            }

            if (noticeText != null)
            {
                if (session.ConsumeItemNotice()) noticeText.text = session.ItemNoticeText;
                else if (session.ConsumeMapExpiredNotice()) noticeText.text = "Your map has faded away.";
                else if (session.MapReveal != null && session.MapReveal.IsActive)
                    noticeText.text = "Your map reads the next " + GameSession.MapRevealLayers +
                                      " rooms. It fades after " + session.MapReveal.Remaining +
                                      (session.MapReveal.Remaining == 1 ? " more encounter." : " more encounters.");
                else noticeText.text = "Press M for your map, I for your character.";
            }

            if (characterButton != null) characterButton.onClick.AddListener(OnCharacter);
            if (mapButton != null) mapButton.onClick.AddListener(OnMap);
            if (leaveButton != null) leaveButton.onClick.AddListener(OnLeave);

            BuildExits();
        }

        void Update()
        {
            if (Hotkeys.CharacterPressed()) OnCharacter();
        }

        void BuildExits()
        {
            foreach (PathButtonView button in buttons)
            {
                if (button != null) Destroy(button.gameObject);
            }
            buttons.Clear();

            MapNode node = Session.CurrentNode;
            if (node == null || pathContainer == null || pathButtonPrefab == null) return;

            List<MapNode> exits = Session.Map.ExitsFrom(node.id);
            foreach (MapNode exit in exits)
            {
                PathButtonView view = Instantiate(pathButtonPrefab, pathContainer);
                view.Bind(exit, OnExitSelected);
                buttons.Add(view);
            }

            if (buttons.Count == 0 && titleText != null)
            {
                // Should not happen: every room is generated with a way onward.
                titleText.text = "No way forward. Use LEAVE DUNGEON to end the run.";
            }
        }

        void OnExitSelected(MapNode target)
        {
            if (target == null) return;
            Session.ChooseExit(target.id);
        }

        void OnCharacter()
        {
            GameSession session = Session;
            session.ReturnScene = GameSession.PathSelectionScene;
            session.LoadScene(GameSession.CharacterScene);
        }

        void OnMap()
        {
            MapOverlay.OpenIfAvailable();
        }

        void OnLeave()
        {
            Session.AbandonRun();
        }
    }
}

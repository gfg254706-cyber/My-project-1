using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>Main menu. Play generates a fresh dungeon and drops the player into the entrance room.</summary>
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] Button playButton;
        [SerializeField] Button characterButton;
        [SerializeField] Button quitButton;
        [SerializeField] Text statusText;
        [SerializeField] Text hintText;
        [SerializeField] Text lastRunText;

        GameSession Session { get { return GameSession.Instance; } }

        void Start()
        {
            if (playButton != null) playButton.onClick.AddListener(OnPlay);
            if (characterButton != null) characterButton.onClick.AddListener(OnCharacter);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuit);

            if (hintText != null) hintText.text = "Press I for your character menu during a run. Press M for the map.";
            RefreshStatus();
        }

        void Update()
        {
            if (Hotkeys.CharacterPressed()) OnCharacter();
        }

        void RefreshStatus()
        {
            if (statusText == null) return;

            GameSession session = Session;
            statusText.text = "Deck " + session.Decks[session.ActiveDeckIndex].Count + " cards    Level " + session.Level +
                              "    Health " + session.Stats.Health + " / " + session.Stats.MaxHealth;
        }

        public void OnPlay()
        {
            LastDeathReasonCleanup();
            Session.StartRun();
        }

        void LastDeathReasonCleanup()
        {
            if (lastRunText == null) return;

            string reason = Session.LastDeathReason;
            lastRunText.text = string.IsNullOrEmpty(reason) ? "" : "Last run ended: " + reason;
        }

        public void OnCharacter()
        {
            GameSession session = Session;
            session.ReturnScene = GameSession.MainMenuScene;
            session.LoadScene(GameSession.CharacterScene);
        }

        public void OnQuit()
        {
            Application.Quit();
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// One attribute row in the character menu. The level button is enabled only when the player can
    /// afford the next level.
    /// </summary>
    public class SkillRowView : MonoBehaviour
    {
        [SerializeField] Text nameText;
        [SerializeField] Text valueText;
        [SerializeField] Text costText;
        [SerializeField] Button levelButton;
        [SerializeField] Text levelButtonLabel;

        StatType stat;
        CharacterMenuController owner;

        void Awake()
        {
            if (levelButton != null) levelButton.onClick.AddListener(OnLevelUp);
        }

        void OnDestroy()
        {
            if (levelButton != null) levelButton.onClick.RemoveListener(OnLevelUp);
        }

        public void Bind(StatType type, CharacterMenuController controller)
        {
            stat = type;
            owner = controller;

            if (nameText != null) nameText.text = type.ToString().ToUpperInvariant();
            Refresh();
        }

        public void Refresh()
        {
            GameSession session = GameSession.Instance;
            if (session == null || session.Stats == null) return;

            int baseValue = session.Stats.GetBaseStat(stat);
            int bonus = session.Stats.GetItemStat(stat);
            int total = session.Stats.GetStat(stat);

            if (valueText != null)
            {
                string value = total.ToString();
                if (bonus != 0) value += "  (" + baseValue + " + " + bonus + " item)";
                valueText.text = value;
            }

            int cost = session.ExperienceForNextLevel;
            bool affordable = session.Experience >= cost;

            if (costText != null) costText.text = cost + " XP";
            if (levelButton != null) levelButton.interactable = affordable;
            if (levelButtonLabel != null) levelButtonLabel.text = affordable ? "+" : "-";
        }

        void OnLevelUp()
        {
            if (owner != null) owner.LevelUpStat(stat);
        }
    }
}

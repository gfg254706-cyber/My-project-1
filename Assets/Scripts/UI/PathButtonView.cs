using System;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>One route out of the room the player is standing in, taken from the dungeon map's edges.</summary>
    public class PathButtonView : MonoBehaviour
    {
        [SerializeField] Text iconText;
        [SerializeField] Text typeText;
        [SerializeField] Text descriptionText;
        [SerializeField] Button button;

        MapNode node;
        Action<MapNode> onSelected;

        void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(OnClicked);
        }

        void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(OnClicked);
        }

        public void Bind(MapNode target, Action<MapNode> callback)
        {
            node = target;
            onSelected = callback;

            if (node == null) return;
            if (iconText != null) iconText.text = RoomTypes.Icon(node.roomType);
            if (typeText != null) typeText.text = RoomTypes.Label(node.roomType);
            if (descriptionText != null) descriptionText.text = RoomTypes.Description(node.roomType);
        }

        void OnClicked()
        {
            if (onSelected != null) onSelected(node);
        }
    }
}

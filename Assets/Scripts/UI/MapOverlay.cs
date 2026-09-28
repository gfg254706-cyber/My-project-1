using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// The dungeon map, opened with M. It draws only the layers a vantage room revealed, marking where
    /// the player currently stands. When the map has expired it does nothing.
    /// </summary>
    public class MapOverlay : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] GameObject panel;
        [SerializeField] RectTransform graphContainer;
        [SerializeField] Text titleText;
        [SerializeField] Text statusText;
        [SerializeField] Text hintText;
        [SerializeField] Button closeButton;
        [SerializeField] Button openButton;

        const float ColumnWidth = 240f;
        const float RowHeight = 108f;
        static readonly Vector2 NodeSize = new Vector2(168f, 76f);

        static MapOverlay instance;
        static Font runtimeFont;

        static readonly Color CombatColor = new Color(0.55f, 0.22f, 0.24f, 1f);
        static readonly Color EventColor = new Color(0.34f, 0.26f, 0.52f, 1f);
        static readonly Color VantageColor = new Color(0.17f, 0.40f, 0.44f, 1f);
        static readonly Color ExitColor = new Color(0.20f, 0.44f, 0.28f, 1f);
        static readonly Color BossColor = new Color(0.48f, 0.12f, 0.16f, 1f);
        static readonly Color VisitedTint = new Color(0.26f, 0.27f, 0.32f, 1f);
        static readonly Color CurrentColor = new Color(0.78f, 0.66f, 0.28f, 1f);

        void Awake()
        {
            instance = this;

            if (panel != null) panel.SetActive(false);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (openButton != null) openButton.onClick.AddListener(Toggle);
            if (titleText != null) titleText.text = "DUNGEON MAP";
            if (hintText != null) hintText.text = "M closes the map.   I opens your character menu.";
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void Update()
        {
            if (Hotkeys.MapPressed()) Toggle();
        }

        public static void OpenIfAvailable()
        {
            if (instance != null) instance.Open();
        }

        public void Toggle()
        {
            if (panel == null) return;
            if (panel.activeSelf) Close();
            else Open();
        }

        /// <summary>Opens the map when one is active. With no map, an expired one included, this does nothing.</summary>
        public void Open()
        {
            GameSession session = GameSession.Instance;
            if (session.MapReveal == null || !session.MapReveal.IsActive) return;
            if (panel == null || graphContainer == null) return;

            panel.SetActive(true);
            Rebuild(session);
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
        }

        // ------------------------------------------------------------------ drawing

        void Rebuild(GameSession session)
        {
            ClearGraph();

            DungeonMap map = session.Map;
            MapReveal reveal = session.MapReveal;
            if (map == null) return;

            // The map reads the next few rooms from wherever the player stands, not a slice fixed when
            // the map was picked up, so the window moves with them until the map fades.
            int currentLayer = session.CurrentNode != null ? session.CurrentNode.layer : 0;
            int from = Mathf.Clamp(currentLayer, 0, map.LayerCount - 1);
            int to = Mathf.Min(map.LayerCount - 1, from + GameSession.MapRevealLayers);
            if (to < from)
            {
                if (statusText != null) statusText.text = "Nothing is revealed.";
                return;
            }

            var positions = new Dictionary<int, Vector2>();
            float width = (to - from) * ColumnWidth;

            for (int layer = from; layer <= to; layer++)
            {
                List<MapNode> nodes = map.NodesInLayer(layer);
                for (int i = 0; i < nodes.Count; i++)
                {
                    positions[nodes[i].id] = new Vector2(
                        (layer - from) * ColumnWidth - width * 0.5f,
                        (nodes.Count - 1) * RowHeight * 0.5f - i * RowHeight);
                }
            }

            // Edges first so the room boxes sit on top of the lines.
            for (int layer = from; layer < to; layer++)
            {
                foreach (MapNode node in map.NodesInLayer(layer))
                {
                    Vector2 start;
                    if (!positions.TryGetValue(node.id, out start)) continue;

                    foreach (int targetId in node.outgoing)
                    {
                        MapNode target = map.Get(targetId);
                        Vector2 end;
                        if (target == null || target.layer != layer + 1) continue;
                        if (!positions.TryGetValue(targetId, out end)) continue;

                        DrawEdge(start, end);
                    }
                }
            }

            for (int layer = from; layer <= to; layer++)
            {
                foreach (MapNode node in map.NodesInLayer(layer))
                {
                    Vector2 position;
                    if (positions.TryGetValue(node.id, out position)) DrawNode(node, position, session.Map.CurrentNodeId);
                }
            }

            if (statusText != null)
            {
                statusText.text = "Showing the next " + (to - from) +
                                  (to - from == 1 ? " room" : " rooms") +
                                  "    the map fades after " + reveal.Remaining +
                                  (reveal.Remaining == 1 ? " more encounter" : " more encounters");
            }
        }

        void DrawNode(MapNode node, Vector2 position, int currentNodeId)
        {
            bool isCurrent = node.id == currentNodeId;

            var go = new GameObject("Node_" + node.id, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(graphContainer, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = NodeSize;
            rect.anchoredPosition = position;

            Image image = go.GetComponent<Image>();
            image.color = isCurrent ? CurrentColor : (node.visited ? VisitedTint : ColorFor(node.roomType));

            Text icon = CreateText(go.transform, "Icon", RoomTypes.Icon(node.roomType), 30, TextAnchor.MiddleCenter, Color.white);
            SetStretch(icon.rectTransform, 0f, 0f, 0f, 18f);

            Text caption = CreateText(go.transform, "Label", RoomTypes.Label(node.roomType), 14, TextAnchor.LowerCenter, new Color(0.94f, 0.95f, 1f));
            SetStretch(caption.rectTransform, 0f, 0f, 20f, 4f);

            if (isCurrent)
            {
                Text marker = CreateText(go.transform, "Marker", "YOU ARE HERE", 12, TextAnchor.UpperCenter, new Color(0.16f, 0.14f, 0.05f));
                SetStretch(marker.rectTransform, 0f, 0f, 2f, 30f);
            }
        }

        void DrawEdge(Vector2 from, Vector2 to)
        {
            float halfWidth = NodeSize.x * 0.5f;
            Vector2 start = from + new Vector2(halfWidth, 0f);
            Vector2 end = to - new Vector2(halfWidth, 0f);
            Vector2 direction = end - start;

            var go = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(graphContainer, false);

            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = start;
            rect.sizeDelta = new Vector2(direction.magnitude, 3f);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);

            Image image = go.GetComponent<Image>();
            image.color = new Color(0.55f, 0.58f, 0.66f, 0.85f);
            image.raycastTarget = false;

            // Edges render behind the boxes.
            go.transform.SetAsFirstSibling();
        }

        void ClearGraph()
        {
            if (graphContainer == null) return;

            for (int i = graphContainer.childCount - 1; i >= 0; i--)
            {
                GameObject child = graphContainer.GetChild(i).gameObject;
                child.SetActive(false);
                child.transform.SetParent(null, false);
                Destroy(child);
            }
        }

        static Color ColorFor(RoomType type)
        {
            switch (type)
            {
                case RoomType.Combat: return CombatColor;
                case RoomType.Event: return EventColor;
                case RoomType.Vantage: return VantageColor;
                case RoomType.Boss: return BossColor;
                default: return ExitColor;
            }
        }

        // ------------------------------------------------------------------ runtime ui helpers

        static Text CreateText(Transform parent, string name, string content, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            Text text = go.AddComponent<Text>();
            text.font = RuntimeFont();
            text.fontSize = size;
            text.text = content;
            text.alignment = anchor;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static void SetStretch(RectTransform rect, float left, float bottom, float top, float right)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        static Font RuntimeFont()
        {
            if (runtimeFont != null) return runtimeFont;

            runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (runtimeFont == null) runtimeFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return runtimeFont;
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// The hover tooltip for a card: it names the card and lists what each of its keywords does.
    ///
    /// It lives on its own canvas with a high sorting order, so it is never clipped by a scroll view or
    /// hidden behind an overlay, and every graphic on it has raycasting switched off so it can never
    /// steal the pointer away from the card that is showing it (which would make it flicker).
    ///
    /// Placement is clamped rather than fixed: it sits beside the card, on the right of it, and only mirrors
    /// to the left when there is no room there. Either way it is then pushed back inside the screen on both
    /// axes, so it stays fully visible even for a card in the corner. Sitting beside the card rather than
    /// over it is what keeps the card's own name and art readable while the tooltip is open.
    /// </summary>
    public class CardTooltip : MonoBehaviour
    {
        [SerializeField] Canvas canvas;
        [SerializeField] RectTransform panel;
        [SerializeField] TMP_Text titleText;
        [SerializeField] TMP_Text bodyText;

        [Header("Placement")]
        [Tooltip("Distance kept between the tooltip and the card it describes.")]
        [SerializeField] float gap = 16f;
        [Tooltip("Distance kept between the tooltip and the edge of the screen.")]
        [SerializeField] float screenPadding = 12f;

        public static CardTooltip Instance { get; private set; }

        /// <summary>
        /// The view currently showing this tooltip, so one that is being left behind cannot hide the
        /// tooltip a neighbouring card or relic has just opened. A Component rather than a CardView,
        /// because the shop's relics use the same tooltip without being cards.
        /// </summary>
        public Component Owner { get; private set; }

        /// <summary>
        /// The tooltip for this scene, created the first time anything asks for it.
        ///
        /// Cards and relics share the one instance, so two things can never put up two tooltips at once
        /// and the owner guard above can always tell who is speaking.
        /// </summary>
        public static CardTooltip Shared(CardTooltip prefab)
        {
            if (Instance == null && prefab != null)
            {
                Instance = Instantiate(prefab);
                Instance.name = "CardTooltip";
            }

            return Instance;
        }

        /// <summary>
        /// The box the tooltip is placed against, remembered so it can keep following the card after it has
        /// been shown.
        /// </summary>
        RectTransform anchor;

        void Awake()
        {
            if (canvas == null) canvas = GetComponent<Canvas>();
            Instance = this;
            Hide();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool IsOpen { get { return panel != null && panel.gameObject.activeSelf; } }

        public void Hide()
        {
            Owner = null;
            anchor = null;
            if (panel != null) panel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Keeps the tooltip beside its card for as long as it is open. The hand fan straightens and lifts the
        /// hovered card as it eases into place, so a tooltip placed once on hover would be left behind and the
        /// card would rise into it, ending up under its own tooltip.
        /// </summary>
        void LateUpdate()
        {
            if (IsOpen && anchor != null) Place(anchor);
        }

        /// <summary>Shows the keyword glossary for a card, anchored beside it and kept on screen.</summary>
        public void Show(CardData card, RectTransform source, CardView owner)
        {
            if (card == null) return;
            Show(card.cardName, CardKeywords.BuildTooltipBody(card), source, owner);
        }

        /// <summary>
        /// Shows a tooltip beside whatever asked for it. A relic writes its own title and body, so the
        /// card-specific overload above is the only part of this that has to know what a CardData is.
        /// </summary>
        public void Show(string title, string body, RectTransform source, Component owner)
        {
            if (source == null || panel == null) return;

            Owner = owner;
            anchor = source;

            if (titleText != null) titleText.text = title;
            if (bodyText != null) bodyText.text = body;

            panel.gameObject.SetActive(true);

            // The panel is sized by its own layout group and fitter, so it has to be rebuilt before its
            // height is known and it can be placed.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

            Place(source);
        }

        void Place(RectTransform source)
        {
            RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            if (canvasRect == null) return;

            float scale = canvas.scaleFactor <= 0f ? 1f : canvas.scaleFactor;
            float canvasWidth = canvasRect.rect.width;
            float canvasHeight = canvasRect.rect.height;

            float width = panel.rect.width;
            float height = panel.rect.height;

            // The card's box, converted from world space into canvas units.
            var corners = new Vector3[4];
            source.GetWorldCorners(corners);

            // 0 is the bottom left corner, 2 the top right.
            Vector2 cardBottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]) / scale;
            Vector2 cardTopRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]) / scale;

            // A screen too narrow to hold the tooltip beside a card collapses the horizontal range onto the
            // left edge, so the clamps below can never invert and put it off screen.
            float widestX = canvasWidth - width - screenPadding;
            if (widestX < screenPadding) widestX = screenPadding;

            // Horizontal: just past the card's right edge, so the tooltip never covers the card it is
            // describing. A card close to the right edge of the screen has no room there, so rather than
            // clamp back on top of the card it mirrors to the card's left instead.
            float x = cardTopRight.x + gap;
            if (x > widestX)
            {
                float mirrored = cardBottomLeft.x - gap - width;
                x = mirrored < screenPadding ? screenPadding : mirrored;
            }
            x = Mathf.Clamp(x, screenPadding, widestX);

            // Vertical: level with the top of the card, then pulled back so the box stays on screen.
            // The pivot is the top left corner, so y is the top edge and the box hangs below it.
            float lowestY = height + screenPadding;
            float highestY = canvasHeight - screenPadding;
            float y = cardTopRight.y;
            y = lowestY > highestY ? highestY : Mathf.Clamp(y, lowestY, highestY);

            panel.anchoredPosition = new Vector2(x, y);
        }
    }
}

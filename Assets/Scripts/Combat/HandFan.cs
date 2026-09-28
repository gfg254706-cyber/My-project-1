using System.Collections.Generic;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// Lays the hand out as a fan: the cards overlap, the outer ones sit lower and lean outwards, and the
    /// card under the pointer straightens, rises out of the row and pushes its neighbours aside so it can
    /// be read.
    ///
    /// This owns the cards' anchoredPosition and localRotation; CardHoverEffect owns their scale. Nothing
    /// here reorders siblings and there is no layout group, so hovering can never reflow the strip under
    /// the pointer. That reflow is what a layout group does when a card is re-sorted, and it is what caused
    /// the hover jitter.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class HandFan : MonoBehaviour
    {
        [Header("Fan")]
        [Tooltip("Distance between neighbouring card centres. Below the card's width, so the cards overlap.")]
        [SerializeField] float spacing = 108f;

        [Tooltip("How much lower the cards at the ends of the fan sit than the one in the middle.")]
        [SerializeField] float arcDrop = 30f;

        [Tooltip("How far the cards at the ends lean, in degrees. The middle card stands upright.")]
        [SerializeField] float maxTilt = 9f;

        [Tooltip("Nudges the whole fan up or down within the strip.")]
        [SerializeField] float baseline = -6f;

        [Header("Hover")]
        [Tooltip("How far the hovered card rises out of the fan.")]
        [SerializeField] float hoverLift = 40f;

        [Tooltip("How far the hovered card's neighbours are pushed aside, easing off with distance. This has " +
                 "to clear the hovered card's own grown width, or the card beside it covers its text.")]
        [SerializeField] float hoverSpread = 118f;

        [Tooltip("How quickly a card eases towards where it belongs. Higher is snappier.")]
        [SerializeField] float speed = 14f;

        /// <summary>
        /// One card's easing state. Everything in the fan bends around the hover weight: a card's own weight
        /// straightens and lifts it, and its neighbours read it to part out of the way.
        /// </summary>
        class FanCard
        {
            public CardView view;
            public RectTransform rect;
            public CardHoverEffect hover;
            public float weight;
        }

        readonly List<FanCard> cards = new List<FanCard>();

        IReadOnlyList<CardView> hand;

        /// <summary>
        /// Hands the fan the cards that are in the hand, in order. The strip's own children cannot be used:
        /// the hand is rebuilt by destroying the old cards and creating new ones, and a destroyed card
        /// lingers in the hierarchy until the end of the frame, so for one frame the children would be the
        /// cards that are being thrown away as well as their replacements.
        /// </summary>
        public void SetCards(IReadOnlyList<CardView> views)
        {
            hand = views;
        }

        void LateUpdate()
        {
            Sync();
            if (cards.Count == 0) return;

            float ease = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);

            // The weights are eased first, because every position and tilt below is read from them. One
            // pointer move then animates the whole fan rather than each card on its own.
            for (int i = 0; i < cards.Count; i++)
            {
                float target = cards[i].hover != null && cards[i].hover.IsHovered ? 1f : 0f;
                cards[i].weight = Mathf.Lerp(cards[i].weight, target, ease);
                if (Mathf.Abs(cards[i].weight - target) < 0.002f) cards[i].weight = target;
            }

            float span = (cards.Count - 1) * 0.5f;

            for (int i = 0; i < cards.Count; i++)
            {
                FanCard card = cards[i];

                // -1 at the left end of the fan, +1 at the right end, 0 in the middle.
                float t = span <= 0f ? 0f : (i - span) / span;

                float x = t * spacing * span;
                float y = baseline + arcDrop * (0.5f - t * t);

                // Neighbours part away from whichever card is hovered, falling off with distance, which is
                // what gives the hovered card room from the rest of the hand.
                for (int j = 0; j < cards.Count; j++)
                {
                    if (j == i) continue;
                    float weight = cards[j].weight;
                    if (weight <= 0f) continue;
                    x += (i < j ? -1f : 1f) * hoverSpread * weight / Mathf.Abs(i - j);
                }

                y += hoverLift * card.weight;

                // Upright while hovered, leaning with the fan otherwise.
                float tilt = -t * maxTilt * (1f - card.weight);

                card.rect.anchoredPosition = new Vector2(x, y);
                card.rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
            }
        }

        /// <summary>
        /// Matches the fan's state against the hand. Cards are only dropped or added when the hand really
        /// changed, so a card that is still in hand keeps its weight instead of snapping upright.
        /// </summary>
        void Sync()
        {
            int count = hand != null ? hand.Count : 0;

            if (cards.Count == count)
            {
                bool same = true;
                for (int i = 0; i < count; i++)
                {
                    if (cards[i].view != hand[i]) { same = false; break; }
                }
                if (same) return;
            }

            var previous = new List<FanCard>(cards);
            cards.Clear();

            for (int i = 0; i < count; i++)
            {
                CardView view = hand[i];
                if (view == null) continue;

                FanCard kept = null;
                foreach (FanCard old in previous)
                {
                    if (old.view == view) { kept = old; break; }
                }

                cards.Add(kept ?? NewCard(view));
            }
        }

        static FanCard NewCard(CardView view)
        {
            var rect = (RectTransform)view.transform;

            // The fan measures from the middle of the strip, so every card is anchored there whatever it was
            // last told. The card's own pivot is its centre, which is what puts the card on the fan point.
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);

            return new FanCard
            {
                view = view,
                rect = rect,
                hover = view.GetComponent<CardHoverEffect>()
            };
        }
    }
}

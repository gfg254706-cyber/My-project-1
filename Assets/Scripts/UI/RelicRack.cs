using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// Lays the merchant's relics out as one straight line, and gives the one under the pointer room to be
    /// read: it rises, grows, leans left, and its neighbours slide away from it while the rest of the shop
    /// goes dark.
    ///
    /// This owns the icons' anchoredPosition and localRotation; CardHoverEffect owns their scale. There is
    /// no layout group and nothing is ever reordered, because reflowing the row under the pointer is what
    /// made the combat hand jitter: the row shifted out from under the mouse and the hover flickered.
    ///
    /// The lean is leftward on purpose. The tooltip opens to the icon's right, so leaning away from it keeps
    /// the icon from crowding the text it has just opened.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class RelicRack : MonoBehaviour
    {
        [Header("Rack")]
        [Tooltip("Distance between neighbouring icon centres.")]
        [SerializeField] float spacing = 150f;

        [Tooltip("Nudges the whole line up or down inside the rack.")]
        [SerializeField] float baseline;

        [Header("Hover")]
        [Tooltip("How far the hovered icon rises out of the line.")]
        [SerializeField] float hoverLift = 30f;

        [Tooltip("How far its neighbours are pushed aside, easing off with distance. This has to clear the " +
                 "hovered icon's own grown width, or the icon beside it covers the tooltip's anchor.")]
        [SerializeField] float hoverSpread = 96f;

        [Tooltip("Degrees the hovered icon leans left, away from the tooltip opening on its right.")]
        [SerializeField] float hoverBend = 4f;

        [Tooltip("How far the icons that are not being read are faded, so the one in hand stands out.")]
        [Range(0f, 1f)]
        [SerializeField] float dimmedAlpha = 0.45f;

        [Tooltip("How quickly an icon eases towards where it belongs. Higher is snappier.")]
        [SerializeField] float speed = 14f;

        [Header("Inspect")]
        [Tooltip("Darkens the rest of the shop while a relic is being read, so the value is the only thing lit.")]
        [SerializeField] Image scrim;

        [Range(0f, 1f)]
        [SerializeField] float scrimAlpha = 0.74f;

        [Tooltip("How quickly the dark comes and goes. Faded rather than switched, so it reads as one gesture.")]
        [SerializeField] float scrimSpeed = 12f;

        readonly List<RelicIconView> icons = new List<RelicIconView>();
        float[] weights = new float[0];
        float scrimWeight;

        /// <summary>
        /// Hands the rack the icons that are actually on it. The rack's own children cannot be used: the
        /// icons are written into the scene by the builder and switched off when the shelf is short, and an
        /// icon that is off must not be spaced as though it were there.
        /// </summary>
        public void SetIcons(IReadOnlyList<RelicIconView> views)
        {
            icons.Clear();

            if (views != null)
            {
                foreach (RelicIconView view in views)
                {
                    if (view != null) icons.Add(view);
                }
            }

            weights = new float[icons.Count];

            // Placed now rather than on the next LateUpdate, so the rack is right on the frame it is filled
            // instead of sliding into position in front of the player.
            Place(0f);
        }

        void OnDisable()
        {
            // The shop closes on a hover as often as not, so nothing may be left faded or bent behind it.
            weights = new float[icons.Count];
            scrimWeight = 0f;

            for (int i = 0; i < icons.Count; i++)
            {
                if (icons[i] == null) continue;

                icons[i].SetDim(1f);
                icons[i].Bend(0f);
            }

            if (scrim != null) scrim.color = new Color(scrim.color.r, scrim.color.g, scrim.color.b, 0f);
        }

        void LateUpdate()
        {
            if (icons.Count == 0) return;

            float ease = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);

            // The weights ease first, because every position and lean below reads them. One pointer move
            // then animates the whole rack rather than each relic on its own.
            for (int i = 0; i < icons.Count; i++)
            {
                bool hovered = icons[i] != null && icons[i].Hover != null && icons[i].Hover.IsHovered;
                float target = hovered ? 1f : 0f;

                weights[i] = Mathf.Lerp(weights[i], target, ease);
                if (Mathf.Abs(weights[i] - target) < 0.002f) weights[i] = target;
            }

            // How strongly anything on the rack is being read, which is what drives the dark.
            float focus = 0f;
            for (int i = 0; i < weights.Length; i++) focus = Mathf.Max(focus, weights[i]);

            Place(focus);

            scrimWeight = Mathf.Lerp(scrimWeight, focus, 1f - Mathf.Exp(-scrimSpeed * Time.unscaledDeltaTime));
            if (Mathf.Abs(scrimWeight - focus) < 0.002f) scrimWeight = focus;

            if (scrim != null) scrim.color = new Color(0f, 0f, 0f, scrimAlpha * scrimWeight);
        }

        /// <summary>Writes every icon's place, lean and fade from the current weights.</summary>
        void Place(float focus)
        {
            float span = (icons.Count - 1) * 0.5f;

            for (int i = 0; i < icons.Count; i++)
            {
                RelicIconView icon = icons[i];
                if (icon == null) continue;

                float x = (i - span) * spacing;
                float y = baseline;

                // Neighbours part away from whichever icon is being read, falling off with distance, which
                // is what opens the gap the tooltip needs beside it.
                for (int j = 0; j < icons.Count; j++)
                {
                    if (j == i) continue;

                    float weight = weights[j];
                    if (weight <= 0f) continue;

                    x += (i < j ? -1f : 1f) * hoverSpread * weight / Mathf.Abs(i - j);
                }

                y += hoverLift * weights[i];

                icon.Place(new Vector2(x, y));
                icon.Bend(hoverBend * weights[i]);

                // Full strength for the one being read; everything else is faded by however strongly one of
                // its neighbours has taken the light.
                icon.SetDim(Mathf.Lerp(1f, dimmedAlpha, focus * (1f - weights[i])));
            }
        }
    }
}

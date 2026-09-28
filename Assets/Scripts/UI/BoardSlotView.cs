using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// One square of the board: a construct in play, drawn as the placeholder standing in for its card art.
    ///
    /// The square carries no text. A construct's number and its wording belong together in the tooltip, which
    /// is what the pointer brings up, so a row of squares the player is not reading says nothing but "these
    /// are the things still running". Everything about the construct - what it does at the player's current
    /// attributes, and how many of their turns it is still good for - is one hover away.
    ///
    /// It answers to the same hover contract as a card, so the same lift and the same shared tooltip drive
    /// both without either knowing about the other.
    /// </summary>
    public class BoardSlotView : MonoBehaviour, IHoverView, IPointerClickHandler
    {
        [SerializeField] Image icon;

        [Tooltip("Shared with the cards. Created on demand the first time anything is hovered.")]
        [SerializeField] CardTooltip tooltipPrefab;

        CardData card;
        int turnsLeft;

        /// <summary>
        /// What the construct standing here has stored up, and how hot it has got. Read off the card in play
        /// rather than off the card asset, because these are that one construct's own numbers.
        /// </summary>
        int charges;
        int heat;

        /// <summary>Where this square sits on the board, so a click can name the construct it is offering.</summary>
        int index = -1;

        CombatManager owner;
        bool sacrificeable;

        RectTransform rect;

        /// <summary>The construct standing here, or null when the square is empty.</summary>
        public CardData Card { get { return card; } }

        /// <summary>
        /// Shows a construct in this square, or empties it.
        ///
        /// Everything shown comes from the board rather than from the card: a duration has been counting down
        /// since the card was played, and Charges and Heat are that one construct's own. The manager and the
        /// position are carried so that a square offering the construct up can name what it is offering, and
        /// the flag is decided by the board, because whether a construct may be sacrificed is its own rule.
        /// </summary>
        public void Bind(CardData value, int turns, int slotIndex, CombatManager manager,
                         bool canSacrifice, int storedCharges, int storedHeat)
        {
            card = value;
            turnsLeft = turns;
            index = slotIndex;
            owner = manager;
            sacrificeable = canSacrifice && value != null;
            charges = storedCharges;
            heat = storedHeat;

            if (icon != null) icon.enabled = card != null;

            // A construct can run out its duration while the pointer is resting on it, which would otherwise
            // leave its tooltip up over a square that no longer stands for anything.
            if (card == null)
            {
                sacrificeable = false;
                HideTooltip();
            }
        }

        /// <summary>
        /// Giving the construct up by hand, which is what Fuse's detonation and Forbidden Engine's payout are
        /// both waiting for. Only a square whose construct is sacrificial answers a click at all, so a running
        /// construct cannot be destroyed by a stray one.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!sacrificeable || owner == null) return;

            owner.SacrificeBoardCard(index);
        }

        // ---------------------------------------------------------------- hover

        /// <summary>Called by the square's hover effect. Brings up the card's own tooltip and its clock.</summary>
        public void SetHovered(bool hovered)
        {
            if (card == null)
            {
                HideTooltip();
                return;
            }

            if (!hovered)
            {
                HideTooltip();
                return;
            }

            CardTooltip tooltip = CardTooltip.Shared(tooltipPrefab);
            if (tooltip == null) return;

            if (rect == null) rect = (RectTransform)transform;
            tooltip.Show(card.cardName, Body(), rect, this);
        }

        /// <summary>
        /// Nothing to change. A card shows different numbers at rest and under the pointer because it prints
        /// one of them; a board square prints none, so being lifted tells it nothing new.
        /// </summary>
        public void SetLifted(bool lifted) { }

        /// <summary>
        /// What the construct does, and how much longer it does it for.
        ///
        /// The effect is written at the player's attributes because a construct's number moves with them, and
        /// the clock is stated as a count rather than a bar because that is the whole of what a duration is.
        ///
        /// The card's own text is not appended as well. A card's description field is its summary line at the
        /// printed value, so it would repeat the line above in the numbers the player has already outgrown.
        /// </summary>
        string Body()
        {
            var text = new System.Text.StringBuilder();

            text.Append(card.BuildDescriptionOnBoard(Stats));

            // A construct's wording describes the rules it runs on, not the state it is in, so what it has
            // stored up and how hot it has become are stated here: a Fuse at four Charges is a different
            // thing from the same card at one, and the square itself prints nothing.
            if (card.construct != ConstructKind.None)
            {
                text.Append('\n').Append('\n');
                text.Append("Charges: ").Append(charges);
                if (heat > 0) text.Append("        Heat: ").Append(heat);
            }

            text.Append('\n').Append('\n');
            text.Append(turnsLeft > 0
                ? "Turns left: " + turnsLeft
                : "Stays in play until the fight ends");

            if (sacrificeable) text.Append('\n').Append("Click to sacrifice it.");

            return text.ToString();
        }

        void HideTooltip()
        {
            CardTooltip open = CardTooltip.Instance;
            if (open != null && open.Owner == (Component)this) open.Hide();
        }

        /// <summary>The player's attributes, so the tooltip quotes the number the construct is producing.</summary>
        static PlayerStats Stats
        {
            get
            {
                GameSession session = GameSession.Instance;
                return session != null ? session.Stats : null;
            }
        }
    }
}

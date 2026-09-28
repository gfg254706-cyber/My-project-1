using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// One card on the merchant's shelf, drawn as the card itself rather than as a row describing it.
    ///
    /// A card is already a readable object: its cost, its name, its effect and its number are on it. The
    /// shop therefore instantiates the same prefab the hand, the deck grid and the reward screen use, so
    /// the thing on the shelf is the thing that joins the deck. Reading it and choosing it are the same act
    /// here as everywhere else.
    ///
    /// The price sits underneath rather than on the card. A card face has no room for a shop price, and
    /// printing one on it would put a number on the same object that means nothing inside a fight.
    /// </summary>
    public class ShopCardView : MonoBehaviour
    {
        [SerializeField] CardView cardView;
        [SerializeField] Text priceText;

        CardData card;
        ShopPanel owner;

        /// <summary>The card on this shelf slot, or null when the slot is empty.</summary>
        public CardData Card { get { return card; } }

        /// <summary>
        /// Puts a card on the shelf. A slot is re-bound every time the shelf changes, so the click handler
        /// is dropped before it is added again: without that, one purchase would be paid for once per
        /// redraw, and the purse would be charged the price of a card the player only bought once.
        /// </summary>
        public void BindCard(CardData value, PlayerStats stats, ShopPanel panel)
        {
            card = value;
            owner = panel;

            if (cardView != null)
            {
                cardView.Chosen -= OnChosen;
            }

            if (value == null)
            {
                if (priceText != null) priceText.text = "";
                return;
            }

            if (cardView != null)
            {
                cardView.BindChoice(value, stats);
                cardView.Chosen += OnChosen;
            }

            int price = ShopPricing.CardPrice(value);
            bool affordable = GameSession.Instance != null && GameSession.Instance.Gold >= price;

            // A card the player cannot pay for is not a choice, so it is closed off the same way an offer
            // already taken is on the reward screen. The price still shows what it would cost.
            if (cardView != null) cardView.SetChoiceEnabled(affordable);

            if (priceText != null)
            {
                priceText.text = price + " g";
                priceText.color = affordable
                    ? new Color(0.98f, 0.86f, 0.45f, 1f)
                    : new Color(0.55f, 0.50f, 0.40f, 1f);
            }
        }

        void OnDestroy()
        {
            if (cardView != null) cardView.Chosen -= OnChosen;
        }

        void OnChosen(CardView view)
        {
            if (owner != null) owner.Buy(this);
        }
    }
}

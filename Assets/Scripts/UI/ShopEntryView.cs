using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// One thing for sale on one of the shop's three racks. The same view serves a card, a relic and a
    /// piece of equipment, because all three are a name, a line about it and a price.
    ///
    /// A row is a fixed object in the shop scene rather than something instantiated from a prefab. The
    /// racks are a known size, so the shop holds five card rows, three relic rows and two equipment rows
    /// and simply switches off the ones it is not using. Nothing is built at runtime, so nothing can be
    /// left behind in the layout when a shelf empties.
    /// </summary>
    public class ShopEntryView : MonoBehaviour
    {
        [SerializeField] Button buyButton;
        [SerializeField] Text nameText;
        [SerializeField] Text detailText;
        [SerializeField] Text priceText;

        CardData card;
        ItemData item;
        ItemInstance instance;
        ShopPanel owner;

        /// <summary>The card on this row, or null when the row is showing a relic or equipment.</summary>
        public CardData Card { get { return card; } }

        /// <summary>The relic or equipment on this row, or null when the row is showing a card.</summary>
        public ItemData Item { get { return item; } }

        /// <summary>
        /// The instance behind an item row, which is what carries a relic's roll. Buying goes through this
        /// rather than through the definition, so the roll the row shows survives the purchase.
        /// </summary>
        public ItemInstance Instance { get { return instance; } }

        void Awake()
        {
            if (buyButton != null) buyButton.onClick.AddListener(OnBuy);
        }

        void OnDestroy()
        {
            if (buyButton != null) buyButton.onClick.RemoveListener(OnBuy);
        }

        public void BindCard(CardData value, PlayerStats stats, ShopPanel panel)
        {
            card = value;
            item = null;
            owner = panel;

            if (value == null) return;

            if (nameText != null) nameText.text = value.cardName;

            // One line: what it costs to play, and what it does at the player's current attributes.
            if (detailText != null)
            {
                string resource = value.costType == CardCostType.Mana ? "mana" : "energy";
                detailText.text = "Costs " + value.cost + " " + resource + "   -   " +
                                  value.Summary(value.ValueFor(stats));
            }

            if (priceText != null) priceText.text = ShopPricing.CardPrice(value) + " g";
            RefreshAffordable(ShopPricing.CardPrice(value));
        }

        public void BindItem(ItemInstance value, ShopPanel panel)
        {
            instance = value;
            item = value != null ? value.data : null;
            card = null;
            owner = panel;

            if (item == null) return;

            if (nameText != null)
            {
                // A rare item is starred and drawn in gold, so it reads as special even in a list.
                nameText.text = ItemRarityStyle.Decorate(item);
                nameText.color = ItemRarityStyle.ColorFor(item.rarity);
            }

            if (detailText != null)
            {
                // A relic is labelled by its tier, which is the real measure of it: the rarity it borrows
                // for colour only has two values and cannot tell a Tier I from a Tier III. Its rolled stats
                // are shown here because the roll is what the player is actually choosing between, and the
                // negatives are shown with it rather than hidden.
                string kind = value.relic != null
                    ? RelicInstance.TierLabel(value.relic.tier) + " RELIC"
                    : (item.relic ? "RELIC" : item.SlotLabel());

                detailText.text = "[" + kind + "]   " + value.BonusSummary();
            }

            if (priceText != null) priceText.text = ShopPricing.ItemPrice(item) + " g";
            RefreshAffordable(ShopPricing.ItemPrice(item));
        }

        /// <summary>
        /// A row the player cannot pay for is shown greyed and cannot be pressed, so the shop answers
        /// "can I have this?" before the click rather than after it.
        /// </summary>
        void RefreshAffordable(int price)
        {
            bool affordable = GameSession.Instance != null && GameSession.Instance.Gold >= price;

            if (buyButton != null) buyButton.interactable = affordable;
            if (priceText != null)
            {
                priceText.color = affordable
                    ? new Color(0.98f, 0.86f, 0.45f, 1f)
                    : new Color(0.55f, 0.50f, 0.40f, 1f);
            }
        }

        void OnBuy()
        {
            if (owner != null) owner.Buy(this);
        }
    }
}

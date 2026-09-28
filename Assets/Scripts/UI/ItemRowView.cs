using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// One entry in the character menu. The same row serves the bag, the body and the merchant's sell
    /// list: an equipment row names its slot, a bag row shows the item loose, and a card row shows a card
    /// the shop would buy. Rare items are drawn in their own colour.
    ///
    /// Selling is a right-click, so a row cannot be given away by a stray left click while it is being
    /// read. The click is caught with IPointerClickHandler rather than through the action button, which is
    /// already spoken for by equipping and using.
    /// </summary>
    public class ItemRowView : MonoBehaviour, IPointerClickHandler
    {
        static readonly Color EmptyNameColor = new Color(0.38f, 0.40f, 0.47f, 1f);
        static readonly Color PriceColor = new Color(0.98f, 0.86f, 0.45f, 1f);

        [SerializeField] Text slotText;
        [SerializeField] Text nameText;
        [SerializeField] Text bonusText;
        [SerializeField] Text descriptionText;
        [SerializeField] Button actionButton;
        [SerializeField] Text actionLabel;
        [Tooltip("What the merchant pays for this row. Only shown while the menu is open at a shop.")]
        [SerializeField] Text priceText;

        ItemInstance instance;
        CardData card;
        int slotIndex = -1;
        CharacterMenuController owner;
        bool isBagRow;

        /// <summary>The item on this row, or null when the row is showing nothing or a card.</summary>
        public ItemInstance Instance { get { return instance; } }

        /// <summary>The card on this row, or null when the row is showing an item.</summary>
        public CardData Card { get { return card; } }

        void Awake()
        {
            if (actionButton != null) actionButton.onClick.AddListener(OnAction);
        }

        void OnDestroy()
        {
            if (actionButton != null) actionButton.onClick.RemoveListener(OnAction);
        }

        /// <summary>Binds a row to a worn item. An equipment slot with nothing in it binds null.</summary>
        public void BindSlot(ItemInstance item, int equipmentSlotIndex, CharacterMenuController controller)
        {
            instance = item;
            card = null;
            slotIndex = equipmentSlotIndex;
            isBagRow = false;
            owner = controller;
            Refresh();
        }

        /// <summary>Binds a row to a loose item in the bag.</summary>
        public void BindCarried(ItemInstance item, CharacterMenuController controller)
        {
            instance = item;
            card = null;
            slotIndex = -1;
            isBagRow = true;
            owner = controller;
            Refresh();
        }

        /// <summary>
        /// Binds a row to a card the merchant would buy. A card has no bag slot of its own, so the row
        /// reads as a card rather than as an item, and it carries no action button: the only thing left to
        /// do with it here is sell it.
        /// </summary>
        public void BindCard(CardData value, CharacterMenuController controller)
        {
            card = value;
            instance = null;
            slotIndex = -1;
            isBagRow = true;
            owner = controller;
            Refresh();
        }

        public void Refresh()
        {
            if (card != null)
            {
                RefreshCard();
                return;
            }

            bool hasItem = instance != null && instance.data != null;

            if (slotText != null)
            {
                slotText.text = isBagRow ? "BAG" : Inventory.SlotName(slotIndex);
            }

            if (!hasItem)
            {
                if (nameText != null)
                {
                    nameText.text = isBagRow ? "" : "-- empty --";
                    nameText.color = EmptyNameColor;
                }
                if (bonusText != null) bonusText.text = "";
                if (descriptionText != null) descriptionText.text = "";
                if (actionButton != null) actionButton.gameObject.SetActive(false);
                ShowPrice(0, false);
                return;
            }

            ItemData data = instance.data;

            if (nameText != null)
            {
                // A rare item is starred and drawn in gold, so it reads as special even in a list.
                nameText.text = ItemRarityStyle.Decorate(data);
                nameText.color = ItemRarityStyle.ColorFor(data.rarity);
            }

            // Read through the instance rather than off the definition, so a relic counts its guaranteed
            // stats and its roll together instead of showing only half of what it does.
            if (bonusText != null) bonusText.text = instance.BonusSummary();

            if (descriptionText != null)
            {
                if (instance.relic != null)
                {
                    // A rolled relic lists every stat, zeros included, so what it costs the build is as
                    // legible as what it pays. The tier is named rather than a rarity, because a rolled
                    // relic is not simply common or rare.
                    descriptionText.text = data.description +
                                           "   [" + RelicInstance.TierLabel(instance.relic.tier) + " RELIC]" +
                                           "\n" + instance.StatLines();
                }
                else
                {
                    descriptionText.text = data.description +
                                           "   [" + ItemRarityStyle.LabelFor(data.rarity) + " " + data.SlotLabel() + "]";
                }
            }

            if (actionButton != null) actionButton.gameObject.SetActive(true);
            if (actionLabel != null) actionLabel.text = ActionLabel(data);
            if (actionButton != null) actionButton.interactable = CanAct(data);

            ShowPrice(ShopPricing.SellPrice(data), true);
        }

        void RefreshCard()
        {
            if (slotText != null) slotText.text = "CARD";

            if (nameText != null)
            {
                nameText.text = card.cardName;
                nameText.color = Color.white;
            }

            if (bonusText != null)
            {
                string resource = card.costType == CardCostType.Mana ? "mana" : "energy";
                bonusText.text = "Costs " + card.cost + " " + resource + "   -   " + card.Summary(card.ValueFor(CurrentStats));
            }

            if (descriptionText != null)
            {
                string build = card.archetype != CardArchetype.Neutral ? card.archetype + "   " : "";
                descriptionText.text = build + "In your deck x" + DeckCountOf(card);
            }

            // The action button belongs to items. There is nothing to equip or use about a card.
            if (actionButton != null) actionButton.gameObject.SetActive(false);

            ShowPrice(ShopPricing.CardSellPrice(card), true);
        }

        /// <summary>
        /// Shows what the row would fetch, but only where there is a merchant to sell to. The row is the
        /// same object everywhere; it is the shop that turns the price on.
        /// </summary>
        void ShowPrice(int amount, bool sellable)
        {
            if (priceText == null) return;

            bool selling = sellable && owner != null && owner.SellMode;

            priceText.gameObject.SetActive(selling);
            if (!selling) return;

            priceText.text = amount + " g";
            priceText.color = PriceColor;
        }

        string ActionLabel(ItemData data)
        {
            if (instance.Equipped) return "UNEQUIP";
            if (data.consumable) return "USE";
            if (data.IsCharm) return "ACTIVE";
            return "EQUIP";
        }

        /// <summary>A charm works from the bag already, so its row has nothing to press.</summary>
        bool CanAct(ItemData data)
        {
            if (data.IsCharm && !instance.Equipped) return false;
            return true;
        }

        void OnAction()
        {
            if (owner != null) owner.ActivateItem(instance);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null) return;
            if (eventData.button != PointerEventData.InputButton.Right) return;
            if (owner == null || !owner.SellMode) return;

            owner.SellRow(this);
        }

        static PlayerStats CurrentStats
        {
            get
            {
                GameSession session = GameSession.Instance;
                return session != null ? session.Stats : null;
            }
        }

        static int DeckCountOf(CardData value)
        {
            GameSession session = GameSession.Instance;
            return session != null && session.Decks != null && session.Decks[session.ActiveDeckIndex] != null ? session.Decks[session.ActiveDeckIndex].CountOf(value) : 0;
        }
    }
}

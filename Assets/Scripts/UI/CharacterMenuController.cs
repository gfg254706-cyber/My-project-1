using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// The character menu opened with I: attributes and levelling at the top, and the worn equipment and
    /// the bag below. Ten bag slots hold loose items; anything worn on the body is an equipment slot and
    /// costs no bag space.
    ///
    /// The deck is not a tab here. It is edited full screen in the deck view, which the VIEW DECK button
    /// opens, and the button sits where the deck tab used to.
    /// </summary>
    public class CharacterMenuController : MonoBehaviour
    {
        [Header("Skills")]
        [SerializeField] Text headerText;
        [SerializeField] SkillRowView[] skillRows;
        [SerializeField] Text noticeText;

        [Header("Tabs")]
        [SerializeField] Button equipmentTabButton;
        [SerializeField] Button itemTabButton;
        [SerializeField] GameObject equipmentPanel;
        [SerializeField] GameObject itemPanel;

        [Header("Equipment")]
        [SerializeField] Transform equipmentRowContainer;
        [SerializeField] Text equipmentSummaryText;

        [Header("Bag")]
        [SerializeField] Transform itemRowContainer;
        [SerializeField] ItemRowView itemRowPrefab;
        [SerializeField] Text itemSummaryText;

        [Header("Common")]
        [SerializeField] Button backButton;

        [Header("Deck view")]
        [SerializeField] Button viewDeckButton;
        [Tooltip("The full screen deck editor. It replaces the old deck tab: this button opens it.")]
        [SerializeField] DeckViewPanel deckView;

        [Header("Selling")]
        [Tooltip("The purse and the sell instruction. Only shown when the menu is open at a shop.")]
        [SerializeField] Text sellHintText;

        readonly List<ItemRowView> itemRows = new List<ItemRowView>();

        readonly List<ItemRowView> equipmentRows = new List<ItemRowView>();

        GameSession Session { get { return GameSession.Instance; } }

        /// <summary>
        /// True when this menu was opened at a merchant. Selling is offered there and nowhere else,
        /// because a shop is the only thing in the dungeon that pays for anything.
        ///
        /// The room the player came from and the node they are standing on together say so: the menu is
        /// reached from the room scene, and the node is a shop.
        /// </summary>
        public bool SellMode
        {
            get
            {
                GameSession session = Session;
                if (session == null) return false;
                return session.ReturnScene == GameSession.RoomScene && session.AtShop;
            }
        }

        void Start()
        {
            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(OnBack);
            }
            if (equipmentTabButton != null)
            {
                equipmentTabButton.onClick.RemoveAllListeners();
                equipmentTabButton.onClick.AddListener(ShowEquipmentTab);
            }
            if (itemTabButton != null)
            {
                itemTabButton.onClick.RemoveAllListeners();
                itemTabButton.onClick.AddListener(ShowItemTab);
            }
            if (viewDeckButton != null)
            {
                viewDeckButton.onClick.RemoveAllListeners();
                viewDeckButton.onClick.AddListener(OnViewDeck);
            }

            BuildSkillRows();
            BuildEquipmentRows();
            RebuildItemRows();

            ShowEquipmentTab();
            Refresh();
        }

        void Update()
        {
            // I steps back one screen at a time: it closes the deck view first, then leaves the menu.
            if (!Hotkeys.CharacterPressed()) return;

            if (deckView != null && deckView.IsOpen) deckView.Close();
            else OnBack();
        }

        /// <summary>Opens the full screen deck editor. This is the readable way to look at the deck.</summary>
        void OnViewDeck()
        {
            if (deckView != null) deckView.Open();
        }

        // ------------------------------------------------------------------ setup

        void BuildSkillRows()
        {
            if (skillRows == null) return;

            for (int i = 0; i < skillRows.Length; i++)
            {
                if (skillRows[i] == null) continue;
                skillRows[i].Bind((StatType)i, this);
            }
        }

        /// <summary>One row per equipment slot, in the order the body wears them. Empty slots still show.</summary>
        void BuildEquipmentRows()
        {
            if (equipmentRowContainer == null || itemRowPrefab == null) return;

            for (int i = 0; i < Inventory.SlotOrder.Length; i++)
            {
                ItemRowView row = Instantiate(itemRowPrefab, equipmentRowContainer);
                row.BindSlot(null, i, this);
                equipmentRows.Add(row);
            }
        }

        void RebuildItemRows()
        {
            if (itemRowContainer == null || itemRowPrefab == null) return;

            foreach (ItemRowView row in itemRows)
            {
                if (row != null) Destroy(row.gameObject);
            }
            itemRows.Clear();

            GameSession session = Session;

            foreach (ItemInstance item in session.Inventory.Carried)
            {
                ItemRowView row = Instantiate(itemRowPrefab, itemRowContainer);
                row.BindCarried(item, this);
                itemRows.Add(row);
            }

            // At a merchant the bag also lists the cards he would buy, in with the items he would buy. They
            // are real things he takes off your hands, so they belong in the same list rather than behind
            // a screen of their own.
            if (!SellMode) return;

            foreach (CardData card in session.SellableCards())
            {
                ItemRowView row = Instantiate(itemRowPrefab, itemRowContainer);
                row.BindCard(card, this);
                itemRows.Add(row);
            }
        }

        /// <summary>
        /// Sells whatever a row holds. Reached by right clicking a row, and only while the menu is open at
        /// a shop, so the same row can be read anywhere and only spends its contents there.
        /// </summary>
        public void SellRow(ItemRowView row)
        {
            if (row == null) return;

            GameSession session = Session;

            if (row.Card != null)
            {
                int price = ShopPricing.CardSellPrice(row.Card);
                string cardName = row.Card.cardName;

                if (!session.SellCard(row.Card))
                {
                    Notice(cardName + " stays: it is the last card left in your deck.");
                    return;
                }

                Notice("Sold " + cardName + " for " + price + " gold.");
            }
            else if (row.Instance != null && row.Instance.data != null)
            {
                ItemInstance item = row.Instance;
                int price = ShopPricing.SellPrice(item.data);
                string itemName = item.data.itemName;

                if (!session.SellItem(item))
                {
                    Notice(itemName + " could not be sold.");
                    return;
                }

                Notice("Sold " + itemName + " for " + price + " gold.");
            }
            else
            {
                return;
            }

            // Both lists change: an item leaves the bag, or a card leaves the deck this screen counts.
            RebuildItemRows();
            Refresh();
        }

        // ------------------------------------------------------------------ tabs

        void ShowEquipmentTab()
        {
            SetTab(true, false);
        }

        void ShowItemTab()
        {
            SetTab(false, true);
        }

        void SetTab(bool equipment, bool bag)
        {
            if (equipmentPanel != null) equipmentPanel.SetActive(equipment);
            if (itemPanel != null) itemPanel.SetActive(bag);
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Buys one level of the chosen attribute, spending the experience it costs.</summary>
        public void LevelUpStat(StatType type)
        {
            GameSession session = Session;
            int cost = session.ExperienceForNextLevel;

            if (!session.SpendExperienceOnStat(type))
            {
                Notice("Not enough experience. You need " + cost + " XP.");
                return;
            }

            Notice("Raised " + type + " to " + session.Stats.GetBaseStat(type) + ". Level " + session.Level + ".");
            Refresh();
        }

        /// <summary>
        /// Pressed on an item row. What that means depends on where the item is: a worn item comes off,
        /// a potion is drunk, and anything else goes on if its slot is free.
        /// </summary>
        public void ActivateItem(ItemInstance item)
        {
            if (item == null || item.data == null) return;

            GameSession session = Session;
            Inventory inventory = session.Inventory;
            ItemData data = item.data;

            if (item.Equipped)
            {
                if (!inventory.Unequip(item))
                {
                    Notice("Your bag is full (" + Inventory.MaxCarriedSlots + " slots). Unequip or use something else first.");
                    return;
                }
                Notice("Stowed the " + data.itemName + ".");
            }
            else if (data.consumable)
            {
                session.UseItem(item);
                Notice(session.ItemNoticeText);
                session.ConsumeItemNotice();
            }
            else if (data.IsCharm)
            {
                Notice("The " + data.itemName + " is a charm. It works from your bag already.");
            }
            else if (!inventory.Equip(item))
            {
                Notice("No free " + data.SlotLabel() + " slot. Unequip what is worn there first.");
            }
            else
            {
                Notice("Equipped the " + data.itemName + ".");
            }

            RebuildItemRows();
            Refresh();
        }

        void Notice(string message)
        {
            if (noticeText != null) noticeText.text = message;
        }

        void Refresh()
        {
            GameSession session = Session;

            if (headerText != null)
            {
                headerText.text = "LEVEL " + session.Level +
                                  "        XP " + session.Experience + " / " + session.ExperienceForNextLevel +
                                  " per level" +
                                  "        Health " + session.Stats.Health + " / " + session.Stats.MaxHealth +
                                  "        Energy " + session.Stats.MaxEnergy;
            }

            if (skillRows != null)
            {
                foreach (SkillRowView row in skillRows)
                {
                    if (row != null) row.Refresh();
                }
            }

            Inventory inventory = session.Inventory;

            for (int i = 0; i < equipmentRows.Count; i++)
            {
                ItemRowView row = equipmentRows[i];
                if (row == null) continue;
                row.BindSlot(inventory.GetSlot(i), i, this);
            }

            if (equipmentSummaryText != null)
            {
                equipmentSummaryText.text = CountWorn(inventory) + " of " + Inventory.SlotOrder.Length +
                                            " slots filled.    Worn items take up no bag space.";
            }

            foreach (ItemRowView row in itemRows)
            {
                if (row != null) row.Refresh();
            }

            if (itemSummaryText != null)
            {
                itemSummaryText.text = "Bag " + inventory.CarriedCount + " / " + Inventory.MaxCarriedSlots +
                                       (inventory.IsBagFull ? "  (full)" : "") +
                                       (SellMode
                                           ? "    Right click anything to sell it."
                                           : "    Charms work while carried. Other items work only while worn.");
            }

            if (sellHintText != null)
            {
                // Shown only at a shop, where there is somebody to sell to.
                bool selling = SellMode;
                sellHintText.gameObject.SetActive(selling);
                if (selling)
                {
                    sellHintText.text = "GOLD " + session.Gold +
                                        "        Right-click an item or a card to sell it to the merchant.";
                }
            }
        }

        static int CountWorn(Inventory inventory)
        {
            int worn = 0;
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                if (inventory.GetSlot(i) != null) worn++;
            }
            return worn;
        }

        public void OnBack()
        {
            GameSession session = Session;
            string target = string.IsNullOrEmpty(session.ReturnScene) ? GameSession.MainMenuScene : session.ReturnScene;
            session.LoadScene(target);
        }
    }
}

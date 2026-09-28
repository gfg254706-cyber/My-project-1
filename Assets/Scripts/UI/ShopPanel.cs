using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// The merchant's screen: three racks of cards, relics and equipment, and the purse they are paid out
    /// of.
    ///
    /// Selling is deliberately not here. It lives in the character menu, which the player already has a
    /// key for and which already lists everything they own, so the merchant does not need a second screen
    /// of his own for half the transaction. That leaves this as one screen of things to buy, and the racks
    /// are a known size, so nothing on it has to scroll.
    ///
    /// Every price and every affordability decision comes from ShopPricing and GameSession. The screen
    /// holds no opinion of its own about what anything costs, so the racks and the purse cannot drift.
    /// </summary>
    public class ShopPanel : MonoBehaviour
    {
        [Header("Purse and messages")]
        [SerializeField] Text goldText;
        [SerializeField] Text noticeText;

        [Header("Buy racks")]
        [Tooltip("The card rack, drawn as the cards themselves. Equipment below it stays rows.")]
        [SerializeField] ShopCardView[] cardSlots;

        [Tooltip("The relic rack, drawn as small square icons in a line. Each one is bound to the roll it " +
                 "is selling, so what the tooltip quotes is what the purchase hands over.")]
        [SerializeField] RelicIconView[] relicRows;

        [Tooltip("The line the relic icons sit in. It owns their positions, so they must not also be in a " +
                 "layout group, or the two would write the same values and fight.")]
        [SerializeField] RelicRack relicRack;

        [SerializeField] ShopEntryView[] equipmentRows;

        [Header("Leaving")]
        [SerializeField] Button leaveButton;

        Action onLeave;

        GameSession Session { get { return GameSession.Instance; } }

        void Awake()
        {
            if (leaveButton != null) leaveButton.onClick.AddListener(OnLeave);
        }

        void OnDestroy()
        {
            if (leaveButton != null) leaveButton.onClick.RemoveListener(OnLeave);
        }

        /// <summary>
        /// Shows the room's stock and records what happens when the player walks away.
        ///
        /// The stock is asked for here as well as on the way into the room. PrepareShop returns early for
        /// a room it has already filled, so this costs nothing, and it keeps the screen correct when the
        /// Room scene is played on its own from the editor with no room transition to stock the shelves.
        /// </summary>
        public void Open(Action onLeaveCallback)
        {
            onLeave = onLeaveCallback;

            GameSession session = Session;
            if (session != null)
            {
                MapNode node = session.CurrentNode;
                if (node != null) session.PrepareShop(node.id);
            }

            gameObject.SetActive(true);
            Refresh();
        }

        /// <summary>Hides the screen. The room calls this so only one room panel is ever up.</summary>
        public void Close()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Called by a rack row's buy button. A purchase that cannot go through leaves the purse alone and
        /// says why, so the shop answers the question before taking the gold rather than after.
        /// </summary>
        public void Buy(ShopEntryView row)
        {
            if (row == null) return;

            GameSession session = Session;
            if (session == null) return;

            if (row.Card != null)
            {
                if (!session.BuyShopCard(row.Card))
                {
                    Notify("Not enough gold for " + row.Card.cardName + ".");
                    return;
                }

                Notify(row.Card.cardName + " joins your deck.");
            }
            else if (row.Instance != null && row.Item != null)
            {
                // The instance is bought rather than the definition, so a relic keeps the roll this row
                // advertised instead of being re-rolled at the till.
                if (!session.BuyShopItem(row.Instance))
                {
                    // BuyShopItem refuses before it charges, so a full bag never eats the gold.
                    Notify("No room for " + row.Item.itemName + ". Sell something first.");
                    return;
                }

                Notify(row.Item.itemName + " is yours.");
            }

            Refresh();
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Redraws the racks from the session, which is the one place the stock lives.</summary>
        void Refresh()
        {
            GameSession session = Session;
            if (session == null) return;

            if (goldText != null) goldText.text = "GOLD " + session.Gold;

            FillCardRack(cardSlots, session.ShopCards, session.Stats);
            FillRelicRack(relicRows, session.ShopRelics, session.Stats);
            FillItemRack(equipmentRows, session.ShopEquipment);
        }

        /// <summary>
        /// <summary>
        /// Called by a shelf card when it is clicked. The card rack is its own view because it is its own
        /// kind of object, so it gets its own entry point rather than being folded into the row overload.
        /// </summary>
        public void Buy(ShopCardView slot)
        {
            if (slot == null) return;

            GameSession session = Session;
            if (session == null || slot.Card == null) return;

            if (!session.BuyShopCard(slot.Card))
            {
                Notify("Not enough gold for " + slot.Card.cardName + ".");
                return;
            }

            Notify(slot.Card.cardName + " joins your deck.");
            Refresh();
        }


        /// Binds one rack of cards. Buying takes a card off the shelf, so the rows are rebound from the
        /// shortened list every time and the spare rows are cleared as well as switched off: a hidden row
        /// still holding a card could otherwise be pressed through the back of the shelf.
        /// </summary>
        void FillCardRack(ShopCardView[] rows, IReadOnlyList<CardData> stock, PlayerStats stats)
        {
            if (rows == null) return;

            for (int i = 0; i < rows.Length; i++)
            {
                ShopCardView row = rows[i];
                if (row == null) continue;

                CardData card = i < stock.Count ? stock[i] : null;
                row.gameObject.SetActive(card != null);
                row.BindCard(card, stats, this);
            }
        }

        /// <summary>
        /// Binds one rack of relics or equipment. A null entry clears the row as well as hiding it.
        ///
        /// The rack holds instances, so a relic row is bound to the object that carries its roll and the
        /// stats on the row are the stats that will be bought.
        /// </summary>
        void FillItemRack(ShopEntryView[] rows, IReadOnlyList<ItemInstance> stock)
        {
            if (rows == null) return;

            for (int i = 0; i < rows.Length; i++)
            {
                ShopEntryView row = rows[i];
                if (row == null) continue;

                ItemInstance entry = i < stock.Count ? stock[i] : null;
                row.gameObject.SetActive(entry != null);
                row.BindItem(entry, this);
            }
        }

        /// <summary>
        /// Binds one rack of relics. The icons are fixed objects in the shop scene and the shelf is a known
        /// size, so a short shelf switches icons off rather than destroying them.
        ///
        /// The rack is told only about the icons that are showing: an icon that is switched off must not be
        /// spaced as though it were on the shelf, or the line would have a hole in it.
        /// </summary>
        void FillRelicRack(RelicIconView[] icons, IReadOnlyList<ItemInstance> stock, PlayerStats stats)
        {
            if (icons == null) return;

            var showing = new List<RelicIconView>();

            for (int i = 0; i < icons.Length; i++)
            {
                RelicIconView icon = icons[i];
                if (icon == null) continue;

                ItemInstance entry = i < stock.Count ? stock[i] : null;
                icon.gameObject.SetActive(entry != null);
                icon.Bind(entry, this);

                if (entry == null) continue;

                showing.Add(icon);

                // What the icon costs and what its tooltip will quote, logged side by side, so the rack can
                // be read against the relic it is selling without hovering anything.
                var definition = entry.data as RelicDefinition;
                string quoted = definition != null && definition.effect != RelicEffect.None
                    ? definition.EffectSummary(stats, entry.GetStat(definition.scaling.stat))
                    : entry.BonusSummary();

                Debug.Log("[Shop] relic slot " + i + " = " + entry.data.itemName +
                          " (" + icon.Price() + " g | " + quoted + ")");
            }

            if (relicRack != null) relicRack.SetIcons(showing);
        }

        /// <summary>
        /// Called by a relic icon. Nothing is paid unless there is somewhere to put the relic, and the
        /// message names which of the two things went wrong rather than always blaming the purse.
        /// </summary>
        public void Buy(RelicIconView icon)
        {
            if (icon == null || icon.Instance == null || icon.Item == null) return;

            GameSession session = Session;
            if (session == null) return;

            // The instance is bought rather than the definition, so a relic keeps the roll this icon
            // advertised instead of being freshly rolled at the till.
            if (!session.BuyShopItem(icon.Instance))
            {
                Notify(session.Inventory != null && session.Inventory.CanAccept(icon.Item)
                    ? "Not enough gold for " + icon.Item.itemName + "."
                    : "No room for " + icon.Item.itemName + ". Sell something first.");
                return;
            }

            Notify(icon.Item.itemName + " is yours.");
            Refresh();
        }

        void Notify(string message)
        {
            if (noticeText != null) noticeText.text = message;
        }

        void OnLeave()
        {
            if (onLeave == null) return;

            // Cleared before the call: leaving loads a scene, and a stale callback would let a second
            // press on the way out start a second transition.
            Action callback = onLeave;
            onLeave = null;
            callback();
        }
    }
}

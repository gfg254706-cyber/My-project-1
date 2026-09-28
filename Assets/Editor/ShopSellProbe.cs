using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// Temporary probe. Drives the character menu's sell path in play mode without a mouse: the same
    /// right-click entry point a player uses, so the gate in ItemRowView and the routing in
    /// CharacterMenuController.SellRow are both exercised rather than skipped.
    ///
    /// Delete once the sell path has been verified.
    /// </summary>
    public static class ShopSellProbe
    {
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>
        /// Puts the play session back into the state this probe needs: a fresh run, the player standing on
        /// a shop node with gold in hand, one item worn and the rest loose, then the character scene loaded
        /// so the menu builds its rows against it. Run this first, then Execute.
        /// </summary>
        public static string Setup()
        {
            GameSession session = GameSession.Instance;
            if (session == null) return "FAIL: no GameSession";

            session.StartNewRun();

            int shopId = -1;
            foreach (MapNode node in session.Map.nodes)
            {
                if (node != null && node.roomType == RoomType.Shop)
                {
                    shopId = node.id;
                    break;
                }
            }

            if (shopId < 0) return "FAIL: the generated map has no shop node";

            session.EnterNode(shopId);
            session.AddGold(20);
            session.PrepareShop(shopId);

            // A couple more things in the bag, and one of them worn, so the bag and the body both have
            // something on them to sell.
            GameDatabase db = session.Database;
            if (db != null && db.startingItems != null)
            {
                foreach (ItemData data in db.startingItems)
                {
                    if (data != null) session.Inventory.Add(data);
                }
            }

            foreach (ItemInstance item in session.Inventory.Carried)
            {
                if (item == null || item.data == null) continue;
                if (item.data.IsCharm || item.data.consumable) continue;
                if (session.Inventory.Equip(item)) break;
            }

            session.ReturnScene = GameSession.RoomScene;
            session.LoadScene(GameSession.CharacterScene);

            return "shop node " + shopId + " of " + session.Map.nodes.Count + " nodes   gold " + session.Gold +
                   "   bag " + session.Inventory.CarriedCount + "   deck " + session.Deck.Count +
                   "   roomType " + session.CurrentNode.roomType + "   loading " + GameSession.CharacterScene;
        }

        public static string Execute()

        {
            var sb = new StringBuilder();

            GameSession session = GameSession.Instance;
            if (session == null) return "FAIL: no GameSession in scene " + SceneManager.GetActiveScene().name;

            CharacterMenuController controller = Object.FindAnyObjectByType<CharacterMenuController>();
            if (controller == null) return "FAIL: no CharacterMenuController in scene " + SceneManager.GetActiveScene().name;

            List<ItemRowView> rows = Read<List<ItemRowView>>(controller, "itemRows");
            List<ItemRowView> body = Read<List<ItemRowView>>(controller, "equipmentRows");
            Text notice = Read<Text>(controller, "noticeText");
            if (rows == null) return "FAIL: itemRows not readable";

            sb.AppendLine("scene      " + SceneManager.GetActiveScene().name);
            sb.AppendLine("SellMode   " + controller.SellMode + "   AtShop " + session.AtShop +
                          "   ReturnScene " + session.ReturnScene);
            sb.AppendLine("start      gold " + session.Gold + "   deck " + session.Deck.Count +
                          "   bag " + session.Inventory.CarriedCount + "/" + Inventory.MaxCarriedSlots);
            sb.AppendLine("rows       " + rows.Count + "  (loose items " + CountItems(rows) + ", cards " + CountCards(rows) + ")");
            sb.AppendLine("body rows  " + (body != null ? body.Count.ToString() : "?") + "  (worn " + CountItems(body) + ")");
            sb.AppendLine();

            // -- 1. what each kind of row advertises -------------------------------
            ItemRowView cardRow = FirstCard(rows);
            ItemRowView itemRow = FirstItem(rows);
            sb.AppendLine("card row   " + Describe(cardRow));
            sb.AppendLine("item row   " + Describe(itemRow));
            sb.AppendLine("worn row   " + Describe(FirstItem(body)));
            sb.AppendLine();

            // -- 2. a left click is not a sale -------------------------------------
            if (cardRow != null)
            {
                CardData card = cardRow.Card;
                int goldBefore = session.Gold;
                int deckBefore = session.Deck.Count;

                Click(cardRow, PointerEventData.InputButton.Left);

                bool held = goldBefore == session.Gold && deckBefore == session.Deck.Count;
                sb.AppendLine("left click '" + card.cardName + "'   gold " + goldBefore + " -> " + session.Gold +
                              "   deck " + deckBefore + " -> " + session.Deck.Count +
                              "   " + (held ? "PASS nothing sold" : "FAIL it sold on a left click"));
            }
            sb.AppendLine();

            // -- 3. right click a card: the real thing -----------------------------
            if (cardRow != null)
            {
                CardData card = cardRow.Card;
                int price = ShopPricing.CardSellPrice(card);
                int goldBefore = session.Gold;
                int deckBefore = session.Deck.Count;
                int ownedBefore = session.OwnedCount(card);

                Click(cardRow, PointerEventData.InputButton.Right);

                sb.AppendLine("right click card '" + card.cardName + "'  price " + price);
                sb.AppendLine("   gold  " + goldBefore + " -> " + session.Gold +
                              (session.Gold == goldBefore + price ? "   PASS" : "   FAIL expected +" + price));
                sb.AppendLine("   deck  " + deckBefore + " -> " + session.Deck.Count +
                              (session.Deck.Count == deckBefore - 1 ? "   PASS" : "   FAIL expected -1"));
                sb.AppendLine("   owned " + ownedBefore + " -> " + session.OwnedCount(card) +
                              (session.OwnedCount(card) == ownedBefore - 1 ? "   PASS" : "   FAIL expected -1"));
                sb.AppendLine("   rows  " + rows.Count + "  (loose items " + CountItems(rows) + ", cards " + CountCards(rows) + ")");
                sb.AppendLine("   notice '" + NoticeOf(notice) + "'");
            }
            sb.AppendLine();

            // -- 4. right click a loose item ---------------------------------------
            itemRow = FirstItem(rows);
            if (itemRow != null)
            {
                string name = itemRow.Instance.data.itemName;
                int price = ShopPricing.SellPrice(itemRow.Instance.data);
                int goldBefore = session.Gold;
                int bagBefore = session.Inventory.CarriedCount;

                Click(itemRow, PointerEventData.InputButton.Right);

                sb.AppendLine("right click item '" + name + "'  price " + price);
                sb.AppendLine("   gold  " + goldBefore + " -> " + session.Gold +
                              (session.Gold == goldBefore + price ? "   PASS" : "   FAIL expected +" + price));
                sb.AppendLine("   bag   " + bagBefore + " -> " + session.Inventory.CarriedCount +
                              (session.Inventory.CarriedCount == bagBefore - 1 ? "   PASS" : "   FAIL expected -1"));
                sb.AppendLine("   rows  " + rows.Count + "  (loose items " + CountItems(rows) + ", cards " + CountCards(rows) + ")");
                sb.AppendLine("   notice '" + NoticeOf(notice) + "'");
            }
            sb.AppendLine();

            // -- 5. right click something worn -------------------------------------
            ItemRowView wornRow = FirstItem(body);
            if (wornRow != null)
            {
                string name = wornRow.Instance.data.itemName;
                int price = ShopPricing.SellPrice(wornRow.Instance.data);
                int goldBefore = session.Gold;
                int wornBefore = CountItems(body);

                Click(wornRow, PointerEventData.InputButton.Right);

                sb.AppendLine("right click worn '" + name + "'  price " + price);
                sb.AppendLine("   gold  " + goldBefore + " -> " + session.Gold +
                              (session.Gold == goldBefore + price ? "   PASS" : "   FAIL expected +" + price));
                sb.AppendLine("   worn  " + wornBefore + " -> " + CountItems(body) +
                              (CountItems(body) == wornBefore - 1 ? "   PASS" : "   FAIL expected -1"));
                sb.AppendLine("   notice '" + NoticeOf(notice) + "'");
            }
            else sb.AppendLine("right click worn: nothing is worn, skipped");
            sb.AppendLine();

            // -- 6. the last card in the deck must be refused ----------------------
            ItemRowView guardRow = FirstCard(rows);
            if (guardRow != null && session.Deck.Count > 1)
            {
                CardData card = guardRow.Card;
                var saved = new List<CardData>(session.Deck.Cards);
                int goldBefore = session.Gold;
                int ownedBefore = session.OwnedCount(card);

                session.Deck.Clear();
                session.Deck.Add(card, 1);

                Click(guardRow, PointerEventData.InputButton.Right);

                sb.AppendLine("last-card guard: deck forced down to 1 x '" + card.cardName + "'");
                sb.AppendLine("   gold  " + goldBefore + " -> " + session.Gold +
                              (session.Gold == goldBefore ? "   PASS unchanged" : "   FAIL paid out"));
                sb.AppendLine("   deck  " + session.Deck.Count +
                              (session.Deck.Count == 1 ? "   PASS still 1" : "   FAIL emptied"));
                sb.AppendLine("   owned " + ownedBefore + " -> " + session.OwnedCount(card) +
                              (session.OwnedCount(card) == ownedBefore ? "   PASS unchanged" : "   FAIL lost a copy"));
                sb.AppendLine("   notice '" + NoticeOf(notice) + "'");

                session.Deck.Clear();
                foreach (CardData entry in saved) session.Deck.Add(entry);
                Invoke(controller, "RebuildItemRows");
                Invoke(controller, "Refresh");
                sb.AppendLine("   deck restored to " + session.Deck.Count + ", rows rebuilt to " + rows.Count);
            }
            else sb.AppendLine("last-card guard: skipped (no card row, or the deck is already a single card)");
            sb.AppendLine();

            sb.AppendLine("end        gold " + session.Gold + "   deck " + session.Deck.Count +
                          "   bag " + session.Inventory.CarriedCount);

            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers

        static void Click(ItemRowView row, PointerEventData.InputButton button)
        {
            var data = new PointerEventData(EventSystem.current);
            data.button = button;
            row.OnPointerClick(data);
        }

        static string Describe(ItemRowView row)
        {
            if (row == null) return "none";

            Text price = Read<Text>(row, "priceText");
            Button action = Read<Button>(row, "actionButton");
            string priceLabel = price == null ? "<no priceText field>"
                               : price.gameObject.activeSelf ? "'" + price.text + "'"
                               : "<hidden>";

            if (row.Card != null)
            {
                return "'" + row.Card.cardName + "'  price " + priceLabel +
                       "  action button " + (action != null && action.gameObject.activeSelf ? "shown" : "hidden");
            }

            if (row.Instance == null || row.Instance.data == null) return "empty slot";

            return "'" + row.Instance.data.itemName + "'" + (row.Instance.Equipped ? " (worn)" : "") +
                   "  price " + priceLabel +
                   "  action button " + (action != null && action.gameObject.activeSelf ? "shown" : "hidden");
        }

        static string NoticeOf(Text notice)
        {
            return notice != null ? notice.text : "<no noticeText field>";
        }

        static int CountItems(List<ItemRowView> rows)
        {
            if (rows == null) return 0;
            int count = 0;
            foreach (ItemRowView row in rows)
            {
                if (row != null && row.Card == null && row.Instance != null) count++;
            }
            return count;
        }

        static int CountCards(List<ItemRowView> rows)
        {
            if (rows == null) return 0;
            int count = 0;
            foreach (ItemRowView row in rows)
            {
                if (row != null && row.Card != null) count++;
            }
            return count;
        }

        static ItemRowView FirstCard(List<ItemRowView> rows)
        {
            if (rows == null) return null;
            foreach (ItemRowView row in rows)
            {
                if (row != null && row.Card != null) return row;
            }
            return null;
        }

        static ItemRowView FirstItem(List<ItemRowView> rows)
        {
            if (rows == null) return null;
            foreach (ItemRowView row in rows)
            {
                if (row != null && row.Card == null && row.Instance != null) return row;
            }
            return null;
        }

        static T Read<T>(object target, string fieldName) where T : class
        {
            if (target == null) return null;
            FieldInfo field = target.GetType().GetField(fieldName, Private);
            return field != null ? field.GetValue(target) as T : null;
        }

        static void Invoke(object target, string methodName)
        {
            if (target == null) return;
            MethodInfo method = target.GetType().GetMethod(methodName, Private);
            if (method != null) method.Invoke(target, null);
        }
    }
}

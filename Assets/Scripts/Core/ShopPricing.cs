using UnityEngine;

namespace DungeonCards
{
    /// <summary>
    /// What the shop charges and what it pays. Kept in one place so the racks and the sell screen can
    /// never drift apart, and so the two halves of the economy can be tuned against each other.
    ///
    /// The two numbers that matter are the cheapest thing on the shelves and what a loose item fetches:
    /// an ordinary item sells for a few gold and a card for a single one, against card prices in the
    /// forties and fifties. Selling is therefore a way to shed what you are carrying, not a way to fund
    /// the next purchase, and the only thing worth carrying back is rare equipment.
    ///
    /// Every sale is far under what the same thing costs to buy, so buying and selling back is always a
    /// loss and the shop cannot be farmed for gold.
    /// </summary>
    public static class ShopPricing
    {
        /// <summary>Price of a card before its cost is counted.</summary>
        public const int CardBasePrice = 40;

        /// <summary>Added to a card's price for every point of its cost, so expensive cards cost more.</summary>
        public const int CardPricePerCost = 10;

        /// <summary>Price of a piece of equipment nobody has worn yet.</summary>
        public const int EquipmentPrice = 45;

        /// <summary>Price of a relic. Relics do more than gear and cost accordingly.</summary>
        public const int RelicPrice = 75;

        /// <summary>What rarity multiplies a price by. Deliberately larger than the sell-side gap.</summary>
        public const float RarePriceMultiplier = 1.8f;

        /// <summary>
        /// What an ordinary piece of equipment fetches. Under five gold, so selling is a tidy-up rather
        /// than an income: the shop is a sink, and the sell side must not open a second one.
        /// </summary>
        public const int CommonSellPrice = 3;

        /// <summary>
        /// What rare equipment fetches, and the one thing on the sell side worth carrying back. Still far
        /// under what it costs to buy, so it can never be bought and sold back at a profit.
        /// </summary>
        public const int RareSellPrice = 20;

        /// <summary>What a consumable fetches: the least of anything, and a token amount.</summary>
        public const int ConsumableSellPrice = 2;

        /// <summary>
        /// What a card fetches, whatever the card is. A flat one gold rather than a valuation: with cards
        /// this cheap the deck cannot be cashed in, so the opening shop cannot be funded by selling the
        /// deck the player arrived with.
        /// </summary>
        public const int CardSellValue = 100;

        /// <summary>What a card on the shop's card rack costs.</summary>
        public static int CardPrice(CardData card)
        {
            if (card == null) return 0;
            return CardBasePrice + CardPricePerCost * Mathf.Max(0, card.cost);
        }

        /// <summary>What a relic or a piece of equipment costs, depending on which rack it is on.</summary>
        public static int ItemPrice(ItemData item)
        {
            if (item == null) return 0;

            float price = item.relic ? RelicPrice : EquipmentPrice;
            if (item.rarity == ItemRarity.Rare) price *= RarePriceMultiplier;

            return Mathf.RoundToInt(price);
        }

        /// <summary>
        /// What a card fetches. One gold, whoever it is and whatever it cost to play, so the sell screen
        /// is a way to shed cards rather than a way to raise gold.
        /// </summary>
        public static int CardSellPrice(CardData card)
        {
            if (card == null) return 0;
            return CardSellValue;
        }

        /// <summary>What a loose item fetches. Always well under what the shop sells it for.</summary>
        public static int SellPrice(ItemData item)
        {
            if (item == null) return 0;
            if (item.consumable) return ConsumableSellPrice;
            return item.rarity == ItemRarity.Rare ? RareSellPrice : CommonSellPrice;
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonCards
{
    /// <summary>
    /// One relic on the merchant's rack, drawn as a small square rather than as a row describing it.
    ///
    /// A relic is not a card: it has no cost to play and no card body, so the square is somewhere for art
    /// that does not exist yet, and everything that would have been written on a card is in the tooltip
    /// instead. At rest the icon says nothing but what it costs, which is what keeps three of them legible
    /// side by side. Under the pointer the price gives way to the name, the icon grows, and the rack makes
    /// room beside it for the tooltip.
    ///
    /// The hover itself is the same CardHoverEffect the cards wear, driven by the same EventTrigger, so the
    /// rack can treat a relic and a card identically.
    /// </summary>
    public class RelicIconView : MonoBehaviour, IHoverView
    {
        [Header("Parts")]
        [SerializeField] Image body;
        [SerializeField] TextMeshProUGUI captionText;
        [SerializeField] CanvasGroup group;
        [SerializeField] Button buyButton;

        [Header("Interaction")]
        [Tooltip("Shared with the cards. Created on demand the first time anything is hovered.")]
        [SerializeField] CardTooltip tooltipPrefab;

        // Placeholder tints, chosen by tier so three relics on a rack read as three different things. Art
        // replaces the whole body later; until then the square still has to be distinguishable.
        static readonly Color UniqueBody = new Color(0.44f, 0.28f, 0.50f, 1f);
        static readonly Color TierIBody = new Color(0.50f, 0.40f, 0.26f, 1f);
        static readonly Color TierIIBody = new Color(0.32f, 0.35f, 0.45f, 1f);
        static readonly Color TierIIIBody = new Color(0.29f, 0.29f, 0.33f, 1f);
        static readonly Color PlainBody = new Color(0.29f, 0.29f, 0.33f, 1f);

        static readonly Color PriceColor = new Color(0.98f, 0.86f, 0.45f, 1f);
        static readonly Color UnaffordableColor = new Color(0.55f, 0.50f, 0.40f, 1f);

        RectTransform rect;
        ItemInstance instance;
        ShopPanel owner;

        /// <summary>The relic on this icon, or null when the icon is not in use.</summary>
        public ItemInstance Instance { get { return instance; } }

        /// <summary>The definition behind it, which is what a purchase is checked against.</summary>
        public ItemData Item { get { return instance != null ? instance.data : null; } }

        /// <summary>The lift, so the rack can tell which icon the pointer is on.</summary>
        public CardHoverEffect Hover { get; private set; }

        void Awake()
        {
            Cache();

            if (buyButton != null) buyButton.onClick.AddListener(OnBuy);
        }

        void OnDestroy()
        {
            if (buyButton != null) buyButton.onClick.RemoveListener(OnBuy);
        }

        void Cache()
        {
            if (rect == null) rect = (RectTransform)transform;
            if (group == null) group = GetComponent<CanvasGroup>();
            if (buyButton == null) buyButton = GetComponent<Button>();
            if (Hover == null) Hover = GetComponent<CardHoverEffect>();
        }

        // ---------------------------------------------------------------- binding

        /// <summary>
        /// Puts a relic on this icon, or empties it. The price is what an empty icon has instead of text, so
        /// binding is what decides whether the icon has anything to say at all.
        /// </summary>
        public void Bind(ItemInstance value, ShopPanel panel)
        {
            Cache();

            instance = value;
            owner = panel;

            if (instance != null && instance.data != null && body != null) body.color = BodyColor(instance.data);

            ShowResting();

            if (buyButton != null) buyButton.interactable = instance != null && Affordable();
        }

        /// <summary>What the merchant charges for this one.</summary>
        public int Price()
        {
            return instance != null && instance.data != null ? ShopPricing.ItemPrice(instance.data) : 0;
        }

        // ---------------------------------------------------------------- caption

        /// <summary>The at-rest caption: the price and nothing else. Three relics have to read side by side.</summary>
        void ShowResting()
        {
            if (captionText == null) return;

            if (instance == null || instance.data == null)
            {
                captionText.text = "";
                return;
            }

            captionText.text = Price() + " g";
            captionText.color = Affordable() ? PriceColor : UnaffordableColor;
        }

        /// <summary>Under the pointer the price gives way to the name, which the tooltip then explains.</summary>
        void ShowHovered()
        {
            if (captionText == null || instance == null || instance.data == null) return;

            captionText.text = instance.data.itemName;
            captionText.color = ItemRarityStyle.ColorFor(instance.data.rarity);
        }

        // ---------------------------------------------------------------- hover

        /// <summary>Called by the icon's hover effect. Opens the tooltip and swaps the caption to the name.</summary>
        public void SetHovered(bool hovered)
        {
            SetLifted(hovered);

            if (instance == null || instance.data == null) return;

            if (hovered)
            {
                // One tooltip serves the whole scene, so the shop's relics and the cards share it.
                CardTooltip tooltip = CardTooltip.Shared(tooltipPrefab);
                if (tooltip != null) tooltip.Show(RelicText.Title(instance), RelicText.Body(instance, Stats), rect, this);
                return;
            }

            CardTooltip open = CardTooltip.Instance;
            if (open != null && open.Owner == (Component)this) open.Hide();
        }

        /// <summary>
        /// The caption on its own, without the tooltip. A relic is only ever read by the pointer, so this is
        /// the same thing to it; the two are kept apart because a card can be laid out without being pointed
        /// at and a relic cannot.
        /// </summary>
        public void SetLifted(bool lifted)
        {
            Cache();
            if (instance == null || instance.data == null) return;

            if (lifted) ShowHovered();
            else ShowResting();
        }

        // ---------------------------------------------------------------- rack

        /// <summary>Where the rack puts this icon. The rack owns position and lean; the hover owns scale.</summary>
        public void Place(Vector2 position)
        {
            Cache();
            rect.anchoredPosition = position;
        }

        /// <summary>How far the rack leans this icon, in degrees. Leftward, away from the tooltip.</summary>
        public void Bend(float degrees)
        {
            Cache();
            rect.localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        /// <summary>
        /// Fades the icon while a different one is being read. Alpha only: raycasting is left alone, so a
        /// faded relic can still be pointed at and still be bought.
        /// </summary>
        public void SetDim(float alpha)
        {
            Cache();
            if (group != null) group.alpha = alpha;
        }

        // ---------------------------------------------------------------- buying

        void OnBuy()
        {
            if (owner != null) owner.Buy(this);
        }

        bool Affordable()
        {
            GameSession session = GameSession.Instance;
            return session != null && session.Gold >= Price();
        }

        /// <summary>The player's attributes, so the tooltip quotes the number this relic would produce.</summary>
        static PlayerStats Stats
        {
            get
            {
                GameSession session = GameSession.Instance;
                return session != null ? session.Stats : null;
            }
        }

        static Color BodyColor(ItemData data)
        {
            var definition = data as RelicDefinition;
            if (definition == null) return PlainBody;

            switch (definition.tier)
            {
                case RelicRarity.Unique: return UniqueBody;
                case RelicRarity.TierI: return TierIBody;
                case RelicRarity.TierII: return TierIIBody;
            }

            return TierIIIBody;
        }
    }
}

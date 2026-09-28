using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


namespace DungeonCards
{
    /// <summary>
    /// One card, drawn as a physical card: a cost badge in the corner, an art window, and the effect
    /// spelled out underneath.
    ///
    /// The number in the description is the value the card will actually produce right now, not the value
    /// printed on it. When stats or Strength are pushing it above its base it is drawn in green and
    /// bounces, so a boosted card reads differently from a plain one at a glance.
    ///
    /// The same view serves the hand and the deck grid; only a hand card is playable.
    /// </summary>
    public class CardView : MonoBehaviour, IPointerClickHandler, IHoverView
    {
        /// <summary>Fired when this view's hover state changes. Parameter is `true` when hovered.</summary>
        public event System.Action<bool> HoverChanged;

        [Header("Text")]
        [SerializeField] TextMeshProUGUI nameText;
        [SerializeField] TextMeshProUGUI costText;
        [SerializeField] TextMeshProUGUI valueText;
        [SerializeField] TextMeshProUGUI descriptionText;

        [Header("Graphics")]
        [SerializeField] Image frame;
        [SerializeField] Image costBadge;
        [SerializeField] Image artImage;

        [Header("Deck entry")]
        [Tooltip("The copy count badge, shown only on the deck grid. Hidden on a hand card.")]
        [SerializeField] GameObject countRoot;
        [SerializeField] TextMeshProUGUI countText;
        [SerializeField] Image countBadge;


        [Header("Interaction")]
        [SerializeField] Button playButton;
        [Tooltip("Created on demand the first time a card is hovered.")]
        [SerializeField] CardTooltip tooltipPrefab;

        static readonly Color AttackColor = new Color(0.45f, 0.20f, 0.22f, 1f);
        static readonly Color BlockColor = new Color(0.18f, 0.30f, 0.46f, 1f);
        static readonly Color SkillColor = new Color(0.26f, 0.26f, 0.34f, 1f);

        /// <summary>Board cards are tinted so you can tell at a glance what will keep running.</summary>
        static readonly Color BoardTint = new Color(0.30f, 0.21f, 0.42f, 1f);

        // Art window tints, used when a card has no art assigned.
        static readonly Color AttackArt = new Color(0.60f, 0.33f, 0.35f, 1f);
        static readonly Color BlockArt = new Color(0.31f, 0.46f, 0.64f, 1f);
        static readonly Color SkillArt = new Color(0.40f, 0.40f, 0.50f, 1f);
        static readonly Color BoardArt = new Color(0.49f, 0.37f, 0.65f, 1f);

        static readonly Color EnergyCostColor = new Color(0.72f, 0.90f, 1f, 1f);
        static readonly Color ManaCostColor = new Color(0.74f, 0.82f, 1f, 1f);
        static readonly Color EnergyBadgeColor = new Color(0.19f, 0.33f, 0.51f, 1f);
        static readonly Color ManaBadgeColor = new Color(0.28f, 0.27f, 0.57f, 1f);

        /// <summary>A value above the card's printed base is green; anything else is white.</summary>
        static readonly Color BoostedNumber = new Color(0.49f, 0.88f, 0.49f, 1f);

        // Rarity, carried on the card's name. Common is plain white, so the other two read as a find.
        static readonly Color UncommonName = new Color(0.62f, 0.80f, 1f, 1f);
        static readonly Color RareName = new Color(1f, 0.84f, 0.42f, 1f);

        // A deck entry with no copies left in the deck is greyed out, but stays clickable so it can be
        // put back. Colours rather than transparency, so it reads as "empty" rather than "faded".
        static readonly Color SpentFrame = new Color(0.15f, 0.15f, 0.17f, 1f);
        static readonly Color SpentArt = new Color(0.21f, 0.21f, 0.23f, 1f);
        static readonly Color SpentBadge = new Color(0.24f, 0.24f, 0.27f, 1f);
        static readonly Color SpentText = new Color(0.47f, 0.47f, 0.51f, 1f);


        const string BoostedHex = "7EE07E";
        const string PlainHex = "FFFFFF";
        const float DescriptionInlineSize = 140f;
        const float DescriptionFontShort = 13.5f;
        const float DescriptionFontLong = 8.5f;
        const int DescriptionCharsShort = 90;
        const int DescriptionCharsLong = 240;
        const float DescriptionShakeDistance = 1.9f;
        const float DescriptionShakeDuration = 0.42f;
        const float DescriptionLoopFrequency = 24f;

        [SerializeField] bool doubleDigitHoverAnimationOnly = true;

        public CardData Card { get; private set; }
        public bool Playable { get; private set; }

        /// <summary>True on a deck grid entry, which is edited with the mouse instead of played.</summary>
        public bool DeckEditable { get; private set; }

        /// <summary>True on a reward offer, which is taken by clicking it rather than played.</summary>
        public bool Choice { get; private set; }

        /// <summary>Raised when a reward offer is clicked. The screen decides what taking it means.</summary>
        public event Action<CardView> Chosen;

        /// <summary>
        /// Raised on a deck grid entry with the mouse button that was clicked, as a
        /// PointerEventData.InputButton: 0 is left, 1 is right.
        /// </summary>
        public event Action<CardView, int> DeckClick;



        CombatManager manager;
        PlayerStats stats;
        int handIndex = -1;
        Coroutine bounceRoutine;
        Coroutine descriptionShakeRoutine;
        bool animatedNumberLoop;

        /// <summary>
        /// True while the card is being read, which is when it stops showing what it was printed with and
        /// starts showing what the player's attributes make of it.
        /// </summary>
        bool inspecting;

        void Awake()
        {
            if (playButton == null) playButton = GetComponent<Button>();
            if (playButton != null) playButton.onClick.AddListener(OnClicked);
        }

        void OnDestroy()
        {
            if (playButton != null) playButton.onClick.RemoveListener(OnClicked);

            // A card can be destroyed while it is the one showing the tooltip, which would otherwise
            // leave the tooltip stranded on screen.
            CardTooltip open = CardTooltip.Instance;
            if (open != null && open.Owner == (Component)this) open.Hide();
        }

        // ---------------------------------------------------------------- binding

        /// <summary>Binds a playable card to a position in the hand. Play is index based, so it must be fresh.</summary>
        public void Bind(CardData card, CombatManager owner, PlayerStats playerStats, int index)
        {
            manager = owner;
            stats = playerStats;
            handIndex = index;
            Playable = true;
            Render(card);
        }

        /// <summary>
        /// Binds a card as a deck grid entry: it cannot be played, it shows how many copies are in the
        /// deck, and it is added to or removed from the deck with the mouse.
        /// </summary>
        public void BindDeckEntry(CardData card, PlayerStats playerStats, int inDeck)
        {
            manager = null;
            stats = playerStats;
            handIndex = -1;
            Playable = false;
            DeckEditable = true;
            Render(card);
            RefreshDeckEntry(inDeck);
        }

        /// <summary>
        /// Binds a card as an offer on the reward screen. It is drawn exactly like a card in hand, so the
        /// choice is read off the object the player already knows, and clicking it is what takes it.
        /// </summary>
        public void BindChoice(CardData card, PlayerStats playerStats)
        {
            manager = null;
            stats = playerStats;
            handIndex = -1;
            Playable = false;
            DeckEditable = false;
            Choice = true;
            Render(card);

            // The copy badge belongs to the deck grid; an offer is not in the deck yet.
            if (countRoot != null) countRoot.SetActive(false);
        }

        /// <summary>Stops an offer from being taken again, once the choice has been made.</summary>
        public void SetChoiceEnabled(bool enabled)
        {
            Choice = enabled;
            if (playButton != null) playButton.interactable = enabled;
        }

        /// <summary>Updates the copy count and the greyed out state after the deck changed.</summary>
        public void RefreshDeckEntry(int inDeck)
        {
            if (Card == null) return;

            // Re-render first: it restores the card's own colours, which a spent card has had replaced.
            Render(Card);

            bool spent = inDeck <= 0;
            if (spent) ApplySpentTint();

            if (countRoot != null) countRoot.SetActive(true);
            if (countText != null) countText.text = inDeck.ToString();

            if (countBadge != null)
            {
                countBadge.color = spent
                    ? SpentBadge
                    : (Card.costType == CardCostType.Mana ? ManaBadgeColor : EnergyBadgeColor);
            }
        }

        /// <summary>Greys a card out, including the boosted number, so nothing is left bright on it.</summary>
        void ApplySpentTint()
        {
            if (frame != null) frame.color = SpentFrame;
            if (artImage != null) artImage.color = SpentArt;
            if (costBadge != null) costBadge.color = SpentBadge;
            if (nameText != null) nameText.color = SpentText;
            if (costText != null) costText.color = SpentText;
            if (valueText != null) valueText.color = SpentText;

            if (descriptionText != null)
            {
                descriptionText.color = SpentText;

                // The plain summary drops the boosted number's colour markup, which would otherwise
                // stay bright white on an otherwise greyed card.
                descriptionText.text = Card.Summary(Card.ValueFor(stats));
            }
        }

        /// <summary>
        /// A deck entry is edited with the mouse. A hand card is played by its button, but a card the player
        /// cannot pay for has that button switched off, so the click never reaches the manager and the card
        /// simply does nothing when clicked. The click does still arrive here, which is what gives the
        /// refusal somewhere to be explained.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null) return;

            // An offer on the reward screen is taken by clicking it. There is no second stage to it: the
            // card is already drawn out, so the only decision is whether to keep it.
            if (Choice)
            {
                if (Chosen != null) Chosen(this);
                return;
            }

            if (DeckEditable)
            {
                if (DeckClick != null) DeckClick(this, (int)eventData.button);
                return;
            }

            if (!Playable || Card == null || manager == null || handIndex < 0) return;
            if (!manager.AcceptsPlays) return;

            // Only the unaffordable case. An affordable card is played by its own button, and acting on it
            // here as well would try to play it a second time.
            manager.ReportUnaffordable(Card);
        }


        /// <summary>Updates just the playable state, used when energy changes but the hand does not.</summary>
        public void RefreshInteractable()
        {
            if (playButton == null) return;

            // Nothing is playable while a card is waiting on a click: a pick has to be the only move the
            // fight is taking, so every other card goes dead instead of queueing up behind it.
            bool playable = Playable && manager != null && manager.AcceptsPlays;
            playButton.interactable = playable && Card != null && manager.CanAfford(Card);
        }

        void Render(CardData card)
        {
            Card = card;

            if (card == null)
            {
                if (nameText != null) nameText.text = "-";
                if (playButton != null) playButton.interactable = false;
                return;
            }

            bool mana = card.costType == CardCostType.Mana;

            // A card whose number comes out of the fight rather than off the card is read with the fight's
            // state, so Empty Pockets counts the hand you are actually holding and Chain Reaction counts the
            // cards that have already gone.
            CardContext context = Context();
            int value = DisplayValue(card, context);

            // Anything at or below the printed base is unremarkable; only a genuine boost is highlighted,
            // and only while the card is being read. At rest a card shows what it was printed with, so
            // there is nothing to highlight yet.
            bool boosted = inspecting && card.valueSource == CardValueSource.Fixed && card.StatBonus(stats) > 0;

            if (nameText != null)
            {
                nameText.text = card.cardName;
                nameText.color = NameColorFor(card.rarity);
            }

            if (costText != null)
            {
                // Read through the manager when there is one, so a relic's discount shows on the card it was
                // applied to rather than only being taken off at the moment of paying for it.
                costText.text = (manager != null ? manager.CostOf(card) : card.cost).ToString();
                costText.color = mana ? ManaCostColor : EnergyCostColor;
            }

            if (costBadge != null) costBadge.color = mana ? ManaBadgeColor : EnergyBadgeColor;

            if (artImage != null)
            {
                if (card.art != null)
                {
                    artImage.sprite = card.art;
                    artImage.color = Color.white;
                }
                else
                {
                    // An Image with no sprite still draws, so the window reads as art rather than a gap.
                    artImage.sprite = null;
                    artImage.color = ArtFor(card);
                }
            }

            if (frame != null) frame.color = card.persistent ? BoardTint : ColorFor(card.cardType);

            if (valueText != null)
            {
                valueText.text = value.ToString();
                valueText.color = boosted ? BoostedNumber : Color.white;
            }

            if (descriptionText != null)
            {
                ConfigureDescriptionText(card);
                descriptionText.text = card.BuildDescription(stats, Coloured(value, boosted), context, value);
            }

            RefreshInteractable();
        }

        /// <summary>
        /// The number to draw. At rest that is the value printed on the card; once the card is being read
        /// it is what the player's attributes make of it, which is the whole point of hovering.
        ///
        /// Two kinds of card have nothing printed to fall back on, so they read the same either way. A card
        /// that counts the fight (Chain Reaction, Empty Pockets) has a per-card amount where the printed
        /// value would be, and a Ripost deals your shielding, which is not a number on the card at all.
        /// </summary>
        int DisplayValue(CardData card, CardContext context)
        {
            if (inspecting) return card.ValueFor(stats, context);
            if (card.valueSource != CardValueSource.Fixed) return card.ValueFor(stats, context);
            if (card.effect == CardEffect.DamageEqualToBlock) return card.ValueFor(stats, context);

            return card.baseValue;
        }

        /// <summary>Wraps the total in rich text colour markup so TMP draws just the number tinted.</summary>
        static string Coloured(int value, bool boosted)
        {
            return "<size=" + DescriptionInlineSize.ToString("0") + "%><color=#" + (boosted ? BoostedHex : PlainHex) + ">" + value + "</color></size>";
        }

        /// <summary>
        /// The fight state a dynamic card's number is read from. Outside a fight there is none and the card
        /// falls back to its printed value, which is what the deck grid and the reward screen show.
        /// </summary>
        CardContext Context()
        {
            CombatManager combat = CombatManager.Instance;
            return combat != null ? combat.Context() : null;
        }

        /// <summary>
        /// The card's name carries its rarity, which is the only place on the card with room for it and the
        /// place the eye already goes. Common is left plain so the other two stand out against it.
        /// </summary>
        static Color NameColorFor(CardRarity rarity)
        {
            switch (rarity)
            {
                case CardRarity.Rare: return RareName;
                case CardRarity.Uncommon: return UncommonName;
                default: return Color.white;
            }
        }

        static Color ColorFor(CardType type)
        {
            switch (type)
            {
                case CardType.Attack: return AttackColor;
                case CardType.Block: return BlockColor;
                default: return SkillColor;
            }
        }

        static Color ArtFor(CardData card)
        {
            if (card.persistent) return BoardArt;

            switch (card.cardType)
            {
                case CardType.Attack: return AttackArt;
                case CardType.Block: return BlockArt;
                default: return SkillArt;
            }
        }

        void ConfigureDescriptionText(CardData card)
        {
            if (descriptionText == null || card == null) return;

            int length = card.BuildDescription(stats, null, Context()).Length;
            float pressure = Mathf.InverseLerp(DescriptionCharsShort, DescriptionCharsLong, length);
            float maxFontSize = Mathf.Lerp(DescriptionFontShort, DescriptionFontLong, pressure);

            descriptionText.enableAutoSizing = true;
            descriptionText.fontSizeMax = maxFontSize;
            descriptionText.fontSizeMin = Mathf.Max(7f, maxFontSize - 4.5f);
            descriptionText.fontSize = maxFontSize;
        }

        // ---------------------------------------------------------------- interaction

        void OnClicked()
        {
            CombatManager combat = manager != null ? manager : CombatManager.Instance;
            if (combat == null || Card == null || handIndex < 0) return;
            combat.TryPlayCard(handIndex);
        }

        /// <summary>
        /// Called by the card's hover effect. Shows the keyword glossary and switches the number over to
        /// what the player's attributes make of it.
        /// </summary>
        public void SetHovered(bool hovered)
        {
            SetLifted(hovered);

            if (!hovered)
            {
                CardTooltip open = CardTooltip.Instance;
                if (open != null && open.Owner == (Component)this) open.Hide();
                HoverChanged?.Invoke(false);
                return;
            }

            if (Card == null) return;

            // One tooltip serves the whole scene, cards and relics alike, so a second one can never be
            // put up beside the first.
            CardTooltip tooltip = CardTooltip.Shared(tooltipPrefab);
            if (tooltip != null) tooltip.Show(Card, (RectTransform)transform, this);
            HoverChanged?.Invoke(true);
        }

        /// <summary>
        /// The value state on its own, without the tooltip. A card laid out by a Palm Trick is being offered
        /// rather than pointed at, so it draws its real numbers while staying quiet.
        /// </summary>
        public void SetLifted(bool lifted)
        {
            if (lifted == inspecting) return;

            inspecting = lifted;

            if (!inspecting)
            {
                StopDescriptionAnimation();
                if (Card != null) Render(Card);
                return;
            }

            if (Card != null) Render(Card);

            if (Card != null && Card.StatBonus(stats) > 0)
            {
                Bounce();
                StartDescriptionAnimation();
            }
        }

        /// <summary>Pops the value number up and back, so a boosted card draws the eye.</summary>
        void Bounce()
        {
            if (valueText == null || !isActiveAndEnabled) return;

            if (bounceRoutine != null) StopCoroutine(bounceRoutine);
            bounceRoutine = StartCoroutine(BounceRoutine((RectTransform)valueText.transform));
        }

        void StartDescriptionAnimation()
        {
            if (descriptionText == null || !isActiveAndEnabled) return;

            if (descriptionShakeRoutine != null) StopCoroutine(descriptionShakeRoutine);

            int value = Card != null ? Card.ValueFor(stats, Context()) : 0;
            animatedNumberLoop = doubleDigitHoverAnimationOnly && value >= 10;
            descriptionShakeRoutine = StartCoroutine(AnimateDescriptionNumberRoutine());
        }

        void StopDescriptionAnimation()
        {
            if (descriptionShakeRoutine != null)
            {
                StopCoroutine(descriptionShakeRoutine);
                descriptionShakeRoutine = null;
            }

            animatedNumberLoop = false;

            if (descriptionText != null)
            {
                descriptionText.ForceMeshUpdate();
                descriptionText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
            }
        }

        IEnumerator AnimateDescriptionNumberRoutine()
        {
            if (descriptionText == null)
            {
                descriptionShakeRoutine = null;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < DescriptionShakeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / DescriptionShakeDuration);
                float wobble = Mathf.Sin(progress * Mathf.PI * 8f) * DescriptionShakeDistance * (1f - progress * 0.6f);
                float scale = 1f + 0.05f * Mathf.Sin(progress * Mathf.PI * 4f);
                float rise = Mathf.Lerp(0f, 3f, progress);
                ApplyDescriptionNumberOffset(wobble, rise, scale);
                yield return null;
            }

            if (!animatedNumberLoop)
            {
                ApplyDescriptionNumberOffset(0f, 3f, 1f);
                descriptionShakeRoutine = null;
                yield break;
            }

            while (inspecting)
            {
                float wobble = Mathf.Sin(Time.unscaledTime * DescriptionLoopFrequency) * DescriptionShakeDistance;
                float scale = 1f + 0.025f * Mathf.Sin(Time.unscaledTime * (DescriptionLoopFrequency * 0.5f));
                float rise = 3f + Mathf.Sin(Time.unscaledTime * (DescriptionLoopFrequency * 0.25f)) * 0.4f;
                ApplyDescriptionNumberOffset(wobble, rise, scale);
                yield return null;
            }

            ApplyDescriptionNumberOffset(0f, 3f, 1f);
            descriptionShakeRoutine = null;
        }

        void ApplyDescriptionNumberOffset(float xOffset, float yOffset, float scale)
        {
            if (descriptionText == null) return;

            descriptionText.ForceMeshUpdate();
            TMP_TextInfo textInfo = descriptionText.textInfo;

            float basePointSize = descriptionText.fontSize;

            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
                if (!charInfo.isVisible) continue;
                if (charInfo.pointSize <= basePointSize * 1.05f) continue;

                int materialIndex = charInfo.materialReferenceIndex;
                int vertexIndex = charInfo.vertexIndex;
                Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;

                Vector3 midPoint = (vertices[vertexIndex] + vertices[vertexIndex + 2]) / 2f;
                Vector3 offset = new Vector3(xOffset, yOffset, 0f);
                Vector3 scaleOffset = (midPoint - (Vector3)charInfo.bottomLeft) * (scale - 1f);

                vertices[vertexIndex + 0] += offset + scaleOffset;
                vertices[vertexIndex + 1] += offset + scaleOffset;
                vertices[vertexIndex + 2] += offset + scaleOffset;
                vertices[vertexIndex + 3] += offset + scaleOffset;
            }

            descriptionText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }

        IEnumerator BounceRoutine(RectTransform target)
        {
            const float duration = 0.34f;
            const float peak = 0.25f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);

                // A single sine arc: up quickly, back down, settling exactly at scale 1.
                float scale = 1f + peak * Mathf.Sin(progress * Mathf.PI);
                target.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            target.localScale = Vector3.one;
            bounceRoutine = null;
        }
    }
}

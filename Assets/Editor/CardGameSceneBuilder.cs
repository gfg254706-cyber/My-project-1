#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DungeonCards.EditorTools
{
    /// <summary>
    /// One-shot content and scene generator for the dungeon card game.
    /// Run it from Tools > Dungeon Cards > Build Game Scenes.
    /// It creates the card, item, enemy and event assets, the UI prefabs and the six game scenes, then
    /// rewrites the build settings with the main menu first. Re-running it updates in place.
    /// </summary>
    public static class CardGameSceneBuilder
    {
        const string DataFolder = "Assets/GameData";
        const string CardsFolder = DataFolder + "/Cards";
        const string ItemsFolder = DataFolder + "/Items";
        const string RelicsFolder = DataFolder + "/Relics";
        const string EnemiesFolder = DataFolder + "/Enemies";
        const string EventsFolder = DataFolder + "/Events";
        const string PrefabsFolder = "Assets/Prefabs";
        const string ScenesFolder = "Assets/Scenes";
        const string ResourcesFolder = "Assets/Resources";
        const string DatabasePath = ResourcesFolder + "/GameDatabase.asset";

        const string CardPrefabPath = PrefabsFolder + "/CardView.prefab";
        const string RelicIconPrefabPath = PrefabsFolder + "/RelicIcon.prefab";

        const string ItemRowPrefabPath = PrefabsFolder + "/ItemRow.prefab";
        const string PathButtonPrefabPath = PrefabsFolder + "/PathButton.prefab";
        const string SkillRowPrefabPath = PrefabsFolder + "/SkillRow.prefab";
        const string EventOptionPrefabPath = PrefabsFolder + "/EventOption.prefab";
        const string TooltipPrefabPath = PrefabsFolder + "/CardTooltip.prefab";
        const string UiFolder = "Assets/UI";
        const string CircleSpritePath = UiFolder + "/CostCircle.png";

        /// <summary>
        /// Cards are drawn at a fixed 2:3 ratio. The deck grid uses this same box, so the aspect ratio
        /// fitter and the grid layout never argue about how big a card should be.
        /// </summary>
        static readonly Vector2 CardSize = new Vector2(180f, 270f);
        const float CardAspectRatio = 2f / 3f;

        static readonly string[] SceneOrder =
        {
            GameSession.MainMenuScene,
            GameSession.CombatScene,
            GameSession.RewardScene,
            GameSession.PathSelectionScene,
            GameSession.RoomScene,
            GameSession.CharacterScene
        };

        static readonly string[] ObsoleteScenes = { "DeckEditor", "Inventory" };

        static readonly Color BackgroundColor = new Color(0.06f, 0.06f, 0.09f, 1f);
        static readonly Color PanelColor = new Color(0.11f, 0.12f, 0.17f, 1f);
        static readonly Color ButtonColor = new Color(0.22f, 0.30f, 0.48f, 1f);
        static readonly Color MutedTextColor = new Color(0.72f, 0.76f, 0.84f, 1f);
        static readonly Color AccentColor = new Color(0.62f, 0.86f, 1f, 1f);

        static Font uiFont;

        [MenuItem("Tools/Dungeon Cards/Build Game Scenes")]
        public static void BuildGameScenes()
        {
            EditorSceneManager.SaveOpenScenes();

            // The card UI is built out of TextMeshPro, so its essential resources have to be present.
            TmpEssentials.Ensure();

            EnsureFolders();

            // --- content ---------------------------------------------------------
            List<CardData> cards = BuildCards();
            List<ItemData> items = BuildItems();

            // Relics join the same item list the database carries: they are charms the shop's relic rack
            // trades in, and FillRack already splits that list on the relic flag. The templates come with no
            // roll, so nothing here decides what any individual relic is worth.
            foreach (RelicDefinition relic in BuildRelics()) items.Add(relic);

            EnsureRelicSettings();

            List<EnemyData> enemies = BuildEnemies();
            EnemyData boss = CreateBossEnemy();

            var events = CreateEvents(items, boss);
            // One piece for every body slot but the rings, plus a charm for the bag. The player starts
            // dressed, so the first equipment decision is what to replace rather than what to fill.
            var startingItems = new List<ItemData>
            {
                FindItem(items, "Blade of Vigor"),
                FindItem(items, "Worn Leather Cap"),
                FindItem(items, "Iron Plate"),
                FindItem(items, "Frayed Trousers"),
                FindItem(items, "Muddy Boots")
            };
            GameDatabase database = BuildDatabase(cards, items, enemies, events, startingItems, boss);

            // --- prefabs ---------------------------------------------------------
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject tooltipPrefab = BuildTooltipPrefab();
            GameObject cardPrefab = BuildCardPrefab(tooltipPrefab);
            GameObject relicIconPrefab = BuildRelicIconPrefab(tooltipPrefab);
            GameObject itemRowPrefab = BuildItemRowPrefab();

            GameObject pathButtonPrefab = BuildPathButtonPrefab();
            GameObject skillRowPrefab = BuildSkillRowPrefab();
            GameObject eventOptionPrefab = BuildEventOptionPrefab();

            AssetDatabase.SaveAssets();

            // --- scenes ----------------------------------------------------------
            BuildMainMenuScene(database);
            BuildCombatScene(database, cardPrefab);
            BuildRewardScene(database, cardPrefab);
            BuildPathSelectionScene(database, pathButtonPrefab);
            BuildRoomScene(database, eventOptionPrefab, cardPrefab, relicIconPrefab);
            BuildCharacterScene(database, itemRowPrefab, skillRowPrefab, cardPrefab);


            DeleteObsoleteScenes();
            SetBuildScenes(SceneOrder);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorSceneManager.OpenScene(ScenesFolder + "/" + GameSession.MainMenuScene + ".unity");
            Debug.Log("[CardGameSceneBuilder] Built content and scenes. Press Play in MainMenu to start a run.");
        }

        // ------------------------------------------------------------------ folders

        static void EnsureFolders()
        {
            string[] folders = { DataFolder, CardsFolder, ItemsFolder, RelicsFolder, EnemiesFolder, EventsFolder, PrefabsFolder, ScenesFolder, ResourcesFolder, UiFolder };
            foreach (string folder in folders)
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;

                string parent = Path.GetDirectoryName(folder);
                if (string.IsNullOrEmpty(parent)) continue;
                parent = parent.Replace('\\', '/');

                string leaf = Path.GetFileName(folder);
                if (AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder(parent, leaf);
            }
        }

        static void DeleteObsoleteScenes()
        {
            foreach (string name in ObsoleteScenes)
            {
                string path = ScenesFolder + "/" + name + ".unity";
                if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            }
        }

        static void SetBuildScenes(string[] sceneNames)
        {
            var list = new List<EditorBuildSettingsScene>();
            foreach (string name in sceneNames)
            {
                string path = ScenesFolder + "/" + name + ".unity";
                if (File.Exists(path)) list.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------ content assets

        /// <summary>
        /// Creates a card with everything cleared first, so re-running the builder cannot leave a stale
        /// scaling rate or a persistent flag behind on an asset that already existed.
        /// </summary>
        static CardData CreateCard(string cardName, CardArchetype archetype, CardType type, CardEffect effect,
                                   CardCostType costType, int cost, int baseValue,
                                   StatType scalingStat, float perPoint, bool persistent)
        {
            string path = CardsFolder + "/" + cardName.Replace(" ", "") + ".asset";
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (card == null)
            {
                card = ScriptableObject.CreateInstance<CardData>();
                AssetDatabase.CreateAsset(card, path);
            }

            card.cardName = cardName;
            card.archetype = archetype;
            card.cardType = type;
            card.effect = effect;
            card.persistent = persistent;
            card.costType = costType;
            card.cost = cost;
            card.baseValue = baseValue;

            // The scaling tag is authored from the rate rather than remembered beside it. A card that prints a
            // rate reads that attribute; a card that prints none reads nothing, and says so. That is what
            // retires the raw stat-scaling every construct used to carry: they were written against Strength
            // and held harmless only by a zero sitting next to it.
            //
            // The handful of cards whose formula reads an attribute through something other than the printed
            // rate - a hit count, a gold rate, a permanent price - set the tag themselves where they are
            // authored, because only the card knows which of its numbers moves.
            card.scaling = perPoint != 0f ? ScalingFor(scalingStat) : CardStatScaling.None;
            card.valuePerStatPoint = perPoint;
            card.appliesStatus = StatusEffectType.None;
            card.statusMagnitude = 0;
            card.statusDuration = 0;
            card.statusTargetsSelf = false;

            // Every rider the pool can set is cleared here rather than at the handful of call sites that
            // remember to. A card that stops using a rider keeps it on the asset otherwise, which is how
            // Reckless Stance went on charging health for a turn after it stopped being a stance. Art is
            // deliberately left alone: it is assigned by hand, so a rebuild must not take it off a card.
            card.hits = 1;
            card.hitsScaleWithStat = false;
            card.valueSource = CardValueSource.Fixed;
            card.allowsNegativeValue = false;
            card.drawsCards = 0;
            card.energyGain = 0;
            card.bonusIfFirstCard = 0;
            card.discardsHand = false;
            card.energyPerCardDiscarded = 0;
            card.generatedPerCardDiscarded = 0;
            card.generatedCards = 0;
            card.returnsToHand = false;
            card.freeAction = false;
            card.bonusGoldOnKill = 0;
            card.goldPerStatPoint = 0f;
            card.doubleIfCardsPlayed = 0;
            card.discardRandomEachTurn = false;
            card.selfDamagePerTurn = 0;
            card.trigger = CardTrigger.StartOfTurn;
            card.triggerCardsPlayedLastTurn = 0;
            card.constructDuration = 0;
            card.construct = ConstructKind.None;
            card.sacrificable = false;
            card.permanentStatChange = 0;
            card.permanentStatChangePerStatPoint = 0f;
            card.permanentStatTarget = StatType.Strength;

            card.description = card.Summary(baseValue);


            EditorUtility.SetDirty(card);
            return card;
        }

        /// <summary>
        /// The card pool. Three archetypes, each one a different relationship with time: Strength is
        /// strong from turn one, Agility spends now to draw more now, and Intellect pays a heavy price
        /// early to own the late turns.
        /// </summary>
        static List<CardData> BuildCards()
        {
            var cards = new List<CardData>();

            // ------------------------------------------------------------------ Strength
            // The line that trades with the fight directly. Everything here is either a bigger number or
            // a way to make the next number bigger, and its price is paid in the run itself.

            // The deck's own attack, and the one attack in the pool that does not scale. A flat six for one
            // energy, so the opening fights read as a total rather than as a build.
            cards.Add(Setup(CreateCard("Strike", CardArchetype.Strength, CardType.Attack, CardEffect.Damage,
                CardCostType.Energy, 1, 6, StatType.Strength, 0f, false), CardRarity.Common));

            cards.Add(Setup(CreateCard("Armor Up", CardArchetype.Strength, CardType.Block, CardEffect.Block,
                CardCostType.Energy, 1, 5, StatType.Strength, 0.5f, false), CardRarity.Common));

            cards.Add(Setup(CreateCard("Cleave", CardArchetype.Strength, CardType.Attack, CardEffect.Damage,
                CardCostType.Energy, 2, 7, StatType.Strength, 1f, false), CardRarity.Common));

            // +3 Strength for this turn only: a burst that makes the rest of the turn hit harder.
            cards.Add(Setup(CreateCard("Furor", CardArchetype.Strength, CardType.Skill, CardEffect.TemporaryStrength,
                CardCostType.Energy, 1, 3, StatType.Strength, 0f, false), CardRarity.Common));

            // Turns the shielding you built this turn into a hit. Free, so it can ride on a full turn.
            cards.Add(Setup(CreateCard("Ripost", CardArchetype.Strength, CardType.Attack, CardEffect.DamageEqualToBlock,
                CardCostType.Energy, 0, 0, StatType.Strength, 0f, false), CardRarity.Common));

            // ---- the Warrior set

            // Eight damage is a poor rate at three energy. The gold is what it is actually for, and the
            // scaling means it gets better at the job the more Strength you have committed to.
            CardData glory = CreateCard("Glory", CardArchetype.Strength, CardType.Attack, CardEffect.Damage,
                CardCostType.Energy, 3, 8, StatType.Strength, 0f, false);
            glory.rarity = CardRarity.Uncommon;
            glory.bonusGoldOnKill = 30;
            glory.goldPerStatPoint = 0.5f;

            // Eight flat damage, but the gold it pays on a kill is bought with Strength.
            glory.scaling = CardStatScaling.Strength;
            cards.Add(glory);

            // The answer to an enemy that turtles. Worthless against one that does not, which is the
            // trade: it is only ever as good as what the fight puts in front of it.
            cards.Add(Setup(CreateCard("Sunder", CardArchetype.Strength, CardType.Attack, CardEffect.DamageToBlock,
                CardCostType.Energy, 2, 0, StatType.Strength, 1f, false), CardRarity.Common));

            cards.Add(Setup(CreateCard("Big Stick", CardArchetype.Strength, CardType.Attack, CardEffect.Damage,
                CardCostType.Energy, 3, 20, StatType.Strength, 0.5f, false), CardRarity.Common));

            // Two points of shielding per point of Strength is the best block rate in the game, on the
            // archetype that is otherwise made of damage.
            cards.Add(Setup(CreateCard("Fortify", CardArchetype.Strength, CardType.Block, CardEffect.Block,
                CardCostType.Energy, 2, 5, StatType.Strength, 2f, false), CardRarity.Common));

            // Two points of damage per point of Strength, all of it loaded onto the next thing that swings.
            // Free, and worth nothing at all until there is Strength behind it, which is exactly the gamble:
            // the whole card is spent on one attack, and an enemy that blocks that attack takes none of it.
            CardData reckless = CreateCard("Reckless Stance", CardArchetype.Strength, CardType.Skill,
                CardEffect.NextAttackBonus, CardCostType.Energy, 0, 0, StatType.Strength, 2f, false);
            reckless.rarity = CardRarity.Rare;
            cards.Add(reckless);

            // One Strength for every card still in hand, which is a card that wants to be played first.
            CardData fightOrFlight = CreateCard("Fight or Flight", CardArchetype.Strength, CardType.Skill,
                CardEffect.TemporaryStrength, CardCostType.Energy, 2, 1, StatType.Strength, 0f, false);
            fightOrFlight.rarity = CardRarity.Uncommon;
            fightOrFlight.valueSource = CardValueSource.PerCardInHand;
            cards.Add(fightOrFlight);

            // Pays off for the cards other archetypes load the draw pile with, so it is the one Strength
            // card that wants company.
            cards.Add(Setup(CreateCard("Jungle Might", CardArchetype.Strength, CardType.Skill,
                CardEffect.PruneDrawPile, CardCostType.Energy, 3, 0, StatType.Strength, 0f, false), CardRarity.Rare));

            // An enemy has no attributes to take, so what is stolen is its attack, for the rest of the
            // fight. Against a hard hitter this is worth more than any block card.
            cards.Add(Setup(CreateCard("Intimidate", CardArchetype.Strength, CardType.Skill,
                CardEffect.StealStrength, CardCostType.Energy, 3, 1, StatType.Strength, 0.5f, false), CardRarity.Uncommon));

            cards.Add(Setup(CreateCard("Grit", CardArchetype.Strength, CardType.Block, CardEffect.Block,
                CardCostType.Energy, 3, 5, StatType.Strength, 0.25f, false), CardRarity.Uncommon));

            // The way down, and the first of the Willpower cards. Twenty damage for one energy is the best
            // rate in the set, and its price is paid by every Strength card you will draw afterwards. The
            // price is the half that scales: Willpower is how much of yourself there is to spend, so the
            // deeper the commitment the larger the thing each of these asks you to give up.
            CardData allOut = CreateCard("All Out", CardArchetype.Willpower, CardType.Attack, CardEffect.Damage,
                CardCostType.Energy, 1, 20, StatType.Willpower, 0f, false);
            allOut.rarity = CardRarity.Common;
            allOut.permanentStatChange = -2;
            allOut.permanentStatChangePerStatPoint = -1f;
            allOut.permanentStatTarget = StatType.Strength;

            // Twenty flat damage, and the price it takes off you is bought with Willpower.
            allOut.scaling = CardStatScaling.Willpower;
            cards.Add(allOut);

            // Two hits rather than one, so an enemy that blocks soaks it twice as well.
            CardData twinStrike = CreateCard("Twin Strike", CardArchetype.Strength, CardType.Attack,
                CardEffect.Damage, CardCostType.Energy, 2, 7, StatType.Strength, 0f, false);
            twinStrike.rarity = CardRarity.Common;
            twinStrike.hits = 2;
            cards.Add(twinStrike);

            // Four energy to delete a turn. It does nothing to an enemy that spends its turns making
            // itself bigger, which is the counterweight to it being the most expensive card here.
            cards.Add(Setup(CreateCard("Zone", CardArchetype.Strength, CardType.Skill, CardEffect.ReduceEnemyAttack,
                CardCostType.Energy, 4, 0, StatType.Strength, 0f, false), CardRarity.Common));

            // One damage per point of Strength, so it is a filler card until it is a finisher. Scales hard
            // with temporary Strength, which is the whole point of it.
            CardData dice = CreateCard("Dice", CardArchetype.Strength, CardType.Attack, CardEffect.Damage,
                CardCostType.Energy, 1, 1, StatType.Strength, 0f, false);
            dice.rarity = CardRarity.Uncommon;
            dice.hitsScaleWithStat = true;

            // Its printed number never moves; its hit count does, which is a stat-scaling tag all the same.
            dice.scaling = CardStatScaling.Strength;
            cards.Add(dice);

            // ------------------------------------------------------------------ Agility
            // Tempo. Cheap, flat, and the best at finding more cards, which is what makes a turn longer
            // rather than bigger.

            // Flat, like Strike. The two of them are the floor the opening deck stands on, which is why
            // neither of them cares what the player has committed to.
            cards.Add(Setup(CreateCard("Defend", CardArchetype.Agility, CardType.Block, CardEffect.Block,
                CardCostType.Energy, 1, 5, StatType.Agility, 0f, false), CardRarity.Common));

            cards.Add(Setup(CreateCard("Quick Jab", CardArchetype.Agility, CardType.Attack, CardEffect.Damage,
                CardCostType.Energy, 1, 4, StatType.Agility, 0f, false), CardRarity.Common));

            cards.Add(Setup(CreateCard("Feint", CardArchetype.Agility, CardType.Block, CardEffect.Block,
                CardCostType.Energy, 1, 6, StatType.Agility, 0f, false), CardRarity.Common));

            // ---- the Rogue set

            // Draws twice as much when it opens the turn, which is the whole card: it pays for being the
            // first thing you do and is mediocre otherwise.
            CardData quickdraw = CreateCard("Quickdraw", CardArchetype.Agility, CardType.Skill, CardEffect.Draw,
                CardCostType.Energy, 0, 1, StatType.Agility, 0f, false);
            quickdraw.rarity = CardRarity.Common;
            quickdraw.bonusIfFirstCard = 1;
            cards.Add(quickdraw);

            // Damage that replaces itself. The cheapest way to keep a turn going.
            CardData riffle = CreateCard("Riffle", CardArchetype.Agility, CardType.Attack, CardEffect.Damage,
                CardCostType.Energy, 1, 5, StatType.Agility, 0.5f, false);
            riffle.rarity = CardRarity.Common;
            riffle.drawsCards = 1;
            cards.Add(riffle);

            cards.Add(Setup(CreateCard("Sidestep", CardArchetype.Agility, CardType.Block, CardEffect.Block,
                CardCostType.Energy, 1, 4, StatType.Agility, 1f, false), CardRarity.Common));

            // Three small hits. The Agility counterweight to Twin Strike, and equally bad against block.
            CardData flurry = CreateCard("Flurry", CardArchetype.Agility, CardType.Attack, CardEffect.Damage,
                CardCostType.Energy, 2, 4, StatType.Agility, 0f, false);
            flurry.rarity = CardRarity.Common;
            flurry.hits = 3;
            cards.Add(flurry);

            cards.Add(Setup(CreateCard("Read the Room", CardArchetype.Agility, CardType.Skill,
                CardEffect.DiscardOne, CardCostType.Energy, 0, 1, StatType.Agility, 0f, false), CardRarity.Common));

            // Agility for the turn, so it turns a hand of Agility cards into a bigger hand.
            CardData adrenaline = CreateCard("Adrenaline", CardArchetype.Agility, CardType.Skill,
                CardEffect.StatusOnly, CardCostType.Energy, 1, 0, StatType.Agility, 0f, false);
            adrenaline.rarity = CardRarity.Common;
            adrenaline.appliesStatus = StatusEffectType.Agility;
            adrenaline.statusMagnitude = 2;
            adrenaline.statusDuration = 1;
            adrenaline.statusTargetsSelf = true;
            cards.Add(adrenaline);

            // Free and once a turn: it is an extra slot rather than a discount, which makes it a card
            // that is worth having whether or not the turn is going well.
            CardData splitSecond = CreateCard("Split Second", CardArchetype.Agility, CardType.Attack,
                CardEffect.Damage, CardCostType.Energy, 0, 3, StatType.Agility, 0.5f, false);
            splitSecond.rarity = CardRarity.Common;
            splitSecond.freeAction = true;
            cards.Add(splitSecond);

            // Damage that reads the hand backwards: the emptier it is, the harder it hits. Free, so it is
            // the payoff at the end of a long turn.
            CardData emptyPockets = CreateCard("Empty Pockets", CardArchetype.Agility, CardType.Attack,
                CardEffect.Damage, CardCostType.Energy, 0, 5, StatType.Agility, 0f, false);
            emptyPockets.rarity = CardRarity.Common;
            emptyPockets.valueSource = CardValueSource.EmptyHand;
            cards.Add(emptyPockets);

            // The first construct. It is not on a cadence: it is paid every time a free card is played,
            // which is what turns a deck full of free cards into a wall.
            CardData reflexGuard = CreateCard("Reflex Guard", CardArchetype.Agility, CardType.Block,
                CardEffect.Block, CardCostType.Energy, 1, 1, StatType.Agility, 0f, true);
            reflexGuard.rarity = CardRarity.Common;
            reflexGuard.trigger = CardTrigger.ZeroCostCardPlayed;
            cards.Add(reflexGuard);

            // Comes back instead of being spent, so it is a card you hold rather than one you use up.
            CardData boomerang = CreateCard("Boomerang", CardArchetype.Agility, CardType.Attack,
                CardEffect.Damage, CardCostType.Energy, 1, 6, StatType.Agility, 1f, false);
            boomerang.rarity = CardRarity.Uncommon;
            boomerang.returnsToHand = true;
            cards.Add(boomerang);

            cards.Add(Setup(CreateCard("Palm Trick", CardArchetype.Agility, CardType.Skill,
                CardEffect.LookAtTopCards, CardCostType.Energy, 0, 3, StatType.Agility, 0f, false), CardRarity.Uncommon));

            cards.Add(Setup(CreateCard("Wild Card", CardArchetype.Agility, CardType.Skill,
                CardEffect.GenerateCard, CardCostType.Energy, 1, 1, StatType.Agility, 0f, false), CardRarity.Uncommon));

            // Draws off Agility, and with Agility driven negative it hands the cards back instead. The
            // direct parallel to what negative Strength does to a damage card.
            CardData overdraw = CreateCard("Overdraw", CardArchetype.Agility, CardType.Skill, CardEffect.Draw,
                CardCostType.Energy, 1, 0, StatType.Agility, 1f, false);
            overdraw.rarity = CardRarity.Uncommon;
            overdraw.allowsNegativeValue = true;
            cards.Add(overdraw);

            // Throws the hand away and turns it into energy, so it wants a full hand and a turn already
            // in progress.
            CardData looseGrip = CreateCard("Loose Grip", CardArchetype.Agility, CardType.Skill,
                CardEffect.DiscardHand, CardCostType.Energy, 0, 0, StatType.Agility, 0f, false);
            looseGrip.rarity = CardRarity.Uncommon;
            looseGrip.energyPerCardDiscarded = 1;
            cards.Add(looseGrip);

            // The damage construct. Two of these plus a hand of free cards is the payoff the archetype is
            // built toward.
            CardData twinBlades = CreateCard("Twin Blades", CardArchetype.Agility, CardType.Attack,
                CardEffect.Damage, CardCostType.Energy, 2, 2, StatType.Agility, 0.3f, true);
            twinBlades.rarity = CardRarity.Uncommon;
            twinBlades.trigger = CardTrigger.ZeroCostCardPlayed;
            cards.Add(twinBlades);

            // The mirror of Quickdraw: it wants to be the last card of a long turn rather than the first.
            // Two of them are a bad deal; the third card onward, it is the best rate in the set.
            CardData backstab = CreateCard("Backstab", CardArchetype.Agility, CardType.Attack,
                CardEffect.Damage, CardCostType.Energy, 2, 6, StatType.Agility, 1f, false);
            backstab.rarity = CardRarity.Uncommon;
            backstab.doubleIfCardsPlayed = 3;
            cards.Add(backstab);

            // Damage for the length of the turn rather than for the size of any one card. Free cards feed
            // it twice: they are cheap to play and they raise the count.
            CardData chainReaction = CreateCard("Chain Reaction", CardArchetype.Agility, CardType.Attack,
                CardEffect.Damage, CardCostType.Energy, 2, 2, StatType.Agility, 0f, false);
            chainReaction.rarity = CardRarity.Rare;
            chainReaction.valueSource = CardValueSource.PerCardPlayed;
            cards.Add(chainReaction);

            // The construct that only keeps going while the turns before it were busy. It is the reward
            // for the whole archetype and it switches itself off the moment the turn gets thin.
            CardData momentumLoop = CreateCard("Momentum Loop", CardArchetype.Agility, CardType.Skill,
                CardEffect.Draw, CardCostType.Energy, 2, 2, StatType.Agility, 0f, true);
            momentumLoop.rarity = CardRarity.Rare;
            momentumLoop.trigger = CardTrigger.StartOfTurnAfterMany;
            momentumLoop.triggerCardsPlayedLastTurn = 4;
            momentumLoop.energyGain = 1;
            cards.Add(momentumLoop);

            // Eight Agility for nothing, and a card thrown away every turn for the rest of the fight. The
            // cheapest way to make a hand enormous and the fastest way to run out of deck.
            CardData overclock = CreateCard("Overclock", CardArchetype.Agility, CardType.Skill,
                CardEffect.StatusOnly, CardCostType.Energy, 0, 0, StatType.Agility, 0f, false);
            overclock.rarity = CardRarity.Rare;
            overclock.appliesStatus = StatusEffectType.Agility;
            overclock.statusMagnitude = 8;
            overclock.statusDuration = 0;
            overclock.statusTargetsSelf = true;
            overclock.discardRandomEachTurn = true;
            cards.Add(overclock);

            // The slot machine: everything in hand becomes something else, which is the Agility line's
            // random generation at its most extreme.
            CardData vanishingAct = CreateCard("Vanishing Act", CardArchetype.Agility, CardType.Skill,
                CardEffect.DiscardHand, CardCostType.Energy, 1, 0, StatType.Agility, 0f, false);
            vanishingAct.rarity = CardRarity.Rare;
            vanishingAct.generatedPerCardDiscarded = 1;
            cards.Add(vanishingAct);

            // ------------------------------------------------------------------ Intellect
            // The auto-battler. Expensive, slow, and permanent once it is running.

            // 3 energy for 1 mana, plus one more per five Intellect.
            cards.Add(Setup(CreateCard("Create Mana", CardArchetype.Intellect, CardType.Skill, CardEffect.GainMana,
                CardCostType.Energy, 3, 1, StatType.Intellect, 0.2f, false), CardRarity.Common));

            // Energy that keeps arriving, which is what makes the long game reachable.
            cards.Add(Setup(CreateCard("Aether Conduit", CardArchetype.Intellect, CardType.Skill, CardEffect.GainEnergy,
                CardCostType.Energy, 3, 1, StatType.Intellect, 0f, true), CardRarity.Uncommon));

            cards.Add(Setup(CreateCard("Mana Font", CardArchetype.Intellect, CardType.Skill, CardEffect.GainMana,
                CardCostType.Energy, 2, 1, StatType.Intellect, 0f, true), CardRarity.Uncommon));

            // The payoff: damage and shielding that renew themselves every turn.
            cards.Add(Setup(CreateCard("Sigil of Embers", CardArchetype.Intellect, CardType.Attack, CardEffect.Damage,
                CardCostType.Mana, 1, 2, StatType.Intellect, 0.5f, true), CardRarity.Uncommon));

            cards.Add(Setup(CreateCard("Warding Sigil", CardArchetype.Intellect, CardType.Block, CardEffect.Block,
                CardCostType.Mana, 1, 2, StatType.Intellect, 0.5f, true), CardRarity.Uncommon));

            cards.Add(Setup(CreateCard("Mana Burn", CardArchetype.Intellect, CardType.Attack, CardEffect.Damage,
                CardCostType.Mana, 2, 4, StatType.Intellect, 0.75f, false), CardRarity.Common));

            // The construct on a clock: it is played once and does its work on the next four of your turns,
            // which is what a duration is for. A construct with no duration of its own runs for the fight.
            CardData tribunal = CreateCard("The Tribunal", CardArchetype.Intellect, CardType.Skill,
                CardEffect.LookAtTopCardsKeepOne, CardCostType.Energy, 2, 3, StatType.Intellect, 0f, true);
            tribunal.rarity = CardRarity.Uncommon;
            tribunal.constructDuration = 4;
            cards.Add(tribunal);

            // ------------------------------------------------------------------ Willpower
            // The sacrifice archetype. Everything here buys something outright and bills the run for it, and
            // the bill is what Willpower widens: the stat is never a resource and never a bonus, it is how
            // much of yourself each of these is allowed to take.

            // Thirteen shielding for two energy, bought with Strength. The safe way down, and the one that
            // pays the whole Willpower commitment out at once: the shielding is flat and the price is not.
            CardData exhaust = CreateCard("Exhaust", CardArchetype.Willpower, CardType.Block, CardEffect.Block,
                CardCostType.Energy, 2, 13, StatType.Willpower, 0f, false);
            exhaust.rarity = CardRarity.Common;
            exhaust.permanentStatChange = -3;
            exhaust.permanentStatChangePerStatPoint = -0.5f;
            exhaust.permanentStatTarget = StatType.Strength;

            // The shielding is flat, and what it costs you is bought with Willpower.
            exhaust.scaling = CardStatScaling.Willpower;
            cards.Add(exhaust);

            BuildConstructs(cards);

            foreach (CardData card in cards)
            {
                card.description = card.Summary(card.baseValue);
                EditorUtility.SetDirty(card);
            }
            return cards;
        }

        /// <summary>
        /// One card from the neutral construct pool.
        ///
        /// Every construct is authored the same way: persistent, reading no attribute of any kind, and with
        /// its whole behaviour named by its ConstructKind rather than carried by an effect value.
        ///
        /// The archetype it is authored against is an ownership tag, not a scaling one. A construct belongs
        /// to a line - a Relay Node is an Intellect card and sits with the rest of the Intellect engine -
        /// while its numbers come out of ConstructRules and no attribute moves them. Those are two different
        /// questions, which is why the archetype is passed in here and the scaling tag is left at None by
        /// CreateCard's zero rate.
        ///
        /// The flag is the other half of the same idea: whether a construct may be given up by the player is
        /// a fact about that construct, so it is authored with the card rather than derived from its kind.
        ///
        /// The description is the whole card. A construct's rules are a paragraph rather than a substituted
        /// number, which is why the printed value is zero and the effect is CardEffect.Construct: there is
        /// nothing for the card UI to resolve, colour or mark as boosted.
        /// </summary>
        static CardData Construct(string cardName, CardArchetype archetype, CardCostType costType, int cost,
                                  int duration, ConstructKind kind, bool sacrificable, string description)
        {
            CardData card = CreateCard(cardName, archetype, CardType.Skill, CardEffect.Construct,
                costType, cost, 0, StatType.Strength, 0f, true);

            card.construct = kind;
            card.constructDuration = duration;
            card.sacrificable = sacrificable;
            card.description = description;

            // Common, so the pool is offered often enough to build with. A construct is meant to be something
            // a deck is built around, and it cannot be that if it is rarely turned up.
            card.rarity = CardRarity.Common;

            return card;
        }

        /// <summary>
        /// The neutral construct pool.
        ///
        /// These are the cards that do not scale with anything. Each one is a machine with its own clock, so
        /// the pool reads as a set of engines a deck is assembled around rather than as a fifth attribute: a
        /// Generator to bank with, an Amplifier to multiply it, Timers that spend themselves, Reactors that
        /// answer what the rest of the deck did, and a Converter that wants a hand too big to use.
        ///
        /// Every number behind these is in ConstructRules, and every one of them is written there rather than
        /// here, so a card's wording and its rules cannot drift apart.
        /// </summary>
        static void BuildConstructs(List<CardData> cards)
        {
            // ------------------------------------------------------------------ the Generator
            // The one that makes something out of nothing, and asks for a small hand to be allowed to.

            cards.Add(Construct("Dormant Engine", CardArchetype.Intellect, CardCostType.Energy, 2, 5, ConstructKind.DormantEngine, false,
                "Gains 1 Charge each turn, and does nothing with them while you are holding 5 or more cards. " +
                "Once the hand is down to 4 or fewer it gains 1 energy for every Charge it has stored, and " +
                "starts over."));

            // ------------------------------------------------------------------ the Amplifier
            // It does not do anything itself. It is what makes everything else do more.

            cards.Add(Construct("Relay Node", CardArchetype.Intellect, CardCostType.Mana, 2, 5, ConstructKind.RelayNode, false,
                "Whenever another construct triggers, gains 1 Charge. At 3 Charges the Charges are spent, and " +
                "triggers a random other construct an additional time. A construct can only be relayed into " +
                "once a turn."));

            // ------------------------------------------------------------------ the Timers
            // Both of these are a countdown with a price at the end, and both of them are worth taking off
            // the board early.

            cards.Add(Construct("Fuse", CardArchetype.Willpower, CardCostType.Energy, 1, 5, ConstructKind.Fuse, true,
                "Gains 1 Charge each turn it is still standing, and deals 8 damage per Charge when it runs " +
                "out. It can be sacrificed at any time to set it off early."));

            cards.Add(Construct("Overheated Core", CardArchetype.Intellect, CardCostType.Mana, 1, 4, ConstructKind.OverheatedCore, false,
                "At the start of your turn, gains 2 Mana. From its second turn it also gains 1 Heat each turn, " +
                "and at 3 Heat or more it burns you for 10 damage."));

            // ------------------------------------------------------------------ the Reactors
            // They answer what the turn before them did.

            cards.Add(Construct("Reclamation Engine", CardArchetype.Agility, CardCostType.Energy, 2, 4, ConstructKind.ReclamationEngine, false,
                "At the start of your turn, if you discarded 3 or more cards last turn, a random card from " +
                "your discard pile comes back to your hand."));

            cards.Add(Construct("Temporal Anchor", CardArchetype.Intellect, CardCostType.Mana, 2, 1, ConstructKind.TemporalAnchor, false,
                "At the start of your next turn, the last card you played this turn resolves a second time " +
                "for nothing. Then it expires."));

            // ------------------------------------------------------------------ the Converter
            // A hand too big to use is a resource, and this is what it is converted into.

            cards.Add(Construct("Overflowing Archive", CardArchetype.Intellect, CardCostType.Energy, 2, 4, ConstructKind.OverflowingArchive, false,
                "Whenever you end your turn holding more than 7 cards, gains 1 Mana for every card above 7."));

            // ------------------------------------------------------------------ the Conditional and Puzzle constructs
            // These are the ones you play towards rather than play: each of them wants the turn to be a
            // particular shape before it will do anything at all.

            cards.Add(Construct("Archive", CardArchetype.Intellect, CardCostType.Energy, 1, 5, ConstructKind.Archive, false,
                "At the start of your turn, if your hand holds exactly 3 cards, draws 2."));

            cards.Add(Construct("Perfect Alignment", CardArchetype.Intellect, CardCostType.Mana, 2, 4, ConstructKind.PerfectAlignment, false,
                "At the start of your turn, if your hand holds exactly 5 cards, draws 2. Every turn it does " +
                "not, it loses an extra turn of its duration."));

            cards.Add(Construct("Adaptive Core", CardArchetype.Intellect, CardCostType.Mana, 1, 5, ConstructKind.AdaptiveCore, false,
                "The first time in a fight you play a card from a given archetype, gains 1 Charge. At 4 Charges " +
                "it pays out 1 energy for every Charge it holds, and starts over."));

            cards.Add(Construct("Prototype", CardArchetype.Intellect, CardCostType.Energy, 1, 6, ConstructKind.Prototype, false,
                "Does nothing at all. Gaining 1 Charge whenever you play a card that costs 2 or more Energy. " +
                "When it finally expires, it hands over 3 energy and 2 mana for every Charge it collected."));

            // ------------------------------------------------------------------ the Sacrifice constructs
            // Both of these are played for the moment they leave rather than for the turns they run.

            cards.Add(Construct("Forbidden Engine", CardArchetype.Intellect, CardCostType.Mana, 2, 5, ConstructKind.ForbiddenEngine, true,
                "At the start of your turn, gains 2 Mana. Sacrificing it buys 15 shielding and 2 energy."));

            cards.Add(Construct("Successor Protocol", CardArchetype.Intellect, CardCostType.Mana, 1, 4, ConstructKind.SuccessorProtocol, false,
                "Whenever another construct expires or is sacrificed, whatever it did on the way out happens " +
                "again."));

            // ------------------------------------------------------------------ the Gambler
            // The one that does something every turn and never tells you what in advance.

            // Neutral on purpose: it is reached through generation rather than drafted, so it belongs to no
            // line and any deck can end up holding it.
            cards.Add(Construct("Roulette Core", CardArchetype.Neutral, CardCostType.Energy, 1, 4, ConstructKind.RouletteCore, false,
                "At the start of your turn, rolls: gain 3 shielding, deal 5 damage, or draw 1 card."));
        }

        /// <summary>
        /// The scaling tag for a stat a caller names. CreateCard's callers pass an attribute because that is
        /// the only thing a card's formula is written against; whether the card reads it at all is decided by
        /// the rate they pass beside it, so a zero rate is what produces CardStatScaling.None.
        /// </summary>
        static CardStatScaling ScalingFor(StatType stat)
        {
            switch (stat)
            {
                case StatType.Agility: return CardStatScaling.Agility;
                case StatType.Intellect: return CardStatScaling.Intellect;
                case StatType.Vitality: return CardStatScaling.Vitality;
                case StatType.Willpower: return CardStatScaling.Willpower;
                default: return CardStatScaling.Strength;
            }
        }

        /// <summary>Sets a card's rarity in passing, so an ordinary card can be written on one line.</summary>
        static CardData Setup(CardData card, CardRarity rarity)
        {
            card.rarity = rarity;
            return card;
        }

        /// <summary>
        /// Creates an item with everything cleared, so re-running the builder cannot leave a stale
        /// bonus behind on an asset that already existed. Callers set what they need afterwards.
        /// </summary>
        static ItemData CreateItem(string itemName, string description, EquipmentSlot slot)
        {
            string path = ItemsFolder + "/" + itemName.Replace(" ", "") + ".asset";
            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemData>();
                AssetDatabase.CreateAsset(item, path);
            }

            item.itemName = itemName;
            item.description = description;
            item.slot = slot;

            item.rarity = ItemRarity.Common;
            item.consumable = false;
            item.relic = false;
            item.healAmount = 0;
            item.strength = 0;
            item.agility = 0;
            item.intellect = 0;
            item.vitality = 0;
            item.maxHealthBonus = 0;
            item.energyBonus = 0;
            item.grantsCardEveryEncounters = 0;

            return item;
        }

        static ItemData FindItem(List<ItemData> items, string itemName)
        {
            foreach (ItemData item in items)
            {
                if (item != null && item.itemName == itemName) return item;
            }
            return null;
        }

        /// <summary>
        /// The whole item list. Charms work from a bag slot, gear works from the slot it is worn in, and
        /// potions are drunk from the bag and are then gone.
        /// </summary>
        static List<ItemData> BuildItems()
        {
            var items = new List<ItemData>();

            ItemData blade = CreateItem("Blade of Vigor", "A chipped but eager blade. You keep it close.", EquipmentSlot.Carried);
            blade.strength = 2;
            items.Add(blade);

            ItemData boots = CreateItem("Swift Boots", "Light enough to dodge a clumsy swing.", EquipmentSlot.Boots);
            boots.agility = 2;
            items.Add(boots);

            ItemData focus = CreateItem("Arcane Focus", "It hums, and your energy answers.", EquipmentSlot.Ring);
            focus.intellect = 3;
            items.Add(focus);

            ItemData plate = CreateItem("Iron Plate", "Heavy, but you are harder to kill in it.", EquipmentSlot.Chest);
            plate.vitality = 2;
            plate.maxHealthBonus = 5;
            items.Add(plate);

            // Armour that chests and events hand out.
            ItemData helm = CreateItem("Dented Helm", "Someone else's dent. It fits well enough.", EquipmentSlot.Helmet);
            helm.vitality = 1;
            helm.maxHealthBonus = 3;
            items.Add(helm);

            ItemData greaves = CreateItem("Scavenged Greaves", "Strapped tight over old cloth.", EquipmentSlot.Trousers);
            greaves.vitality = 1;
            greaves.agility = 1;
            items.Add(greaves);

            // Potions. Drunk from the bag, and gone afterwards.
            ItemData potion = CreateItem("Health Potion", "Bitter, and it works.", EquipmentSlot.Carried);
            potion.consumable = true;
            potion.healAmount = 12;
            items.Add(potion);

            // The feather from the egg nest: a small, safe reward.
            ItemData feather = CreateItem("Feather Ring", "A pale feather set in a plain band. You stand a little lighter.", EquipmentSlot.Ring);
            feather.agility = 1;
            items.Add(feather);

            // The egg itself: ruinous to wear, and worth it over a long run.
            ItemData egg = CreateItem("Cracked Egg", "Something inside is still alive, and it is slowly eating you. Every fifth fight, it leaves you a gift.", EquipmentSlot.Ring);
            egg.rarity = ItemRarity.Rare;
            egg.strength = -3;
            egg.vitality = -3;
            egg.grantsCardEveryEncounters = 5;
            items.Add(egg);

            // A plain set to open a run in. Deliberately weaker than the armour above, so starting gear
            // reads as something to replace rather than something to keep.
            ItemData cap = CreateItem("Worn Leather Cap", "Patched leather, softened by rain. It has kept worse off you.", EquipmentSlot.Helmet);
            cap.vitality = 1;
            items.Add(cap);

            ItemData trousers = CreateItem("Frayed Trousers", "Sturdy cloth, worn thin at the knees.", EquipmentSlot.Trousers);
            trousers.vitality = 1;
            items.Add(trousers);

            ItemData muddyBoots = CreateItem("Muddy Boots", "Someone else walked a long way in these.", EquipmentSlot.Boots);
            muddyBoots.agility = 1;
            items.Add(muddyBoots);

            // Relics. They take no equipment slot, they work from the bag like a charm, and they are what
            // the shop's relic rack trades in. FillRack splits the item list on exactly this flag, so a
            // relic that is never flagged leaves the rack empty.
            ItemData sigil = CreateItem("Warded Sigil", "A cold iron disc, scratched over with a language nobody reads any more.", EquipmentSlot.Carried);
            sigil.relic = true;
            sigil.intellect = 3;
            sigil.maxHealthBonus = 4;
            items.Add(sigil);

            ItemData bloodstone = CreateItem("Bloodstone", "Warm to the touch, and it never cools.", EquipmentSlot.Carried);
            bloodstone.relic = true;
            bloodstone.vitality = 3;
            bloodstone.maxHealthBonus = 8;
            items.Add(bloodstone);

            ItemData talon = CreateItem("Hunter's Talon", "Strung on a leather cord. It quickens the hand.", EquipmentSlot.Carried);
            talon.relic = true;
            talon.agility = 3;
            items.Add(talon);

            ItemData totem = CreateItem("Twin Fang Totem", "Two fangs bound together, pointing opposite ways.", EquipmentSlot.Carried);
            totem.relic = true;
            totem.rarity = ItemRarity.Rare;
            totem.strength = 2;
            totem.agility = 2;
            totem.intellect = 2;
            items.Add(totem);

            foreach (ItemData item in items) EditorUtility.SetDirty(item);
            return items;
        }

        /// <summary>
        /// Weights in RelicInstance.StatOrder: Strength, Agility, Intellect, Vitality, Willpower.
        ///
        /// These are biases and never rules. Every relic below keeps some weight on the stats it does not
        /// want, because a roll that is occasionally strange is the point of the system: it is what lets a
        /// player assemble a build around a relic instead of only picking the relic their build wants.
        /// </summary>
        static float[] Affinity(float strength, float agility, float intellect, float vitality, float willpower)
        {
            return new float[] { strength, agility, intellect, vitality, willpower };
        }

        /// <summary>
        /// Creates a relic template with everything cleared, so re-running the builder cannot leave a
        /// stale budget, affinity or effect behind on an asset that already existed.
        ///
        /// OnValidate does not run for a script that writes these fields directly, so the three things a
        /// relic cannot choose for itself are set here as well: it is a relic, it is carried, and its
        /// rarity follows its tier.
        /// </summary>
        static RelicDefinition CreateRelic(string relicName, string description, RelicRarity tier)
        {
            string path = RelicsFolder + "/" + relicName.Replace(" ", "") + ".asset";
            RelicDefinition relic = AssetDatabase.LoadAssetAtPath<RelicDefinition>(path);
            if (relic == null)
            {
                relic = ScriptableObject.CreateInstance<RelicDefinition>();
                AssetDatabase.CreateAsset(relic, path);
            }

            relic.itemName = relicName;
            relic.description = description;

            relic.relic = true;
            relic.slot = EquipmentSlot.Carried;
            relic.consumable = false;
            relic.healAmount = 0;
            relic.tier = tier;
            relic.rarity = (tier == RelicRarity.TierI || tier == RelicRarity.Unique) ? ItemRarity.Rare : ItemRarity.Common;

            // Guaranteed stats, cleared. These are what the relic always has, added on top of its roll and
            // independent of it. They may be negative: that is how a relic buys an upside with a real cost.
            relic.strength = 0;
            relic.agility = 0;
            relic.intellect = 0;
            relic.vitality = 0;
            relic.willpower = 0;
            relic.maxHealthBonus = 0;
            relic.energyBonus = 0;
            relic.grantsCardEveryEncounters = 0;

            // Per-relic overrides off by default: the tier tables in RelicSettings are the balance of the
            // game, and a relic only overrides them when it has a reason to.
            relic.useCustomBudget = false;
            relic.positiveMin = 1;
            relic.positiveMax = 3;
            relic.useCustomNegativeBudget = false;
            relic.negativeMin = 0;
            relic.negativeMax = 0;
            relic.negativeChance = -1f;
            relic.useCustomConcentration = false;

            relic.positiveAffinity = Affinity(1f, 1f, 1f, 1f, 1f);
            relic.negativeAffinity = Affinity(1f, 1f, 1f, 1f, 1f);
            relic.positiveConcentrationWeights = new float[] { 1f, 1f, 1f, 1f };
            relic.negativeConcentrationWeights = new float[] { 1f, 1f, 1f, 1f };

            relic.effect = RelicEffect.None;
            relic.scaling = new RelicScaling();
            relic.threshold = 0;
            relic.secondaryMagnitude = 0;

            return relic;
        }

        /// <summary>
        /// Guaranteed points in one named attribute with every other one cleared, so re-running the builder
        /// cannot leave a guarantee from a previous shape behind.
        /// </summary>
        static void SetGuaranteed(RelicDefinition relic, StatType stat, int amount)
        {
            relic.strength = 0;
            relic.agility = 0;
            relic.intellect = 0;
            relic.vitality = 0;
            relic.willpower = 0;

            switch (stat)
            {
                case StatType.Strength: relic.strength = amount; break;
                case StatType.Agility: relic.agility = amount; break;
                case StatType.Intellect: relic.intellect = amount; break;
                case StatType.Vitality: relic.vitality = amount; break;
                case StatType.Willpower: relic.willpower = amount; break;
            }
        }

        /// <summary>
        /// Weights in RelicInstance.StatOrder: Strength, Agility, Intellect, Vitality, Willpower.
        ///
        /// The named attribute is favoured and every other one keeps some weight, because these are biases
        /// and never rules. A relic whose own attribute could never be rolled around would only ever be
        /// picked by a build that already exists, which is the opposite of what the family is for.
        /// </summary>
        static float[] AffinityFor(StatType favoured, float favouredWeight, float otherWeight, float vitalityWeight)
        {
            var weights = new float[RelicInstance.StatCount];
            for (int i = 0; i < weights.Length; i++)
            {
                StatType type = RelicInstance.StatOrder[i];
                weights[i] = type == favoured
                    ? favouredWeight
                    : (type == StatType.Vitality ? vitalityWeight : otherWeight);
            }
            return weights;
        }

        /// <summary>The negative package's weights, with the relic's own attribute the least likely to be hit.</summary>
        static float[] AffinityAgainst(StatType favoured, float otherWeight, float favouredWeight, float willpowerWeight)
        {
            var weights = new float[RelicInstance.StatCount];
            for (int i = 0; i < weights.Length; i++)
            {
                StatType type = RelicInstance.StatOrder[i];
                weights[i] = type == favoured
                    ? favouredWeight
                    : (type == StatType.Willpower ? willpowerWeight : otherWeight);
            }
            return weights;
        }

        /// <summary>The effect number a relic is named for, with no scaling of its own.</summary>
        static RelicScaling Flat(StatType stat, float amount)
        {
            return new RelicScaling
            {
                stat = stat,
                baseValue = amount,
                perStatPoint = 0f,
                divisor = 1f,
                isPercent = false
            };
        }

        /// <summary>A percentage that grows with an attribute, clamped so it can never promise certainty.</summary>
        static RelicScaling Percent(StatType stat, float baseValue, float perStatPoint)
        {
            return new RelicScaling
            {
                stat = stat,
                baseValue = baseValue,
                perStatPoint = perStatPoint,
                divisor = 1f,
                isPercent = true
            };
        }

        /// <summary>
        /// One cut of the Goblet family: a designated attribute bought a point at a time at the top of every
        /// turn, at the price of four cards out of the opening hand.
        ///
        /// The four cuts differ only in data, which is the point of the family. The designated attribute is
        /// what the fight grants and what the tooltip names, and it comes from the RelicScaling every relic
        /// already reads rather than from a field invented for this one.
        /// </summary>
        static void AddGoblet(List<RelicDefinition> relics, string relicName, StatType designated,
                              string description)
        {
            RelicDefinition goblet = CreateRelic(relicName, description, RelicRarity.TierI);

            // Guaranteed, and so on top of the random budget: the identity survives every run while the
            // roll still makes each one of them different.
            SetGuaranteed(goblet, designated, 5);

            goblet.positiveAffinity = AffinityFor(designated, 4f, 0.8f, 2f);
            goblet.negativeAffinity = AffinityAgainst(designated, 2f, 0.1f, 3f);

            goblet.effect = RelicEffect.Goblet;
            goblet.threshold = 4;
            goblet.scaling = Flat(designated, 1f);

            relics.Add(goblet);
        }

        /// <summary>
        /// The relic templates. A relic is a definition plus a roll, so two of the same relic are different
        /// objects and the shop can show what it is selling.
        /// </summary>
        static List<RelicDefinition> BuildRelics()
        {
            var relics = new List<RelicDefinition>();

            // --- Tier I: the strongest ordinary relics, and the only ones with guaranteed stats --------
            RelicDefinition grip = CreateRelic("Titan's Grip",
                "A grip that was never meant to let go. Whatever it takes from you, it takes quickly.",
                RelicRarity.TierI);
            grip.positiveAffinity = Affinity(6f, 1.2f, 0.5f, 0.8f, 0.4f);
            grip.negativeAffinity = Affinity(0.2f, 3f, 3f, 1f, 1.5f);
            grip.positiveConcentrationWeights = new float[] { 1f, 2f, 3f, 4f };
            grip.useCustomConcentration = true;

            // Guaranteed Strength, because what the relic does is swing the weapon you are already holding:
            // the effect is only worth having on a build that has committed to the attribute.
            SetGuaranteed(grip, StatType.Strength, 3);
            grip.effect = RelicEffect.TitansGrip;

            relics.Add(grip);

            // The Goblet family: one artifact in four cuts, one per attribute. Each is bought a point at a
            // time at the top of every turn, and each is paid for out of the opening hand.
            AddGoblet(relics, "Goblet of Power", StatType.Strength,
                "Drinking from it is not the point. Holding it is. The strength is always the same; the rest is not.");

            AddGoblet(relics, "Goblet of Prowess", StatType.Agility,
                "It rewards the hand that is already quick, and it asks for four cards in exchange.");

            AddGoblet(relics, "Goblet of Insight", StatType.Intellect,
                "Patience, poured. It pays out a thought at a time and takes its fee up front.");

            AddGoblet(relics, "Goblet of Force", StatType.Willpower,
                "Resolve in a cup. What it buys is only ever good for the fight you are standing in.");

            RelicDefinition orrery = CreateRelic("Grand Orrery",
                "Every gear in it is a card that was already spent. Wind it and they turn over one last time.",
                RelicRarity.TierI);
            orrery.intellect = 3;
            orrery.positiveAffinity = Affinity(0.6f, 0.6f, 6f, 1.2f, 1f);
            orrery.negativeAffinity = Affinity(2.5f, 2f, 0.2f, 2f, 1.5f);
            orrery.positiveConcentrationWeights = new float[] { 1f, 2f, 2.5f, 2f };
            orrery.useCustomConcentration = true;
            orrery.effect = RelicEffect.GrandOrrery;
            relics.Add(orrery);

            RelicDefinition bloodCrown = CreateRelic("Blood Crown",
                "It only fits once you are already bleeding. That is when it starts to be worth wearing.",
                RelicRarity.TierI);
            bloodCrown.willpower = 3;
            bloodCrown.positiveAffinity = Affinity(1.2f, 0.8f, 0.8f, 1.5f, 5f);
            bloodCrown.negativeAffinity = Affinity(2f, 2.5f, 2.5f, 1.5f, 0.2f);
            bloodCrown.effect = RelicEffect.BloodCrown;
            // Two of each: two energy and two of the designated attribute, once per fight.
            bloodCrown.scaling = Flat(StatType.Willpower, 2f);
            bloodCrown.secondaryMagnitude = 2;
            relics.Add(bloodCrown);

            RelicDefinition grimoire = CreateRelic("Loaded Grimoire",
                "It writes both answers before you have read the question, and lets you keep one.",
                RelicRarity.TierI);
            grimoire.intellect = 3;
            grimoire.positiveAffinity = Affinity(0.7f, 2f, 5f, 1f, 1f);
            grimoire.negativeAffinity = Affinity(2.5f, 1.5f, 0.2f, 2f, 2f);
            grimoire.effect = RelicEffect.LoadedGrimoire;
            // Two cards turned up, one kept. The count is the relic's, not the conjure's.
            grimoire.secondaryMagnitude = 2;
            relics.Add(grimoire);

            RelicDefinition ledger = CreateRelic("Reaper's Ledger",
                "Every card it has crossed out was still standing when the line was drawn. The debt is paid in energy.",
                RelicRarity.TierI);
            ledger.willpower = 3;
            ledger.intellect = 1;
            ledger.positiveAffinity = Affinity(1.2f, 1f, 3f, 1.2f, 4f);
            ledger.negativeAffinity = Affinity(2.5f, 2f, 0.3f, 2f, 0.3f);
            ledger.effect = RelicEffect.ReapersLedger;
            ledger.scaling = Flat(StatType.Willpower, 1f);
            ledger.secondaryMagnitude = 1;
            relics.Add(ledger);

            RelicDefinition mirage = CreateRelic("Mirage Engine",
                "The third card you play is the one it answers, and what it answers with was never in your deck.",
                RelicRarity.TierI);
            mirage.agility = 3;
            mirage.positiveAffinity = Affinity(0.8f, 6f, 1.5f, 1f, 0.8f);
            mirage.negativeAffinity = Affinity(2.5f, 0.2f, 2f, 2f, 2f);
            mirage.effect = RelicEffect.MirageEngine;
            mirage.threshold = 3;
            relics.Add(mirage);

            // --- Unique: the stat-scaling relics. The effect is a number that grows with the build, so
            // these are tuned on their own terms rather than as a stronger Tier I -------------------------
            RelicDefinition dice = CreateRelic("Loaded Dice",
                "They come up your way. The more you lean on your reflexes, the more often they do.",
                RelicRarity.Unique);
            dice.agility = 1;
            dice.positiveAffinity = Affinity(0.7f, 5f, 0.7f, 0.6f, 0.6f);
            dice.negativeAffinity = Affinity(3f, 0.3f, 2.5f, 2f, 2f);
            dice.effect = RelicEffect.LoadedDice;
            // 20% plus one point per Agility, clamped by the scaling itself so it can never pass certainty.
            dice.scaling = new RelicScaling
            {
                stat = StatType.Agility,
                baseValue = 20f,
                perStatPoint = 1f,
                divisor = 1f,
                isPercent = true
            };
            relics.Add(dice);

            RelicDefinition core = CreateRelic("Clockwork Core",
                "Something inside it keeps time to a rhythm that is not yours. It can be learned.",
                RelicRarity.Unique);
            core.intellect = 2;
            core.positiveAffinity = Affinity(0.6f, 0.6f, 5f, 0.8f, 1f);
            core.negativeAffinity = Affinity(2f, 2.5f, 0.3f, 2f, 1.5f);
            core.effect = RelicEffect.ClockworkCore;
            // 10% plus a fifth of a point per Intellect: a slow ramp that rewards committing to the stat.
            core.scaling = new RelicScaling
            {
                stat = StatType.Intellect,
                baseValue = 10f,
                perStatPoint = 0.2f,
                divisor = 1f,
                isPercent = true
            };
            relics.Add(core);

            RelicDefinition boulder = CreateRelic("Boulder Shield",
                "Cut from something much larger. It is only as good as the arm holding it up.",
                RelicRarity.Unique);
            boulder.vitality = 1;
            boulder.positiveAffinity = Affinity(5f, 1f, 0.5f, 3f, 0.6f);
            boulder.negativeAffinity = Affinity(0.3f, 3f, 3f, 0.5f, 2f);
            boulder.effect = RelicEffect.BoulderShield;
            // Strength / 3, in whole points: base 0, one per point of Strength, divided by three. This is
            // the same rounding a card uses to turn a calculated number into an integer.
            boulder.scaling = new RelicScaling
            {
                stat = StatType.Strength,
                baseValue = 0f,
                perStatPoint = 1f,
                divisor = 3f,
                isPercent = false
            };
            relics.Add(boulder);

            // --- Tier III: the minor relics. One small thing, once, on top of a small stat roll ----------
            RelicDefinition ash = CreateRelic("Ash Pendant",
                "Grey dust behind glass. It is not worth much, and it is still worth carrying.",
                RelicRarity.TierIII);
            ash.positiveAffinity = Affinity(1f, 1f, 1f, 1f, 1f);
            ash.negativeAffinity = Affinity(1f, 1f, 1f, 1f, 1f);
            relics.Add(ash);

            RelicDefinition whetstone = CreateRelic("Whetstone",
                "Three strokes before every fight. It is only ever good for the first cut, and the first cut is the one.",
                RelicRarity.TierIII);
            whetstone.positiveAffinity = Affinity(4f, 3.5f, 0.7f, 1.2f, 0.8f);
            whetstone.negativeAffinity = Affinity(0.4f, 0.5f, 2.5f, 2f, 2.5f);
            whetstone.effect = RelicEffect.Whetstone;
            whetstone.scaling = Flat(StatType.Strength, 2f);
            relics.Add(whetstone);

            RelicDefinition thread = CreateRelic("Bloodstained Thread",
                "Tied off at one end. Something is still paying attention to where it leads.",
                RelicRarity.TierIII);
            thread.positiveAffinity = Affinity(1.2f, 1.5f, 1.5f, 2f, 4f);
            thread.negativeAffinity = Affinity(2f, 2f, 2f, 1.5f, 0.4f);
            thread.effect = RelicEffect.BloodstainedThread;
            thread.scaling = Flat(StatType.Willpower, 1f);
            relics.Add(thread);

            RelicDefinition lens = CreateRelic("Bent Lens",
                "It takes the light in at an angle. What comes out the other side is not what went in.",
                RelicRarity.TierIII);
            lens.positiveAffinity = Affinity(0.8f, 1f, 5f, 1.2f, 1f);
            lens.negativeAffinity = Affinity(2.5f, 2f, 0.4f, 2f, 2f);
            lens.effect = RelicEffect.BentLens;
            lens.scaling = Flat(StatType.Intellect, 2f);
            relics.Add(lens);

            RelicDefinition charm = CreateRelic("Lucky Charm",
                "Nothing about it is lucky. It is simply the first thing you touch when a card turns up from nowhere.",
                RelicRarity.TierIII);
            charm.positiveAffinity = Affinity(0.7f, 4f, 3.5f, 1f, 1.2f);
            charm.negativeAffinity = Affinity(2.5f, 0.4f, 0.5f, 2f, 2f);
            charm.effect = RelicEffect.LuckyCharm;
            charm.scaling = Flat(StatType.Agility, 1f);
            relics.Add(charm);

            RelicDefinition coin = CreateRelic("Traveler's Coin",
                "Minted somewhere it has never been back to. It has been spent and handed on many times.",
                RelicRarity.TierIII);
            coin.positiveAffinity = Affinity(1f, 4f, 1.2f, 1.2f, 3.5f);
            coin.negativeAffinity = Affinity(2f, 0.5f, 2.5f, 2f, 0.4f);
            coin.effect = RelicEffect.TravelersCoin;
            coin.scaling = Flat(StatType.Willpower, 2f);
            relics.Add(coin);

            // --- Tier II: the build relics. An engine rather than a moment, on a real budget and with a real
            // chance of a negative package to pay for it -----------------------------------------------
            RelicDefinition trophy = CreateRelic("War Trophy",
                "Taken off something that did not want to give it up. It is still warm.",
                RelicRarity.TierII);
            trophy.positiveAffinity = Affinity(5f, 1.2f, 0.6f, 1.5f, 0.8f);
            trophy.negativeAffinity = Affinity(0.2f, 2.5f, 2.5f, 2f, 2.5f);
            trophy.effect = RelicEffect.WarTrophy;
            trophy.scaling = Flat(StatType.Strength, 1f);
            relics.Add(trophy);

            RelicDefinition ribbon = CreateRelic("Duelist's Ribbon",
                "Won long ago, in a fight that went three passes. It remembers the third one.",
                RelicRarity.TierII);
            ribbon.agility = 1;
            ribbon.positiveAffinity = Affinity(1f, 5f, 1.5f, 1f, 1f);
            ribbon.negativeAffinity = Affinity(2.5f, 0.2f, 2f, 2f, 2f);
            ribbon.effect = RelicEffect.DuelistsRibbon;
            ribbon.threshold = 3;
            ribbon.scaling = Flat(StatType.Agility, 1f);
            relics.Add(ribbon);

            RelicDefinition architect = CreateRelic("Architect's Lens",
                "Ground for looking at a thing that is already standing and asking how much longer.",
                RelicRarity.TierII);
            architect.positiveAffinity = Affinity(0.7f, 1f, 5f, 1.5f, 1.2f);
            architect.negativeAffinity = Affinity(2.5f, 2f, 0.2f, 2f, 2f);
            architect.effect = RelicEffect.ArchitectsLens;
            architect.scaling = Flat(StatType.Intellect, 1f);
            relics.Add(architect);

            RelicDefinition bloodSigil = CreateRelic("Blood Sigil",
                "A promise written in advance. It only calls in the debt when enough of it has been paid.",
                RelicRarity.TierII);
            bloodSigil.positiveAffinity = Affinity(1.5f, 1f, 1f, 2.5f, 5f);
            bloodSigil.negativeAffinity = Affinity(2.5f, 2f, 2f, 2f, 0.2f);
            bloodSigil.effect = RelicEffect.BloodSigil;
            bloodSigil.threshold = 5;
            bloodSigil.scaling = Flat(StatType.Willpower, 1f);
            relics.Add(bloodSigil);

            RelicDefinition satchel = CreateRelic("Scrap Satchel",
                "Full of things that were nearly useful. Sort through enough of it and something always is.",
                RelicRarity.TierII);
            satchel.positiveAffinity = Affinity(0.8f, 4f, 4f, 1.2f, 1.2f);
            satchel.negativeAffinity = Affinity(2.5f, 0.4f, 0.4f, 2f, 2f);
            satchel.effect = RelicEffect.ScrapSatchel;
            satchel.threshold = 3;
            satchel.scaling = Flat(StatType.Intellect, 1f);
            relics.Add(satchel);

            // Deliberately even. It has no archetype to lean on, so it is the one relic that can turn up
            // useful to a deck that is already committed to something else.
            RelicDefinition brokenCrown = CreateRelic("Broken Crown",
                "Snapped across the band and kept anyway. What it lifts is whatever you have least of.",
                RelicRarity.TierII);
            brokenCrown.positiveAffinity = Affinity(1f, 1f, 1f, 1f, 1f);
            brokenCrown.negativeAffinity = Affinity(1f, 1f, 1f, 1f, 1f);
            brokenCrown.effect = RelicEffect.BrokenCrown;
            brokenCrown.scaling = Flat(StatType.Vitality, 2f);
            relics.Add(brokenCrown);

            // --- The polarity relic: it does not add power, it lifts a rule -------------------------------
            RelicDefinition oath = CreateRelic("The Broken Oath",
                "Half of it is missing and the half that is left is still binding. It lets you go under.",
                RelicRarity.Unique);

            // Guaranteed Willpower, because the relic is about Willpower and a build has to be able to
            // commit to it before there is anything to spend below zero.
            SetGuaranteed(oath, StatType.Willpower, 2);

            // It sets its own budget rather than borrowing a tier's: the whole point of the artifact is that
            // it is not an ordinary relic with a bigger number on it.
            oath.useCustomBudget = true;
            oath.positiveMin = 6;
            oath.positiveMax = 12;
            oath.useCustomNegativeBudget = true;
            oath.negativeMin = 2;
            oath.negativeMax = 5;
            oath.negativeChance = 0.6f;

            oath.positiveAffinity = Affinity(1.2f, 1.2f, 1.2f, 2f, 5f);
            oath.negativeAffinity = Affinity(2f, 2f, 2f, 1.2f, 0.3f);
            oath.effect = RelicEffect.BrokenOath;
            relics.Add(oath);

            foreach (RelicDefinition relic in relics)
            {
                relic.EnsureWeights();
                EditorUtility.SetDirty(relic);
            }

            return relics;
        }

        /// <summary>
        /// Writes the generator's tuning asset if it is missing, and leaves it alone if it is there.
        ///
        /// It is deliberately not overwritten: it holds the per-tier budgets and the concentration odds,
        /// which are balance data meant to be edited in the inspector. Re-running the builder must not
        /// stamp over that.
        /// </summary>
        static RelicSettings EnsureRelicSettings()
        {
            string path = ResourcesFolder + "/RelicSettings.asset";
            RelicSettings settings = AssetDatabase.LoadAssetAtPath<RelicSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<RelicSettings>();
                settings.Configure();
                AssetDatabase.CreateAsset(settings, path);
                EditorUtility.SetDirty(settings);
            }

            return settings;
        }

        static List<EnemyData> BuildEnemies()
        {
            return new List<EnemyData>
            {
                // The Goblin is the one enemy with no answer to anything: it just attacks. That is why the
                // depth gate makes it the entrance encounter, and why it stays a fair fight at any size.
                CreateEnemy("Goblin", 20, new[] { 6 }, new[] { 0 }, 0, 25, 12, "Quick and spiteful."),

                // It raises its shield on the beat its attack is small, so the turn you would have spent
                // healing is the turn it hides behind.
                CreateEnemy("Skeleton", 26, new[] { 5, 9 }, new[] { 5, 0 }, 0, 40, 18,
                    "It raises a notched blade behind a notched shield."),

                // Starts behind cover, which is what makes the first turn of the fight about clearing it
                // rather than about damage.
                CreateEnemy("Cave Lurker", 32, new[] { 7, 4, 10 }, new[] { 0, 0, 5 }, 6, 60, 28,
                    "It waits where the light does not reach.")
            };
        }


        /// <summary>
        /// The boss waiting in the last room of every dungeon. It is kept out of the enemy list so it is
        /// never rolled as an ordinary encounter; the only ways to meet it are the end of the map and the
        /// gamble at the egg nest.
        /// </summary>
        static EnemyData CreateBossEnemy()
        {
            // Ten shielding before the first card is drawn, and six more on every third turn. The boss is
            // the fight the shield-breaker exists for.
            return CreateEnemy("The Warden", 50, new[] { 6, 8, 11 }, new[] { 0, 6, 0 }, 10, 150, 60,
                "It has been down here longer than the dungeon has, and it has opinions about visitors.");
        }

        static EnemyData CreateEnemy(string enemyName, int maxHealth, int[] attackPattern, int[] blockPattern,
                                     int startingBlock, int experienceReward, int goldReward, string description)
        {
            string path = EnemiesFolder + "/" + enemyName.Replace(" ", "") + ".asset";
            EnemyData enemy = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
            if (enemy == null)
            {
                enemy = ScriptableObject.CreateInstance<EnemyData>();
                AssetDatabase.CreateAsset(enemy, path);
            }

            enemy.enemyName = enemyName;
            enemy.maxHealth = maxHealth;
            enemy.attackPattern = new List<int>(attackPattern);

            // A null or empty pattern means an enemy that never blocks, which is a legitimate enemy to
            // author: an empty list would silently make GetBlockForTurn return zero anyway, but saying so
            // here keeps the asset readable in the inspector.
            enemy.blockPattern = blockPattern != null && blockPattern.Length > 0
                ? new List<int>(blockPattern)
                : new List<int> { 0 };

            enemy.startingBlock = Mathf.Max(0, startingBlock);
            enemy.experienceReward = experienceReward;
            enemy.goldReward = goldReward;
            enemy.description = description;

            EditorUtility.SetDirty(enemy);
            return enemy;
        }

        /// <summary>Authors the event rooms. Every option pairs an upside with a downside.</summary>
        static List<EventData> CreateEvents(List<ItemData> items, EnemyData boss)
        {
            var events = new List<EventData>
            {
                CreateEvent("Abandoned Shrine",
                    "A stone idol leans in the corner, its face worn smooth. Offerings rot at its feet.",
                    new EventOption
                    {
                        label = "Kneel and pray",
                        resultText = "Something answers. Your arms feel heavier with purpose.",
                        effect = new EventEffect { healthChange = -4, attribute = StatType.Strength, attributeChange = 1 }
                    },
                    new EventOption
                    {
                        label = "Gather the offerings",
                        resultText = "You pocket what the dead left behind.",
                        effect = new EventEffect { experienceChange = 25 }
                    },
                    new EventOption
                    {
                        label = "Rest a moment",
                        resultText = "You catch your breath in the quiet.",
                        effect = new EventEffect { healthChange = 8 }
                    }),

                CreateEvent("Collapsed Armory",
                    "Rusted racks have folded into each other. Something still glints beneath the rubble.",
                    new EventOption
                    {
                        label = "Dig through the rubble",
                        resultText = "You tear your hands open pulling the blade free.",
                        effect = new EventEffect { healthChange = -5, grantItem = FindItem(items, "Blade of Vigor") }
                    },
                    new EventOption
                    {
                        label = "Search slowly and carefully",
                        resultText = "Patience pays, but the hour is late and your mind wanders.",
                        effect = new EventEffect { experienceChange = -20, grantItem = FindItem(items, "Iron Plate") }
                    },
                    new EventOption
                    {
                        label = "Move on",
                        resultText = "You leave the dead their armour.",
                        effect = new EventEffect { healthChange = 3 }
                    }),

                CreateEvent("Whispering Pool",
                    "Still water sits in a basin of black stone. It whispers your name when you lean close.",
                    new EventOption
                    {
                        label = "Drink deeply",
                        resultText = "The water is cold enough to hurt, and it leaves you stronger.",
                        effect = new EventEffect { healthChange = -3, permanentMaxHealth = 6 }
                    },
                    new EventOption
                    {
                        label = "Dip your blade in it",
                        resultText = "The steel comes out lighter than it went in.",
                        effect = new EventEffect { healthChange = -3, attribute = StatType.Agility, attributeChange = 1 }
                    },
                    new EventOption
                    {
                        label = "Ignore it",
                        resultText = "You step around the basin and keep walking.",
                        effect = new EventEffect { healthChange = 5 }
                    }),

                CreateEvent("Sealed Door",
                    "A door of banded iron blocks the corridor. Four ways past it present themselves.",
                    new EventOption
                    {
                        label = "Force it with your shoulder",
                        resultText = "The hinges give, and so does something in your ribs.",
                        effect = new EventEffect { healthChange = -6, attribute = StatType.Strength, attributeChange = 1 }
                    },
                    new EventOption
                    {
                        label = "Work the lock with a pick",
                        resultText = "The tumblers click. You will remember the trick.",
                        effect = new EventEffect { healthChange = -4, attribute = StatType.Agility, attributeChange = 1 }
                    },
                    new EventOption
                    {
                        label = "Study the runes carved into it",
                        resultText = "The runes resolve into a pattern, and the door recognises it.",
                        effect = new EventEffect { healthChange = -3, attribute = StatType.Intellect, attributeChange = 1 }
                    },
                    new EventOption
                    {
                        label = "Leave it and backtrack",
                        resultText = "You lose time, but you learn the shape of this place.",
                        effect = new EventEffect { experienceChange = 15 }
                    }),

                // The plain loot room. Half the time it is a chest; half the time it is a chest with teeth.
                CreateEvent("Iron Banded Chest",
                    "A chest sits against the wall, its lock long since rusted through. Something inside it shifts as you lean closer.",
                    new EventOption
                    {
                        label = "Open it",
                        resultText = "",
                        outcomes = new List<EventOutcome>
                        {
                            new EventOutcome
                            {
                                weight = 25,
                                resultText = "The lid comes up on a folded bundle of gear, still serviceable.",
                                effect = new EventEffect
                                {
                                    randomItemPool = new List<ItemData>
                                    {
                                        FindItem(items, "Dented Helm"),
                                        FindItem(items, "Iron Plate"),
                                        FindItem(items, "Scavenged Greaves"),
                                        FindItem(items, "Swift Boots")
                                    },
                                    randomItemAmount = 1
                                }
                            },
                            new EventOutcome
                            {
                                weight = 25,
                                resultText = "Two clay bottles, wax still sealing them. You take both.",
                                effect = new EventEffect
                                {
                                    randomItemPool = new List<ItemData> { FindItem(items, "Health Potion") },
                                    randomItemAmount = 2
                                }
                            },
                            new EventOutcome
                            {
                                weight = 50,
                                resultText = "The chest unfolds. Teeth, and a great deal of them. You wrench yourself free, bleeding, with nothing to show for it.",
                                effect = new EventEffect { healthChange = -10 }
                            }
                        }
                    },
                    new EventOption
                    {
                        label = "Leave it alone",
                        resultText = "You walk past it. Better poor than bitten.",
                        effect = new EventEffect()
                    }),

                // Leaving is safe and small. Gambling is a rare prize or a fight you are not ready for.
                CreateEvent("Warm Egg",
                    "Nestled in a bowl of old straw is an egg the size of your fist. It is warm, and it is moving. A single pale feather lies beside it. Whatever laid this is not far.",
                    new EventOption
                    {
                        label = "Take the feather and leave the egg",
                        resultText = "You lift the feather and back away, slowly, with the whole nest undisturbed.",
                        effect = new EventEffect { grantItem = FindItem(items, "Feather Ring") }
                    },
                    new EventOption
                    {
                        label = "Reach for the egg",
                        resultText = "",
                        outcomes = new List<EventOutcome>
                        {
                            new EventOutcome
                            {
                                weight = 20,
                                resultText = "The shell gives. Something small and bright settles into your palm, and stays there, and starts to feed.",
                                effect = new EventEffect { grantItem = FindItem(items, "Cracked Egg") }
                            },
                            new EventOutcome
                            {
                                weight = 80,
                                resultText = "The straw erupts. What was mothering this nest is enormous, and you have its egg in your hands.",
                                effect = new EventEffect { startCombat = boss }
                            }
                        }
                    },
                    new EventOption
                    {
                        label = "Back away from the nest",
                        resultText = "You leave the egg where it lies and move on.",
                        effect = new EventEffect()
                    })
            };

            return events;
        }

        static EventData CreateEvent(string eventName, string body, params EventOption[] options)
        {
            string path = EventsFolder + "/" + eventName.Replace(" ", "") + ".asset";
            EventData data = AssetDatabase.LoadAssetAtPath<EventData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<EventData>();
                AssetDatabase.CreateAsset(data, path);
            }

            data.eventName = eventName;
            data.body = body;
            data.options = new List<EventOption>(options);

            EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>Adds a number of copies of one card to an opening deck, skipping a card that is missing.</summary>
        static void AddCopies(List<CardData> deck, CardData card, int count)
        {
            if (deck == null || card == null) return;
            for (int i = 0; i < count; i++) deck.Add(card);
        }

        static GameDatabase BuildDatabase(List<CardData> cards, List<ItemData> items, List<EnemyData> enemies, List<EventData> events, List<ItemData> startingItems, EnemyData boss)
        {
            GameDatabase database = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<GameDatabase>();
                AssetDatabase.CreateAsset(database, DatabasePath);
            }

            database.allCards = new List<CardData>(cards);
            database.allItems = new List<ItemData>(items);
            database.enemies = new List<EnemyData>(enemies);
            database.events = new List<EventData>(events);
            database.startingItems = new List<ItemData>(startingItems);

            // The boss sits outside the enemy list, so it is never rolled as an ordinary encounter.
            database.bossEnemy = boss;

            database.experiencePerLevel = 100;

            // The opening deck is authored rather than rolled: five attacks, five blocks and three riposts,
            // thirteen cards, every one of them flat and unscaled.
            //
            // Every run therefore opens on the same hand, so the first fight is about playing what you were
            // dealt rather than about what the draw happened to give you. It also puts the scaling cards
            // where they belong: something found in a reward rather than something you started with, which
            // is what makes the first real card you take feel like a decision.
            database.startingDeck = new List<CardData>();
            AddCopies(database.startingDeck, database.FindCard("Strike"), 5);
            AddCopies(database.startingDeck, database.FindCard("Defend"), 5);
            AddCopies(database.startingDeck, database.FindCard("Ripost"), 3);

            // Kept as the pool for a rolled opening deck. The authored list above takes precedence, so this
            // only comes into play if that list is ever emptied.
            database.startingDeckPool = new List<CardData>(cards);
            database.startingDeckSize = 20;
            database.startingDeckGuaranteedCheap = 0;

            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            return database;
        }

        // ------------------------------------------------------------------ prefabs

        /// <summary>
        /// Builds the card: a fixed 2:3 panel with a round cost badge in the corner, a masked art window
        /// across the upper middle, and the effect spelled out in a description box across the bottom.
        ///
        /// The card carries its own canvas so the hover effect has a sorting order to raise, and an
        /// EventTrigger wired to the hover methods.
        /// </summary>
        static GameObject BuildCardPrefab(GameObject tooltipPrefab)
        {
            GameObject root = NewUI("CardView", null);
            SetRect(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, CardSize);

            Image frame = root.AddComponent<Image>();
            frame.color = new Color(0.45f, 0.20f, 0.22f, 1f);

            // Fixed aspect ratio: the card is always two thirds as wide as it is tall, whatever the width.
            AspectRatioFitter aspect = root.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            aspect.aspectRatio = CardAspectRatio;

            Button button = root.AddComponent<Button>();
            button.targetGraphic = frame;

            // Deliberately no Canvas here: a card with its own override-sorting canvas is sorted against
            // the screen's overlay canvases too, which drew the deck cards behind the deck view. Hover
            // lifting is done by sibling order instead, in CardHoverEffect.
            CardView view = root.AddComponent<CardView>();

            CardHoverEffect hover = root.AddComponent<CardHoverEffect>();

            EventTrigger trigger = root.AddComponent<EventTrigger>();
            trigger.triggers = new List<EventTrigger.Entry>();
            trigger.triggers.Add(PersistentEntry(EventTriggerType.PointerEnter, hover.OnPointerEnter));
            trigger.triggers.Add(PersistentEntry(EventTriggerType.PointerExit, hover.OnPointerExit));

            // ---- cost badge, top left
            Image costBadge = MakePanel(root.transform, "CostBadge", new Color(0.19f, 0.33f, 0.51f, 1f));
            costBadge.sprite = EnsureCircleSprite();
            costBadge.type = Image.Type.Simple;
            costBadge.raycastTarget = false;
            SetRect(costBadge.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(7f, -7f), new Vector2(52f, 52f));

            TextMeshProUGUI costText = MakeTmpText(costBadge.transform, "CostText", "1", 28f, TextAlignmentOptions.Center, AccentColor, false);
            Stretch(costText.gameObject, 0f, 0f, 0f, 0f);

            // ---- name, beside the badge
            TextMeshProUGUI nameText = MakeTmpText(root.transform, "NameText", "Strike", 17f, TextAlignmentOptions.Left, Color.white, true);
            SetRect(nameText.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(66f, -10f), new Vector2(106f, 44f));

            // ---- art window, upper middle, masked to its frame
            Image artFrame = MakePanel(root.transform, "ArtFrame", new Color(1f, 1f, 1f, 0.06f));
            artFrame.raycastTarget = false;
            SetRect(artFrame.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(-14f, 100f));

            Mask artMask = artFrame.gameObject.AddComponent<Mask>();
            artMask.showMaskGraphic = false;

            Image artImage = MakePanel(artFrame.transform, "CardArt", new Color(0.60f, 0.33f, 0.35f, 1f));
            artImage.raycastTarget = false;
            Stretch(artImage.gameObject, 0f, 0f, 0f, 0f);

            // ---- description box, bottom half
            Image descriptionBox = MakePanel(root.transform, "DescriptionBox", new Color(0f, 0f, 0f, 0.28f));
            descriptionBox.raycastTarget = false;
            SetRect(descriptionBox.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(-14f, 96f));

            // The effect total, which is what turns green and bounces when it is boosted.
            TextMeshProUGUI valueText = MakeTmpText(descriptionBox.transform, "ValueText", "3", 34f, TextAlignmentOptions.Center, Color.white, false);
            SetRect(valueText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(0f, 42f));

            TextMeshProUGUI descriptionText = MakeTmpText(descriptionBox.transform, "DescriptionText", "Deal 3 damage", 13f, TextAlignmentOptions.Top, new Color(0.88f, 0.90f, 0.96f), true);
            Stretch(descriptionText.gameObject, 6f, 6f, 46f, 6f);

            // ---- copy count, top right. Only the deck grid shows it, so it starts hidden: a hand card
            // has no count to report.
            Image countBadge = MakePanel(root.transform, "CountBadge", new Color(0.19f, 0.33f, 0.51f, 1f));
            countBadge.sprite = EnsureCircleSprite();
            countBadge.type = Image.Type.Simple;
            countBadge.raycastTarget = false;
            // Set below the name band rather than in the card's very corner: the name text runs to the
            // right edge of that band, so a badge up there truncates it ("Armor U" instead of "Armor Up").
            // It sits in the top right of the art window instead, where nothing else is drawn.
            SetRect(countBadge.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-7f, -62f), new Vector2(48f, 48f));


            TextMeshProUGUI countText = MakeTmpText(countBadge.transform, "CountText", "1", 26f, TextAlignmentOptions.Center, Color.white, false);
            Stretch(countText.gameObject, 0f, 0f, 0f, 0f);

            countBadge.gameObject.SetActive(false);

            Assign(view, "nameText", nameText);

            Assign(view, "costText", costText);
            Assign(view, "valueText", valueText);
            Assign(view, "descriptionText", descriptionText);
            Assign(view, "playButton", button);
            Assign(view, "frame", frame);
            Assign(view, "costBadge", costBadge);
            Assign(view, "artImage", artImage);
            Assign(view, "countRoot", countBadge.gameObject);
            Assign(view, "countText", countText);
            Assign(view, "countBadge", countBadge);
            Assign(view, "tooltipPrefab", tooltipPrefab != null ? tooltipPrefab.GetComponent<CardTooltip>() : null);


            return SavePrefab(root, CardPrefabPath);
        }

        /// <summary>
        /// Builds the card tooltip. It is its own canvas at a high sorting order so it draws over every
        /// other screen, and nothing on it catches the pointer, so it can never take the hover away from
        /// the card that opened it.
        /// </summary>
        static GameObject BuildTooltipPrefab()
        {
            var root = new GameObject("CardTooltip", typeof(Canvas), typeof(CanvasScaler));

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Pivot top left, anchored to the bottom left: the tooltip positions itself by its top left
            // corner in canvas units, which is what the clamping maths in CardTooltip assumes.
            Image panel = MakePanel(root.transform, "Panel", new Color(0.04f, 0.05f, 0.08f, 0.97f));
            panel.raycastTarget = false;
            SetRect(panel.gameObject, Vector2.zero, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(430f, 200f));

            VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Height follows the text; the width stays fixed, which is what keeps it on screen.
            ContentSizeFitter fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TextMeshProUGUI title = MakeTmpText(panel.transform, "TitleText", "Strike", 24f, TextAlignmentOptions.Left, Color.white, true);
            TextMeshProUGUI body = MakeTmpText(panel.transform, "BodyText", "", 17f, TextAlignmentOptions.TopLeft, new Color(0.84f, 0.87f, 0.94f), true);

            CardTooltip tooltip = root.AddComponent<CardTooltip>();
            Assign(tooltip, "canvas", canvas);
            Assign(tooltip, "panel", panel.transform as RectTransform);
            Assign(tooltip, "titleText", title);
            Assign(tooltip, "bodyText", body);

            return SavePrefab(root, TooltipPrefabPath);
        }

        /// <summary>Edge of a relic icon at rest. Square and small: three of them share one rack.</summary>
        const float RelicIconSize = 104f;

        /// <summary>
        /// Distance between icon centres on the relic rack. Comfortably wider than the icon, so a grown one
        /// still clears its neighbour before the rack has finished parting them.
        /// </summary>
        const float RelicIconSpacing = 150f;

        /// <summary>
        /// The relic icon: a square standing in for art that does not exist yet, with one line underneath it.
        ///
        /// A relic has no cost to play and no card body, so the square is somewhere for art and the line
        /// under it is the only text the icon ever carries: the price at rest, and the name once the pointer
        /// arrives. Everything else about the relic lives in the tooltip, which is what keeps three of them
        /// legible beside each other on one rack.
        ///
        /// It wears the same hover the cards do, from the same component and the same EventTrigger, so the
        /// rack can drive a relic and a card identically.
        /// </summary>
        static GameObject BuildRelicIconPrefab(GameObject tooltipPrefab)
        {
            GameObject root = NewUI("RelicIcon", null);
            SetRect(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(RelicIconSize, RelicIconSize));

            Image body = root.AddComponent<Image>();
            body.color = new Color(0.29f, 0.29f, 0.33f, 1f);

            // Square whatever size it is given, so the placeholder reads as the art slot it stands in for.
            AspectRatioFitter aspect = root.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            aspect.aspectRatio = 1f;

            Button button = root.AddComponent<Button>();
            button.targetGraphic = body;

            // Deliberately no Canvas here, for the same reason the card has none: a canvas with override
            // sorting is sorted against the screen's own overlay canvases too, which drew cards behind the
            // deck view. The icons are lifted by scale and by the rack's own writes instead.
            RelicIconView view = root.AddComponent<RelicIconView>();

            // The fade is what lets the rack put one relic in focus without moving the others out of reach.
            // BlocksRaycasts is left on, so a faded relic is still hoverable and still buyable.
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            CardHoverEffect hover = root.AddComponent<CardHoverEffect>();

            EventTrigger trigger = root.AddComponent<EventTrigger>();
            trigger.triggers = new List<EventTrigger.Entry>();
            trigger.triggers.Add(PersistentEntry(EventTriggerType.PointerEnter, hover.OnPointerEnter));
            trigger.triggers.Add(PersistentEntry(EventTriggerType.PointerExit, hover.OnPointerExit));

            // The line hangs below the square rather than inside it, so the art slot stays clean. It is
            // anchored to the square's bottom edge, so it follows whatever size the square ends up.
            TextMeshProUGUI captionText = MakeTmpText(root.transform, "CaptionText", "0 g", 20f,
                                                      TextAlignmentOptions.Center, new Color(0.98f, 0.86f, 0.45f, 1f), false);
            SetRect(captionText.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -6f), new Vector2(RelicIconSpacing, 26f));

            Assign(view, "body", body);
            Assign(view, "captionText", captionText);
            Assign(view, "group", group);
            Assign(view, "buyButton", button);
            Assign(view, "tooltipPrefab", tooltipPrefab != null ? tooltipPrefab.GetComponent<CardTooltip>() : null);

            // A relic is smaller than a card, so it can afford a bigger lift than the cards' 1.16 without
            // reaching its neighbours.
            AssignFloat(hover, "hoverScale", 1.24f);

            return SavePrefab(root, RelicIconPrefabPath);
        }

        /// <summary>
        /// An EventTrigger entry wired to a real method, so the hover wiring is visible on the prefab and
        /// does not depend on anything happening at runtime.
        /// </summary>
        static EventTrigger.Entry PersistentEntry(EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            UnityEventTools.AddPersistentListener(entry.callback, action);
            return entry;
        }

        static GameObject BuildItemRowPrefab()

        {
            GameObject root = NewUI("ItemRow", null);
            SetRect(root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 108f));

            Image background = root.AddComponent<Image>();
            background.color = new Color(0.15f, 0.16f, 0.21f, 1f);

            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.minHeight = 108f;
            layout.preferredHeight = 108f;

            ItemRowView view = root.AddComponent<ItemRowView>();

            Text slotText = MakeText(root.transform, "SlotText", "BAG", 18, TextAnchor.MiddleLeft, new Color(0.55f, 0.60f, 0.70f));
            SetRect(slotText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(140f, 0f));

            Text nameText = MakeText(root.transform, "NameText", "Blade of Vigor", 24, TextAnchor.MiddleLeft, Color.white);
            SetRect(nameText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(170f, -10f), new Vector2(-360f, 32f));

            Text bonusText = MakeText(root.transform, "BonusText", "+2 Strength", 19, TextAnchor.MiddleLeft, AccentColor);
            SetRect(bonusText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(170f, -44f), new Vector2(-360f, 26f));

            Text descriptionText = MakeText(root.transform, "DescriptionText", "A chipped but eager blade.", 17, TextAnchor.UpperLeft, MutedTextColor);
            SetRect(descriptionText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(170f, -70f), new Vector2(-360f, 34f));

            // What the merchant pays for this row. Hidden everywhere except at a shop, so the bag reads as
            // it always did except where there is somebody to sell to.
            Text priceText = MakeText(root.transform, "PriceText", "", 20, TextAnchor.MiddleRight, new Color(0.98f, 0.86f, 0.45f));
            SetRect(priceText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-200f, -14f), new Vector2(160f, 28f));
            priceText.gameObject.SetActive(false);

            Button actionButton = MakeButton(root.transform, "ActionButton", "Equip", 20, ButtonColor);
            SetRect(actionButton.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-20f, 0f), new Vector2(160f, 48f));
            Text actionLabel = actionButton.GetComponentInChildren<Text>();

            Assign(view, "slotText", slotText);
            Assign(view, "nameText", nameText);
            Assign(view, "bonusText", bonusText);
            Assign(view, "descriptionText", descriptionText);
            Assign(view, "actionButton", actionButton);
            Assign(view, "actionLabel", actionLabel);
            Assign(view, "priceText", priceText);

            return SavePrefab(root, ItemRowPrefabPath);
        }

        static GameObject BuildPathButtonPrefab()
        {
            // Sized so four of these fit side by side in the junction layout.
            GameObject root = NewUI("PathButton", null);
            SetRect(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(340f, 360f));

            Image background = root.AddComponent<Image>();
            background.color = new Color(0.16f, 0.20f, 0.31f, 1f);

            Button button = root.AddComponent<Button>();
            button.targetGraphic = background;

            PathButtonView view = root.AddComponent<PathButtonView>();

            Text iconText = MakeText(root.transform, "IconText", "!", 110, TextAnchor.MiddleCenter, Color.white);
            SetRect(iconText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 66f), new Vector2(300f, 140f));

            Text typeText = MakeText(root.transform, "TypeText", "COMBAT", 32, TextAnchor.MiddleCenter, new Color(0.85f, 0.89f, 1f));
            SetRect(typeText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -50f), new Vector2(320f, 46f));

            Text descriptionText = MakeText(root.transform, "DescriptionText", "A monster guards this path.", 19, TextAnchor.UpperCenter, MutedTextColor);
            SetRect(descriptionText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(312f, 80f));

            Assign(view, "iconText", iconText);
            Assign(view, "typeText", typeText);
            Assign(view, "descriptionText", descriptionText);
            Assign(view, "button", button);

            return SavePrefab(root, PathButtonPrefabPath);
        }

        static GameObject BuildSkillRowPrefab()
        {
            GameObject root = NewUI("SkillRow", null);
            SetRect(root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 44f));

            Image background = root.AddComponent<Image>();
            background.color = new Color(0.15f, 0.16f, 0.21f, 1f);

            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.minHeight = 44f;
            layout.preferredHeight = 44f;

            SkillRowView view = root.AddComponent<SkillRowView>();

            Text nameText = MakeText(root.transform, "NameText", "STRENGTH", 24, TextAnchor.MiddleLeft, Color.white);
            SetRect(nameText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(320f, 0f));

            Text valueText = MakeText(root.transform, "ValueText", "0", 24, TextAnchor.MiddleLeft, AccentColor);
            SetRect(valueText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(360f, 0f), new Vector2(520f, 0f));

            Text costText = MakeText(root.transform, "CostText", "100 XP", 22, TextAnchor.MiddleRight, MutedTextColor);
            SetRect(costText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(1140f, 0f), new Vector2(160f, 0f));

            Button levelButton = MakeButton(root.transform, "LevelButton", "+", 30, new Color(0.22f, 0.44f, 0.30f, 1f));
            SetRect(levelButton.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(1330f, 0f), new Vector2(70f, 34f));

            Assign(view, "nameText", nameText);
            Assign(view, "valueText", valueText);
            Assign(view, "costText", costText);
            Assign(view, "levelButton", levelButton);
            Assign(view, "levelButtonLabel", levelButton.GetComponentInChildren<Text>());

            return SavePrefab(root, SkillRowPrefabPath);
        }

        static GameObject BuildEventOptionPrefab()
        {
            Button button = MakeButton(null, "EventOption", "Option", 24, new Color(0.18f, 0.22f, 0.33f, 1f));
            GameObject root = button.gameObject;

            Text label = button.GetComponentInChildren<Text>();
            if (label != null) label.alignment = TextAnchor.MiddleCenter;

            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.minHeight = 64f;
            layout.preferredHeight = 64f;

            return SavePrefab(root, EventOptionPrefabPath);
        }

        // ------------------------------------------------------------------ scenes

        static void BuildMainMenuScene(GameDatabase database)
        {
            Scene scene = BeginScene();
            Canvas canvas = MakeCanvas();

            Image background = MakePanel(canvas.transform, "Background", BackgroundColor);
            Stretch(background.gameObject, 0f, 0f, 0f, 0f);

            Text title = MakeText(canvas.transform, "TitleText", "DUNGEON CARDS", 84, TextAnchor.MiddleCenter, Color.white);
            SetRect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(1400f, 110f));

            Text subtitle = MakeText(canvas.transform, "SubtitleText", "Explore the dungeon, fight what guards it, and find the way out.", 26, TextAnchor.MiddleCenter, MutedTextColor);
            SetRect(subtitle.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -240f), new Vector2(1400f, 40f));

            Button playButton = MakeButton(canvas.transform, "PlayButton", "PLAY", 34, ButtonColor);
            SetRect(playButton.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(420f, 88f));

            Button characterButton = MakeButton(canvas.transform, "CharacterButton", "CHARACTER (I)", 26, ButtonColor);
            SetRect(characterButton.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(420f, 72f));

            Button quitButton = MakeButton(canvas.transform, "QuitButton", "QUIT", 24, new Color(0.32f, 0.18f, 0.21f, 1f));
            SetRect(quitButton.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -100f), new Vector2(420f, 64f));

            Text hintText = MakeText(canvas.transform, "HintText", "", 22, TextAnchor.MiddleCenter, MutedTextColor);
            SetRect(hintText.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(1400f, 30f));

            Text lastRunText = MakeText(canvas.transform, "LastRunText", "", 24, TextAnchor.MiddleCenter, new Color(1f, 0.72f, 0.72f));
            SetRect(lastRunText.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(1400f, 30f));

            Text statusText = MakeText(canvas.transform, "StatusText", "", 24, TextAnchor.LowerLeft, MutedTextColor);
            SetRect(statusText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 30f), new Vector2(1000f, 40f));

            var controllerGo = new GameObject("MainMenu");
            MainMenuController controller = controllerGo.AddComponent<MainMenuController>();
            Assign(controller, "playButton", playButton);
            Assign(controller, "characterButton", characterButton);
            Assign(controller, "quitButton", quitButton);
            Assign(controller, "statusText", statusText);
            Assign(controller, "hintText", hintText);
            Assign(controller, "lastRunText", lastRunText);

            CreateSessionObject(database);
            SaveScene(scene, GameSession.MainMenuScene);
        }

        static void BuildCombatScene(GameDatabase database, GameObject cardPrefab)
        {
            Scene scene = BeginScene();
            Canvas canvas = MakeCanvas();

            Image background = MakePanel(canvas.transform, "Background", new Color(0.07f, 0.07f, 0.10f, 1f));
            Stretch(background.gameObject, 0f, 0f, 0f, 0f);

            // ---- enemy panel (top right)
            Image enemyPanel = MakePanel(canvas.transform, "EnemyPanel", PanelColor);
            SetRect(enemyPanel.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(640f, 240f));

            Text enemyNameText = MakeText(enemyPanel.transform, "EnemyNameText", "Goblin", 36, TextAnchor.MiddleLeft, Color.white);
            SetRect(enemyNameText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(-48f, 46f));

            Text enemyHealthText = MakeText(enemyPanel.transform, "EnemyHealthText", "Health 20 / 20", 26, TextAnchor.MiddleLeft, new Color(1f, 0.72f, 0.72f));
            SetRect(enemyHealthText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -72f), new Vector2(-48f, 36f));

            Text enemyBlockText = MakeText(enemyPanel.transform, "EnemyBlockText", "", 24, TextAnchor.MiddleLeft, AccentColor);
            SetRect(enemyBlockText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -114f), new Vector2(-48f, 32f));

            Text enemyStatusText = MakeText(enemyPanel.transform, "EnemyStatusText", "No active effects", 20, TextAnchor.MiddleLeft, MutedTextColor);
            SetRect(enemyStatusText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -156f), new Vector2(-48f, 32f));

            // ---- player panel (top left)
            Image playerPanel = MakePanel(canvas.transform, "PlayerPanel", PanelColor);
            SetRect(playerPanel.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(640f, 360f));

            Text playerHealthText = MakeText(playerPanel.transform, "PlayerHealthText", "Health 30 / 30", 30, TextAnchor.MiddleLeft, Color.white);
            SetRect(playerHealthText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(-48f, 40f));

            Text playerEnergyText = MakeText(playerPanel.transform, "PlayerEnergyText", "Energy 3 / 3", 30, TextAnchor.MiddleLeft, AccentColor);
            SetRect(playerEnergyText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -60f), new Vector2(-48f, 40f));

            Text playerManaText = MakeText(playerPanel.transform, "PlayerManaText", "Mana 0", 30, TextAnchor.MiddleLeft, new Color(0.62f, 0.72f, 1f, 1f));
            SetRect(playerManaText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -104f), new Vector2(-48f, 40f));

            Text playerBlockText = MakeText(playerPanel.transform, "PlayerBlockText", "Shield 0", 26, TextAnchor.MiddleLeft, AccentColor);
            SetRect(playerBlockText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -148f), new Vector2(-48f, 34f));

            // The attribute breakdown that used to sit here now lives in the inventory overlay, so the HUD
            // keeps only what actually changes during a fight.
            Text playerStatusText = MakeText(playerPanel.transform, "PlayerStatusText", "No active effects", 20, TextAnchor.MiddleLeft, MutedTextColor);
            SetRect(playerStatusText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -186f), new Vector2(-48f, 30f));

            // The squares show the same tooltip the cards do.
            CardTooltip sharedTooltip = CardTooltipOf(cardPrefab);

            // ---- board: one square per construct in play, filled from the left and carrying on underneath
            // itself once a line is full. The square stands in for a card's art and prints nothing at all:
            // hovering one brings up the construct's card, turns left and all, which is where its wording
            // belongs and which keeps a row of squares uncluttered. The names stay beneath as the same
            // readout in words.
            //
            // The row is built as wide as a board can ever be; CombatManager shows only the slots the player
            // owns, so the spare squares are built here rather than being added to the scene later. Where a
            // square lands comes from CombatManager's own numbers, so the scene and the row drawn at runtime
            // cannot disagree about the wrap.
            float slotStep = CombatManager.SlotSize + CombatManager.SlotSpacing;

            Image boardSlots = MakePanel(playerPanel.transform, "BoardSlots", new Color(1f, 1f, 1f, 0f));
            SetRect(boardSlots.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                    new Vector2(24f, CombatManager.SlotRowTop), new Vector2(-48f, CombatManager.SlotSize));
            boardSlots.raycastTarget = false;

            var boardSlotViews = new BoardSlotView[PlayerStats.MaxBoardSlots];
            for (int i = 0; i < boardSlotViews.Length; i++)
            {
                GameObject slotGo = NewUI("BoardSlot" + (i + 1), boardSlots.transform);
                SetRect(slotGo, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2((i % CombatManager.SlotsPerRow) * slotStep,
                                    -(i / CombatManager.SlotsPerRow) * slotStep),
                        new Vector2(CombatManager.SlotSize, CombatManager.SlotSize));

                // The frame is the square's body, and the one thing here that catches the pointer: it is
                // what lets a square be hovered at all.
                Image frame = slotGo.AddComponent<Image>();
                frame.color = new Color(1f, 1f, 1f, 0.06f);

                Image icon = MakePanel(slotGo.transform, "Icon", new Color(0.82f, 0.74f, 1f, 0.9f));
                Stretch(icon.gameObject, 5f, 5f, 5f, 5f);
                icon.raycastTarget = false;
                icon.enabled = false;

                // A square explains itself the way a card does, so it wears the same lift: the pointer opens
                // the construct's tooltip and the square grows a little to say it was found.
                BoardSlotView view = slotGo.AddComponent<BoardSlotView>();
                CardHoverEffect hover = slotGo.AddComponent<CardHoverEffect>();

                EventTrigger trigger = slotGo.AddComponent<EventTrigger>();
                trigger.triggers = new List<EventTrigger.Entry>();
                trigger.triggers.Add(PersistentEntry(EventTriggerType.PointerEnter, hover.OnPointerEnter));
                trigger.triggers.Add(PersistentEntry(EventTriggerType.PointerExit, hover.OnPointerExit));

                Assign(view, "icon", icon);
                Assign(view, "tooltipPrefab", sharedTooltip);

                boardSlotViews[i] = view;
                slotGo.SetActive(i < PlayerStats.BaseBoardSlots);
            }

            Text boardText = MakeText(playerPanel.transform, "BoardText", "BOARD: nothing established", 20, TextAnchor.UpperLeft, new Color(0.82f, 0.74f, 1f));
            SetRect(boardText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(24f, -284f), new Vector2(-48f, 70f));

            // ---- turn label
            Text turnText = MakeText(canvas.transform, "TurnText", "Your turn", 30, TextAnchor.MiddleCenter, Color.white);
            SetRect(turnText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(600f, 50f));

            // ---- combat log
            Text logText = MakeText(canvas.transform, "LogText", "", 20, TextAnchor.UpperLeft, MutedTextColor);
            SetRect(logText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -310f), new Vector2(640f, 380f));

            // ---- piles
            Text pileText = MakeText(canvas.transform, "PileText", "Draw 0    Discard 0    Deck 0", 22, TextAnchor.LowerLeft, MutedTextColor);
            SetRect(pileText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 358f), new Vector2(900f, 36f));

            // ---- hand. Cards are 180x270 at a fixed 2:3 ratio, so the strip holds them plus the lift they
            // take when hovered. It does not catch the pointer, so the corner buttons stay clickable through it.
            Image handPanel = MakePanel(canvas.transform, "HandPanel", new Color(1f, 1f, 1f, 0.03f));
            SetRect(handPanel.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(1560f, 316f));
            handPanel.raycastTarget = false;

            // The hand is fanned by HandFan rather than laid out by a group. A layout group reflows the whole
            // strip whenever a card is re-sorted, which is what used to make a hovered card jitter, and it
            // cannot overlap the cards at all. The fan positions and tilts the cards itself and never
            // reorders them; CardHoverEffect still owns their scale.
            HandFan handFan = handPanel.gameObject.AddComponent<HandFan>();

            // ---- end turn
            Button endTurnButton = MakeButton(canvas.transform, "EndTurnButton", "END TURN", 26, ButtonColor);
            SetRect(endTurnButton.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(250f, 84f));

            // ---- character. One button, one screen. It opens the character menu the rest of the game
            // uses, and the fight is held in the GameSession, so leaving the scene to read the deck does
            // not disturb the encounter.
            Button characterButton = MakeButton(canvas.transform, "CharacterButton", "CHARACTER (I)", 22, ButtonColor);
            SetRect(characterButton.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(280f, 84f));

            // ---- defeat overlay
            Image defeatPanel = MakePanel(canvas.transform, "DefeatPanel", new Color(0f, 0f, 0f, 0.92f));
            Stretch(defeatPanel.gameObject, 0f, 0f, 0f, 0f);

            Text defeatText = MakeText(defeatPanel.transform, "DefeatText", "YOU DIED", 90, TextAnchor.MiddleCenter, new Color(1f, 0.42f, 0.42f));
            SetRect(defeatText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1200f, 140f));

            Text defeatReasonText = MakeText(defeatPanel.transform, "DefeatReasonText", "", 30, TextAnchor.MiddleCenter, MutedTextColor);
            SetRect(defeatReasonText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1200f, 60f));

            Button returnToMenuButton = MakeButton(defeatPanel.transform, "ReturnToMenuButton", "RETURN TO MAIN MENU", 26, ButtonColor);
            SetRect(returnToMenuButton.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(500f, 84f));

            defeatPanel.gameObject.SetActive(false);

            // ---- side screens
            MapOverlay overlay = AddMapOverlay(canvas);
            Button mapButton = MakeButton(canvas.transform, "MapButton", "MAP (M)", 22, ButtonColor);
            SetRect(mapButton.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-310f, 40f), new Vector2(220f, 84f));
            Assign(overlay, "openButton", mapButton);

            // ---- manager
            var managerGo = new GameObject("CombatManager");
            CombatManager manager = managerGo.AddComponent<CombatManager>();
            Assign(manager, "cardViewPrefab", cardPrefab.GetComponent<CardView>());
            Assign(manager, "handContainer", handPanel.transform);
            Assign(manager, "handFan", handFan);
            Assign(manager, "enemyNameText", enemyNameText);
            Assign(manager, "enemyHealthText", enemyHealthText);
            Assign(manager, "enemyBlockText", enemyBlockText);
            Assign(manager, "enemyStatusText", enemyStatusText);
            Assign(manager, "playerHealthText", playerHealthText);
            Assign(manager, "playerEnergyText", playerEnergyText);
            Assign(manager, "playerManaText", playerManaText);
            Assign(manager, "playerBlockText", playerBlockText);
            Assign(manager, "playerStatusText", playerStatusText);
            Assign(manager, "boardText", boardText);
            Assign(manager, "boardSlotsRect", boardSlots.rectTransform);
            Assign(manager, "boardTextRect", boardText.rectTransform);
            AssignBoardSlots(manager, boardSlotViews);
            Assign(manager, "turnText", turnText);
            Assign(manager, "pileText", pileText);
            Assign(manager, "logText", logText);
            Assign(manager, "endTurnButton", endTurnButton);
            Assign(manager, "characterButton", characterButton);
            Assign(manager, "defeatPanel", defeatPanel.gameObject);
            Assign(manager, "defeatReasonText", defeatReasonText);
            Assign(manager, "returnToMenuButton", returnToMenuButton);

            CreateSessionObject(database);
            SaveScene(scene, GameSession.CombatScene);
        }

        static void BuildRewardScene(GameDatabase database, GameObject cardPrefab)
        {
            Scene scene = BeginScene();
            Canvas canvas = MakeCanvas();

            Image background = MakePanel(canvas.transform, "Background", BackgroundColor);
            Stretch(background.gameObject, 0f, 0f, 0f, 0f);

            Text titleText = MakeText(canvas.transform, "TitleText", "VICTORY", 90, TextAnchor.MiddleCenter, new Color(0.72f, 1f, 0.78f));
            SetRect(titleText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(1400f, 110f));

            Text experienceText = MakeText(canvas.transform, "ExperienceText", "+0 XP", 52, TextAnchor.MiddleCenter, Color.white);
            SetRect(experienceText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(1400f, 60f));

            // The fight's other payout, directly under the experience so the two read as one result. Gold is
            // what the shop takes, so it is the number that tells the player what a shelf price is worth.
            Text goldText = MakeText(canvas.transform, "GoldText", "+0 gold", 34, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.42f));
            SetRect(goldText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -288f), new Vector2(1400f, 50f));

            // ---- the card offer, drawn as cards
            Text rewardTitleText = MakeText(canvas.transform, "RewardTitleText", "TAKE A CARD", 40, TextAnchor.MiddleCenter, AccentColor);
            SetRect(rewardTitleText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -348f), new Vector2(1400f, 48f));

            // The offers are the card prefab, instantiated side by side by the controller: the same object
            // the hand and the deck grid draw, so an offer is read by the same means as a card already in
            // the deck rather than described in a paragraph about itself.
            Image rewardPanel = MakePanel(canvas.transform, "RewardPanel", new Color(1f, 1f, 1f, 0f));
            SetRect(rewardPanel.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -578f), new Vector2(940f, 400f));

            HorizontalLayoutGroup rewardLayout = rewardPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            rewardLayout.spacing = 40f;
            rewardLayout.childAlignment = TextAnchor.MiddleCenter;
            rewardLayout.childControlWidth = false;
            rewardLayout.childControlHeight = false;
            rewardLayout.childForceExpandWidth = false;
            rewardLayout.childForceExpandHeight = false;

            Text rewardResultText = MakeText(canvas.transform, "RewardResultText", "", 30, TextAnchor.MiddleCenter, new Color(0.86f, 0.92f, 1f));
            SetRect(rewardResultText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -812f), new Vector2(1400f, 44f));

            // The reading sits along the bottom left, clear of the card band above it and of the button
            // that ends the screen.
            Text summaryText = MakeText(canvas.transform, "SummaryText", "", 26, TextAnchor.LowerLeft, MutedTextColor);
            SetRect(summaryText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 36f), new Vector2(1020f, 190f));

            Button continueButton = MakeButton(canvas.transform, "ContinueButton", "SKIP", 30, ButtonColor);
            SetRect(continueButton.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 54f), new Vector2(400f, 88f));

            var controllerGo = new GameObject("RewardScreen");
            RewardScreenController controller = controllerGo.AddComponent<RewardScreenController>();
            Assign(controller, "titleText", titleText);
            Assign(controller, "experienceText", experienceText);
            Assign(controller, "goldText", goldText);
            Assign(controller, "rewardTitleText", rewardTitleText);
            Assign(controller, "rewardContainer", rewardPanel.rectTransform);
            Assign(controller, "cardPrefab", cardPrefab != null ? cardPrefab.GetComponent<CardView>() : null);
            Assign(controller, "rewardResultText", rewardResultText);
            Assign(controller, "summaryText", summaryText);
            Assign(controller, "continueButton", continueButton);

            CreateSessionObject(database);
            SaveScene(scene, GameSession.RewardScene);
        }

        static void BuildPathSelectionScene(GameDatabase database, GameObject pathButtonPrefab)
        {
            Scene scene = BeginScene();
            Canvas canvas = MakeCanvas();

            Image background = MakePanel(canvas.transform, "Background", BackgroundColor);
            Stretch(background.gameObject, 0f, 0f, 0f, 0f);

            Text titleText = MakeText(canvas.transform, "TitleText", "Choose your path", 64, TextAnchor.MiddleCenter, Color.white);
            SetRect(titleText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1400f, 90f));

            Text infoText = MakeText(canvas.transform, "InfoText", "", 26, TextAnchor.UpperCenter, MutedTextColor);
            SetRect(infoText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(1400f, 90f));

            Text noticeText = MakeText(canvas.transform, "NoticeText", "", 24, TextAnchor.UpperCenter, AccentColor);
            SetRect(noticeText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -290f), new Vector2(1400f, 40f));

            Image pathPanel = MakePanel(canvas.transform, "PathContainer", new Color(1f, 1f, 1f, 0f));
            SetRect(pathPanel.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(1500f, 420f));

            HorizontalLayoutGroup pathLayout = pathPanel.gameObject.AddComponent<HorizontalLayoutGroup>();
            pathLayout.spacing = 28f;
            pathLayout.childAlignment = TextAnchor.MiddleCenter;
            pathLayout.childControlWidth = false;
            pathLayout.childControlHeight = false;
            pathLayout.childForceExpandWidth = false;
            pathLayout.childForceExpandHeight = false;

            Button characterButton = MakeButton(canvas.transform, "CharacterButton", "CHARACTER (I)", 24, ButtonColor);
            SetRect(characterButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-300f, 40f), new Vector2(340f, 68f));

            Button mapButton = MakeButton(canvas.transform, "MapButton", "MAP (M)", 24, ButtonColor);
            SetRect(mapButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(60f, 40f), new Vector2(340f, 68f));

            Button leaveButton = MakeButton(canvas.transform, "LeaveButton", "LEAVE DUNGEON", 24, new Color(0.32f, 0.18f, 0.21f, 1f));
            SetRect(leaveButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(420f, 40f), new Vector2(340f, 68f));

            var controllerGo = new GameObject("PathSelection");
            PathSelectionController controller = controllerGo.AddComponent<PathSelectionController>();
            Assign(controller, "pathContainer", pathPanel.transform);
            Assign(controller, "pathButtonPrefab", pathButtonPrefab.GetComponent<PathButtonView>());
            Assign(controller, "titleText", titleText);
            Assign(controller, "infoText", infoText);
            Assign(controller, "noticeText", noticeText);
            Assign(controller, "characterButton", characterButton);
            Assign(controller, "mapButton", mapButton);
            Assign(controller, "leaveButton", leaveButton);

            MapOverlay overlay = AddMapOverlay(canvas);
            Assign(overlay, "openButton", mapButton);

            CreateSessionObject(database);
            SaveScene(scene, GameSession.PathSelectionScene);
        }

        /// <summary>A heading inside a rack, so a rack column reads as three named shelves.</summary>
        static void MakeShopSectionLabel(Transform parent, string name, string label)
        {
            Text text = MakeText(parent, name, label, 22, TextAnchor.MiddleLeft, AccentColor);

            LayoutElement layout = text.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 28f;
            layout.preferredHeight = 28f;
        }

        /// <summary>
        /// One row of a buy rack: what it is, what it does and what it costs, with the button that spends
        /// the gold. A rack is always the same size, so its rows are fixed objects in the scene.
        /// </summary>
        static ShopEntryView MakeShopEntryRow(Transform parent, string name)
        {
            GameObject root = NewUI(name, parent);
            SetRect(root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 58f));

            Image background = root.AddComponent<Image>();
            background.color = new Color(0.15f, 0.16f, 0.21f, 1f);

            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.minHeight = 58f;
            layout.preferredHeight = 58f;

            ShopEntryView view = root.AddComponent<ShopEntryView>();

            Text nameText = MakeText(root.transform, "NameText", "", 22, TextAnchor.MiddleLeft, Color.white);
            SetRect(nameText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(16f, -4f), new Vector2(-360f, 24f));

            Text detailText = MakeText(root.transform, "DetailText", "", 17, TextAnchor.MiddleLeft, MutedTextColor);
            SetRect(detailText.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(16f, -29f), new Vector2(-360f, 22f));

            Text priceText = MakeText(root.transform, "PriceText", "", 20, TextAnchor.MiddleRight, new Color(0.98f, 0.86f, 0.45f));
            SetRect(priceText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-176f, -15f), new Vector2(150f, 26f));

            Button buyButton = MakeButton(root.transform, "BuyButton", "BUY", 20, ButtonColor);
            SetRect(buyButton.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(150f, 44f));

            Assign(view, "buyButton", buyButton);
            Assign(view, "nameText", nameText);
            Assign(view, "detailText", detailText);
            Assign(view, "priceText", priceText);

            return view;
        }

        /// <summary>Card face size on the shelf. The prefab's aspect fitter derives the height from it.</summary>
        const float ShopCardWidth = 196f;
        const float ShopCardHeight = 294f;
        const float ShopCardSlotHeight = 344f;

        /// <summary>
        /// The strip the shelf's cards sit in, inside the card rack. A hand reads left to right, so the
        /// shop's cards do too.
        /// </summary>
        static RectTransform MakeShopCardRow(Transform parent, string name)
        {
            GameObject row = NewUI(name, parent);
            SetRect(row, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, ShopCardSlotHeight));

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 18f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            LayoutElement element = row.AddComponent<LayoutElement>();
            element.minHeight = ShopCardSlotHeight;
            element.preferredHeight = ShopCardSlotHeight;

            return (RectTransform)row.transform;
        }

        /// <summary>
        /// One card on the shelf: the card prefab itself, with its price underneath. Instantiating the same
        /// prefab the hand, the deck grid and the reward screen use is the whole point of the rack, so what
        /// is on the shelf is the object that joins the deck.
        /// </summary>
        static ShopCardView MakeShopCardSlot(Transform parent, string name, GameObject cardPrefab)
        {
            GameObject root = NewUI(name, parent);
            SetRect(root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(ShopCardWidth, ShopCardSlotHeight));

            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.minWidth = ShopCardWidth;
            layout.preferredWidth = ShopCardWidth;
            layout.minHeight = ShopCardSlotHeight;
            layout.preferredHeight = ShopCardSlotHeight;

            ShopCardView slot = root.AddComponent<ShopCardView>();

            GameObject cardObject = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab, root.transform);
            SetRect(cardObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(ShopCardWidth, ShopCardHeight));

            CardView cardView = cardObject.GetComponent<CardView>();

            Text priceText = MakeText(root.transform, "PriceText", "0 g", 22, TextAnchor.MiddleCenter, new Color(0.98f, 0.86f, 0.45f));
            SetRect(priceText.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(ShopCardWidth, 34f));

            Assign(slot, "cardView", cardView);
            Assign(slot, "priceText", priceText);

            return slot;
        }

        /// <summary>
        /// The line the relic icons sit in. Deliberately not a layout group: the rack writes each icon's
        /// position itself, and a layout group writing the same values is what made the combat hand jitter
        /// when a card was hovered. It is only here so the icons have a box to be centred in.
        /// </summary>
        static RectTransform MakeShopRelicRow(Transform parent, string name)
        {
            GameObject row = NewUI(name, parent);
            SetRect(row, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(RelicIconSpacing * GameSession.ShopRelicCount, RelicIconSize + 44f));

            // The rack above this one is a vertical stack, so the row has to declare its own height or it
            // is sized by whatever the stack guesses.
            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.minHeight = RelicIconSize + 44f;
            layout.preferredHeight = RelicIconSize + 44f;

            return (RectTransform)row.transform;
        }

        /// <summary>
        /// One relic on the rack: the icon prefab itself, placed where it belongs in the line. The rack
        /// takes the position over from the first frame, so this is only what the scene looks like before
        /// anything runs.
        /// </summary>
        static RelicIconView MakeShopRelicSlot(Transform parent, string name, GameObject relicIconPrefab, int index, int count)
        {
            GameObject icon = (GameObject)PrefabUtility.InstantiatePrefab(relicIconPrefab, parent);

            float span = (count - 1) * 0.5f;
            SetRect(icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2((index - span) * RelicIconSpacing, 0f), new Vector2(RelicIconSize, RelicIconSize));

            icon.name = name;
            return icon.GetComponent<RelicIconView>();
        }


        /// <summary>
        /// A rack of wares: a heading and its rows, top down. Every rack is a known size, so the whole
        /// shop is a fixed stack that fits on one screen and nothing on it needs to scroll.
        /// </summary>
        static RectTransform MakeShopRack(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject root = NewUI(name, parent);
            SetRect(root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), anchoredPosition, size);

            Image background = root.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.03f);

            VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 5f;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return (RectTransform)root.transform;
        }

        static void BuildRoomScene(GameDatabase database, GameObject eventOptionPrefab, GameObject cardPrefab, GameObject relicIconPrefab)
        {
            Scene scene = BeginScene();
            Canvas canvas = MakeCanvas();

            Image background = MakePanel(canvas.transform, "Background", BackgroundColor);
            Stretch(background.gameObject, 0f, 0f, 0f, 0f);

            Text roomTitleText = MakeText(canvas.transform, "RoomTitleText", "EVENT ROOM", 56, TextAnchor.MiddleCenter, Color.white);
            SetRect(roomTitleText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1400f, 80f));

            Text noticeText = MakeText(canvas.transform, "NoticeText", "", 24, TextAnchor.MiddleCenter, AccentColor);
            SetRect(noticeText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1400f, 40f));

            // ---- event panel
            Image eventPanel = MakePanel(canvas.transform, "EventPanel", new Color(1f, 1f, 1f, 0f));
            SetRect(eventPanel.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(1600f, 760f));

            Text eventTitleText = MakeText(eventPanel.transform, "EventTitleText", "Event", 46, TextAnchor.MiddleCenter, Color.white);
            SetRect(eventTitleText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(1400f, 60f));

            Text eventBodyText = MakeText(eventPanel.transform, "EventBodyText", "", 26, TextAnchor.UpperCenter, MutedTextColor);
            SetRect(eventBodyText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(1400f, 120f));

            Image optionsPanel = MakePanel(eventPanel.transform, "OptionsContainer", new Color(1f, 1f, 1f, 0f));
            SetRect(optionsPanel.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(1000f, 300f));

            VerticalLayoutGroup optionsLayout = optionsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            optionsLayout.spacing = 14f;
            optionsLayout.childAlignment = TextAnchor.UpperCenter;
            optionsLayout.childControlWidth = true;
            optionsLayout.childControlHeight = true;
            optionsLayout.childForceExpandWidth = true;
            optionsLayout.childForceExpandHeight = false;

            Text eventResultText = MakeText(eventPanel.transform, "EventResultText", "", 26, TextAnchor.UpperCenter, new Color(0.86f, 0.92f, 1f));
            SetRect(eventResultText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -540f), new Vector2(1400f, 140f));

            Button eventContinueButton = MakeButton(eventPanel.transform, "EventContinueButton", "CONTINUE", 28, ButtonColor);
            SetRect(eventContinueButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(380f, 76f));
            eventContinueButton.gameObject.SetActive(false);

            // ---- vantage panel
            Image vantagePanel = MakePanel(canvas.transform, "VantagePanel", new Color(1f, 1f, 1f, 0f));
            SetRect(vantagePanel.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(1600f, 700f));

            Text vantageTitleText = MakeText(vantagePanel.transform, "VantageTitleText", "VANTAGE POINT", 52, TextAnchor.MiddleCenter, new Color(0.72f, 0.96f, 1f));
            SetRect(vantageTitleText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1400f, 70f));

            Text vantageBodyText = MakeText(vantagePanel.transform, "VantageBodyText", "", 28, TextAnchor.UpperCenter, MutedTextColor);
            SetRect(vantageBodyText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(1400f, 340f));

            Button vantageContinueButton = MakeButton(vantagePanel.transform, "VantageContinueButton", "CONTINUE", 28, ButtonColor);
            SetRect(vantageContinueButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(380f, 76f));

            vantagePanel.gameObject.SetActive(false);

            // ---- exit panel
            Image exitPanel = MakePanel(canvas.transform, "ExitPanel", new Color(0.05f, 0.09f, 0.07f, 0.96f));
            SetRect(exitPanel.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(1600f, 600f));

            Text exitTitleText = MakeText(exitPanel.transform, "ExitTitleText", "YOU ESCAPED", 84, TextAnchor.MiddleCenter, new Color(0.72f, 1f, 0.78f));
            SetRect(exitTitleText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1400f, 110f));

            Text exitBodyText = MakeText(exitPanel.transform, "ExitBodyText", "", 30, TextAnchor.UpperCenter, MutedTextColor);
            SetRect(exitBodyText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1400f, 160f));

            Button exitButton = MakeButton(exitPanel.transform, "ExitButton", "RETURN TO MAIN MENU", 26, ButtonColor);
            SetRect(exitButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(520f, 80f));

            exitPanel.gameObject.SetActive(false);

            // ---- shop panel
            // Fully opaque: the room's own title and notice sit directly behind the shop, and at anything
            // short of solid they read through the merchant's name.
            Image shopPanel = MakePanel(canvas.transform, "ShopPanel", new Color(0.05f, 0.05f, 0.08f, 1f));
            Stretch(shopPanel.gameObject, 0f, 0f, 0f, 0f);

            ShopPanel shop = shopPanel.gameObject.AddComponent<ShopPanel>();

            Text shopTitleText = MakeText(shopPanel.transform, "ShopTitleText", "THE MERCHANT", 52, TextAnchor.MiddleCenter, Color.white);
            SetRect(shopTitleText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1200f, 70f));

            Text shopGoldText = MakeText(shopPanel.transform, "ShopGoldText", "GOLD 0", 34, TextAnchor.MiddleRight, new Color(0.98f, 0.86f, 0.45f));
            SetRect(shopGoldText.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -42f), new Vector2(520f, 50f));

            Text shopNoticeText = MakeText(shopPanel.transform, "ShopNoticeText", "", 26, TextAnchor.MiddleCenter, AccentColor);
            SetRect(shopNoticeText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -108f), new Vector2(1700f, 40f));

            // The cards across the top as the cards themselves, the relics as square icons in a line under
            // them, and the equipment as rows beside that. A card and a relic are the things being bought,
            // so both are drawn as themselves; equipment is a name, a bonus and a price, which is what a
            // row already is.
            RectTransform cardRack = MakeShopRack(shopPanel.transform, "CardRack", new Vector2(0f, -150f), new Vector2(1400f, 430f));

            MakeShopSectionLabel(cardRack, "CardsLabel", "CARDS");
            RectTransform cardRow = MakeShopCardRow(cardRack, "CardRow");
            var cardSlots = new ShopCardView[GameSession.ShopCardCount];
            for (int i = 0; i < cardSlots.Length; i++) cardSlots[i] = MakeShopCardSlot(cardRow, "CardSlot" + i, cardPrefab);

            RectTransform equipmentRack = MakeShopRack(shopPanel.transform, "EquipmentRack", new Vector2(490f, -600f), new Vector2(940f, 300f));

            MakeShopSectionLabel(equipmentRack, "EquipmentLabel", "EQUIPMENT");
            var equipmentRows = new ShopEntryView[GameSession.ShopEquipmentCount];
            for (int i = 0; i < equipmentRows.Length; i++) equipmentRows[i] = MakeShopEntryRow(equipmentRack, "EquipmentRow" + i);

            // Selling is the character menu's job, so the merchant only has to say where it is. The room's
            // own CHARACTER button is still on the HUD under this screen, but the reminder is what makes
            // the shop look like it buys as well as sells.
            Text shopSellHintText = MakeText(shopPanel.transform, "ShopSellHintText", "Press I for your character sheet to sell cards and items to the merchant.", 24, TextAnchor.MiddleLeft, MutedTextColor);
            SetRect(shopSellHintText.gameObject, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 46f), new Vector2(1100f, 40f));

            // Kept out of the bottom centre, which belongs to the map and character buttons: a leave
            // button there would sit half buried under them.
            Button shopLeaveButton = MakeButton(shopPanel.transform, "ShopLeaveButton", "LEAVE THE SHOP", 28, ButtonColor);
            SetRect(shopLeaveButton.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 30f), new Vector2(420f, 72f));

            // Everything above is created before this line and the relic rack after it, because draw order
            // inside the panel is simply hierarchy order. That puts the dark under the relics and over
            // everything else, which is what inspecting a value has to look like: the relic stays lit, the
            // shop behind it recedes, and the tooltip - its own canvas at order 500 - sits above it all.
            //
            // Nothing on it catches the pointer. Raycasting ignores draw order, so a scrim that did would
            // swallow the very hover it was put up for.
            Image shopScrim = MakePanel(shopPanel.transform, "ShopScrim", new Color(0f, 0f, 0f, 0f));
            Stretch(shopScrim.gameObject, 0f, 0f, 0f, 0f);
            shopScrim.raycastTarget = false;

            RectTransform relicRack = MakeShopRack(shopPanel.transform, "RelicRack", new Vector2(-490f, -600f), new Vector2(940f, 300f));

            MakeShopSectionLabel(relicRack, "RelicsLabel", "RELICS");
            RectTransform relicRow = MakeShopRelicRow(relicRack, "RelicRow");

            var relicIcons = new RelicIconView[GameSession.ShopRelicCount];
            for (int i = 0; i < relicIcons.Length; i++)
            {
                relicIcons[i] = MakeShopRelicSlot(relicRow, "RelicIcon" + i, relicIconPrefab, i, relicIcons.Length);
            }

            RelicRack relicLine = relicRack.gameObject.AddComponent<RelicRack>();

            Assign(shop, "goldText", shopGoldText);
            Assign(shop, "noticeText", shopNoticeText);
            AssignArray(shop, "cardSlots", cardSlots);
            AssignArray(shop, "relicRows", relicIcons);
            Assign(shop, "relicRack", relicLine);
            AssignArray(shop, "equipmentRows", equipmentRows);
            Assign(shop, "leaveButton", shopLeaveButton);
            Assign(relicLine, "scrim", shopScrim);

            shopPanel.gameObject.SetActive(false);

            // ---- side screens
            Button characterButton = MakeButton(canvas.transform, "CharacterButton", "CHARACTER (I)", 24, ButtonColor);
            SetRect(characterButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-220f, 30f), new Vector2(340f, 64f));

            Button mapButton = MakeButton(canvas.transform, "MapButton", "MAP (M)", 24, ButtonColor);
            SetRect(mapButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(140f, 30f), new Vector2(340f, 64f));

            var controllerGo = new GameObject("Room");
            RoomController controller = controllerGo.AddComponent<RoomController>();
            Assign(controller, "noticeText", noticeText);
            Assign(controller, "roomTitleText", roomTitleText);
            Assign(controller, "mapButton", mapButton);
            Assign(controller, "characterButton", characterButton);
            Assign(controller, "eventPanel", eventPanel.gameObject);
            Assign(controller, "eventTitleText", eventTitleText);
            Assign(controller, "eventBodyText", eventBodyText);
            Assign(controller, "optionContainer", optionsPanel.transform);
            Assign(controller, "optionButtonPrefab", eventOptionPrefab.GetComponent<Button>());
            Assign(controller, "eventResultText", eventResultText);
            Assign(controller, "eventContinueButton", eventContinueButton);
            Assign(controller, "vantagePanel", vantagePanel.gameObject);
            Assign(controller, "vantageTitleText", vantageTitleText);
            Assign(controller, "vantageBodyText", vantageBodyText);
            Assign(controller, "vantageContinueButton", vantageContinueButton);
            Assign(controller, "exitPanel", exitPanel.gameObject);
            Assign(controller, "exitTitleText", exitTitleText);
            Assign(controller, "exitBodyText", exitBodyText);
            Assign(controller, "exitButton", exitButton);
            Assign(controller, "shopPanel", shop);

            MapOverlay overlay = AddMapOverlay(canvas);
            Assign(overlay, "openButton", mapButton);

            CreateSessionObject(database);
            SaveScene(scene, GameSession.RoomScene);
        }

        static void BuildCharacterScene(GameDatabase database, GameObject itemRowPrefab, GameObject skillRowPrefab, GameObject cardPrefab)

        {
            Scene scene = BeginScene();
            Canvas canvas = MakeCanvas();

            Image background = MakePanel(canvas.transform, "Background", BackgroundColor);
            Stretch(background.gameObject, 0f, 0f, 0f, 0f);

            Text headerText = MakeText(canvas.transform, "HeaderText", "", 30, TextAnchor.MiddleCenter, Color.white);
            SetRect(headerText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1800f, 44f));

            Text noticeText = MakeText(canvas.transform, "NoticeText", "", 24, TextAnchor.MiddleCenter, AccentColor);
            SetRect(noticeText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(1800f, 34f));

            // The merchant's line: the purse and how to sell. Shown only when this menu was opened at a
            // shop, which is the only place anything pays for what the player is carrying.
            Text sellHintText = MakeText(canvas.transform, "SellHintText", "", 24, TextAnchor.MiddleCenter, new Color(0.98f, 0.86f, 0.45f));
            SetRect(sellHintText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(1800f, 32f));
            sellHintText.gameObject.SetActive(false);

            // ---- skills at the top
            Image skillsPanel = MakePanel(canvas.transform, "SkillsPanel", new Color(0.10f, 0.11f, 0.15f, 1f));
            SetRect(skillsPanel.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -172f), new Vector2(1420f, 292f));

            VerticalLayoutGroup skillsLayout = skillsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            skillsLayout.spacing = 8f;
            skillsLayout.padding = new RectOffset(14, 14, 14, 14);
            skillsLayout.childAlignment = TextAnchor.UpperCenter;
            skillsLayout.childControlWidth = true;
            skillsLayout.childControlHeight = true;
            skillsLayout.childForceExpandWidth = true;
            skillsLayout.childForceExpandHeight = false;

            // One row per attribute, taken from StatType rather than spelled out, so a stat added to the enum
            // arrives here with a row and a level button of its own.
            int statCount = System.Enum.GetValues(typeof(StatType)).Length;
            var skillRows = new SkillRowView[statCount];
            for (int i = 0; i < statCount; i++)

            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(skillRowPrefab, skillsPanel.transform);
                instance.name = ((StatType)i) + "Row";
                skillRows[i] = instance.GetComponent<SkillRowView>();
            }

            // ---- tabs. The deck is not one of them: VIEW DECK opens the full screen deck editor, and
            // the button sits in the slot the deck tab used to occupy.
            Button viewDeckButton = MakeButton(canvas.transform, "ViewDeckButton", "VIEW DECK", 26, ButtonColor);
            SetRect(viewDeckButton.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-410f, -470f), new Vector2(340f, 64f));

            Button equipmentTabButton = MakeButton(canvas.transform, "EquipmentTabButton", "EQUIPMENT", 26, ButtonColor);
            SetRect(equipmentTabButton.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -470f), new Vector2(340f, 64f));

            Button itemTabButton = MakeButton(canvas.transform, "ItemTabButton", "BAG", 26, ButtonColor);
            SetRect(itemTabButton.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(410f, -470f), new Vector2(340f, 64f));


            // ---- equipment panel
            Image equipmentPanel = MakePanel(canvas.transform, "EquipmentPanel", new Color(1f, 1f, 1f, 0f));
            SetRect(equipmentPanel.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -520f), new Vector2(1420f, 430f));

            Text equipmentSummaryText = MakeText(equipmentPanel.transform, "EquipmentSummaryText", "", 22, TextAnchor.MiddleCenter, AccentColor);
            SetRect(equipmentSummaryText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(1400f, 32f));

            RectTransform equipmentRowContainer = MakeScrollView(equipmentPanel.transform, "EquipmentScrollView",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1420f, 372f));

            equipmentPanel.gameObject.SetActive(false);

            // ---- bag panel
            Image itemPanel = MakePanel(canvas.transform, "ItemPanel", new Color(1f, 1f, 1f, 0f));
            SetRect(itemPanel.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -520f), new Vector2(1420f, 430f));

            Text itemSummaryText = MakeText(itemPanel.transform, "ItemSummaryText", "", 22, TextAnchor.MiddleCenter, AccentColor);
            SetRect(itemSummaryText.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(1400f, 32f));

            RectTransform itemRowContainer = MakeScrollView(itemPanel.transform, "ItemScrollView",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(1420f, 372f));

            itemPanel.gameObject.SetActive(false);

            // ---- back
            Button backButton = MakeButton(canvas.transform, "BackButton", "BACK", 26, ButtonColor);
            SetRect(backButton.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(340f, 64f));

            // The same full screen card grid the inventory overlay opens, so the deck reads identically
            // wherever you look at it.
            DeckViewPanel deckView = AddDeckView(cardPrefab);

            var controllerGo = new GameObject("CharacterMenu");

            CharacterMenuController controller = controllerGo.AddComponent<CharacterMenuController>();
            Assign(controller, "headerText", headerText);
            Assign(controller, "noticeText", noticeText);
            AssignArray(controller, "skillRows", skillRows);
            Assign(controller, "equipmentTabButton", equipmentTabButton);
            Assign(controller, "itemTabButton", itemTabButton);
            Assign(controller, "equipmentPanel", equipmentPanel.gameObject);
            Assign(controller, "itemPanel", itemPanel.gameObject);
            Assign(controller, "equipmentRowContainer", equipmentRowContainer);

            Assign(controller, "equipmentSummaryText", equipmentSummaryText);
            Assign(controller, "itemRowContainer", itemRowContainer);
            Assign(controller, "itemRowPrefab", itemRowPrefab.GetComponent<ItemRowView>());
            Assign(controller, "itemSummaryText", itemSummaryText);
            Assign(controller, "backButton", backButton);
            Assign(controller, "viewDeckButton", viewDeckButton);
            Assign(controller, "deckView", deckView);
            Assign(controller, "sellHintText", sellHintText);


            CreateSessionObject(database);
            SaveScene(scene, GameSession.CharacterScene);
        }

        /// <summary>Builds the map overlay into a canvas and returns its component.</summary>
        static MapOverlay AddMapOverlay(Canvas canvas)
        {
            var overlayGo = new GameObject("MapOverlay");
            MapOverlay overlay = overlayGo.AddComponent<MapOverlay>();

            Image panel = MakePanel(canvas.transform, "MapPanel", new Color(0.03f, 0.03f, 0.05f, 0.98f));
            Stretch(panel.gameObject, 0f, 0f, 0f, 0f);

            Text title = MakeText(panel.transform, "TitleText", "DUNGEON MAP", 52, TextAnchor.MiddleCenter, Color.white);
            SetRect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1400f, 70f));

            Text status = MakeText(panel.transform, "StatusText", "", 26, TextAnchor.MiddleCenter, AccentColor);
            SetRect(status.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -130f), new Vector2(1600f, 40f));

            GameObject graphGo = NewUI("GraphContainer", panel.transform);
            var graphRect = (RectTransform)graphGo.transform;
            graphRect.anchorMin = new Vector2(0.5f, 0.5f);
            graphRect.anchorMax = new Vector2(0.5f, 0.5f);
            graphRect.pivot = new Vector2(0.5f, 0.5f);
            graphRect.anchoredPosition = new Vector2(0f, 20f);
            graphRect.sizeDelta = new Vector2(1700f, 740f);

            Text hint = MakeText(panel.transform, "HintText", "", 20, TextAnchor.MiddleCenter, MutedTextColor);
            SetRect(hint.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(1400f, 30f));

            Button close = MakeButton(panel.transform, "CloseButton", "CLOSE (M)", 26, ButtonColor);
            SetRect(close.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(340f, 64f));

            Assign(overlay, "panel", panel.gameObject);
            Assign(overlay, "graphContainer", graphRect);
            Assign(overlay, "titleText", title);
            Assign(overlay, "statusText", status);
            Assign(overlay, "hintText", hint);
            Assign(overlay, "closeButton", close);

            return overlay;
        }

        // ------------------------------------------------------------------ scene plumbing

        static Scene BeginScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.orthographic = true;
            cameraGo.AddComponent<AudioListener>();

            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            AddInputModule(eventSystemGo);

            return scene;
        }

        static void AddInputModule(GameObject eventSystemGo)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            System.Type moduleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null)
            {
                eventSystemGo.AddComponent(moduleType);
                return;
            }
#endif
            eventSystemGo.AddComponent<StandaloneInputModule>();
        }

        static void CreateSessionObject(GameDatabase database)
        {
            var go = new GameObject("GameSession");
            GameSession session = go.AddComponent<GameSession>();
            Assign(session, "database", database);
        }

        static void SaveScene(Scene scene, string sceneName)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenesFolder + "/" + sceneName + ".unity");
        }

        // ------------------------------------------------------------------ ui helpers

        static Font UIFont()
        {
            if (uiFont != null) return uiFont;

            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return uiFont;
        }

        static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static Canvas MakeCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        static RectTransform SetRect(GameObject go, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        static RectTransform Stretch(GameObject go, float left, float right, float top, float bottom)
        {
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        static Text MakeText(Transform parent, string name, string content, int fontSize, TextAnchor alignment, Color color)
        {
            GameObject go = NewUI(name, parent);
            Text text = go.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static TextMeshProUGUI MakeTmpText(Transform parent, string name, string content, float fontSize,
                                          TextAlignmentOptions alignment, Color color, bool wrap)
        {
            GameObject go = NewUI(name, parent);
            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();

            // AddComponent does not reliably pick the default font up from a script, so assign it.
            if (text.font == null && TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static Button MakeButton(Transform parent, string name, string label, int fontSize, Color color)
        {
            GameObject go = NewUI(name, parent);

            Image image = go.AddComponent<Image>();
            image.color = color;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;

            Text labelText = MakeText(go.transform, "Label", label, fontSize, TextAnchor.MiddleCenter, Color.white);
            Stretch(labelText.gameObject, 8f, 8f, 4f, 4f);

            return button;
        }

        static InputField MakeInputField(Transform parent, string name, string placeholderText, int fontSize)
        {
            GameObject go = NewUI(name, parent);
            Image image = go.AddComponent<Image>();
            image.color = new Color(0.12f, 0.16f, 0.24f, 1f);

            InputField input = go.AddComponent<InputField>();

            GameObject textGo = NewUI("Text", go.transform);
            Text text = textGo.AddComponent<Text>();
            text.font = UIFont();
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            Stretch(textGo, 12f, 12f, 4f, 4f);

            GameObject placeholderGo = NewUI("Placeholder", go.transform);
            Text placeholder = placeholderGo.AddComponent<Text>();
            placeholder.font = UIFont();
            placeholder.fontSize = fontSize;
            placeholder.color = new Color(0.6f, 0.65f, 0.75f, 0.7f);
            placeholder.text = placeholderText;
            placeholder.alignment = TextAnchor.MiddleLeft;
            Stretch(placeholderGo, 12f, 12f, 4f, 4f);

            input.textComponent = text;
            input.placeholder = placeholder;
            input.targetGraphic = image;

            return input;
        }

        static Image MakePanel(Transform parent, string name, Color color)
        {
            GameObject go = NewUI(name, parent);
            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        static RectTransform MakeScrollView(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject root = NewUI(name, parent);
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = anchorMin;
            rootRect.anchorMax = anchorMax;
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = sizeDelta;

            Image background = root.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.35f);

            GameObject viewport = NewUI("Viewport", root.transform);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.pivot = new Vector2(0.5f, 0.5f);
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewport.AddComponent<RectMask2D>();

            GameObject content = NewUI("Content", viewport.transform);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = root.AddComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            return contentRect;
        }

        /// <summary>
        /// A scroll view whose content lays its children out in a grid rather than a column. Used for the
        /// deck view, where the children are cards. The cell size matches the card box, so the grid and the
        /// cards' own aspect ratio fitters agree instead of fighting.
        /// </summary>
        static ScrollRect MakeGridScrollView(Transform parent, string name, Vector2 anchoredPosition,
                                            Vector2 sizeDelta, Vector2 cellSize, Vector2 spacing)
        {
            GameObject root = NewUI(name, parent);
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = sizeDelta;

            Image background = root.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.30f);

            GameObject viewport = NewUI("Viewport", root.transform);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.pivot = new Vector2(0.5f, 0.5f);
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewport.AddComponent<RectMask2D>();

            GameObject content = NewUI("Content", viewport.transform);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            GridLayoutGroup grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.padding = new RectOffset(20, 20, 20, 20);
            grid.childAlignment = TextAnchor.UpperCenter;

            // The rows are laid out downwards, so only the height has to follow the content.
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scrollRect = root.AddComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 40f;

            return scrollRect;
        }

        /// <summary>
        /// A generated circle for the card's cost badge, so the corner of a card is round without needing
        /// art from the project. Written once, then reused on every later run.
        /// </summary>
        static Sprite EnsureCircleSprite()
        {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);
            if (existing != null) return existing;

            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var centre = new Vector2(size * 0.5f, size * 0.5f);
            float radius = size * 0.5f - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);

                    // Half a pixel of feather, so the rim is smooth rather than stepped.
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();

            File.WriteAllBytes(CircleSpritePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(CircleSpritePath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(CircleSpritePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(CircleSpritePath);
        }

        /// <summary>
        /// An overlay gets a canvas of its own, so whether it draws over the HUD does not depend on where
        /// it happens to sit among its siblings.
        /// </summary>
        static Canvas MakeOverlayCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            return canvas;
        }

        /// <summary>
        /// The full screen deck editor: a dark overlay, a close button in the top right, and every card
        /// the player owns in a scrolling grid, each showing how many copies are in the deck. It has its
        /// own canvas so it sits above whatever opened it, and the controller lives on that canvas, which
        /// stays active while only the panel starts hidden.
        /// </summary>

        static DeckViewPanel AddDeckView(GameObject cardPrefab)
        {
            Canvas canvas = MakeOverlayCanvas("DeckViewCanvas", 200);
            DeckViewPanel controller = canvas.gameObject.AddComponent<DeckViewPanel>();

            // Fully opaque: at 0.95 the screen behind bled through enough to read the character menu
            // behind the grid, which looks like a rendering fault rather than a backdrop.
            Image panel = MakePanel(canvas.transform, "DeckViewPanel", new Color(0.02f, 0.02f, 0.04f, 1f));

            Stretch(panel.gameObject, 0f, 0f, 0f, 0f);

            Text title = MakeText(panel.transform, "TitleText", "DECK", 46, TextAnchor.MiddleCenter, Color.white);
            SetRect(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(900f, 60f));

            Text summary = MakeText(panel.transform, "SummaryText", "", 24, TextAnchor.MiddleCenter, AccentColor);
            SetRect(summary.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(1600f, 36f));

            // 22 apart: a hovered grid card grows to 1.1x, which is 9 past each edge, so a 22 gutter
            // means the growth stays inside it and nothing needs to be lifted above its neighbours.
            ScrollRect scroll = MakeGridScrollView(panel.transform, "DeckScroll",
                new Vector2(0f, -34f), new Vector2(1660f, 830f), CardSize, new Vector2(22f, 22f));


            Button close = MakeButton(panel.transform, "CloseButton", "CLOSE (ESC)", 24, ButtonColor);
            SetRect(close.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(260f, 64f));

            Button btn3 = MakeButton(panel.transform, "DeckButton3", "3", 24, ButtonColor);
            SetRect(btn3.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-310f, -30f), new Vector2(60f, 64f));

            Button btn2 = MakeButton(panel.transform, "DeckButton2", "2", 24, ButtonColor);
            SetRect(btn2.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-380f, -30f), new Vector2(60f, 64f));

            Button btn1 = MakeButton(panel.transform, "DeckButton1", "1", 24, ButtonColor);
            SetRect(btn1.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-450f, -30f), new Vector2(60f, 64f));

            InputField search = MakeInputField(panel.transform, "SearchInput", "Search cards...", 22);
            SetRect(search.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(340f, 64f));

            Assign(controller, "panel", panel.gameObject);
            Assign(controller, "closeButton", close);
            Assign(controller, "gridContent", scroll.content);
            Assign(controller, "cardPrefab", cardPrefab != null ? cardPrefab.GetComponent<CardView>() : null);
            Assign(controller, "titleText", title);
            Assign(controller, "summaryText", summary);
            Assign(controller, "searchInput", search);
            Assign(controller, "deckButton1", btn1);
            Assign(controller, "deckButton2", btn2);
            Assign(controller, "deckButton3", btn3);

            panel.gameObject.SetActive(false);
            return controller;
        }

        static GameObject SavePrefab(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }

        static void Assign(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogWarning("[CardGameSceneBuilder] Serialized field '" + fieldName + "' not found on " + target.GetType().Name + ".");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AssignFloat(UnityEngine.Object target, string fieldName, float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogWarning("[CardGameSceneBuilder] Serialized field '" + fieldName + "' not found on " + target.GetType().Name + ".");
                return;
            }

            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The shared tooltip the cards use, read off the card prefab this scene was built with. The board
        /// squares show the same one, and the prefab is the only place this method can reach it from.
        /// </summary>
        static CardTooltip CardTooltipOf(GameObject cardPrefab)
        {
            if (cardPrefab == null) return null;

            var cardView = cardPrefab.GetComponent<CardView>();
            if (cardView == null) return null;

            var serialized = new SerializedObject(cardView);
            SerializedProperty property = serialized.FindProperty("tooltipPrefab");
            return property != null ? property.objectReferenceValue as CardTooltip : null;
        }

        /// <summary>
        /// Writes the board row into the manager. A slot is a component now rather than three loose
        /// references, so the array carries the component itself; its own fields were assigned where the
        /// square was built.
        /// </summary>
        static void AssignBoardSlots(CombatManager manager, BoardSlotView[] slots)
        {
            var serialized = new SerializedObject(manager);
            SerializedProperty property = serialized.FindProperty("boardSlots");
            if (property == null || !property.isArray)
            {
                Debug.LogWarning("[CardGameSceneBuilder] Serialized array field 'boardSlots' not found on CombatManager.");
                return;
            }

            property.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AssignArray(UnityEngine.Object target, string fieldName, UnityEngine.Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null || !property.isArray)
            {
                Debug.LogWarning("[CardGameSceneBuilder] Serialized array field '" + fieldName + "' not found on " + target.GetType().Name + ".");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif

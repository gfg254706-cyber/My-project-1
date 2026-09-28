using System;
using System.Collections.Generic;

namespace DungeonCards
{
    /// <summary>
    /// The player's belongings. Ten bag slots carry loose items; items worn on the body live in the
    /// equipment slots and take up no bag space. An item only changes the player's attributes while it
    /// is active, which means worn for gear and merely carried for a charm.
    /// </summary>
    public class Inventory
    {
        /// <summary>How many loose items the bag holds. Worn items are not counted against this.</summary>
        public const int MaxCarriedSlots = 10;

        /// <summary>The physical equipment slots, in the order the character menu shows them.</summary>
        public static readonly EquipmentSlot[] SlotOrder =
        {
            EquipmentSlot.Helmet,
            EquipmentSlot.Chest,
            EquipmentSlot.Trousers,
            EquipmentSlot.Boots,
            EquipmentSlot.Ring,
            EquipmentSlot.Ring
        };

        /// <summary>Raised when items are added, removed, equipped, unequipped or used.</summary>
        public event Action Changed;

        readonly List<ItemInstance> carried = new List<ItemInstance>();
        readonly ItemInstance[] slots = new ItemInstance[SlotOrder.Length];

        /// <summary>Loose items, in the order they were picked up.</summary>
        public IReadOnlyList<ItemInstance> Carried { get { return carried; } }

        public int CarriedCount { get { return carried.Count; } }

        public bool IsBagFull { get { return carried.Count >= MaxCarriedSlots; } }

        public int SlotCount { get { return slots.Length; } }

        public ItemInstance GetSlot(int index)
        {
            if (index < 0 || index >= slots.Length) return null;
            return slots[index];
        }

        public static string SlotName(int index)
        {
            if (index < 0 || index >= SlotOrder.Length) return "SLOT";

            // Two ring slots, so number them rather than showing RING twice.
            if (SlotOrder[index] == EquipmentSlot.Ring)
            {
                int which = 1;
                for (int i = 0; i < index; i++) if (SlotOrder[i] == EquipmentSlot.Ring) which++;
                return "RING " + which;
            }

            return SlotOrder[index].ToString().ToUpperInvariant();
        }

        /// <summary>
        /// Takes an item. Gear goes straight onto the body when its slot is free, and into the bag when
        /// it is not. Returns false when the item has nowhere to go.
        /// </summary>
        public bool Add(ItemData data)
        {
            if (data == null) return false;

            var instance = new ItemInstance(data);
            RollIfRelic(instance);

            return Add(instance);
        }

        /// <summary>
        /// Takes an item that already exists as an instance, keeping whatever roll it is carrying.
        ///
        /// This is the path the shop uses, so a relic bought off the rack arrives with the exact numbers
        /// the player was shown. It is also why the roll is created in Add rather than at the point of
        /// sale: every way an item reaches the player comes through here, so a relic granted by an event
        /// or handed out as starting gear is rolled too instead of arriving with nothing.
        /// </summary>
        public bool Add(ItemInstance instance)
        {
            if (instance == null || instance.data == null) return false;
            if (instance.Equipped) return false;

            // A relic has no slot to take and lands in the bag like any other charm.
            int freeSlot = FindFreeSlot(instance.data.slot);
            if (freeSlot >= 0)
            {
                instance.slotIndex = freeSlot;
                slots[freeSlot] = instance;
                NotifyChanged();
                return true;
            }

            if (IsBagFull) return false;

            instance.slotIndex = -1;
            carried.Add(instance);
            NotifyChanged();
            return true;
        }

        /// <summary>
        /// Rolls a relic's stats, once, on the instance. Doing it here means a relic definition can never
        /// be handed to the player without its roll, which is the failure that would otherwise show up as
        /// a relic that grants nothing at all.
        /// </summary>
        static void RollIfRelic(ItemInstance instance)
        {
            if (instance == null || instance.relic != null) return;

            var definition = instance.data as RelicDefinition;
            if (definition == null) return;

            instance.relic = RelicGenerator.Roll(definition, RelicGenerator.RandomSeed());
        }


        /// <summary>True when the item would fit somewhere, without changing anything.</summary>
        public bool CanAccept(ItemData data)
        {
            if (data == null) return false;
            if (FindFreeSlot(data.slot) >= 0) return true;
            return !IsBagFull;
        }

        /// <summary>Moves a loose item onto the body. Whatever was worn in that slot goes to the bag.</summary>
        public bool Equip(ItemInstance item)
        {
            if (item == null || item.data == null || item.Equipped) return false;
            if (item.data.IsCharm) return false;

            int index = FindFreeSlot(item.data.slot);
            if (index < 0) return false;

            carried.Remove(item);
            item.slotIndex = index;
            slots[index] = item;
            NotifyChanged();
            return true;
        }

        /// <summary>Moves a worn item back into the bag. Returns false when the bag is full.</summary>
        public bool Unequip(ItemInstance item)
        {
            if (item == null || !item.Equipped) return false;
            if (IsBagFull) return false;

            slots[item.slotIndex] = null;
            item.slotIndex = -1;
            carried.Add(item);
            NotifyChanged();
            return true;
        }

        /// <summary>Takes an item out of the player's possession, from the bag or from the body.</summary>
        public bool Remove(ItemInstance item)
        {
            if (item == null) return false;

            bool removed = carried.Remove(item);
            if (!removed && item.Equipped)
            {
                slots[item.slotIndex] = null;
                item.slotIndex = -1;
                removed = true;
            }

            if (removed) NotifyChanged();
            return removed;
        }

        /// <summary>Sums the bonuses of every item currently doing something: worn gear and carried charms.</summary>
        public void GetBonuses(out Dictionary<StatType, int> statBonuses, out int maxHealthBonus, out int energyBonus)
        {
            statBonuses = new Dictionary<StatType, int>
            {
                { StatType.Strength, 0 },
                { StatType.Agility, 0 },
                { StatType.Intellect, 0 },
                { StatType.Vitality, 0 },
                { StatType.Willpower, 0 }
            };
            maxHealthBonus = 0;
            energyBonus = 0;

            foreach (ItemInstance item in ActiveItems())
            {
                // Read through the instance rather than off the definition, so a relic contributes its
                // guaranteed stats and its roll together. For everything else the roll is null and this
                // is exactly the flat bonus it always was.
                foreach (StatType type in RelicInstance.StatOrder)
                {
                    statBonuses[type] += item.GetStat(type);
                }

                maxHealthBonus += item.data.maxHealthBonus;
                energyBonus += item.data.energyBonus;
            }

        }

        /// <summary>Every item currently changing the player's attributes.</summary>
        public IEnumerable<ItemInstance> ActiveItems()
        {
            foreach (ItemInstance item in slots)
            {
                if (item != null && item.data != null) yield return item;
            }

            foreach (ItemInstance item in carried)
            {
                if (item != null && item.data != null && item.data.IsCharm) yield return item;
            }
        }

        /// <summary>The shortest card cadence across active items, or 0 when nothing grants cards.</summary>
        public int CardGrantInterval()
        {
            int interval = 0;
            foreach (ItemInstance item in ActiveItems())
            {
                int cadence = item.data.grantsCardEveryEncounters;
                if (cadence <= 0) continue;
                if (interval == 0 || cadence < interval) interval = cadence;
            }
            return interval;
        }

        /// <summary>Names the item behind the card cadence, for the message telling the player what paid out.</summary>
        public string CardGrantSourceName()
        {
            foreach (ItemInstance item in ActiveItems())
            {
                if (item.data.grantsCardEveryEncounters > 0) return item.data.itemName;
            }
            return "charm";
        }

        public void Clear()
        {
            carried.Clear();
            for (int i = 0; i < slots.Length; i++) slots[i] = null;
            NotifyChanged();
        }

        int FindFreeSlot(EquipmentSlot slot)
        {
            if (slot == EquipmentSlot.Carried) return -1;

            for (int i = 0; i < SlotOrder.Length; i++)
            {
                if (SlotOrder[i] != slot) continue;
                if (slots[i] == null) return i;
            }
            return -1;
        }

        public void NotifyChanged()
        {
            if (Changed != null) Changed();
        }
    }
}

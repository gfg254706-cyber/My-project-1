using System.Text;

namespace DungeonCards
{
    /// <summary>
    /// The words a relic shows when it is being read.
    ///
    /// A relic icon carries no text of its own beyond the line under it, so everything the player needs in
    /// order to decide sits here. The order is the order the decision is made in: what the relic does, what
    /// it moves, and then the line written about it.
    /// </summary>
    public static class RelicText
    {
        /// <summary>The heading: the relic's name, and the tier it was rolled at when it has one.</summary>
        public static string Title(ItemInstance instance)
        {
            if (instance == null || instance.data == null) return "";

            string name = instance.data.itemName;
            string tier = instance.relic != null ? RelicInstance.TierLabel(instance.relic.tier) : "";

            return string.IsNullOrEmpty(tier) ? name : name + "   [" + tier + "]";
        }

        /// <summary>
        /// Everything the tooltip carries, in the order it is read.
        ///
        /// The effect is resolved as though the relic had already joined the player's carry, which is the
        /// number the decision is actually about. A relic on the shop's rack is not in the Inventory, so its
        /// own stat is not inside PlayerStats and its effect would otherwise quote the number it produces in
        /// nobody's hands.
        /// </summary>
        public static string Body(ItemInstance instance, PlayerStats stats)
        {
            if (instance == null || instance.data == null) return "";

            var sb = new StringBuilder();
            var definition = instance.data as RelicDefinition;

            if (definition != null && definition.effect != RelicEffect.None)
            {
                int own = instance.GetStat(definition.scaling.stat);
                string effect = definition.EffectSummary(stats, own);
                if (!string.IsNullOrEmpty(effect)) sb.AppendLine(effect);
            }

            // Every stat, zeros and negatives included: what a relic costs is as much of the decision as
            // what it pays, and a negative is part of the loot rather than a defect.
            //
            // The older relics carry no roll, so they list whatever their definition grants instead.
            if (instance.relic != null) sb.AppendLine(instance.StatLines());
            else sb.AppendLine(instance.BonusSummary());

            if (!string.IsNullOrEmpty(instance.data.description))
            {
                sb.AppendLine();
                sb.Append(instance.data.description);
            }

            return sb.ToString().TrimEnd('\n');
        }
    }
}

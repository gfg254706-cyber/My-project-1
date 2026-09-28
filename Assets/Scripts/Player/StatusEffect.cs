using System;
using System.Collections.Generic;
using System.Text;

namespace DungeonCards
{
    /// <summary>A timed combat modifier. A negative duration lasts until the end of the fight.</summary>
    [Serializable]
    public class StatusEffect
    {
        public StatusEffectType type;
        public int magnitude;
        public int turnsRemaining;

        public StatusEffect() { }

        public StatusEffect(StatusEffectType type, int magnitude, int turnsRemaining)
        {
            this.type = type;
            this.magnitude = magnitude;
            this.turnsRemaining = turnsRemaining;
        }

        public StatusEffect Clone()
        {
            return new StatusEffect(type, magnitude, turnsRemaining);
        }
    }

    public static class StatusEffects
    {
        /// <summary>Negative duration means the effect lasts until the end of the combat.</summary>
        public static void Add(List<StatusEffect> list, StatusEffectType type, int magnitude, int duration)
        {
            if (list == null || type == StatusEffectType.None || magnitude == 0) return;

            var existing = list.Find(s => s.type == type);
            if (existing != null)
            {
                existing.magnitude += magnitude;
                if (duration > existing.turnsRemaining) existing.turnsRemaining = duration;
            }
            else
            {
                list.Add(new StatusEffect(type, magnitude, duration));
            }
        }

        public static int Magnitude(List<StatusEffect> list, StatusEffectType type)
        {
            if (list == null) return 0;
            var effect = list.Find(s => s.type == type);
            return effect != null ? effect.magnitude : 0;
        }

        /// <summary>Drops one turn of duration and removes anything that ran out.</summary>
        public static void Tick(List<StatusEffect> list)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].turnsRemaining > 0) list[i].turnsRemaining--;
                if (list[i].turnsRemaining == 0) list.RemoveAt(i);
            }
        }

        public static void Clear(List<StatusEffect> list)
        {
            if (list != null) list.Clear();
        }

        public static string Describe(List<StatusEffect> list)
        {
            if (list == null || list.Count == 0) return "No active effects";

            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(list[i].type).Append(' ').Append(list[i].magnitude);
                if (list[i].turnsRemaining > 0) sb.Append(" (").Append(list[i].turnsRemaining).Append(')');
            }
            return sb.ToString();
        }
    }
}

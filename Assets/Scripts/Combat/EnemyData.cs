using System.Collections.Generic;
using UnityEngine;

namespace DungeonCards
{
    /// <summary>An opponent: its health pool, its repeating attack pattern and the experience it is worth.</summary>
    [CreateAssetMenu(fileName = "NewEnemy", menuName = "Dungeon Cards/Enemy")]
    public class EnemyData : ScriptableObject
    {
        public string enemyName = "Goblin";
        [TextArea] public string description = "";

        public int maxHealth = 20;

        [Tooltip("Damage dealt on each enemy turn. The pattern loops once its last entry is used.")]
        public List<int> attackPattern = new List<int> { 6 };

        [Tooltip("Shielding the enemy starts the fight with. It never wears off, so it has to be broken.")]
        public int startingBlock = 0;

        [Tooltip("Shielding gained on each enemy turn, read on the same beat as the attack pattern. " +
                 "An entry of 0 is a turn spent attacking. This shielding is permanent: it stays until it " +
                 "is broken, which is what makes an enemy that turtles worth a dedicated answer.")]
        public List<int> blockPattern = new List<int> { 0 };

        [Tooltip("Experience granted to the player when this enemy is defeated.")]
        public int experienceReward = 25;

        [Tooltip("Gold granted to the player when this enemy is defeated. A fight is the only reliable income.")]
        public int goldReward = 0;

        public int GetAttackForTurn(int turnIndex)
        {
            if (attackPattern == null || attackPattern.Count == 0) return 0;
            int index = turnIndex % attackPattern.Count;
            if (index < 0) index += attackPattern.Count;
            return Mathf.Max(0, attackPattern[index]);
        }

        /// <summary>
        /// Shielding gained on the given enemy turn, on the same index as the attack so the two patterns
        /// describe one turn between them: block on the beat the attack is small, attack on the rest.
        /// </summary>
        public int GetBlockForTurn(int turnIndex)
        {
            if (blockPattern == null || blockPattern.Count == 0) return 0;
            int index = turnIndex % blockPattern.Count;
            if (index < 0) index += blockPattern.Count;
            return Mathf.Max(0, blockPattern[index]);
        }
    }
}

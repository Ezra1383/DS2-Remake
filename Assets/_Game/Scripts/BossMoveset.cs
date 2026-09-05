using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Her whole repertoire, as data. She has every move from the first attempt to the last -
    /// the difficulty curve is the player's growing toolkit, not hers.
    ///
    /// Selection is weighted random inside a range band, with a cooldown so she cannot spam her
    /// best answer. That is the entire AI: no behaviour tree, no utility scoring. Tuning happens
    /// here, in the inspector, while the fight is running.
    /// </summary>
    [CreateAssetMenu(menuName = "DS2/Boss Moveset", fileName = "BossMoveset")]
    public class BossMoveset : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public MoveDefinition move;

            [Tooltip("Relative likelihood against every other valid move at this range. " +
                     "0 removes it from random selection - use that for moves she should only " +
                     "reach by chaining into them.")]
            public float weight;

            [Tooltip("Range band this move is chosen in, in metres.")]
            public float minRange, maxRange;

            [Tooltip("Seconds before she may pick it again. This is what stops her leaning on " +
                     "one answer and makes her feel like she is reading you.")]
            public float cooldown;
        }

        public Entry[] entries = System.Array.Empty<Entry>();

        /// <summary>
        /// Weighted pick among moves valid at this distance and off cooldown. Returns null when
        /// nothing is available, which the caller should treat as "close the distance instead".
        /// </summary>
        public MoveDefinition Select(float distance, System.Func<MoveDefinition, bool> isReady)
        {
            float total = 0f;
            foreach (Entry e in entries)
            {
                if (!IsValid(e, distance, isReady)) continue;
                total += e.weight;
            }

            if (total <= 0f) return null;

            float roll = Random.value * total;
            foreach (Entry e in entries)
            {
                if (!IsValid(e, distance, isReady)) continue;
                roll -= e.weight;
                if (roll <= 0f) return e.move;
            }
            return null;
        }

        /// <summary>Direct lookup, for moves reached by reaction rather than by weighted roll.</summary>
        public MoveDefinition Find(MoveId id)
        {
            foreach (Entry e in entries)
                if (e.move != null && e.move.moveId == id) return e.move;
            return null;
        }

        static bool IsValid(Entry e, float distance, System.Func<MoveDefinition, bool> isReady)
        {
            return e.move != null
                && e.weight > 0f
                && distance >= e.minRange
                && distance <= e.maxRange
                && isReady(e.move);
        }
    }
}

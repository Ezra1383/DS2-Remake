using System.Collections.Generic;
using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Death is the progression mechanic. The move that lands the killing blow is the move you
    /// learn - it is a mirror match, so every one of hers has an equivalent of yours.
    ///
    /// THE ONE RULE THAT MATTERS: a player must never walk away from a death with nothing. Every
    /// death grants exactly one unlock until the pool is empty. If that ever fails, the premise
    /// fails with it - the fight stops being a teacher and becomes a wall.
    ///
    /// Persisted in PlayerPrefs. It is twelve booleans; a save system would be ceremony.
    /// </summary>
    public class ProgressionManager : MonoBehaviour
    {
        public static ProgressionManager Instance { get; private set; }

        const string PrefsKey = "DS2.Unlocked";

        /// <summary>
        /// Slash 1 to threaten, Evade to survive. The design doc is firm that evasion cannot be
        /// something you earn: with no stamina and no block it is the only defensive verb in the
        /// game, and a player without it has no interaction beyond swinging - the first fight
        /// becomes a cutscene rather than a lesson.
        /// </summary>
        /// <remarks>
        /// Parry belongs here and NOT on the ladder. It is a core verb rather than a reward, and
        /// putting it on the ladder would make the arc eleven deaths instead of the ten the game
        /// is named after. It is also the reason this array is the only place that grants it -
        /// leaving it out of both lists made IsUnlocked return false forever, so the button did
        /// nothing at all.
        /// </remarks>
        static readonly MoveId[] StartingKit = { MoveId.Slash1, MoveId.Evade, MoveId.Parry };

        /// <summary>
        /// The order deaths resolve into when the killer's equivalent is already known. Straight
        /// from the design doc: each rung either widens the moveset or re-frames what you already
        /// have, and the Draw sits in the middle as the fight's turn.
        /// </summary>
        static readonly MoveId[] Ladder =
        {
            MoveId.Slash2,       // turns one swing into a decision about continuing
            MoveId.QuickShiftB,  // making space on purpose
            MoveId.Slash3,       // the finisher - 25 posture, breaking her becomes possible
            MoveId.QuickShiftF,  // punishing her recovery from across the arena
            MoveId.Draw,         // access to Special stance. the midpoint turn
            MoveId.Skill1,       // first heavy
            MoveId.QuickShiftL,  // circling becomes an option
            MoveId.Skill2,       // her signature lunge, now yours
            MoveId.QuickShiftR,  // completes the four-way dash
            MoveId.Skill3,       // the last thing she has. the mirror is even
        };

        [Header("Debug")]
        [Tooltip("Ignores the save and starts from the two-move kit every play. On while tuning " +
                 "the arc - Week 4 balance testing is meaningless from a half-finished save.")]
        [SerializeField] bool wipeOnPlay;

        [Tooltip("Grants everything at startup, for testing the late fight without ten deaths.")]
        [SerializeField] bool unlockEverything;

        [SerializeField] bool logUnlocks = true;

        readonly HashSet<MoveId> unlocked = new();

        /// <summary>Fired when a death grants a move. The death screen listens for the card.</summary>
        public event System.Action<MoveId> Granted;

        /// <summary>Fired on a death that granted nothing because the ladder is exhausted.</summary>
        public event System.Action LadderComplete;

        public int UnlockedCount => unlocked.Count;
        public int TotalCount => StartingKit.Length + Ladder.Length;
        public bool IsComplete => unlocked.Count >= TotalCount;

        /// <summary>The ladder in order, for a HUD's learned-moves list.</summary>
        public static IReadOnlyList<MoveId> LadderOrder => Ladder;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // ONLY the component. This used to destroy the whole GameObject, which was
                // catastrophic the moment a second copy ended up on a scene object that owned
                // something else: a duplicate on the arena root took the floor and all four walls
                // with it, the actors fell forever, and because Awake order between two instances
                // of one component is undefined it only happened about half the time.
                Debug.LogWarning("[Progression] A second ProgressionManager on '" + name +
                                 "' was removed. Only one should exist.", this);
                Destroy(this);
                return;
            }

            Instance = this;

            // DontDestroyOnLoad promotes the object to a root and keeps everything under it.
            // Refusing to do that to an object with children is what stops a stray copy dragging
            // half the scene into the DontDestroyOnLoad bucket.
            if (transform.parent == null && transform.childCount == 0)
            {
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Debug.LogWarning("[Progression] '" + name + "' has a parent or children, so it " +
                                 "will NOT survive a scene load. Put ProgressionManager on its " +
                                 "own empty GameObject.", this);
            }

            Load();
        }

        // ------------------------------------------------------------------ queries

        public bool IsUnlocked(MoveId id)
        {
            // Sheathe is not on the ladder: it is the other half of the Draw. Learning to take
            // the stance without being able to leave it would just be a trap.
            if (id == MoveId.Sheathe) return unlocked.Contains(MoveId.Draw);

            return id == MoveId.None || unlocked.Contains(id);
        }

        public bool IsUnlocked(MoveDefinition move) => move == null || IsUnlocked(move.moveId);

        // ------------------------------------------------------------------ the death hook

        /// <summary>
        /// Call on player death with the move that killed them. Returns what was granted, or
        /// MoveId.None once the ladder is exhausted.
        /// </summary>
        public MoveId GrantForDeath(MoveDefinition killer)
        {
            MoveId granted = Resolve(killer != null ? killer.moveId : MoveId.None);

            if (granted == MoveId.None)
            {
                if (logUnlocks) Debug.Log("[Progression] Ladder complete - nothing left to grant.", this);
                LadderComplete?.Invoke();
                return MoveId.None;
            }

            unlocked.Add(granted);
            Save();

            if (logUnlocks)
                Debug.Log($"[Progression] Killed by {killer?.moveId.ToString() ?? "unknown"} " +
                          $"-> learned {granted}  ({unlocked.Count}/{TotalCount})", this);

            Granted?.Invoke(granted);
            return granted;
        }

        /// <summary>
        /// The resolution rules, in the design doc's order. Kept separate from GrantForDeath so
        /// it can be reasoned about - and tested - without mutating anything.
        /// </summary>
        public MoveId Resolve(MoveId killer)
        {
            // A Special you cannot take the stance for is not a lesson, it is a locked door.
            // Narratively you learned to draw the blade instead.
            bool killerIsSpecial = killer == MoveId.Skill1 || killer == MoveId.Skill2 || killer == MoveId.Skill3;
            if (killerIsSpecial && !unlocked.Contains(MoveId.Draw))
                return MoveId.Draw;

            // 1:1 mirror: her move IS your move, so the id maps to itself.
            if (killer != MoveId.None && !unlocked.Contains(killer) && OnLadder(killer))
                return killer;

            // Already known, or not something that can be taught (a Quick Shift she used to
            // reposition, say). Walk the ladder and grant the first thing still missing.
            foreach (MoveId rung in Ladder)
                if (!unlocked.Contains(rung))
                    return rung;

            return MoveId.None;
        }

        static bool OnLadder(MoveId id)
        {
            foreach (MoveId rung in Ladder)
                if (rung == id) return true;
            return false;
        }

        // ------------------------------------------------------------------ persistence

        void Load()
        {
            unlocked.Clear();
            foreach (MoveId id in StartingKit) unlocked.Add(id);

            if (unlockEverything)
            {
                foreach (MoveId id in Ladder) unlocked.Add(id);
                if (logUnlocks) Debug.Log("[Progression] unlockEverything - full moveset.", this);
                return;
            }

            if (wipeOnPlay)
            {
                PlayerPrefs.DeleteKey(PrefsKey);
                if (logUnlocks) Debug.Log("[Progression] wipeOnPlay - starting from Slash1 + Evade.", this);
                return;
            }

            string saved = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(saved)) return;

            foreach (string part in saved.Split(','))
                if (System.Enum.TryParse(part, out MoveId id))
                    unlocked.Add(id);

            if (logUnlocks)
                Debug.Log($"[Progression] loaded {unlocked.Count}/{TotalCount} unlocked.", this);
        }

        void Save()
        {
            if (unlockEverything || wipeOnPlay) return;   // never write a debug state over a real save

            var parts = new List<string>(unlocked.Count);
            foreach (MoveId id in unlocked) parts.Add(id.ToString());

            PlayerPrefs.SetString(PrefsKey, string.Join(",", parts));
            PlayerPrefs.Save();
        }

        /// <summary>Back to the two-move kit. Used by the Week 4 balance passes.</summary>
        [ContextMenu("Wipe Save")]
        public void Wipe()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
            Load();
            if (logUnlocks) Debug.Log("[Progression] save wiped.", this);
        }
    }
}

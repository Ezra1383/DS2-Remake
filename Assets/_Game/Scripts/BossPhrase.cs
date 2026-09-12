using UnityEngine;

namespace DS2
{
    /// <summary>
    /// One authored attack string - the unit the player actually learns.
    ///
    /// THIS IS THE POINT OF THE REWRITE. The old boss re-rolled a move from a weighted table
    /// every time she became free, which produces a different sequence every exchange and so
    /// produces no sequence at all: nothing to recognise, nothing to punish, nothing to learn.
    /// Randomness moves up one level. She rolls for a PHRASE, then performs it as written.
    /// Which phrase you get is unpredictable; what a phrase does once it starts is not.
    ///
    /// This is FromSoftware's structure with the scripting taken out: their bosses push an
    /// attack and its follow-ups onto a goal stack as one unit, rather than re-deciding at every
    /// link. A fixed opener that sometimes extends is readable. A coin flip at every link is not.
    /// </summary>
    [CreateAssetMenu(menuName = "DS2/Boss Phrase", fileName = "Phrase_")]
    public class BossPhrase : ScriptableObject
    {
        [System.Serializable]
        public struct Step
        {
            public MoveDefinition move;

            [Tooltip("Seconds of neutral after this step before the next one starts. 0 links as " +
                     "fast as the move allows. This is the rhythm of the phrase - varying it " +
                     "between phrases is what stops the player running on autopilot.")]
            public float gapAfter;

            [Tooltip("Enter the next step at this move's cancelWindow instead of waiting for it " +
                     "to finish. The player has always chained this way; the boss waiting for the " +
                     "full clip is most of why her combos felt slack next to theirs.")]
            public bool linkInCancel;

            [Tooltip("Overrides the move's own moveEnd for this step only. Set it to 1 on a " +
                     "finisher to play the whole draw-cut-sheathe cycle: a long, unmistakable " +
                     "recovery that says 'punish me now'. Leave at 0 to use the asset's trim.")]
            [Range(0f, 1f)] public float endOverride;

            [Tooltip("Odds of continuing past this step. This is the branch point - the same " +
                     "opener that sometimes extends and sometimes does not. 0 is treated as 1, " +
                     "so a step added by hand and left at its default continues rather than " +
                     "silently truncating the phrase. To stop, end the step list.")]
            [Range(0f, 1f)] public float continueChance;

            [Tooltip("Abandon the rest of the phrase if the player is further than this when the " +
                     "step is due. 0 means never abandon. Stops her swinging at open air after " +
                     "the player has already left.")]
            public float breakRange;

            public float ContinueChance => continueChance <= 0f ? 1f : continueChance;
        }

        [Tooltip("Shown in the debug HUD and the decision log. Name it after what it teaches, " +
                 "not after the moves in it - 'Feint' rather than 'Slash1 Slash1'.")]
        public string phraseName = "Phrase";

        public Step[] steps = System.Array.Empty<Step>();

        [Header("When she may choose it")]
        [Tooltip("Relative likelihood against every other valid phrase at this range. 0 removes " +
                 "it from the weighted roll - use that for phrases only reachable by reaction.")]
        public float weight = 1f;

        public float minRange;
        public float maxRange = 3f;

        [Tooltip("Seconds before she may choose it again. With a small repertoire this is what " +
                 "stops one phrase becoming the whole fight.")]
        public float cooldown;

        [Tooltip("Seconds of pressure this phrase spends from her budget, on top of what it " +
                 "actually takes. Raise it on a phrase that should be followed by an opening " +
                 "even though it is short.")]
        public float extraPressureCost;

        public bool IsValidAt(float distance) =>
            steps.Length > 0 && distance >= minRange && distance <= maxRange;

        /// <summary>Rough length, for the pacing layer's budget arithmetic.</summary>
        public float EstimatedDuration
        {
            get
            {
                float total = extraPressureCost;
                foreach (Step s in steps)
                {
                    if (s.move == null) continue;
                    float end = s.endOverride > 0f ? s.endOverride : s.move.moveEnd;
                    total += s.move.Duration * (s.linkInCancel ? s.move.cancelWindow : end);
                    total += s.gapAfter;
                }
                return total;
            }
        }
    }
}

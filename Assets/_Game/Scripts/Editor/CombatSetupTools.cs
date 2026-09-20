using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DS2.EditorTools
{
    /// <summary>
    /// Builds Step 1.1: the move assets and the gameplay animator controller.
    ///
    /// All timing numbers come from Docs/clip-report.csv (measured 4 Sep 2026) and the retarget
    /// in Docs/build-plan.md. Hitbox and i-frame windows are the proportions from
    /// combat-design.html; because they are normalized they survive the 2x retarget unchanged.
    ///
    /// Safe to re-run: existing move assets are updated in place, not duplicated.
    /// </summary>
    static class CombatSetupTools
    {
        static string AnimFolder => VendorPaths.AnimFolder;
        const string KiFolder = "Assets/Kevin Iglesias/Human Animations/Animations/Female";

        /// <summary>
        /// Eight-way locomotion, as offsets on the MoveX/MoveY plane. MoveX is right, MoveY is
        /// forward, both in the character's local space - so these are literally which way she
        /// is travelling relative to where she is looking.
        /// </summary>
        static readonly (string suffix, Vector2 dir)[] Directions =
        {
            ("Forward",       new Vector2( 0f,     1f)),
            ("ForwardRight",  new Vector2( 0.707f, 0.707f)),
            ("Right",         new Vector2( 1f,     0f)),
            ("BackwardRight", new Vector2( 0.707f,-0.707f)),
            ("Backward",      new Vector2( 0f,    -1f)),
            ("BackwardLeft",  new Vector2(-0.707f,-0.707f)),
            ("Left",          new Vector2(-1f,     0f)),
            ("ForwardLeft",   new Vector2(-0.707f, 0.707f)),
        };
        const string MovesFolder = "Assets/_Game/Moves";
        const string BossMovesFolder = "Assets/_Game/Moves/Boss";
        const string BossPhraseFolder = "Assets/_Game/Moves/Boss/Phrases";
        const string ControllerPath = "Assets/_Game/Animation/KG_Combat.controller";

        /// <summary>
        /// How much faster than the source clips the game runs. THE dial for combat pace.
        ///
        /// The design doc's frame data implied 2.3-3.9x, which was measured on 4 Sep and found to
        /// be fantasy - the vendor clips are authored at a deliberate pace with real anticipation,
        /// and speeding them up destroys the wind-up that telegraphs depend on. 2.0x was tried and
        /// read as absurdly fast. 1.4x keeps the swings legible while still tightening them.
        ///
        /// Change this one number and re-run both menu items to retune the whole game.
        /// Raise it for a faster fight, lower it toward 1.0 for the animators' original pacing.
        /// </summary>
        const float Tempo = 1.4f;

        /// <summary>
        /// Eight-way locomotion instead of the vendor's forward-only set.
        ///
        /// OFF as of 5 Sep. The Kevin Iglesias clips are technically ideal - Humanoid, in-place
        /// and [RM] variants, all eight directions - but they are realistic mocap retargeted onto
        /// a stylised anime rig, and the result looks ridiculous next to the vendor animations.
        /// A style mismatch is not something blend weights can fix.
        ///
        /// The 2D tree, MoveX/MoveY and the strafing rotation are all written and correct. Turn
        /// this on the moment there is an eight-way set that matches the vendor's style - most
        /// likely Walk_Back / Strafe_L / Strafe_R adapted from the vendor's own Walk, which keeps
        /// the proportions and the exaggeration. Set PlayerLocomotion.hasDirectionalClips to match.
        /// </summary>
        const bool DirectionalLocomotion = false;

        struct Spec
        {
            public MoveId id;
            public string state, clip;
            public float measured;
            public int damage, posture;
            public float hbOpen, hbClose, ifStart, ifEnd, cancel;
            public float pStart, pEnd;
            public MoveId chainTo;

            /// <summary>Odds an AI continues into chainTo. 0 means "use 1".</summary>
            public float chain;

            public float ChainChance => chain > 0f ? chain : 1f;

            /// <summary>Per-move override. Left at 0 the global Tempo is used.</summary>
            public float tempo;

            /// <summary>Normalized end point. Left at 0 the whole clip plays.</summary>
            public float end;

            public float End => end > 0f ? end : 1f;

            /// <summary>
            /// The boss used to run every move at moveEnd = 1, on the theory that the full
            /// draw-cut-sheathe cycle telegraphs commitment. Measured, that made a three-slash
            /// chain 5.18 s long containing 0.72 s of live hitbox - the fight was 86% dead air.
            /// She now gets the player's trims plus a beat, and the full sheathe is spent only
            /// where a phrase asks for it, which is what turns it back into a signal.
            /// </summary>
            public float BossEnd => Mathf.Clamp01(End + 0.06f);

            /// <summary>Normalized point where facing locks. Left at 0, a sensible default is used.</summary>
            public float trackUntil;

            /// <summary>Degrees per second the BOSS may turn during that window.</summary>
            public float bossTrack;

            /// <summary>
            /// Degrees per second the PLAYER may turn during that window.
            ///
            /// This was 0 for every move until 16 Sep, on the reasoning that a swing should point
            /// where the stick pointed and steering it for the player would feel like the game
            /// taking over. The first playtest killed that: with no strafe clips she turns to
            /// face her direction of TRAVEL, and facing freezes the instant a move starts, so an
            /// attack thrown while moving goes wherever you were walking rather than at the
            /// target. The boss meanwhile corrects at 240 deg/s. Her hits landed and yours did
            /// not, and the cause was in the data rather than in the animation set.
            ///
            /// The original concern survives intact, because tracking turns toward FaceTarget and
            /// FaceTarget is null unless you are locked on: THIS VALUE ONLY APPLIES WHILE LOCKED
            /// ON. Free-aim still points exactly where the stick pointed.
            ///
            /// Committed moves get less than the light chain deliberately - a Special that
            /// swings 180 degrees to find you stops being a commitment.
            /// </summary>
            public float playerTrack;

            public float TrackUntil => trackUntil > 0f ? trackUntil : 0.25f;

            /// <summary>Socket at move start, and at move end. Empty leaves the clip in charge.</summary>
            public string socket, endSocket;

            public float Multiplier => tempo > 0f ? tempo : Tempo;
            public float Duration => measured / Multiplier;
        }

        // speedMultiplier is derived from measured/duration, never hand-typed.
        static readonly Spec[] Specs =
        {
            new Spec { id = MoveId.Slash1, trackUntil = 0.26f, bossTrack = 240f, playerTrack = 420f, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", end = 0.52f, state = "Attack1", clip = "Attack1", measured = 2.033f,
                       damage = 8,  posture = 12, hbOpen = 0.300f, hbClose = 0.467f, cancel = 0.467f, chainTo = MoveId.Slash2, chain = 1.0f },
            new Spec { id = MoveId.Slash2, trackUntil = 0.22f, bossTrack = 240f, playerTrack = 420f, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", end = 0.50f, state = "Attack2", clip = "Attack2", measured = 1.833f,
                       // RETIMED 20 Sep, and the whole 19 Sep story was wrong.
                       //
                       // The 19 Sep note argued Attack2's contact was EARLY (0.10-0.28) because
                       // the boss "lands her Slash 2" at 0.095-0.275 while the player's did not.
                       // She does not. Her Slash 2 never landed either - it was assumed, never
                       // watched - so the window chased a phantom, and widening it toward that
                       // phantom (0.095-0.320, then 0.080-0.478) took it from 2 swings in 5 to
                       // none at all. A wider window landing LESS was the tell: the bug was never
                       // timing. It was Hitbox.TryHit spending the swing on a contact that dealt
                       // no damage, fixed there 20 Sep.
                       //
                       // 0.300-0.478 was tried and ALSO never landed, which is the measurement that
                       // matters: Slash 1 opens at 0.300 and lands every time, Slash 2 opened at
                       // the same instant and landed never. Same actor, same fight, same target.
                       // So the window is not the variable and never was - four windows have now
                       // failed (0.258-0.419, 0.095-0.275, 0.080-0.478, 0.300-0.478) and the only
                       // one that ever produced hits managed 2 in 5.
                       //
                       // MEASURED 20 Sep, finally. A full-width 0.0-1.0 window plus the
                       // closest-approach instrument settled it in one run:
                       //
                       //   KatanaGirl Slash2: CONNECTED. closest approach 0.372 m at t=0.162
                       //   KatanaGirl Slash2: CONNECTED. closest approach 0.351 m at t=0.166
                       //   Boss       Slash2: CONNECTED. closest approach 0.292 m at t=0.012
                       //
                       // ATTACK2'S CONTACT IS EARLY - the blade's nearest pass is t~0.16, and at
                       // chain range it is already on the target by t~0.01. So every window from
                       // 0.258 onward was simply behind the swing, and the 19 Sep note's "contact
                       // is EARLY, around 0.10-0.28" was RIGHT while its reasoning was wrong. The
                       // 20 Sep retime to 0.300, matching Slash 1 by analogy, moved it further
                       // away and took it from rare to never.
                       //
                       // OPENS AT ZERO, AND THAT IS THE ONLY THING THAT WORKS.
                       //
                       // 0.060 was tried - skipping only the first ~79 ms to avoid registering on
                       // Slash 1's carry-over - and it took the player's Slash 2 straight back to
                       // never landing. The measurement says why:
                       //
                       //   KatanaGirl Slash2: NO hit. closest approach 0.367 m at t=0.163
                       //   KatanaGirl Slash2: NO hit. closest approach 0.371 m at t=0.164
                       //   KatanaGirl Slash2: NO hit. closest approach 0.376 m at t=0.161
                       //
                       // Identical every swing, to within 9 mm. ATTACK2'S OWN ARC NEVER REACHES
                       // THE TARGET at chain range - the blade's nearest pass leaves 0.37 m of
                       // air, and the only contact this move ever makes is in its first frames,
                       // while the sword is still where Slash 1's follow-through put it.
                       //
                       // Note that Slash 1 CONNECTS at a closest approach of 0.382 m - further
                       // away than Slash 2's 0.367 m miss. Distance is not what separates them;
                       // orientation is. The blade is 1.12 m long and only 0.06 wide, so it
                       // reaches when it points at her and passes by when it does not. No window
                       // can fix a swing that is aimed elsewhere.
                       //
                       // So the chain is what lands this move, and that is accepted rather than
                       // fought. Widening the damage volume enough to close 0.37 m would mean a
                       // blade box wider than she is.
                       damage = 10, posture = 15, hbOpen = 0.0f, hbClose = 0.400f, cancel = 0.484f, chainTo = MoveId.Slash3, chain = 0.5f },
            new Spec { id = MoveId.Slash3, trackUntil = 0.24f, bossTrack = 180f, playerTrack = 360f, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", end = 0.55f, state = "Attack3", clip = "Attack3", measured = 2.267f,
                       damage = 14, posture = 25, hbOpen = 0.289f, hbClose = 0.444f, cancel = 1f },

            new Spec { id = MoveId.Evade, trackUntil = 0.10f, bossTrack = 0f, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", state = "Evade", clip = "Evade", measured = 1.467f,
                       ifStart = 0.083f, ifEnd = 0.633f, cancel = 0.700f },

            // No parry animation exists in the pack, so this is Evade at nearly double speed,
            // cut short. Two move assets pointing at one animator state with different data is
            // exactly what MoveDefinition is for - the boss already does it.
            //
            // NOTE there are no i-frames here on purpose: outside the parry window this move has
            // no defence at all, which is the whole cost of reaching for it instead of dodging.
            //
            // WINDOW MATH (Duration = 1.467 / 2.6 = 0.564 s):
            //   pStart 0.02 -> opens 0.011 s after the press, so a press right on the blade still
            //                  counts. Do not raise this; a parry that ignores a perfectly timed
            //                  input feels broken rather than strict.
            //   pEnd   0.38 -> closes at 0.214 s, giving a 0.203 s window, about 12 frames at 60.
            //                  Was 0.146 s / 9 frames, which was tighter than Sekiro's deflect
            //                  while paying out a full posture break.
            //   end    0.55 -> the move runs 0.310 s, leaving ~0.10 s of defenceless recovery
            //                  after the window shuts. Widening the window without moving this
            //                  too would have turned it into a quarter-second of free immunity.
            new Spec { id = MoveId.Parry, trackUntil = 0.30f, bossTrack = 0f,
                       socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade",
                       state = "Evade", clip = "Evade", measured = 1.467f, tempo = 2.6f, end = 0.55f,
                       pStart = 0.02f, pEnd = 0.38f, cancel = 1f },

            new Spec { id = MoveId.QuickShiftF, trackUntil = 0.12f, bossTrack = 360f, playerTrack = 300f, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", state = "Quickshift_F", clip = "Quickshift_F", measured = 1f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },
            new Spec { id = MoveId.QuickShiftB, trackUntil = 0.10f, bossTrack = 0f, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", state = "Quickshift_B", clip = "Quickshift_B", measured = 1f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },
            new Spec { id = MoveId.QuickShiftL, trackUntil = 0.10f, bossTrack = 0f, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", state = "Quickshift_L", clip = "Quickshift_L", measured = 1f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },
            new Spec { id = MoveId.QuickShiftR, trackUntil = 0.10f, bossTrack = 0f, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", state = "Quickshift_R", clip = "Quickshift_R", measured = 1f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },

            // Vulnerable throughout - no hitbox, no i-frames. That is the cost of the stance.
            new Spec { id = MoveId.Draw, trackUntil = 0.10f, bossTrack = 0f,    state = "Take", clip = "Take", measured = 1.733f, cancel = 1f },
            new Spec { id = MoveId.Sheathe, trackUntil = 0.10f, bossTrack = 0f, state = "Put",  clip = "Put",  measured = 1.667f, cancel = 1f },

            new Spec { id = MoveId.Skill1, trackUntil = 0.26f, bossTrack = 150f, playerTrack = 180f, end = 0.93f, state = "Sp_Skill1", clip = "Sp_Skill1", measured = 3.200f,
                       damage = 18, posture = 30, hbOpen = 0.309f, hbClose = 0.433f, cancel = 1f },
            // Vendor naming inconsistency: state Sp_Skill2 plays clip K_Sp_Skill_2.
            new Spec { id = MoveId.Skill2, trackUntil = 0.28f, bossTrack = 200f, playerTrack = 200f, end = 0.93f, state = "Sp_Skill2", clip = "K_Sp_Skill_2", measured = 3.867f,
                       damage = 22, posture = 34, hbOpen = 0.328f, hbClose = 0.483f, cancel = 1f },
            new Spec { id = MoveId.Skill3, trackUntil = 0.28f, bossTrack = 120f, playerTrack = 120f, end = 0.85f, state = "Sp_Skill3", clip = "Sp_Skill3", measured = 4.500f,
                       // WINDOW = THE ENTIRE TIME THE SWORD IS IN HER HAND, read off the clip's own
                       // baked SwitchSocket events rather than guessed:
                       //
                       //   0.662s  To_Hand_R_Socket-Blade   -> drawn      = 0.147 normalized
                       //   3.112s  To_add_weapon_r-Blade    -> stowed     = 0.692 normalized
                       //
                       // This is an IAI move: it opens with the blade at To_Katana_Close-Blade,
                       // i.e. sheathed. The HitBox is parented to the katana, so for the first
                       // 15% of this move the damage volume is sitting on her hip and could not
                       // hit anything however wide the window was. Outside 0.147-0.692 there is
                       // nothing to hit with, so there is no point looking there.
                       //
                       // Safe to open at the draw here in a way it is not on Slash 2: a Special is
                       // never chained into, so no previous swing's follow-through is resting on
                       // the target when the window opens.
                       //
                       // IF IT STILL MISSES, IT IS RANGE, NOT TIMING. RootXZNet is 4.44 m - this
                       // move carries her further than the arena's whole melee band, so thrown
                       // from close it puts her behind the target before the blade arrives. The
                       // 20 Sep measurement is consistent with exactly that: closest approach
                       // 0.439 m at t=0.435, then 2.167 m by t=0.482 - she is already leaving.
                       damage = 28, posture = 40, hbOpen = 0.147f, hbClose = 0.692f, cancel = 1f },
        };

        // Reaction states. Note the swap: clip Hit1 is K_Hit_R.fbx, clip Hit2 is K_Hit_L.fbx.
        static readonly (string state, string clip)[] Reactions =
        {
            ("Hit_R", "Hit1"), ("Hit_L", "Hit2"), ("Stun", "Stun"), ("Die", "Die"),
        };

        [MenuItem("Tools/DS2/Build Move Assets")]
        static void BuildMoves() => BuildMoveSet(MovesFolder, "Move_", forBoss: false);

        /// <summary>
        /// The boss runs the same animator states off her own assets, with moveEnd = 1 and no
        /// socket override - so she plays the full draw-cut-sheathe cycle. That sheathe is her
        /// telegraph: a readable "I am committed, punish me now" window, which the player's
        /// trimmed version deliberately throws away.
        /// </summary>
        [MenuItem("Tools/DS2/Build Boss Move Assets")]
        static void BuildBossMoves() => BuildMoveSet(BossMovesFolder, "Boss_", forBoss: true);

        static void BuildMoveSet(string folder, string prefix, bool forBoss)
        {
            string MovesFolder = folder;
            Directory.CreateDirectory(Path.GetFullPath(MovesFolder));
            var made = new Dictionary<MoveId, MoveDefinition>();

            foreach (Spec s in Specs)
            {
                string path = MovesFolder + "/" + prefix + s.id + ".asset";
                var move = AssetDatabase.LoadAssetAtPath<MoveDefinition>(path);
                if (move == null)
                {
                    move = ScriptableObject.CreateInstance<MoveDefinition>();
                    AssetDatabase.CreateAsset(move, path);
                }

                move.moveId = s.id;
                move.stateName = s.state;
                move.speedMultiplier = s.Multiplier;
                move.measuredLength = s.measured;
                move.useRootMotion = true;
                move.damage = s.damage;
                move.postureDamage = s.posture;
                move.hitboxOpen = s.hbOpen;
                move.hitboxClose = s.hbClose;
                move.iframeStart = s.ifStart;
                move.iframeEnd = s.ifEnd;
                move.parryStart = s.pStart;
                move.parryEnd = s.pEnd;
                move.cancelWindow = s.cancel;
                move.chainChance = s.ChainChance;
                move.moveEnd = forBoss ? s.BossEnd : s.End;
                move.trackUntil = s.TrackUntil;
                move.trackDegreesPerSecond = forBoss ? s.bossTrack : s.playerTrack;
                move.weaponSocket = forBoss ? "" : s.socket;
                move.endWeaponSocket = forBoss ? "" : s.endSocket;

                // The wind-up whoosh lands just before the blade starts moving.
                move.swingSoundNormalized = Mathf.Max(0.02f, s.hbOpen - 0.06f);

                EditorUtility.SetDirty(move);
                made[s.id] = move;
            }

            // Second pass - every chain target must exist before it can be referenced.
            foreach (Spec s in Specs)
            {
                if (s.chainTo != MoveId.None && made.TryGetValue(s.chainTo, out MoveDefinition next))
                {
                    made[s.id].nextInChain = next;
                    EditorUtility.SetDirty(made[s.id]);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DS2] " + made.Count + " move assets written to " + MovesFolder);

            ValidateHitboxWindows();
        }

        /// <summary>
        /// Her repertoire and how she picks from it. Slash2 and Slash3 carry weight 0 on purpose:
        /// she reaches them by chaining out of Slash1, so the combo reads as a combo instead of
        /// three unrelated swings. Cooldowns are what stop her leaning on one answer.
        /// </summary>
        static readonly (MoveId id, float weight, float min, float max, float cooldown)[] BossPlan =
        {
            (MoveId.Slash1,      3.0f, 0f,   2.8f,  0f),
            (MoveId.Slash2,      0f,   0f,   2.8f,  0f),   // chain only
            (MoveId.Slash3,      0f,   0f,   2.8f,  0f),   // chain only
            (MoveId.Skill1,      1.0f, 0f,   3.0f,  6f),
            (MoveId.Skill2,      1.0f, 2.5f, 5.5f,  8f),   // the 5.18 m lunge
            (MoveId.Skill3,      0.6f, 0f,   3.0f, 12f),   // her heaviest, rarest
            (MoveId.QuickShiftF, 2.0f, 2.5f, 5.0f,  3f),   // covers 2.85 m
            (MoveId.QuickShiftB, 0f, 0f,   2.0f,  7f),   // make space
            (MoveId.QuickShiftL, 0f, 0f,   3.0f,  6f),   // circle
            (MoveId.QuickShiftR, 0f, 0f,   3.0f,  6f),
        };

        [MenuItem("Tools/DS2/Build Boss Moveset")]
        static void BuildBossMoveset()
        {
            const string path = BossMovesFolder + "/BossMoveset.asset";
            Directory.CreateDirectory(Path.GetFullPath(BossMovesFolder));

            var set = AssetDatabase.LoadAssetAtPath<BossMoveset>(path);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<BossMoveset>();
                AssetDatabase.CreateAsset(set, path);
            }

            var list = new List<BossMoveset.Entry>();
            foreach ((MoveId id, float weight, float min, float max, float cooldown) p in BossPlan)
            {
                var move = AssetDatabase.LoadAssetAtPath<MoveDefinition>(
                    BossMovesFolder + "/Boss_" + p.id + ".asset");

                if (move == null)
                {
                    Debug.LogWarning("[DS2] Boss_" + p.id + " missing - run Build Boss Move Assets first.");
                    continue;
                }

                list.Add(new BossMoveset.Entry
                {
                    move = move, weight = p.weight,
                    minRange = p.min, maxRange = p.max, cooldown = p.cooldown,
                });
            }

            set.entries = list.ToArray();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DS2] Boss moveset: " + list.Count + " entries -> " + path);
        }

        /// <summary>
        /// Her phrases: authored attack strings, which is the whole point of the AI rewrite.
        ///
        /// The old boss re-rolled one move from a weighted table every time she became free. That
        /// produces a different sequence every exchange and therefore no sequence at all - nothing
        /// to recognise, nothing to punish, nothing to learn. Randomness now lives one level up:
        /// she rolls for a PHRASE, then performs it as written. Which one you get is
        /// unpredictable; what it does once it starts is not.
        ///
        /// Name each one after what it TEACHES, not after the moves in it.
        /// </summary>
        readonly struct PhraseStep
        {
            public readonly MoveId id;
            public readonly float gapAfter;
            public readonly bool linkInCancel;
            public readonly float endOverride;
            public readonly float continueChance;
            public readonly float breakRange;

            public PhraseStep(MoveId id, float gapAfter = 0f, bool linkInCancel = false,
                              float endOverride = 0f, float continueChance = 1f,
                              float breakRange = 0f)
            {
                this.id = id;
                this.gapAfter = gapAfter;
                this.linkInCancel = linkInCancel;
                this.endOverride = endOverride;
                this.continueChance = continueChance;
                this.breakRange = breakRange;
            }
        }

        readonly struct PhrasePlan
        {
            public readonly string file, name;
            public readonly float weight, min, max, cooldown, extraPressure;
            public readonly PhraseStep[] steps;

            public PhrasePlan(string file, string name, float weight, float min, float max,
                              float cooldown, float extraPressure, params PhraseStep[] steps)
            {
                this.file = file; this.name = name;
                this.weight = weight; this.min = min; this.max = max;
                this.cooldown = cooldown; this.extraPressure = extraPressure;
                this.steps = steps;
            }
        }

        static readonly PhrasePlan[] Phrases =
        {
            // The baseline she opens with. Links inside the cancel window exactly like the player
            // does, so her combo has the same snap as theirs; the last slash plays its full
            // sheathe (endOverride 1) as the deliberate "punish me now" window.
            new PhrasePlan("Phrase_Pressure", "Pressure", 3.0f, 0f, 2.9f, 0f, 0f,
                new PhraseStep(MoveId.Slash1, linkInCancel: true, continueChance: 1.0f, breakRange: 3.2f),
                new PhraseStep(MoveId.Slash2, linkInCancel: true, continueChance: 0.55f, breakRange: 3.6f),
                new PhraseStep(MoveId.Slash3, endOverride: 0.85f)),

            // The same opener with a hole in the middle. This is the contradiction that stops the
            // player running on autopilot: identical first frames, different rhythm, so the dodge
            // timing they learned against Pressure gets them hit here.
            new PhrasePlan("Phrase_Feint", "Feint", 2.0f, 0f, 2.9f, 3f, 0f,
                new PhraseStep(MoveId.Slash1, gapAfter: 0.85f, continueChance: 1f),
                new PhraseStep(MoveId.Slash2, linkInCancel: true, continueChance: 0.5f, breakRange: 3.4f),
                new PhraseStep(MoveId.Slash3, endOverride: 0.85f)),

            // Distance is not safety. Quickshift_F covers 2.85 m, so this arrives already in range.
            new PhrasePlan("Phrase_GapCloser", "Gap closer", 2.5f, 2.8f, 5.0f, 2.5f, 0f,
                new PhraseStep(MoveId.QuickShiftF, gapAfter: 0.1f),
                new PhraseStep(MoveId.Slash1, linkInCancel: true, continueChance: 0.7f, breakRange: 3.2f),
                new PhraseStep(MoveId.Slash2, endOverride: 0.85f)),

            // Her heaviest read. K_Sp_Skill_2 travels 5.18 m: a huge tell and a huge reward for
            // seeing it coming, which is why it ends in the full sheathe.
            new PhrasePlan("Phrase_Committed", "Committed lunge", 1.0f, 2.6f, 5.5f, 8f, 1f,
                new PhraseStep(MoveId.Skill2, endOverride: 1f)),

            // Weight 0: reachable only by BossBrain catching the player in recovery frames. Being
            // punished for whiffing should read as a consequence, not a coincidence.
            new PhrasePlan("Phrase_Punish", "Punish", 0f, 0f, 3.0f, 0f, 1f,
                // No linkInCancel here: Slash1 chains into Slash2, not Skill1, so the cancel
                // would be refused and fall back to waiting regardless.
                new PhraseStep(MoveId.Slash1, gapAfter: 0.05f, continueChance: 0.8f),
                new PhraseStep(MoveId.Skill1, endOverride: 1f)),
        };

        [MenuItem("Tools/DS2/Build Boss Phrases")]
        static void BuildBossPhrases()
        {
            Directory.CreateDirectory(Path.GetFullPath(BossPhraseFolder));
            int written = 0;

            foreach (PhrasePlan plan in Phrases)
            {
                string path = BossPhraseFolder + "/" + plan.file + ".asset";
                var phrase = AssetDatabase.LoadAssetAtPath<BossPhrase>(path);
                if (phrase == null)
                {
                    phrase = ScriptableObject.CreateInstance<BossPhrase>();
                    AssetDatabase.CreateAsset(phrase, path);
                }

                var steps = new List<BossPhrase.Step>();
                bool ok = true;

                foreach (PhraseStep ps in plan.steps)
                {
                    var move = AssetDatabase.LoadAssetAtPath<MoveDefinition>(
                        BossMovesFolder + "/Boss_" + ps.id + ".asset");

                    if (move == null)
                    {
                        Debug.LogWarning("[DS2] Boss_" + ps.id +
                                         " missing - run Build Boss Move Assets first.");
                        ok = false;
                        break;
                    }

                    steps.Add(new BossPhrase.Step
                    {
                        move = move,
                        gapAfter = ps.gapAfter,
                        linkInCancel = ps.linkInCancel,
                        endOverride = ps.endOverride,
                        continueChance = ps.continueChance,
                        breakRange = ps.breakRange,
                    });
                }

                if (!ok) continue;

                phrase.phraseName = plan.name;
                phrase.steps = steps.ToArray();
                phrase.weight = plan.weight;
                phrase.minRange = plan.min;
                phrase.maxRange = plan.max;
                phrase.cooldown = plan.cooldown;
                phrase.extraPressureCost = plan.extraPressure;

                EditorUtility.SetDirty(phrase);
                written++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DS2] " + written + " boss phrases written to " + BossPhraseFolder);
        }

        /// <summary>
        /// The whole boss pipeline in the right order, because getting it wrong fails quietly:
        /// phrases reference move assets, and wiring references phrases.
        /// </summary>
        [MenuItem("Tools/DS2/Rebuild Boss (moves, phrases, wiring)")]
        static void RebuildBoss()
        {
            BuildBossMoves();
            BuildBossMoveset();
            BuildBossPhrases();
            BossWiring.Wire();
        }

        /// <summary>
        /// Cross-checks every hitbox window against the clip's own baked SwitchSocket events.
        ///
        /// THIS EXISTS BECAUSE THE BUG IT CATCHES COST A DAY. The windows were taken as
        /// proportions from combat-design.html, which assumed a tight ~0.6 s attack. The real
        /// clips are 2-4.5 s draw-cut-sheathe cycles, so those proportions landed the hitbox
        /// AFTER the blade had already been stowed - on five of the six attacks. The HitBox is
        /// parented to the katana, so it went into the scabbard with it and registered hits from
        /// the character's hip, seconds after the swing the player actually watched.
        ///
        /// Nothing errored. The only symptom was hits landing at the wrong time. Docs/project-notes
        /// warned about exactly this ("opening one while the blade is sheathed is a silent bug")
        /// and it happened anyway, which is why it is a build step now rather than a note.
        /// </summary>
        [MenuItem("Tools/DS2/Validate Hitbox Windows")]
        static void ValidateHitboxWindows()
        {
            Dictionary<string, AnimationClip> clips = LoadClips();
            int bad = 0, checked_ = 0;

            foreach (Spec spec in Specs)
            {
                if (spec.hbClose <= spec.hbOpen) continue;          // no hitbox, nothing to check
                if (!clips.TryGetValue(spec.clip, out AnimationClip clip) || clip == null)
                {
                    Debug.LogWarning("[DS2] No clip '" + spec.clip + "' to validate " + spec.id);
                    continue;
                }

                checked_++;

                // AnimationEvent.time from a loaded clip is in SECONDS. Note that the times
                // written in the FBX .meta are NORMALIZED 0-1 - reading those as seconds and
                // dividing by length again double-normalizes, which is exactly the mistake that
                // made this validator necessary in the first place. Docs/clip-report.csv is the
                // authoritative source and lists event times in seconds.
                float length = clip.length > 0.01f ? clip.length : spec.measured;
                float held = 0f, sheathed = 1f;
                bool sawHand = false;

                foreach (AnimationEvent e in clip.events)
                {
                    if (e.functionName != "SwitchSocket" || string.IsNullOrEmpty(e.stringParameter))
                        continue;

                    float n = e.time / length;

                    // add_weapon_r is a RIGHT-HAND bone, not the scabbard - only Katana_Close
                    // actually stows the blade.
                    if (!sawHand && e.stringParameter.Contains("To_Hand_R_Socket-Blade"))
                    {
                        held = n;
                        sawHand = true;
                    }
                    else if (sawHand && e.stringParameter.Contains("To_Katana_Close-Blade"))
                    {
                        sheathed = n;
                        break;
                    }
                }

                bool ok = spec.hbOpen >= held - 0.01f && spec.hbClose <= sheathed + 0.01f;
                string line = spec.id + " (" + spec.clip + ", " + length.ToString("0.00") +
                              "s): blade held " +
                              held.ToString("0.000") + "-" + sheathed.ToString("0.000") +
                              ", hitbox " + spec.hbOpen.ToString("0.000") + "-" +
                              spec.hbClose.ToString("0.000");

                if (ok) Debug.Log("[DS2] OK  " + line);
                else
                {
                    bad++;
                    Debug.LogError("[DS2] HITBOX ON A SHEATHED BLADE  " + line +
                                   "\nThe HitBox is parented to the katana, so this window swings " +
                                   "a collider that is inside the scabbard. Move it inside the " +
                                   "blade-held interval above.");
                }
            }

            if (bad == 0) Debug.Log("[DS2] All " + checked_ + " hitbox windows are on a drawn blade.");
            else Debug.LogError("[DS2] " + bad + " of " + checked_ + " hitbox windows are wrong.");
        }

        [MenuItem("Tools/DS2/Build Combat Animator")]
        static void BuildController()
        {
            Dictionary<string, AnimationClip> clips = LoadClips();
            Directory.CreateDirectory(Path.GetFullPath(Path.GetDirectoryName(ControllerPath)));

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

            if (controller != null)
            {
                if (!EditorUtility.DisplayDialog("Rebuild controller?",
                        ControllerPath + " will be emptied and rebuilt.", "Rebuild", "Cancel"))
                    return;

                // Emptied and refilled rather than deleted and recreated: deleting the asset mints
                // a new GUID, which silently breaks every Animator already pointing at it.
                ClearController(controller);
            }
            else
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Stance", AnimatorControllerParameterType.Bool);
            controller.AddParameter("MoveSpeed", AnimatorControllerParameterType.Float);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            AnimatorState normal = DirectionalLocomotion
                ? MakeLocomotion2D(controller, "Locomotion", clips)
                : MakeLocomotion(controller, "Locomotion", clips, "Idle", "Walk", "Run");
            AnimatorState special = MakeLocomotion(controller, "LocomotionSpecial", clips, "Sp_Idle", "Sp_Walk", "Sp_Run");
            sm.defaultState = normal;

            // Stance is the only transition pair in the graph. Every move is entered from code.
            AnimatorStateTransition draw = normal.AddTransition(special);
            draw.AddCondition(AnimatorConditionMode.If, 0f, "Stance");
            draw.duration = 0.15f;
            draw.hasExitTime = false;

            AnimatorStateTransition sheathe = special.AddTransition(normal);
            sheathe.AddCondition(AnimatorConditionMode.IfNot, 0f, "Stance");
            sheathe.duration = 0.15f;
            sheathe.hasExitTime = false;

            int built = 0;
            foreach (Spec s in Specs)
            {
                if (AddOneShot(sm, normal, clips, s.state, s.clip, 1f, true, s.End)) built++;
            }

            foreach ((string state, string clip) r in Reactions)
            {
                if (AddOneShot(sm, normal, clips, r.state, r.clip, 1f, false, 0.9f)) built++;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DS2] " + ControllerPath + ": 2 locomotion trees + " + built + " one-shot states.");
        }

        /// <summary>
        /// Strips a controller back to empty while keeping the asset - and therefore its GUID, and
        /// therefore every Animator reference to it - intact.
        /// </summary>
        static void ClearController(AnimatorController controller)
        {
            while (controller.parameters.Length > 0)
                controller.RemoveParameter(0);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            foreach (ChildAnimatorState cs in sm.states)
                sm.RemoveState(cs.state);

            foreach (ChildAnimatorStateMachine sub in sm.stateMachines)
                sm.RemoveStateMachine(sub.stateMachine);

            // Blend trees live as sub-assets inside the .controller file. Removing the states that
            // referenced them leaves them orphaned in there, accumulating on every rebuild.
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (o is BlendTree tree) Object.DestroyImmediate(tree, true);
        }

        /// <summary>
        /// Idle / 8-way walk / 8-way run, as a 1D tree on Speed whose walk and run children are
        /// each a 2D directional tree on MoveX/MoveY.
        ///
        /// This is what makes lock-on work: she can hold her facing at the target and still move
        /// in any direction, because there is now a clip for every direction. The vendor pack had
        /// forward locomotion only, which is why she used to turn to face wherever she was going.
        /// </summary>
        static AnimatorState MakeLocomotion2D(AnimatorController c, string name,
                                              Dictionary<string, AnimationClip> clips)
        {
            AnimatorState state = c.CreateBlendTreeInController(name, out BlendTree root, 0);
            root.blendType = BlendTreeType.Simple1D;
            root.blendParameter = "Speed";
            root.useAutomaticThresholds = false;

            // A combat-ready idle, not the arms-down one - she is holding a drawn sword now.
            if (clips.TryGetValue("HumanF@CombatIdle1H01", out AnimationClip idle))
                root.AddChild(idle, 0f);
            else if (clips.TryGetValue("Idle", out AnimationClip fallbackIdle))
                root.AddChild(fallbackIdle, 0f);

            AddDirectionalTier(root, clips, "HumanF@Walk01_", "Walk", 0.5f);
            AddDirectionalTier(root, clips, "HumanF@Run01_", "Run", 1f);
            return state;
        }

        static void AddDirectionalTier(BlendTree root, Dictionary<string, AnimationClip> clips,
                                       string prefix, string label, float threshold)
        {
            BlendTree tier = root.CreateBlendTreeChild(threshold);
            tier.name = label;
            tier.blendType = BlendTreeType.SimpleDirectional2D;
            tier.blendParameter = "MoveX";
            tier.blendParameterY = "MoveY";
            tier.useAutomaticThresholds = false;

            int found = 0;
            foreach ((string suffix, Vector2 dir) d in Directions)
            {
                // In-place clips only. The [RM] variants carry root motion, which would fight
                // the scripted locomotion in PlayerLocomotion.OnAnimatorMove.
                if (clips.TryGetValue(prefix + d.suffix, out AnimationClip clip))
                {
                    tier.AddChild(clip, d.dir);
                    found++;
                }
            }

            if (found < Directions.Length)
                Debug.LogWarning("[DS2] " + label + ": only " + found + "/" + Directions.Length +
                                 " directions found for prefix " + prefix);
        }

        static AnimatorState MakeLocomotion(AnimatorController c, string name,
                                            Dictionary<string, AnimationClip> clips,
                                            string idle, string walk, string run)
        {
            AnimatorState state = c.CreateBlendTreeInController(name, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;

            if (clips.TryGetValue(idle, out AnimationClip a)) tree.AddChild(a, 0f);
            if (clips.TryGetValue(walk, out AnimationClip b)) tree.AddChild(b, 0.5f);
            if (clips.TryGetValue(run, out AnimationClip d)) tree.AddChild(d, 1f);
            return state;
        }

        /// <summary>
        /// An isolated state with NO inbound transition, entered only via
        /// Animator.CrossFadeInFixedTime(move.StateHash). This avoids the demo controller
        /// one-trigger-per-clip pattern, which does not scale to data-driven moves.
        /// </summary>
        static bool AddOneShot(AnimatorStateMachine sm, AnimatorState fallback,
                               Dictionary<string, AnimationClip> clips,
                               string stateName, string clipName, float speed,
                               bool speedFromParameter, float exitTime)
        {
            if (!clips.TryGetValue(clipName, out AnimationClip clip))
            {
                Debug.LogWarning("[DS2] Clip " + clipName + " not found - state " + stateName + " skipped.");
                return false;
            }

            AnimatorState state = sm.AddState(stateName);
            state.motion = clip;
            state.speed = speed;

            // Move states read their rate from the MoveSpeed float, which CombatActor sets from
            // MoveDefinition.speedMultiplier. That makes the asset the live tuning surface -
            // baking speed into the state here would make the inspector field dead data.
            if (speedFromParameter)
            {
                state.speedParameterActive = true;
                state.speedParameter = "MoveSpeed";
            }

            AnimatorStateTransition exit = state.AddTransition(fallback);
            exit.hasExitTime = true;
            exit.exitTime = exitTime;
            exit.duration = 0.1f;
            return true;
        }

        static Dictionary<string, AnimationClip> LoadClips()
        {
            var map = new Dictionary<string, AnimationClip>();
            string[] folders = AssetDatabase.IsValidFolder(KiFolder)
                ? new[] { AnimFolder, KiFolder }
                : new[] { AnimFolder };

            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (Object o in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                {
                    if (o is AnimationClip c && !c.name.StartsWith("__preview__")) map[c.name] = c;
                }
            }
            return map;
        }
    }
}

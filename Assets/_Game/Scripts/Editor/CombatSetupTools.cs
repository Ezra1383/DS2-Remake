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
        const string AnimFolder = "Assets/CombatGirlsCharacterPack/Katana_Girl/Animations";
        const string MovesFolder = "Assets/_Game/Moves";
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

        struct Spec
        {
            public MoveId id;
            public string state, clip;
            public float measured;
            public int damage, posture;
            public float hbOpen, hbClose, ifStart, ifEnd, cancel;
            public MoveId chainTo;

            /// <summary>Per-move override. Left at 0 the global Tempo is used.</summary>
            public float tempo;

            /// <summary>Normalized end point. Left at 0 the whole clip plays.</summary>
            public float end;

            public float End => end > 0f ? end : 1f;

            /// <summary>Socket at move start, and at move end. Empty leaves the clip in charge.</summary>
            public string socket, endSocket;

            public float Multiplier => tempo > 0f ? tempo : Tempo;
            public float Duration => measured / Multiplier;
        }

        // speedMultiplier is derived from measured/duration, never hand-typed.
        static readonly Spec[] Specs =
        {
            new Spec { id = MoveId.Slash1, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", end = 0.52f, state = "Attack1", clip = "Attack1", measured = 2.033f,
                       damage = 8,  posture = 12, hbOpen = 0.300f, hbClose = 0.467f, cancel = 0.467f, chainTo = MoveId.Slash2 },
            new Spec { id = MoveId.Slash2, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", end = 0.50f, state = "Attack2", clip = "Attack2", measured = 1.833f,
                       damage = 10, posture = 15, hbOpen = 0.258f, hbClose = 0.419f, cancel = 0.484f, chainTo = MoveId.Slash3 },
            new Spec { id = MoveId.Slash3, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", end = 0.55f, state = "Attack3", clip = "Attack3", measured = 2.267f,
                       damage = 14, posture = 25, hbOpen = 0.289f, hbClose = 0.444f, cancel = 1f },

            new Spec { id = MoveId.Evade, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", state = "Evade", clip = "Evade", measured = 1.467f,
                       ifStart = 0.083f, ifEnd = 0.633f, cancel = 0.700f },

            new Spec { id = MoveId.QuickShiftF, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", state = "Quickshift_F", clip = "Quickshift_F", measured = 1f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },
            new Spec { id = MoveId.QuickShiftB, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", state = "Quickshift_B", clip = "Quickshift_B", measured = 1f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },
            new Spec { id = MoveId.QuickShiftL, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", state = "Quickshift_L", clip = "Quickshift_L", measured = 1f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },
            new Spec { id = MoveId.QuickShiftR, socket = "To_Hand_R_Socket-Blade", endSocket = "To_Hand_R_Socket-Blade", state = "Quickshift_R", clip = "Quickshift_R", measured = 1f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },

            // Vulnerable throughout - no hitbox, no i-frames. That is the cost of the stance.
            new Spec { id = MoveId.Draw,    state = "Take", clip = "Take", measured = 1.733f, cancel = 1f },
            new Spec { id = MoveId.Sheathe, state = "Put",  clip = "Put",  measured = 1.667f, cancel = 1f },

            new Spec { id = MoveId.Skill1, end = 0.93f, state = "Sp_Skill1", clip = "Sp_Skill1", measured = 3.200f,
                       damage = 18, posture = 30, hbOpen = 0.309f, hbClose = 0.433f, cancel = 1f },
            // Vendor naming inconsistency: state Sp_Skill2 plays clip K_Sp_Skill_2.
            new Spec { id = MoveId.Skill2, end = 0.93f, state = "Sp_Skill2", clip = "K_Sp_Skill_2", measured = 3.867f,
                       damage = 22, posture = 34, hbOpen = 0.328f, hbClose = 0.483f, cancel = 1f },
            new Spec { id = MoveId.Skill3, end = 0.85f, state = "Sp_Skill3", clip = "Sp_Skill3", measured = 4.500f,
                       damage = 28, posture = 40, hbOpen = 0.345f, hbClose = 0.483f, cancel = 1f },
        };

        // Reaction states. Note the swap: clip Hit1 is K_Hit_R.fbx, clip Hit2 is K_Hit_L.fbx.
        static readonly (string state, string clip)[] Reactions =
        {
            ("Hit_R", "Hit1"), ("Hit_L", "Hit2"), ("Stun", "Stun"), ("Die", "Die"),
        };

        [MenuItem("Tools/DS2/Build Move Assets")]
        static void BuildMoves()
        {
            Directory.CreateDirectory(Path.GetFullPath(MovesFolder));
            var made = new Dictionary<MoveId, MoveDefinition>();

            foreach (Spec s in Specs)
            {
                string path = MovesFolder + "/Move_" + s.id + ".asset";
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
                move.cancelWindow = s.cancel;
                move.moveEnd = s.End;
                move.weaponSocket = s.socket;
                move.endWeaponSocket = s.endSocket;

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

            AnimatorState normal = MakeLocomotion(controller, "Locomotion", clips, "Idle", "Walk", "Run");
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
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { AnimFolder }))
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

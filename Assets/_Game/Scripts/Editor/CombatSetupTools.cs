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

        struct Spec
        {
            public MoveId id;
            public string state, clip;
            public float measured, duration;
            public int damage, posture;
            public float hbOpen, hbClose, ifStart, ifEnd, cancel;
            public MoveId chainTo;
        }

        // speedMultiplier is derived from measured/duration, never hand-typed.
        static readonly Spec[] Specs =
        {
            new Spec { id = MoveId.Slash1, state = "Attack1", clip = "Attack1", measured = 2.033f, duration = 1.00f,
                       damage = 8,  posture = 12, hbOpen = 0.300f, hbClose = 0.467f, cancel = 0.467f, chainTo = MoveId.Slash2 },
            new Spec { id = MoveId.Slash2, state = "Attack2", clip = "Attack2", measured = 1.833f, duration = 0.90f,
                       damage = 10, posture = 15, hbOpen = 0.258f, hbClose = 0.419f, cancel = 0.484f, chainTo = MoveId.Slash3 },
            new Spec { id = MoveId.Slash3, state = "Attack3", clip = "Attack3", measured = 2.267f, duration = 1.15f,
                       damage = 14, posture = 25, hbOpen = 0.289f, hbClose = 0.444f, cancel = 1f },

            new Spec { id = MoveId.Evade, state = "Evade", clip = "Evade", measured = 1.467f, duration = 0.75f,
                       ifStart = 0.083f, ifEnd = 0.633f, cancel = 0.700f },

            new Spec { id = MoveId.QuickShiftF, state = "Quickshift_F", clip = "Quickshift_F", measured = 1f, duration = 0.50f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },
            new Spec { id = MoveId.QuickShiftB, state = "Quickshift_B", clip = "Quickshift_B", measured = 1f, duration = 0.50f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },
            new Spec { id = MoveId.QuickShiftL, state = "Quickshift_L", clip = "Quickshift_L", measured = 1f, duration = 0.50f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },
            new Spec { id = MoveId.QuickShiftR, state = "Quickshift_R", clip = "Quickshift_R", measured = 1f, duration = 0.50f,
                       ifStart = 0.091f, ifEnd = 0.545f, cancel = 0.636f },

            // Vulnerable throughout - no hitbox, no i-frames. That is the cost of the stance.
            new Spec { id = MoveId.Draw,    state = "Take", clip = "Take", measured = 1.733f, duration = 0.85f, cancel = 1f },
            new Spec { id = MoveId.Sheathe, state = "Put",  clip = "Put",  measured = 1.667f, duration = 0.85f, cancel = 1f },

            new Spec { id = MoveId.Skill1, state = "Sp_Skill1", clip = "Sp_Skill1", measured = 3.200f, duration = 1.60f,
                       damage = 18, posture = 30, hbOpen = 0.309f, hbClose = 0.433f, cancel = 1f },
            // Vendor naming inconsistency: state Sp_Skill2 plays clip K_Sp_Skill_2.
            new Spec { id = MoveId.Skill2, state = "Sp_Skill2", clip = "K_Sp_Skill_2", measured = 3.867f, duration = 1.95f,
                       damage = 22, posture = 34, hbOpen = 0.328f, hbClose = 0.483f, cancel = 1f },
            new Spec { id = MoveId.Skill3, state = "Sp_Skill3", clip = "Sp_Skill3", measured = 4.500f, duration = 2.25f,
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
                move.duration = s.duration;
                move.speedMultiplier = s.measured / s.duration;
                move.useRootMotion = true;
                move.damage = s.damage;
                move.postureDamage = s.posture;
                move.hitboxOpen = s.hbOpen;
                move.hitboxClose = s.hbClose;
                move.iframeStart = s.ifStart;
                move.iframeEnd = s.ifEnd;
                move.cancelWindow = s.cancel;

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

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null &&
                !EditorUtility.DisplayDialog("Rebuild controller?",
                    ControllerPath + " already exists and will be replaced.", "Replace", "Cancel"))
                return;

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveY", AnimatorControllerParameterType.Float);
            controller.AddParameter("Stance", AnimatorControllerParameterType.Bool);

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
                if (AddOneShot(sm, normal, clips, s.state, s.clip, s.measured / s.duration)) built++;
            }

            foreach ((string state, string clip) r in Reactions)
            {
                if (AddOneShot(sm, normal, clips, r.state, r.clip, 1f)) built++;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DS2] " + ControllerPath + ": 2 locomotion trees + " + built + " one-shot states.");
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
                               string stateName, string clipName, float speed)
        {
            if (!clips.TryGetValue(clipName, out AnimationClip clip))
            {
                Debug.LogWarning("[DS2] Clip " + clipName + " not found - state " + stateName + " skipped.");
                return false;
            }

            AnimatorState state = sm.AddState(stateName);
            state.motion = clip;
            state.speed = speed;

            AnimatorStateTransition exit = state.AddTransition(fallback);
            exit.hasExitTime = true;
            exit.exitTime = 0.9f;
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

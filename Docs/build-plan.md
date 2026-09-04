# Ten Deaths to the Mirror — Build Plan

## Context

`Docs/combat-design.html` is a finished design document: one arena, one boss, a mirror match where each death teaches you the move that killed you. It defines the frame data, the posture economy, the unlock ladder and a four-week schedule. What it does not have is an execution order — a list of steps you can work down, each with a stopping condition that tells you it is done.

The Unity project is currently a bare URP template plus the Combat Girls asset pack. Nothing gameplay-related exists. Start was 3 Sep 2026, today is 4 Sep 2026 (**Day 2 of 30**), ship is 3 Oct 2026, solo, deadline firm.

This plan turns the design doc into that ordered task list, and corrects the handful of places where the doc's assumptions don't match what is actually in the project.

**Two schedule facts to internalise now:**
- Day N = (2 + N) September. Day 22 = 24 Sep, Day 28 = 30 Sep, Day 30 = 2 Oct.
- The doc's masthead promises a "three-day buffer," but days 29–30 already consume 1–2 Oct. **The real buffer is one day: 3 Oct.** Plan accordingly; do not spend it in week one.

---

## What the project audit changed

Findings from reading the actual assets, and the decisions that follow. These supersede the corresponding lines in `combat-design.html`.

| Finding | Consequence |
|---|---|
| Toon shader was not installed, and once imported its URP SubShader would not compile against URP 17.6. | **Resolved 4 Sep** — see *Vendor modifications* below. Toon shading, outlines, matcap and SDF face shadow all working. |
| `KatanaGirl_FullBody.prefab` references **Magica Cloth** (guid `e6f5338a…`), which is not in the project. Missing-script components on hair, ribbon, skirt. | Strip them in Phase 0. Cloth sim is out of scope; the prefab must be clean or every playtest logs errors. |
| **Every clip has baked `SwitchSocket` animation events** consumed by `Character_Weapon_Controller` on the prefab root. | That component is mandatory on player and boss. It swaps the katana between hand and back via ParentConstraint source-switching, not reparenting — so a hitbox parented to the katana object follows correctly. `Dummy_Event.cs` exists to swallow the events on anything that shouldn't react. |
| All clips are **Humanoid** (`animationType: 3`), root XZ displacement preserved (`keepOriginalPositionY: 1`, no XZ bake). | The doc's "root motion on for attacks and dodges" is correct and free. Retargeting to the bot body also works. |
| Clip names **inside** the FBXs are `Attack1`, `Evade`, `Quickshift_F`, `Sp_Skill3` — not the `K_Attack_1` filenames the doc uses. | `MoveDefinition` must reference **animator state names**, not clip names or triggers. See the architecture change below. |
| **Measured 4 Sep: every move is 2.3×–3.9× slower than the design assumed.** Not one outlier — the whole table. | The frame data in `combat-design.html` is superseded. See *Measured frame data* below. |
| `Humanoid_F_Katana.prefab` is a light (364-line) bot body already holding the katana, with `Character_Weapon_Controller`, no Magica dependency, no toon-shader dependency, same humanoid avatar. | **This is the week-1 training dummy and the early boss stand-in.** Swap in the real girl once feel is proven. |
| No Cinemachine in the manifest. | Install Cinemachine 3 (decided). Replaces hand-written `LockOnCamera` and gives Impulse for the week-3 shake pass. |
| `activeInputHandler: 1` — new Input System only. `Assets/InputSystem_Actions.inputactions` is the Unity 6 template (Player map: Move, Look, Attack, Sprint, Jump, Crouch, Interact). | Extend that asset; don't author a second one. |

### One architecture change to the doc

The doc puts hitbox open/close on animation events. **Don't.** Events are baked into the FBX `.meta` alongside the existing `SwitchSocket` events — re-adding yours after any reimport is a footgun, and retuning means leaving the code editor.

Instead: `MoveDefinition` stores hitbox open/close as **normalized time**, and `CombatActor` polls `stateInfo.normalizedTime` each frame to open and close the window. Retuning a hitbox becomes editing a float on a ScriptableObject with the game running. This directly defuses the doc's own "clip timings don't match the targets" risk, and it means `AnimationEventRelay.cs` is only needed if you later want VFX events.

Same reasoning for the animator: **no trigger-per-move.** The demo `School_Katana_Controller` has 38 states and one trigger each; that pattern does not scale to data-driven moves. Author `KG_Combat.controller` with a locomotion blend tree, a Special-stance blend tree, and every move as an **isolated state with no inbound transitions**. Enter them with `Animator.CrossFadeInFixedTime(stateHash, blend)`. `MoveDefinition` stores the state name; hash it on enable.

---

## Measured frame data — supersedes the design doc

Measured 4 Sep 2026 with `Tools ▸ DS2 ▸ Clip Report`. Raw output: `Docs/clip-report.csv`.

**The frame-data table in `combat-design.html` is not achievable.** Every move needs a 2.3×–3.9×
speed multiplier to hit its stated target — a systematic error, not a few bad rows. At those
multipliers the animation reads as fast-forwarded video.

| Move | Clip | Measured | Doc target | Doc needed | **New target** | **Multiplier** |
|---|---|---|---|---|---|---|
| Slash 1 | `Attack1` | 2.033 s | 0.60 | 3.39× | **1.00 s** | 2.03 |
| Slash 2 | `Attack2` | 1.833 s | 0.62 | 2.96× | **0.90 s** | 2.04 |
| Slash 3 | `Attack3` | 2.267 s | 0.90 | 2.52× | **1.15 s** | 1.97 |
| Evade | `Evade` | 1.467 s | 0.60 | 2.44× | **0.75 s** | 1.96 |
| Quick Shift | `Quickshift_F/B/L/R` | 1.000 s | 0.44 | 2.27× | **0.50 s** | 2.00 |
| Draw / Sheathe | `Take` / `Put` | 1.733 / 1.667 s | 0.45 | 3.85× / 3.70× | **0.85 s** | 2.04 / 1.96 |
| Skill 1 | `Sp_Skill1` | 3.200 s | 0.97 | 3.30× | **1.60 s** | 2.00 |
| Skill 2 | `K_Sp_Skill_2` | 3.867 s | 1.16 | 3.33× | **1.95 s** | 1.98 |
| Skill 3 | `Sp_Skill3` | 4.500 s | 1.45 | 3.10× | **2.25 s** | 2.00 |

**Decision: a global 2.0×.** It gives round numbers, keeps every design ratio intact, and suits
what these clips actually are — big theatrical swings travelling 1.4–2.2 m. Trying to make them
twitchy fights the animation. The result is a heavier, more deliberate fight, closer to a Dark
Souls greatsword than to Bloodborne. Treat 2.0× as the starting point and tune individual moves
toward 2.5× if they feel sluggish in play; that is exactly what the per-move multiplier is for.

### Two derived numbers must move with it

- **Full three-slash chain: 2.1 s → 3.05 s.** So the **stun window goes 2.5 s → 3.1 s**, because
  the doc's stated reason for its length is "exactly long enough for one full three-slash chain."
  The `Stun` clip is 2.0 s and loops, so holding it 3.1 s is free.
- **Posture decay: 8/s → 5.5/s.** Sustained pressure now lands 52 posture over 3.05 s (17/s) where
  the doc assumed 52 over 2.1 s (24.8/s). Decay has to drop proportionally or the meter can never
  be filled, and "aggression is the correct defense" stops being true.

Also scale the boss's mandatory neutral gap **0.8 s → 1.0 s** to keep its relative size.

### What the measurements also revealed

- **`Sp_Skill3` is the only 60 fps clip** — everything else is 30. That is the entire explanation
  for its 270 frames. Nothing is wrong with it.
- **`Evade` has zero net displacement** (`RootXZNet 0`, `RootXZPath 0.83`). It moves and returns.
  The design doc says Evade "gets you out of trouble but gives up your position" — that is false.
  The real distinction is **in-place i-frames (Evade) vs. displacement (Quick Shift, 2.8–3.1 m)**,
  which is cleaner than the doc's version. Update the doc's wording, keep the mechanic.
- **The boss's range bands are already correct.** `Quickshift_F` travels 2.85 m and `K_Sp_Skill_2`
  travels 5.18 m, which map almost exactly onto the doc's 2.5 m and 5 m bands.
- **`Hit1` is `K_Hit_R.fbx` and `Hit2` is `K_Hit_L.fbx`.** The clip names carry no side information.
  Do not guess in `HitReaction.cs`.
- **Skill 2's clip is named `K_Sp_Skill_2`** while Skills 1 and 3 are `Sp_Skill1` / `Sp_Skill3`.
  Vendor inconsistency; it is why that row initially had no target match.

---

## Vendor modifications — READ THIS FIRST

Three hand-edits inside `Assets/CombatGirlsCharacterPack/`. **None of them survive reimporting the Combat Girls pack or the toon `.unitypackage`.** If rendering goes magenta again or `CS0619` errors reappear after any reimport, this list is the reason.

**1. `…/Unity Chan Toon Shader - SDF - Unity6_URP/Runtime/Shaders/URP/URPIncludeGuards.hlsl` — patched (+6 lines).**

URP 17.6 moved the parameterless `IsSurfaceTypeTransparent()` into `Shaders/Utils/SurfaceType.hlsl`, which URP reaches only via `LitInput.hlsl`. UTS deliberately blocks `LitInput.hlsl` (Toon declares its own `UnityPerMaterial` CBUFFER) but still includes URP's `LitForwardPass.hlsl`, whose line 265 calls that function. Result: `'IsSurfaceTypeTransparent': no matching 0 parameter function`, the `Toon/Toon` ForwardLit pass fails, everything renders magenta. The patch supplies the overload against Toon's own `_Surface` and claims `SurfaceType.hlsl`'s include guard so it cannot come back and redeclare `_Surface`:

```hlsl
#ifndef UNIVERSAL_SURFACE_TYPE_TRANSPARENT_INCLUDED
#define UNIVERSAL_SURFACE_TYPE_TRANSPARENT_INCLUDED
inline bool IsSurfaceTypeTransparent() { return _Surface > 0; }
inline bool IsSurfaceTypeOpaque()      { return !IsSurfaceTypeTransparent(); }
#endif
```

This is the concrete form of the store page's "tested to Unity 6.4" caveat. It is in URP's shader library, not in any package metadata, which is why nothing in `package.json` or the asmdefs predicts it.

**2. `Biperworks_Tools/Editor/MissingChecker/` — deleted.** Used `SerializedProperty.objectReferenceInstanceIDValue`, obsolete-as-error in 6.6 (`CS0619`, not suppressible by pragma). A standalone vendor prefab-diffing utility; nothing referenced it.

**3. Toon package `Tests/` folders ×2 — deleted** (`Unity6_URP/Tests/` and `com.unity.film-internal-utilities/Tests/`). Used `Object.GetInstanceID()`, also `CS0619`. These only compiled because the vendor ships the package as loose files under `Assets/` — in `Packages/`, Unity never builds package tests unless listed under `testables` in `manifest.json`. Deleting them restores intended behaviour.

Remaining `CS0618` **warnings** in `AnalyticsSender.cs`, `MonoBehaviourSingleton.cs` and `ObjectUtility.cs` are expected and harmless — live runtime code, obsolete-but-functional. Do not delete those files.

---

## Phase 0 — Unblock (Day 2)

Everything here is a hard dependency for week one. None of it is gameplay.

- [x] **Import the toon shader** and fix its URP 17.6 incompatibility. *Done 4 Sep — see Vendor modifications.* Verified: toon shading, outline pass, blade matcap, and SDF face shadow tracking correctly under camera orbit.
- [x] **Clear all compile errors.** *Done 4 Sep.* Project compiles clean; only the harmless `CS0618` warnings remain.
- [x] **Install Cinemachine** — 6.6.0 (the CM3 line under Unity 6 versioning). *Done 4 Sep.*
- [x] **Create the gameplay folder.** `Assets/_Game/{Scripts,Prefabs,Moves,Animation,Scenes,VFX,Audio}`. *Done 4 Sep.* Nothing you write goes inside `CombatGirlsCharacterPack/` — that folder stays read-only so the demo viewer keeps working as a clip previewer.
- [x] **Write `ClipReportWindow.cs`** (`Assets/_Game/Scripts/Editor/`, menu `Tools ▸ DS2 ▸ Clip Report`). *Done 4 Sep* — output in `Docs/clip-report.csv`, conclusions in *Measured frame data*.
- [x] **Strip Magica Cloth** from `KatanaGirl_FullBody.prefab`. *Done 4 Sep* — exactly 20 Magica GameObjects and 19 missing-script components removed, verified by set-diff against the committed version. All 19 `SkinnedMeshRenderer`s, `Weapon`, `SchoolUniform`, `SportsWear` and `No_Glass` intact.
- [x] **New scene** `Assets/_Game/Scenes/Arena.unity`. *Done 4 Sep* — floor plane, four boundary cubes with colliders, directional light, `Dome` as skybox, `CinemachineCamera`, in Build Settings with `SampleScene` disabled.
- [ ] **Create the player prefab variant.** *Outstanding.* The vendor `Prefab/` folder was **moved** into `_Game/Prefabs/` rather than a variant being created — same GUIDs, new location. Restore the pack's folder and make a real Prefab Variant, so gameplay components land on your asset and a pack reimport cannot collide. See *Prefab ownership* below.
- [ ] **Place `Humanoid_F_Katana`** in the arena as the training dummy.
- [ ] **Revert the vendor viewer scene** — `git checkout -- "Assets/CombatGirlsCharacterPack/Katana_Girl/Katana_Girl_Scene.unity"` with that scene closed in Unity. Editing the prefab nulled its `ButtonGenerator.characters` reference. Harmless and out of scope, but keep the pack clean.

**Done when:** the arena scene opens with a correctly-shaded Katana Girl and a dummy standing on a plane, no console errors, and the player prefab is a variant under `_Game/Prefabs/`.

### Prefab ownership

The character must be a **Prefab Variant** of the vendor prefab, stored in `_Game/Prefabs/`, for one
concrete reason: `CombatActor`, `PlayerLocomotion`, `PlayerCombat` and a `CharacterController` are all
about to be added to it. On the vendor asset those are destroyed by any pack reimport — the same
failure mode as the three HLSL/script patches above, but with gameplay code instead of six lines of
shader. A variant keeps your components in your file while the vendor prefab stays the base.

Moving the vendor folder is not equivalent: it leaves you editing the vendor asset, and because a
move preserves GUIDs, a later reimport recreates those files at their original paths with identical
GUIDs — a genuine conflict.

---

## Phase 1 — Make one sword hit one thing (Days 2–7, to 9 Sep)

### Step 1.1 — Move data and the animator (Day 2 evening – Day 3)

- `MoveDefinition.cs` — ScriptableObject: `stateName`, `speedMultiplier`, `damage`, `postureDamage`, `hitboxOpenNormalized`, `hitboxCloseNormalized`, `cancelWindowNormalized`, `iframeStartNormalized`, `iframeEndNormalized`, `useRootMotion`, `nextInChain` (a `MoveDefinition` reference), `moveId` (enum, used by progression).
- Author one asset per move in `Assets/_Game/Moves/`. **Multipliers are already measured — copy them straight from the *Measured frame data* table above.** All nine sit at 1.96–2.04; there is no per-move guesswork left. If a move feels sluggish in play, push it toward 2.5× individually rather than rescaling everything.
- `KG_Combat.controller` in `Assets/_Game/Animation/`: Base layer with a Normal locomotion 1D blend tree (`Idle → Walk → Run` on a `Speed` float), a Special locomotion blend tree, and isolated states for the 3 slashes, Evade, 4 Quick Shifts, Take, Put, 3 Skills, Hit_L, Hit_R, Stun, Die. Parameters: `Speed`, `MoveX`, `MoveY`, `Stance` (bool), and nothing else — no per-move triggers.

**Done when:** you can select any move asset and see its state play at the intended duration in the animator preview.

### Step 1.2 — Locomotion and camera (Days 3–4)

**Done 4 Sep.** `PlayerLocomotion.cs` and `LockOnController.cs`, both on the `KatanaGirl` variant root.

- Player root = the character prefab root itself (the Animator lives there; a separate parent just fights root motion).
- **Root motion reconciliation — the day-3 trap, solved.** Every position write goes through a single `controller.Move()` in `OnAnimatorMove`, choosing between `animator.deltaPosition` and scripted velocity on a `RootMotionDriven` flag. Root motion and script can never both write position in one frame. `PlayerCombat` sets that flag per-move from `MoveDefinition.useRootMotion` in Step 1.3.
- Walk/run speeds default to **1.55 / 4.8 m/s**, derived from the measured root displacement of the `Walk` and `Run` clips. These are the two dials for foot sliding.
- Lock-on camera: `HorizontalAxis.Value` on `CinemachineOrbitalFollow` is driven each `Update` toward the yaw from player to target, easing with `SmoothDampAngle`. That places the camera on the line running from the enemy through the player and out behind her — the Dark Souls arrangement. A `CinemachineTargetGroup` (created at runtime) handles `LookAt` so both stay framed. Requires **Orbital Follow with Binding Mode = World Space**; `Awake` warns otherwise.
- `InputSystem_Actions` extended with `Dodge` (Space / buttonEast), `LockOn` (Q / rightStickPress), `Stance` (R / buttonNorth). `Attack` already existed.

**Correction to this step as originally written: lock-on strafing is not achievable.** The pack ships
`Idle`, `Walk`, `Run` and nothing else — no strafe, no walk-back. Holding her facing at a target while
she moves sideways plays a forward walk across a sideways translation, which reads as skating, and no
blend tree fixes a clip that does not exist. **She therefore turns to face her direction of travel
whether locked on or not**, and lock-on only owns her facing while she is standing still. Lock-on
still frames the fight and still picks the target attacks commit toward.

Sourcing strafe and walk-back clips is the only real fix and would let lock-on hold her facing as the
design assumes. Tracked in *Known gaps*.

**Done when:** you can run around the arena, camera locked onto a dummy, camera behind her with the
dummy ahead, feet not sliding. *Met — mild residual slide on run, accepted for now.*

### Step 1.3 — Hitting things (Days 4–5)

- `CombatActor.cs` — shared base: `health`, `currentMove`, `isInvulnerable`, `TryExecute(MoveDefinition)`, `ApplyDamage(amount, sourceMove, hitDirection)`, `Die()`. Owns the normalized-time polling loop that opens/closes the hitbox and the i-frame window from the current `MoveDefinition`.
- `Hitbox.cs` — trigger collider parented to the katana GameObject (it follows the ParentConstraint automatically). Maintains a per-swing hit-set so one swing can't hit the same target twice.
- `Hurtbox.cs` — capsule on the actor, routes to its `CombatActor`.
- `PlayerCombat.cs` — input → `TryExecute`. Chain buffering: pressing attack inside `cancelWindowNormalized` queues `nextInChain`; pressing outside it does nothing (this is what stops mashing).
- Place `Humanoid_F_Katana.prefab` in the arena as a training dummy with a `CombatActor` and a hurtbox.

**Done when:** you can walk up, swing three times as a chain, and watch the dummy's health field drop by 8, 10, 14 in the inspector.

### Step 1.4 — Reactions, evasion, death (Days 6–7)

- `HitReaction.cs` — dot the hit direction against the actor's forward-right to pick `Hit_L` vs `Hit_R`. Cheap, and it makes hits read.
- Evade and Quick Shift with i-frames driven from the move asset. **Tune the two dodges against each other now, not later** — the doc is right that the Evade/Quick Shift distinction carries a disproportionate share of the game's depth, and it is a lot easier to feel that difference against a dummy than in a live fight.
- Death state: `Die` plays `K_Die`, disables input, disables colliders.
- The killing swing is the moment to prove the swap: put the cleaned `KatanaGirl_FullBody` variant in as the player, confirm every clip retargets, confirm the katana socket events still fire.

**Milestone (end of Day 7):** *You can walk up to a dummy, hit it, watch it flinch in the correct direction, dodge through nothing, and kill it.*

---

## Phase 2 — Make it a fight (Days 8–14, to 16 Sep)

### Step 2.1 — Posture (Days 8–9)

`PostureSystem.cs`, boss-only. Accumulate on hit, start decaying at **5.5/s** after 1.5 s of no contact, break at 100 → `CrossFadeInFixedTime(Stun)` for **3.1 s** with a ×2 damage multiplier. Both numbers are rescaled from the doc's 8/s and 2.5 s to match the measured 3.05 s chain — see *Measured frame data*. Keep the decay delay and rate serialized and public; they are the two dials that decide whether aggression is actually the correct defense, and you will move them twenty times.

**Done when:** sustained pressure breaks the dummy and poke-and-retreat visibly does not.

### Step 2.2 — Boss AI (Days 10–12) — HARD THREE-DAY BUDGET

`BossAI.cs`. Weighted random over range bands, nothing more. No behaviour tree, no utility framework, no package.

- Range bands: close < 2.5 m (slash chains), mid 2.5–5 m (Quick Shift F or Skill 2 as a lunge), far > 5 m (approach, or hold Special stance and walk).
- **The mandatory gap:** max 3 attacks, then a hard **1.0 s** neutral window (rescaled from the doc's 0.8 s along with everything else). Write this first, before the selection logic. In a game with no block button, this single rule is the difference between "hard" and "broken."
- Per-move cooldowns so she can't repeat the same read twice.
- Whiff punish: on detecting player recovery frames, ~30% chance to immediately attack.
- Patience timer: if the player hasn't closed within N seconds, she closes instead.

She reuses `CombatActor` and the same `MoveDefinition` assets. It is a mirror match — that is the point, and it halves the work.

**If day 12 ends and she isn't fun, stop anyway.** Move to telegraphs; a simple boss with good tells beats a clever one that lands on day 25.

### Step 2.3 — Telegraphs and first playtest (Days 13–14)

You cannot author new animation, so tells come from elsewhere, in this order of value:
1. **Distinct audio cue per move, on startup frame.** Cheapest, largest effect.
2. **Weapon trail that colours in during startup** (`TrailRenderer` on the blade tip, colour lerped over the startup window).
3. **Animator speed dip** in the first few frames of startup.

Then play the fight for real, with the full moveset unlocked. Write down what feels unfair; do not fix any of it yet.

**Milestone (end of Day 14):** *A fight you can genuinely lose, and with everything unlocked, genuinely win.*

---

## Phase 3 — The loop, and the feel (Days 15–21, to 23 Sep)

### Step 3.1 — Progression (Days 15–17)

`ProgressionManager.cs`, `DontDestroyOnLoad` singleton.

- Every `ApplyDamage` records the `MoveDefinition` that caused it. On death, the last one is the candidate.
- Resolve: map boss move → player equivalent (1:1, it's a mirror). If already unlocked, walk the ladder and grant the first locked entry. **If the candidate is a Special skill and the Draw is locked, grant the Draw instead.** Every death grants exactly one unlock while any remain.
- Persist to `PlayerPrefs` or a JSON in `Application.persistentDataPath` — 12 booleans, don't over-engineer it.
- Death screen: the death animation, an unlock card naming the move you just learned, one input to retry. **Budget: under two seconds from killing blow to next attempt.** Reload the scene, or better, reset actor state in place and skip the load entirely.

**Done when:** you can die five times in a row and each death hands you exactly one new move, in an order that reflects what actually killed you.

### Step 3.2 — Stance and specials (Days 18–19)

Draw/Sheathe via `Take`/`Put`, vulnerable throughout, switching the `Stance` bool and the active locomotion tree. The three Special skills as committed, high-posture moves.

### Step 3.3 — The juice pass (Days 20–21) — DO NOT CUT THIS

`HitFeedback.cs`: hitstop (freeze both actors 60–100 ms on connect, scaled by damage), Cinemachine Impulse shake, impact VFX at the hitbox contact point, impact audio, weapon trails on player swings too.

These two days do more for "is this fun" than every system above them. They are scheduled before tuning and before the HUD deliberately. If week three overruns, cut stance polish — not this.

**Milestone (end of Day 21):** *The complete death → unlock → retry loop, and it feels good to hit her.*

---

## Phase 4 — Tune until it's fair, then ship (Days 22–30, to 2 Oct)

- **Days 22–24 — Balance the ten-death arc.** Play start to finish, repeatedly, from a wiped save. Tune **boss damage first** — it is the fastest lever on arc length. Target: 2–4 minutes on a winning attempt, roughly one death per unlock for a competent player.
- **Days 25–26 — HUD and audio.** Player health, boss health, boss posture, learned-moves list. Full audio pass.
- **Days 27–28 — Edge cases.** Death during stun. Unlock granted mid-animation. Camera on target loss or target death. Boss killed during her own attack. Player dying to a move that's already unlocked with the ladder exhausted. Two hits landing on the same frame.
- **Days 29–30 (1–2 Oct) — Build, test the build, write-up.** Test the *build*, not the editor — animation events and `Resources` lookups behave differently there.

**Buffer: 3 Oct, one day.**

---

## Critical files

New, all under `Assets/_Game/Scripts/`:

`MoveDefinition.cs` · `CombatActor.cs` · `PostureSystem.cs` · `PlayerLocomotion.cs` · `PlayerCombat.cs` · `BossAI.cs` · `Hitbox.cs` · `Hurtbox.cs` · `HitReaction.cs` · `ProgressionManager.cs` · `HitFeedback.cs` · `Editor/ClipReportWindow.cs`

Dropped from the doc's list: `LockOnCamera.cs` (Cinemachine 3 replaces it), `AnimationEventRelay.cs` (normalized-time polling replaces it; add back only if you want VFX events later).

Existing assets to reuse, not rewrite:
- [Character_Weapon_Controller.cs](Assets/CombatGirlsCharacterPack/Biperworks_Tools/CombatGirls_Weapon_Control/Character_Weapon_Controller.cs) — keep on both actors, handles the baked `SwitchSocket` events.
- [Dummy_Event.cs](Assets/CombatGirlsCharacterPack/Biperworks_Tools/CombatGirls_Weapon_Control/Dummy_Event.cs) — swallows `SwitchSocket` on anything that shouldn't react.
- [Humanoid_F_Katana.prefab](Assets/CombatGirlsCharacterPack/Katana_Girl/Prefab/Humanoid_F_Katana.prefab) — week-1 dummy and early boss stand-in.
- [InputSystem_Actions.inputactions](Assets/InputSystem_Actions.inputactions) — extend, don't replace.
- [School_Katana_Controller.controller](Assets/CombatGirlsCharacterPack/Katana_Girl/Animations/School_Katana_Controller.controller) — leave alone; useful as a clip previewer.

---

## Verification

Per phase, the acceptance test is the milestone above — run it in Play mode before moving on. Beyond that:

- **Phase 0:** arena scene opens, zero console errors, clip-timing CSV exists on disk.
- **Phase 1:** dummy takes 8 / 10 / 14 from a three-hit chain; dodging through a scripted dummy attack takes zero damage; `K_Die` plays and input locks.
- **Phase 2:** log boss attack counts and confirm no sequence exceeds 3 without an 0.8 s gap. Log posture per second and confirm a 4-hit-per-5-seconds poke pattern never reaches 100.
- **Phase 3:** wipe the save, die ten times deliberately to different moves, assert the unlock log grants exactly one per death and never grants a Special before the Draw.
- **Phase 4:** three full playthroughs from a wiped save in a **standalone build**, each recorded end to end. If any run exceeds 5 minutes on the winning attempt or the arc needs more than ~20 deaths, drop boss damage and re-run.

No unit tests. In a 30-day solo action game the test harness is you, playing it, every day.

---

## Standing risks, revised

| Risk | Mitigation |
|---|---|
| Boss AI eats the schedule | Hard stop day 12. Weighted random over range bands only. |
| ~~Speed multipliers make the specials look like fast-forward~~ | **Closed 4 Sep.** Measured: the whole table needed 2.3–3.9×. Retargeted to a global 2.0×, with the stun window and posture decay rescaled to match. |
| Gameplay components land on a vendor asset | The player must be a Prefab Variant in `_Game/Prefabs/`. A pack reimport destroys components added to the vendor prefab. |
| The fight now runs 45% slower than designed | Deliberate — the animations are heavy. Watch it in the day 13–14 playtest; if it drags, push individual moves toward 2.5× rather than rescaling globally again. |

---

## Known gaps

**No strafe or walk-back animations.** The pack has `Idle`, `Walk`, `Run` only. Consequences:

- Lock-on cannot hold her facing at the target while she moves; she turns to face travel instead.
- Backing away from the boss plays a forward walk. Visible, and it matters because backing off is
  a core defensive verb in a game with no block button.
- Mild foot slide remains on the run cycle.

Sourcing or authoring **Walk_Back, Strafe_L, Strafe_R** (and ideally the run equivalents) would close
all three. That converts the locomotion tree from 1D on `Speed` to 2D on `MoveX`/`MoveY` — both
parameters already exist in `KG_Combat` and are already written by `PlayerLocomotion`, so the change
is confined to the blend tree plus deleting the facing special-case in `PlayerLocomotion.UpdateRotation`.

Scheduled by the author for the weekend of 5–6 Sep. Not on the critical path: everything downstream
works without it.
| Feel work gets cut | Scheduled days 20–21, ahead of HUD and tuning. It is the product, not polish. |
| ~~Toon shader fights URP 17~~ | **Closed 4 Sep.** One-line HLSL patch; see *Vendor modifications*. Re-opens on any pack reimport. |
| Vendor patches lost to a reimport | Three unrecorded hand-edits documented above. Commit them; a reimport silently reverts all three. |
| Root motion vs. CharacterController discovered late | Solved on day 3, before any attack code exists. |
| The buffer is one day, not three | Do not spend it in week one. If you're behind at day 21, cut the Special stance (steps 3.2) before cutting juice or tuning. |

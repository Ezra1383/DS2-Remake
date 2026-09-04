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
| Clip lengths are wildly uneven: `Attack1` = 61 frames, `Sp_Skill3` = **270 frames** against a 1.45 s target. | The speed multiplier is doing extreme work on the specials. Measure before you trust any number in the frame-data table. |
| `Humanoid_F_Katana.prefab` is a light (364-line) bot body already holding the katana, with `Character_Weapon_Controller`, no Magica dependency, no toon-shader dependency, same humanoid avatar. | **This is the week-1 training dummy and the early boss stand-in.** Swap in the real girl once feel is proven. |
| No Cinemachine in the manifest. | Install Cinemachine 3 (decided). Replaces hand-written `LockOnCamera` and gives Impulse for the week-3 shake pass. |
| `activeInputHandler: 1` — new Input System only. `Assets/InputSystem_Actions.inputactions` is the Unity 6 template (Player map: Move, Look, Attack, Sprint, Jump, Crouch, Interact). | Extend that asset; don't author a second one. |

### One architecture change to the doc

The doc puts hitbox open/close on animation events. **Don't.** Events are baked into the FBX `.meta` alongside the existing `SwitchSocket` events — re-adding yours after any reimport is a footgun, and retuning means leaving the code editor.

Instead: `MoveDefinition` stores hitbox open/close as **normalized time**, and `CombatActor` polls `stateInfo.normalizedTime` each frame to open and close the window. Retuning a hitbox becomes editing a float on a ScriptableObject with the game running. This directly defuses the doc's own "clip timings don't match the targets" risk, and it means `AnimationEventRelay.cs` is only needed if you later want VFX events.

Same reasoning for the animator: **no trigger-per-move.** The demo `School_Katana_Controller` has 38 states and one trigger each; that pattern does not scale to data-driven moves. Author `KG_Combat.controller` with a locomotion blend tree, a Special-stance blend tree, and every move as an **isolated state with no inbound transitions**. Enter them with `Animator.CrossFadeInFixedTime(stateHash, blend)`. `MoveDefinition` stores the state name; hash it on enable.

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
- [ ] **Install Cinemachine 3** via Package Manager.
- [ ] **Clean the prefab.** Open `Katana_Girl/Prefab/KatanaGirl_FullBody.prefab`, delete the Magica Cloth capsule-collider GameObjects and the missing-script components. Save as your own variant under `Assets/_Game/Prefabs/` — leave the pack's original untouched.
- [ ] **Create the gameplay folder.** `Assets/_Game/{Scripts,Prefabs,Moves,Animation,Scenes,VFX,Audio}`. Nothing you write goes inside `CombatGirlsCharacterPack/` — that folder stays read-only so the demo viewer keeps working as a clip previewer.
- [ ] **Write `ClipReportWindow.cs`** (`Assets/_Game/Scripts/Editor/`). An editor menu item that walks every clip in the pack and logs, as CSV: clip name, `clip.length`, `clip.frameRate`, `clip.hasRootCurves`, root-motion total XZ displacement, and every existing animation event with its time. **This is the single highest-value hour of the project** — it produces the real numbers that the entire frame-data table is guessing at, and the displacement column tells you how far each attack and dodge actually travels.
- [ ] **New scene** `Assets/_Game/Scenes/Arena.unity`: flat plane, boundary, one directional light, the pack's `Dome` prefab as a cheap skybox. Add it to build settings.

**Done when:** the arena scene opens with a correctly-shaded Katana Girl standing on a plane, no console errors, and you have a CSV of real clip timings on disk.

---

## Phase 1 — Make one sword hit one thing (Days 2–7, to 9 Sep)

### Step 1.1 — Move data and the animator (Day 2 evening – Day 3)

- `MoveDefinition.cs` — ScriptableObject: `stateName`, `speedMultiplier`, `damage`, `postureDamage`, `hitboxOpenNormalized`, `hitboxCloseNormalized`, `cancelWindowNormalized`, `iframeStartNormalized`, `iframeEndNormalized`, `useRootMotion`, `nextInChain` (a `MoveDefinition` reference), `moveId` (enum, used by progression).
- Author one asset per move in `Assets/_Game/Moves/`. Fill `speedMultiplier` from the Phase 0 CSV: `speedMultiplier = measuredLength / targetLength`. Expect ugly numbers on the specials — `Sp_Skill3` may need 3× or more. **If a multiplier exceeds ~2.5× the animation will read as sped-up video.** Raise that move's target duration in the design instead of forcing the number; the frame-data table serves the game, not the reverse.
- `KG_Combat.controller` in `Assets/_Game/Animation/`: Base layer with a Normal locomotion 1D blend tree (`Idle → Walk → Run` on a `Speed` float), a Special locomotion blend tree, and isolated states for the 3 slashes, Evade, 4 Quick Shifts, Take, Put, 3 Skills, Hit_L, Hit_R, Stun, Die. Parameters: `Speed`, `MoveX`, `MoveY`, `Stance` (bool), and nothing else — no per-move triggers.

**Done when:** you can select any move asset and see its state play at the intended duration in the animator preview.

### Step 1.2 — Locomotion and camera (Days 3–4)

- Player root = the character prefab root itself (the Animator lives there; a separate parent just fights root motion). Add `CharacterController`, `PlayerLocomotion`, `PlayerCombat`, `CombatActor`.
- **Root motion reconciliation is the day-3 trap.** Implement `OnAnimatorMove()`: when the current move has `useRootMotion`, feed `characterController.Move(animator.deltaPosition)`; otherwise apply scripted velocity. Get this working before you write a single attack.
- Cinemachine: a `CinemachineCamera` with an Orbital follow on the player and a `CinemachineTargetGroup` (player + boss, weighted) for lock-on framing. Toggle lock-on by swapping the camera's LookAt between the group and a free-look target. Add `CinemachineImpulseSource` on the player now — you'll use it in week 3 and wiring it later means touching prefabs again.
- Extend `InputSystem_Actions`: add `Dodge`, `LockOn`, `HeavyAttack`, `Stance`. Bind `Attack`/`Dodge` to the existing template actions where they already fit.

**Done when:** you can run around the arena, camera-locked onto a dummy, strafing correctly, feet not sliding.

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

`PostureSystem.cs`, boss-only. Accumulate on hit, start decaying at 8/s after 1.5 s of no contact, break at 100 → `CrossFadeInFixedTime(Stun)` for 2.5 s with a ×2 damage multiplier. Keep the decay delay and rate serialized and public — they are the two dials that decide whether aggression is actually the correct defense, and you will move them twenty times.

**Done when:** sustained pressure breaks the dummy and poke-and-retreat visibly does not.

### Step 2.2 — Boss AI (Days 10–12) — HARD THREE-DAY BUDGET

`BossAI.cs`. Weighted random over range bands, nothing more. No behaviour tree, no utility framework, no package.

- Range bands: close < 2.5 m (slash chains), mid 2.5–5 m (Quick Shift F or Skill 2 as a lunge), far > 5 m (approach, or hold Special stance and walk).
- **The mandatory gap:** max 3 attacks, then a hard 0.8 s neutral window. Write this first, before the selection logic. In a game with no block button, this single rule is the difference between "hard" and "broken."
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
| Speed multipliers make the specials look like fast-forward | Measure day 2. If a multiplier exceeds ~2.5×, change the design target, not the animation. |
| Feel work gets cut | Scheduled days 20–21, ahead of HUD and tuning. It is the product, not polish. |
| ~~Toon shader fights URP 17~~ | **Closed 4 Sep.** One-line HLSL patch; see *Vendor modifications*. Re-opens on any pack reimport. |
| Vendor patches lost to a reimport | Three unrecorded hand-edits documented above. Commit them; a reimport silently reverts all three. |
| Root motion vs. CharacterController discovered late | Solved on day 3, before any attack code exists. |
| The buffer is one day, not three | Do not spend it in week one. If you're behind at day 21, cut the Special stance (steps 3.2) before cutting juice or tuning. |

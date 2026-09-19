# Ten Deaths to the Mirror — Build Plan

> **DEADLINE CHANGED 16 SEP 2026.** The instructor cut the project to **five days from 16 Sep —
> ship 20 September 2026**. Day numbers below still refer to the original 30-day schedule; Phase 4
> has been rewritten for the real one. Phases 0–3 are complete.
>
> **STATUS 19 SEP:** four of those five days went to a single bug — Slash 2 never damaging the
> boss — which is now fixed. The audio pass, the balance pass and the wiped-save playtest were all
> consumed by it. See `project-notes.md` §7 for the one-day plan that replaces the rest of this.

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

**2.0x was tried in play and rejected — it read as absurdly fast.** The "New target" column above
is therefore superseded; see *Tempo, trim and the blade* below for the settled 1.4x. The useful part
of this table is the **Measured** column, which is fact.

### Derived numbers still owed

The stun window and posture decay both scale with tempo, and the trims change the chain length
again. **Compute these against the final tempo when building Step 2.1**, not from this table:

- Stun window must equal roughly one full three-slash chain, per the design doc's own reasoning.
- Posture decay must stay proportional to posture-per-second landed, or the meter cannot be filled
  and "aggression is the correct defense" stops being true.
- The boss's mandatory neutral gap scales with everything else (0.8 s at the doc's original pace).

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

### Step 2.1 — Posture (Days 8–9) — **DONE 5 Sep**

`PostureSystem.cs`, boss-only. Accumulate on hit, start decaying at **5.5/s** after 1.5 s of no contact, break at 100 → `CrossFadeInFixedTime(Stun)` for **3.1 s** with a ×2 damage multiplier. Both numbers are rescaled from the doc's 8/s and 2.5 s to match the measured 3.05 s chain — see *Measured frame data*. Keep the decay delay and rate serialized and public; they are the two dials that decide whether aggression is actually the correct defense, and you will move them twenty times.

**Done when:** sustained pressure breaks the dummy and poke-and-retreat visibly does not.

### Step 2.2 — Boss AI (Days 10–12) — **v1 done 5 Sep, REWRITTEN 11 Sep**

`BossAI.cs` v1 was weighted random over range bands. It shipped in a day and it did not work:
in play she read as **passive and unreadable**. Tuning its numbers stopped producing improvement,
so the architecture was replaced. v1 is kept in the repo until the rewrite is validated in play.

**Why v1 failed — measured, not guessed:**

| Symptom | Cause | Evidence |
|---|---|---|
| Passive | Boss moves all ran at `moveEnd = 1` (full draw-cut-sheathe tail) plus a fixed 0.4 s beat | A Slash1→2→3 chain ran **5.18 s** containing **0.72 s** of live hitbox — 14% threat density, 86% dead air |
| Passive | Her decision timer waited for the whole clip, so she never used her own cancel windows | Slash1's `cancelWindow` is 0.467 (0.68 s). The player chains there; she took 1.85 s per link |
| Shuffles | The reactive dodge rolled its chance **every frame** of the player's start-up | Slash1 start-up ≈ 26 frames → P(dodge) = 1 − 0.65²⁶ ≈ **100%**, not the 35% on the inspector. She evaded nearly everything, then walked back in from 3 m |
| Unreadable | Zero facing tracking during wind-up (`ActorLocomotion.UpdateRotation` returned early on `IsBusy`) | Committed root-motion swings whiffed against any lateral movement; exchanges resolved to nothing |
| Unreadable | A failed chain roll fell through to the same weighted table, which re-picked the opener | "New attack" and "combo continuation" looked identical — no pattern to learn |

**v2 — `BossBrain.cs`, four layers.** FromSoftware's goal stack flattened into something one
person can finish: a *phrase* is what they push as sub-goals, a *reaction* is what they bubble up
as an interrupt.

```
3  Reactions    rare, delayed, one roll per event, may start a phrase
2  Phrase       an authored attack string — the unit the player learns
1  Pacing       whether she may act, and what she owes the player
0  Positioning  where she stands when she is not attacking
```

**The central change: randomness moved up one level.** She no longer re-rolls a move every time
she is free. She rolls for a **`BossPhrase`** — an authored string with fixed branch points — and
then performs it as written. Which phrase you get is unpredictable; what a phrase does once it
starts is not. That is what makes a pattern learnable while keeping the fight non-deterministic.

Five phrases, generated by `Tools ▸ DS2 ▸ Build Boss Phrases`:

| Phrase | Steps | Range | Teaches |
|---|---|---|---|
| **Pressure** | Slash1 → Slash2 → Slash3, linked in the cancel window, continue 1.0 / 0.55 | 0–2.9 | The baseline. Dodge three, punish the tail. |
| **Feint** | Slash1, **0.85 s hole**, Slash2 → Slash3 | 0–2.9 | Identical opening frames, different rhythm — the contradiction that breaks autopilot. |
| **Gap closer** | QuickShiftF → Slash1 → Slash2 | 2.4–5.0 | Distance is not safety. |
| **Committed lunge** | Skill2 (5.18 m), full sheathe | 2.6–5.5 | Big tell, big punish window. |
| **Punish** | Slash1 → Skill1 | 0–3.0 | Weight 0 — reachable *only* by catching the player in recovery. |

**The three rules, as they now stand:**

- **RULE 1, the mandatory gap.** A **4 s pressure budget** (time, not attack count — a sidestep
  and a heavy skill are not the same debt) *and* a hard cap of **3 attacks**, whichever comes
  first, then a **0.85 s** neutral window. She **holds** through it rather than retreating:
  backing off turned the opening she had just promised into dead air plus a slow walk back in,
  which was most of what made her feel passive. `retreatDuringGap` is still there if it reads
  better the other way.
- **RULE 2, telegraphs.** Now partly free. Boss moves get real trims (the player's, +0.06), and
  the full draw-cut-sheathe cycle is spent only on a phrase step with `endOverride = 1`. Blanket
  `moveEnd = 1` made every move a "punish me" window, which means none of them were.
- **RULE 3, reactions.** 30% evade on a read swing, 35% punish on recovery — each rolled
  **once per swing**, in `BossPerception`, behind a **0.25 s ± 0.08 reaction delay**. Reacting on
  the frame a swing starts is input reading; players feel it as the game cheating. Separate roll
  tokens for start-up and recovery, or a failed dodge roll silently cancels the punish.

**Tracking is now a per-move property** (`trackUntil`, `trackDegreesPerSecond` on
`MoveDefinition`, applied by `ActorLocomotion`). She turns toward the player through the wind-up
and locks just before the hitbox opens. Player moves are at **0** — their swings point where the
stick pointed. This is FromSoft's spinning / non-spinning attack split.

**`BossDebugHUD.cs` is the instrument this all gets tuned against.** Threat density — the
fraction of the last 10 s with a boss hitbox open — is the number that means "passive". v1
measured ≈14%; after the 12 Sep aggression pass it **measures 16%, very consistently**.

**The 20–25% target was wrong and has been retired** (the HUD now warns at 0.15). It came from
generic melee-pacing advice and is not reachable in this game. Two reasons:

- **There is a hard ceiling in the clips.** The Pressure phrase at maximum compression — every
  link in the cancel window, no gap, no stun, no travel — is **0.704 s of hitbox in 2.688 s, or
  26%**. Slash 1's hitbox opens 0.436 s into a 1.452 s move: 30% of every attack is wind-up, and
  that wind-up *is* the telegraph. The mandatory opening and posture stuns come off the top of
  that ceiling, so 16% is about 62% of theoretical maximum.
- **The metric punishes the design working.** Time she spends stunned by posture pressure counts
  against her, as do her reactive dodges — but those are the fight doing exactly what it should.

**Watch `worst quiet`, not density.** Longest silence inside a phrase went **1.2 s → 0.34 s**
across the rewrite, and that is what actually changed how she feels — the composition of the dead
air moved far more than the total did, which is why 14% → 16% understates the difference so badly.
Treat density as a regression alarm, not a goal. Longest quiet inside a phrase
drops from ~1.2 s to **0.34 s**, because she finally links in the cancel window like the player
does.

**Aggression pass, 12 Sep.** Verdict after the first play was "solid, about 5% more aggressive."
Threat density went ≈18% → ≈22% by shortening the neutral gap to 0.85 s, trimming the common
finishers from a full sheathe to `endOverride 0.85`, standing 2.45 m out instead of 2.6, dropping
the evade rate to 25% (every evade is ~0.7 s of zero threat plus a walk back in), refunding
pressure at 2.2/s, and losing patience at 3.2 s. **`maxAttacksBeforeGap` stayed at 3** — that is
the promise to the player, not a dial.

The full sheathe is still spent on **Committed lunge** and **Punish**, so the unmistakable
"punish me now" recovery now marks her two heaviest commitments rather than every move she makes.

**Where the aggression numbers live:** `Tuning` in `Editor/BossWiring.cs`, not the inspector.
`BossBrain`'s fields are `[SerializeField]`, so the values that actually run are the ones baked on
the prefab — editing a default in `BossBrain.cs` does nothing to a component that already exists.
Same contract as the move `Specs` table: **re-running the menu item overwrites hand edits**, so
anything worth keeping goes in that table.

Run `Tools ▸ DS2 ▸ Rebuild Boss (moves, phrases, wiring)` after any change — the pipeline fails
quietly otherwise, since phrases reference move assets and the wiring references phrases.

**If day 12 ends and she isn't fun, stop anyway.** Move to telegraphs; a simple boss with good
tells beats a clever one that lands on day 25.

### Step 2.3 — Telegraphs and first playtest (Days 13–14)

You cannot author new animation, so tells come from elsewhere, in this order of value:
1. **Distinct audio cue per move, on startup frame.** Cheapest, largest effect. **Still open** — the system is built and silent; the clips are the gap.
2. ~~**Weapon trail that colours in during startup**~~ — **DONE 16 Sep.** `WeaponTrail.cs`, boss only: her blade ramps cold→hot across the wind-up and snaps to the swing colour as the hitbox opens. The player deliberately does not get it, on the mirror-match rule.
3. **Animator speed dip** in the first few frames of startup. Still open, and now the cheapest thing left here.

Then play the fight for real, with the full moveset unlocked. Write down what feels unfair; do not fix any of it yet.

**Milestone (end of Day 14):** *A fight you can genuinely lose, and with everything unlocked, genuinely win.*

---

## Phase 3 — The loop, and the feel (Days 15–21, to 23 Sep)

### Step 3.1 — Progression (Days 15–17) — **DONE 15 Sep (Day 13), two days early**

`ProgressionManager.cs` + `DeathScreen.cs`. The ladder is the design doc's, unchanged:
Slash2 → QuickShiftB → Slash3 → QuickShiftF → **Draw** → Skill1 → QuickShiftL → Skill2 →
QuickShiftR → Skill3. Starting kit is **Slash 1 + Evade + Parry**.

All four resolution rules live in `Resolve()`, kept as a pure function so it can be reasoned about
without mutating anything. Two things that bit:

- **A move in neither `StartingKit` nor `Ladder` is never unlocked**, so its button silently does
  nothing. Parry shipped that way and looked like a broken input action.
- **Duplicate-singleton handling must destroy the COMPONENT, not the GameObject.** A second
  `ProgressionManager` had landed on the arena root; `Destroy(gameObject)` took the floor and all
  four walls with it, about half the time, because Awake order between two instances is undefined.

The retry resets **in place** rather than reloading — nothing is destroyed, so every reference and
event subscription survives, and the two-second budget is met comfortably.

`ProgressionManager.cs`, `DontDestroyOnLoad` singleton.

- Every `ApplyDamage` records the `MoveDefinition` that caused it. On death, the last one is the candidate.
- Resolve: map boss move → player equivalent (1:1, it's a mirror). If already unlocked, walk the ladder and grant the first locked entry. **If the candidate is a Special skill and the Draw is locked, grant the Draw instead.** Every death grants exactly one unlock while any remain.
- Persist to `PlayerPrefs` or a JSON in `Application.persistentDataPath` — 12 booleans, don't over-engineer it.
- Death screen: the death animation, an unlock card naming the move you just learned, one input to retry. **Budget: under two seconds from killing blow to next attempt.** Reload the scene, or better, reset actor state in place and skip the load entirely.

**Done when:** you can die five times in a row and each death hands you exactly one new move, in an order that reflects what actually killed you.

### Step 3.2 — Stance and specials (Days 18–19) — **MOSTLY DONE 15 Sep (Day 13)**

Draw/Sheathe via `Take`/`Put`, vulnerable throughout, switching the `Stance` bool and the active locomotion tree. The three Special skills as committed, high-posture moves.

**Reachable now:** hold `Special` (Left Ctrl / LB) + Attack / Dodge / Stance fires Skill 1 / 2 / 3;
tapping Stance alone toggles the persistent stance. Before this, `PlayerCombat` held no reference to
any Skill, Draw or Sheathe asset — half the moveset could not be reached whatever the player pressed.

**What `Take` and `Put` actually do** — worth knowing before touching this again. They move the
**scabbard**, not the blade: the blade sits in `Katana_Close` throughout both. `Put` stows the
scabbard on her body, `Take` returns it to her left hand. `Idle` and `Sp_Idle` are socket-identical
(sheathed, scabbard in left hand), and **every attack in the pack is a full draw-cut-sheathe** —
even `Attack1` starts sheathed and draws 0.075 s in.

`WeaponStance` forces the blade into her right hand every frame so she does not look like she is
constantly sheathing. That is why Specials used to *pop*: `WeaponStance` stands aside for a move
with no `endWeaponSocket`, and the clip's frame-0 event slammed the blade back into the scabbard
mid-move. **There is no blade-sheathing animation in the pack**, so that switch cannot be animated —
only moved somewhere the player is not watching a swing. Holding Special now sheathes her into the
iai stance first, and the Special begins from where its animation expects to begin.

**Still open:** the drawn→sheathed switch is instant. A short VFX or sound would cover it; the
alternative (playing `Put` first, ~0.8 s) would mean waiting before every Special.

### Step 3.2b — Parry (added 15 Sep, a deliberate departure from the design doc)

`combat-design.html` calls "no block, no parry, no shield" **the single most important fact in the
project** and builds the posture economy on it. Overruled after playtesting: Evade already read as
defending herself, and a parry now earns the **same** payoff as a posture break — 2 s stun, ×2
damage — on **its own button** (`F` / RMB / RB), leaving Evade untouched.

- **No parry animation exists.** `Move_Parry` points at the `Evade` state with different data
  (tempo 2.6, `moveEnd` 0.55) so it reads as a defensive snap. Two assets on one animator state is
  what `MoveDefinition` is for; the boss already does it.
- **Window 0.02–0.38 normalized ≈ 0.20 s (~12 frames)**, with **no i-frames** outside it and ~0.10 s
  of defenceless recovery. That is the entire cost of reaching for it instead of dodging.
- `parryStart` / `parryEnd` on `Move_Parry.asset` are polled live — **drag them while playing**,
  then copy the result into the `Specs` table before `Build Move Assets` overwrites them.
- **`CombatActor.Stagger(seconds, damageMultiplier)` is now the single owner of the stunned state.**
  Both the posture break and the parry route through it; `PostureSystem` no longer keeps its own
  stun clock. Two systems owning that is how an actor ends up frozen forever.

**The number to watch:** a parry reaches the same opening that four seconds of sustained pressure
buys. If parrying becomes strictly better than pressuring, the posture meter stops mattering and
"aggression is the correct defense" quietly stops being true. The lever is `parryStagger` /
`parryStaggerDamageMultiplier` on `Hitbox` — keep it hard to *hit*, shrink the payoff, rather than
narrowing the window.

### Step 3.3 — The juice pass (Days 20–21) — **PULLED FORWARD, built 12 Sep (Day 10)**

Brought forward from Days 20–21 because it is not polish: with no impact feedback a landed hit and
a whiff look identical, so every further judgement about the boss's spacing was being made through
noise. It is an instrument for tuning the AI, not a coat of paint on top of it.

**The research that set the priorities.** Lin et al. (2022) ranked 44 action games on "impact feel"
by NLP over Steam reviews, then compared the top eight against the bottom eight across a 19-feature
framework. Three features separated them — **hit stop, sound coherence, camera control** — and
*"a lack of dedicated design on one of these three features may ruin players' impact feel."*
Particles, flashes and trails are in the framework but are **not** what divided good from bad. The
build order follows that finding rather than intuition.

Files: `Feel/HitFeedback.cs` (the conductor), `Feel/ImpactAudio.cs`, `Feel/HitFlash.cs`,
`Editor/FeelWiring.cs`. Run **`Tools ▸ DS2 ▸ Wire Feel`**.

**Numbers.** Hit stop 0.08 / 0.11 / 0.16 s by damage tier — deliberately below the fighting-game
references (Guilty Gear Xrd uses 7 and 10 frames), because those are 2D games where the freeze is
far more visible. Posture break gets 0.30 s frozen then 0.55 s at `timeScale 0.35`. Death gets
0.20 s then 1.2 s at 0.30.

**Three things worth not forgetting:**

- **The fatal trap.** `Time.timeScale = 0` also makes `Time.deltaTime` 0, so a hit-stop timer on
  scaled time never advances and the game freezes **permanently**. Everything that counts down
  through a freeze runs on `unscaledDeltaTime`. Use `timeScale`, never `animator.speed` —
  `CombatActor.moveTimer` and every normalized window are on scaled time, as is `BossBrain`.
- **Hit stop lengthens the cancel window**, because `moveTimer` stops with everything else. Street
  Fighter 2 relied on exactly this to keep combo timing consistent. Feature, not bug.
- **Cinemachine Impulse does nothing without a `CinemachineImpulseListener` on the camera.** Sources
  broadcast, listeners react, and with no listener every impulse is discarded in total silence —
  no error, no warning. `FeelWiring` adds it; the Arena had none.

#### The mirror-match rule — the camera belongs to the victim

Both fighters share clips, moves and silhouette, so symmetric feedback turns exchanges into mush.
Most juice advice assumes visually distinct enemies and does not cover this.

| | You hit her | She hits you |
|---|---|---|
| Hit stop | full | ×0.8 |
| Camera | **small** impulse (trauma 0.25) | **large** impulse (trauma 0.6) |
| Audio | brighter, sharper | duller, heavier |

Big shake on your own hits is exhausting across a ten-death arc; the camera reacting hard when
*you* are hit is the fastest channel for telling the two directions apart.

Camera shake is Cinemachine Impulse, which already implements what Eiserloh's GDC talk prescribes
(6D Perlin noise rather than random, envelope, spatial falloff). Applied on top: magnitude scales
as **trauma²**, not linearly — that curve is why escalation reads.

#### Posture made audible

**Impact pitch rises as her posture fills** (`posturePitchAtFull`, 1.35 by default). The meter is
the design's whole thesis — "aggression is the correct defense" — and until the Week 4 HUD the
player has no way to perceive it filling. This teaches it through feel rather than through a number,
and costs one `Mathf.Lerp`.

#### Audio shopping list — ~15 clips

Every slot is optional and silent when empty; nothing is blocked waiting for these. **Mono WAV,
44.1 kHz, trimmed hard to the transient** so the attack punches through. Variations matter more
than quality here: one clip on every slash fatigues within a minute.

| Slot | Count | Notes |
|---|---|---|
| Swing whoosh — light / heavy / special | 3 × 2 | On `MoveDefinition.swingSound`. **Also the Step 2.3 telegraph** |
| Impact — normal | 3 | Sharp transient + a little low end |
| Impact — heavy/special | 2 | Longer tail |
| Dodge whoosh | 2 | Evade and Quick Shift |
| Passed through i-frames | 1 | The player's confirmation their dodge worked |
| Posture break | 1 | Unmistakable, used nowhere else — glass, bell, metal snap |
| Posture recover | 1 | |
| Death | 1 | |

`swingSound` is normalized (`swingSoundNormalized`, default 0.12) for the same reason the hitbox
windows are: the global 1.4× tempo would slide a fixed offset out of sync. **This closes most of
Step 2.3 as a side effect** — the build plan calls a distinct audio cue per move the cheapest,
largest-effect telegraph available, and it is the same mechanism.

#### Not done yet

- ~~**Damage vignette** for the player. Needs a Canvas, and there is none until the Week 4 HUD.~~
  **Done 16 Sep** with the HUD — and it never needed a Canvas. `CombatHUD` draws it in OnGUI from
  a 64×64 radial texture generated at runtime, so the player's half of the mirror rule is in.
- ~~**Impact VFX and weapon trails.** `HitFeedback` has the particle slots and spawns at the real
  contact point; no particle assets exist yet.~~ **Done 16 Sep** — `Tools ▸ DS2 ▸ Build VFX`
  generates the textures procedurally (the pack ships no particle art), builds additive URP
  Shuriken effects for normal / heavy / **parry**, and puts a trail on the blade. The parry had
  been spawning nothing at all. See `project-notes.md` §7.

If week three overruns, cut stance polish — not this.

**Milestone (end of Day 21):** *The complete death → unlock → retry loop, and it feels good to hit her.*

---

## Phase 4 — RESCOPED 16 Sep: five days, not nine (16–20 Sep)

**The instructor cut the deadline on 16 Sep to five days from that date.** The original Days 22–30
below are preserved at the end of this section for the record, but they are not the plan any more.

The saving grace is that Phases 0–3 are all complete: the juice pass arrived on Day 10 instead of
Day 21, progression on Day 13 instead of Day 17, and the **HUD on Day 14 instead of Days 25–26**.
What is left is triage.

- **Day 14 (16 Sep) — HUD. DONE.** `UI/CombatHUD.cs` — player health, boss health, boss posture,
  learned moves, damage vignette. IMGUI rather than a Canvas; see `project-notes.md` §7 for why,
  and for the one thing that bought: the vignette is no longer blocked.
- **Day 14 (16 Sep) — Impact VFX and weapon trails. DONE**, pulled forward from the cut list.
  `WeaponTrail.cs` + `Editor/VfxWiring.cs`. The trail half doubles as the Step 2.3 telegraph.
- **Day 14 (16 Sep) — Player attack tracking. DONE**, out of order: the first playtest found the
  player could not reliably hit the boss. Not the missing strafe clips — `trackDegreesPerSecond`
  was `forBoss ? bossTrack : 0f`, so she corrected at 240 deg/s mid-wind-up and the player at 0.
  Fixed in the spec table; **re-run `Build Move Assets`**. See `project-notes.md` §7.
- **Days 15–17 (17–19 Sep) — WHAT ACTUALLY HAPPENED: one bug.** Slash 2 never damaged the boss.
  Four wrong diagnoses (reactive dodge, blade tunnelling, `OnTriggerEnter` semantics, window too
  late) before the cause was found: the window sat past Attack2's contact point. Fixed at
  `0.080-0.478`. Along the way it produced a swept-overlap hitbox, per-move window logging, the
  dodge backstep and evade feedback — all real improvements, none of them the bug.

  **The planned playtest, audio pass and balance pass did not happen.** They are not "behind";
  they are cut unless tomorrow makes room.
- **Day 18 (20 Sep) — Ship.** Build FIRST, then one wiped-save run, then whatever fits. Full plan
  in `project-notes.md` §7 "Start here".

**Buffer: none.** Cut in this order if a day overruns: edge cases, then balance depth, then the
number of audio variations (one clip per slot still beats silence). **Do not cut the build day** —
an editor-only submission is the one failure mode that loses everything at once.

### What this replaces (original Days 22–30, to 2 Oct)

- **Days 22–24 — Balance the ten-death arc.** Play start to finish, repeatedly, from a wiped save. Tune **boss damage first** — it is the fastest lever on arc length. Target: 2–4 minutes on a winning attempt, roughly one death per unlock for a competent player.
- **Days 25–26 — HUD and audio.** Player health, boss health, boss posture, learned-moves list. Full audio pass.
- **Days 27–28 — Edge cases.** Death during stun. Unlock granted mid-animation. Camera on target loss or target death. Boss killed during her own attack. Player dying to a move that's already unlocked with the ladder exhausted. Two hits landing on the same frame.
- **Days 29–30 (1–2 Oct) — Build, test the build, write-up.** Test the *build*, not the editor — animation events and `Resources` lookups behave differently there.

**Buffer was: 3 Oct, one day.**

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
| ~~Speed multipliers make the specials look like fast-forward~~ | **Closed 4 Sep.** Measured, then tuned in play: settled at a global **1.4×**, one constant in `CombatSetupTools.cs`. |
| Gameplay components land on a vendor asset | The player must be a Prefab Variant in `_Game/Prefabs/`. A pack reimport destroys components added to the vendor prefab. |
| The fight now runs 45% slower than designed | Deliberate — the animations are heavy. Watch it in the day 13–14 playtest; if it drags, push individual moves toward 2.5× rather than rescaling globally again. |
| ~~Boss AI eats the schedule~~ | **Re-opened and closed again 11 Sep.** v1 was fast to build and not fun. v2 is `BossBrain` + `BossPhrase`; see Step 2.2. The lesson is in the row below. |
| Tuning a boss by feel goes in circles | **`BossDebugHUD` exists now.** "She feels passive" is not tunable; threat density is. Measure before changing numbers — the per-frame dodge roll had been invisible for six days. |

---

## Tempo, trim and the blade — settled 4 Sep

**Tempo is one constant**, `Tempo` in `CombatSetupTools.cs`, currently **1.4x**. The design doc's
frame data implied 2.3-3.9x; 2.0x was tried in play and read as absurdly fast. These clips are
authored with real anticipation and speeding them up destroys the wind-up telegraphs depend on.
Change the constant, re-run both menu items, and the whole game retunes.

Per-move, `speedMultiplier` on the asset is the **live** dial - the animator states read their rate
from a `MoveSpeed` float that `CombatActor` sets, so dragging the slider mid-play works. `Duration`
is derived (`measuredLength / speedMultiplier`) so animation and timing windows cannot desync.

**`moveEnd` trims dead recovery.** Every vendor attack is a full draw-cut-sheathe cycle and the tail
is the character standing still - 38% of Slash 1. `CombatActor.EndMove` cross-fades back to
locomotion explicitly, so the trim actually cuts the animation rather than just unlocking input.

**The blade stays drawn for normals and dodges; Specials keep the full iai cycle.**

| | Blade | `moveEnd` |
|---|---|---|
| Slash 1 / 2 / 3 | drawn | 0.52 / 0.50 / 0.55 |
| Evade, Quick Shift x4 | drawn | 1.0 |
| Skill 1 / 2 / 3 | **sheathes** | 0.93 / 0.93 / 0.85 |
| Draw / Sheathe | untouched | 1.0 |

Held by `MoveDefinition.weaponSocket` / `endWeaponSocket` plus `WeaponStance`, which re-asserts the
socket every LateUpdate because `Idle` loops and re-fires its own sheathe event.

**This gives Week 2 a telegraph for free.** The boss can use her own move assets pointing at the
same animator states with `moveEnd = 1` and no socket override - so *she* performs the full
cinematic sheathe after a combo, which is a readable "I am committed, punish me now" window. Same
clips, different data. Build the boss set when wiring Step 2.2.

**Still open:** final trim values need a tuning pass. Numbers above are starting points, and
`Build Move Assets` overwrites hand edits from the `Specs` table.

---

## Known gaps

**No strafe or walk-back animations.** The pack has `Idle`, `Walk`, `Run` only. Consequences:

- Lock-on cannot hold her facing at the target while she moves; she turns to face travel instead.
- Backing away from the boss plays a forward walk. Visible, and it matters because backing off is
  a core defensive verb in a game with no block button.
- Mild foot slide remains on the run cycle.

**These are appearance problems, not aiming problems** — worth stating plainly because the first
playtest read "hard to hit her" as a missing-strafe problem when the cause was the player's attack
tracking sitting at 0 (fixed 16 Sep). Strafe clips would stop the skating; they were never what
made swings miss.

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

# Project Notes — DS2 Remake

Reference notes for *Ten Deaths to the Mirror*. Facts established by inspecting the project,
and decisions taken along the way, so none of it has to be rediscovered.

`combat-design.html` is the design. `build-plan.md` is the ordered execution plan. **This file
is the reference** — environment facts, asset-pack structure, gotchas, and why things are the
way they are.

Last updated **15 September 2026** (Day 13 of 30). Ship date **3 October 2026**.

**New here? Read §7 “Start here” first** — current state, the menu items to re-run after a pull,
and what to do next. §8 is the control scheme.

---

## 1. Environment

| | |
|---|---|
| Unity | **6000.6.0f1** |
| Pipeline | URP **17.6.0** |
| Input | New Input System 1.20 — `activeInputHandler: 1`, **new system only**, old `Input` API unavailable |
| Camera | Cinemachine **6.6.0** (the CM3 line under Unity 6 versioning) |
| Also present | AI Navigation 2.0.14, Timeline 6.6, uGUI 2.6, Visual Scripting 1.9.12, Test Framework 1.8 |
| Input asset | `Assets/InputSystem_Actions.inputactions` — Unity 6 template, **extend it, don't author a second one**. Template actions: Move, Look, Attack, Sprint, Jump, Crouch, Interact. Added 4 Sep: **Dodge** (Space / buttonEast), **LockOn** (Q / rightStickPress), **Stance** (R / buttonNorth). `generateWrapperCode` is off — scripts take an `InputActionAsset` reference and resolve by name, so no codegen step. |

---

## 2. The Combat Girls asset pack

Everything lives under `Assets/CombatGirlsCharacterPack/`. Treat it as **read-only** — all
gameplay code and assets go in `Assets/_Game/`. Exception: the three documented patches in §4.

### 2.1 Rig and clips

- All clips are **Humanoid** (`animationType: 3`). Retargeting works, so the player and the boss
  can share one clip set, and clips also retarget onto the bot body.
- Root motion: `keepOriginalPositionY: 1`, XZ **not** baked into pose — so the clips carry real
  horizontal displacement. The design doc's "root motion on for attacks and dodges" is correct
  and costs nothing.
- **Clip names inside the FBXs differ from the filenames.** The file is `K_Attack_1.fbx`; the clip
  inside is `Attack1`. Likewise `Evade`, `Quickshift_F`, `Sp_Skill3`, `Take`, `Put`.
  `MoveDefinition` therefore references **animator state names**, never filenames.

**Measured 4 Sep** via `Tools ▸ DS2 ▸ Clip Report`. Full data in `Docs/clip-report.csv`; the
resulting retarget is in `build-plan.md` under *Measured frame data*.

| Clip | Length | fps | RootXZ net | RootXZ path | | Clip | Length | fps |
|---|---|---|---|---|---|---|---|---|
| `Attack1` | 2.033 s | 30 | 1.397 | 1.919 | | `Idle` | 3.000 s | 30 |
| `Attack2` | 1.833 s | 30 | 0.961 | 1.528 | | `Walk` | 1.133 s | 30 |
| `Attack3` | 2.267 s | 30 | 2.217 | 2.449 | | `Run` | 0.667 s | 30 |
| `Evade` | 1.467 s | 30 | **0.000** | 0.830 | | `Stun` (loops) | 2.000 s | 30 |
| `Quickshift_F` | 1.000 s | 30 | 2.845 | 3.063 | | `Die` | 2.833 s | 30 |
| `Quickshift_B` | 1.000 s | 30 | 2.821 | 3.426 | | `Hit1` (=`K_Hit_R`) | 1.467 s | 30 |
| `Quickshift_L` | 1.000 s | 30 | 3.130 | 3.257 | | `Hit2` (=`K_Hit_L`) | 1.467 s | 30 |
| `Quickshift_R` | 1.000 s | 30 | 2.990 | 3.313 | | `Sp_Idle` | 2.500 s | 30 |
| `Take` / `Put` | 1.733 / 1.667 s | 30 | ~0.04 | ~0.4 | | `Sp_Walk` | 1.133 s | 30 |
| `Sp_Skill1` | 3.200 s | 30 | 1.964 | 2.552 | | `Sp_Run` | 0.867 s | 30 |
| `K_Sp_Skill_2` | 3.867 s | 30 | **5.180** | 6.275 | | `Sp_TurnL/R` | 0.667 s | 30 |
| `Sp_Skill3` | 4.500 s | **60** | 4.440 | 5.974 | | | | |

Four things this settled:

- **`Sp_Skill3` is the only 60 fps clip.** That alone explains its 270 frames. Nothing is wrong with it.
- **Every move needed 2.3×–3.9×** to hit the design doc's targets. Retargeted to a global **2.0×**;
  see `build-plan.md`. The stun window and posture decay were rescaled to match.
- **`Evade` has zero net displacement** — it moves and returns. The design doc's "gives up your
  position" is wrong. The real Evade/Quick Shift split is **in-place i-frames vs. 2.8–3.1 m of
  displacement**, which is a cleaner distinction than the doc's.
- **The boss's range bands were already right.** `Quickshift_F` covers 2.85 m and `K_Sp_Skill_2`
  covers 5.18 m, matching the doc's 2.5 m and 5 m bands almost exactly.

Naming traps: **`Hit1` is `K_Hit_R.fbx`, `Hit2` is `K_Hit_L.fbx`** — the clip names carry no side
information, so don't guess in `HitReaction.cs`. And Skill 2's clip is `K_Sp_Skill_2` while Skills
1 and 3 are `Sp_Skill1` / `Sp_Skill3`.

### No strafe animations

The locomotion set is `Idle`, `Walk`, `Run` and nothing else — **no strafe, no walk-back**. This is
the single biggest asset limitation in the project and it constrains design, not just polish:

- Lock-on cannot hold her facing at the target while moving; a forward walk played across a sideways
  translation reads as skating. She turns to face travel instead.
- Backing away plays a forward walk — and backing off is a core defensive verb in a game with no block.
- Mild residual foot slide on the run cycle.

Authoring **Walk_Back / Strafe_L / Strafe_R** closes all three. `MoveX` and `MoveY` already exist in
`KG_Combat` and are already written every frame by `PlayerLocomotion`, so the work is: swap the 1D
`Speed` blend tree for a 2D one, and delete the facing special-case in
`PlayerLocomotion.UpdateRotation`. Nothing else depends on it.

### 2.2 Weapon sockets — do not break this

Every clip has **baked animation events** calling `SwitchSocket(string)`, consumed by
`Character_Weapon_Controller` on the prefab root. It moves the katana between hand and back.

- **That component is mandatory on both player and boss.** Remove it and you get
  "AnimationEvent has no receiver" spam and the blade stops moving.
- It switches via **ParentConstraint source index**, not reparenting. So the katana GameObject
  stays put in the hierarchy — **a hitbox parented to the katana follows it correctly.**
- Slots are `Blade` and `Sheath`; socket names `Katana_Close`, `Hand_R_Socket`, `add_weapon_r`,
  `Hand_L_Socket`, `add_weapon_l`, `Put_Socket_Katana`.
- `Dummy_Event.cs` exists purely to swallow `SwitchSocket` on objects that shouldn't react.
- Useful bones: `add_weapon_r`, `add_weapon_l`, `root`, standard UE-style humanoid names.

Attack1's events run 0 → 0.616 s; the blade is only in the right hand for part of the clip.
**Check the event times before choosing a hitbox window** — opening one while the blade is
sheathed is a silent bug.

### 2.3 Animation event times: .meta is NORMALIZED, clip.events is SECONDS

**Learned 15 Sep, the expensive way.** The event times written in an FBX `.meta` are **normalized
0-1**, not seconds. `AnimationEvent.time` on a *loaded* `AnimationClip` is in **seconds**. Reading
the `.meta` values as seconds and dividing by clip length double-normalizes them, which makes every
blade-held interval look about 4x shorter and earlier than it is.

That mistake produced a confident, completely wrong conclusion that five of six hitbox windows were
firing on a sheathed blade, and a "fix" that moved them all too early and broke the three Specials.
**The original windows were correct** — every one already sat inside the blade-held interval:

| Clip | Length | Drawn | Swing | Stowed | Hitbox |
|---|---|---|---|---|---|
| `Attack1` | 2.033 s | 0.075 | 0.490 | 0.556 | 0.300–0.467 ✓ |
| `Attack2` | 1.833 s | 0.000 | 0.475 | 0.613 | 0.258–0.419 ✓ |
| `Attack3` | 2.267 s | 0.000 | 0.369 | 0.794 | 0.289–0.444 ✓ |
| `Sp_Skill1` | 3.200 s | 0.134 | 0.615 | 0.841 | 0.309–0.433 ✓ |
| `K_Sp_Skill_2` | 3.867 s | 0.105 | 0.609 | 0.842 | 0.328–0.483 ✓ |
| `Sp_Skill3` | 4.500 s | 0.147 | 0.692 | 0.751 | 0.345–0.483 ✓ |

**`Docs/clip-report.csv` is the authoritative source** for both clip lengths and event times, and it
lists events in seconds. It already contained everything needed to get this right.

Socket vocabulary: `To_Hand_R_Socket-Blade` draws, `To_Katana_Close-Blade` stows.
`To_add_weapon_r-Blade` is **not** a sheathe — `add_weapon_r` is a right-hand bone, and that event
marks the end of the swing arc.

**`Tools ▸ DS2 ▸ Validate Hitbox Windows`** cross-checks every window against the loaded clip's own
events and runs automatically at the end of `Build Move Assets`. It is worth keeping precisely
because it caught this: the windows it rejected were the ones a bad analysis had just written.

### 2.4 Prefabs

| Prefab | Use |
|---|---|
| `KatanaGirl_FullBody.prefab` | The real character. Originally carried missing-script components from Magica Cloth (guid `e6f5338a96461264fb9a1132c8509672`), which is not in the project — hair/ribbon/skirt sim is dead. **Cleaned 4 Sep:** exactly 20 Magica GameObjects and 19 missing components removed; all 19 `SkinnedMeshRenderer`s plus `Weapon`, `SchoolUniform`, `SportsWear`, `No_Glass` intact. |
| `Humanoid_F_Katana.prefab` | Bot body holding the katana. 364 lines, no Magica dependency, no toon-shader dependency, same humanoid avatar. **Week-1 training dummy and early boss stand-in.** |
| `Katana_Girl/Katana_Girl_Scene.unity` | The vendor's clip viewer. Out of scope — but note that editing the character prefab nulls its `ButtonGenerator.characters` reference, so clicking a button there throws `UnassignedReferenceException`. Harmless; revert the scene from git and don't open it. |

**Prefab ownership — unresolved as of 4 Sep.** The vendor `Prefab/` folder was *moved* into
`_Game/Prefabs/` instead of a Prefab Variant being created. GUIDs are preserved so nothing is broken
now, but the character is still the vendor asset, and a pack reimport would recreate those files at
their original paths with identical GUIDs. The character must become a **Variant** before
`CombatActor` / `PlayerLocomotion` / `PlayerCombat` / `CharacterController` are added to it —
otherwise a reimport destroys the gameplay code the same way it reverts the HLSL patch in §4.

Root of `KatanaGirl_FullBody`: Transform, Animator (`ApplyRootMotion: 1`),
`Character_Weapon_Controller`, `SDFFaceShadowController`.

### 2.5 Animator

`School_Katana_Controller.controller` is a flat **38-state** demo controller, one trigger per clip,
built for the vendor's button viewer. Not salvageable for gameplay — author `KG_Combat.controller`
in `_Game/Animation/` and leave this one alone.

---

## 3. Unity 6.6 gotchas

Unity 6.6 promoted several obsolete APIs to **`CS0619` — obsolete-as-error**. These cannot be
suppressed with `#pragma warning disable`; a single one blocks the whole script domain.

| Obsolete | Use instead |
|---|---|
| `Object.GetInstanceID()` | `GetEntityId()` — or sidestep it (e.g. `HashSet<T>` of the objects themselves) |
| `SerializedProperty.objectReferenceInstanceIDValue` | `objectReferenceEntityIdValue` |

`CS0618` (plain warnings — `FindObjectsSortMode`, `EditorAnalytics.SendEventWithLimit`) are
harmless and still functional. Leave them; they're in live vendor runtime code.

---

## 4. Vendor modifications — reverted by any reimport

Three hand-edits inside `CombatGirlsCharacterPack/`. **Reimporting the pack or the toon
`.unitypackage` silently undoes all three.** If rendering goes magenta or `CS0619` errors
reappear, this is why.

**1. `…/Unity6_URP/Runtime/Shaders/URP/URPIncludeGuards.hlsl` — patched, +6 lines.**

URP 17.6 moved the parameterless `IsSurfaceTypeTransparent()` into `Shaders/Utils/SurfaceType.hlsl`,
which URP reaches only through `LitInput.hlsl`. UTS deliberately blocks `LitInput.hlsl` (Toon
declares its own `UnityPerMaterial` CBUFFER) but still includes URP's `LitForwardPass.hlsl`, whose
line 265 calls that function. Result: `'IsSurfaceTypeTransparent': no matching 0 parameter function`,
the `Toon/Toon` ForwardLit pass fails, everything renders magenta. The patch supplies the overload
against Toon's own `_Surface` and claims `SurfaceType.hlsl`'s include guard so it can't come back
and redeclare `_Surface`:

```hlsl
#ifndef UNIVERSAL_SURFACE_TYPE_TRANSPARENT_INCLUDED
#define UNIVERSAL_SURFACE_TYPE_TRANSPARENT_INCLUDED
inline bool IsSurfaceTypeTransparent() { return _Surface > 0; }
inline bool IsSurfaceTypeOpaque()      { return !IsSurfaceTypeTransparent(); }
#endif
```

This is the concrete form of the store page's "tested to Unity 6.4" caveat. It lives in URP's
shader library, not in package metadata — nothing in `package.json` or the asmdefs predicts it.
`URPIncludeGuards.hlsl` is the right home: both `UnityToon.shader` and `UnityToonTessellation.shader`
include it immediately after `UniversalToonInput.hlsl` (where `_Surface` is declared) and
immediately before `LitForwardPass.hlsl`.

**2. `Biperworks_Tools/Editor/MissingChecker/` — deleted.** `CS0619` on
`objectReferenceInstanceIDValue`. Standalone vendor prefab-diffing tool; nothing referenced it.

**3. Toon package `Tests/` folders ×2 — deleted** (`Unity6_URP/Tests/` and
`com.unity.film-internal-utilities/Tests/`). `CS0619` on `GetInstanceID()`. They only compiled
because the vendor ships the package as loose files under `Assets/` — in `Packages/`, Unity never
builds package tests unless the package is listed under `testables` in `manifest.json`. Deleting
them restores intended behaviour.

**Do not import `Unity Chan Toon Shader - SDF - URP_Katana.unitypackage`** (the non-Unity6 one).
It's the older UTS2 build; having both gives duplicate shader names and duplicate asmdefs.

Also note: the toon `.unitypackage` ships all 27 `.mat` files with **GUIDs identical** to the
pack's, so importing it overwrites materials in place — no deletion or reimport needed first.

---

## 5. Repository

Remote `github.com/Ezra1383/DS2-Remake.git`, branch `main`.

History was re-initialised on 4 Sep. The original repo had **no `.gitignore`**, tracked all of
`Library/` (28,758 of 29,681 files, `.git` at 1.4 GB), and had LFS pointed at exactly the wrong
things — `Library/ArtifactDB` and `DataStore/*.bin` were in LFS while the 48 FBXs and 61 PNGs
were not. `ArtifactDB` is rewritten every editor session and would have burned GitHub's 1 GB/month
free LFS bandwidth within days.

After the fix: **914 tracked files, 122 in LFS, `.git` at 165 MB.** Nothing had been pushed, so no
history rewrite was needed. Old `.git` parked at `C:\GGD TermProject\_git-backup-1.4GB` — safe to
delete once a push succeeds.

`.gitattributes` keeps Unity's YAML text assets (`.unity`, `.prefab`, `.mat`, `.asset`, `.controller`)
**out of LFS** deliberately: they're small, and real diffs on them matter when a reference breaks
late in the schedule.

---

## 6. Decisions and why

| Decision | Reasoning |
|---|---|
| Hitbox windows as **normalized time on the ScriptableObject**, not animation events | Events are baked into the FBX `.meta` next to the vendor's `SwitchSocket` events; re-adding yours after a reimport is a footgun, and retuning would mean leaving the editor. Polling `normalizedTime` makes a hitbox a float you drag while playing. Also defuses the "clip timings don't match targets" risk. |
| **No trigger-per-move** in the animator | The demo controller's 38-triggers pattern doesn't scale to data-driven moves. Isolated states entered with `CrossFadeInFixedTime`, state name stored on the move asset. |
| **Cinemachine 3** over a hand-written `LockOnCamera` | Orbital follow, target-group framing and Impulse shake out of the box; saves ~1.5 days and feeds the week-3 juice pass. |
| **UTS toon shader** over URP/Lit | URP/Lit silently kills `SDFFaceShadowController` (its `_UseSDFShadow` / `_FaceForward` properties don't exist on Lit), the outline pass, and the blade matcap. Silhouette readability is mechanical here, not just aesthetic — telegraphs have to read at speed. |
| Player root = **the character prefab root itself** | The Animator lives there; a separate parent GameObject just fights root motion. |
| **One `controller.Move()` call**, in `OnAnimatorMove` | Root motion and scripted movement must never both write position in the same frame. A `RootMotionDriven` flag picks the source; `OnAnimatorMove` is also the only point where `animator.deltaPosition` is valid. |
| Impact feedback is built around **hit stop, sound, camera** and nothing else first | Lin et al. (2022) compared the best and worst action games on "impact feel" across a 19-feature framework; those three were what separated them, and missing any one "may ruin players' impact feel". Particles and flashes are in the framework but did not divide good from bad. Building in that order meant the first pass was narrow instead of a grab-bag. |
| The camera shakes for the **victim**, not the attacker | It is a mirror match - same clips, same moves, same silhouette - so symmetric feedback makes exchanges unreadable. A small impulse when you connect and a large one when you are hit is the fastest channel for telling the two apart, and it stops the shake becoming exhausting over a ten-death arc. |
| Boss AI is **authored phrases**, not per-move weighted random | v1 re-rolled one move from a weighted table whenever she became free. Every exchange came out different, so nothing was recognisable and nothing was learnable — and since deaths *are* the progression system here, an unreadable boss makes the whole premise fail. Phrases put the randomness one level up: unpredictable which string you get, fixed what the string does. |
| The boss **tracks during wind-up, then locks** | Without it her committed root-motion swings whiffed against anyone walking sideways, and exchanges resolved to nothing. The wind-up exists so the player learns dodge *timing*; tracking is what stops "walk away" beating every attack. Per-move, because a lunge and a heavy overhead want different answers. |
| Lock-on drives the **orbit yaw**, not just `LookAt` | Dark Souls puts the camera on the line from enemy through player. A `CinemachineTargetGroup` alone centres the shot *between* them, which frames both but leaves the camera wherever the player last pointed it. Driving `CinemachineOrbitalFollow.HorizontalAxis.Value` toward the player-to-target yaw is what puts it behind her. Needs Binding Mode = World Space, or the angle is measured against a player who is constantly turning. |
| **She faces her direction of travel, even locked on** | Forced by the asset — see *No strafe animations* below. |
| `Humanoid_F_Katana` as the week-1 dummy | No Magica dependency, no toon-shader dependency, same avatar, 30× smaller prefab. |

**Schedule correction:** the design doc's masthead promises a three-day buffer, but Day N =
(2 + N) September, so days 29–30 already consume 1–2 Oct. **The real buffer is one day: 3 Oct.**

---

## 7. Status

### Start here

**Day 13 of 30** (15 Sep 2026). Ship **3 Oct**. Running **ahead** of `build-plan.md` — the juice
pass came in a week early — but Week 4's balance and edge-case days are still fully spoken for.

The fight is playable end to end: move, lock on, chain three slashes, dodge, parry, use all three
Specials, break her posture, die, learn the move that killed you, retry in under two seconds.

**After a fresh pull, run these in order** (all re-runnable, all overwrite hand edits):

| Menu item | Why |
|---|---|
| `Tools ▸ DS2 ▸ Build Move Assets` | Player moves. Ends by running the hitbox-window validator |
| `Tools ▸ DS2 ▸ Rebuild Boss` | Boss moves, moveset, phrases, and prefab wiring + tuning |
| `Tools ▸ DS2 ▸ Wire Feel` | `HitFeedback`, impulse source, **impulse listener on the camera** |
| `Tools ▸ DS2 ▸ Wire Progression` | Player move slots, `ProgressionManager`, `DeathScreen`, duplicate cull |

**Next, in order of value:**

1. **Audio — ~15 clips.** The largest remaining gap by some distance, and research finding #2 of
   three. The system is built and silent; shopping list in `build-plan.md` Step 3.3. The parry clip
   matters most — a deflect with no metallic clang is missing most of what makes parrying land.
2. **The first real playtest.** Step 2.3's actual deliverable and still never done: play the whole
   arc from a wiped save, write down what feels unfair, **fix nothing yet**.
3. **HUD** (Step 4, Days 25–26) — player health, boss health, **boss posture**, learned moves. The
   posture meter especially, now that parry is a second route to breaking her.
4. **Housekeeping:** delete `BossAI.cs` (dead since the rewrite); strip the five unused Unity
   template input actions — `Jump` sits on Space and `Crouch` on pad B, both colliding with Dodge.

**Unverified, worth ten minutes each:** does `Committed lunge` ever connect (Skill2 travels 5.18 m
but is chosen at 2.6–5.5 m, so it may sail straight past)? Does the `Punish` phrase ever fire
(weight 0, reaction-only)? Is the death slow-motion at 1.2 s too long?

**Debug switches:** `logTimeline` on HitFeedback (names every freeze and its cause), `logDecisions`
on BossBrain, `logUnlocks` on ProgressionManager, `wipeOnPlay` / `unlockEverything` on
ProgressionManager, **F1** for the boss HUD.

**Compile without opening Unity:**
`"C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/DotNetSdk/dotnet.exe" build Assembly-CSharp.csproj`
— and `Assembly-CSharp-Editor.csproj` separately. New `.cs` files need adding to the `.csproj` by
hand first; Unity regenerates it, and it is gitignored.

---


**Phase 0 — done:** toon shader imported and patched (verified: toon shading, outlines, matcap,
SDF face shadow tracking under camera orbit); all compile errors cleared; Cinemachine 6.6.0
installed; `Assets/_Game/{Scripts/Editor,Prefabs,Moves,Animation,Scenes,VFX,Audio}` created;
`ClipReportWindow.cs` written and **run** — every clip measured and the frame data retargeted;
Magica Cloth stripped from the character prefab; `Arena.unity` built (floor, boundary, light,
Dome, `CinemachineCamera`) and registered in Build Settings; repo cleaned and re-initialised
(1.4 GB → 165 MB, 29,681 → 914 tracked files).

**Phase 0 — closed 4 Sep.** `KatanaGirl.prefab` and `TrainingDummy.prefab` exist as Prefab Variants
in `_Game/Prefabs/`, the arena references them, and the repo is pushed.

**Step 1.1 — done 4 Sep.** `MoveDefinition.cs` plus 13 move assets in `_Game/Moves/` (nine table rows,
but Quick Shift is four assets and Draw/Sheathe two), and `KG_Combat.controller` with two locomotion
blend trees and 17 isolated one-shot states. Both generated by `Tools ▸ DS2 ▸ Build Move Assets` and
`Build Combat Animator`, which are re-runnable.

**Step 1.2 — done 4 Sep.** `PlayerLocomotion.cs` (camera-relative movement, single-`Move` root-motion
reconciliation, animator params) and `LockOnController.cs` (target acquisition, orbit-yaw framing,
runtime target group). Verified in play: movement, running, lock-on with the camera behind her and
the dummy ahead, lock breaking on range.

**Week 2 — done 5 Sep.** Posture, boss AI v1, reactive dodge.

**Boss AI rewritten 11 Sep (Day 9).** v1's weighted-random-per-move selection read as passive and
unreadable in play, and tuning its numbers had stopped helping. Replaced with a four-layer brain:
`BossBrain.cs` (pacing, phrases, reactions, positioning), `BossPhrase.cs` (authored attack
strings), `BossPerception.cs` (delayed, once-per-swing reactions), `BossDebugHUD.cs` (threat
density). Full reasoning and the measured causes are in `build-plan.md` Step 2.2. Three things
worth carrying forward:

- **Randomness belongs at the choice of phrase, not at every action.** A boss that re-rolls a
  move each time she is free produces a different sequence every exchange and therefore no
  sequence at all.
- **Threat density is the metric.** Fraction of time a boss hitbox is open. v1 measured ≈14%;
  everything else she did was dead air. Never tune boss feel without it on screen.
- **Probability rolled per frame is not probability.** A 35% dodge chance re-rolled across 26
  frames of start-up is a 100% dodge chance. `BossPerception` now owns one roll per swing.

**Juice pass built 12 Sep (Day 10),** pulled forward from Days 20-21. `Feel/HitFeedback.cs`,
`Feel/ImpactAudio.cs`, `Feel/HitFlash.cs`, `Editor/FeelWiring.cs`. Reasoning and numbers in
`build-plan.md` Step 3.3. Carry forward:

- **`Time.timeScale = 0` makes `Time.deltaTime` 0.** A hit-stop timer on scaled time never ends and
  the game freezes permanently. Anything counting down through a freeze uses `unscaledDeltaTime`.
- **Cinemachine Impulse is silent without a listener.** No `CinemachineImpulseListener` on the
  camera means every impulse is discarded with no error at all. The Arena had none.
- **UTS needs three colour properties flashed together.** `_BaseColor` lights only the lit region;
  `_1st_ShadeColor` and `_2nd_ShadeColor` own the shaded bands and are not derived from it. Tint
  just the base and half the character stays dark, which reads as a rendering bug.
- **No VFX Graph, no Shader Graph** in the manifest - built-in Particle System only.

**Progression built 15 Sep (Day 13).** `ProgressionManager.cs` (unlock ladder, resolution rules,
PlayerPrefs), `DeathScreen.cs` (placeholder card + in-place retry), plus the **Special** input
action. Notes:

- **Starting kit is Slash 1 + Evade + Parry.** The design doc is firm about Evade: with no stamina
  and no block it is the only defensive verb, and a player without it has no interaction beyond
  swinging. Parry joined it on 15 Sep as a core verb rather than a reward - putting it on the
  ladder would make the arc eleven deaths instead of the ten the game is named after.
  **It has to be in `StartingKit`**: a move that is in neither that array nor the ladder is never
  unlocked, so its button silently does nothing, which is exactly what happened first time.
- **Specials: hold `Special` (Left Ctrl / LB) + Attack / Dodge / Stance = Skill 1 / 2 / 3.** Tapping
  Stance alone takes or leaves the Special stance. Before this, `PlayerCombat` had no reference to
  any Skill, Draw or Sheathe asset - half the moveset was unreachable whatever you pressed.
- **The retry resets in place** rather than reloading. The budget is two seconds from killing blow
  to next attempt, and nothing is destroyed, so every reference and event subscription survives.
  `CombatActor.ResetForRetry` disables the `CharacterController` around the teleport, or it resolves
  the move against the collision it is standing in and slides elsewhere.
- **`DeathScreen.Retry` forces `Time.timeScale = 1`.** The killing blow's slow motion is still
  running when the card appears; without this the next attempt plays at 30%.

**Parry added 15 Sep (Day 13), as a deliberate departure from the design doc.**
`combat-design.html` calls "no block, no parry, no shield" the single most important fact in the
project and builds the posture economy on it. The author's call after playtesting was that Evade
already reads as defending herself, and that a parry earns the **same** payoff as a posture break
(2 s stun, x2 damage) on **its own button** (`F` / RMB / RB), leaving Evade untouched.

- **There is no parry animation in the rig.** `Move_Parry` points at the `Evade` state with
  different data - tempo 2.6, `moveEnd` 0.45 - so it reads as a defensive snap rather than a roll.
  Two assets on one animator state is what `MoveDefinition` is for; the boss already does it.
- **No i-frames on the parry, on purpose.** Outside the 0.04-0.30 window (~0.15 s) the move has no
  defence at all. That is the entire cost of reaching for it instead of dodging.
- **`CombatActor.Stagger(seconds, damageMultiplier)` is now the single owner of the stunned state.**
  A posture break and a parry both route through it. `PostureSystem` no longer sets
  `actor.IsStunned` or runs its own stun clock - two systems each owning that is how an actor ends
  up permanently frozen.
- **Watch this number:** a parry now reaches the same opening that four seconds of sustained
  pressure buys. If parrying becomes strictly better than pressuring, the posture meter stops
  mattering and "aggression is the correct defense" quietly stops being true. The lever is
  `Hitbox.parryStagger` / `parryStaggerDamageMultiplier`.

**Singleton duplicates destroyed the arena (found 15 Sep).** The camera drifted away on start,
intermittently. Cause: a second `ProgressionManager` had ended up on `Arena(Experiment)`, which is
the **parent of the floor and all four boundary walls**. `Awake` resolved duplicates with
`Destroy(gameObject)`, and Awake order between two instances of one component is undefined - so
roughly half the time the loser was the arena root and the floor vanished, dropping both actors
through the world with the camera following.

Three things changed, and the pattern generalises to any manager added later:

- **Duplicate resolution destroys the COMPONENT, never the GameObject.** That object may own
  something; `Destroy(this)` cannot take the scene with it.
- **`DontDestroyOnLoad` is refused on an object with a parent or children**, with a warning. DDoL
  promotes to root and keeps the whole subtree, so a stray copy would otherwise drag half the
  scene out of the loaded scene entirely.
- **`Tools ▸ DS2 ▸ Wire Progression` now culls duplicates** rather than skipping when it finds one,
  and prefers a dedicated childless root object as the survivor.

`DeathScreen` had the same duplication (two subscribers to `Died`, two overlapping cards, two
retries per death) and now guards the same way.

**Next:** validate the boss rewrite in play with `BossDebugHUD` on — threat density 20-25%, no
quiet stretch over 3 s, reactive dodge firing on 25-35% of swings rather than all of them. Then
`build-plan.md` Step 2.3, telegraphs and the first real playtest. Delete `BossAI.cs` once the
rewrite is proven (`BossWiring.cs` references the type to strip it off the prefab, so that
reference goes at the same time).

---

## 8. Controls

From `Assets/InputSystem_Actions.inputactions`. **Extend that asset, never author a second one.**

| Action | Keyboard / Mouse | Gamepad | Notes |
|---|---|---|---|
| Move | WASD / arrows | Left stick | Camera-relative |
| Sprint | Left Shift | L3 | |
| Attack | Left Mouse | X / □ | Chains via each move's `cancelWindow` |
| Dodge | Space | B / ○ | Neutral = Evade; with a direction = Quick Shift |
| **Parry** | **F** / Right Mouse | RB / R1 | ~0.20 s window, no i-frames outside it |
| Lock on | Q | R3 | |
| Stance | R | Y / △ | Draw / Sheathe, persistent |
| **Special** (hold) | **Left Ctrl** | LB / L1 | Also enters the sheathed iai stance |

**Specials:** hold Special, then Attack / Dodge / Stance = **Skill 1 / 2 / 3**. The stance part
requires **The Draw** unlocked (death 5), which is what the ladder says grants Special stance.

**Meta:** F1 toggles the boss debug HUD; any key retries on the death card.

**Dead template actions still in the asset** — `Look`, `Jump`, `Crouch`, `Interact`, `Previous`,
`Next`. Nothing reads any of them, but `Jump` is bound to **Space** and `Crouch` to **pad B**,
colliding with Dodge. Harmless today; strip them before the build.

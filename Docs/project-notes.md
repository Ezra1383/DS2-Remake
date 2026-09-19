# Project Notes — DS2 Remake

Reference notes for *Ten Deaths to the Mirror*. Facts established by inspecting the project,
and decisions taken along the way, so none of it has to be rediscovered.

`combat-design.html` is the design. `build-plan.md` is the ordered execution plan. **This file
is the reference** — environment facts, asset-pack structure, gotchas, and why things are the
way they are.

Last updated **16 September 2026** (Day 14). **Ship date 20 September 2026** - the instructor
cut the deadline on 16 Sep to five days from that date. The original 3 Oct plan is dead;
`build-plan.md` Phase 4 has been rescoped to match.

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

**KNOW WHAT IT DOES NOT PROVE.** It checks the blade is *drawn* during the window. It cannot check
the blade is *pointed at anything*. Slash 2's window passed validation for two weeks while sitting
entirely past Attack2's contact point, and the move damaged nothing the whole time (19 Sep entry
in §7). A green result from this tool means "not obviously wrong", not "this move connects".

For the same reason, **`To_add_weapon_r-Blade` is not a contact marker.** It records where the
swing arc ENDS. For Attack1 that is near the contact; for Attack2 it is at the opposite end of the
clip from it. Reasoning about where a hitbox should sit from that event produced two wrong fixes
in a row. The only reliable measure was empirical: which windows actually land in play.

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

**Day 17** (19 Sep 2026). **Ship 20 Sep — TOMORROW.** Phases 0–3 are complete; Phase 4 has been
compressed into whatever tomorrow holds.

The fight is playable end to end: move, lock on, chain three slashes, dodge, parry, use all three
Specials, break her posture, die, learn the move that killed you, retry in under two seconds. The
HUD shows her health, **her posture**, your health and what you have learned; impacts have hit
stop, camera shake, particles and a blade trail; her wind-up telegraphs through that trail.

> ### ⚠ Read this before anything else
>
> **1. Days 14–17 were spent almost entirely on one bug** — Slash 2 never damaging the boss. It is
> fixed (see the Slash 2 entry below), but it consumed the audio pass, the balance pass and the
> playtest. Judge the remaining plan against that, not against the original Phase 4.
>
> **2. Turn the debug logging OFF before building.** `logHits` on `KatanaGirl.prefab` AND
> `Boss Variant.prefab`, and `logSweep` on the HitBox. They are `Debug.Log` per hit and per
> hitbox-window transition — console spam and a real per-frame cost in a build.
>
> **3. Commit state at handoff:** `78ff1f1 Added VFX and HUD` covers most of Days 14–16. Left
> uncommitted on 19 Sep: the **Slash 2 window fix** (`CombatSetupTools.cs` + the regenerated
> `Move_Slash2.asset`), the per-move hitbox logging in `CombatActor.cs`, and these docs. The author
> was pushing manually — **check `git log` and `git status` before assuming anything is versioned.**
>
> **4. There has still never been a full playtest from a wiped save.** Every session so far has
> been played with `unlockEverything` on. The ten-death arc — the thing the game is named after —
> has not once been experienced as a player would experience it.

**After a fresh pull, run these in order** (all re-runnable, all overwrite hand edits):

| Menu item | Why |
|---|---|
| `Tools ▸ DS2 ▸ Build Move Assets` | Player moves. Ends by running the hitbox-window validator |
| `Tools ▸ DS2 ▸ Rebuild Boss` | Boss moves, moveset, phrases, and prefab wiring + tuning |
| `Tools ▸ DS2 ▸ Wire Feel` | `HitFeedback`, impulse source, **impulse listener on the camera** |
| `Tools ▸ DS2 ▸ Wire Progression` | Player move slots, `ProgressionManager`, `DeathScreen`, `CombatHUD`, duplicate cull |
| `Tools ▸ DS2 ▸ Build VFX` | Particle textures, materials, impact/parry prefabs, blade trail, `HitFeedback` slots |

(`Tools ▸ DS2 ▸ Wire HUD` re-adds just the HUD after a scene revert, without the prefab half.)

**One day left. In this order, and stop when the day does:**

1. **MAKE THE STANDALONE BUILD FIRST.** Before any polish. Animation events and `Resources`
   lookups behave differently in a build than in the editor, and that is not a thing to discover
   on the final evening. An editor-only submission is the one failure that loses everything at
   once. Turn the debug logging off as part of this.
2. **One playthrough from a wiped save** (`wipeOnPlay` on `ProgressionManager`, `unlockEverything`
   OFF). Confirm the ten-death arc actually functions: each death grants exactly one move, no
   Special arrives before the Draw, the card reads correctly. This has never been done.
3. **Balance, only if 2 reveals something serious.** Boss damage is the fastest lever on arc
   length. Target 2–4 minutes on a winning attempt. Accept "roughly right".
4. **Audio — ~15 clips**, if any time survives. Shopping list in `build-plan.md` Step 3.3. Still
   the largest quality gap, and it doubles as the Step 2.3 telegraph. Every slot is optional and
   silent when empty, so this can be cut entirely without breaking anything.
5. **Write-up.** Likely carries real weight in the grade, and the raw material here is unusually
   good — the measured frame data, the boss AI v1 failure and rewrite, the normalized-vs-seconds
   mistake, and the Slash 2 hunt are all documented with their reasoning intact.

**Cut in this order if the day runs out:** audio variations first, then balance depth, then the
wiped-save run. **Never cut the build.**

**Not done, and now almost certainly shipping unfixed:** `BossAI.cs` is dead code since the 11 Sep
rewrite (delete it and the reference in `BossWiring.cs` together); the five unused Unity template
input actions are still bound, with `Jump` on Space and `Crouch` on pad B colliding with Dodge —
harmless because nothing reads them, but untidy in a build.

**HUD — done 16 Sep (Day 14).** `UI/CombatHUD.cs`: player health, boss health, **boss posture**,
learned-moves panel, and the damage vignette the juice pass had to leave out. Notes:

- **IMGUI, not a uGUI Canvas.** Deliberate under the compressed deadline: no prefabs, no sprites,
  no `CanvasScaler` to get wrong between the editor and the build. One `GUI.matrix` scale against a
  1080p reference height makes every resolution agree. `DeathScreen` and `BossDebugHUD` are already
  OnGUI, so this is a third instance of one pattern rather than a second UI system. The cost is
  per-frame GC churn in `OnGUI` and text that is not crisp when scaled far up — acceptable here,
  and the reason to revisit it would be a Canvas-based menu, which is out of scope now.
- **It also unblocked the damage vignette**, which `build-plan.md` Step 3.3 had parked behind
  "needs a Canvas, and there is none until the Week 4 HUD". The vignette is a 64×64 radial texture
  generated at runtime and stretched — no asset, no import settings.
- **Every animation here runs on `Time.unscaledDeltaTime`.** Same trap as `HitFeedback`: hit stop
  sets `Time.timeScale = 0`, and a bar lerping on scaled time freezes mid-slide on every connect,
  which reads as the HUD hitching rather than as the hit landing.
- **The trailing "chip" bar is what makes damage legible.** The solid bar snaps down immediately so
  the hit is felt; a pale bar behind it drains after a 0.45 s hold, and the gap between them is the
  size of what just landed. Without it a three-slash chain is one indistinct slide.
- **`CombatHUD` guards duplicates the same way `DeathScreen` does** — `Destroy(this)`, the
  component, never the GameObject. Same reasoning as the arena-deleting `ProgressionManager` bug.
- **Move names moved to `MoveDefinition.DisplayName`.** The death card names the move you just
  learned and the HUD panel lists it a second later; two copies of that table would eventually
  disagree about one of them.
- **Locked ladder rungs show as `---------`, not as names.** How many are left is information the
  player should have; which ones they are is the reward.

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

**"The hitbox is broken" was Slash 2's window in the wrong place — found 19 Sep (Day 17).** The reported symptom was the second
slash of a chain never damaging her while the first and third did.

**The cause: `Move_Slash2`'s hitbox window was `0.258-0.419`, and Attack2's contact is at roughly
`0.10-0.28`.** The two overlapped only across `0.258-0.275` - one or two frames at the very edge -
so it read as "never".

**Settled at `0.080-0.478`** after two passes. `0.095-0.320` took it from never landing to landing
about **two swings in five** - right region, too narrow - so the window was opened out to very
nearly the whole trimmed move. That is blunt on purpose and it is safe: `alreadyHit` caps a swing
at one hit per target, so a wide window cannot double-hit, and the only cost is that contact may
register marginally early. **If Slash 2 ever needs retuning, narrow from here and re-measure
rather than reasoning from event times.**

**How it was finally found, after four wrong guesses: the boss runs the same clip.** Her
`Boss_Slash2` has the window at `0.095-0.275` and her Slash 2 always landed, while the player's
never did - same `Attack2` state, same 1.309 s duration, different window. That difference IS the
measurement. **When two actors share a clip and one's window lands while the other's does not,
compare them before reasoning about anything else.**

Reasoning from the animation events sent this the wrong way twice. `To_add_weapon_r-Blade` at
0.475 is described in §2.3 as "the end of the swing arc", which suggested contact was LATE and
produced a "fix" that moved the window to `0.300-0.478` and made it strictly worse. That event
marks where the arc finishes, **not where the blade crosses an opponent**, and for Attack2 those
are at opposite ends of the clip.

**`Validate Hitbox Windows` passed this window for two weeks.** It checks the blade is DRAWN
during the window; it cannot check the blade is pointed at anything. Worth knowing what that tool
does and does not prove.

The four wrong guesses, recorded because each cost time: a reactive dodge eating the hits; blade
tunnelling; `OnTriggerEnter` missing an already-overlapping blade; and the window being too late.
Two produced real fixes for real latent problems (below) and neither was the bug.

**What would have found it in ten minutes instead of three days:** the per-move logging that
eventually cracked it - `BEGIN <move> (state, duration, window, end)` plus `hitbox OPEN/CLOSE at
t=` - printed alongside the damage lines. It is in `CombatActor` under `logHits` now. Any future
"this attack does not work" starts there, because it separates *the window never opened* from
*the window opened and the blade was not on the target*, and those two have completely different
causes. Guessing between them is what cost the time.

Still true and worth keeping, but NOT the cause of the above: `BossPerception` rolls a dodge
**once per swing**, and a three-slash chain is three swings, so at `dodgeChance` 0.25 she dodges at
least one link of a chain roughly **58%** of the time. `Evade`
carries i-frames over `0.083–0.633` — **55% of the move** — and the Quick Shifts `0.091–0.545`, so
one dodge comfortably covers the follow-ups too. Those hits return `HitResult.Evaded`: no damage,
by design.

**The bug was that `HitResult.Evaded` produced nothing observable at all.** The branch called
`sfx.PlayEvaded` and returned — and there are no audio clips yet, no VFX on that path and no flash.
`HitFeedback`'s own comment says silence there "reads as the game failing to notice rather than as
a success"; it was right, it just assumed the audio would arrive. So a working dodge and a broken
hitbox looked identical, and the natural reading was the wrong one.

**The fix is that she now MOVES, not that the hit is annotated.** A reactive dodge always
backsteps: `PickDodge` used to backpedal under 2 m and sidestep beyond it, and a sidestep at melee
range reads as repositioning rather than as avoiding the swing. `Quickshift_B` carries **2.82 m**
of measured root motion straight away from the blade, which is unambiguous. `Evade` is useless
here and always was — **RootXZNet 0.000**, it steps out and returns, so it reads as her standing
still and shrugging off the hit. That is the whole reason the dodge was invisible.

The flash-and-VFX route was built first and then switched off (`flashOnEvade`, default off;
`evadeVfx` built but unassigned). Making her *behave* legibly beats annotating behaviour the player
cannot read. `HitFlash.Play(Color)` and `FX_Evade` remain for the player's own evade, which is
in-place and may still want a tell.

**Watch the cost:** every backstep is ~0.7 s of zero threat plus the walk back in, and
always-backwards spends more of that than the old mix did. If the fight starts to feel like
chasing her, that is this, and `sidestepChance` on `BossBrain` (default 0) is the dial back.

**The lesson worth keeping: an outcome with no feedback is indistinguishable from a bug**, and it
will be reported as the most mechanically alarming bug the player can imagine. Every `HitResult`
needs its own voice. `Evaded` had none, and it cost most of a day in the physics layer.

**Balance question left open:** whether she should be able to dodge *out of* a chain at all.
`dodgeChance` 0.25 / `dodgeCooldown` 2.5 s / `dodgeRange` 3.5 m are on `BossBrain`. If chain
pressure is meant to be the route to a posture break, ~58% of chains being interrupted works
against "aggression is the correct defense". Lower `dodgeChance`, or gate the reaction so she
cannot dodge a swing that lands while she is already in hit reaction.

**Hitbox detection rewritten 16 Sep (Day 14) — second playtest finding.** Two reported symptoms,
one root cause: a slash visibly passing through her doing nothing, and the second slash of a chain
doing nothing after the first one landed.

Detection was `OnTriggerEnter` alone. Both failures follow from that:

- **Tunnelling.** The blade collider is `0.06 × 0.07 × 1.12` — very thin — and it belongs to the
  character root's compound rigidbody, which is **kinematic with Discrete collision detection**.
  Physics samples poses 50×/s; between two samples a thin fast box can be entirely one side of her
  and then entirely the other, and no contact is ever generated.
- **`OnTriggerEnter` fires on ENTRY.** At close range the blade is frequently already *inside* her
  hurtbox when the next window in a chain opens, so there is no entry to report. That is exactly
  the "second slash does nothing" case, and it gets worse the closer you are — i.e. when you are
  doing the right thing.

**Layers were checked first and are correct**: the player's mask is `8` (layer 3 *Enemy*, the
boss's hurtbox), the boss's is `64` (layer 6 *Player*, set as a scene override on the player
instance). A layer fault would have been a total failure, not an intermittent one — the
intermittency is what points at detection rather than filtering.

**This was not the cause of the reported misses** (see the entry above — she was dodging). It was
a real latent flaw found while chasing them, and it is kept because a thin fast blade on a Discrete
kinematic body genuinely can tunnel. `Hitbox` now runs a **swept overlap query** in `FixedUpdate` while the window is open: it
sub-steps between the blade's pose last physics step and its pose now (`sweepSubSteps`, default 5)
and runs `Physics.OverlapBox` at each. Sub-stepping defeats the tunnelling; asking *"is anything
inside right now"* rather than *"did anything just enter"* defeats the chain case.

- **The trigger callback was kept** and both paths funnel into one `TryHit`. `alreadyHit` means
  whichever sees the target first wins and the other is a no-op. Losing hits entirely would be a
  worse failure than the intermittent one being fixed, and this could not be verified in play
  before shipping it.
- **`Open()` resets the sweep's previous pose.** Carrying it over from the last swing would sweep
  across the gap between them — through anything standing in between, and across the entire arena
  after a retry teleport.
- **`logSweep` on `Hitbox`** names every collider the sweep touches. That is the switch for
  telling "the window never opened" apart from "the window opened and detection missed".
- **If misses persist, the next lever is the blade's thinness**, not the sweep count. Plenty of
  action games make the damage volume fatter than the visible weapon; widening the box's X/Y is
  cheaper than raising sub-steps and is what the remaining near-misses would be.

**Player attack tracking, 16 Sep (Day 14) — found by the first playtest.** The complaint was
"it is really hard to hit her, I can never face her." The cause was not the missing strafe clips,
which is where suspicion naturally falls:

- With no directional clips she turns to face her direction of **travel**, and `UpdateRotation`
  **freezes facing the instant a move starts**. So an attack thrown while moving points where you
  were walking, not at the target.
- `CombatSetupTools` line ~231 read `forBoss ? s.bossTrack : 0f` — **the boss corrected at
  240 deg/s through her wind-up and the player at zero, on every single move.** Her swings landed
  and yours did not, and the asymmetry was in the data rather than in the animation set.

The original reasoning for the zero ("player swings point where the stick pointed, steering them
would feel like the game taking over") was sound for free-aim and was simply never revisited once
lock-on became how the fight is actually played. **It survives intact anyway**, because tracking
turns toward `FaceTarget` and `FaceTarget` is null unless locked on — so `playerTrack` applies
**only while locked on**, and free-aim is untouched. No new code; one field on `Spec`.

Values: **420 deg/s on Slash 1-2, 360 on Slash 3**, 300 on Quick Shift Forward (a gap-closer
should close the gap), **180/200/120 on the Specials** — a commitment that swings 180 degrees to
find you is not a commitment. Evade, Parry, the back and side dashes and the stance moves stay at
**0**: a dodge that steers toward the boss is not a dodge.

Also widened when she holds facing at all. It was `desired.sqrMagnitude < 0.0001f` — exactly zero
input — and **`MoveDirection` arrives normalized** (`PlayerLocomotion.CameraRelative` normalizes),
so there is no partial-deflection signal to read and a magnitude-based deadzone would be dead
code. The usable question is the **angle between travel and the target**: `holdFacingMaxAngle`
(40 deg) keeps her facing the Mirror while she walks roughly at her, and lets her turn to face
travel once she is striding sideways or backing off, which is where the skating actually shows.

**Re-run `Tools ▸ DS2 ▸ Build Move Assets` after pulling this** — the values live in the spec
table and the move assets are generated from it.

**Impact VFX and weapon trails built 16 Sep (Day 14).** `Feel/WeaponTrail.cs` and
`Editor/VfxWiring.cs`. One menu item — `Tools ▸ DS2 ▸ Build VFX` — makes the whole set from
nothing. Notes:

- **The particle textures are generated, not imported.** The Combat Girls pack ships no particle
  art at all (searched: no spark, glow, smoke, flare or streak PNGs anywhere in it), and there is
  no VFX Graph and no Shader Graph in the manifest. So `VfxWiring` writes three procedural PNGs —
  a core flash with four axis spikes, a pointed lens shard, a soft band for the trail — sets their
  import settings, and builds materials from them. **No art dependency to source, and a fresh
  clone rebuilds the entire effect set from the menu.**
- **URP's own `BaseShaderGUI.SetupMaterialBlendMode` sets the additive blend**, rather than hand-
  writing `_SrcBlend` / `_DstBlend` / `_ZWrite` / queue / keywords. Those are hidden properties
  whose correct combination is only defined in URP's editor code, and this project has already
  lost a day to a shader that rendered magenta. `_Surface = 1`, `_Blend = 2`, then let URP do it.
- **The boss is a Prefab Variant of the player**, so the blade trail goes on `KatanaGirl.prefab`
  once and she inherits it; only `telegraph` is overridden on her. Adding it to both would give
  her two trails.
- **Only the boss gets the wind-up colour ramp.** Her blade warms cold→hot across the startup
  window and snaps to the swing colour as the hitbox opens; the player's trail appears with the
  hitbox and nowhere else. This is the same mirror-match rule as the camera shaking for the victim
  — symmetric feedback makes a same-clips-same-silhouette fight unreadable. It also closes
  `build-plan.md` Step 2.3's second-cheapest telegraph, so the trail is **not** pure polish.
- **The parry had no VFX at all.** `SpawnVfx` was only ever called on the damage path; the parry
  branch froze, shook, played a sound and flashed the attacker, and drew nothing. There is now a
  `parryVfx` slot, spawned at the *midpoint* between attacker and contact point — spawning at the
  contact point alone puts the clash inside whoever got parried. It is radial and white where a
  damage hit is directional and pink, because shape reads faster than colour.
- **The trail is cut on a teleport.** A retry moves both actors, and a `TrailRenderer` that is not
  cleared draws a bright line across the arena from where she died to where she respawned. Any
  tip movement over 1.5 m in one frame clears it.
- **`WeaponTrail` runs in `LateUpdate`.** The blade's transform is driven by the Animator, so
  reading the tip any earlier samples the previous frame's pose.
- **Unverified: which end of the blade collider is the point.** The trail sits at local
  `z = +0.56` on the `HitBox` (collider is `0.06 × 0.07 × 1.12`, centred). If it comes off the
  hilt, negate `VfxWiring.BladeTipLocalZ` and re-run. Nothing in the YAML records which end is
  which.

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

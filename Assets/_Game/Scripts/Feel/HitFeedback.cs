using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace DS2
{
    /// <summary>What actually happened when a hitbox overlapped a hurtbox.</summary>
    public enum HitResult
    {
        /// <summary>Nothing - already dead, or the target was in i-frames.</summary>
        NoEffect,

        /// <summary>The target was in invulnerability frames. They dodged it.</summary>
        Evaded,

        /// <summary>The target deflected it. No damage, and the ATTACKER is staggered.</summary>
        Parried,

        Damaged,
        Killed,
    }

    /// <summary>One contact, with everything the feedback layer needs to dramatise it.</summary>
    public struct HitInfo
    {
        public CombatActor attacker, victim;
        public MoveDefinition move;

        /// <summary>World-space contact point, for VFX and for the impulse origin.</summary>
        public Vector3 point;

        public int damage;
        public HitResult result;
        public bool victimIsPlayer;

        /// <summary>0-1 of the victim's posture AFTER this hit. Drives the rising-pitch cue.</summary>
        public float victimPosture;
    }

    /// <summary>
    /// Everything that makes a hit feel like a hit: hit stop, camera, audio, VFX.
    ///
    /// WHY THESE THREE, IN THIS ORDER. Lin et al. (2022) ranked 44 action games on "impact feel"
    /// by NLP over Steam reviews, then compared the top eight against the bottom eight across a
    /// 19-feature framework. Three features separated them: HIT STOP, SOUND COHERENCE and CAMERA
    /// CONTROL, and "a lack of dedicated design on one of these three features may ruin players'
    /// impact feel." Particles, flashes and trails are in the framework but are not what divided
    /// good from bad. So this class owns those three, and the rest is decoration on top.
    ///
    /// THE MIRROR-MATCH RULE. This is a mirror match: both fighters use the same clips, the same
    /// moves and the same silhouette. If hitting her and being hit by her produce the same shake
    /// and the same sound, exchanges turn to mush. So the camera belongs to the VICTIM - a small
    /// impulse when you land a hit, a large one when you take one. Big shake on your own hits is
    /// also exhausting across a ten-death arc.
    ///
    /// THE FATAL TRAP. Hit stop sets Time.timeScale to 0, which also makes Time.deltaTime 0. A
    /// timer written on scaled time therefore never advances and the game freezes FOREVER.
    /// Everything here that counts down through a freeze runs on unscaledDeltaTime. Audio is not
    /// affected by timeScale in Unity, which is exactly what sells the freeze - the world stops
    /// and the impact still rings.
    /// </summary>
    public class HitFeedback : MonoBehaviour
    {
        public static HitFeedback Instance { get; private set; }

        [Header("Hit stop (seconds)")]
        [Tooltip("Guilty Gear Xrd freezes 7 frames on a light attack and about 10 on a heavy. " +
                 "Those are 2D fighters where the freeze is very visible; a 3D game wants less. " +
                 "These are starting points - hit stop is the first thing to tune.")]
        [SerializeField] float lightHitstop = 0.08f;
        [SerializeField] float mediumHitstop = 0.11f;
        [SerializeField] float heavyHitstop = 0.16f;

        [Tooltip("Damage at or above this counts as heavy. Below mediumDamage counts as light.")]
        [SerializeField] int mediumDamage = 10;
        [SerializeField] int heavyDamage = 18;

        [Tooltip("Taking a hit freezes slightly less than landing one, so being hit stays " +
                 "readable without stealing the feeling of connecting.")]
        [Range(0f, 1f)] [SerializeField] float hitstopWhenVictimIsPlayer = 0.8f;

        [Header("Camera - the impulse belongs to the victim")]
        [Tooltip("Impulse when the PLAYER lands a hit. Deliberately small: shaking the camera " +
                 "hard every time you connect is exhausting over a ten-death arc.")]
        [SerializeField] float traumaDealing = 0.25f;

        [Tooltip("Impulse when the player TAKES a hit. Large, because the camera reacting is the " +
                 "fastest way to tell the two directions of a mirror match apart.")]
        [SerializeField] float traumaTaking = 0.6f;

        [Tooltip("Shake scales as trauma^2, not linearly (Eiserloh, GDC 2016). The curve is why " +
                 "escalation reads: trauma 0.3/0.6/0.9 gives roughly 3%/22%/73% of full shake.")]
        [SerializeField] float traumaExponent = 2f;

        [SerializeField] float impulseScale = 0.35f;

        [Header("Posture break - the moment")]
        [Tooltip("The design's whole thesis is that aggression is the correct defense, and the " +
                 "break is its payoff. It gets a real freeze, then slow motion.")]
        [SerializeField] float breakHitstop = 0.3f;
        [SerializeField] float breakSlowMoDuration = 0.55f;
        [Range(0.05f, 1f)] [SerializeField] float breakSlowMoScale = 0.35f;
        [SerializeField] float breakTrauma = 0.9f;

        [Header("Parry")]
        [Tooltip("A parry is the biggest thing a player can do on defence, so it freezes hardest " +
                 "of anything except a posture break. This is the moment the whole mechanic sells " +
                 "itself on.")]
        [SerializeField] float parryHitstop = 0.22f;
        [SerializeField] float parryTrauma = 0.7f;

        [Header("Death")]
        [SerializeField] float deathHitstop = 0.2f;
        [SerializeField] float deathSlowMoDuration = 1.2f;
        [Range(0.05f, 1f)] [SerializeField] float deathSlowMoScale = 0.3f;
        [SerializeField] float deathTrauma = 0.6f;

        [Header("Audio - clips are optional, silence is never an error")]
        [SerializeField] ImpactAudio sfx = new();

        [Header("VFX")]
        [Tooltip("Optional. Spawned at the contact point, unlit/additive to match the toon look.")]
        [SerializeField] ParticleSystem impactVfx;
        [SerializeField] ParticleSystem heavyImpactVfx;

        [Tooltip("The blades meeting. Deliberately a different shape from a damage hit - radial " +
                 "and white rather than directional and pink - because a parry is the one " +
                 "outcome the player most needs to recognise instantly, and it is the only one " +
                 "that used to produce no visual at all.")]
        [SerializeField] ParticleSystem parryVfx;

        [Tooltip("Spawned when a swing passes through i-frames. THIS IS NOT DECORATION. Without " +
                 "it an evaded hit is completely silent and invisible - no damage, no sound, no " +
                 "flash - and the first playtester read a boss dodging his chain as a broken " +
                 "hitbox, which cost a day chasing physics. A dodge has to look like a dodge.")]
        [SerializeField] ParticleSystem evadeVfx;

        [Tooltip("OFF by design. The dodge is shown by MOVEMENT - a reactive dodge backsteps " +
                 "2.82 m, which is unmistakable - and a flash on top annotates something the " +
                 "player can already see. Kept because the player's own evade is in place " +
                 "(RootXZNet 0.000) and may still want a tell if that ever reads as unresponsive.")]
        [SerializeField] bool flashOnEvade;

        [Tooltip("Cold, where damage is warm, so the two are tellable apart in one frame.")]
        [SerializeField] Color evadeFlashColor = new(0.55f, 0.85f, 1f);

        [Header("Debug")]
        [SerializeField] bool logHits;

        [Tooltip("Logs every freeze and slow-motion with an UNSCALED timestamp and what caused " +
                 "it. Turn this on to find out which effect you are actually seeing: a hit " +
                 "freeze, a posture break, or the death slow motion are easy to confuse when " +
                 "none of them has a sound yet.")]
        [SerializeField] bool logTimeline;

        CinemachineImpulseSource impulse;

        // Hit stop bookkeeping. A running freeze remembers the scale it interrupted rather than
        // assuming 1, so a hit landed during the posture-break slow motion restores the slow
        // motion instead of cancelling it.
        float freezeEndsAt;
        float restoreScaleTo = 1f;
        float slowMoEndsAt;
        float slowMoScale = 1f;

        readonly List<PostureSystem> subscribedPosture = new();
        readonly List<CombatActor> subscribedActors = new();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[HitFeedback] A second instance exists; disabling this one.", this);
                enabled = false;
                return;
            }
            Instance = this;

            impulse = GetComponent<CinemachineImpulseSource>();
            if (impulse == null)
                Debug.LogWarning("[HitFeedback] No CinemachineImpulseSource - camera shake is off. " +
                                 "Run Tools > DS2 > Wire Feel.", this);

            sfx.Initialise(gameObject);
            SubscribeAll();
        }

        void OnDestroy()
        {
            UnsubscribeAll();
            if (Instance == this)
            {
                Instance = null;
                // Never leave the game frozen because the scene unloaded mid-freeze.
                Time.timeScale = 1f;
            }
        }

        /// <summary>
        /// Finds the posture and death events in the scene. Done here rather than from each actor
        /// so nothing in the combat code has to know the feedback layer exists.
        /// </summary>
        void SubscribeAll()
        {
            foreach (PostureSystem posture in FindObjectsByType<PostureSystem>(FindObjectsSortMode.None))
            {
                posture.Broken += OnPostureBroken;
                posture.Recovered += OnPostureRecovered;
                subscribedPosture.Add(posture);
            }

            foreach (CombatActor actor in FindObjectsByType<CombatActor>(FindObjectsSortMode.None))
            {
                actor.Died += OnDied;
                subscribedActors.Add(actor);
            }
        }

        void UnsubscribeAll()
        {
            foreach (PostureSystem p in subscribedPosture)
            {
                if (p == null) continue;
                p.Broken -= OnPostureBroken;
                p.Recovered -= OnPostureRecovered;
            }
            subscribedPosture.Clear();

            foreach (CombatActor a in subscribedActors)
                if (a != null) a.Died -= OnDied;
            subscribedActors.Clear();
        }

        /// <summary>
        /// Re-scans for actors and posture systems. Week 3's retry loop resets the fight in place
        /// or reloads the scene; either way anything created after Awake needs picking up, and a
        /// boss whose death is silent is a very confusing bug to chase.
        /// </summary>
        public void Rescan()
        {
            UnsubscribeAll();
            SubscribeAll();
        }

        void Update()
        {
            // unscaledDeltaTime, ALWAYS. With timeScale at 0 a scaled timer never advances and
            // the freeze becomes permanent.
            if (freezeEndsAt > 0f && Time.unscaledTime >= freezeEndsAt)
            {
                freezeEndsAt = 0f;
                Time.timeScale = restoreScaleTo;
                if (logTimeline)
                    Debug.Log($"[Feel] t={Time.unscaledTime:0.000} freeze released -> timeScale {restoreScaleTo:0.00}", this);
            }

            if (slowMoEndsAt > 0f && Time.unscaledTime >= slowMoEndsAt)
            {
                slowMoEndsAt = 0f;
                if (logTimeline)
                    Debug.Log($"[Feel] t={Time.unscaledTime:0.000} slowmo released", this);
                slowMoScale = 1f;
                if (freezeEndsAt > 0f) restoreScaleTo = 1f;
                else Time.timeScale = 1f;
            }
        }

        // ------------------------------------------------------------------ entry points

        /// <summary>Called by Hitbox for every contact, landed or evaded.</summary>
        public static void Report(in HitInfo hit)
        {
            if (Instance != null && Instance.enabled) Instance.Impact(hit);
        }

        /// <summary>
        /// A move's wind-up whoosh. Static like Report so nothing in the combat code needs a
        /// reference to the feedback layer, and silent if no HitFeedback is in the scene.
        /// </summary>
        public static void Swing(AudioClip clip, Vector3 at, float speedMultiplier)
        {
            if (Instance != null && Instance.enabled) Instance.sfx.PlaySwing(clip, at, speedMultiplier);
        }

        void Impact(in HitInfo hit)
        {
            if (logTimeline)
                Debug.Log($"[Feel] t={Time.unscaledTime:0.000} CONTACT " +
                          $"{(hit.move != null ? hit.move.moveId.ToString() : "?")} " +
                          $"attacker={(hit.attacker != null ? hit.attacker.name : "?")} " +
                          $"moveProgress={(hit.attacker != null ? hit.attacker.MoveProgress : 0f):0.00} " +
                          $"result={hit.result}", this);

            if (logHits)
                Debug.Log($"[HitFeedback] {hit.result} {hit.damage} on {hit.victim?.name} at {hit.point}", this);

            if (hit.result == HitResult.Parried)
            {
                Freeze(parryHitstop, "PARRY by " + hit.victim.name);
                Shake(parryTrauma, hit.point);
                sfx.PlayParry(hit.point);

                // Flash the ATTACKER, not the victim - the deflection happened to her.
                if (hit.attacker != null && hit.attacker.TryGetComponent(out HitFlash flash))
                    flash.Play();

                // The clash happens BETWEEN the two of them, not on the victim: spawning it at
                // the contact point alone puts it inside whoever was parried.
                Spawn(parryVfx, hit, Midpoint(hit));
                return;
            }

            if (hit.result == HitResult.Evaded)
            {
                // The dodge worked. Saying so is the player's confirmation - silence reads as the
                // game failing to notice rather than as a success.
                //
                // That was written assuming the audio would exist. It does not yet, and with no
                // visual either this branch produced NOTHING AT ALL: a boss dodging mid-chain was
                // indistinguishable from the hitbox failing, which is exactly how the first
                // playtest read it. Flash and VFX carry it now, clip or no clip.
                sfx.PlayEvaded(hit.point);

                // Flash the DODGER - the evasion is hers, the same way damage feedback belongs to
                // whoever took it. Cold colour, so it never reads as a hit landing. Off by
                // default: the backstep carries this now.
                if (flashOnEvade && hit.victim != null &&
                    hit.victim.TryGetComponent(out HitFlash evaded))
                    evaded.Play(evadeFlashColor);

                Spawn(evadeVfx, hit, hit.point);
                return;
            }

            if (hit.result != HitResult.Damaged && hit.result != HitResult.Killed) return;

            Freeze(HitstopFor(hit), "impact " + (hit.move != null ? hit.move.moveId.ToString() : "?") +
                                     " dmg=" + hit.damage + " -> " + hit.victim.name);
            Shake(TraumaFor(hit), hit.point);
            sfx.PlayImpact(hit, hit.damage >= heavyDamage);
            SpawnVfx(hit);
        }

        /// <summary>
        /// Who was hit decides the base - the mirror-match rule - and how hard scales it. A heavy
        /// skill landing should move the camera more than a jab, but never as much as taking one.
        /// </summary>
        float TraumaFor(in HitInfo hit)
        {
            if (hit.move != null && hit.move.traumaOverride > 0f) return hit.move.traumaOverride;

            float baseTrauma = hit.victimIsPlayer ? traumaTaking : traumaDealing;
            float tier =
                hit.damage >= heavyDamage ? 1.6f :
                hit.damage >= mediumDamage ? 1.25f :
                1f;

            return Mathf.Clamp01(baseTrauma * tier);
        }

        float HitstopFor(in HitInfo hit)
        {
            float seconds =
                hit.move != null && hit.move.hitstopOverride > 0f ? hit.move.hitstopOverride :
                hit.damage >= heavyDamage ? heavyHitstop :
                hit.damage >= mediumDamage ? mediumHitstop :
                lightHitstop;

            if (hit.victimIsPlayer) seconds *= hitstopWhenVictimIsPlayer;
            return seconds;
        }

        void OnPostureBroken(PostureSystem posture)
        {
            Vector3 at = posture.transform.position + Vector3.up;

            Freeze(breakHitstop, "POSTURE BREAK on " + posture.name);
            SlowMo(breakSlowMoScale, breakSlowMoDuration, "posture break");
            Shake(breakTrauma, at);
            sfx.PlayPostureBreak(at);
        }

        /// <summary>
        /// The opening closing again. SOUND ONLY - no freeze, no shake, no slow motion.
        ///
        /// PostureSystem fires Recovered when the stagger ends, which is the moment the player's
        /// pressure came to nothing. Spending a hit stop on that would dress up a failure as an
        /// event, and hit stop is the game's way of saying something landed.
        /// </summary>
        void OnPostureRecovered(PostureSystem posture)
        {
            sfx.PlayPostureRecover(posture.transform.position + Vector3.up);
        }

        void OnDied(CombatActor actor)
        {
            Vector3 at = actor.transform.position + Vector3.up;

            Freeze(deathHitstop, "DEATH of " + actor.name);
            SlowMo(deathSlowMoScale, deathSlowMoDuration, "death");
            Shake(deathTrauma, at);
            sfx.PlayDeath(at);
        }

        // ------------------------------------------------------------------ effects

        /// <summary>
        /// Freezes both actors at the point of collision. Freezing BOTH is the point - it is what
        /// sells the contact, and it is what every fighting game since Street Fighter 2 does.
        ///
        /// Side effect worth knowing: because CombatActor.moveTimer runs on scaled time, a freeze
        /// also postpones the cancel window by its own duration. Street Fighter 2 relied on
        /// exactly this to keep combo timing consistent. It is a feature, not a bug.
        /// </summary>
        public void Freeze(float seconds, string reason = "")
        {
            if (seconds <= 0f) return;

            float end = Time.unscaledTime + seconds;
            if (end <= freezeEndsAt) return;   // a longer freeze is already running

            if (logTimeline)
                Debug.Log($"[Feel] t={Time.unscaledTime:0.000} FREEZE {seconds:0.000}s  {reason}", this);

            // Remember what to go back to, so a hit during slow motion does not cancel it.
            if (freezeEndsAt <= 0f) restoreScaleTo = slowMoEndsAt > 0f ? slowMoScale : 1f;

            freezeEndsAt = end;
            Time.timeScale = 0f;
        }

        public void SlowMo(float scale, float duration, string reason = "")
        {
            if (duration <= 0f) return;

            if (logTimeline)
                Debug.Log($"[Feel] t={Time.unscaledTime:0.000} SLOWMO {duration:0.000}s @{scale:0.00}  {reason}", this);

            slowMoScale = Mathf.Clamp(scale, 0.01f, 1f);
            slowMoEndsAt = Time.unscaledTime + duration;

            if (freezeEndsAt > 0f) restoreScaleTo = slowMoScale;
            else Time.timeScale = slowMoScale;
        }

        /// <summary>
        /// Camera shake, via Cinemachine Impulse - which already implements what Eiserloh's GDC
        /// talk prescribes (6D Perlin noise rather than random, an envelope, spatial falloff).
        /// What is applied on top is his other two lessons: magnitude scales as trauma^2 rather
        /// than linearly, and the noise profile should be rotation-dominant, because translational
        /// shake in 3D "feels super lame."
        /// </summary>
        public void Shake(float trauma, Vector3 at)
        {
            if (impulse == null || trauma <= 0f) return;

            float magnitude = Mathf.Pow(Mathf.Clamp01(trauma), traumaExponent) * impulseScale;
            if (magnitude <= 0.0001f) return;

            impulse.GenerateImpulseAtPositionWithVelocity(at, Random.onUnitSphere * magnitude);
        }

        void SpawnVfx(in HitInfo hit)
        {
            ParticleSystem prefab = hit.damage >= heavyDamage && heavyImpactVfx != null
                ? heavyImpactVfx
                : impactVfx;
            Spawn(prefab, hit, hit.point);
        }

        /// <summary>Contact point nudged back toward the attacker - where the blades actually met.</summary>
        static Vector3 Midpoint(in HitInfo hit)
        {
            if (hit.attacker == null) return hit.point;
            Vector3 chest = hit.attacker.transform.position + Vector3.up * 1.2f;
            return Vector3.Lerp(hit.point, chest, 0.35f);
        }

        void Spawn(ParticleSystem prefab, in HitInfo hit, Vector3 at)
        {
            if (prefab == null) return;

            ParticleSystem fx = Instantiate(prefab, at, Quaternion.identity);

            // Face the effect back along the blow, so it reads as coming from the attacker.
            if (hit.attacker != null)
            {
                Vector3 away = at - hit.attacker.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f) fx.transform.rotation = Quaternion.LookRotation(away);
            }

            // constantMax reads 0 for curve-based lifetimes, so floor it rather than deleting
            // the effect before it has played.
            ParticleSystem.MainModule main = fx.main;
            Destroy(fx.gameObject, main.duration + Mathf.Max(0.5f, main.startLifetime.constantMax));
        }
    }
}

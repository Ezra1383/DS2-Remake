using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Boss-only. The player has no stamina and no posture - what limits them is animation
    /// commitment. The pressure runs the other way, onto her.
    ///
    /// Posture decays, so chip damage from a safe poke-and-retreat game goes nowhere: the meter
    /// drains faster than that pace can fill it. The only way to break her is sustained pressure,
    /// which makes aggression the correct defense - and the player discovers that themselves
    /// rather than being told.
    ///
    /// Tuned against the measured chain: three slashes land 52 posture over 2.30 s (22.6/s), so a
    /// decay of 8/s leaves a healthy margin for committed play and none for poking.
    /// </summary>
    [RequireComponent(typeof(CombatActor))]
    public class PostureSystem : MonoBehaviour
    {
        [Header("Meter")]
        [SerializeField] float maxPosture = 100f;

        [Tooltip("Per second. The dial that decides whether aggression is actually rewarded. " +
                 "Raise it and only perfect pressure breaks her; lower it and poking works.")]
        [SerializeField] float decayPerSecond = 8f;

        [Tooltip("Grace after the last hit before decay starts, so a chain is not punished for " +
                 "its own recovery frames.")]
        [SerializeField] float decayDelay = 1.5f;

        [Header("Break")]
        [Tooltip("Long enough for one full three-slash chain (2.30 s measured). That is why the " +
                 "chain exists.")]
        [SerializeField] float stunDuration = 2.5f;

        [SerializeField] float stunDamageMultiplier = 2f;

        [Header("Debug")]
        [SerializeField] bool logPosture;

        CombatActor actor;

        float posture;
        float lastHitTime = float.NegativeInfinity;

        /// <summary>0-1, for the HUD meter in Week 4.</summary>
        public float Normalized => maxPosture > 0f ? posture / maxPosture : 0f;
        public bool IsStunned { get; private set; }

        public event System.Action<PostureSystem> Broken;
        public event System.Action<PostureSystem> Recovered;

        void Awake()
        {
            actor = GetComponent<CombatActor>();
            actor.StaggerEnded += OnStaggerEnded;
        }

        void OnDestroy()
        {
            if (actor != null) actor.StaggerEnded -= OnStaggerEnded;
        }

        void OnStaggerEnded(CombatActor _)
        {
            // A parry also staggers, and that one owes nothing to the meter.
            if (IsStunned) Recover();
        }

        void Update()
        {
            if (actor.IsDead) return;

            // CombatActor owns the stun clock now; Recover is driven by its StaggerEnded event.
            if (IsStunned) return;

            if (Time.time - lastHitTime < decayDelay) return;
            posture = Mathf.Max(0f, posture - decayPerSecond * Time.deltaTime);
        }

        /// <summary>Called by Hitbox when a move with postureDamage connects.</summary>
        public void Add(int amount)
        {
            if (actor.IsDead || IsStunned || amount <= 0) return;

            posture = Mathf.Min(maxPosture, posture + amount);
            lastHitTime = Time.time;

            if (logPosture)
                Debug.Log($"[{name}] posture +{amount} = {posture:0}/{maxPosture:0}", this);

            if (posture >= maxPosture) Break();
        }

        void Break()
        {
            IsStunned = true;

            // Whatever she was doing, she is not doing it any more. Breaking her mid-swing is
            // the reward for pressuring through an attack rather than backing off from it.
            actor.Stagger(stunDuration, stunDamageMultiplier);

            if (logPosture) Debug.Log($"[{name}] POSTURE BREAK - stunned {stunDuration}s", this);
            Broken?.Invoke(this);
        }

        /// <summary>Wipes the meter for a retry. Does not fire Recovered - nothing broke.</summary>
        public void ResetPosture()
        {
            posture = 0f;
            IsStunned = false;
            lastHitTime = float.NegativeInfinity;
        }

        void Recover()
        {
            IsStunned = false;
            posture = 0f;
            lastHitTime = Time.time;

            if (logPosture) Debug.Log($"[{name}] recovered", this);
            Recovered?.Invoke(this);
        }
    }
}

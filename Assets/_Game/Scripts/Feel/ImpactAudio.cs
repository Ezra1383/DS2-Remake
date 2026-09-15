using UnityEngine;

namespace DS2
{
    /// <summary>
    /// The audio half of impact feedback. Serialized inside HitFeedback rather than being its own
    /// component, because these clips are one decision, not eight.
    ///
    /// SOUND COHERENCE is the second of the three features Lin et al. found decisive for impact
    /// feel, and it is the cheapest of them to get wrong: the sound has to arrive with the hit
    /// (inside ~12 ms, and certainly inside the 100 ms at which players start perceiving lag) and
    /// it has to differ meaningfully between events. A single clip on every slash fatigues within
    /// a minute, which is why every slot takes an array and picks at random with a little pitch
    /// jitter.
    ///
    /// EVERY CLIP IS OPTIONAL. An empty slot is silent and never logs - the system ships before
    /// the audio does, and a console full of warnings would just train you to ignore the console.
    /// </summary>
    [System.Serializable]
    public class ImpactAudio
    {
        [Header("Impact")]
        [Tooltip("Normal slashes landing. Sharp transient plus a little low end for weight.")]
        [SerializeField] AudioClip[] impact = System.Array.Empty<AudioClip>();

        [Tooltip("Specials landing. Heavier, longer tail.")]
        [SerializeField] AudioClip[] heavyImpact = System.Array.Empty<AudioClip>();

        [Tooltip("An attack that passed through i-frames. This is the player's confirmation that " +
                 "their dodge worked - without it a successful dodge is indistinguishable from " +
                 "the game not noticing.")]
        [SerializeField] AudioClip[] evaded = System.Array.Empty<AudioClip>();

        [Tooltip("A deflection. This is the single most rewarding sound in a game with a parry - " +
                 "bright, metallic, immediate. Worth more care than any other clip here.")]
        [SerializeField] AudioClip[] parry = System.Array.Empty<AudioClip>();

        [Header("Events")]
        [Tooltip("Must be unmistakable and used nowhere else - glass, a bell, a metal snap. It is " +
                 "the single most important sound in the game.")]
        [SerializeField] AudioClip postureBreak;

        [SerializeField] AudioClip death;

        [Header("Mix")]
        [SerializeField] [Range(0f, 1f)] float volume = 0.9f;

        [Tooltip("Plus or minus, per play, so repeated hits never sound mechanical.")]
        [SerializeField] [Range(0f, 0.3f)] float pitchJitter = 0.05f;

        [Header("Posture - making an invisible meter audible")]
        [Tooltip("ON by default, and the best value in this file. Impact pitch rises as her " +
                 "posture fills, so the player can HEAR the break coming long before the Week 4 " +
                 "HUD exists. It teaches 'aggression is the correct defense' through feel rather " +
                 "than through a number on screen.")]
        [SerializeField] bool pitchWithPosture = true;

        [Tooltip("Pitch multiplier at full posture. 1 disables the ramp.")]
        [SerializeField] [Range(1f, 2f)] float posturePitchAtFull = 1.35f;

        [Header("Pool")]
        [Tooltip("Concurrent one-shots. Too few and a fast chain cuts its own sounds off.")]
        [SerializeField] int voices = 8;

        AudioSource[] sources;
        int next;

        public void Initialise(GameObject host)
        {
            voices = Mathf.Max(2, voices);
            sources = new AudioSource[voices];

            // A pool rather than AudioSource.PlayClipAtPoint, which allocates and destroys a
            // GameObject per call - which in a fight is a lot of garbage per second.
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(host.transform, false);

                AudioSource src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;

                // Mostly 2D. Combat audio wants to sit in front of the player rather than pan
                // around the arena; a hit that drifts to one ear reads as further away than it is.
                src.spatialBlend = 0.25f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.maxDistance = 25f;

                // Unity does not scale audio by timeScale, so impacts keep ringing through hit
                // stop. That is precisely what sells the freeze.
                sources[i] = src;
            }
        }

        public void PlayImpact(in HitInfo hit, bool heavy)
        {
            // A move may name its own impact sound; otherwise it draws from the shared bank.
            AudioClip clip = hit.move != null && hit.move.impactSound != null
                ? hit.move.impactSound
                : Pick(heavy && heavyImpact.Length > 0 ? heavyImpact : impact);

            if (clip == null) return;

            float pitch = 1f;
            if (pitchWithPosture && !hit.victimIsPlayer)
                pitch *= Mathf.Lerp(1f, posturePitchAtFull, Mathf.Clamp01(hit.victimPosture));

            Play(clip, hit.point, pitch);
        }

        public void PlayEvaded(Vector3 at) => Play(Pick(evaded), at, 1f);
        public void PlayParry(Vector3 at) => Play(Pick(parry), at, 1f);
        public void PlayPostureBreak(Vector3 at) => Play(postureBreak, at, 1f);
        public void PlayDeath(Vector3 at) => Play(death, at, 1f);

        /// <summary>Plays a move's own swing clip. Called by CombatActor from a normalized window.</summary>
        public void PlaySwing(AudioClip clip, Vector3 at, float speedMultiplier)
        {
            // Pitch with playback rate, or a swing sped up by the global tempo sounds like it
            // belongs to a slower animation than the one on screen.
            Play(clip, at, Mathf.Lerp(1f, speedMultiplier, 0.5f));
        }

        void Play(AudioClip clip, Vector3 at, float pitch)
        {
            if (clip == null || sources == null) return;

            AudioSource src = sources[next];
            next = (next + 1) % sources.Length;

            src.transform.position = at;
            src.pitch = pitch * (1f + Random.Range(-pitchJitter, pitchJitter));
            src.PlayOneShot(clip, volume);
        }

        AudioClip Pick(AudioClip[] bank)
        {
            if (bank == null || bank.Length == 0) return null;
            return bank[Random.Range(0, bank.Length)];
        }
    }
}

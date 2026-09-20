using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Background music, with an optional second track that takes over when the Mirror wakes.
    ///
    /// Assign one clip and it simply loops. Assign two and the arena is quiet-ish until the player
    /// crosses into the fight, at which point the tracks cross-fade - which costs nothing here
    /// because BossGate already knows the exact moment, and it makes walking in feel like a door
    /// closing behind you.
    ///
    /// EVERYTHING FADES ON UNSCALED TIME. Unity does not slow audio with Time.timeScale, so music
    /// keeps playing through a hit stop - but a fade driven by scaled time would freeze mid-blend
    /// on every connect and, during the 1.2 s death slow-motion, crawl. Same trap HitFeedback and
    /// the HUD already document.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        [Header("Tracks - both optional")]
        [Tooltip("Plays from the start. Leave empty for silence until the fight begins.")]
        [SerializeField] AudioClip explorationTrack;

        [Tooltip("Takes over when the boss wakes. Leave empty to keep the first track playing.")]
        [SerializeField] AudioClip combatTrack;

        [Header("Mix")]
        // 0.45 -> 0.5625 on 20 Sep, a quarter up, after the SFX bank came down to 0.675.
        [Range(0f, 1f)] [SerializeField] float volume = 0.5625f;
        [Tooltip("Seconds to cross-fade between tracks.")]
        [SerializeField] float crossfade = 2.5f;

        [Header("Wiring - resolved at Awake if empty")]
        [SerializeField] BossGate gate;

        AudioSource current, next;
        bool switched;
        float blend;

        void Awake()
        {
            // Same duplicate rule as DeathScreen and ProgressionManager: destroy the COMPONENT,
            // never the GameObject, because this may well be sitting on something that owns
            // other things. That mistake once deleted the arena floor.
            if (FindObjectsByType<MusicPlayer>(FindObjectsInactive.Include).Length > 1
                && this != FindAnyObjectByType<MusicPlayer>())
            {
                Destroy(this);
                return;
            }

            if (gate == null) gate = FindAnyObjectByType<BossGate>();

            current = MakeSource();
            next = MakeSource();
        }

        AudioSource MakeSource()
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;

            // Fully 2D. Music has no position in the world, and any spatial blend at all would
            // pan it as the camera orbits her.
            src.spatialBlend = 0f;
            src.volume = 0f;
            return src;
        }

        void Start()
        {
            if (explorationTrack != null)
            {
                current.clip = explorationTrack;
                current.volume = volume;
                current.Play();
            }
            else if (combatTrack != null)
            {
                // Only a combat track was given: treat it as the one and only track.
                current.clip = combatTrack;
                current.volume = volume;
                current.Play();
                switched = true;
            }
        }

        void Update()
        {
            if (!switched && combatTrack != null && gate != null && gate.IsAwake)
                BeginSwitch();

            if (blend <= 0f) return;

            blend -= Time.unscaledDeltaTime;
            float t = crossfade > 0f ? Mathf.Clamp01(1f - blend / crossfade) : 1f;

            next.volume = volume * t;
            current.volume = volume * (1f - t);

            if (blend > 0f) return;

            current.Stop();
            (current, next) = (next, current);
        }

        void BeginSwitch()
        {
            switched = true;

            if (current.clip == combatTrack) return;   // nothing to change to

            next.clip = combatTrack;
            next.volume = 0f;
            next.Play();

            blend = Mathf.Max(0.01f, crossfade);
        }
    }
}

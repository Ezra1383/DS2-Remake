using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Makes a light behave like fire. Without this a "firelit" room is lit by a set of perfectly
    /// steady orange bulbs, which reads as coloured lighting rather than as flame - the movement
    /// is what your eye identifies as fire, far more than the colour is.
    ///
    /// Perlin noise rather than Random.value per frame: white noise flickers at the frame rate and
    /// looks like a fault, while Perlin wanders, which is what a flame actually does. Each light
    /// gets its own seed so a room full of them never pulses in unison.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class FireLight : MonoBehaviour
    {
        [SerializeField] Light target;

        [Tooltip("Intensity this flickers around. The tool sets it from the light it created.")]
        [SerializeField] float baseIntensity = 4f;

        [Tooltip("Fraction of base intensity the flicker swings, plus or minus.")]
        [Range(0f, 1f)] [SerializeField] float amplitude = 0.35f;

        [Tooltip("Higher is twitchier. Around 6-9 reads as an open flame; 2-3 as embers.")]
        [SerializeField] float speed = 7f;

        [Tooltip("Small positional wander. Shifts where the shadows fall, which sells it further " +
                 "than intensity alone - but keep it small or the light visibly detaches.")]
        [SerializeField] float positionJitter = 0.06f;

        Vector3 basePosition;
        float seedA, seedB;

        void Awake()
        {
            if (target == null) target = GetComponent<Light>();
            if (baseIntensity <= 0f) baseIntensity = target.intensity;

            basePosition = transform.localPosition;

            // Per-instance offsets into the noise field, so twelve fires are twelve fires.
            seedA = Random.value * 1000f;
            seedB = Random.value * 1000f;
        }

        void Update()
        {
            // SCALED time, deliberately - unlike HitFeedback and the HUD, which must keep running
            // through a freeze. Hit stop is meant to stop the world; a flame still dancing while
            // both fighters are frozen reads as the characters having hitched, not as time
            // stopping. This is the one place in the project where scaled time is the right call.
            float t = Time.time * speed;

            float flicker = Mathf.PerlinNoise(seedA, t) - 0.5f;
            target.intensity = Mathf.Max(0f, baseIntensity * (1f + flicker * 2f * amplitude));

            if (positionJitter > 0f)
            {
                float x = Mathf.PerlinNoise(seedB, t) - 0.5f;
                float y = Mathf.PerlinNoise(seedB + 37f, t) - 0.5f;
                float z = Mathf.PerlinNoise(seedB + 73f, t) - 0.5f;

                transform.localPosition = basePosition + new Vector3(x, y, z) * positionJitter * 2f;
            }
        }

        /// <summary>Called by the wiring tool so the flicker centres on the intensity it set.</summary>
        public void SetBaseIntensity(float value) => baseIntensity = value;
    }
}

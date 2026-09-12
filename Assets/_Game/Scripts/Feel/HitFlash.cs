using UnityEngine;

namespace DS2
{
    /// <summary>
    /// Whites out the character for a moment when they are hit.
    ///
    /// THE TOON-SHADER CATCH. UTS does not light a surface from one colour. It has _BaseColor for
    /// the lit region and _1st_ShadeColor / _2nd_ShadeColor for the two shaded bands, and the
    /// shade colours are NOT derived from the base. Tint only _BaseColor and the lit half flashes
    /// while the shadowed half stays dark, which reads as a rendering bug rather than as a hit.
    /// All three are driven together here. Each is checked with HasProperty first, because the
    /// face, eye and hair materials on this character do not all expose the same set.
    ///
    /// Runs on SCALED time deliberately: the flash holds through hit stop and resolves afterwards,
    /// so the freeze and the flash read as one event rather than two.
    /// </summary>
    public class HitFlash : MonoBehaviour
    {
        [SerializeField] Color flashColor = Color.white;

        [Tooltip("Seconds. 50-100 ms is the usable band - below about 30 ms it goes unnoticed, " +
                 "above 150 ms it smears and stops reading as an impact.")]
        [SerializeField] float duration = 0.08f;

        [Tooltip("How far toward the flash colour at full strength. 1 is a total white-out, " +
                 "which on a toon character usually loses the silhouette.")]
        [Range(0f, 1f)] [SerializeField] float strength = 0.75f;

        [Tooltip("Shape of the fade. Flat at the start then falling reads as a strike; a " +
                 "symmetric curve reads as a pulse.")]
        [SerializeField] AnimationCurve falloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Color1 = Shader.PropertyToID("_Color");
        static readonly int Shade1 = Shader.PropertyToID("_1st_ShadeColor");
        static readonly int Shade2 = Shader.PropertyToID("_2nd_ShadeColor");

        struct Target
        {
            public Renderer renderer;
            public bool hasBase, hasColor, hasShade1, hasShade2;
            public Color baseColor, color, shade1, shade2;
        }

        Target[] targets;
        MaterialPropertyBlock block;
        float remaining;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            Cache();

            CombatActor actor = GetComponent<CombatActor>();
            if (actor != null) actor.Damaged += OnDamaged;
        }

        void OnDestroy()
        {
            CombatActor actor = GetComponent<CombatActor>();
            if (actor != null) actor.Damaged -= OnDamaged;
        }

        /// <summary>
        /// Reads every renderer's original colours once. Done from sharedMaterial so nothing is
        /// instantiated - the tint is applied through a MaterialPropertyBlock, which leaves the
        /// asset untouched and survives a domain reload.
        /// </summary>
        void Cache()
        {
            Renderer[] found = GetComponentsInChildren<Renderer>(true);
            targets = new Target[found.Length];

            for (int i = 0; i < found.Length; i++)
            {
                Material mat = found[i].sharedMaterial;
                var t = new Target { renderer = found[i] };

                if (mat != null)
                {
                    t.hasBase = mat.HasProperty(BaseColor);
                    t.hasColor = mat.HasProperty(Color1);
                    t.hasShade1 = mat.HasProperty(Shade1);
                    t.hasShade2 = mat.HasProperty(Shade2);

                    if (t.hasBase) t.baseColor = mat.GetColor(BaseColor);
                    if (t.hasColor) t.color = mat.GetColor(Color1);
                    if (t.hasShade1) t.shade1 = mat.GetColor(Shade1);
                    if (t.hasShade2) t.shade2 = mat.GetColor(Shade2);
                }

                targets[i] = t;
            }
        }

        void OnDamaged(CombatActor actor, MoveDefinition move) => Play();

        public void Play()
        {
            remaining = duration;
            Apply(1f);
        }

        void Update()
        {
            if (remaining <= 0f) return;

            remaining -= Time.deltaTime;
            if (remaining <= 0f)
            {
                remaining = 0f;
                Clear();
                return;
            }

            Apply(falloff.Evaluate(1f - remaining / duration));
        }

        void Apply(float amount)
        {
            float t = Mathf.Clamp01(amount) * strength;

            foreach (Target target in targets)
            {
                if (target.renderer == null) continue;

                target.renderer.GetPropertyBlock(block);
                if (target.hasBase) block.SetColor(BaseColor, Color.Lerp(target.baseColor, flashColor, t));
                if (target.hasColor) block.SetColor(Color1, Color.Lerp(target.color, flashColor, t));
                if (target.hasShade1) block.SetColor(Shade1, Color.Lerp(target.shade1, flashColor, t));
                if (target.hasShade2) block.SetColor(Shade2, Color.Lerp(target.shade2, flashColor, t));
                target.renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// Clears the block entirely rather than writing the originals back, so the renderers go
        /// back onto the SRP Batcher fast path between flashes instead of staying off it.
        /// </summary>
        void Clear()
        {
            block.Clear();
            foreach (Target target in targets)
                if (target.renderer != null)
                    target.renderer.SetPropertyBlock(null);
        }
    }
}

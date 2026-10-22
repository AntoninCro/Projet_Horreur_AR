using UnityEngine;

namespace HorrorMR
{
    /// <summary>
    /// Lampe torche tenue dans la main gauche. Éclaire le monstre (vraie lumière URP) et "perce"
    /// l'obscurité du passthrough (valeurs lues par DarknessController pour les shaders).
    /// </summary>
    public class Flashlight : MonoBehaviour
    {
        public Light spot;
        public Renderer lens;
        public float intensity = 12f;
        public float range = 15f;
        public float outerAngle = 46f;
        public float innerAngle = 24f;

        public bool IsOn { get; private set; }
        /// <summary>Intensité effective 0..1 (scintillement compris).</summary>
        public float Output { get; private set; }

        float m_FlickerUntil;
        float m_NextRandomFlicker;
        float m_Nervousness;
        MaterialPropertyBlock m_Block;
        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            if (spot != null)
            {
                spot.type = LightType.Spot;
                spot.range = range;
                spot.spotAngle = outerAngle;
                spot.innerSpotAngle = innerAngle;
                spot.color = new Color(1f, 0.94f, 0.82f);
                spot.shadows = LightShadows.None;
            }
            m_Block = new MaterialPropertyBlock();
            SetOn(false, false);
            m_NextRandomFlicker = Time.time + Random.Range(20f, 40f);
        }

        public void SetOn(bool on, bool withClick = true)
        {
            IsOn = on;
            if (withClick && GameManager.Instance != null && GameManager.Instance.assets != null)
                HorrorAudio.PlayAt(GameManager.Instance.assets.flashlightClick, transform.position, 0.6f, 0.3f, 4f);
        }

        public void Toggle() => SetOn(!IsOn);

        /// <summary>Plus le monstre est proche, plus la lampe a des ratés (0..1).</summary>
        public void SetNervousness(float value) => m_Nervousness = Mathf.Clamp01(value);

        public void FlickerFor(float seconds) => m_FlickerUntil = Mathf.Max(m_FlickerUntil, Time.time + seconds);

        void Update()
        {
            float t = Time.time;
            if (t > m_NextRandomFlicker)
            {
                FlickerFor(Random.Range(0.3f, 0.9f));
                m_NextRandomFlicker = t + Random.Range(25f, 50f);
            }

            float flicker = 1f;
            bool flickering = t < m_FlickerUntil || m_Nervousness > 0.05f;
            if (flickering)
            {
                float n = Mathf.PerlinNoise(t * 18f, 0.37f);
                float strength = t < m_FlickerUntil ? 1f : m_Nervousness * 0.8f;
                flicker = n < 0.35f * strength ? 0.05f : Mathf.Lerp(1f, 0.55f + 0.45f * n, strength);
            }

            Output = IsOn ? flicker : 0f;
            if (spot != null)
            {
                spot.enabled = Output > 0.02f;
                spot.intensity = intensity * Output;
            }
            if (lens != null)
            {
                lens.GetPropertyBlock(m_Block);
                var glow = new Color(1f, 0.92f, 0.75f) * (0.2f + 2.5f * Output);
                m_Block.SetColor(k_BaseColor, glow);
                m_Block.SetColor(k_EmissionColor, glow);
                lens.SetPropertyBlock(m_Block);
            }
        }
    }
}

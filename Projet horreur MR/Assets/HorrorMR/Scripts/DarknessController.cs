using UnityEngine;

namespace HorrorMR
{
    /// <summary>
    /// Assombrit la vraie pièce (passthrough) et règle l'éclairage virtuel.
    /// Écrit les variables globales lues par les shaders HorrorMR.
    /// </summary>
    public class DarknessController : MonoBehaviour
    {
        public Flashlight flashlight;
        public Transform head;
        [Tooltip("Grande sphère qui assombrit là où il n'y a pas de maillage de la pièce.")]
        public Transform dome;
        public Light moonLight;

        [Tooltip("Obscurité max. Ne pas mettre 1 : on doit toujours deviner les obstacles réels.")]
        [Range(0f, 1f)] public float maxDarkness = 0.9f;
        public Color brightAmbient = new Color(0.55f, 0.55f, 0.6f);
        public Color darkAmbient = new Color(0.035f, 0.03f, 0.045f);
        public float brightMoon = 1f;
        public float darkMoon = 0.04f;

        public float Darkness { get; private set; }

        float m_Target;
        float m_Speed = 0.3f;
        float m_Flash;

        static readonly int k_Darkness = Shader.PropertyToID("_HorrorDarkness");
        static readonly int k_FlashPos = Shader.PropertyToID("_HorrorFlashPos");
        static readonly int k_FlashDir = Shader.PropertyToID("_HorrorFlashDir");
        static readonly int k_FlashParams = Shader.PropertyToID("_HorrorFlashParams");
        static readonly int k_Ambient = Shader.PropertyToID("_HorrorAmbient");

        /// <summary>Fait varier l'obscurité vers <paramref name="target"/> (0..1, multiplié par maxDarkness) en <paramref name="seconds"/>.</summary>
        public void FadeTo(float target, float seconds)
        {
            m_Target = Mathf.Clamp01(target) * maxDarkness;
            m_Speed = seconds <= 0f ? 1000f : Mathf.Abs(m_Target - Darkness) / seconds;
            if (m_Speed < 0.0001f)
                m_Speed = 1f;
        }

        public void SetImmediate(float target)
        {
            m_Target = Darkness = Mathf.Clamp01(target) * maxDarkness;
        }

        /// <summary>Éclair (coup de feu) : la pièce s'illumine une fraction de seconde.</summary>
        public void Flash(float amount) => m_Flash = Mathf.Max(m_Flash, amount);

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Darkness = Mathf.MoveTowards(Darkness, m_Target, m_Speed * dt);
            m_Flash = Mathf.MoveTowards(m_Flash, 0f, dt * 9f);
            float effective = Darkness * (1f - m_Flash * 0.75f);
            float k = maxDarkness > 0f ? Darkness / maxDarkness : 0f;

            Shader.SetGlobalFloat(k_Darkness, effective);
            var ambient = Color.Lerp(brightAmbient, darkAmbient, k);
            ambient += new Color(1f, 0.8f, 0.5f) * m_Flash * 0.8f;
            Shader.SetGlobalVector(k_Ambient, new Vector4(ambient.r, ambient.g, ambient.b, 1f));
            RenderSettings.ambientLight = ambient;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            if (moonLight != null)
                moonLight.intensity = Mathf.Lerp(brightMoon, darkMoon, k);

            if (flashlight != null)
            {
                var t = flashlight.spot != null ? flashlight.spot.transform : flashlight.transform;
                Shader.SetGlobalVector(k_FlashPos, new Vector4(t.position.x, t.position.y, t.position.z, flashlight.Output));
                Shader.SetGlobalVector(k_FlashDir, t.forward);
                Shader.SetGlobalVector(k_FlashParams, new Vector4(
                    Mathf.Cos(flashlight.outerAngle * 0.5f * Mathf.Deg2Rad),
                    Mathf.Cos(flashlight.innerAngle * 0.5f * Mathf.Deg2Rad),
                    flashlight.range, 0f));
            }
            else
            {
                Shader.SetGlobalVector(k_FlashPos, Vector4.zero);
            }

            if (dome != null && head != null)
                dome.position = head.position;
        }
    }
}

using UnityEngine;

namespace HorrorMR
{
    /// <summary>
    /// Calque plein écran : vignette noire quand le monstre approche, écran de plus en plus rouge
    /// à chaque coup reçu, flash rouge, fondu au noir. À placer sous la caméra.
    /// </summary>
    public class ScreenFX : MonoBehaviour
    {
        public Material overlayMaterial;

        Material m_Material;
        float m_Damage;
        float m_DamageTarget;
        float m_Flash;
        float m_Vignette;
        float m_VignetteTarget;
        float m_Fade;
        float m_FadeTarget;
        float m_FadeSpeed = 1f;

        static readonly int k_Damage = Shader.PropertyToID("_Damage");
        static readonly int k_Flash = Shader.PropertyToID("_Flash");
        static readonly int k_Vignette = Shader.PropertyToID("_Vignette");
        static readonly int k_Fade = Shader.PropertyToID("_Fade");

        public float Fade => m_Fade;

        void Awake()
        {
            var mesh = new Mesh { name = "Quad plein écran" };
            mesh.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            // Le shader ignore la transformation : de grandes bornes évitent que le quad soit retiré par le culling.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (overlayMaterial != null)
            {
                m_Material = new Material(overlayMaterial);
                renderer.sharedMaterial = m_Material;
            }
            transform.localPosition = new Vector3(0f, 0f, 0.5f);
            transform.localRotation = Quaternion.identity;
        }

        /// <summary>hits = coups déjà reçus.</summary>
        public void SetDamage(int hits, int maxHits)
        {
            m_DamageTarget = maxHits <= 0 ? 0f : hits <= 0 ? 0f : Mathf.Clamp01(0.3f + 0.7f * (hits - 1) / Mathf.Max(1, maxHits - 1));
        }

        public void HitFlash(float amount = 1f) => m_Flash = Mathf.Max(m_Flash, amount);

        public void SetVignette(float value) => m_VignetteTarget = Mathf.Clamp01(value);

        public void FadeTo(float value, float seconds)
        {
            m_FadeTarget = Mathf.Clamp01(value);
            m_FadeSpeed = seconds <= 0f ? 1000f : Mathf.Max(Mathf.Abs(m_FadeTarget - m_Fade) / seconds, 0.01f);
        }

        public void ResetAll()
        {
            m_Damage = m_DamageTarget = m_Flash = m_Vignette = m_VignetteTarget = m_Fade = m_FadeTarget = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Damage = Mathf.MoveTowards(m_Damage, m_DamageTarget, dt * 1.5f);
            m_Flash = Mathf.MoveTowards(m_Flash, 0f, dt * 2.2f);
            m_Vignette = Mathf.MoveTowards(m_Vignette, m_VignetteTarget, dt * 1.2f);
            m_Fade = Mathf.MoveTowards(m_Fade, m_FadeTarget, m_FadeSpeed * dt);
            if (m_Material == null)
                return;
            m_Material.SetFloat(k_Damage, m_Damage);
            m_Material.SetFloat(k_Flash, m_Flash);
            m_Material.SetFloat(k_Vignette, m_Vignette);
            m_Material.SetFloat(k_Fade, m_Fade);
        }
    }
}

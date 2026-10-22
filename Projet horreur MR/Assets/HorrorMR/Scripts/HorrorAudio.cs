using UnityEngine;

namespace HorrorMR
{
    /// <summary>Ambiance, battements de cœur, sons d'interface et sons 3D ponctuels.</summary>
    public class HorrorAudio : MonoBehaviour
    {
        AudioSource m_Ambience;
        AudioSource m_Heartbeat;
        AudioSource m_Ui;
        HorrorAssets m_Assets;
        float m_AmbienceTarget;
        float m_HeartbeatTarget;
        float m_HeartbeatPitch = 1f;

        public void Init(HorrorAssets assets)
        {
            m_Assets = assets;
            m_Ambience = Create2D("Ambiance", assets.ambience, true);
            m_Heartbeat = Create2D("Coeur", assets.heartbeat, true);
            m_Ui = Create2D("Interface", null, false);
        }

        AudioSource Create2D(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.playOnAwake = false;
            source.loop = loop;
            source.clip = clip;
            source.volume = 0f;
            if (loop && clip != null)
                source.Play();
            return source;
        }

        public void SetAmbience(float volume) => m_AmbienceTarget = volume;

        /// <summary>0 = calme, 1 = monstre collé à toi.</summary>
        public void SetHeartbeat(float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            m_HeartbeatTarget = intensity <= 0.001f ? 0f : 0.2f + 0.8f * intensity;
            m_HeartbeatPitch = 0.85f + 0.7f * intensity;
        }

        public void PlayUI(AudioClip clip, float volume = 1f)
        {
            if (clip != null && m_Ui != null)
                m_Ui.PlayOneShot(clip, volume);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (m_Ambience != null)
                m_Ambience.volume = Mathf.MoveTowards(m_Ambience.volume, m_AmbienceTarget, dt * 0.25f);
            if (m_Heartbeat != null)
            {
                m_Heartbeat.volume = Mathf.MoveTowards(m_Heartbeat.volume, m_HeartbeatTarget, dt * 0.8f);
                m_Heartbeat.pitch = Mathf.MoveTowards(m_Heartbeat.pitch, m_HeartbeatPitch, dt * 0.5f);
            }
        }

        /// <summary>Joue un son spatialisé à une position (source temporaire).</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float minDistance = 1f, float maxDistance = 25f)
        {
            if (clip == null)
                return;
            var go = new GameObject("Son 3D " + clip.name);
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            Configure3D(source, minDistance, maxDistance);
            source.clip = clip;
            source.volume = volume;
            source.pitch = Random.Range(0.95f, 1.05f);
            source.Play();
            Destroy(go, clip.length / Mathf.Max(source.pitch, 0.1f) + 0.2f);
        }

        public static void Configure3D(AudioSource source, float minDistance = 1f, float maxDistance = 25f)
        {
            source.spatialBlend = 1f;
            source.spatialize = true;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.playOnAwake = false;
        }
    }
}

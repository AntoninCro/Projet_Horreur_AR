using UnityEngine;

namespace HorrorMR
{
    /// <summary>Fait grandir un objet (flaque de sang qui s'étend).</summary>
    public class GrowOverTime : MonoBehaviour
    {
        float m_From;
        float m_To;
        float m_Duration;
        float m_Time;

        public void Configure(float from, float to, float duration)
        {
            m_From = from;
            m_To = to;
            m_Duration = duration;
            transform.localScale = Vector3.one * from;
        }

        void Update()
        {
            m_Time += Time.deltaTime;
            float k = Mathf.Clamp01(m_Time / m_Duration);
            k = 1f - (1f - k) * (1f - k);
            transform.localScale = Vector3.one * Mathf.Lerp(m_From, m_To, k);
            if (m_Time > m_Duration)
                enabled = false;
        }
    }
}

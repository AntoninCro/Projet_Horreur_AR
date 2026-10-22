using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace HorrorMR
{
    /// <summary>
    /// Interface flottante qui suit le regard avec un léger retard : titre, consigne, minuteur, aide,
    /// et une flèche au sol qui indique la direction du pistolet.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        public Transform head;
        public float distance = 1.5f;
        public float heightOffset = -0.12f;
        public float followAngle = 22f;
        public Material arrowMaterial;
        public TMP_FontAsset font;

        TextMeshPro m_Title;
        TextMeshPro m_Message;
        TextMeshPro m_Timer;
        TextMeshPro m_Hint;
        Transform m_Panel;
        Transform m_Arrow;
        Vector3? m_ArrowTarget;
        bool m_Following;
        float m_PulseUntil;
        Color m_MessageColor = Color.white;

        public static readonly Color White = new Color(0.92f, 0.92f, 0.92f);
        public static readonly Color Red = new Color(0.9f, 0.08f, 0.05f);
        public static readonly Color Orange = new Color(1f, 0.55f, 0.15f);
        public static readonly Color Green = new Color(0.45f, 0.95f, 0.5f);

        void Awake()
        {
            var panel = new GameObject("Panneau");
            m_Panel = panel.transform;
            m_Panel.SetParent(transform, false);
            m_Title = CreateText("Titre", new Vector2(1.3f, 0.14f), new Vector3(0f, 0.22f, 0f), FontStyles.Bold);
            m_Message = CreateText("Consigne", new Vector2(1.2f, 0.2f), new Vector3(0f, 0.04f, 0f), FontStyles.Normal);
            m_Timer = CreateText("Minuteur", new Vector2(0.5f, 0.11f), new Vector3(0f, 0.2f, 0f), FontStyles.Bold);
            m_Hint = CreateText("Aide", new Vector2(1.15f, 0.16f), new Vector3(0f, -0.18f, 0f), FontStyles.Italic);
            m_Message.fontSizeMax = 1.2f;
            m_Hint.fontSizeMax = 0.45f;
            m_Arrow = CreateArrow();
            SetArrowTarget(null);
            Show("", "", "");
            HideTimer();
        }

        TextMeshPro CreateText(string name, Vector2 size, Vector3 localPosition, FontStyles style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(m_Panel, false);
            go.transform.localPosition = localPosition;
            var text = go.AddComponent<TextMeshPro>();
            if (font == null)
                font = TMP_Settings.defaultFontAsset;
            if (font != null)
                text.font = font;
            text.rectTransform.sizeDelta = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.05f;
            text.fontSizeMax = 3f;
            text.fontStyle = style;
            text.color = White;
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color32(0, 0, 0, 255);
            // Toujours visible : par-dessus le monstre, les murs réels et le calque d'écran.
            var mat = text.fontMaterial;
            if (mat != null)
            {
                mat.renderQueue = 4200;
                if (mat.HasProperty("unity_GUIZTestMode"))
                    mat.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            }
            return text;
        }

        Transform CreateArrow()
        {
            var go = new GameObject("Flèche pistolet");
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = "Flèche" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0.16f), new Vector3(-0.09f, 0f, -0.02f), new Vector3(0.09f, 0f, -0.02f),
                new Vector3(-0.035f, 0f, -0.02f), new Vector3(0.035f, 0f, -0.02f), new Vector3(-0.035f, 0f, -0.12f), new Vector3(0.035f, 0f, -0.12f)
            };
            mesh.uv = new[] { new Vector2(0.5f, 1f), new Vector2(0f, 0.6f), new Vector2(1f, 0.6f), new Vector2(0.3f, 0.6f), new Vector2(0.7f, 0.6f), new Vector2(0.3f, 0f), new Vector2(0.7f, 0f) };
            mesh.triangles = new[] { 0, 2, 1, 3, 4, 5, 4, 6, 5 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = arrowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        public void Show(string title, string message, string hint, Color? messageColor = null, bool pulse = false)
        {
            m_Title.text = title;
            m_Message.text = message;
            m_Hint.text = hint;
            m_MessageColor = messageColor ?? White;
            m_Message.color = m_MessageColor;
            m_Title.color = messageColor.HasValue && messageColor.Value == Green ? Green : Red;
            m_PulseUntil = pulse ? Time.time + 3f : 0f;
            Recenter();
        }

        public void SetMessage(string message, Color color, bool pulse = false)
        {
            m_Message.text = message;
            m_MessageColor = color;
            m_Message.color = color;
            m_PulseUntil = pulse ? Time.time + 3f : 0f;
        }

        public void SetHint(string hint) => m_Hint.text = hint;

        public void SetTimer(float seconds, bool urgent)
        {
            m_Timer.gameObject.SetActive(true);
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            m_Timer.text = $"{s / 60:00}:{s % 60:00}";
            m_Timer.color = urgent ? Red : White;
        }

        public void HideTimer() => m_Timer.gameObject.SetActive(false);

        public void SetArrowTarget(Vector3? target)
        {
            m_ArrowTarget = target;
            if (m_Arrow != null)
                m_Arrow.gameObject.SetActive(target.HasValue);
        }

        public void Recenter()
        {
            if (head == null)
                return;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();
            m_Panel.position = head.position + forward * distance + Vector3.up * heightOffset;
            m_Panel.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        void LateUpdate()
        {
            if (head == null)
                return;

            // Suivi paresseux du regard : le panneau ne bouge que si on tourne franchement la tête.
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.001f)
            {
                forward.Normalize();
                var toPanel = Vector3.ProjectOnPlane(m_Panel.position - head.position, Vector3.up).normalized;
                float angle = Vector3.Angle(forward, toPanel);
                if (angle > followAngle)
                    m_Following = true;
                if (m_Following)
                {
                    var targetPos = head.position + forward * distance + Vector3.up * heightOffset;
                    var targetRot = Quaternion.LookRotation(forward, Vector3.up);
                    float k = 1f - Mathf.Exp(-Time.deltaTime * 4f);
                    m_Panel.position = Vector3.Lerp(m_Panel.position, targetPos, k);
                    m_Panel.rotation = Quaternion.Slerp(m_Panel.rotation, targetRot, k);
                    if (angle < 3f)
                        m_Following = false;
                }
                else
                {
                    var p = m_Panel.position;
                    p.y = Mathf.Lerp(p.y, head.position.y + heightOffset, 1f - Mathf.Exp(-Time.deltaTime * 3f));
                    m_Panel.position = p;
                }
            }

            if (Time.time < m_PulseUntil)
            {
                float a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 7f));
                m_Message.color = new Color(m_MessageColor.r, m_MessageColor.g, m_MessageColor.b, a);
            }
            else
            {
                m_Message.color = m_MessageColor;
            }

            if (m_ArrowTarget.HasValue && m_Arrow != null)
            {
                var flat = forward.sqrMagnitude > 0.001f ? forward : Vector3.forward;
                m_Arrow.position = head.position + flat * 0.55f + Vector3.down * 0.45f;
                var toTarget = Vector3.ProjectOnPlane(m_ArrowTarget.Value - m_Arrow.position, Vector3.up);
                if (toTarget.sqrMagnitude > 0.0001f)
                    m_Arrow.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                m_Arrow.localScale = Vector3.one * (1f + 0.08f * Mathf.Sin(Time.time * 6f));
            }
        }
    }
}

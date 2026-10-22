using System;
using UnityEngine;

namespace HorrorMR
{
    /// <summary>
    /// Animation procédurale de la peluche (le modèle Alien3 a un squelette mais aucune animation).
    /// Chaque image, le squelette repart de la pose de repos (T-pose) et on applique des rotations
    /// exprimées dans les axes du personnage : marche dandinante, marche accroupie, course bras tendus,
    /// attaque, recul quand il est touché, cri, jumpscare et effondrement de peluche vide à la mort.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class PlushAnimator : MonoBehaviour
    {
        [Tooltip("Racine du modèle (mise à l'échelle). Son axe +Z est l'avant du personnage.")]
        public Transform modelRoot;

        [Header("Démarche")]
        public float walkSwing = 24f;
        public float crouchSwing = 19f;
        public float runSwing = 36f;
        public float kneeBend = 40f;
        [Tooltip("Inclinaison permanente de la tête (effet poupée inquiétante).")]
        public float headTilt = 11f;
        [Tooltip("Centre de l'œil peint, par rapport à l'os de la tête (unités du modèle d'origine).")]
        public Vector3 faceOffset = new Vector3(0f, 0.12f, 0.12f);

        // Paramètres pilotés par MonsterAI.
        [NonSerialized] public float speed;
        [NonSerialized] public float crouch;
        [NonSerialized] public float reach;
        [NonSerialized] public float jaw;
        [NonSerialized] public bool hasLookTarget;
        [NonSerialized] public Vector3 lookTarget;
        [NonSerialized] public bool lookAround;
        [NonSerialized] public bool jumpscare;
        [NonSerialized] public bool allowTwitch = true;

        /// <summary>Pied qui touche le sol (0 = gauche, 1 = droit).</summary>
        public event Action<int> Footstep;

        const int k_Pelvis = 0, k_Spine1 = 1, k_Spine2 = 2, k_Spine3 = 3, k_Neck = 4, k_Head = 5, k_Jaw = 6;
        const int k_UpperArmL = 7, k_LowerArmL = 8, k_HandL = 9, k_UpperArmR = 10, k_LowerArmR = 11, k_HandR = 12;
        const int k_ThighL = 13, k_CalfL = 14, k_FootL = 15, k_ThighR = 16, k_CalfR = 17, k_FootR = 18;
        const int k_BoneCount = 19;

        static readonly string[] k_BoneNames =
        {
            "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "head", "CC_Base_JawRoot",
            "upperarm_l", "lowerarm_l", "hand_l", "upperarm_r", "lowerarm_r", "hand_r",
            "thigh_l", "calf_l", "foot_l", "thigh_r", "calf_r", "foot_r"
        };

        readonly Transform[] m_Bones = new Transform[k_BoneCount];
        readonly Quaternion[] m_Rest = new Quaternion[k_BoneCount];
        readonly float[] m_Pitch = new float[k_BoneCount];
        readonly float[] m_Roll = new float[k_BoneCount];
        readonly float[] m_Yaw = new float[k_BoneCount];

        Vector3 m_FacePointLocal;
        Vector3 m_RestPelvisModel;
        Vector3 m_FaceLocal;
        Vector3 m_BaseModelScale;
        float m_LegLength = 0.55f;
        bool m_Ready;

        float m_Phase;
        int m_LastStepIndex;
        float m_Gait;
        float m_Crouch;
        float m_Reach;
        float m_Jaw;
        float m_LookWeight;
        float m_LookAround;
        float m_Jumpscare;
        float m_AttackTime = -1f;
        float m_FlinchTime = -1f;
        float m_SpotTime = -1f;
        float m_DeathTime = -1f;
        float m_TwitchStart = -10f;
        float m_TwitchRoll;
        float m_NextTwitch;
        float m_SquashTime = -10f;

        public bool IsReady => m_Ready;
        public bool IsAttacking => m_AttackTime >= 0f && Time.time - m_AttackTime < 1f;
        public Transform HeadBone => m_Bones[k_Head];
        public Transform ChestBone => m_Bones[k_Spine3] != null ? m_Bones[k_Spine3] : transform;

        /// <summary>L'œil peint sur la grosse tête (les os "yeux" du squelette sont plus bas que lui).</summary>
        public Vector3 EyePosition
        {
            get
            {
                var head = m_Bones[k_Head];
                return head != null ? head.TransformPoint(m_FacePointLocal) : transform.position + Vector3.up * 1.6f;
            }
        }

        public Vector3 HeadForward
        {
            get
            {
                var head = m_Bones[k_Head];
                return head != null ? head.rotation * m_FaceLocal : transform.forward;
            }
        }

        void Awake()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (m_Ready)
                return;
            if (modelRoot == null && transform.childCount > 0)
                modelRoot = transform.GetChild(0);
            if (modelRoot == null)
            {
                Debug.LogError("[Horreur] PlushAnimator : aucun modèle trouvé.", this);
                return;
            }

            // Le modèle importé possède un Animator sans contrôleur : on le coupe pour garder la main sur les os.
            foreach (var animator in modelRoot.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;

            for (int i = 0; i < k_BoneCount; i++)
            {
                m_Bones[i] = FindDeep(modelRoot, k_BoneNames[i]);
                if (m_Bones[i] != null)
                    m_Rest[i] = m_Bones[i].localRotation;
            }

            if (m_Bones[k_Pelvis] == null || m_Bones[k_Head] == null)
            {
                Debug.LogError("[Horreur] PlushAnimator : squelette Alien3 introuvable (pelvis/head).", this);
                return;
            }

            m_RestPelvisModel = modelRoot.InverseTransformPoint(m_Bones[k_Pelvis].position);
            m_FaceLocal = Quaternion.Inverse(m_Bones[k_Head].rotation) * transform.forward;
            var headModel = modelRoot.InverseTransformPoint(m_Bones[k_Head].position);
            m_FacePointLocal = m_Bones[k_Head].InverseTransformPoint(modelRoot.TransformPoint(headModel + faceOffset));
            m_BaseModelScale = modelRoot.localScale;
            if (m_Bones[k_ThighL] != null && m_Bones[k_FootL] != null)
                m_LegLength = Vector3.Distance(m_Bones[k_ThighL].position, m_Bones[k_FootL].position) * 1.2f;
            m_NextTwitch = Time.time + UnityEngine.Random.Range(2f, 5f);
            m_Ready = true;
        }

        static Transform FindDeep(Transform root, string boneName)
        {
            if (root.name == boneName)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), boneName);
                if (found != null)
                    return found;
            }
            return null;
        }

        public void TriggerAttack(float time = -1f) => m_AttackTime = time >= 0f ? time : Time.time;
        public void TriggerFlinch(float time = -1f) => m_FlinchTime = time >= 0f ? time : Time.time;
        public void TriggerSpot(float time = -1f) => m_SpotTime = time >= 0f ? time : Time.time;
        public void TriggerDeath(float time = -1f) => m_DeathTime = time >= 0f ? time : Time.time;

        public void ResetPose()
        {
            m_AttackTime = m_FlinchTime = m_SpotTime = m_DeathTime = -1f;
            m_Jumpscare = 0f;
            jumpscare = false;
        }

        static float Smooth(float current, float target, float rate, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        static float EaseOutBack(float x)
        {
            const float c1 = 1.4f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        void LateUpdate() => Evaluate(Time.time, Time.deltaTime);

        /// <summary>Calcule la pose pour l'instant <paramref name="t"/> (public pour l'aperçu dans l'éditeur).</summary>
        public void Evaluate(float t, float dt)
        {
            if (!m_Ready)
                return;

            bool dead = m_DeathTime >= 0f;

            for (int i = 0; i < k_BoneCount; i++)
            {
                if (m_Bones[i] != null)
                    m_Bones[i].localRotation = m_Rest[i];
                m_Pitch[i] = m_Roll[i] = m_Yaw[i] = 0f;
            }

            // --- Lissage des paramètres -------------------------------------------------------------
            m_Gait = Mathf.MoveTowards(m_Gait, dead ? 0f : Mathf.Clamp01(speed / 0.3f), dt * 3f);
            m_Crouch = Smooth(m_Crouch, dead ? 0f : Mathf.Clamp01(crouch), 4f, dt);
            m_Reach = Smooth(m_Reach, dead ? 0f : Mathf.Clamp01(reach), 5f, dt);
            m_Jaw = Smooth(m_Jaw, Mathf.Clamp01(jaw), 10f, dt);
            m_LookWeight = Smooth(m_LookWeight, hasLookTarget && !dead ? 1f : 0f, hasLookTarget ? 9f : 3f, dt);
            m_LookAround = Smooth(m_LookAround, lookAround && !dead ? 1f : 0f, 2f, dt);
            m_Jumpscare = Mathf.MoveTowards(m_Jumpscare, jumpscare ? 1f : 0f, dt * 8f);

            float g = m_Gait;
            float c = m_Crouch;
            float j = m_Jumpscare;

            // --- Phase de marche --------------------------------------------------------------------
            float run = Mathf.Clamp01((speed - 0.9f) / 0.9f);
            float swing = Mathf.Lerp(Mathf.Lerp(walkSwing, crouchSwing, c), runSwing, run);
            float stepLength = 2f * m_LegLength * Mathf.Sin(swing * Mathf.Deg2Rad);
            float cycleLength = Mathf.Max(2f * stepLength, 0.2f);
            if (!dead)
                m_Phase += 2f * Mathf.PI * (speed / cycleLength) * dt;

            int stepIndex = Mathf.FloorToInt((m_Phase - Mathf.PI * 0.5f) / Mathf.PI);
            if (stepIndex != m_LastStepIndex)
            {
                if (g > 0.3f)
                {
                    Footstep?.Invoke(((stepIndex % 2) + 2) % 2);
                    m_SquashTime = t;
                }
                m_LastStepIndex = stepIndex;
            }

            float sL = Mathf.Sin(m_Phase);
            float cL = Mathf.Cos(m_Phase);
            float sR = -sL;
            float cR = -cL;

            // --- Bassin ------------------------------------------------------------------------------
            var pelvisModel = m_RestPelvisModel;
            pelvisModel.y -= 0.085f * c;
            pelvisModel.z -= 0.02f * c;
            pelvisModel.y += (Mathf.Abs(cL) - 0.5f) * 0.012f * g * (1f + run);
            pelvisModel.x += sL * 0.006f * g;
            pelvisModel.z += Mathf.Sin(t * 31f) * 0.004f * j;
            m_Roll[k_Pelvis] = sL * 7f * g;
            m_Yaw[k_Pelvis] = sL * 5f * g;

            // --- Colonne -----------------------------------------------------------------------------
            float breath = Mathf.Sin(t * 1.7f);
            float idle = 1f - g;
            float lean = 22f * c + 9f * run * g + 5f * m_Reach + 16f * j;
            m_Pitch[k_Spine1] = lean * 0.35f + breath * 0.8f;
            m_Pitch[k_Spine2] = lean * 0.35f + breath * 1.2f;
            m_Pitch[k_Spine3] = lean * 0.3f + breath * 0.6f;
            m_Roll[k_Spine2] = -sL * 4f * g + Mathf.Sin(t * 0.6f) * 3f * idle;
            m_Yaw[k_Spine3] = -sL * 6f * g;
            m_Pitch[k_Neck] = -10f * c;
            m_Pitch[k_Head] = -12f * c - 6f * run * g;

            // --- Bras (la T-pose est à l'horizontale : on les fait pendre) ------------------------------
            float armLower = 74f;
            float armSwing = 16f * g * (1f - m_Reach);
            float armForward = -8f - 72f * m_Reach;
            float grab = m_Reach * Mathf.Sin(t * 14f) * 5f;
            float floppy = Mathf.Sin(m_Phase - 0.8f) * 7f * g;
            m_Roll[k_UpperArmL] = Mathf.Lerp(armLower, -15f, j);
            m_Roll[k_UpperArmR] = -Mathf.Lerp(armLower, -15f, j);
            m_Pitch[k_UpperArmL] = Mathf.Lerp(sL * armSwing + armForward + grab, -55f, j);
            m_Pitch[k_UpperArmR] = Mathf.Lerp(sR * armSwing + armForward - grab, -55f, j);
            m_Pitch[k_LowerArmL] = Mathf.Lerp(-18f - 12f * m_Reach + floppy, -35f, j);
            m_Pitch[k_LowerArmR] = Mathf.Lerp(-18f - 12f * m_Reach - floppy, -35f, j);

            // --- Jambes ------------------------------------------------------------------------------
            float thighBase = -58f * c;
            float kneeBase = 92f * c;
            float thL = thighBase - sL * swing * g;
            float thR = thighBase - sR * swing * g;
            float knL = kneeBase + Mathf.Max(0f, cL) * kneeBend * g + 5f * g;
            float knR = kneeBase + Mathf.Max(0f, cR) * kneeBend * g + 5f * g;
            m_Pitch[k_ThighL] = thL;
            m_Pitch[k_ThighR] = thR;
            m_Pitch[k_CalfL] = knL;
            m_Pitch[k_CalfR] = knR;
            m_Pitch[k_FootL] = -(thL + knL) * 0.85f;
            m_Pitch[k_FootR] = -(thR + knR) * 0.85f;
            m_Roll[k_ThighL] = -4f;
            m_Roll[k_ThighR] = 4f;

            // --- Tête : inclinaison de poupée, tics nerveux, regard qui balaie ------------------------
            m_Roll[k_Head] = (headTilt + Mathf.Sin(t * 0.4f) * 4f) * (1f - j);
            if (allowTwitch && !dead && j < 0.01f && t > m_NextTwitch)
            {
                m_TwitchStart = t;
                m_TwitchRoll = UnityEngine.Random.Range(25f, 42f) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
                m_NextTwitch = t + UnityEngine.Random.Range(2.5f, 6.5f);
            }
            float tw = t - m_TwitchStart;
            float twitch = tw < 0.07f ? tw / 0.07f : tw < 0.45f ? 1f : Mathf.Clamp01(1f - (tw - 0.45f) / 0.35f);
            m_Roll[k_Head] += m_TwitchRoll * twitch;
            m_Yaw[k_Head] += Mathf.Sin(t * 0.9f) * 60f * m_LookAround;
            m_Pitch[k_Head] += Mathf.Sin(t * 0.5f) * 8f * m_LookAround;

            // Tremblement du jumpscare.
            if (j > 0f)
            {
                m_Yaw[k_Head] += (Mathf.PerlinNoise(t * 25f, 0.1f) - 0.5f) * 26f * j;
                m_Pitch[k_Head] += (Mathf.PerlinNoise(0.7f, t * 25f) - 0.5f) * 20f * j;
                m_Roll[k_Spine3] += (Mathf.PerlinNoise(t * 18f, 3.3f) - 0.5f) * 10f * j;
            }

            float jawOpen = m_Jaw + j;

            // --- Attaque : la peluche se ramasse en arrière puis se jette en avant, gueule ouverte ---
            // (ses bras sont minuscules par rapport à la tête : un coup par-dessus la tête serait invisible)
            if (m_AttackTime >= 0f)
            {
                float ta = t - m_AttackTime;
                if (ta < 1f)
                {
                    float w = Mathf.SmoothStep(0f, 1f, ta / 0.45f);
                    float s = Mathf.SmoothStep(0f, 1f, (ta - 0.45f) / 0.12f);
                    float r = Mathf.SmoothStep(0f, 1f, (ta - 0.6f) / 0.4f);
                    float k = ta < 0.45f ? -w : ta < 0.6f ? Mathf.Lerp(-1f, 1f, s) : Mathf.Lerp(1f, 0f, r);
                    // k < 0 : armé (recul), k > 0 : frappe (projection vers l'avant).
                    float back = Mathf.Max(0f, -k);
                    float strike = Mathf.Max(0f, k);
                    m_Pitch[k_Spine1] += -8f * back + 14f * strike;
                    m_Pitch[k_Spine2] += -8f * back + 14f * strike;
                    m_Pitch[k_Head] += -18f * back + 12f * strike;
                    m_Pitch[k_UpperArmL] += 40f * back - 95f * strike;
                    m_Pitch[k_UpperArmR] += 40f * back - 95f * strike;
                    m_Roll[k_UpperArmL] -= 25f * back;
                    m_Roll[k_UpperArmR] += 25f * back;
                    m_Pitch[k_LowerArmL] += -25f * back;
                    m_Pitch[k_LowerArmR] += -25f * back;
                    m_Yaw[k_Spine3] += Mathf.Sin(ta * 9f) * 6f * back;
                    pelvisModel.z += -0.015f * back + 0.045f * strike;
                    pelvisModel.y -= 0.02f * back;
                    jawOpen += back * 0.6f + strike;
                }
            }

            // --- Touché par une balle --------------------------------------------------------------
            if (m_FlinchTime >= 0f)
            {
                float tf = t - m_FlinchTime;
                if (tf < 0.6f)
                {
                    float k = Mathf.Exp(-tf * 8f);
                    m_Pitch[k_Spine2] -= 20f * k;
                    m_Pitch[k_Head] -= 15f * k;
                    m_Roll[k_UpperArmL] -= 20f * k;
                    m_Roll[k_UpperArmR] += 20f * k;
                    jawOpen += 0.6f * k;
                }
            }

            // --- Il t'a repéré : se fige, ouvre la gueule, frissonne --------------------------------
            if (m_SpotTime >= 0f)
            {
                float ts = t - m_SpotTime;
                if (ts < 1.3f)
                {
                    float k = Mathf.SmoothStep(0f, 1f, ts / 0.12f) * (1f - Mathf.SmoothStep(0f, 1f, (ts - 0.9f) / 0.4f));
                    jawOpen += k;
                    m_Pitch[k_Spine3] += 8f * k;
                    m_Roll[k_UpperArmL] -= 22f * k;
                    m_Roll[k_UpperArmR] += 22f * k;
                    m_Roll[k_Spine2] += Mathf.Sin(t * 40f) * 2f * k;
                }
            }
            m_Pitch[k_Jaw] = Mathf.Clamp01(jawOpen) * 28f;

            // --- Mort : la peluche se vide et s'assoit, affalée -------------------------------------
            if (dead)
            {
                float td = t - m_DeathTime;
                float d = td < 0.15f ? 0f : EaseOutBack(Mathf.Clamp01((td - 0.15f) / 0.75f));
                float backKick = td < 0.3f ? Mathf.Sin(td / 0.3f * Mathf.PI) : 0f;
                pelvisModel.y = Mathf.LerpUnclamped(pelvisModel.y, 0.09f, d);
                pelvisModel.z = Mathf.LerpUnclamped(pelvisModel.z, m_RestPelvisModel.z - 0.03f, d);
                LerpBone(k_Pelvis, 0f, 0f, 0f, d);
                LerpBone(k_Spine1, 12f - 15f * backKick, 0f, 0f, d);
                LerpBone(k_Spine2, 14f, 4f, 0f, d);
                LerpBone(k_Spine3, 12f, 0f, 0f, d);
                LerpBone(k_Neck, 15f, 0f, 0f, d);
                float lateTwitch = (td > 2.0f && td < 2.15f) || (td > 3.4f && td < 3.5f) ? 12f : 0f;
                LerpBone(k_Head, 38f, 22f + lateTwitch, 0f, d);
                LerpBone(k_UpperArmL, -5f, 88f, 0f, d);
                LerpBone(k_UpperArmR, -5f, -88f, 0f, d);
                LerpBone(k_LowerArmL, -10f, 0f, 0f, d);
                LerpBone(k_LowerArmR, -10f, 0f, 0f, d);
                LerpBone(k_ThighL, -85f, 0f, -18f, d);
                LerpBone(k_ThighR, -85f, 0f, 18f, d);
                LerpBone(k_CalfL, 8f, 0f, 0f, d);
                LerpBone(k_CalfR, 8f, 0f, 0f, d);
                LerpBone(k_FootL, 10f, 0f, 0f, d);
                LerpBone(k_FootR, 10f, 0f, 0f, d);
                m_Pitch[k_Jaw] = Mathf.Lerp(m_Pitch[k_Jaw], 12f, d);
            }

            // --- Application ------------------------------------------------------------------------
            var pelvis = m_Bones[k_Pelvis];
            pelvis.position = modelRoot.TransformPoint(pelvisModel);

            var right = transform.right;
            var up = transform.up;
            var forward = transform.forward;
            for (int i = 0; i < k_BoneCount; i++)
            {
                var bone = m_Bones[i];
                if (bone == null)
                    continue;
                var delta = Quaternion.AngleAxis(m_Yaw[i], up) * Quaternion.AngleAxis(m_Pitch[i], right) * Quaternion.AngleAxis(m_Roll[i], forward);
                bone.rotation = delta * bone.rotation;
            }

            // Regard vers la cible, appliqué en dernier sur l'orientation réelle de la tête.
            var head = m_Bones[k_Head];
            if (m_LookWeight > 0.01f && hasLookTarget)
            {
                var desired = (lookTarget - EyePosition).normalized;
                var current = head.rotation * m_FaceLocal;
                var fromTo = Quaternion.FromToRotation(current, desired);
                fromTo.ToAngleAxis(out var angle, out var axis);
                if (angle > 180f)
                    angle -= 360f;
                angle = Mathf.Clamp(angle, -115f, 115f);
                if (!float.IsNaN(axis.x))
                    head.rotation = Quaternion.AngleAxis(angle * m_LookWeight, axis) * head.rotation;
            }

            // Petit écrasement de peluche à chaque pas.
            float squash = Mathf.Exp(-(t - m_SquashTime) * 12f) * g;
            modelRoot.localScale = new Vector3(
                m_BaseModelScale.x * (1f + 0.02f * squash),
                m_BaseModelScale.y * (1f - 0.035f * squash),
                m_BaseModelScale.z * (1f + 0.02f * squash));
        }

        void LerpBone(int index, float pitch, float roll, float yaw, float d)
        {
            m_Pitch[index] = Mathf.LerpUnclamped(m_Pitch[index], pitch, d);
            m_Roll[index] = Mathf.LerpUnclamped(m_Roll[index], roll, d);
            m_Yaw[index] = Mathf.LerpUnclamped(m_Yaw[index], yaw, d);
        }
    }
}

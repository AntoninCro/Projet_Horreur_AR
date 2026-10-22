using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace HorrorMR
{
    public enum MonsterState { Inactive, Patrol, Search, Alert, Chase, Attack, Recover, Dead, Jumpscare }

    /// <summary>
    /// Cerveau de la peluche géante : elle erre (debout ou accroupie), te repère si elle te voit
    /// (les meubles réels bloquent sa vue), te poursuit, te frappe (3 coups = mort) et part te
    /// chercher là où elle t'a vu pour la dernière fois.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class MonsterAI : MonoBehaviour
    {
        [Header("Références")]
        public PlushAnimator plush;
        public AudioSource voice;
        public AudioSource loop;
        public AudioSource steps;

        [Header("Vitesses (m/s)")]
        public float walkSpeed = 0.75f;
        public float crouchSpeed = 0.5f;
        public float chaseSpeed = 1.85f;

        [Header("Perception")]
        public float sightRange = 14f;
        public float sightRangeCrouched = 6.5f;
        public float fieldOfView = 120f;
        public float proximityRadius = 1.8f;
        public float proximityRadiusCrouched = 1.0f;
        public float loseSightDelay = 3f;
        public float searchDuration = 6f;

        [Header("Attaque")]
        public float attackRange = 1.5f;
        public float hitRange = 2.0f;
        public float strikeTime = 0.5f;
        public float recoverTime = 1.6f;

        [Header("Vie")]
        public int maxHealth = 5;

        public MonsterState State { get; private set; } = MonsterState.Inactive;
        public bool IsDead => State == MonsterState.Dead;
        public bool IsHunting => State == MonsterState.Chase || State == MonsterState.Attack || State == MonsterState.Recover || State == MonsterState.Alert;
        public int Health { get; private set; }
        public Vector3 ChestPosition => plush != null ? plush.ChestBone.position : transform.position + Vector3.up * 1.1f;

        public event Action<MonsterAI> Spotted;
        public event Action<MonsterAI> Killed;
        public event Action<MonsterAI> Damaged;

        NavMeshAgent m_Agent;
        GameManager m_Game;
        HorrorAssets m_Assets;
        float m_StateTime;
        float m_NextPerception;
        float m_LastSeenTime = -100f;
        Vector3 m_LastKnownPosition;
        bool m_CanSeePlayer;
        bool m_Waiting;
        float m_WaitUntil;
        float m_NextRepath;
        float m_NextVoice;
        float m_StaggerUntil;
        bool m_HitDone;
        bool m_Enraged;
        Collider[] m_Colliders;
        static readonly RaycastHit[] s_Hits = new RaycastHit[16];

        public void Init(GameManager game, HorrorAssets assets)
        {
            m_Game = game;
            m_Assets = assets;
            m_Agent = GetComponent<NavMeshAgent>();
            m_Agent.radius = 0.25f;
            m_Agent.height = 1.9f;
            m_Agent.angularSpeed = 220f;
            m_Agent.acceleration = 8f;
            m_Agent.autoBraking = true;
            m_Agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
            m_Colliders = GetComponentsInChildren<Collider>(true);
            if (plush == null)
                plush = GetComponent<PlushAnimator>();
            plush.Initialize();
            plush.Footstep += OnFootstep;
            Health = maxHealth;

            if (loop != null && assets.musicBox != null)
            {
                loop.clip = assets.musicBox;
                loop.loop = true;
                loop.volume = 0f;
                loop.Play();
            }
            EnterState(MonsterState.Patrol);
        }

        void OnDestroy()
        {
            if (plush != null)
                plush.Footstep -= OnFootstep;
        }

        PlayerState Player => m_Game.Player;
        RoomModel Room => m_Game.Room;

        /// <summary>Après la fin du minuteur, la peluche devient plus agressive.</summary>
        public void Enrage()
        {
            m_Enraged = true;
            chaseSpeed *= 1.1f;
            m_LastKnownPosition = Player.HeadPosition;
            if (State == MonsterState.Patrol || State == MonsterState.Search)
                EnterState(MonsterState.Search);
        }

        void EnterState(MonsterState next)
        {
            State = next;
            m_StateTime = Time.time;
            m_Waiting = false;
            m_HitDone = false;
            plush.lookAround = false;
            plush.reach = 0f;
            plush.jaw = 0f;

            switch (next)
            {
                case MonsterState.Patrol:
                    bool crouched = UnityEngine.Random.value < 0.4f;
                    plush.crouch = crouched ? 1f : 0f;
                    SetSpeed(crouched ? crouchSpeed : walkSpeed);
                    PickWanderTarget();
                    break;
                case MonsterState.Search:
                    plush.crouch = 1f;
                    SetSpeed(crouchSpeed * 1.2f);
                    SetDestination(m_LastKnownPosition);
                    break;
                case MonsterState.Alert:
                    StopAgent();
                    plush.TriggerSpot();
                    plush.crouch = 0f;
                    PlayVoice(m_Assets.spottedScream, 1f);
                    Spotted?.Invoke(this);
                    break;
                case MonsterState.Chase:
                    plush.crouch = 0f;
                    plush.reach = 1f;
                    SetSpeed(chaseSpeed);
                    m_Agent.acceleration = 14f;
                    break;
                case MonsterState.Attack:
                    StopAgent();
                    plush.TriggerAttack();
                    plush.reach = 0.3f;
                    break;
                case MonsterState.Recover:
                    StopAgent();
                    break;
            }
        }

        void SetSpeed(float speed)
        {
            if (!m_Agent.enabled)
                return;
            m_Agent.speed = speed;
            m_Agent.acceleration = 8f;
            if (m_Agent.isOnNavMesh)
                m_Agent.isStopped = false;
        }

        void StopAgent()
        {
            if (m_Agent.enabled && m_Agent.isOnNavMesh)
            {
                m_Agent.isStopped = true;
                m_Agent.velocity = Vector3.zero;
            }
        }

        void SetDestination(Vector3 target)
        {
            if (!m_Agent.enabled || !m_Agent.isOnNavMesh)
                return;
            if (Room.SnapToNavMesh(target, 2.5f, out var snapped))
                target = snapped;
            m_Agent.isStopped = false;
            m_Agent.SetDestination(target);
        }

        void PickWanderTarget()
        {
            float bias = m_Enraged ? 0.6f : 0.35f;
            if (Room.TryWanderPoint(transform.position, Player.HeadPosition, bias, out var p))
                SetDestination(p);
            else
                BeginWait(2f);
        }

        void BeginWait(float duration)
        {
            m_Waiting = true;
            m_WaitUntil = Time.time + duration;
            StopAgent();
            plush.lookAround = true;
        }

        bool ReachedDestination =>
            m_Agent.enabled && m_Agent.isOnNavMesh && !m_Agent.pathPending &&
            (m_Agent.remainingDistance <= Mathf.Max(m_Agent.stoppingDistance, 0.4f) || m_Agent.pathStatus == NavMeshPathStatus.PathInvalid);

        void Update()
        {
            if (m_Game == null || State == MonsterState.Inactive || State == MonsterState.Dead || State == MonsterState.Jumpscare)
                return;

            float now = Time.time;
            if (now >= m_NextPerception)
            {
                m_NextPerception = now + 0.12f;
                m_CanSeePlayer = !Player.IsDead && CanSeePlayer();
                if (m_CanSeePlayer)
                {
                    m_LastSeenTime = now;
                    m_LastKnownPosition = Player.HeadPosition;
                }
            }

            float inState = now - m_StateTime;
            var head = Player.HeadPosition;
            float flatDistance = FlatDistance(transform.position, head);

            switch (State)
            {
                case MonsterState.Patrol:
                    if (m_CanSeePlayer) { EnterState(MonsterState.Alert); break; }
                    if (m_Waiting)
                    {
                        if (now > m_WaitUntil) { m_Waiting = false; plush.lookAround = false; EnterState(MonsterState.Patrol); }
                    }
                    else if (ReachedDestination)
                    {
                        BeginWait(UnityEngine.Random.Range(1.5f, 4f));
                    }
                    else if (inState > 25f)
                    {
                        EnterState(MonsterState.Patrol);
                    }
                    MaybeAmbientVoice(now, m_Assets.whisper, 9f, 16f, 0.6f);
                    break;

                case MonsterState.Search:
                    if (m_CanSeePlayer) { EnterState(MonsterState.Alert); break; }
                    if (!m_Waiting && ReachedDestination)
                        BeginWait(searchDuration);
                    else if (m_Waiting && now > m_WaitUntil)
                        EnterState(MonsterState.Patrol);
                    else if (inState > 20f)
                        EnterState(MonsterState.Patrol);
                    break;

                case MonsterState.Alert:
                    FaceTowards(head, 360f);
                    if (inState > 0.9f)
                        EnterState(MonsterState.Chase);
                    break;

                case MonsterState.Chase:
                    if (now >= m_NextRepath)
                    {
                        m_NextRepath = now + 0.2f;
                        SetDestination(Room.PlayerFloorPosition(m_CanSeePlayer ? head : m_LastKnownPosition));
                    }
                    if (now < m_StaggerUntil && m_Agent.enabled)
                        m_Agent.velocity *= 0.85f;
                    if (flatDistance < attackRange && (m_CanSeePlayer || flatDistance < 1.1f))
                    {
                        EnterState(MonsterState.Attack);
                        break;
                    }
                    if (now - m_LastSeenTime > loseSightDelay)
                        EnterState(MonsterState.Search);
                    MaybeAmbientVoice(now, m_Assets.growl, 2.5f, 4.5f, 0.9f);
                    break;

                case MonsterState.Attack:
                    FaceTowards(head, 240f);
                    if (!m_HitDone && inState > 0.3f && inState - Time.deltaTime <= 0.3f)
                        PlayVoice(m_Assets.attackSwoosh, 0.9f);
                    if (!m_HitDone && inState >= strikeTime)
                    {
                        m_HitDone = true;
                        if (FlatDistance(transform.position, Player.HeadPosition) < hitRange && !Player.IsDead)
                            m_Game.OnPlayerHit(this);
                    }
                    if (inState > 0.95f && State == MonsterState.Attack)
                        EnterState(MonsterState.Recover);
                    break;

                case MonsterState.Recover:
                    FaceTowards(head, 120f);
                    plush.jaw = inState < 0.8f ? 0.7f : 0f;
                    if (inState > recoverTime)
                        EnterState(MonsterState.Chase);
                    break;
            }

            // Le coup a pu tuer le joueur et lancer le jumpscare : on ne touche plus à l'animation.
            if (State == MonsterState.Jumpscare || State == MonsterState.Dead)
                return;

            // Paramètres d'animation.
            plush.speed = m_Agent.enabled ? m_Agent.velocity.magnitude : 0f;
            bool watching = State == MonsterState.Alert || State == MonsterState.Chase || State == MonsterState.Attack || State == MonsterState.Recover;
            plush.hasLookTarget = watching || (m_CanSeePlayer && State != MonsterState.Patrol);
            plush.lookTarget = head;
            plush.allowTwitch = !watching;

            UpdateLoopAudio();
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void FaceTowards(Vector3 target, float degreesPerSecond)
        {
            var dir = target - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                return;
            var goal = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, degreesPerSecond * Time.deltaTime);
        }

        bool CanSeePlayer()
        {
            var eye = plush.EyePosition;
            var target = Player.HeadPosition;
            var toTarget = target - eye;
            float distance = toTarget.magnitude;
            bool crouched = Player.IsCrouching(Room.FloorY);
            float flat = FlatDistance(eye, target);

            float proximity = crouched ? proximityRadiusCrouched : proximityRadius;
            if (flat < proximity)
                return flat < proximity * 0.6f || HasLineOfSight(eye, target);

            float range = crouched ? sightRangeCrouched : sightRange;
            if (!m_Game.FlashlightOn)
                range *= 0.75f;
            if (IsHunting)
                range *= 1.4f;
            if (distance > range)
                return false;

            float fov = IsHunting ? 200f : fieldOfView;
            if (Vector3.Angle(plush.HeadForward, toTarget) > fov * 0.5f &&
                Vector3.Angle(transform.forward, toTarget) > fov * 0.5f)
                return false;

            return HasLineOfSight(eye, target);
        }

        /// <summary>Vrai si aucun élément de la pièce réelle (maillage, plans, meubles) ne bloque la vue.</summary>
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            var dir = to - from;
            float distance = dir.magnitude;
            if (distance < 0.01f)
                return true;
            int count = Physics.RaycastNonAlloc(from, dir / distance, s_Hits, Mathf.Max(distance - 0.25f, 0.01f), ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = s_Hits[i].collider;
                if (col == null || col.transform.IsChildOf(transform))
                    continue;
                if (col.GetComponentInParent<HorrorOwned>() != null)
                    continue;
                return false;
            }
            return true;
        }

        void OnFootstep(int foot)
        {
            if (steps == null || m_Assets.footsteps == null || m_Assets.footsteps.Length == 0)
                return;
            steps.pitch = UnityEngine.Random.Range(0.85f, 1.05f) * (plush.crouch > 0.5f ? 1.1f : 0.95f);
            float volume = State == MonsterState.Chase ? 1f : plush.crouch > 0.5f ? 0.45f : 0.75f;
            steps.PlayOneShot(m_Assets.footsteps[UnityEngine.Random.Range(0, m_Assets.footsteps.Length)], volume);
        }

        void PlayVoice(AudioClip clip, float volume)
        {
            if (voice == null || clip == null)
                return;
            voice.pitch = UnityEngine.Random.Range(0.92f, 1.05f);
            voice.PlayOneShot(clip, volume);
        }

        void MaybeAmbientVoice(float now, AudioClip clip, float min, float max, float volume)
        {
            if (now < m_NextVoice)
                return;
            if (m_NextVoice > 0f)
                PlayVoice(clip, volume);
            m_NextVoice = now + UnityEngine.Random.Range(min, max);
        }

        void UpdateLoopAudio()
        {
            if (loop == null)
                return;
            float target = State == MonsterState.Patrol || State == MonsterState.Search ? 0.55f : 0.12f;
            loop.volume = Mathf.MoveTowards(loop.volume, target, Time.deltaTime * 0.5f);
            loop.pitch = Mathf.MoveTowards(loop.pitch, IsHunting ? 0.82f : 1f, Time.deltaTime * 0.4f);
        }

        /// <summary>Touché par une balle.</summary>
        public void TakeDamage(Vector3 point, Vector3 direction)
        {
            if (State == MonsterState.Dead || State == MonsterState.Jumpscare || State == MonsterState.Inactive)
                return;

            Health--;
            Effects.BloodBurst(point, -direction, 1f);
            Effects.StuffingBurst(point, -direction, false);
            PlayVoice(UnityEngine.Random.value < 0.5f ? m_Assets.monsterSqueak : m_Assets.monsterHurt, 1f);
            if (m_Assets.fabricRip != null)
                HorrorAudio.PlayAt(m_Assets.fabricRip, point, 0.7f);
            plush.TriggerFlinch();
            m_StaggerUntil = Time.time + 0.35f;
            Damaged?.Invoke(this);

            if (Health <= 0)
            {
                Die();
                return;
            }

            m_LastKnownPosition = Player.HeadPosition;
            m_LastSeenTime = Time.time;
            if (State == MonsterState.Patrol || State == MonsterState.Search)
                EnterState(MonsterState.Alert);
        }

        void Die()
        {
            State = MonsterState.Dead;
            m_StateTime = Time.time;
            if (m_Agent.enabled)
            {
                StopAgent();
                m_Agent.enabled = false;
            }
            plush.speed = 0f;
            plush.hasLookTarget = false;
            plush.TriggerDeath();
            PlayVoice(m_Assets.monsterDeath, 1f);
            foreach (var col in m_Colliders)
            {
                if (col != null)
                    col.enabled = false;
            }
            StartCoroutine(DeathRoutine());
            Killed?.Invoke(this);
        }

        IEnumerator DeathRoutine()
        {
            Effects.StuffingBurst(ChestPosition, transform.forward, true);
            Effects.BloodBurst(ChestPosition, transform.forward, 2f);
            yield return new WaitForSeconds(1.0f);
            Effects.BloodPool(new Vector3(transform.position.x, Room.FloorY, transform.position.z) + transform.forward * 0.35f);

            // La boîte à musique ralentit et meurt avec la peluche.
            float t = 0f;
            float startVolume = loop != null ? Mathf.Max(loop.volume, 0.4f) : 0f;
            while (loop != null && t < 4f)
            {
                t += Time.deltaTime;
                loop.pitch = Mathf.Lerp(0.9f, 0.25f, t / 4f);
                loop.volume = Mathf.Lerp(startVolume, 0f, t / 4f);
                yield return null;
            }
            if (loop != null)
                loop.Stop();
        }

        /// <summary>Mort du joueur : la peluche lui saute au visage (façon FNAF).</summary>
        public void StartJumpscare(Transform head)
        {
            StopAllCoroutines();
            State = MonsterState.Jumpscare;
            if (m_Agent.enabled)
            {
                StopAgent();
                m_Agent.enabled = false;
            }
            if (loop != null)
                loop.Stop();
            plush.speed = 0f;
            plush.crouch = 0f;
            plush.reach = 0f;
            plush.jumpscare = true;
            plush.hasLookTarget = true;
            plush.allowTwitch = false;

            // Sans lumière, le visage resterait dans le noir.
            var lightGo = new GameObject("Lumière jumpscare");
            lightGo.transform.SetParent(plush.HeadBone != null ? plush.HeadBone : transform, false);
            lightGo.transform.position = plush.EyePosition + transform.forward * 0.6f + Vector3.down * 0.25f;
            var faceLight = lightGo.AddComponent<Light>();
            faceLight.type = LightType.Point;
            faceLight.color = new Color(1f, 0.55f, 0.45f);
            faceLight.range = 2.5f;
            faceLight.intensity = 3f;
            faceLight.shadows = LightShadows.None;

            StartCoroutine(JumpscareRoutine(head));
        }

        IEnumerator JumpscareRoutine(Transform head)
        {
            var flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.01f)
                flatForward = Vector3.forward;
            flatForward.Normalize();
            float t = 0f;
            while (State == MonsterState.Jumpscare)
            {
                t += Time.deltaTime;
                float distance = Mathf.Lerp(1.0f, 0.5f, Mathf.Clamp01(t / 0.12f));
                var targetFace = head.position + flatForward * distance;
                transform.rotation = Quaternion.LookRotation(-flatForward, Vector3.up);
                var eyeOffset = plush.EyePosition - transform.position;
                transform.position = targetFace - eyeOffset;
                plush.lookTarget = head.position;
                yield return null;
            }
        }

        public void Hide()
        {
            StopAllCoroutines();
            State = MonsterState.Inactive;
            gameObject.SetActive(false);
        }
    }
}

using System;
using UnityEngine;

namespace HorrorMR
{
    /// <summary>
    /// Pistolet : posé dans la pièce avec un faisceau lumineux et un bip pour le repérer, se colle
    /// dans la main quand on l'attrape (grip ou gâchette à proximité), tire à la gâchette.
    /// Munitions infinies, petite aide à la visée.
    /// </summary>
    public class Gun : MonoBehaviour
    {
        public enum GunState { Hidden, Available, Held }

        [Header("Références")]
        public Transform model;
        public Transform muzzle;
        public LineRenderer laser;
        public GameObject beacon;
        public Light glow;

        [Header("Réglages")]
        public float grabRadius = 0.3f;
        public float fireInterval = 0.22f;
        public float range = 40f;
        public float aimAssistDegrees = 4f;
        public float hoverHeight = 0.12f;
        public Vector3 heldLocalPosition = new Vector3(0f, -0.025f, -0.07f);
        public Vector3 heldLocalEuler = Vector3.zero;

        public GunState State { get; private set; } = GunState.Hidden;
        public Hand HeldBy { get; private set; }

        public event Action PickedUp;

        GameManager m_Game;
        HorrorAssets m_Assets;
        AudioSource m_Audio;
        Vector3 m_RestPosition;
        float m_NextFire;
        float m_Recoil;
        float m_NextBeep;
        static readonly RaycastHit[] s_Hits = new RaycastHit[24];

        public void Init(GameManager game, HorrorAssets assets)
        {
            m_Game = game;
            m_Assets = assets;
            m_Audio = gameObject.AddComponent<AudioSource>();
            HorrorAudio.Configure3D(m_Audio, 0.8f, 20f);
            foreach (var col in GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            if (GetComponent<HorrorOwned>() == null)
                gameObject.AddComponent<HorrorOwned>();
            Hide();
        }

        public void Hide()
        {
            State = GunState.Hidden;
            transform.SetParent(null, true);
            gameObject.SetActive(false);
        }

        public void PlaceAt(Vector3 surfacePoint)
        {
            transform.SetParent(null, true);
            m_RestPosition = surfacePoint + Vector3.up * hoverHeight;
            transform.position = m_RestPosition;
            transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            if (model != null)
                model.localRotation = Quaternion.identity;
            gameObject.SetActive(true);
            State = GunState.Available;
            if (beacon != null)
                beacon.SetActive(true);
            if (glow != null)
                glow.enabled = true;
            if (laser != null)
                laser.enabled = false;
            m_NextBeep = Time.time + 0.5f;
        }

        void Update()
        {
            if (m_Game == null)
                return;
            switch (State)
            {
                case GunState.Available:
                    UpdateAvailable();
                    break;
                case GunState.Held:
                    UpdateHeld();
                    break;
            }
        }

        void UpdateAvailable()
        {
            float t = Time.time;
            transform.position = m_RestPosition + Vector3.up * (Mathf.Sin(t * 2f) * 0.02f);
            transform.Rotate(0f, 45f * Time.deltaTime, 0f, Space.World);
            if (glow != null)
                glow.intensity = 1.2f + 0.6f * Mathf.Sin(t * 5f);

            if (t >= m_NextBeep)
            {
                m_NextBeep = t + 1.6f;
                if (m_Assets.gunBeacon != null)
                    m_Audio.PlayOneShot(m_Assets.gunBeacon, 0.8f);
            }

            TryGrab(m_Game.LeftHand, Hand.Left);
            if (State == GunState.Available)
                TryGrab(m_Game.RightHand, Hand.Right);
        }

        void TryGrab(HandPose hand, Hand side)
        {
            if (hand == null || !hand.HasPose)
                return;
            if (Vector3.Distance(hand.transform.position, transform.position) > grabRadius)
                return;
            if (!(HorrorInput.GripPressed(side) || HorrorInput.TriggerPressed(side)))
                return;
            Grab(hand, side);
        }

        /// <summary>"Snap" : le pistolet se place tout seul correctement dans la main.</summary>
        public void Grab(HandPose hand, Hand side)
        {
            if (State != GunState.Available)
                return;
            State = GunState.Held;
            HeldBy = side;
            transform.SetParent(hand.transform, false);
            transform.localPosition = heldLocalPosition;
            transform.localRotation = Quaternion.Euler(heldLocalEuler);
            if (beacon != null)
                beacon.SetActive(false);
            if (glow != null)
                glow.enabled = false;
            if (laser != null)
                laser.enabled = true;
            m_NextFire = Time.time + 0.3f;
            if (m_Assets.gunPickup != null)
                m_Audio.PlayOneShot(m_Assets.gunPickup, 1f);
            HorrorInput.Haptic(side, 0.6f, 0.12f);
            PickedUp?.Invoke();
        }

        void UpdateHeld()
        {
            m_Recoil = Mathf.MoveTowards(m_Recoil, 0f, Time.deltaTime * 6f);
            if (model != null)
                model.localRotation = Quaternion.Euler(-14f * m_Recoil, 0f, 0f);

            if (HorrorInput.TriggerPressed(HeldBy) && Time.time >= m_NextFire)
                Fire();

            if (laser != null && muzzle != null)
            {
                var end = muzzle.position + muzzle.forward * 12f;
                if (Raycast(muzzle.position, muzzle.forward, 12f, out var hit))
                    end = hit.point;
                laser.SetPosition(0, muzzle.position);
                laser.SetPosition(1, end);
            }
        }

        bool Raycast(Vector3 origin, Vector3 direction, float distance, out RaycastHit best)
        {
            best = default;
            int count = Physics.RaycastNonAlloc(origin, direction, s_Hits, distance, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = s_Hits[i];
                if (hit.collider == null || hit.collider.GetComponentInParent<HorrorOwned>() != null)
                    continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = hit;
                    found = true;
                }
            }
            return found;
        }

        public void Fire()
        {
            m_NextFire = Time.time + fireInterval;
            m_Recoil = 1f;
            if (m_Assets.gunshot != null)
                m_Audio.PlayOneShot(m_Assets.gunshot, 1f);
            HorrorInput.Haptic(HeldBy, 1f, 0.09f);
            Effects.MuzzleFlash(muzzle);
            m_Game.Darkness.Flash(0.35f);

            var origin = muzzle.position;
            var direction = muzzle.forward;
            Vector3 end = origin + direction * range;
            MonsterAI target = null;
            Vector3 hitPoint = end;
            Vector3 hitNormal = -direction;

            if (Raycast(origin, direction, range, out var hit))
            {
                end = hit.point;
                hitPoint = hit.point;
                hitNormal = hit.normal;
                target = hit.collider.GetComponentInParent<MonsterAI>();
            }

            // Aide à la visée : on accepte un tir légèrement à côté si le monstre est bien visible.
            var monster = m_Game.Monster;
            if (target == null && monster != null && !monster.IsDead)
            {
                var toChest = monster.ChestPosition - origin;
                if (Vector3.Angle(direction, toChest) < aimAssistDegrees && toChest.magnitude < range &&
                    (!Raycast(origin, toChest.normalized, toChest.magnitude, out var blocker) || blocker.collider.GetComponentInParent<MonsterAI>() == monster))
                {
                    target = monster;
                    hitPoint = monster.ChestPosition;
                    end = hitPoint;
                }
            }

            Effects.Tracer(origin, end);
            if (target != null)
            {
                target.TakeDamage(hitPoint, direction);
                m_Game.OnMonsterShot(target);
            }
            else if (hitPoint != origin + direction * range)
            {
                Effects.Sparks(hitPoint, hitNormal);
                if (m_Assets.bulletImpact != null)
                    HorrorAudio.PlayAt(m_Assets.bulletImpact, hitPoint, 0.6f);
            }
        }
    }
}

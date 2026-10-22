using System;
using UnityEngine;

namespace HorrorMR
{
    /// <summary>Vie du joueur (3 coups = mort) et détection de l'accroupissement.</summary>
    public class PlayerState : MonoBehaviour
    {
        public Transform head;
        public int maxHealth = 3;
        [Tooltip("Rapport hauteur actuelle / hauteur debout en dessous duquel on est accroupi.")]
        public float crouchRatio = 0.72f;

        public int Health { get; private set; }
        public bool IsDead => Health <= 0;
        public float StandingHeight { get; private set; } = 1.65f;
        public Vector3 HeadPosition => head != null ? head.position : transform.position;

        public event Action<int> Damaged;
        public event Action Died;

        void Awake()
        {
            Health = maxHealth;
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
        }

        public void CalibrateStanding(float floorY)
        {
            StandingHeight = Mathf.Clamp(HeadPosition.y - floorY, 1.2f, 2.1f);
        }

        public bool IsCrouching(float floorY) => HeadPosition.y - floorY < StandingHeight * crouchRatio;

        public void ResetHealth() => Health = maxHealth;

        public void TakeHit()
        {
            if (IsDead)
                return;
            Health--;
            Damaged?.Invoke(Health);
            if (IsDead)
                Died?.Invoke();
        }
    }
}

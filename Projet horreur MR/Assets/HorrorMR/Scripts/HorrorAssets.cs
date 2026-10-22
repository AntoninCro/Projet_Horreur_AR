using UnityEngine;

namespace HorrorMR
{
    /// <summary>
    /// Toutes les ressources partagées du jeu (sons, matériaux, prefabs).
    /// Rempli automatiquement par le menu "Horreur MR > Tout configurer".
    /// </summary>
    [CreateAssetMenu(menuName = "Horreur MR/Ressources", fileName = "HorrorAssets")]
    public class HorrorAssets : ScriptableObject
    {
        [Header("Prefabs")]
        public GameObject monsterPrefab;
        public GameObject gunPrefab;
        public GameObject flashlightPrefab;

        [Header("Ambiance")]
        public AudioClip ambience;
        public AudioClip heartbeat;
        public AudioClip startSting;
        public AudioClip alarm;
        public AudioClip tick;
        public AudioClip victory;
        public AudioClip spottedSting;

        [Header("Monstre")]
        public AudioClip musicBox;
        public AudioClip growl;
        public AudioClip whisper;
        public AudioClip spottedScream;
        public AudioClip jumpscareScream;
        public AudioClip attackSwoosh;
        public AudioClip monsterHurt;
        public AudioClip monsterSqueak;
        public AudioClip monsterDeath;
        public AudioClip fabricRip;
        public AudioClip[] footsteps;

        [Header("Joueur")]
        public AudioClip playerHit;
        public AudioClip gunshot;
        public AudioClip gunPickup;
        public AudioClip gunBeacon;
        public AudioClip flashlightClick;
        public AudioClip bulletImpact;

        [Header("Matériaux d'effets")]
        public Material bloodParticle;
        public Material stuffingParticle;
        public Material sparkParticle;
        public Material muzzleFlash;
        public Material bloodPool;
        public Material beacon;
        public Material laser;
        public Material arrow;

        [Header("Matériaux de rendu MR")]
        public Material roomDarkness;
        public Material darknessDome;
        public Material screenOverlay;

        [Header("Police (optionnelle)")]
        public TMPro.TMP_FontAsset font;
    }
}

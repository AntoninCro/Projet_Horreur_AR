using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace HorrorMR
{
    public enum GamePhase { Boot, Scanning, Calibration, Menu, Intro, Survival, GunHunt, Armed, Victory, GameOver }

    /// <summary>
    /// Déroulé de "Prêt ou pas, ils arrivent" :
    /// scan de la pièce → menu → la pièce s'éteint, lampe torche → survivre au minuteur →
    /// trouver le pistolet → abattre la peluche (5 balles) → victoire. 3 coups reçus = jumpscare et mort.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Ressources")]
        public HorrorAssets assets;

        [Header("Scène")]
        public XROrigin xrOrigin;
        public ARSession arSession;
        public RoomModel room;
        public PlayerState player;
        public HandPose leftHand;
        public HandPose rightHand;
        public Flashlight flashlight;
        public DarknessController darkness;
        public ScreenFX screenFX;
        public HUD hud;
        public HorrorAudio horrorAudio;

        [Header("Pièce scannée à la main")]
        public ManualRoom manualRoom;
        public RoomCalibrator calibrator;
        public ARAnchorManager anchorManager;
        [Tooltip("Enregistrer la pièce (fichier + ancre spatiale) pour ne pas la rescanner à chaque partie.")]
        public bool persistRoom = true;
        [Tooltip("Temps d'attente des données de pièce du Quest avant de passer au scan manuel (s).")]
        public float questRoomWait = 5f;

        [Header("Partie")]
        public float survivalSeconds = 90f;
        public float minSpawnDistance = 5f;
        [Tooltip("Pour tester : touche K (éditeur) ou A + X avec les deux grips pour passer le minuteur.")]
        public bool allowDebugSkip = true;

        public GamePhase Phase { get; private set; } = GamePhase.Boot;
        public MonsterAI Monster { get; private set; }
        public Gun Gun { get; private set; }
        public PlayerState Player => player;
        public RoomModel Room => room;
        public DarknessController Darkness => darkness;
        public HandPose LeftHand => leftHand;
        public HandPose RightHand => rightHand;
        public bool FlashlightOn => flashlight != null && flashlight.IsOn;

        const string k_Title = "PRÊT OU PAS, ILS ARRIVENT";

        float m_TimeLeft;
        float m_RoundStart;
        int m_LastTick = -1;
        float m_NextHousekeeping;
        float m_SpottedMessageUntil = -1f;
        float m_CanRestartAt;
        bool m_SkipScan;
        bool m_Rebuilding;
        float m_NextRoomCheck;
        Coroutine m_RoundRoutine;
        RoomLayout m_PendingLayout;
        Pose m_PendingFrame;
        string m_PhaseMessage = "";
        Color m_PhaseColor = Color.white;

        void Awake()
        {
            Instance = this;
        }

        IEnumerator Start()
        {
            HorrorInput.Init();
            Effects.Init(assets);
            horrorAudio.Init(assets);
            if (player.head == null && Camera.main != null)
                player.head = Camera.main.transform;
            hud.head = player.head;
            darkness.head = player.head;
            darkness.flashlight = flashlight;
            darkness.SetImmediate(0f);

            var cam = player.head != null ? player.head.GetComponent<Camera>() : null;
            if (cam != null)
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 120f);

            if (assets.gunPrefab != null)
            {
                Gun = Instantiate(assets.gunPrefab).GetComponent<Gun>();
                Gun.Init(this, assets);
                Gun.PickedUp += OnGunPickedUp;
            }

            CleanRig();
            Phase = GamePhase.Scanning;
            hud.Show(k_Title, "Analyse de la pièce…", "");
            horrorAudio.SetAmbience(0.25f);

            if (calibrator != null)
                calibrator.Init(this);

            yield return RequestScenePermission();
            SuppressBoundary();
            yield return PrepareRoom();
            yield return BuildRoomNavMesh();
            EnterMenu();
        }

        // ------------------------------------------------------------------------------------------
        // Scan de la pièce
        // ------------------------------------------------------------------------------------------

        IEnumerator RequestScenePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string permission = "com.oculus.permission.USE_SCENE";
            if (Permission.HasUserAuthorizedPermission(permission))
                yield break;
            bool done = false;
            bool granted = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => { done = true; granted = true; };
            callbacks.PermissionDenied += _ => { done = true; };
            Permission.RequestUserPermission(permission, callbacks);
            float timeout = Time.time + 30f;
            while (!done && Time.time < timeout)
                yield return null;
            if (granted)
            {
                // Les gestionnaires AR doivent redémarrer pour lire les données de la pièce.
                RestartSceneManagers();
                yield return new WaitForSeconds(0.5f);
            }
            else
            {
                Debug.LogWarning("[Horreur] Permission des données spatiales refusée : mode sans scan.");
            }
#else
            yield break;
#endif
        }

        void RestartSceneManagers()
        {
            if (room.planeManager != null) { room.planeManager.enabled = false; room.planeManager.enabled = true; }
            if (room.boundingBoxManager != null) { room.boundingBoxManager.enabled = false; room.boundingBoxManager.enabled = true; }
            if (room.meshManager != null) { room.meshManager.enabled = false; room.meshManager.enabled = true; }
        }

        IEnumerator BuildRoomNavMesh()
        {
            float originY = xrOrigin != null ? xrOrigin.transform.position.y : 0f;
            room.SetFallbackFloor(originY);
            room.RefreshSurfaceRendering();
            hud.Show(k_Title, "Préparation de la pièce…", "");
            yield return room.BuildNavMesh(player.HeadPosition);
        }

        /// <summary>Pièce enregistrée, sinon données du Quest, sinon scan manuel.</summary>
        IEnumerator PrepareRoom()
        {
            if (m_PendingLayout != null)
            {
                ApplyManualRoom(m_PendingLayout, m_PendingFrame, null);
                m_PendingLayout = null;
                yield break;
            }

            if (manualRoom != null && RoomLayout.Exists)
            {
                var saved = RoomLayout.Load();
                if (saved != null)
                {
                    yield return LoadSavedRoom(saved);
                    if (room.UsingManualRoom)
                        yield break;
                }
            }

            yield return WaitForQuestRoom();
            if (!room.HasRoomData && !m_SkipScan && calibrator != null)
                yield return ScanRoom();
        }

        IEnumerator WaitForQuestRoom()
        {
            float deadline = Time.time + questRoomWait;
            float firstDataTime = -1f;
            while (Time.time < deadline && !m_SkipScan)
            {
                if (room.HasRoomData)
                {
                    if (firstDataTime < 0f)
                    {
                        firstDataTime = Time.time;
                        deadline = Time.time + 4f;
                    }
                    // On laisse un peu de temps au maillage et aux meubles d'arriver après le sol.
                    if (Time.time - firstDataTime > (room.MeshCount > 0 ? 1.5f : 3f))
                        yield break;
                }
                yield return null;
            }
        }

        // ------------------------------------------------------------------------------------------
        // Pièce scannée à la main : scan, sauvegarde (ancre spatiale), chargement, recalage
        // ------------------------------------------------------------------------------------------

        /// <summary>Tests automatiques : pièce imposée, sans scan ni sauvegarde.</summary>
        public void UseRoomForTesting(RoomLayout layout, Pose frame)
        {
            m_PendingLayout = layout;
            m_PendingFrame = frame;
        }

        IEnumerator ScanRoom()
        {
            var previous = Phase;
            Phase = GamePhase.Calibration;
            flashlight.SetOn(false, false);
            darkness.SetImmediate(0f);
            if (manualRoom != null && manualRoom.HasRoom)
                manualRoom.Clear();
            RoomLayout layout = null;
            var frame = default(Pose);
            yield return calibrator.RunFullScan((l, f) => { layout = l; frame = f; });
            yield return SaveAndApplyRoom(layout, frame);
            Phase = previous;
        }

        IEnumerator SaveAndApplyRoom(RoomLayout layout, Pose frame)
        {
            Transform anchorTransform = null;
            if (persistRoom)
            {
                hud.Show(k_Title, "Enregistrement de la pièce…", "");
                ARAnchor anchor = null;
                yield return CreatePersistentAnchor(frame, layout, a => anchor = a);
                anchorTransform = anchor != null ? anchor.transform : null;
                layout.lastFramePosition = frame.position;
                layout.lastFrameRotation = frame.rotation;
                try
                {
                    layout.Save();
                    Debug.Log("[Horreur] Pièce enregistrée : " + RoomLayout.FilePath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Horreur] Impossible d'enregistrer la pièce : " + e.Message);
                }
            }
            ApplyManualRoom(layout, frame, anchorTransform);
        }

        bool AnchorsAvailable => anchorManager != null && anchorManager.enabled && anchorManager.subsystem != null && anchorManager.subsystem.running;

        IEnumerator CreatePersistentAnchor(Pose frame, RoomLayout layout, Action<ARAnchor> done)
        {
            if (!AnchorsAvailable)
            {
                done(null);
                yield break;
            }
            var add = anchorManager.TryAddAnchorAsync(frame).GetAwaiter();
            while (!add.IsCompleted)
                yield return null;
            var added = add.GetResult();
            if (!added.status.IsSuccess() || added.value == null)
            {
                Debug.LogWarning("[Horreur] Création de l'ancre impossible : " + added.status);
                done(null);
                yield break;
            }
            var anchor = added.value;

            var descriptor = anchorManager.descriptor;
            if (descriptor != null && descriptor.supportsSaveAnchor)
            {
                if (descriptor.supportsEraseAnchor && !string.IsNullOrEmpty(layout.anchorGuid) && Guid.TryParse(layout.anchorGuid, out var oldGuid))
                {
                    var erase = anchorManager.TryEraseAnchorAsync(new UnityEngine.XR.ARSubsystems.SerializableGuid(oldGuid)).GetAwaiter();
                    float timeout = Time.time + 3f;
                    while (!erase.IsCompleted && Time.time < timeout)
                        yield return null;
                    if (erase.IsCompleted)
                        erase.GetResult();
                }
                var save = anchorManager.TrySaveAnchorAsync(anchor).GetAwaiter();
                while (!save.IsCompleted)
                    yield return null;
                var saved = save.GetResult();
                if (saved.status.IsSuccess())
                    layout.anchorGuid = saved.value.guid.ToString();
                else
                    Debug.LogWarning("[Horreur] Sauvegarde de l'ancre impossible : " + saved.status);
            }
            done(anchor);
        }

        IEnumerator LoadSavedRoom(RoomLayout layout)
        {
            hud.Show(k_Title, "Chargement de la pièce enregistrée…", "");
            // Le sous-système d'ancres démarre avec la session XR.
            float wait = Time.time + 4f;
            while (!AnchorsAvailable && Time.time < wait)
                yield return null;

            Pose? frame = null;
            Transform anchorTransform = null;
            var descriptor = AnchorsAvailable ? anchorManager.descriptor : null;
            bool canLoad = descriptor != null && descriptor.supportsLoadAnchor;

            if (canLoad && !string.IsNullOrEmpty(layout.anchorGuid) && Guid.TryParse(layout.anchorGuid, out var guid))
            {
                var load = anchorManager.TryLoadAnchorAsync(new UnityEngine.XR.ARSubsystems.SerializableGuid(guid)).GetAwaiter();
                float timeout = Time.time + 8f;
                while (!load.IsCompleted && Time.time < timeout)
                    yield return null;
                if (load.IsCompleted)
                {
                    var loaded = load.GetResult();
                    if (loaded.status.IsSuccess() && loaded.value != null)
                    {
                        var anchor = loaded.value;
                        float tracking = Time.time + 5f;
                        while (anchor != null && anchor.trackingState != TrackingState.Tracking && Time.time < tracking)
                            yield return null;
                        if (anchor != null && anchor.trackingState == TrackingState.Tracking)
                        {
                            anchorTransform = anchor.transform;
                            frame = new Pose(anchor.transform.position, anchor.transform.rotation);
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[Horreur] Ancre de la pièce introuvable : " + loaded.status);
                    }
                }
            }
            else if (!canLoad)
            {
                // Pas d'ancres persistantes (éditeur) : on réutilise le repère de la dernière session.
                frame = new Pose(layout.lastFramePosition, layout.lastFrameRotation);
            }

            if (frame.HasValue)
            {
                ApplyManualRoom(layout, frame.Value, anchorTransform);
                Debug.Log("[Horreur] Pièce enregistrée chargée" + (anchorTransform != null ? " (ancre retrouvée)." : "."));
                yield break;
            }

            // Ancre perdue : recaler avec 2 coins, ou tout rescanner.
            var previous = Phase;
            Phase = GamePhase.Calibration;
            hud.Show(k_Title, "Ta pièce enregistrée n'a pas été retrouvée automatiquement.",
                "Gâchette : recaler (toucher les coins 1 et 2)\nB / Y : refaire un scan complet", HUD.Orange);
            yield return null;
            while (true)
            {
                if (HorrorInput.AnyTriggerPressed)
                {
                    yield return null;
                    var newFrame = default(Pose);
                    yield return calibrator.RunRealign(layout, f => newFrame = f);
                    yield return SaveAndApplyRoom(layout, newFrame);
                    break;
                }
                if (HorrorInput.AnySecondaryPressed)
                {
                    yield return null;
                    yield return ScanRoom();
                    break;
                }
                yield return null;
            }
            Phase = previous;
        }

        void ApplyManualRoom(RoomLayout layout, Pose frame, Transform anchor)
        {
            manualRoom.Apply(layout, frame, anchor);
            room.manualRoom = manualRoom;
            // La pièce manuelle remplace les données du Quest (sinon double assombrissement).
            if (room.planeManager != null) { room.planeManager.SetTrackablesActive(false); room.planeManager.enabled = false; }
            if (room.boundingBoxManager != null) { room.boundingBoxManager.SetTrackablesActive(false); room.boundingBoxManager.enabled = false; }
            if (room.meshManager != null) { room.meshManager.DestroyAllMeshes(); room.meshManager.enabled = false; }
        }

        /// <summary>Relance le scan manuel depuis le menu (bouton B/Y, ou tests).</summary>
        public void RescanRoom()
        {
            if (Phase == GamePhase.Menu && !m_Rebuilding && calibrator != null && manualRoom != null)
                StartCoroutine(RescanFromMenu());
        }

        IEnumerator RescanFromMenu()
        {
            m_Rebuilding = true;
            yield return null;
            yield return ScanRoom();
            Phase = GamePhase.Scanning;
            yield return BuildRoomNavMesh();
            m_Rebuilding = false;
            EnterMenu();
        }

        /// <summary>Demande au Quest de masquer le Guardian (autorisé pour les applis en passthrough).</summary>
        void SuppressBoundary()
        {
#if HORROR_META_OPENXR && (UNITY_ANDROID || UNITY_EDITOR)
            if (!UnityEngine.XR.XRSettings.isDeviceActive)
                return;
            try
            {
                var settings = UnityEngine.XR.OpenXR.OpenXRSettings.Instance;
                var feature = settings != null ? settings.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.BoundaryVisibilityFeature>() : null;
                if (feature != null && feature.enabled)
                {
                    var result = feature.TryRequestBoundaryVisibility(UnityEngine.XR.OpenXR.Features.Meta.XrBoundaryVisibility.VisibilitySuppressed);
                    Debug.Log("[Horreur] Demande de masquage du Guardian : " + result);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Horreur] Masquage du Guardian impossible : " + e.Message);
            }
#endif
        }

        /// <summary>Coupe la locomotion, les rayons et les modèles de manettes du template (immersion + MR).</summary>
        void CleanRig()
        {
            if (xrOrigin == null)
                return;
            foreach (var provider in xrOrigin.GetComponentsInChildren<LocomotionProvider>(true))
                provider.enabled = false;
            foreach (var interactor in xrOrigin.GetComponentsInChildren<XRBaseInteractor>(true))
            {
                if (!IsProtected(interactor))
                    interactor.enabled = false;
            }
            foreach (var renderer in xrOrigin.GetComponentsInChildren<Renderer>(true))
            {
                if (!IsProtected(renderer))
                    renderer.enabled = false;
            }
            foreach (var behaviour in xrOrigin.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || IsProtected(behaviour))
                    continue;
                var typeName = behaviour.GetType().Name;
                if (typeName.Contains("Visualizer") || typeName.EndsWith("Visual") || typeName.Contains("Affordance") || typeName.Contains("Tooltip"))
                    behaviour.enabled = false;
            }
        }

        bool IsProtected(Component c)
        {
            if (c.GetComponentInParent<HorrorOwned>() != null || c.GetComponentInParent<ARTrackable>() != null)
                return true;
            if (room != null && room.meshManager != null && c.transform.IsChildOf(room.meshManager.transform))
                return true;
            return xrOrigin.TrackablesParent != null && c.transform.IsChildOf(xrOrigin.TrackablesParent);
        }

        // ------------------------------------------------------------------------------------------
        // Phases de jeu
        // ------------------------------------------------------------------------------------------

        void EnterMenu()
        {
            Phase = GamePhase.Menu;
            int s = Mathf.RoundToInt(survivalSeconds);
            string roomInfo = room.UsingManualRoom
                ? $"Pièce scannée à la main : {manualRoom.Layout.Width:F1} x {manualRoom.Layout.depth:F1} m, {manualRoom.Layout.furniture.Count} meuble(s)."
                : room.UsedFallback
                    ? "Pièce non scannée : zone de jeu de secours."
                    : $"Pièce du Quest : {room.PlaneCount} surfaces, {room.BoxCount} meubles, maillage {(room.MeshCount > 0 ? "oui" : "non")}.";
            if (manualRoom != null)
                manualRoom.SetOutlinesVisible(true);
            hud.Show(k_Title,
                $"Une peluche géante rôde dans la pièce.\nSurvis {s / 60}:{s % 60:00}, puis trouve l'arme et abats-la.",
                "ATTENTION : expérience d'horreur, déconseillée aux personnes cardiaques ou sensibles. Dégage la zone de jeu.\n" +
                roomInfo + "\nGâchette : commencer      B / Y : refaire le scan de la pièce",
                HUD.White);
            horrorAudio.SetAmbience(0.3f);
        }

        IEnumerator RebuildRoom()
        {
            m_Rebuilding = true;
            room.RefreshSurfaceRendering();
            yield return room.BuildNavMesh(player.HeadPosition);
            m_Rebuilding = false;
            if (Phase == GamePhase.Menu)
                EnterMenu();
        }

        /// <summary>Jouer sans scan de la pièce (zone de jeu de secours).</summary>
        public void SkipRoomScan() => m_SkipScan = true;

        public void StartRound()
        {
            ResetRound();
            if (m_RoundRoutine != null)
                StopCoroutine(m_RoundRoutine);
            m_RoundRoutine = StartCoroutine(IntroRoutine());
        }

        void ResetRound()
        {
            if (Monster != null)
            {
                Monster.Spotted -= OnMonsterSpotted;
                Monster.Killed -= OnMonsterKilled;
                Destroy(Monster.gameObject);
                Monster = null;
            }
            if (Gun != null)
                Gun.Hide();
            hud.SetArrowTarget(null);
            hud.HideTimer();
            screenFX.ResetAll();
            player.ResetHealth();
            horrorAudio.SetHeartbeat(0f);
            flashlight.SetNervousness(0f);
            m_SpottedMessageUntil = -1f;
            m_LastTick = -1;
        }

        IEnumerator IntroRoutine()
        {
            Phase = GamePhase.Intro;
            if (manualRoom != null)
                manualRoom.SetOutlinesVisible(false);
            SuppressBoundary();
            player.CalibrateStanding(room.FloorY);
            horrorAudio.PlayUI(assets.startSting, 1f);
            horrorAudio.SetAmbience(0.6f);
            darkness.FadeTo(1f, 5f);
            hud.Show("", "Les lumières s'éteignent…", "", HUD.White);
            yield return new WaitForSeconds(2.5f);

            flashlight.SetOn(true);
            hud.Show("", "Ils arrivent.", "Gâchette gauche : allumer / éteindre la lampe", HUD.Red);
            yield return new WaitForSeconds(3f);

            SpawnMonster();
            Phase = GamePhase.Survival;
            m_TimeLeft = survivalSeconds;
            m_RoundStart = Time.time;
            SetPhaseMessage("SURVIS", HUD.White);
            hud.SetHint("Cache-toi derrière les meubles. Accroupis-toi : elle te verra moins.");
            m_RoundRoutine = null;
        }

        void SpawnMonster()
        {
            var head = player.HeadPosition;
            if (!room.TryFindSpawnPoint(head, player.head.forward, minSpawnDistance, out var position) &&
                !room.TryRandomFloorPoint(out position))
            {
                position = room.PlayerFloorPosition(head) - Vector3.ProjectOnPlane(player.head.forward, Vector3.up).normalized * minSpawnDistance;
            }

            var toPlayer = Vector3.ProjectOnPlane(head - position, Vector3.up);
            var rotation = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer.normalized) : Quaternion.identity;
            var go = Instantiate(assets.monsterPrefab, position, rotation);
            if (go.TryGetComponent<NavMeshAgent>(out var agent))
                agent.Warp(position);
            Monster = go.GetComponent<MonsterAI>();
            Monster.Spotted += OnMonsterSpotted;
            Monster.Killed += OnMonsterKilled;
            Monster.Init(this, assets);
        }

        void EnterGunHunt()
        {
            Phase = GamePhase.GunHunt;
            hud.HideTimer();
            horrorAudio.PlayUI(assets.alarm, 1f);
            screenFX.HitFlash(0.5f);
            var spot = room.FindGunSpot(player.HeadPosition);
            if (Gun != null)
            {
                Gun.PlaceAt(spot);
                hud.SetArrowTarget(Gun.transform.position);
            }
            SetPhaseMessage("TROUVE L'ARME !", HUD.Red, true);
            hud.SetHint("Suis la flèche et le faisceau lumineux. Grip ou gâchette pour l'attraper.");
            if (Monster != null && !Monster.IsDead)
                Monster.Enrage();
        }

        void OnGunPickedUp()
        {
            if (Phase != GamePhase.GunHunt)
                return;
            Phase = GamePhase.Armed;
            hud.SetArrowTarget(null);
            SetPhaseMessage("TUE-LA !", HUD.Orange, true);
            hud.SetHint("Gâchette pour tirer. Il faut 5 balles.");
        }

        public void OnMonsterShot(MonsterAI monster)
        {
            if (monster.IsDead)
                return;
            hud.SetHint($"Touchée ! {monster.maxHealth - monster.Health} / {monster.maxHealth}");
        }

        void OnMonsterSpotted(MonsterAI monster)
        {
            if (Phase != GamePhase.Survival && Phase != GamePhase.GunHunt && Phase != GamePhase.Armed)
                return;
            horrorAudio.PlayUI(assets.spottedSting, 0.9f);
            screenFX.HitFlash(0.35f);
            HorrorInput.HapticBoth(0.5f, 0.25f);
            flashlight.FlickerFor(0.5f);
            hud.SetMessage("ELLE T'A VU ! FUIS !", HUD.Red, true);
            m_SpottedMessageUntil = Time.time + 2.5f;
        }

        void OnMonsterKilled(MonsterAI monster)
        {
            if (m_RoundRoutine != null)
                StopCoroutine(m_RoundRoutine);
            m_RoundRoutine = StartCoroutine(VictoryRoutine());
        }

        IEnumerator VictoryRoutine()
        {
            Phase = GamePhase.Victory;
            hud.SetArrowTarget(null);
            hud.SetMessage("…", HUD.White);
            hud.SetHint("");
            horrorAudio.SetHeartbeat(0f);
            screenFX.SetVignette(0f);
            flashlight.SetNervousness(0f);
            yield return new WaitForSeconds(2.5f);

            darkness.FadeTo(0f, 5f);
            horrorAudio.PlayUI(assets.victory, 1f);
            horrorAudio.SetAmbience(0.1f);
            int total = Mathf.RoundToInt(Time.time - m_RoundStart);
            hud.Show("TU AS SURVÉCU", $"La peluche est morte.\nTemps total : {total / 60}:{total % 60:00}", "A / X : rejouer", HUD.Green);
            m_CanRestartAt = Time.time + 1.5f;
            m_RoundRoutine = null;
        }

        /// <summary>Appelé par le monstre quand son coup touche.</summary>
        public void OnPlayerHit(MonsterAI monster)
        {
            if (player.IsDead || Phase == GamePhase.Victory || Phase == GamePhase.GameOver)
                return;
            player.TakeHit();
            screenFX.SetDamage(player.maxHealth - player.Health, player.maxHealth);
            screenFX.HitFlash(1f);
            horrorAudio.PlayUI(assets.playerHit, 1f);
            HorrorInput.HapticBoth(1f, 0.35f);
            flashlight.FlickerFor(0.7f);

            if (player.IsDead)
            {
                if (m_RoundRoutine != null)
                    StopCoroutine(m_RoundRoutine);
                m_RoundRoutine = StartCoroutine(GameOverRoutine(monster));
            }
            else
            {
                hud.SetMessage(player.Health == 1 ? "ENCORE UN COUP ET TU ES MORT" : "ELLE T'A FRAPPÉ !", HUD.Red, true);
                m_SpottedMessageUntil = Time.time + 2.5f;
            }
        }

        IEnumerator GameOverRoutine(MonsterAI monster)
        {
            Phase = GamePhase.GameOver;
            hud.Show("", "", "");
            hud.HideTimer();
            hud.SetArrowTarget(null);
            horrorAudio.SetHeartbeat(0f);
            screenFX.SetVignette(0f);
            if (Gun != null)
                Gun.Hide();

            monster.StartJumpscare(player.head);
            horrorAudio.PlayUI(assets.jumpscareScream, 1f);
            HorrorInput.HapticBoth(1f, 1.3f);
            yield return new WaitForSeconds(1.35f);

            screenFX.FadeTo(1f, 0.05f);
            yield return new WaitForSeconds(0.1f);
            monster.Hide();
            horrorAudio.SetAmbience(0.15f);
            yield return new WaitForSeconds(1.2f);

            hud.Show("TU ES MORT", "La peluche t'a eu…", "A / X : rejouer", HUD.Red);
            m_CanRestartAt = Time.time + 1f;
            m_RoundRoutine = null;
        }

        void SetPhaseMessage(string message, Color color, bool pulse = false)
        {
            m_PhaseMessage = message;
            m_PhaseColor = color;
            hud.SetMessage(message, color, pulse);
        }

        // ------------------------------------------------------------------------------------------

        void Update()
        {
            float now = Time.time;
            if (now >= m_NextHousekeeping)
            {
                m_NextHousekeeping = now + 2f;
                CleanRig();
                if (Phase != GamePhase.Boot && Phase != GamePhase.Scanning)
                    room.RefreshSurfaceRendering();
            }

            bool playing = Phase == GamePhase.Survival || Phase == GamePhase.GunHunt || Phase == GamePhase.Armed;

            switch (Phase)
            {
                case GamePhase.Menu:
                    if (m_Rebuilding)
                        break;
                    if (HorrorInput.AnyTriggerPressed)
                        StartRound();
                    else if (HorrorInput.AnySecondaryPressed)
                        RescanRoom();
                    else if (!room.UsingManualRoom && now >= m_NextRoomCheck)
                    {
                        // Le Quest peut encore compléter la pièce : on recalcule tant qu'on est au menu.
                        m_NextRoomCheck = now + 3f;
                        if (room.DataChangedSinceBuild)
                            StartCoroutine(RebuildRoom());
                    }
                    break;

                case GamePhase.Survival:
                    m_TimeLeft -= Time.deltaTime;
                    if (allowDebugSkip && HorrorInput.DebugSkipPressed)
                        m_TimeLeft = 0f;
                    hud.SetTimer(m_TimeLeft, m_TimeLeft <= 10f);
                    int tick = Mathf.CeilToInt(m_TimeLeft);
                    if (tick <= 10 && tick != m_LastTick && tick > 0)
                    {
                        m_LastTick = tick;
                        horrorAudio.PlayUI(assets.tick, 0.8f);
                    }
                    if (m_TimeLeft <= 0f)
                        EnterGunHunt();
                    break;

                case GamePhase.GunHunt:
                    if (Gun != null && Gun.State == Gun.GunState.Available)
                        hud.SetArrowTarget(Gun.transform.position);
                    break;

                case GamePhase.Victory:
                case GamePhase.GameOver:
                    if (now >= m_CanRestartAt && m_RoundRoutine == null && (HorrorInput.AnyPrimaryPressed || HorrorInput.AnyTriggerPressed))
                    {
                        darkness.FadeTo(1f, 2f);
                        StartRound();
                    }
                    break;
            }

            if (playing)
            {
                UpdateTension();
                if (m_SpottedMessageUntil > 0f && now > m_SpottedMessageUntil)
                {
                    m_SpottedMessageUntil = -1f;
                    hud.SetMessage(m_PhaseMessage, m_PhaseColor, Phase == GamePhase.GunHunt);
                }

                bool leftHoldsGun = Gun != null && Gun.State == Gun.GunState.Held && Gun.HeldBy == Hand.Left;
                bool leftNearGun = Gun != null && Gun.State == Gun.GunState.Available && leftHand != null &&
                                   Vector3.Distance(leftHand.transform.position, Gun.transform.position) < Gun.grabRadius * 1.5f;
                if (!leftHoldsGun && !leftNearGun && HorrorInput.TriggerPressed(Hand.Left))
                    flashlight.Toggle();
            }
        }

        void UpdateTension()
        {
            if (Monster == null || Monster.IsDead)
            {
                horrorAudio.SetHeartbeat(0.1f);
                screenFX.SetVignette(0f);
                flashlight.SetNervousness(0f);
                return;
            }

            var a = player.HeadPosition;
            var b = Monster.transform.position;
            a.y = b.y = 0f;
            float distance = Vector3.Distance(a, b);
            float tension = Mathf.Clamp01(1f - (distance - 1.2f) / 6f);
            if (Monster.IsHunting)
                tension = Mathf.Max(tension, 0.65f);
            horrorAudio.SetHeartbeat(tension);
            // Aide contextuelle du doc : le champ de vision se rétrécit quand le monstre est proche.
            screenFX.SetVignette(Mathf.Clamp01((4f - distance) / 3f) * 0.85f);
            flashlight.SetNervousness(Mathf.Clamp01((3.5f - distance) / 2.5f));
        }
    }
}

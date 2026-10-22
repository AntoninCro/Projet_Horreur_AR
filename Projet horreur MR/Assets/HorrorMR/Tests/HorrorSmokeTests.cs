using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HorrorMR.Tests
{
    /// <summary>
    /// Test de bout en bout sans casque : la scène tourne sans données de pièce (zone de secours),
    /// on joue une manche gagnée puis une manche perdue et on vérifie qu'aucune exception du jeu n'apparaît.
    /// </summary>
    public class HorrorSmokeTests
    {
        readonly List<string> m_GameErrors = new List<string>();

        void OnLog(string condition, string stackTrace, LogType type)
        {
            bool ours = (stackTrace != null && stackTrace.Contains("HorrorMR")) || condition.Contains("[Horreur]");
            if (ours && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert))
                m_GameErrors.Add($"{type}: {condition}\n{stackTrace}");
        }

        /// <summary>Capture de la vue du joueur si on passe -horrorShots dossier en ligne de commande.</summary>
        static void Capture(string name)
        {
            var args = Environment.GetCommandLineArgs();
            string dir = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-horrorShots")
                    dir = args[i + 1];
            }
            var cam = Camera.main;
            if (dir == null || cam == null)
                return;
            var rt = new RenderTexture(960, 640, 24, RenderTextureFormat.ARGB32);
            var previous = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = previous;
            rt.Release();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
        }

        static IEnumerator WaitUntil(Func<bool> condition, float timeout, string what)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > end)
                    Assert.Fail("Délai dépassé : " + what);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator FullRoundWithoutHeadset()
        {
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += OnLog;
            try
            {
                SceneManager.LoadScene("HorrorGame");
                yield return null;
                yield return null;
                var gm = UnityEngine.Object.FindAnyObjectByType<GameManager>();
                Assert.IsNotNull(gm, "GameManager absent de la scène");
                gm.SkipRoomScan();
                gm.LeftHand.followCameraWithoutController = false;
                gm.RightHand.followCameraWithoutController = false;

                yield return WaitUntil(() => gm.Phase == GamePhase.Menu, 40f, "menu");
                Assert.IsTrue(gm.Room.NavMeshReady, "NavMesh de secours non construit");
                Debug.Log($"[Test] Menu atteint, sol {gm.Room.FloorY:F2}, secours {gm.Room.UsedFallback}, surface {gm.Room.WalkableArea:F1} m²");
                yield return new WaitForSeconds(1f);
                Capture("1_menu");

                // --- Manche gagnée -----------------------------------------------------------------
                gm.survivalSeconds = 8f;
                gm.player.maxHealth = 100;
                gm.StartRound();
                yield return WaitUntil(() => gm.Phase == GamePhase.Survival, 15f, "début de la survie");
                Assert.IsNotNull(gm.Monster, "le monstre n'est pas apparu");
                var start = gm.Monster.transform.position;

                // Vue de contrôle : lampe dans l'axe du regard, peluche placée à 3 m devant.
                gm.LeftHand.followCameraWithoutController = true;
                var head = gm.player.head;
                var flat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                var front = new Vector3(head.position.x, gm.Room.FloorY, head.position.z) + flat * 3.2f;
                gm.Monster.GetComponent<UnityEngine.AI.NavMeshAgent>().Warp(front);
                gm.Monster.transform.rotation = Quaternion.LookRotation(-flat);
                yield return new WaitForSeconds(0.7f);
                Capture("2_survie");
                gm.OnPlayerHit(gm.Monster);
                gm.OnPlayerHit(gm.Monster);
                yield return new WaitForSeconds(0.25f);
                Capture("3_degats");
                gm.LeftHand.followCameraWithoutController = false;
                yield return new WaitForSeconds(4f);
                Debug.Log($"[Test] Monstre : état {gm.Monster.State}, déplacement {(gm.Monster.transform.position - start).magnitude:F2} m, vie joueur {gm.player.Health}");

                yield return WaitUntil(() => gm.Phase == GamePhase.GunHunt, 15f, "fin du minuteur");
                Assert.AreEqual(Gun.GunState.Available, gm.Gun.State, "pistolet non placé");
                yield return new WaitForSeconds(0.5f);
                Capture("4_arme");
                gm.Gun.Grab(gm.RightHand, Hand.Right);
                Assert.AreEqual(GamePhase.Armed, gm.Phase);

                for (int i = 0; i < 12 && !gm.Monster.IsDead; i++)
                {
                    var hand = gm.RightHand.transform;
                    hand.rotation = Quaternion.LookRotation(gm.Monster.ChestPosition - hand.position);
                    gm.Gun.Fire();
                    yield return new WaitForSeconds(0.3f);
                }
                Assert.IsTrue(gm.Monster.IsDead, "le monstre a survécu aux tirs");
                yield return WaitUntil(() => gm.Phase == GamePhase.Victory, 5f, "victoire");
                yield return new WaitForSeconds(3f);
                Capture("5_victoire");

                // --- Manche perdue (3 coups, jumpscare) -----------------------------------------------
                gm.player.maxHealth = 3;
                gm.StartRound();
                yield return WaitUntil(() => gm.Phase == GamePhase.Survival, 15f, "début de la 2e manche");
                for (int i = 0; i < 3; i++)
                {
                    gm.OnPlayerHit(gm.Monster);
                    yield return new WaitForSeconds(0.4f);
                }
                Assert.AreEqual(GamePhase.GameOver, gm.Phase, "le joueur devrait être mort");
                yield return new WaitForSeconds(0.2f);
                Capture("6_jumpscare");
                yield return new WaitForSeconds(3.3f);
                Capture("7_mort");
                Debug.Log("[Test] Manche perdue jouée jusqu'à l'écran de mort.");
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            Assert.IsEmpty(m_GameErrors, "Erreurs du jeu :\n" + string.Join("\n---\n", m_GameErrors));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace HorrorMR.Editor
{
    /// <summary>
    /// Rend une planche PNG des poses procédurales du monstre (vérification visuelle sans casque).
    /// Menu : Horreur MR > Aperçu des poses du monstre. En ligne de commande : -horrorPreviewOut chemin.png
    /// </summary>
    public static class HorrorPreview
    {
        const string k_Prefab = "Assets/HorrorMR/Prefabs/Monster_Alien3.prefab";
        const int k_Width = 320;
        const int k_Height = 420;

        struct Pose
        {
            public string name;
            public Action<PlushAnimator, float> setup;
            public float duration;
        }

        [MenuItem("Horreur MR/Aperçu des poses du monstre (PNG)", priority = 20)]
        public static void RenderPosesMenu()
        {
            var path = EditorUtility.SaveFilePanel("Planche des poses", "", "HorrorPoses.png", "png");
            if (!string.IsNullOrEmpty(path))
                RenderPoses(path);
        }

        public static void RenderPosesBatch()
        {
            var args = Environment.GetCommandLineArgs();
            string output = "HorrorPoses.png";
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-horrorPreviewOut")
                    output = args[i + 1];
            }
            RenderPoses(output);
        }

        static void RenderPoses(string output)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_Prefab);
            if (prefab == null)
                throw new Exception("Lance d'abord Horreur MR > Tout configurer.");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            var lightGo = new GameObject("Lumière");
            lightGo.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(6f, 0.02f, 6f);
            floor.transform.position = new Vector3(0f, -0.01f, 0f);

            var camGo = new GameObject("Caméra");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.18f, 0.18f, 0.2f);
            cam.fieldOfView = 30f;
            var rt = new RenderTexture(k_Width, k_Height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            var poses = new List<Pose>
            {
                new Pose { name = "repos", duration = 1.5f, setup = (p, t) => { } },
                new Pose { name = "marche", duration = 1.37f, setup = (p, t) => p.speed = 0.75f },
                new Pose { name = "accroupi", duration = 2.1f, setup = (p, t) => { p.speed = 0.5f; p.crouch = 1f; } },
                new Pose { name = "poursuite", duration = 2.05f, setup = (p, t) => { p.speed = 1.85f; p.reach = 1f; } },
                new Pose { name = "attaque armee", duration = 1.42f, setup = (p, t) => p.TriggerAttack(t + 1f) },
                new Pose { name = "attaque frappe", duration = 1.56f, setup = (p, t) => p.TriggerAttack(t + 1f) },
                new Pose { name = "cri", duration = 1.5f, setup = (p, t) => { p.TriggerSpot(t + 1.2f); p.jaw = 1f; } },
                new Pose { name = "mort", duration = 3f, setup = (p, t) => p.TriggerDeath(t + 0.5f) },
                new Pose { name = "jumpscare", duration = 1f, setup = (p, t) => p.jumpscare = true },
                new Pose { name = "regard", duration = 1.5f, setup = (p, t) => { p.hasLookTarget = true; p.lookTarget = new Vector3(2f, 2.5f, 1.5f); } },
            };

            var views = new[]
            {
                (position: new Vector3(1.9f, 1.4f, 4.2f), target: new Vector3(0f, 0.95f, 0f)),
                (position: new Vector3(4.6f, 1.2f, 0f), target: new Vector3(0f, 0.95f, 0f)),
            };

            var atlas = new Texture2D(k_Width * poses.Count, k_Height * views.Length, TextureFormat.RGB24, false);
            for (int i = 0; i < poses.Count; i++)
            {
                var monster = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                monster.transform.position = Vector3.zero;
                monster.transform.rotation = Quaternion.identity;
                var plush = monster.GetComponent<PlushAnimator>();
                plush.Initialize();
                plush.allowTwitch = false;
                const float baseTime = 100f;
                poses[i].setup(plush, baseTime);
                const float dt = 1f / 60f;
                for (float t = baseTime; t < baseTime + poses[i].duration; t += dt)
                    plush.Evaluate(t, dt);

                for (int v = 0; v < views.Length; v++)
                {
                    cam.transform.position = views[v].position;
                    cam.transform.LookAt(views[v].target);
                    cam.Render();
                    RenderTexture.active = rt;
                    atlas.ReadPixels(new Rect(0, 0, k_Width, k_Height), i * k_Width, (views.Length - 1 - v) * k_Height);
                    RenderTexture.active = null;
                }
                UnityEngine.Object.DestroyImmediate(monster);
            }
            atlas.Apply();
            File.WriteAllBytes(output, atlas.EncodeToPNG());
            Debug.Log("[Horreur] Planche des poses : " + output + " (" + string.Join(", ", poses.ConvertAll(p => p.name)) + ")");
            cam.targetTexture = null;
            rt.Release();
        }
    }
}

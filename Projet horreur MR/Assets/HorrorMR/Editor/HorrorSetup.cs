using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR;

namespace HorrorMR.Editor
{
    /// <summary>
    /// Construit tout le jeu à partir des scripts : réglages d'import, matériaux, prefabs
    /// (monstre Alien3, pistolet, lampe, surfaces AR), ressources et scène "HorrorGame".
    /// Menu : Horreur MR > Tout configurer. Peut être relancé sans risque (tout est régénéré).
    /// </summary>
    public static class HorrorSetup
    {
        const string k_Root = "Assets/HorrorMR";
        const string k_Mat = k_Root + "/Materials";
        const string k_Prefabs = k_Root + "/Prefabs";
        const string k_Scenes = k_Root + "/Scenes";
        const string k_Audio = k_Root + "/Audio";
        const string k_Tex = k_Root + "/Textures";
        const string k_ScenePath = k_Scenes + "/HorrorGame.unity";
        const string k_AlienPrefab = "Assets/Alien_Toys/Alien_Pack/Prefab/Built-In/Alien3.prefab";
        const string k_AlienMaterial = "Assets/Alien_Toys/Alien_Pack/Materials/Standart/Alien3_MAT.mat";
        const string k_MRSetupPrefab = "Assets/MRTemplateAssets/Prefabs/MR Interaction Setup.prefab";
        const string k_Font = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        const float k_MonsterHeight = 2.4f;

        [MenuItem("Horreur MR/Tout configurer (assets + scène)", priority = 0)]
        public static void SetupAll()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Horreur MR", "Réglages d'import…", 0.05f);
                EnsureFolders();
                ConfigureImports();

                EditorUtility.DisplayProgressBar("Horreur MR", "Matériaux…", 0.2f);
                var mats = CreateMaterials();

                EditorUtility.DisplayProgressBar("Horreur MR", "Prefabs…", 0.4f);
                var planePrefab = CreatePlanePrefab(mats.room);
                var boxPrefab = CreateBoundingBoxPrefab();
                var meshPrefab = CreateMeshPrefab(mats.room);
                var gunPrefab = CreateGunPrefab(mats);
                var flashlightPrefab = CreateFlashlightPrefab(mats);
                var monsterPrefab = CreateMonsterPrefab(mats.alien);

                EditorUtility.DisplayProgressBar("Horreur MR", "Ressources…", 0.6f);
                var assets = CreateAssets(mats, gunPrefab, flashlightPrefab, monsterPrefab);

                EditorUtility.DisplayProgressBar("Horreur MR", "Réglages du projet…", 0.7f);
                ConfigureProject();

                EditorUtility.DisplayProgressBar("Horreur MR", "Scène…", 0.85f);
                CreateScene(assets, mats, planePrefab, boxPrefab, meshPrefab, flashlightPrefab);

                AssetDatabase.SaveAssets();
                Debug.Log("[Horreur] Configuration terminée. Scène : " + k_ScenePath);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Point d'entrée pour Unity en ligne de commande (-batchmode -executeMethod).</summary>
        public static void SetupAllBatch()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            SetupAll();
        }

        static void EnsureFolders()
        {
            foreach (var folder in new[] { k_Mat, k_Prefabs, k_Scenes, k_Audio, k_Tex })
            {
                if (AssetDatabase.IsValidFolder(folder))
                    continue;
                var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }
        }

        // ------------------------------------------------------------------------------------------
        // Import
        // ------------------------------------------------------------------------------------------

        static void ConfigureImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { k_Audio }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer))
                    continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                bool longClip = clip != null && clip.length > 8f;
                importer.forceToMono = true;
                importer.loadInBackground = longClip;
                var s = importer.defaultSampleSettings;
                s.loadType = longClip ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                importer.defaultSampleSettings = s;
                importer.SaveAndReimport();
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { k_Tex }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                    continue;
                bool fx = Path.GetFileName(path).StartsWith("fx_");
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.alphaIsTransparency = fx;
                importer.wrapMode = fx ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.maxTextureSize = fx ? 512 : 2048;
                importer.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------------------------------
        // Matériaux
        // ------------------------------------------------------------------------------------------

        class Mats
        {
            public Material alien, room, dome, overlay;
            public Material blood, stuffing, spark, muzzle, bloodPool, beacon, laser, arrow;
            public Material gunMetal, gunGrip, flashBody, flashLens;
        }

        static Material GetOrCreateMaterial(string name, Shader shader)
        {
            var path = $"{k_Mat}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        static Shader FindShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null)
                throw new Exception($"Shader introuvable : {name}. Les shaders HorrorMR ont-ils été importés ?");
            return shader;
        }

        static Texture2D Tex(string file) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{k_Tex}/{file}.png");

        static Material ParticleMaterial(string name, Texture2D texture, Color color, bool additive, bool lit, float emission = 0f,
            bool verticalFade = false, float pulse = 0f, bool alwaysOnTop = false)
        {
            var mat = GetOrCreateMaterial(name, FindShader("HorrorMR/Particle"));
            mat.SetTexture("_MainTex", texture);
            mat.SetColor("_Color", color);
            mat.SetFloat("_Emission", emission);
            mat.SetFloat("_Lit", lit ? 1f : 0f);
            mat.SetFloat("_VerticalFade", verticalFade ? 1f : 0f);
            mat.SetFloat("_PulseSpeed", pulse);
            mat.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendA", (float)BlendMode.One);
            mat.SetFloat("_DstBlendA", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZTest", alwaysOnTop ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual);
            mat.renderQueue = alwaysOnTop ? 4150 : 3000;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LitMaterial(string name, Color color, float metallic, float smoothness)
        {
            var mat = GetOrCreateMaterial(name, FindShader("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Mats CreateMaterials()
        {
            var m = new Mats();

            // Peluche salie et ensanglantée : on part du matériau d'origine (normal map, etc.).
            var source = AssetDatabase.LoadAssetAtPath<Material>(k_AlienMaterial);
            var alienPath = $"{k_Mat}/Alien3_Horror.mat";
            m.alien = AssetDatabase.LoadAssetAtPath<Material>(alienPath);
            if (m.alien == null)
            {
                m.alien = source != null ? new Material(source) : new Material(FindShader("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m.alien, alienPath);
            }
            else if (source != null)
            {
                m.alien.CopyPropertiesFromMaterial(source);
                m.alien.shader = source.shader;
            }
            var albedo = Tex("Alien3_Horror_Albedo");
            if (albedo != null)
            {
                if (m.alien.HasProperty("_BaseMap")) m.alien.SetTexture("_BaseMap", albedo);
                if (m.alien.HasProperty("_MainTex")) m.alien.SetTexture("_MainTex", albedo);
            }
            if (m.alien.HasProperty("_BaseColor")) m.alien.SetColor("_BaseColor", new Color(0.95f, 0.92f, 0.9f));
            if (m.alien.HasProperty("_Smoothness")) m.alien.SetFloat("_Smoothness", 0.12f);
            EditorUtility.SetDirty(m.alien);

            m.room = GetOrCreateMaterial("RoomDarkness", FindShader("HorrorMR/RoomDarkness"));
            m.dome = GetOrCreateMaterial("DarknessDome", FindShader("HorrorMR/DarknessDome"));
            m.overlay = GetOrCreateMaterial("ScreenOverlay", FindShader("HorrorMR/ScreenOverlay"));

            m.blood = ParticleMaterial("FX_Blood", Tex("fx_blood_drop"), Color.white, false, true, 0.2f);
            m.stuffing = ParticleMaterial("FX_Stuffing", Tex("fx_stuffing"), Color.white, false, true);
            m.spark = ParticleMaterial("FX_Spark", Tex("fx_soft"), Color.white, true, false, 1f);
            m.muzzle = ParticleMaterial("FX_Muzzle", Tex("fx_muzzle"), new Color(1f, 0.9f, 0.7f, 1f), true, false, 1.5f);
            m.bloodPool = ParticleMaterial("FX_BloodPool", Tex("fx_blood_pool"), Color.white, false, true, 0.3f);
            m.beacon = ParticleMaterial("FX_Beacon", null, new Color(1f, 0.8f, 0.35f, 0.55f), true, false, 0.5f, true, 3f);
            m.laser = ParticleMaterial("FX_Laser", null, new Color(1f, 0.1f, 0.05f, 0.6f), true, false, 0.5f);
            m.arrow = ParticleMaterial("FX_Arrow", null, new Color(1f, 0.35f, 0.1f, 0.9f), true, false, 0.8f, false, 0f, true);

            m.gunMetal = LitMaterial("Gun_Metal", new Color(0.13f, 0.13f, 0.14f), 0.85f, 0.55f);
            m.gunGrip = LitMaterial("Gun_Grip", new Color(0.05f, 0.045f, 0.04f), 0f, 0.3f);
            m.flashBody = LitMaterial("Flashlight_Body", new Color(0.16f, 0.16f, 0.18f), 0.6f, 0.45f);
            m.flashLens = GetOrCreateMaterial("Flashlight_Lens", FindShader("Universal Render Pipeline/Unlit"));
            m.flashLens.SetColor("_BaseColor", new Color(1f, 0.92f, 0.75f));
            EditorUtility.SetDirty(m.flashLens);
            return m;
        }

        // ------------------------------------------------------------------------------------------
        // Prefabs
        // ------------------------------------------------------------------------------------------

        static GameObject SavePrefab(GameObject go, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{k_Prefabs}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject CreatePlanePrefab(Material room)
        {
            var go = new GameObject("HorrorARPlane");
            go.AddComponent<ARPlane>();
            go.AddComponent<MeshFilter>();
            go.AddComponent<ARPlaneMeshVisualizer>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = room;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<MeshCollider>();
            return SavePrefab(go, "HorrorARPlane");
        }

        static GameObject CreateBoundingBoxPrefab()
        {
            var go = new GameObject("HorrorARBoundingBox");
            go.AddComponent<ARBoundingBox>();
            go.AddComponent<BoxCollider>();
            go.AddComponent<BoundingBoxCollider>();
            return SavePrefab(go, "HorrorARBoundingBox");
        }

        static GameObject CreateMeshPrefab(Material room)
        {
            var go = new GameObject("HorrorARMesh");
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = room;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            go.AddComponent<MeshCollider>();
            return SavePrefab(go, "HorrorARMesh");
        }

        static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static GameObject CreateGunPrefab(Mats m)
        {
            var root = new GameObject("HorrorGun");
            root.AddComponent<HorrorOwned>();
            var gun = root.AddComponent<Gun>();
            var model = new GameObject("Modele").transform;
            model.SetParent(root.transform, false);

            Part(PrimitiveType.Cube, "Culasse", model, new Vector3(0f, 0.03f, 0.05f), new Vector3(0.03f, 0.034f, 0.19f), m.gunMetal);
            Part(PrimitiveType.Cylinder, "Canon", model, new Vector3(0f, 0.032f, 0.15f), new Vector3(0.02f, 0.012f, 0.02f), m.gunMetal, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cube, "Carcasse", model, new Vector3(0f, 0.006f, 0.06f), new Vector3(0.026f, 0.018f, 0.15f), m.gunGrip);
            Part(PrimitiveType.Cube, "Crosse", model, new Vector3(0f, -0.035f, -0.02f), new Vector3(0.028f, 0.1f, 0.045f), m.gunGrip, new Vector3(-16f, 0f, 0f));
            Part(PrimitiveType.Cube, "Pontet", model, new Vector3(0f, -0.022f, 0.03f), new Vector3(0.008f, 0.006f, 0.05f), m.gunGrip);
            Part(PrimitiveType.Cube, "Detente", model, new Vector3(0f, -0.01f, 0.022f), new Vector3(0.006f, 0.02f, 0.006f), m.gunMetal, new Vector3(18f, 0f, 0f));
            Part(PrimitiveType.Cube, "Guidon", model, new Vector3(0f, 0.05f, 0.135f), new Vector3(0.004f, 0.008f, 0.006f), m.gunMetal);
            Part(PrimitiveType.Cube, "Hausse", model, new Vector3(0f, 0.05f, -0.035f), new Vector3(0.018f, 0.008f, 0.006f), m.gunMetal);

            var muzzle = new GameObject("Bouche du canon").transform;
            muzzle.SetParent(model, false);
            muzzle.localPosition = new Vector3(0f, 0.032f, 0.165f);

            var laserGo = new GameObject("Laser");
            laserGo.transform.SetParent(model, false);
            var laser = laserGo.AddComponent<LineRenderer>();
            laser.sharedMaterial = m.laser;
            laser.useWorldSpace = true;
            laser.positionCount = 2;
            laser.startWidth = 0.0025f;
            laser.endWidth = 0.0015f;
            laser.shadowCastingMode = ShadowCastingMode.Off;
            laser.enabled = false;

            var beacon = Part(PrimitiveType.Cylinder, "Faisceau repere", root.transform, new Vector3(0f, 1.45f, 0f), new Vector3(0.22f, 1.45f, 0.22f), m.beacon);

            var glowGo = new GameObject("Lueur");
            glowGo.transform.SetParent(root.transform, false);
            glowGo.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 1.2f;
            glow.intensity = 1.5f;
            glow.color = new Color(1f, 0.85f, 0.5f);
            glow.shadows = LightShadows.None;

            gun.model = model;
            gun.muzzle = muzzle;
            gun.laser = laser;
            gun.beacon = beacon;
            gun.glow = glow;
            return SavePrefab(root, "HorrorGun");
        }

        static GameObject CreateFlashlightPrefab(Mats m)
        {
            var root = new GameObject("HorrorFlashlight");
            root.AddComponent<HorrorOwned>();
            var flashlight = root.AddComponent<Flashlight>();
            Part(PrimitiveType.Cylinder, "Corps", root.transform, new Vector3(0f, 0f, -0.04f), new Vector3(0.032f, 0.07f, 0.032f), m.flashBody, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cylinder, "Tete", root.transform, new Vector3(0f, 0f, 0.04f), new Vector3(0.048f, 0.018f, 0.048f), m.flashBody, new Vector3(90f, 0f, 0f));
            var lens = Part(PrimitiveType.Cylinder, "Verre", root.transform, new Vector3(0f, 0f, 0.059f), new Vector3(0.04f, 0.002f, 0.04f), m.flashLens, new Vector3(90f, 0f, 0f));
            var spotGo = new GameObject("Faisceau");
            spotGo.transform.SetParent(root.transform, false);
            spotGo.transform.localPosition = new Vector3(0f, 0f, 0.065f);
            var spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.shadows = LightShadows.None;
            flashlight.spot = spot;
            flashlight.lens = lens.GetComponent<MeshRenderer>();
            return SavePrefab(root, "HorrorFlashlight");
        }

        static GameObject CreateMonsterPrefab(Material alienMaterial)
        {
            var alienSource = AssetDatabase.LoadAssetAtPath<GameObject>(k_AlienPrefab);
            if (alienSource == null)
                throw new Exception("Prefab Alien3 introuvable : " + k_AlienPrefab);

            var root = new GameObject("Monster_Alien3");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(alienSource);
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            foreach (var animator in visual.GetComponentsInChildren<Animator>(true))
                UnityEngine.Object.DestroyImmediate(animator);

            var smr = visual.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr == null)
                throw new Exception("Alien3 : SkinnedMeshRenderer introuvable.");
            smr.sharedMaterial = alienMaterial;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = ShadowCastingMode.Off;

            // Mesure du modèle (dans son propre repère, échelle 1) pour le mettre à 1,95 m.
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var toVisual = visual.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            var vertices = baked.vertices.Select(v => toVisual.MultiplyPoint3x4(v)).ToArray();
            float minY = vertices.Min(v => v.y);
            float maxY = vertices.Max(v => v.y);
            float height = Mathf.Max(maxY - minY, 0.01f);
            float scale = k_MonsterHeight / height;

            Transform Bone(string n) => visual.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
            var head = Bone("head");
            var spine = Bone("spine_02");
            var pelvis = Bone("pelvis");
            var neck = Bone("neck_01");
            var thigh = Bone("thigh_l");
            float neckY = neck != null ? visual.transform.InverseTransformPoint(neck.position).y : minY + height * 0.6f;
            float hipY = thigh != null ? visual.transform.InverseTransformPoint(thigh.position).y : minY + height * 0.33f;

            Bounds Region(Func<Vector3, bool> filter)
            {
                var pts = vertices.Where(filter).ToArray();
                if (pts.Length == 0)
                    return new Bounds(Vector3.zero, Vector3.one * 0.1f);
                var b = new Bounds(pts[0], Vector3.zero);
                foreach (var p in pts) b.Encapsulate(p);
                return b;
            }
            var headBounds = Region(v => v.y > neckY);
            var torsoBounds = Region(v => v.y > hipY && v.y <= neckY && Mathf.Abs(v.x) < 0.13f);
            var legBounds = Region(v => v.y <= hipY);

            visual.transform.localScale = Vector3.one * scale;
            visual.transform.localPosition = new Vector3(0f, -minY * scale, 0f);
            Debug.Log($"[Horreur] Alien3 : hauteur d'origine {height:F3} m → échelle {scale:F2} (≈ {k_MonsterHeight} m). " +
                      $"Tête {headBounds.size}, torse {torsoBounds.size}, jambes {legBounds.size}.");

            Vector3 ToWorld(Vector3 p) => visual.transform.TransformPoint(p);

            void AddSphere(Transform bone, string n, Bounds b, float factor)
            {
                if (bone == null) return;
                var go = new GameObject(n);
                go.transform.SetParent(bone, false);
                go.transform.position = ToWorld(b.center);
                var col = go.AddComponent<SphereCollider>();
                float worldRadius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z) * scale * factor;
                col.radius = worldRadius / Mathf.Max(go.transform.lossyScale.x, 0.0001f);
            }

            void AddBox(Transform bone, string n, Bounds b, float factor)
            {
                if (bone == null) return;
                var go = new GameObject(n);
                go.transform.SetParent(bone, false);
                go.transform.position = ToWorld(b.center);
                go.transform.rotation = root.transform.rotation;
                var col = go.AddComponent<BoxCollider>();
                col.size = b.size * factor;
                // La boîte est alignée sur le personnage : on compense l'échelle héritée.
                var ls = go.transform.lossyScale;
                col.size = new Vector3(col.size.x * scale / ls.x, col.size.y * scale / ls.y, col.size.z * scale / ls.z);
            }

            AddSphere(head, "Hitbox Tete", headBounds, 0.85f);
            AddBox(spine, "Hitbox Torse", torsoBounds, 0.95f);
            AddBox(pelvis, "Hitbox Jambes", legBounds, 0.9f);

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.25f;
            agent.height = 2.3f;
            agent.baseOffset = 0f;
            agent.speed = 0.75f;
            agent.angularSpeed = 220f;
            agent.acceleration = 8f;
            agent.stoppingDistance = 0.3f;

            var plush = root.AddComponent<PlushAnimator>();
            plush.modelRoot = visual.transform;

            // Position de l'œil peint : le sommet dont l'UV est au centre de l'œil de la texture (973, 572 sur 2048).
            var uvs = baked.uv;
            if (head != null && uvs != null && uvs.Length == vertices.Length)
            {
                var eyeUv = new Vector2(973f / 2048f, 1f - 572f / 2048f);
                int best = 0;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < uvs.Length; i++)
                {
                    float d = (uvs[i] - eyeUv).sqrMagnitude;
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }
                // Les deux points sont dans le repère local du modèle (unités d'origine, l'échelle est déjà compensée).
                var eyeModel = vertices[best];
                plush.faceOffset = eyeModel - visual.transform.InverseTransformPoint(head.position);
                Debug.Log($"[Horreur] Œil peint trouvé en {eyeModel} (écart UV {Mathf.Sqrt(bestDistance):F4}), décalage depuis l'os de la tête {plush.faceOffset}.");
            }
            var ai = root.AddComponent<MonsterAI>();
            ai.plush = plush;
            ai.voice = CreateSource(root.transform, "Voix", 1.6f, 1.5f, 25f);
            ai.loop = CreateSource(root.transform, "Boite a musique", 1.1f, 1.2f, 18f);
            ai.steps = CreateSource(root.transform, "Pas", 0.05f, 1f, 18f);
            return SavePrefab(root, "Monster_Alien3");
        }

        static AudioSource CreateSource(Transform parent, string name, float height, float min, float max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            var source = go.AddComponent<AudioSource>();
            HorrorAudio.Configure3D(source, min, max);
            return source;
        }

        // ------------------------------------------------------------------------------------------
        // Ressources partagées
        // ------------------------------------------------------------------------------------------

        static AudioClip Clip(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{k_Audio}/{name}.wav");
            if (clip == null)
                Debug.LogWarning("[Horreur] Son manquant : " + name);
            return clip;
        }

        static HorrorAssets CreateAssets(Mats m, GameObject gun, GameObject flashlight, GameObject monster)
        {
            var path = $"{k_Root}/HorrorAssets.asset";
            var a = AssetDatabase.LoadAssetAtPath<HorrorAssets>(path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<HorrorAssets>();
                AssetDatabase.CreateAsset(a, path);
            }

            a.monsterPrefab = monster;
            a.gunPrefab = gun;
            a.flashlightPrefab = flashlight;

            a.ambience = Clip("ambience");
            a.heartbeat = Clip("heartbeat");
            a.startSting = Clip("start_sting");
            a.alarm = Clip("alarm");
            a.tick = Clip("tick");
            a.victory = Clip("victory");
            a.spottedSting = Clip("spotted_sting");

            a.musicBox = Clip("musicbox");
            a.growl = Clip("growl");
            a.whisper = Clip("whisper");
            a.spottedScream = Clip("spotted_scream");
            a.jumpscareScream = Clip("jumpscare_scream");
            a.attackSwoosh = Clip("attack_swoosh");
            a.monsterHurt = Clip("monster_hurt");
            a.monsterSqueak = Clip("monster_squeak");
            a.monsterDeath = Clip("monster_death");
            a.fabricRip = Clip("fabric_rip");
            a.footsteps = new[] { Clip("footstep_0"), Clip("footstep_1"), Clip("footstep_2") };

            a.playerHit = Clip("player_hit");
            a.gunshot = Clip("gunshot");
            a.gunPickup = Clip("gun_pickup");
            a.gunBeacon = Clip("gun_beacon");
            a.flashlightClick = Clip("flashlight_click");
            a.bulletImpact = Clip("bullet_impact");

            a.bloodParticle = m.blood;
            a.stuffingParticle = m.stuffing;
            a.sparkParticle = m.spark;
            a.muzzleFlash = m.muzzle;
            a.bloodPool = m.bloodPool;
            a.beacon = m.beacon;
            a.laser = m.laser;
            a.arrow = m.arrow;
            a.roomDarkness = m.room;
            a.darknessDome = m.dome;
            a.screenOverlay = m.overlay;
            a.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(k_Font);
            EditorUtility.SetDirty(a);
            return a;
        }

        // ------------------------------------------------------------------------------------------
        // Réglages du projet
        // ------------------------------------------------------------------------------------------

        static void ConfigureProject()
        {
            // Maillage de la pièce (Quest 3) : désactivé par défaut dans le template.
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (settings == null)
                    continue;
                foreach (var feature in settings.GetFeatures())
                {
                    if (feature != null && feature.GetType().Name == "ARMeshFeature" && !feature.enabled)
                    {
                        feature.enabled = true;
                        EditorUtility.SetDirty(feature);
                        Debug.Log($"[Horreur] OpenXR : 'Meta Quest: Meshing' activé pour {group}.");
                    }
                    // Guardian masqué dès le démarrage (autorisé par Meta pour les applis en passthrough).
                    if (feature != null && feature.GetType().Name == "BoundaryVisibilityFeature")
                    {
                        feature.enabled = true;
                        var so = new SerializedObject(feature);
                        var suppress = so.FindProperty("m_SuppressVisibility");
                        if (suppress != null)
                        {
                            suppress.boolValue = true;
                            so.ApplyModifiedPropertiesWithoutUndo();
                        }
                        EditorUtility.SetDirty(feature);
                    }
                }
                EditorUtility.SetDirty(settings);
            }

            // La lampe torche est une lumière additionnelle : le preset "Performant" (Quest) les désactivait.
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/"))
                    continue;
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                var so = new SerializedObject(asset);
                var mode = so.FindProperty("m_AdditionalLightsRenderingMode");
                var limit = so.FindProperty("m_AdditionalLightsPerObjectLimit");
                if (mode != null)
                    mode.intValue = 1; // Per Pixel
                if (limit != null && limit.intValue < 4)
                    limit.intValue = 4;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            PlayerSettings.productName = "Prêt ou pas, ils arrivent";
        }

        // ------------------------------------------------------------------------------------------
        // Scène
        // ------------------------------------------------------------------------------------------

        static readonly string[] k_TemplateComponentsToRemove =
        {
            "GoalManager", "ObjectSpawner", "SpawnedObjectsManager", "ARFeatureController", "OcclusionManager",
            "ContactSpawnTrigger", "ARFaceManager", "ARDebugMenu"
        };

        static void CreateScene(HorrorAssets assets, Mats m, GameObject planePrefab, GameObject boxPrefab, GameObject meshPrefab, GameObject flashlightPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var setupSource = AssetDatabase.LoadAssetAtPath<GameObject>(k_MRSetupPrefab);
            if (setupSource == null)
                throw new Exception("Prefab du template introuvable : " + k_MRSetupPrefab);
            var setup = (GameObject)PrefabUtility.InstantiatePrefab(setupSource, scene);
            PrefabUtility.UnpackPrefabInstance(setup, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            setup.name = "MR Interaction Setup";

            // On retire le tutoriel et les gadgets du template.
            var toRemove = setup.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(c => c != null && k_TemplateComponentsToRemove.Contains(c.GetType().Name)).ToList();
            foreach (var component in toRemove)
            {
                if (component == null)
                    continue;
                var go = component.gameObject;
                bool onlyThis = go.GetComponents<Component>().Length <= 2 && go.transform.childCount == 0;
                bool spawnerObject = component.GetType().Name == "ContactSpawnTrigger" || component.GetType().Name == "ObjectSpawner" || component.GetType().Name == "GoalManager";
                if ((onlyThis || spawnerObject) && go != setup && go.GetComponent<XROrigin>() == null && go.GetComponent<ARSession>() == null)
                    UnityEngine.Object.DestroyImmediate(go);
                else
                    UnityEngine.Object.DestroyImmediate(component);
            }

            var origin = setup.GetComponentInChildren<XROrigin>(true);
            if (origin == null)
                throw new Exception("XR Origin introuvable dans le prefab du template.");
            var session = setup.GetComponentInChildren<ARSession>(true);
            var planes = origin.GetComponentInChildren<ARPlaneManager>(true) ?? setup.GetComponentInChildren<ARPlaneManager>(true);
            var boxes = origin.GetComponentInChildren<ARBoundingBoxManager>(true) ?? setup.GetComponentInChildren<ARBoundingBoxManager>(true);

            if (planes != null)
            {
                planes.planePrefab = planePrefab;
                planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
                planes.enabled = true;
            }
            if (boxes != null)
            {
                boxes.boundingBoxPrefab = boxPrefab;
                boxes.enabled = true;
            }

            foreach (var occlusion in setup.GetComponentsInChildren<AROcclusionManager>(true))
                occlusion.enabled = false;
            foreach (var behaviour in setup.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Name == "ARShaderOcclusion")
                    behaviour.enabled = false;
            }

            var meshGo = new GameObject("AR Mesh Manager");
            meshGo.transform.SetParent(origin.transform, false);
            var meshManager = meshGo.AddComponent<ARMeshManager>();
            meshManager.meshPrefab = meshPrefab.GetComponent<MeshFilter>();
            meshManager.normals = false;
            meshManager.density = 1f;

            // Mains du jeu (pose de visée des manettes) + lampe torche dans la main gauche.
            var offset = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : origin.transform;
            var left = CreateHand(offset, "Main gauche (jeu)", Hand.Left);
            var right = CreateHand(offset, "Main droite (jeu)", Hand.Right);
            var flashlightGo = (GameObject)PrefabUtility.InstantiatePrefab(flashlightPrefab, scene);
            flashlightGo.transform.SetParent(left.transform, false);
            flashlightGo.transform.localPosition = new Vector3(0f, -0.015f, -0.03f);
            flashlightGo.transform.localRotation = Quaternion.identity;

            var cam = origin.Camera != null ? origin.Camera : setup.GetComponentInChildren<Camera>(true);
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 120f);
            var fxGo = new GameObject("Ecran FX");
            fxGo.AddComponent<HorrorOwned>();
            fxGo.transform.SetParent(cam.transform, false);
            var screenFX = fxGo.AddComponent<ScreenFX>();
            screenFX.overlayMaterial = m.overlay;

            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Dome d'obscurite";
            UnityEngine.Object.DestroyImmediate(dome.GetComponent<Collider>());
            dome.AddComponent<HorrorOwned>();
            dome.transform.localScale = Vector3.one * 100f;
            var domeRenderer = dome.GetComponent<MeshRenderer>();
            domeRenderer.sharedMaterial = m.dome;
            domeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            domeRenderer.receiveShadows = false;

            var moonGo = new GameObject("Lune");
            moonGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.75f, 0.8f, 1f);
            moon.intensity = 1f;
            moon.shadows = LightShadows.None;

            var game = new GameObject("Jeu");
            var player = game.AddComponent<PlayerState>();
            player.head = cam.transform;
            var room = game.AddComponent<RoomModel>();
            room.planeManager = planes;
            room.boundingBoxManager = boxes;
            room.meshManager = meshManager;
            var darkness = game.AddComponent<DarknessController>();
            darkness.head = cam.transform;
            darkness.dome = dome.transform;
            darkness.moonLight = moon;
            darkness.flashlight = flashlightGo.GetComponent<Flashlight>();
            var horrorAudio = game.AddComponent<HorrorAudio>();

            var hudGo = new GameObject("HUD");
            var hud = hudGo.AddComponent<HUD>();
            hud.head = cam.transform;
            hud.arrowMaterial = m.arrow;
            hud.font = assets.font;

            var manualGo = new GameObject("Piece manuelle");
            var manualRoom = manualGo.AddComponent<ManualRoom>();
            manualRoom.darknessMaterial = m.room;
            manualRoom.outlineMaterial = m.arrow;
            var calibrator = game.AddComponent<RoomCalibrator>();
            var anchors = setup.GetComponentInChildren<ARAnchorManager>(true);
            if (anchors != null)
                anchors.enabled = true;

            var manager = game.AddComponent<GameManager>();
            manager.manualRoom = manualRoom;
            manager.calibrator = calibrator;
            manager.anchorManager = anchors;
            manager.assets = assets;
            manager.xrOrigin = origin;
            manager.arSession = session;
            manager.room = room;
            manager.player = player;
            manager.leftHand = left;
            manager.rightHand = right;
            manager.flashlight = flashlightGo.GetComponent<Flashlight>();
            manager.darkness = darkness;
            manager.screenFX = screenFX;
            manager.hud = hud;
            manager.horrorAudio = horrorAudio;

            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

            EditorSceneManager.SaveScene(scene, k_ScenePath);

            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(k_ScenePath, true) };
            scenes.AddRange(EditorBuildSettings.scenes.Where(s => s.path != k_ScenePath).Select(s => new EditorBuildSettingsScene(s.path, false)));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"[Horreur] Scène créée : plans {(planes != null ? "ok" : "ABSENT")}, meubles {(boxes != null ? "ok" : "ABSENT")}, " +
                      $"session {(session != null ? "ok" : "ABSENT")}, ancres {(anchors != null ? "ok" : "ABSENT")}, caméra {cam.name}.");
        }

        static HandPose CreateHand(Transform parent, string name, Hand hand)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<HorrorOwned>();
            var pose = go.AddComponent<HandPose>();
            pose.hand = hand;
            return pose;
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace HorrorMR
{
    /// <summary>
    /// Modèle de la pièce réelle : sol, meubles, murs et maillage scannés par le Quest.
    /// Construit le NavMesh sur lequel marche le monstre et trouve les points d'apparition.
    /// </summary>
    public class RoomModel : MonoBehaviour
    {
        public ARPlaneManager planeManager;
        public ARBoundingBoxManager boundingBoxManager;
        public ARMeshManager meshManager;
        [Tooltip("Pièce scannée à la main : prioritaire sur les données du Quest quand elle existe.")]
        public ManualRoom manualRoom;

        public bool UsingManualRoom => manualRoom != null && manualRoom.HasRoom;

        [Header("NavMesh (taille du monstre)")]
        public float agentRadius = 0.25f;
        public float agentHeight = 1.8f;
        public float agentClimb = 0.18f;
        public float voxelSize = 0.07f;

        [Header("Repli sans données de pièce")]
        public Vector2 fallbackRoomSize = new Vector2(10f, 10f);
        [Tooltip("En dessous de cette surface marchable (m²), on ajoute la dalle de secours.")]
        public float minWalkableArea = 4f;

        public float FloorY { get; private set; }
        public Bounds RoomBounds { get; private set; }
        public bool NavMeshReady { get; private set; }
        public bool UsedFallback { get; private set; }
        public float WalkableArea => m_FloorTotalArea;

        public int PlaneCount => planeManager != null ? planeManager.trackables.count : 0;
        public int BoxCount => boundingBoxManager != null ? boundingBoxManager.trackables.count : 0;
        public int MeshCount => meshManager != null && meshManager.meshes != null ? meshManager.meshes.Count : 0;
        public bool HasFloorPlane => FindFloorPlane() != null;
        public bool HasRoomData => MeshCount > 0 || HasFloorPlane;

        NavMeshData m_NavData;
        NavMeshDataInstance m_NavInstance;
        readonly List<Vector3> m_FloorTris = new List<Vector3>();
        readonly List<float> m_FloorTriCumulative = new List<float>();
        float m_FloorTotalArea;
        float m_FallbackFloorY;

        public void SetFallbackFloor(float y) => m_FallbackFloorY = y;

        ARPlane FindFloorPlane()
        {
            if (planeManager == null)
                return null;
            ARPlane best = null;
            foreach (var plane in planeManager.trackables)
            {
                if ((plane.classifications & PlaneClassifications.Floor) == 0)
                    continue;
                if (best == null || plane.size.x * plane.size.y > best.size.x * best.size.y)
                    best = plane;
            }
            return best;
        }

        static bool IsHorizontal(ARPlane plane) =>
            plane.alignment == PlaneAlignment.HorizontalUp || plane.alignment == PlaneAlignment.HorizontalDown;

        static bool IsFurnitureSurface(ARPlane plane) =>
            (plane.classifications & (PlaneClassifications.Table | PlaneClassifications.Couch | PlaneClassifications.Seat)) != 0;

        static bool IsWall(ARPlane plane) =>
            (plane.classifications & (PlaneClassifications.WallFace | PlaneClassifications.InvisibleWallFace |
                                      PlaneClassifications.InnerWallFace | PlaneClassifications.DoorFrame |
                                      PlaneClassifications.WindowFrame | PlaneClassifications.WallArt)) != 0 ||
            plane.alignment == PlaneAlignment.Vertical;

        /// <summary>Quand le maillage de la pièce existe, il sert de surface sombre : on masque les plans pour éviter un double assombrissement.</summary>
        public void RefreshSurfaceRendering()
        {
            if (planeManager == null)
                return;
            bool meshAvailable = MeshCount > 0;
            foreach (var plane in planeManager.trackables)
            {
                // Désactiver le visualiseur masque aussi son rendu (et fige son collider, ce qui suffit ici).
                if (plane.TryGetComponent<ARPlaneMeshVisualizer>(out var visualizer) && visualizer.enabled == meshAvailable)
                    visualizer.enabled = !meshAvailable;
            }
        }

        Bounds m_CollectBounds;
        bool m_CollectHasBounds;

        void Encapsulate(Bounds b)
        {
            if (!m_CollectHasBounds) { m_CollectBounds = b; m_CollectHasBounds = true; }
            else m_CollectBounds.Encapsulate(b);
        }

        List<NavMeshBuildSource> CollectSources(Vector3 playerHead, bool addFallbackSlab, out Bounds bounds, out ARPlane floorPlane, out float floorY)
        {
            var sources = new List<NavMeshBuildSource>();
            m_CollectHasBounds = false;
            m_CollectBounds = new Bounds();

            // 0. Pièce scannée à la main : elle remplace tout le reste.
            if (UsingManualRoom)
            {
                floorPlane = null;
                floorY = manualRoom.FloorY;
                UsedFallback = false;
                manualRoom.CollectNavSources(sources);
                Encapsulate(manualRoom.WorldBounds);
                m_CollectBounds.Expand(new Vector3(1f, 1f, 1f));
                bounds = m_CollectBounds;
                return sources;
            }

            // 1. Maillage complet de la pièce (Quest 3).
            if (meshManager != null && meshManager.meshes != null)
            {
                foreach (var mf in meshManager.meshes)
                {
                    if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < 3)
                        continue;
                    sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Mesh,
                        sourceObject = mf.sharedMesh,
                        transform = mf.transform.localToWorldMatrix,
                        area = 0
                    });
                    if (mf.TryGetComponent<MeshRenderer>(out var mr))
                        Encapsulate(mr.bounds);
                }
            }

            // 2. Plans détectés : sol marchable, meubles et murs bloquants.
            floorPlane = FindFloorPlane();
            floorY = floorPlane != null ? floorPlane.transform.position.y : m_FallbackFloorY;
            if (planeManager != null)
            {
                foreach (var plane in planeManager.trackables)
                {
                    var planeMatrix = plane.transform.localToWorldMatrix *
                                      Matrix4x4.Translate(new Vector3(plane.centerInPlaneSpace.x, 0f, plane.centerInPlaneSpace.y));
                    var planeSize = new Vector3(Mathf.Max(plane.size.x, 0.05f), 0.04f, Mathf.Max(plane.size.y, 0.05f));
                    var worldBounds = new Bounds(plane.center, plane.transform.rotation * planeSize);
                    worldBounds.extents = new Vector3(Mathf.Abs(worldBounds.extents.x), Mathf.Abs(worldBounds.extents.y), Mathf.Abs(worldBounds.extents.z));

                    if ((plane.classifications & PlaneClassifications.Floor) != 0)
                    {
                        var mesh = plane.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                        if (mesh != null && mesh.vertexCount >= 3)
                            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = mesh, transform = plane.transform.localToWorldMatrix, area = 0 });
                        else
                            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = planeSize, transform = planeMatrix, area = 0 });
                        Encapsulate(worldBounds);
                    }
                    else if (IsHorizontal(plane) && IsFurnitureSurface(plane))
                    {
                        // Un meuble = un bloc plein du sol jusqu'à sa surface.
                        float height = Mathf.Max(plane.center.y - floorY, 0.1f);
                        var blockSize = new Vector3(planeSize.x, height, planeSize.z);
                        var blockMatrix = Matrix4x4.TRS(new Vector3(plane.center.x, floorY + height * 0.5f, plane.center.z),
                            Quaternion.Euler(0f, plane.transform.eulerAngles.y, 0f), Vector3.one);
                        sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = blockSize, transform = blockMatrix, area = 1 });
                    }
                    else if (IsWall(plane) && !IsHorizontal(plane))
                    {
                        var wallSize = new Vector3(planeSize.x, 0.15f, planeSize.z);
                        sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = wallSize, transform = planeMatrix, area = 1 });
                        Encapsulate(worldBounds);
                    }
                }
            }

            // 3. Boîtes englobantes des meubles.
            if (boundingBoxManager != null)
            {
                foreach (var box in boundingBoxManager.trackables)
                {
                    sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        size = box.size,
                        transform = box.transform.localToWorldMatrix,
                        area = 1
                    });
                }
            }

            // 4. Rien (ou presque) : une grande dalle de sol autour du joueur (scan absent ou raté).
            UsedFallback = sources.Count == 0 || addFallbackSlab;
            if (UsedFallback)
            {
                var center = new Vector3(playerHead.x, floorY - 0.02f, playerHead.z);
                var size = new Vector3(fallbackRoomSize.x, 0.04f, fallbackRoomSize.y);
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = size, transform = Matrix4x4.Translate(center), area = 0 });
                Encapsulate(new Bounds(center, size));
                Debug.LogWarning("[Horreur] Données de pièce absentes ou insuffisantes : dalle de secours de " + fallbackRoomSize + " m.");
            }

            if (!m_CollectHasBounds)
                Encapsulate(new Bounds(new Vector3(playerHead.x, floorY, playerHead.z), new Vector3(fallbackRoomSize.x, 1f, fallbackRoomSize.y)));
            m_CollectBounds.Expand(new Vector3(1f, 3f, 1f));
            bounds = m_CollectBounds;
            return sources;
        }

        /// <summary>Empreinte des données de la pièce : change quand le Quest ajoute des plans, meubles ou maillages.</summary>
        public int DataSignature
        {
            get
            {
                int vertices = 0;
                if (meshManager != null && meshManager.meshes != null)
                {
                    foreach (var mf in meshManager.meshes)
                    {
                        if (mf != null && mf.sharedMesh != null)
                            vertices += mf.sharedMesh.vertexCount;
                    }
                }
                return PlaneCount * 7919 + BoxCount * 104729 + MeshCount * 31 + vertices;
            }
        }

        public bool DataChangedSinceBuild => NavMeshReady && DataSignature != m_BuiltSignature;

        int m_BuiltSignature;

        public IEnumerator BuildNavMesh(Vector3 playerHead)
        {
            yield return BuildNavMeshOnce(playerHead, false);
            if (!UsedFallback && !UsingManualRoom && m_FloorTotalArea < minWalkableArea)
            {
                Debug.LogWarning($"[Horreur] Surface marchable trop petite ({m_FloorTotalArea:F1} m²) : ajout de la dalle de secours.");
                yield return BuildNavMeshOnce(playerHead, true);
            }
        }

        IEnumerator BuildNavMeshOnce(Vector3 playerHead, bool addFallbackSlab)
        {
            NavMeshReady = false;
            m_BuiltSignature = DataSignature;
            var sources = CollectSources(playerHead, addFallbackSlab, out var bounds, out var floorPlane, out var floorY);

            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = agentRadius;
            settings.agentHeight = agentHeight;
            settings.agentClimb = agentClimb;
            settings.agentSlope = 30f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = voxelSize;
            settings.overrideTileSize = true;
            settings.tileSize = 128;
            settings.minRegionArea = 0.4f;

            if (m_NavInstance.valid)
                m_NavInstance.Remove();
            m_NavData = new NavMeshData(settings.agentTypeID) { name = "HorrorRoomNavMesh" };
            m_NavInstance = NavMesh.AddNavMeshData(m_NavData);
            var op = NavMeshBuilder.UpdateNavMeshDataAsync(m_NavData, settings, sources, bounds);
            while (!op.isDone)
                yield return null;

            ComputeFloorTriangles(floorPlane != null || UsingManualRoom ? floorY : (float?)null);
            RoomBounds = bounds;
            NavMeshReady = m_FloorTotalArea > 0.5f;
            Debug.Log($"[Horreur] NavMesh prêt : {sources.Count} sources, sol à {FloorY:F2} m, surface marchable {m_FloorTotalArea:F1} m², " +
                      $"plans {PlaneCount}, boîtes {BoxCount}, maillages {MeshCount}, secours {UsedFallback}.");
        }

        void ComputeFloorTriangles(float? knownFloor)
        {
            m_FloorTris.Clear();
            m_FloorTriCumulative.Clear();
            m_FloorTotalArea = 0f;
            var tri = NavMesh.CalculateTriangulation();
            if (tri.indices == null || tri.indices.Length < 3)
            {
                FloorY = knownFloor ?? m_FallbackFloorY;
                return;
            }

            // Hauteur du sol = la tranche de 5 cm qui contient le plus de surface marchable.
            var areaByBin = new Dictionary<int, float>();
            for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                var a = tri.vertices[tri.indices[i]];
                var b = tri.vertices[tri.indices[i + 1]];
                var c = tri.vertices[tri.indices[i + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                int bin = Mathf.RoundToInt((a.y + b.y + c.y) / 3f / 0.05f);
                areaByBin.TryGetValue(bin, out var acc);
                areaByBin[bin] = acc + area;
            }

            int bestBin = 0;
            float bestArea = -1f;
            foreach (var kv in areaByBin)
            {
                if (kv.Value > bestArea) { bestArea = kv.Value; bestBin = kv.Key; }
            }
            FloorY = knownFloor ?? bestBin * 0.05f;

            for (int i = 0; i + 2 < tri.indices.Length; i += 3)
            {
                var a = tri.vertices[tri.indices[i]];
                var b = tri.vertices[tri.indices[i + 1]];
                var c = tri.vertices[tri.indices[i + 2]];
                if (Mathf.Abs(a.y - FloorY) > 0.25f || Mathf.Abs(b.y - FloorY) > 0.25f || Mathf.Abs(c.y - FloorY) > 0.25f)
                    continue;
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (area < 1e-4f)
                    continue;
                m_FloorTris.Add(a);
                m_FloorTris.Add(b);
                m_FloorTris.Add(c);
                m_FloorTotalArea += area;
                m_FloorTriCumulative.Add(m_FloorTotalArea);
            }
        }

        /// <summary>Point aléatoire sur le sol marchable (jamais sur un meuble).</summary>
        public bool TryRandomFloorPoint(out Vector3 point)
        {
            point = default;
            if (m_FloorTriCumulative.Count == 0)
                return false;
            float r = Random.value * m_FloorTotalArea;
            int lo = 0, hi = m_FloorTriCumulative.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (m_FloorTriCumulative[mid] < r) lo = mid + 1;
                else hi = mid;
            }
            var a = m_FloorTris[lo * 3];
            var b = m_FloorTris[lo * 3 + 1];
            var c = m_FloorTris[lo * 3 + 2];
            float u = Random.value, v = Random.value;
            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
            point = a + (b - a) * u + (c - a) * v;
            return true;
        }

        public bool IsReachable(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        public bool SnapToNavMesh(Vector3 position, float maxDistance, out Vector3 snapped)
        {
            if (NavMesh.SamplePosition(position, out var hit, maxDistance, NavMesh.AllAreas))
            {
                snapped = hit.position;
                return true;
            }
            snapped = position;
            return false;
        }

        public Vector3 PlayerFloorPosition(Vector3 head) => new Vector3(head.x, FloorY, head.z);

        /// <summary>Point d'apparition loin du joueur, de préférence dans son dos, relié à lui par un chemin.</summary>
        public bool TryFindSpawnPoint(Vector3 head, Vector3 headForward, float minDistance, out Vector3 spawn)
        {
            spawn = default;
            var playerFloor = PlayerFloorPosition(head);
            SnapToNavMesh(playerFloor, 2f, out var playerOnNav);
            var flatForward = Vector3.ProjectOnPlane(headForward, Vector3.up).normalized;
            float bestScore = float.MinValue;
            bool found = false;
            for (int i = 0; i < 80; i++)
            {
                if (!TryRandomFloorPoint(out var p))
                    break;
                var toP = p - playerFloor;
                toP.y = 0f;
                float dist = toP.magnitude;
                float behind = -Vector3.Dot(flatForward, toP / Mathf.Max(dist, 0.01f));
                float score = Mathf.Min(dist, minDistance * 1.6f) + behind * 2.5f;
                if (dist < minDistance)
                    score -= 20f;
                if (score <= bestScore)
                    continue;
                if (!IsReachable(p, playerOnNav))
                    continue;
                bestScore = score;
                spawn = p;
                found = true;
            }
            return found;
        }

        /// <summary>Point de patrouille aléatoire, éventuellement attiré vers le joueur.</summary>
        public bool TryWanderPoint(Vector3 from, Vector3 head, float biasToPlayer, out Vector3 point)
        {
            point = default;
            var playerFloor = PlayerFloorPosition(head);
            for (int i = 0; i < 25; i++)
            {
                Vector3 p;
                if (Random.value < biasToPlayer)
                {
                    var offset = Random.insideUnitCircle.normalized * Random.Range(2.5f, 5f);
                    if (!SnapToNavMesh(playerFloor + new Vector3(offset.x, 0f, offset.y), 1.5f, out p))
                        continue;
                    if (Mathf.Abs(p.y - FloorY) > 0.3f)
                        continue;
                }
                else if (!TryRandomFloorPoint(out p))
                {
                    return false;
                }

                if ((p - from).sqrMagnitude < 2f * 2f)
                    continue;
                if (!IsReachable(from, p))
                    continue;
                point = p;
                return true;
            }
            return false;
        }

        /// <summary>Endroit où poser le pistolet : une table si possible, sinon le sol.</summary>
        public Vector3 FindGunSpot(Vector3 head)
        {
            var candidates = new List<Vector3>();
            var playerFloor = PlayerFloorPosition(head);
            if (UsingManualRoom && manualRoom.TryGetTableSpot(playerFloor, out var tableSpot))
                return tableSpot;

            bool GoodDistance(Vector3 p)
            {
                var d = p - playerFloor;
                d.y = 0f;
                return d.magnitude > 3f && d.magnitude < 14f;
            }

            // Tables détectées comme plans.
            if (planeManager != null)
            {
                foreach (var plane in planeManager.trackables)
                {
                    if (plane.alignment != PlaneAlignment.HorizontalUp || (plane.classifications & PlaneClassifications.Table) == 0)
                        continue;
                    float h = plane.center.y - FloorY;
                    if (h < 0.4f || h > 1.3f)
                        continue;
                    var local = new Vector3(Random.Range(-0.3f, 0.3f) * plane.size.x, 0f, Random.Range(-0.3f, 0.3f) * plane.size.y);
                    var p = plane.transform.TransformPoint(local + new Vector3(plane.centerInPlaneSpace.x, 0f, plane.centerInPlaneSpace.y));
                    if (GoodDistance(p))
                        candidates.Add(p);
                }
            }

            // Tables détectées comme boîtes.
            if (boundingBoxManager != null)
            {
                foreach (var box in boundingBoxManager.trackables)
                {
                    if ((box.classifications & BoundingBoxClassifications.Table) == 0)
                        continue;
                    var top = box.transform.TransformPoint(new Vector3(Random.Range(-0.3f, 0.3f) * box.size.x, box.size.y * 0.5f, Random.Range(-0.3f, 0.3f) * box.size.z));
                    float h = top.y - FloorY;
                    if (h > 0.4f && h < 1.3f && GoodDistance(top))
                        candidates.Add(top);
                }
            }

            // Surfaces à hauteur de table trouvées dans le maillage (lancers de rayons vers le bas).
            if (candidates.Count == 0 && MeshCount > 0)
            {
                var b = RoomBounds;
                for (int i = 0; i < 250 && candidates.Count < 8; i++)
                {
                    var origin = new Vector3(Random.Range(b.min.x, b.max.x), FloorY + 1.6f, Random.Range(b.min.z, b.max.z));
                    if (!Physics.Raycast(origin, Vector3.down, out var hit, 1.6f, ~0, QueryTriggerInteraction.Ignore))
                        continue;
                    if (hit.normal.y < 0.9f || hit.collider.GetComponentInParent<HorrorOwned>() != null || hit.collider.GetComponentInParent<MonsterAI>() != null)
                        continue;
                    float h = hit.point.y - FloorY;
                    if (h > 0.55f && h < 1.2f && GoodDistance(hit.point))
                        candidates.Add(hit.point);
                }
            }

            if (candidates.Count > 0)
                return candidates[Random.Range(0, candidates.Count)];

            // Repli : le sol, loin du joueur.
            for (int i = 0; i < 40; i++)
            {
                if (TryRandomFloorPoint(out var p) && GoodDistance(p))
                    return p;
            }
            return playerFloor + Vector3.ProjectOnPlane(Random.onUnitSphere, Vector3.up).normalized * 3f;
        }

        void OnDestroy()
        {
            if (m_NavInstance.valid)
                m_NavInstance.Remove();
        }
    }
}

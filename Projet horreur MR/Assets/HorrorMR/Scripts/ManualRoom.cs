using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace HorrorMR
{
    /// <summary>
    /// Pièce virtuelle construite à partir du scan manuel : sol, 4 murs et meubles en boîtes.
    /// Mêmes rôles que le maillage du Quest : assombrir la vraie pièce (sauf sous la lampe),
    /// cacher le monstre derrière les meubles, bloquer sa vue et servir au NavMesh.
    /// </summary>
    public class ManualRoom : MonoBehaviour
    {
        public Material darknessMaterial;
        public Material outlineMaterial;
        public float wallHeight = 3f;

        public RoomLayout Layout { get; private set; }
        public bool HasRoom => Layout != null;
        public float FloorY => transform.position.y;

        readonly List<Renderer> m_FurnitureRenderers = new List<Renderer>();
        readonly List<GameObject> m_Outlines = new List<GameObject>();
        Transform m_Content;
        Transform m_Head;

        public static readonly Color OutlineColor = new Color(1f, 0.25f, 0.1f, 0.9f);

        /// <summary>Place la pièce dans le monde (éventuellement accrochée à une ancre spatiale) et la construit.</summary>
        public void Apply(RoomLayout layout, Pose frame, Transform anchor = null)
        {
            Layout = layout;
            transform.SetParent(anchor, false);
            if (anchor != null)
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
            else
            {
                transform.SetPositionAndRotation(frame.position, frame.rotation);
            }
            Rebuild();
        }

        public void Clear()
        {
            Layout = null;
            if (m_Content != null)
                Destroy(m_Content.gameObject);
            m_Content = null;
            m_FurnitureRenderers.Clear();
            m_Outlines.Clear();
        }

        public Pose Frame => new Pose(transform.position, transform.rotation);

        void Rebuild()
        {
            if (m_Content != null)
                Destroy(m_Content.gameObject);
            m_FurnitureRenderers.Clear();
            m_Outlines.Clear();
            m_Content = new GameObject("Contenu").transform;
            m_Content.SetParent(transform, false);

            var l = Layout;
            float cx = (l.minX + l.maxX) * 0.5f;
            const float t = 0.06f;
            Block("Sol", new Vector3(cx, -0.01f, l.depth * 0.5f), new Vector3(l.Width, 0.02f, l.depth));
            Block("Mur 1", new Vector3(cx, wallHeight * 0.5f, -t * 0.5f), new Vector3(l.Width + 2 * t, wallHeight, t));
            Block("Mur 2", new Vector3(cx, wallHeight * 0.5f, l.depth + t * 0.5f), new Vector3(l.Width + 2 * t, wallHeight, t));
            Block("Mur 3", new Vector3(l.minX - t * 0.5f, wallHeight * 0.5f, l.depth * 0.5f), new Vector3(t, wallHeight, l.depth));
            Block("Mur 4", new Vector3(l.maxX + t * 0.5f, wallHeight * 0.5f, l.depth * 0.5f), new Vector3(t, wallHeight, l.depth));

            m_Outlines.Add(CreateOutline(m_Content, "Contour pièce", outlineMaterial, l.FloorCenter + Vector3.up * 0.01f,
                new Vector3(l.Width, 0f, l.depth)).gameObject);
            for (int i = 0; i < l.furniture.Count; i++)
            {
                var f = l.furniture[i];
                var go = Block("Meuble " + (i + 1), f.center, f.size);
                m_FurnitureRenderers.Add(go.GetComponent<Renderer>());
                m_Outlines.Add(CreateOutline(m_Content, "Contour meuble " + (i + 1), outlineMaterial, f.center, f.size).gameObject);
            }
        }

        GameObject Block(string name, Vector3 center, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(m_Content, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = darknessMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        public void SetOutlinesVisible(bool visible)
        {
            foreach (var o in m_Outlines)
            {
                if (o != null)
                    o.SetActive(visible);
            }
        }

        void LateUpdate()
        {
            if (m_FurnitureRenderers.Count == 0)
                return;
            if (m_Head == null && Camera.main != null)
                m_Head = Camera.main.transform;
            if (m_Head == null)
                return;
            // Sous une table, la boîte du meuble entourerait la tête et masquerait tout : on la cache.
            var head = transform.InverseTransformPoint(m_Head.position);
            for (int i = 0; i < m_FurnitureRenderers.Count && i < Layout.furniture.Count; i++)
            {
                var f = Layout.furniture[i];
                var half = f.size * 0.5f + Vector3.one * 0.15f;
                var d = head - f.center;
                bool inside = Mathf.Abs(d.x) < half.x && Mathf.Abs(d.y) < half.y && Mathf.Abs(d.z) < half.z;
                m_FurnitureRenderers[i].enabled = !inside;
            }
        }

        // ------------------------------------------------------------------------------------------
        // Données pour le NavMesh et le placement des objets
        // ------------------------------------------------------------------------------------------

        public Bounds WorldBounds
        {
            get
            {
                var l = Layout;
                var b = new Bounds(transform.TransformPoint(new Vector3(l.minX, 0f, 0f)), Vector3.zero);
                b.Encapsulate(transform.TransformPoint(new Vector3(l.maxX, 0f, 0f)));
                b.Encapsulate(transform.TransformPoint(new Vector3(l.minX, wallHeight, l.depth)));
                b.Encapsulate(transform.TransformPoint(new Vector3(l.maxX, wallHeight, l.depth)));
                return b;
            }
        }

        public void CollectNavSources(List<NavMeshBuildSource> sources)
        {
            var l = Layout;
            var m = transform.localToWorldMatrix;
            sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(l.Width, 0.04f, l.depth),
                transform = m * Matrix4x4.Translate(l.FloorCenter + Vector3.down * 0.02f),
                area = 0
            });
            foreach (var f in l.furniture)
            {
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = f.size,
                    transform = m * Matrix4x4.Translate(f.center),
                    area = 1
                });
            }
        }

        /// <summary>Point au hasard sur le dessus d'un meuble à hauteur de table, assez loin du joueur.</summary>
        public bool TryGetTableSpot(Vector3 playerFloor, out Vector3 spot)
        {
            spot = default;
            var candidates = new List<Vector3>();
            foreach (var f in Layout.furniture)
            {
                if (f.size.y < 0.4f || f.size.y > 1.3f)
                    continue;
                var local = f.center + new Vector3(Random.Range(-0.3f, 0.3f) * f.size.x, f.size.y * 0.5f, Random.Range(-0.3f, 0.3f) * f.size.z);
                var world = transform.TransformPoint(local);
                var d = world - playerFloor;
                d.y = 0f;
                if (d.magnitude > 3f && d.magnitude < 14f)
                    candidates.Add(world);
            }
            if (candidates.Count == 0)
                return false;
            spot = candidates[Random.Range(0, candidates.Count)];
            return true;
        }

        // ------------------------------------------------------------------------------------------
        // Contours (aperçu du scan)
        // ------------------------------------------------------------------------------------------

        /// <summary>Contour filaire d'une boîte (size.y = 0 : simple rectangle au sol).</summary>
        public static LineRenderer CreateOutline(Transform parent, string name, Material material, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.startWidth = line.endWidth = 0.012f;
            line.startColor = line.endColor = OutlineColor;
            line.shadowCastingMode = ShadowCastingMode.Off;
            SetOutline(line, center, size);
            return line;
        }

        public static void SetOutline(LineRenderer line, Vector3 center, Vector3 size)
        {
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + new Vector3(x * h.x, y * h.y, z * h.z);
            if (size.y <= 0.001f)
            {
                line.positionCount = 5;
                line.SetPositions(new[] { P(-1, 0, -1), P(1, 0, -1), P(1, 0, 1), P(-1, 0, 1), P(-1, 0, -1) });
                return;
            }
            var pts = new[]
            {
                P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), P(-1, -1, -1),
                P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1),
                P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)
            };
            line.positionCount = pts.Length;
            line.SetPositions(pts);
        }
    }
}

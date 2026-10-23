using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HorrorMR
{
    /// <summary>Un meuble saisi à la main : boîte alignée sur les murs, dans le repère de la pièce.</summary>
    [Serializable]
    public class FurnitureBox
    {
        public Vector3 center;
        public Vector3 size;
    }

    /// <summary>
    /// Pièce scannée à la main. Repère : origine au coin 1 au niveau du sol, axe X le long du mur
    /// coin 1 → coin 2, axe Z vers l'intérieur de la pièce. Sauvegardée en JSON dans le casque.
    /// </summary>
    [Serializable]
    public class RoomLayout
    {
        /// <summary>Hauteur de la manette posée à plat au-dessus de la surface (m).</summary>
        public const float ControllerRestHeight = 0.04f;

        public float minX;
        public float maxX;
        public float depth;
        [Tooltip("+1 ou -1 : de quel côté du mur coin 1 → coin 2 se trouve la pièce.")]
        public float zSign = 1f;
        public List<FurnitureBox> furniture = new List<FurnitureBox>();
        [Tooltip("Ancre spatiale persistante du Quest qui mémorise le repère de la pièce.")]
        public string anchorGuid;
        // Repère de la dernière session : sert uniquement quand les ancres ne sont pas disponibles (éditeur).
        public Vector3 lastFramePosition;
        public Quaternion lastFrameRotation = Quaternion.identity;

        public float Width => maxX - minX;
        public Vector3 FloorCenter => new Vector3((minX + maxX) * 0.5f, 0f, depth * 0.5f);

        public static string FilePath => Path.Combine(Application.persistentDataPath, "piece_manuelle.json");
        public static bool Exists => File.Exists(FilePath);

        public void Save() => File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));

        public static RoomLayout Load()
        {
            try
            {
                return JsonUtility.FromJson<RoomLayout>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Horreur] Pièce enregistrée illisible : " + e.Message);
                return null;
            }
        }

        public static void Delete()
        {
            if (Exists)
                File.Delete(FilePath);
        }

        /// <summary>Repère de la pièce à partir du coin 1 et du coin 2 (le long du même mur).</summary>
        public static Pose FrameFromWall(Vector3 corner1, Vector3 corner2, float floorY, float zSign)
        {
            var x = Vector3.ProjectOnPlane(corner2 - corner1, Vector3.up);
            if (x.sqrMagnitude < 0.0001f)
                x = Vector3.right;
            x.Normalize();
            var z = Vector3.Cross(x, Vector3.up) * Mathf.Sign(zSign == 0f ? 1f : zSign);
            return new Pose(new Vector3(corner1.x, floorY, corner1.z), Quaternion.LookRotation(z, Vector3.up));
        }

        static Vector3 ToLocal(Pose frame, Vector3 world) => Quaternion.Inverse(frame.rotation) * (world - frame.position);

        /// <summary>Crée la pièce à partir de 3 coins posés au sol : coin 1, coin 2 (même mur), coin du mur d'en face.</summary>
        public static RoomLayout FromCorners(Vector3 corner1, Vector3 corner2, Vector3 opposite, out Pose frame)
        {
            float floorY = (corner1.y + corner2.y + opposite.y) / 3f - ControllerRestHeight;
            var x = Vector3.ProjectOnPlane(corner2 - corner1, Vector3.up).normalized;
            float side = Vector3.Dot(opposite - corner1, Vector3.Cross(x, Vector3.up)) >= 0f ? 1f : -1f;
            frame = FrameFromWall(corner1, corner2, floorY, side);
            var b = ToLocal(frame, corner2);
            var c = ToLocal(frame, opposite);
            return new RoomLayout
            {
                zSign = side,
                minX = Mathf.Min(0f, b.x, c.x),
                maxX = Mathf.Max(0f, b.x, c.x),
                depth = Mathf.Max(c.z, 0.5f),
            };
        }

        /// <summary>Boîte d'un meuble à partir d'un coin posé au sol et du coin opposé posé sur le dessus.</summary>
        public static FurnitureBox BoxFromCorners(Pose frame, Vector3 pointA, Vector3 pointB)
        {
            var a = ToLocal(frame, pointA);
            var b = ToLocal(frame, pointB);
            float top = Mathf.Clamp(Mathf.Max(a.y, b.y) - ControllerRestHeight, 0.15f, 3f);
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
            float minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);
            return new FurnitureBox
            {
                center = new Vector3((minX + maxX) * 0.5f, top * 0.5f, (minZ + maxZ) * 0.5f),
                size = new Vector3(Mathf.Max(maxX - minX, 0.1f), top, Mathf.Max(maxZ - minZ, 0.1f)),
            };
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HorrorMR
{
    /// <summary>
    /// Scan manuel de la pièce à la manette (le scan du Quest ne couvre qu'une partie d'une grande salle) :
    /// 3 coins au sol pour la pièce, puis 2 coins par meuble (un au sol, l'opposé sur le dessus).
    /// Gâchette = valider le point, B/Y = annuler, A/X = terminer.
    /// </summary>
    public class RoomCalibrator : MonoBehaviour
    {
        GameManager m_Game;
        Transform m_Preview;
        GameObject m_MarkerLeft;
        GameObject m_MarkerRight;
        readonly List<GameObject> m_PointMarkers = new List<GameObject>();

        /// <summary>Tests automatiques : points injectés à la place des appuis (null = « terminer »).</summary>
        public readonly Queue<Vector3?> testInputs = new Queue<Vector3?>();

        const string k_Undo = "B / Y : annuler";

        public void Init(GameManager game) => m_Game = game;

        enum InputKind { None, Point, Undo, Finish }

        InputKind ReadInput(out Vector3 point)
        {
            point = default;
            if (testInputs.Count > 0)
            {
                var next = testInputs.Dequeue();
                if (!next.HasValue)
                    return InputKind.Finish;
                point = next.Value;
                return InputKind.Point;
            }
            foreach (var side in new[] { Hand.Right, Hand.Left })
            {
                var hand = side == Hand.Left ? m_Game.LeftHand : m_Game.RightHand;
                if (hand != null && hand.HasPose && HorrorInput.TriggerPressed(side))
                {
                    point = hand.transform.position;
                    HorrorInput.Haptic(side, 0.5f, 0.08f);
                    return InputKind.Point;
                }
            }
            if (HorrorInput.AnySecondaryPressed)
                return InputKind.Undo;
            if (HorrorInput.AnyPrimaryPressed)
                return InputKind.Finish;
            return InputKind.None;
        }

        void Feedback() => m_Game.horrorAudio.PlayUI(m_Game.assets.tick, 0.9f);

        void BeginVisuals()
        {
            EndVisuals();
            m_Preview = new GameObject("Aperçu du scan").transform;
            m_MarkerLeft = CreateMarker("Repère main gauche", 0.025f);
            m_MarkerRight = CreateMarker("Repère main droite", 0.025f);
        }

        void EndVisuals()
        {
            if (m_Preview != null)
                Destroy(m_Preview.gameObject);
            if (m_MarkerLeft != null)
                Destroy(m_MarkerLeft);
            if (m_MarkerRight != null)
                Destroy(m_MarkerRight);
            m_PointMarkers.Clear();
        }

        GameObject CreateMarker(string name, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.AddComponent<HorrorOwned>();
            go.transform.localScale = Vector3.one * radius * 2f;
            go.GetComponent<MeshRenderer>().sharedMaterial = m_Game.assets.arrow;
            if (m_Preview != null)
                go.transform.SetParent(m_Preview, true);
            return go;
        }

        void AddPointMarker(Vector3 p)
        {
            var go = CreateMarker("Point", 0.035f);
            go.transform.position = p;
            m_PointMarkers.Add(go);
        }

        void RemoveLastPointMarker()
        {
            if (m_PointMarkers.Count == 0)
                return;
            Destroy(m_PointMarkers[m_PointMarkers.Count - 1]);
            m_PointMarkers.RemoveAt(m_PointMarkers.Count - 1);
        }

        void Update()
        {
            if (m_MarkerLeft != null && m_Game.LeftHand != null)
                m_MarkerLeft.transform.position = m_Game.LeftHand.transform.position;
            if (m_MarkerRight != null && m_Game.RightHand != null)
                m_MarkerRight.transform.position = m_Game.RightHand.transform.position;
        }

        Vector3 CurrentHandPoint()
        {
            var right = m_Game.RightHand;
            return right != null && right.HasPose ? right.transform.position : (m_Game.LeftHand != null ? m_Game.LeftHand.transform.position : Vector3.zero);
        }

        /// <summary>Scan complet : pièce (3 coins) puis meubles.</summary>
        public IEnumerator RunFullScan(Action<RoomLayout, Pose> done)
        {
            BeginVisuals();
            var corners = new List<Vector3>();
            string[] steps =
            {
                "Pose une manette AU SOL dans un coin de la pièce, puis appuie sur sa gâchette.\nRetiens bien ce coin : c'est le COIN 1.",
                "Pose-la au sol dans le COIN 2 : l'autre bout du MÊME MUR.",
                "Pose-la au sol dans un coin du MUR D'EN FACE."
            };

            while (corners.Count < 3)
            {
                m_Game.hud.Show($"SCAN DE LA PIÈCE ({corners.Count + 1}/3)", steps[corners.Count], corners.Count > 0 ? k_Undo : "", HUD.White);
                InputKind input;
                Vector3 p;
                while ((input = ReadInput(out p)) == InputKind.None || (input == InputKind.Finish))
                    yield return null;
                if (input == InputKind.Undo)
                {
                    if (corners.Count > 0)
                    {
                        corners.RemoveAt(corners.Count - 1);
                        RemoveLastPointMarker();
                    }
                    yield return null;
                    continue;
                }
                corners.Add(p);
                AddPointMarker(p);
                Feedback();
                yield return null;
            }

            var layout = RoomLayout.FromCorners(corners[0], corners[1], corners[2], out var frame);
            var frameRoot = new GameObject("Repère pièce").transform;
            frameRoot.SetParent(m_Preview, false);
            frameRoot.SetPositionAndRotation(frame.position, frame.rotation);
            ManualRoom.CreateOutline(frameRoot, "Contour pièce", m_Game.assets.arrow, layout.FloorCenter + Vector3.up * 0.01f, new Vector3(layout.Width, 0f, layout.depth));
            Debug.Log($"[Horreur] Pièce scannée : {layout.Width:F1} x {layout.depth:F1} m.");

            yield return ScanFurniture(layout, frame, frameRoot);
            EndVisuals();
            done(layout, frame);
        }

        IEnumerator ScanFurniture(RoomLayout layout, Pose frame, Transform frameRoot)
        {
            var outlines = new List<GameObject>();
            LineRenderer live = null;
            Vector3? first = null;
            const string help = "A / X : terminer      B / Y : annuler le dernier point ou meuble";

            while (true)
            {
                string step = first.HasValue
                    ? "Pose maintenant la manette SUR LE DESSUS du meuble, au coin OPPOSÉ en diagonale, puis gâchette."
                    : "Pose la manette AU SOL contre un coin d'un meuble (table, armoire, rangée de tables…), puis gâchette.";
                m_Game.hud.Show($"MEUBLES ({layout.furniture.Count} saisi{(layout.furniture.Count > 1 ? "s" : "")})", step, help, HUD.White);

                var input = ReadInput(out var p);
                if (first.HasValue && live != null)
                {
                    var preview = RoomLayout.BoxFromCorners(frame, first.Value, CurrentHandPoint());
                    ManualRoom.SetOutline(live, preview.center, preview.size);
                }

                switch (input)
                {
                    case InputKind.Point when !first.HasValue:
                        first = p;
                        AddPointMarker(p);
                        live = ManualRoom.CreateOutline(frameRoot, "Meuble en cours", m_Game.assets.arrow, Vector3.zero, Vector3.zero);
                        Feedback();
                        break;
                    case InputKind.Point:
                        var box = RoomLayout.BoxFromCorners(frame, first.Value, p);
                        layout.furniture.Add(box);
                        RemoveLastPointMarker();
                        ManualRoom.SetOutline(live, box.center, box.size);
                        outlines.Add(live.gameObject);
                        live = null;
                        first = null;
                        Feedback();
                        break;
                    case InputKind.Undo when first.HasValue:
                        first = null;
                        RemoveLastPointMarker();
                        if (live != null)
                            Destroy(live.gameObject);
                        live = null;
                        break;
                    case InputKind.Undo when layout.furniture.Count > 0:
                        layout.furniture.RemoveAt(layout.furniture.Count - 1);
                        Destroy(outlines[outlines.Count - 1]);
                        outlines.RemoveAt(outlines.Count - 1);
                        break;
                    case InputKind.Finish when !first.HasValue:
                        Debug.Log($"[Horreur] {layout.furniture.Count} meuble(s) saisi(s).");
                        yield break;
                }
                yield return null;
            }
        }

        /// <summary>Recalage d'une pièce enregistrée quand l'ancre du Quest est perdue : on retouche les coins 1 et 2.</summary>
        public IEnumerator RunRealign(RoomLayout layout, Action<Pose> done)
        {
            BeginVisuals();
            var corners = new List<Vector3>();
            string[] steps =
            {
                "Pose une manette au sol dans le COIN 1 de ton scan, puis gâchette.",
                "Pose-la au sol dans le COIN 2 (l'autre bout du même mur)."
            };
            while (corners.Count < 2)
            {
                m_Game.hud.Show($"RECALAGE DE LA PIÈCE ({corners.Count + 1}/2)", steps[corners.Count],
                    corners.Count > 0 ? k_Undo : "La pièce enregistrée n'a pas pu être retrouvée automatiquement.", HUD.White);
                InputKind input;
                Vector3 p;
                while ((input = ReadInput(out p)) == InputKind.None || input == InputKind.Finish)
                    yield return null;
                if (input == InputKind.Undo)
                {
                    if (corners.Count > 0)
                    {
                        corners.RemoveAt(corners.Count - 1);
                        RemoveLastPointMarker();
                    }
                    yield return null;
                    continue;
                }
                corners.Add(p);
                AddPointMarker(p);
                Feedback();
                yield return null;
            }
            float floorY = (corners[0].y + corners[1].y) * 0.5f - RoomLayout.ControllerRestHeight;
            EndVisuals();
            done(RoomLayout.FrameFromWall(corners[0], corners[1], floorY, layout.zSign));
        }
    }
}

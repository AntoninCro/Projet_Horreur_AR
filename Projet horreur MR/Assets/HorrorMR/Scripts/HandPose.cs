using UnityEngine;
using UnityEngine.InputSystem;

namespace HorrorMR
{
    /// <summary>
    /// Suit la pose de visée d'une manette (à placer sous le "Camera Offset" de l'XR Origin).
    /// Indépendant des interacteurs du template.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HandPose : MonoBehaviour
    {
        public Hand hand = Hand.Right;
        [Tooltip("Éditeur uniquement : sans manette détectée, la main suit la caméra (test clavier/souris).")]
        public bool followCameraWithoutController = true;

        InputAction m_AimPosition;
        InputAction m_AimRotation;
        InputAction m_GripPosition;
        InputAction m_GripRotation;

        public bool HasPose { get; private set; }

        void OnEnable()
        {
            var side = hand == Hand.Left ? "{LeftHand}" : "{RightHand}";
            m_AimPosition = Create($"<XRController>{side}/pointerPosition", "Vector3");
            m_AimRotation = Create($"<XRController>{side}/pointerRotation", "Quaternion");
            m_GripPosition = Create($"<XRController>{side}/devicePosition", "Vector3");
            m_GripRotation = Create($"<XRController>{side}/deviceRotation", "Quaternion");
            Application.onBeforeRender += OnBeforeRender;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= OnBeforeRender;
            m_AimPosition?.Dispose();
            m_AimRotation?.Dispose();
            m_GripPosition?.Dispose();
            m_GripRotation?.Dispose();
        }

        static InputAction Create(string binding, string controlType)
        {
            var action = new InputAction(type: InputActionType.Value, binding: binding, expectedControlType: controlType);
            action.Enable();
            return action;
        }

        void Update() => Apply();

        void OnBeforeRender() => Apply();

        void Apply()
        {
            if (m_AimPosition == null)
                return;

            if (m_AimPosition.controls.Count > 0 && m_AimRotation.controls.Count > 0)
            {
                transform.localPosition = m_AimPosition.ReadValue<Vector3>();
                transform.localRotation = m_AimRotation.ReadValue<Quaternion>();
                HasPose = true;
            }
            else if (m_GripPosition.controls.Count > 0 && m_GripRotation.controls.Count > 0)
            {
                transform.localPosition = m_GripPosition.ReadValue<Vector3>();
                transform.localRotation = m_GripRotation.ReadValue<Quaternion>();
                HasPose = true;
            }
            else
            {
                HasPose = false;
#if UNITY_EDITOR
                // Test dans l'éditeur (simulation XR, clavier/souris) : la main suit la caméra.
                var cam = followCameraWithoutController ? Camera.main : null;
                if (cam != null)
                {
                    var offset = hand == Hand.Left ? new Vector3(-0.16f, -0.2f, 0.3f) : new Vector3(0.16f, -0.2f, 0.3f);
                    transform.position = cam.transform.TransformPoint(offset);
                    transform.rotation = cam.transform.rotation;
                    HasPose = true;
                }
#endif
            }
        }
    }
}

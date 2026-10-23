using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.OpenXR.Input;

namespace HorrorMR
{
    public enum Hand { Left, Right }

    /// <summary>
    /// Entrées du jeu (manettes Quest + clavier/souris pour tester dans l'éditeur) et vibrations.
    /// </summary>
    public static class HorrorInput
    {
        static InputAction s_LeftTrigger;
        static InputAction s_RightTrigger;
        static InputAction s_LeftGrip;
        static InputAction s_RightGrip;
        static InputAction s_LeftPrimary;
        static InputAction s_RightPrimary;
        static InputAction s_LeftSecondary;
        static InputAction s_RightSecondary;
        static InputAction s_LeftHaptic;
        static InputAction s_RightHaptic;
        static InputAction s_DebugSkip;
        static bool s_Initialized;

        public static void Init()
        {
            if (s_Initialized)
                return;
            s_Initialized = true;

            s_LeftTrigger = Button("<XRController>{LeftHand}/triggerPressed", "<XRController>{LeftHand}/{TriggerButton}", "<Keyboard>/f");
            s_RightTrigger = Button("<XRController>{RightHand}/triggerPressed", "<XRController>{RightHand}/{TriggerButton}", "<Keyboard>/space", "<Mouse>/leftButton");
            s_LeftGrip = Button("<XRController>{LeftHand}/gripPressed", "<XRController>{LeftHand}/{GripButton}");
            s_RightGrip = Button("<XRController>{RightHand}/gripPressed", "<XRController>{RightHand}/{GripButton}", "<Keyboard>/g");
            s_LeftPrimary = Button("<XRController>{LeftHand}/primaryButton", "<XRController>{LeftHand}/{PrimaryButton}");
            s_RightPrimary = Button("<XRController>{RightHand}/primaryButton", "<XRController>{RightHand}/{PrimaryButton}", "<Keyboard>/enter");
            s_LeftSecondary = Button("<XRController>{LeftHand}/secondaryButton", "<XRController>{LeftHand}/{SecondaryButton}");
            s_RightSecondary = Button("<XRController>{RightHand}/secondaryButton", "<XRController>{RightHand}/{SecondaryButton}", "<Keyboard>/backspace");
            s_DebugSkip = Button("<Keyboard>/k");

            s_LeftHaptic = new InputAction("LeftHaptic", InputActionType.PassThrough, "<XRController>{LeftHand}/{Haptic}");
            s_RightHaptic = new InputAction("RightHaptic", InputActionType.PassThrough, "<XRController>{RightHand}/{Haptic}");
            s_LeftHaptic.Enable();
            s_RightHaptic.Enable();
        }

        static InputAction Button(params string[] bindings)
        {
            var action = new InputAction(type: InputActionType.Button);
            foreach (var binding in bindings)
                action.AddBinding(binding);
            action.Enable();
            return action;
        }

        public static bool TriggerPressed(Hand hand) => (hand == Hand.Left ? s_LeftTrigger : s_RightTrigger).WasPressedThisFrame();
        public static bool TriggerHeld(Hand hand) => (hand == Hand.Left ? s_LeftTrigger : s_RightTrigger).IsPressed();
        public static bool GripPressed(Hand hand) => (hand == Hand.Left ? s_LeftGrip : s_RightGrip).WasPressedThisFrame();
        public static bool GripHeld(Hand hand) => (hand == Hand.Left ? s_LeftGrip : s_RightGrip).IsPressed();
        public static bool PrimaryPressed(Hand hand) => (hand == Hand.Left ? s_LeftPrimary : s_RightPrimary).WasPressedThisFrame();
        public static bool PrimaryHeld(Hand hand) => (hand == Hand.Left ? s_LeftPrimary : s_RightPrimary).IsPressed();

        public static bool SecondaryPressed(Hand hand) => (hand == Hand.Left ? s_LeftSecondary : s_RightSecondary).WasPressedThisFrame();
        public static bool AnySecondaryPressed => SecondaryPressed(Hand.Left) || SecondaryPressed(Hand.Right);

        public static bool AnyTriggerPressed => TriggerPressed(Hand.Left) || TriggerPressed(Hand.Right);
        public static bool AnyPrimaryPressed => PrimaryPressed(Hand.Left) || PrimaryPressed(Hand.Right);

        /// <summary>Raccourci de test : touche K dans l'éditeur, ou A + X avec les deux boutons grip maintenus.</summary>
        public static bool DebugSkipPressed =>
            s_DebugSkip.WasPressedThisFrame() ||
            (PrimaryHeld(Hand.Left) && PrimaryHeld(Hand.Right) && GripHeld(Hand.Left) && GripHeld(Hand.Right) &&
             (PrimaryPressed(Hand.Left) || PrimaryPressed(Hand.Right)));

        public static void Haptic(Hand hand, float amplitude, float duration)
        {
            var action = hand == Hand.Left ? s_LeftHaptic : s_RightHaptic;
            try
            {
                if (action != null && action.controls.Count > 0)
                {
                    OpenXRInput.SendHapticImpulse(action, amplitude, duration);
                    return;
                }
            }
            catch (System.Exception)
            {
                // On tente la méthode générique ci-dessous.
            }

            var controller = hand == Hand.Left ? XRController.leftHand : XRController.rightHand;
            if (controller is XRControllerWithRumble rumble)
                rumble.SendImpulse(amplitude, duration);
        }

        public static void HapticBoth(float amplitude, float duration)
        {
            Haptic(Hand.Left, amplitude, duration);
            Haptic(Hand.Right, amplitude, duration);
        }
    }
}

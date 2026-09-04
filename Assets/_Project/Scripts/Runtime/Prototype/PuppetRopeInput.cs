using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1.2 prototype input. Two thumbs, two ropes — nothing else.
    ///
    ///  * VERTICAL drag of a thumb inside its zone  -> that rope's tension (0..1).
    ///  * HORIZONTAL drag, AVERAGED over both thumbs -> depth axis (-1..1):
    ///        both thumbs drag right  -> OUTWARD (+)
    ///        both thumbs drag left   -> INWARD  (-)
    ///        thumbs drag opposite ways -> cancels (no depth)
    ///
    /// Desktop test: A = left rope, L = right rope, Space = both;
    ///               Q = inward, E = outward (depth).
    ///
    /// Left zone -> Left rope -> Foot_L. Right zone -> Right rope -> Foot_R.
    /// This mapping is by the puppet's anatomy and never flips with PlayerSide.
    /// </summary>
    [RequireComponent(typeof(PuppetRopeController))]
    public sealed class PuppetRopeInput : MonoBehaviour
    {
        [Header("Vertical drag -> rope tension")]
        [Min(20f)] public float dragFullPixels = 220f;

        [Header("Horizontal drag -> depth (inward / outward)")]
        [Min(20f)] public float depthFullPixels = 170f;
        [Min(0f)] public float depthDeadzonePixels = 16f;

        [Header("Response (units per second) — Phase 1.2: much faster")]
        [Min(0.1f)] public float tensionRiseSpeed = 12f;
        [Min(0.1f)] public float tensionFallSpeed = 10f;
        [Min(0.1f)] public float depthSpeed = 9f;

        [Header("Keyboard test bindings (desktop)")]
        public Key leftKey = Key.A;
        public Key rightKey = Key.L;
        public Key bothKey = Key.Space;
        public Key inwardKey = Key.Q;
        public Key outwardKey = Key.E;

        // read-only for the HUD
        public float LeftTarget { get; private set; }
        public float RightTarget { get; private set; }
        public float LeftValue { get; private set; }
        public float RightValue { get; private set; }
        public float DepthTarget { get; private set; }
        public float DepthValue { get; private set; }

        PuppetRopeController _controller;

        // mouse: one zone at a time
        bool _mouseActive;
        Vector2 _mouseStart;
        bool _mouseLeftZone;

        struct DragOrigin { public Vector2 start; public bool leftZone; }
        readonly Dictionary<int, DragOrigin> _touches = new();

        void Awake() => _controller = GetComponent<PuppetRopeController>();
        void OnEnable() => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void Update()
        {
            float dt = Time.deltaTime;
            float halfW = Screen.width * 0.5f;

            float targetLeft = 0f, targetRight = 0f;
            float depthLeftContribution = 0f, depthRightContribution = 0f;
            int depthLeftCount = 0, depthRightCount = 0;

            // ---- keyboard (desktop) ----
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb[bothKey].isPressed) { targetLeft = 1f; targetRight = 1f; }
                if (kb[leftKey].isPressed) targetLeft = 1f;
                if (kb[rightKey].isPressed) targetRight = 1f;

                float kbDepth = 0f;
                if (kb[outwardKey].isPressed) kbDepth += 1f;
                if (kb[inwardKey].isPressed) kbDepth -= 1f;
                if (kbDepth != 0f)
                {
                    depthLeftContribution += kbDepth; depthLeftCount++;
                    depthRightContribution += kbDepth; depthRightCount++;
                }
            }

            // ---- mouse (desktop, one zone) ----
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 mp = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    _mouseActive = true; _mouseStart = mp; _mouseLeftZone = mp.x < halfW;
                }
                else if (!mouse.leftButton.isPressed) _mouseActive = false;

                if (_mouseActive)
                {
                    float t = Mathf.Clamp01((_mouseStart.y - mp.y) / dragFullPixels);
                    float d = HorizontalToDepth(mp.x - _mouseStart.x);
                    if (_mouseLeftZone) { targetLeft = Mathf.Max(targetLeft, t); depthLeftContribution += d; depthLeftCount++; }
                    else { targetRight = Mathf.Max(targetRight, t); depthRightContribution += d; depthRightCount++; }
                }
            }

            // ---- multitouch (mobile) ----
            foreach (var touch in ETouch.Touch.activeTouches)
            {
                switch (touch.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        _touches[touch.touchId] = new DragOrigin
                        {
                            start = touch.screenPosition,
                            leftZone = touch.screenPosition.x < halfW,
                        };
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        if (_touches.TryGetValue(touch.touchId, out var o))
                        {
                            float t = Mathf.Clamp01((o.start.y - touch.screenPosition.y) / dragFullPixels);
                            float d = HorizontalToDepth(touch.screenPosition.x - o.start.x);
                            if (o.leftZone) { targetLeft = Mathf.Max(targetLeft, t); depthLeftContribution += d; depthLeftCount++; }
                            else { targetRight = Mathf.Max(targetRight, t); depthRightContribution += d; depthRightCount++; }
                        }
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        _touches.Remove(touch.touchId);
                        break;
                }
            }

            // depth = average of the two thumbs' horizontal contribution
            float leftD = depthLeftCount > 0 ? depthLeftContribution / depthLeftCount : 0f;
            float rightD = depthRightCount > 0 ? depthRightContribution / depthRightCount : 0f;
            float depthTarget;
            if (depthLeftCount > 0 && depthRightCount > 0) depthTarget = 0.5f * (leftD + rightD);
            else if (depthLeftCount > 0) depthTarget = leftD;
            else if (depthRightCount > 0) depthTarget = rightD;
            else depthTarget = 0f;

            LeftTarget = targetLeft;
            RightTarget = targetRight;
            DepthTarget = Mathf.Clamp(depthTarget, -1f, 1f);

            LeftValue = MoveToward(LeftValue, targetLeft, dt, tensionRiseSpeed, tensionFallSpeed);
            RightValue = MoveToward(RightValue, targetRight, dt, tensionRiseSpeed, tensionFallSpeed);
            DepthValue = Mathf.MoveTowards(DepthValue, DepthTarget, depthSpeed * dt);

            _controller.SetInput(LeftValue, RightValue, DepthValue);
        }

        float HorizontalToDepth(float pixels)
        {
            float sign = Mathf.Sign(pixels);
            float mag = Mathf.Max(0f, Mathf.Abs(pixels) - depthDeadzonePixels);
            return Mathf.Clamp(sign * mag / depthFullPixels, -1f, 1f);
        }

        static float MoveToward(float current, float target, float dt, float rise, float fall)
        {
            float speed = target > current ? rise : fall;
            return Mathf.MoveTowards(current, target, speed * dt);
        }
    }
}

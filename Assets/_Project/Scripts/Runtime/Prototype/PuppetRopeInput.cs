using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1.x unified 2-thumb control scheme.
    ///
    /// Vertical inputs (independent per thumb):
    ///  * Left Thumb vertical  -> Left rope tension (rear foot).
    ///  * Right Thumb vertical -> Right rope tension (lead foot).
    ///
    /// Horizontal inputs (unified symmetric coupling):
    ///  * xL = horizontal displacement of Left Thumb (-1..1)
    ///  * xR = horizontal displacement of Right Thumb (-1..1)
    ///
    ///  DepthInput    = (xL + xR) / 2  (common-mode movement: INWARD / OUTWARD lean)
    ///  SwordArmInput = (xR - xL) / 2  (differential-mode movement: SWORD ARM control)
    ///
    /// Desktop debug bindings:
    ///   A / L / Space = Left / Right / Both ropes
    ///   Q / E         = Inward / Outward depth lean
    ///   J / K         = Sword arm retract / thrust
    /// </summary>
    [RequireComponent(typeof(PuppetRopeController))]
    public sealed class PuppetRopeInput : MonoBehaviour
    {
        [Header("Vertical drag -> rope tension")]
        [Min(20f)] public float dragFullPixels = 220f;

        [Header("Horizontal drag -> Depth + Sword Arm (Unified 2-Thumb)")]
        [Min(20f)] public float horizontalFullPixels = 160f;
        [Min(0f)] public float horizontalDeadzonePixels = 16f;

        [Header("Arm Response (units per second)")]
        [Min(0.1f)] public float armSpeed = 16f;
        [Min(0.1f)] public float armReturnSpeed = 5.5f;

        [Header("Response (units per second)")]
        [Min(0.1f)] public float tensionRiseSpeed = 12f;
        [Min(0.1f)] public float tensionFallSpeed = 10f;
        [Min(0.1f)] public float depthSpeed = 9f;

        [Header("Keyboard test bindings (desktop debug)")]
        public Key leftKey = Key.A;
        public Key rightKey = Key.L;
        public Key bothKey = Key.Space;
        public Key inwardKey = Key.Q;
        public Key outwardKey = Key.E;
        public Key armRetractKey = Key.J;
        public Key armThrustKey = Key.K;

        // read-only for the HUD & debugging
        public float LeftTarget { get; private set; }
        public float RightTarget { get; private set; }
        public float LeftValue { get; private set; }
        public float RightValue { get; private set; }
        public float LeftHorizontal { get; private set; }
        public float RightHorizontal { get; private set; }
        public float DepthTarget { get; private set; }
        public float DepthValue { get; private set; }
        public float RightArmTarget { get; private set; }
        public float RightArmValue { get; private set; }
        public float RightArmVelocity { get; private set; }

        PuppetRopeController _controller;

        // mouse: one zone at a time (desktop)
        bool _mouseActive;
        Vector2 _mouseStart;
        Vector2 _mousePrev;
        float _mousePrevTime;
        bool _mouseLeftZone;

        struct DragOrigin
        {
            public Vector2 start;
            public Vector2 prevPos;
            public float prevTime;
            public bool leftZone;
        }
        readonly Dictionary<int, DragOrigin> _touches = new();

        void Awake() => _controller = GetComponent<PuppetRopeController>();
        void OnEnable() => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void Update()
        {
            float dt = Time.deltaTime;
            float now = Time.time;
            float halfW = Screen.width * 0.5f;

            float targetLeft = 0f, targetRight = 0f;
            float xL = 0f, xR = 0f;
            int countL = 0, countR = 0;
            float vxL = 0f, vxR = 0f;

            // ---- keyboard (desktop debug) ----
            var kb = Keyboard.current;
            float kbDepth = 0f;
            float kbArm = 0f;
            float kbArmVel = 0f;
            if (kb != null)
            {
                if (kb[bothKey].isPressed) { targetLeft = 1f; targetRight = 1f; }
                if (kb[leftKey].isPressed) targetLeft = 1f;
                if (kb[rightKey].isPressed) targetRight = 1f;

                if (kb[outwardKey].isPressed) kbDepth += 1f;
                if (kb[inwardKey].isPressed) kbDepth -= 1f;

                if (kb[armThrustKey].isPressed) { kbArm += 1f; kbArmVel = 5f; }
                if (kb[armRetractKey].isPressed) { kbArm -= 1f; kbArmVel = -5f; }
            }

            // ---- mouse (desktop, one zone at a time) ----
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 mp = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    _mouseActive = true;
                    _mouseStart = mp;
                    _mousePrev = mp;
                    _mousePrevTime = now;
                    _mouseLeftZone = mp.x < halfW;
                }
                else if (!mouse.leftButton.isPressed)
                {
                    _mouseActive = false;
                }

                if (_mouseActive)
                {
                    float t = Mathf.Clamp01((_mouseStart.y - mp.y) / dragFullPixels);
                    float deltaT = Mathf.Max(0.001f, now - _mousePrevTime);
                    float deltaX = mp.x - _mouseStart.x;
                    float normX = NormalizeHorizontal(deltaX);
                    float vx = (mp.x - _mousePrev.x) / deltaT / horizontalFullPixels;

                    if (_mouseLeftZone)
                    {
                        targetLeft = Mathf.Max(targetLeft, t);
                        xL += normX;
                        countL++;
                        vxL = vx;
                    }
                    else
                    {
                        targetRight = Mathf.Max(targetRight, t);
                        xR += normX;
                        countR++;
                        vxR = vx;
                    }
                    _mousePrev = mp;
                    _mousePrevTime = now;
                }
            }

            // ---- multitouch (mobile 2-thumb) ----
            foreach (var touch in ETouch.Touch.activeTouches)
            {
                switch (touch.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        _touches[touch.touchId] = new DragOrigin
                        {
                            start = touch.screenPosition,
                            prevPos = touch.screenPosition,
                            prevTime = now,
                            leftZone = touch.screenPosition.x < halfW,
                        };
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        if (_touches.TryGetValue(touch.touchId, out var o))
                        {
                            float t = Mathf.Clamp01((o.start.y - touch.screenPosition.y) / dragFullPixels);
                            float deltaT = Mathf.Max(0.001f, now - o.prevTime);
                            float deltaX = touch.screenPosition.x - o.start.x;
                            float normX = NormalizeHorizontal(deltaX);
                            float vx = (touch.screenPosition.x - o.prevPos.x) / deltaT / horizontalFullPixels;

                            if (o.leftZone)
                            {
                                targetLeft = Mathf.Max(targetLeft, t);
                                xL += normX;
                                countL++;
                                vxL = vx;
                            }
                            else
                            {
                                targetRight = Mathf.Max(targetRight, t);
                                xR += normX;
                                countR++;
                                vxR = vx;
                            }

                            _touches[touch.touchId] = new DragOrigin
                            {
                                start = o.start,
                                prevPos = touch.screenPosition,
                                prevTime = now,
                                leftZone = o.leftZone,
                            };
                        }
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        _touches.Remove(touch.touchId);
                        break;
                }
            }

            // Average touches per zone if multiple
            float finalXL = countL > 0 ? xL / countL : 0f;
            float finalXR = countR > 0 ? xR / countR : 0f;

            LeftHorizontal = finalXL;
            RightHorizontal = finalXR;

            // Unified 2-Thumb Coupling:
            // DepthInput    = (xL + xR) / 2
            // SwordArmInput = (xR - xL) / 2
            float depthFromThumbs = 0.5f * (finalXL + finalXR);
            float armFromThumbs = 0.5f * (finalXR - finalXL);
            float measuredArmVel = 0.5f * (vxR - vxL);

            // Combine with desktop debug keys (Q/E for depth, J/K for sword arm)
            float depthTarget = Mathf.Clamp(depthFromThumbs + kbDepth, -1f, 1f);
            float armTarget = Mathf.Clamp(armFromThumbs + kbArm, -1f, 1f);
            if (Mathf.Abs(kbArmVel) > 0.1f)
                measuredArmVel = kbArmVel;

            LeftTarget = targetLeft;
            RightTarget = targetRight;
            DepthTarget = depthTarget;
            RightArmTarget = armTarget;

            LeftValue = MoveToward(LeftValue, targetLeft, dt, tensionRiseSpeed, tensionFallSpeed);
            RightValue = MoveToward(RightValue, targetRight, dt, tensionRiseSpeed, tensionFallSpeed);
            DepthValue = Mathf.MoveTowards(DepthValue, DepthTarget, depthSpeed * dt);

            // Arm return has natural decay on release so momentum/inertia carries through
            if (Mathf.Abs(armTarget) > 0.001f)
            {
                RightArmValue = Mathf.MoveTowards(RightArmValue, armTarget, armSpeed * dt);
                RightArmVelocity = Mathf.Lerp(RightArmVelocity, measuredArmVel, 30f * dt);
            }
            else
            {
                RightArmValue = Mathf.MoveTowards(RightArmValue, 0f, armReturnSpeed * dt);
                RightArmVelocity = Mathf.MoveTowards(RightArmVelocity, 0f, 10f * dt);
            }

            _controller.SetInput(LeftValue, RightValue, DepthValue, RightArmValue, RightArmVelocity);
        }

        float NormalizeHorizontal(float pixels)
        {
            float sign = Mathf.Sign(pixels);
            float mag = Mathf.Max(0f, Mathf.Abs(pixels) - horizontalDeadzonePixels);
            return Mathf.Clamp(sign * mag / horizontalFullPixels, -1f, 1f);
        }

        static float MoveToward(float current, float target, float dt, float rise, float fall)
        {
            float speed = target > current ? rise : fall;
            return Mathf.MoveTowards(current, target, speed * dt);
        }
    }
}


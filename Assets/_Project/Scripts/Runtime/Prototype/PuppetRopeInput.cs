using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1.x prototype input: 2-thumb control scheme.
    ///
    /// Left Thumb:
    ///  * VERTICAL drag   -> Left rope tension (rear foot).
    ///  * HORIZONTAL drag -> Depth lean (inward / outward, Q/E).
    ///
    /// Right Thumb:
    ///  * VERTICAL drag   -> Right rope tension (lead foot).
    ///  * HORIZONTAL drag -> Sword arm combat control (thrust forward / pull back / slash).
    ///  * SWIPE VELOCITY  -> Dynamic swing momentum & follow-through inertia.
    ///
    /// Desktop test:
    ///   A = Left rope, L = Right rope, Space = Both ropes;
    ///   Q = Inward, E = Outward (depth);
    ///   J = Sword arm retract (-1), K = Sword arm thrust / slash (+1).
    /// </summary>
    [RequireComponent(typeof(PuppetRopeController))]
    public sealed class PuppetRopeInput : MonoBehaviour
    {
        [Header("Vertical drag -> rope tension")]
        [Min(20f)] public float dragFullPixels = 220f;

        [Header("Left thumb horizontal -> depth (inward / outward)")]
        [Min(20f)] public float depthFullPixels = 170f;
        [Min(0f)] public float depthDeadzonePixels = 16f;

        [Header("Right thumb horizontal -> Arm combat control")]
        [Min(20f)] public float armFullPixels = 150f;
        [Min(0f)] public float armDeadzonePixels = 14f;
        [Min(0.1f)] public float armSpeed = 16f;
        [Min(0.1f)] public float armReturnSpeed = 5.5f;

        [Header("Response (units per second)")]
        [Min(0.1f)] public float tensionRiseSpeed = 12f;
        [Min(0.1f)] public float tensionFallSpeed = 10f;
        [Min(0.1f)] public float depthSpeed = 9f;

        [Header("Keyboard test bindings (desktop)")]
        public Key leftKey = Key.A;
        public Key rightKey = Key.L;
        public Key bothKey = Key.Space;
        public Key inwardKey = Key.Q;
        public Key outwardKey = Key.E;
        public Key armRetractKey = Key.J;
        public Key armThrustKey = Key.K;

        // read-only for the HUD
        public float LeftTarget { get; private set; }
        public float RightTarget { get; private set; }
        public float LeftValue { get; private set; }
        public float RightValue { get; private set; }
        public float DepthTarget { get; private set; }
        public float DepthValue { get; private set; }
        public float RightArmTarget { get; private set; }
        public float RightArmValue { get; private set; }
        public float RightArmVelocity { get; private set; }

        PuppetRopeController _controller;

        // mouse: one zone at a time
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
            float depthContribution = 0f;
            int depthCount = 0;
            float armContribution = 0f;
            int armCount = 0;
            float measuredArmVel = 0f;

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
                    depthContribution += kbDepth;
                    depthCount++;
                }

                float kbArm = 0f;
                if (kb[armThrustKey].isPressed) { kbArm += 1f; measuredArmVel = 5f; }
                if (kb[armRetractKey].isPressed) { kbArm -= 1f; measuredArmVel = -5f; }
                if (kbArm != 0f)
                {
                    armContribution += kbArm;
                    armCount++;
                }
            }

            // ---- mouse (desktop, one zone) ----
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

                    if (_mouseLeftZone)
                    {
                        targetLeft = Mathf.Max(targetLeft, t);
                        depthContribution += HorizontalToDepth(deltaX);
                        depthCount++;
                    }
                    else
                    {
                        targetRight = Mathf.Max(targetRight, t);
                        armContribution += HorizontalToArm(deltaX);
                        armCount++;
                        float vx = (mp.x - _mousePrev.x) / deltaT / armFullPixels;
                        measuredArmVel = vx;
                    }
                    _mousePrev = mp;
                    _mousePrevTime = now;
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

                            if (o.leftZone)
                            {
                                targetLeft = Mathf.Max(targetLeft, t);
                                depthContribution += HorizontalToDepth(deltaX);
                                depthCount++;
                            }
                            else
                            {
                                targetRight = Mathf.Max(targetRight, t);
                                armContribution += HorizontalToArm(deltaX);
                                armCount++;
                                float vx = (touch.screenPosition.x - o.prevPos.x) / deltaT / armFullPixels;
                                measuredArmVel = vx;
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

            // Depth from Left Thumb horizontal
            float depthTarget = depthCount > 0 ? Mathf.Clamp(depthContribution / depthCount, -1f, 1f) : 0f;

            // Arm from Right Thumb horizontal
            float armTarget = armCount > 0 ? Mathf.Clamp(armContribution / armCount, -1f, 1f) : 0f;

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

        float HorizontalToDepth(float pixels)
        {
            float sign = Mathf.Sign(pixels);
            float mag = Mathf.Max(0f, Mathf.Abs(pixels) - depthDeadzonePixels);
            return Mathf.Clamp(sign * mag / depthFullPixels, -1f, 1f);
        }

        float HorizontalToArm(float pixels)
        {
            float sign = Mathf.Sign(pixels);
            float mag = Mathf.Max(0f, Mathf.Abs(pixels) - armDeadzonePixels);
            return Mathf.Clamp(sign * mag / armFullPixels, -1f, 1f);
        }

        static float MoveToward(float current, float target, float dt, float rise, float fall)
        {
            float speed = target > current ? rise : fall;
            return Mathf.MoveTowards(current, target, speed * dt);
        }
    }
}


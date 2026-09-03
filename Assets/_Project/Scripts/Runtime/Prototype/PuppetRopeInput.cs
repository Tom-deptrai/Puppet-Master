using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1 prototype input. Produces two independent normalized values
    /// (0 = slack, 1 = taut) from two on-screen control zones — left half and
    /// right half — and feeds them to <see cref="PuppetRopeController"/>.
    ///
    ///  * Mobile   : multitouch. Each finger drives the zone it started in;
    ///               drag DOWN from where you pressed = pull the rope taut.
    ///  * Desktop  : left mouse drag inside a zone (one zone at a time), plus
    ///               keyboard for testing both ropes at once (A / L, Space = both).
    ///
    /// This is deliberately NOT a virtual joystick. It is pull / hold / release.
    /// </summary>
    [RequireComponent(typeof(PuppetRopeController))]
    public sealed class PuppetRopeInput : MonoBehaviour
    {
        [Header("Drag mapping")]
        [Tooltip("Pixels of downward drag inside a zone that equals full tension.")]
        [Min(20f)] public float dragFullPixels = 230f;

        [Header("Response (tension units per second)")]
        [Min(0.1f)] public float riseSpeed = 3.6f;
        [Min(0.1f)] public float fallSpeed = 2.3f;

        [Header("Keyboard test bindings (desktop)")]
        public Key leftKey = Key.A;
        public Key rightKey = Key.L;
        public Key bothKey = Key.Space;

        // read-only for the HUD
        public float LeftTarget { get; private set; }
        public float RightTarget { get; private set; }
        public float LeftValue { get; private set; }
        public float RightValue { get; private set; }

        PuppetRopeController _controller;

        bool _mouseActive;
        float _mouseStartY;
        bool _mouseLeftZone;

        struct DragOrigin { public float startY; public bool leftZone; }
        readonly Dictionary<int, DragOrigin> _touches = new();

        void Awake() => _controller = GetComponent<PuppetRopeController>();

        void OnEnable() => EnhancedTouchSupport.Enable();
        void OnDisable() => EnhancedTouchSupport.Disable();

        void Update()
        {
            float dt = Time.deltaTime;
            float targetLeft = 0f, targetRight = 0f;
            float halfW = Screen.width * 0.5f;

            // ---- keyboard (desktop: lets one tester drive both ropes) ----
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb[bothKey].isPressed) { targetLeft = 1f; targetRight = 1f; }
                if (kb[leftKey].isPressed) targetLeft = 1f;
                if (kb[rightKey].isPressed) targetRight = 1f;
            }

            // ---- mouse (desktop: one zone at a time) ----
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 mp = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    _mouseActive = true;
                    _mouseStartY = mp.y;
                    _mouseLeftZone = mp.x < halfW;
                }
                else if (!mouse.leftButton.isPressed)
                {
                    _mouseActive = false;
                }

                if (_mouseActive)
                {
                    float drag = Mathf.Clamp01((_mouseStartY - mp.y) / dragFullPixels);
                    if (_mouseLeftZone) targetLeft = Mathf.Max(targetLeft, drag);
                    else targetRight = Mathf.Max(targetRight, drag);
                }
            }

            // ---- multitouch (mobile: two independent fingers) ----
            foreach (var touch in ETouch.Touch.activeTouches)
            {
                switch (touch.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        _touches[touch.touchId] = new DragOrigin
                        {
                            startY = touch.screenPosition.y,
                            leftZone = touch.screenPosition.x < halfW,
                        };
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                        if (_touches.TryGetValue(touch.touchId, out var origin))
                        {
                            float drag = Mathf.Clamp01((origin.startY - touch.screenPosition.y) / dragFullPixels);
                            if (origin.leftZone) targetLeft = Mathf.Max(targetLeft, drag);
                            else targetRight = Mathf.Max(targetRight, drag);
                        }
                        break;

                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        _touches.Remove(touch.touchId);
                        break;
                }
            }

            LeftTarget = targetLeft;
            RightTarget = targetRight;
            LeftValue = Step(LeftValue, targetLeft, dt);
            RightValue = Step(RightValue, targetRight, dt);
            _controller.SetInput(LeftValue, RightValue);
        }

        float Step(float current, float target, float dt)
        {
            float speed = target > current ? riseSpeed : fallSpeed;
            return Mathf.MoveTowards(current, target, speed * dt);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Unified 2-thumb control for mobile + desktop debug.
    ///
    /// Vertical (per thumb, independent):
    ///   Left half  → Left rope tension
    ///   Right half → Right rope tension
    ///
    /// Horizontal (screen-normalized displacement from touch origin):
    ///   DepthInput    = (xL + xR) / 2
    ///   SwordArmInput = (xR - xL) / 2
    ///
    /// Finger ownership is locked at Touch Began (half-screen zone) until Ended —
    /// crossing the midline mid-drag does not reassign the thumb.
    ///
    /// Desktop: A/L/Space ropes, Q/E depth, J/K sword arm. Mouse = one zone at a time.
    /// </summary>
    [RequireComponent(typeof(PuppetRopeController))]
    public sealed class PuppetRopeInput : MonoBehaviour
    {
        [Header("Vertical drag → rope tension (screen-relative)")]
        [Tooltip("Fraction of screen height dragged DOWN for full tension (1.0).")]
        [Range(0.10f, 0.60f)] public float verticalFullScreenFraction = 0.30f;

        [Header("Horizontal drag → Depth + Sword Arm (screen-relative)")]
        [Tooltip("Fraction of screen width for full |horizontal| = 1.0.")]
        [Range(0.08f, 0.45f)] public float horizontalFullScreenFraction = 0.18f;
        [Tooltip("Fraction of screen width ignored as horizontal noise.")]
        [Range(0f, 0.08f)] public float horizontalDeadzoneScreenFraction = 0.020f;

        [Header("Arm Response (units per second)")]
        [Min(0.1f)] public float armSpeed = 24f;
        [Min(0.1f)] public float armReturnSpeed = 8f;

        [Header("Response (units per second)")]
        [Min(0.1f)] public float tensionRiseSpeed = 18f;
        [Min(0.1f)] public float tensionFallSpeed = 15f;
        [Min(0.1f)] public float depthSpeed = 13.5f;

        [Header("Keyboard test bindings (desktop debug)")]
        public Key leftKey = Key.A;
        public Key rightKey = Key.L;
        public Key bothKey = Key.Space;
        public Key inwardKey = Key.Q;
        public Key outwardKey = Key.E;
        public Key armRetractKey = Key.J;
        public Key armThrustKey = Key.K;

        [Header("Touch debug overlay")]
        public bool showTouchDebug = true;

        // ---- smoothed gameplay values ----
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

        // ---- mobile touch debug readouts ----
        public bool LeftThumbActive { get; private set; }
        public bool RightThumbActive { get; private set; }
        public Vector2 LeftThumbScreenPos { get; private set; }
        public Vector2 RightThumbScreenPos { get; private set; }
        public float LeftThumbTension { get; private set; }
        public float RightThumbTension { get; private set; }
        public float LeftThumbHorizontal { get; private set; }
        public float RightThumbHorizontal { get; private set; }
        public float VerticalFullPixels { get; private set; }
        public float HorizontalFullPixels { get; private set; }
        public float HorizontalDeadzonePixels { get; private set; }

        PuppetRopeController _controller;

        bool _mouseActive;
        Vector2 _mouseStart;
        Vector2 _mousePrev;
        float _mousePrevTime;
        bool _mouseLeftZone;

        struct ThumbDrag
        {
            public Vector2 start;
            public Vector2 prevPos;
            public float prevTime;
            public bool leftZone;
            public int touchId;
            public bool active;
        }

        // Exactly one owned thumb per half-screen. Locked at Began.
        ThumbDrag _leftThumb;
        ThumbDrag _rightThumb;

        // Legacy id map kept only to reject stray Began for already-tracked ids.
        readonly HashSet<int> _ownedIds = new();

        Texture2D _px;
        GUIStyle _touchLabel;

        void Awake() => _controller = GetComponent<PuppetRopeController>();

        void OnEnable() => EnhancedTouchSupport.Enable();

        void OnDisable()
        {
            EnhancedTouchSupport.Disable();
            ClearThumbs();
        }

        void OnDestroy()
        {
            if (_px != null) Destroy(_px);
        }

        void ClearThumbs()
        {
            _leftThumb = default;
            _rightThumb = default;
            _ownedIds.Clear();
            LeftThumbActive = RightThumbActive = false;
        }

        void Update()
        {
            if (_controller != null && _controller.IsKO)
                return;

            float dt = Time.deltaTime;
            float now = Time.time;
            float halfW = Screen.width * 0.5f;

            // Screen-relative scales so iPhone sizes feel consistent.
            VerticalFullPixels = Mathf.Max(80f, Screen.height * verticalFullScreenFraction);
            HorizontalFullPixels = Mathf.Max(60f, Screen.width * horizontalFullScreenFraction);
            HorizontalDeadzonePixels = Mathf.Max(8f, Screen.width * horizontalDeadzoneScreenFraction);

            float targetLeft = 0f, targetRight = 0f;
            float xL = 0f, xR = 0f;
            float vxL = 0f, vxR = 0f;
            bool touchLeft = false, touchRight = false;

            // ---- keyboard (desktop debug) — additive with touch ----
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

            // ---- mouse (desktop only; ignored when a real touchscreen is driving) ----
            bool touchscreenActive = Touchscreen.current != null &&
                                     ETouch.Touch.activeTouches.Count > 0;
            var mouse = Mouse.current;
            if (mouse != null && !touchscreenActive)
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
                    float t = TensionFromDrag(_mouseStart.y, mp.y);
                    float deltaT = Mathf.Max(0.001f, now - _mousePrevTime);
                    float normX = NormalizeHorizontal(mp.x - _mouseStart.x);
                    float vx = (mp.x - _mousePrev.x) / deltaT / HorizontalFullPixels;

                    if (_mouseLeftZone)
                    {
                        targetLeft = Mathf.Max(targetLeft, t);
                        xL = normX;
                        vxL = vx;
                        touchLeft = true;
                        LeftThumbScreenPos = mp;
                        LeftThumbTension = t;
                        LeftThumbHorizontal = normX;
                    }
                    else
                    {
                        targetRight = Mathf.Max(targetRight, t);
                        xR = normX;
                        vxR = vx;
                        touchRight = true;
                        RightThumbScreenPos = mp;
                        RightThumbTension = t;
                        RightThumbHorizontal = normX;
                    }
                    _mousePrev = mp;
                    _mousePrevTime = now;
                }
            }

            // ---- multitouch (mobile 2-thumb) ----
            PruneEndedTouches();
            ProcessEnhancedTouches(now, halfW,
                ref targetLeft, ref targetRight,
                ref xL, ref xR, ref vxL, ref vxR,
                ref touchLeft, ref touchRight);

            LeftThumbActive = touchLeft || _leftThumb.active;
            RightThumbActive = touchRight || _rightThumb.active;

            if (!LeftThumbActive)
            {
                LeftThumbTension = 0f;
                LeftThumbHorizontal = 0f;
            }
            if (!RightThumbActive)
            {
                RightThumbTension = 0f;
                RightThumbHorizontal = 0f;
            }

            LeftHorizontal = xL;
            RightHorizontal = xR;

            float depthFromThumbs = 0.5f * (xL + xR);
            float armFromThumbs = 0.5f * (xR - xL);
            float measuredArmVel = 0.5f * (vxR - vxL);

            float depthTarget = Mathf.Clamp(depthFromThumbs + kbDepth, -1f, 1f);
            float armTarget = Mathf.Clamp(armFromThumbs + kbArm, -1f, 1f);
            if (Mathf.Abs(kbArmVel) > 0.1f)
                measuredArmVel = kbArmVel;

            LeftTarget = targetLeft;
            RightTarget = targetRight;
            DepthTarget = depthTarget;
            RightArmTarget = armTarget;

            // Smooth rise/fall — no snap on press or release.
            LeftValue = MoveToward(LeftValue, targetLeft, dt, tensionRiseSpeed, tensionFallSpeed);
            RightValue = MoveToward(RightValue, targetRight, dt, tensionRiseSpeed, tensionFallSpeed);
            DepthValue = Mathf.MoveTowards(DepthValue, DepthTarget, depthSpeed * dt);

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

        void ProcessEnhancedTouches(
            float now, float halfW,
            ref float targetLeft, ref float targetRight,
            ref float xL, ref float xR, ref float vxL, ref float vxR,
            ref bool touchLeft, ref bool touchRight)
        {
            foreach (var touch in ETouch.Touch.activeTouches)
            {
                int id = touch.touchId;
                Vector2 pos = touch.screenPosition;

                switch (touch.phase)
                {
                    case UnityEngine.InputSystem.TouchPhase.Began:
                    {
                        bool wantLeft = pos.x < halfW;
                        // Zone already owned by another finger → ignore this Began.
                        if (wantLeft && _leftThumb.active) break;
                        if (!wantLeft && _rightThumb.active) break;
                        if (_ownedIds.Contains(id)) break;

                        var drag = new ThumbDrag
                        {
                            start = pos,
                            prevPos = pos,
                            prevTime = now,
                            leftZone = wantLeft,
                            touchId = id,
                            active = true,
                        };
                        if (wantLeft) _leftThumb = drag;
                        else _rightThumb = drag;
                        _ownedIds.Add(id);

                        // Register immediately (tension 0 until drag down).
                        ApplyThumbSample(ref drag, pos, now,
                            ref targetLeft, ref targetRight,
                            ref xL, ref xR, ref vxL, ref vxR,
                            ref touchLeft, ref touchRight);
                        if (wantLeft) _leftThumb = drag;
                        else _rightThumb = drag;
                        break;
                    }

                    case UnityEngine.InputSystem.TouchPhase.Moved:
                    case UnityEngine.InputSystem.TouchPhase.Stationary:
                    {
                        if (_leftThumb.active && _leftThumb.touchId == id)
                        {
                            var drag = _leftThumb;
                            ApplyThumbSample(ref drag, pos, now,
                                ref targetLeft, ref targetRight,
                                ref xL, ref xR, ref vxL, ref vxR,
                                ref touchLeft, ref touchRight);
                            _leftThumb = drag;
                        }
                        else if (_rightThumb.active && _rightThumb.touchId == id)
                        {
                            var drag = _rightThumb;
                            ApplyThumbSample(ref drag, pos, now,
                                ref targetLeft, ref targetRight,
                                ref xL, ref xR, ref vxL, ref vxR,
                                ref touchLeft, ref touchRight);
                            _rightThumb = drag;
                        }
                        // Unknown id mid-gesture: do NOT steal ownership by current X.
                        break;
                    }

                    case UnityEngine.InputSystem.TouchPhase.Ended:
                    case UnityEngine.InputSystem.TouchPhase.Canceled:
                        ReleaseTouchId(id);
                        break;
                }
            }
        }

        void ApplyThumbSample(
            ref ThumbDrag drag, Vector2 pos, float now,
            ref float targetLeft, ref float targetRight,
            ref float xL, ref float xR, ref float vxL, ref float vxR,
            ref bool touchLeft, ref bool touchRight)
        {
            float t = TensionFromDrag(drag.start.y, pos.y);
            float deltaT = Mathf.Max(0.001f, now - drag.prevTime);
            float normX = NormalizeHorizontal(pos.x - drag.start.x);
            float vx = (pos.x - drag.prevPos.x) / deltaT / HorizontalFullPixels;

            // Ownership uses drag.leftZone from Began — never re-evaluate by current X.
            if (drag.leftZone)
            {
                targetLeft = Mathf.Max(targetLeft, t);
                xL = normX;
                vxL = vx;
                touchLeft = true;
                LeftThumbScreenPos = pos;
                LeftThumbTension = t;
                LeftThumbHorizontal = normX;
            }
            else
            {
                targetRight = Mathf.Max(targetRight, t);
                xR = normX;
                vxR = vx;
                touchRight = true;
                RightThumbScreenPos = pos;
                RightThumbTension = t;
                RightThumbHorizontal = normX;
            }

            drag.prevPos = pos;
            drag.prevTime = now;
        }

        void PruneEndedTouches()
        {
            // Safety: if EnhancedTouch dropped an Ended event, clear orphans.
            if (_leftThumb.active && !TouchStillActive(_leftThumb.touchId))
                ReleaseTouchId(_leftThumb.touchId);
            if (_rightThumb.active && !TouchStillActive(_rightThumb.touchId))
                ReleaseTouchId(_rightThumb.touchId);
        }

        static bool TouchStillActive(int id)
        {
            foreach (var t in ETouch.Touch.activeTouches)
                if (t.touchId == id &&
                    t.phase != UnityEngine.InputSystem.TouchPhase.Ended &&
                    t.phase != UnityEngine.InputSystem.TouchPhase.Canceled)
                    return true;
            return false;
        }

        void ReleaseTouchId(int id)
        {
            if (_leftThumb.active && _leftThumb.touchId == id)
                _leftThumb = default;
            if (_rightThumb.active && _rightThumb.touchId == id)
                _rightThumb = default;
            _ownedIds.Remove(id);
        }

        float TensionFromDrag(float startY, float currentY)
        {
            // Drag DOWN from start = pull rope taut.
            return Mathf.Clamp01((startY - currentY) / VerticalFullPixels);
        }

        float NormalizeHorizontal(float pixels)
        {
            float sign = Mathf.Sign(pixels);
            float mag = Mathf.Max(0f, Mathf.Abs(pixels) - HorizontalDeadzonePixels);
            return Mathf.Clamp(sign * mag / HorizontalFullPixels, -1f, 1f);
        }

        static float MoveToward(float current, float target, float dt, float rise, float fall)
        {
            float speed = target > current ? rise : fall;
            return Mathf.MoveTowards(current, target, speed * dt);
        }

        void OnGUI()
        {
            if (!showTouchDebug) return;
            EnsureTouchStyles();

            float w = Screen.width;
            float h = Screen.height;
            float panelH = 72f;
            float y = h - panelH - 8f;
            float half = w * 0.5f;

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(8f, y, half - 16f, panelH), _px);
            GUI.DrawTexture(new Rect(half + 8f, y, half - 16f, panelH), _px);
            GUI.color = Color.white;

            string leftPos = LeftThumbActive
                ? $"{LeftThumbScreenPos.x:0},{LeftThumbScreenPos.y:0}"
                : "—";
            string rightPos = RightThumbActive
                ? $"{RightThumbScreenPos.x:0},{RightThumbScreenPos.y:0}"
                : "—";

            GUI.Label(new Rect(16f, y + 4f, half - 32f, panelH - 8f),
                $"LEFT THUMB {(LeftThumbActive ? "ON" : "off")}\n" +
                $"pos {leftPos}\n" +
                $"tension {LeftThumbTension:0.00}   horiz {LeftThumbHorizontal:+0.00;-0.00;0.00}",
                _touchLabel);

            GUI.Label(new Rect(half + 16f, y + 4f, half - 32f, panelH - 8f),
                $"RIGHT THUMB {(RightThumbActive ? "ON" : "off")}\n" +
                $"pos {rightPos}\n" +
                $"tension {RightThumbTension:0.00}   horiz {RightThumbHorizontal:+0.00;-0.00;0.00}",
                _touchLabel);
        }

        void EnsureTouchStyles()
        {
            if (_px == null)
            {
                _px = new Texture2D(1, 1);
                _px.SetPixel(0, 0, Color.white);
                _px.Apply();
            }
            if (_touchLabel == null)
            {
                _touchLabel = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    richText = false,
                    alignment = TextAnchor.UpperLeft,
                };
                _touchLabel.normal.textColor = Color.white;
            }
        }
    }
}

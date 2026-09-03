using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1 prototype. Draws one control rope from its overhead pulley down to
    /// the foot it controls, so the two ropes are clearly readable in Scene/Game
    /// view. Taut ropes render straight, thicker and bright; slack ropes sag and
    /// dim so the player can see slack at a glance.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class RopeVisual : MonoBehaviour
    {
        public PuppetRopeController controller;
        public Transform pulley;
        public Transform footAttach;
        [Tooltip("True = this rope shows the LEFT tension, false = RIGHT.")]
        public bool isLeft = true;

        [Min(2)] public int segments = 16;
        [Tooltip("Maximum mid-rope droop (metres) when fully slack.")]
        public float slackSag = 0.6f;
        public float tautWidth = 0.018f;
        public float slackWidth = 0.010f;
        public Color tautColor = new(1f, 0.92f, 0.5f);
        public Color slackColor = new(0.42f, 0.44f, 0.48f);

        LineRenderer _lr;

        void Awake()
        {
            _lr = GetComponent<LineRenderer>();
            _lr.useWorldSpace = true;
            _lr.positionCount = segments + 1;
        }

        void LateUpdate()
        {
            if (pulley == null || footAttach == null) return;
            if (_lr.positionCount != segments + 1) _lr.positionCount = segments + 1;

            float tension = controller == null
                ? 1f
                : Mathf.Clamp01(isLeft ? controller.LeftTension : controller.RightTension);

            float sag = slackSag * (1f - tension);
            Vector3 a = pulley.position;
            Vector3 b = footAttach.position;

            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                Vector3 p = Vector3.Lerp(a, b, u);
                p.y -= sag * 4f * u * (1f - u); // parabola: 0 at both ends, max at middle
                _lr.SetPosition(i, p);
            }

            _lr.widthMultiplier = Mathf.Lerp(slackWidth, tautWidth, tension);
            Color c = Color.Lerp(slackColor, tautColor, tension);
            _lr.startColor = c;
            _lr.endColor = c;
        }
    }
}

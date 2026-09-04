using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1.2 prototype. The rope no longer runs UP — nothing sits above the
    /// puppet. Path:  FOOT -> RAIL SLOT -> below the rail -> down toward the
    /// player's thumb (off the bottom of the view). Reads like a string the
    /// player is pulling from underneath the screen.
    ///
    /// Purely cosmetic — the gameplay force is applied separately by
    /// <see cref="PuppetRopeController"/>. Taut = straight/bright/thick,
    /// slack = sagging/dim/thin. Rendered on top so the ground never hides it.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class RopeVisual : MonoBehaviour
    {
        public PuppetRopeController controller;
        public Transform footAttach;
        public Transform railSlot;
        public Transform belowRail;
        [Tooltip("True = LEFT rope (Foot_L, left thumb zone), false = RIGHT.")]
        public bool isLeft = true;

        [Header("Where the rope continues toward the player (off the bottom of the view)")]
        [Tooltip("Sideways fan of the thumb end (Left rope fans one way, Right the other).")]
        public float thumbFanX = 0.7f;
        public float thumbDrop = 1.5f;
        public float thumbTowardCamera = 2.1f;

        [Header("Look")]
        [Min(3)] public int segmentsPerSpan = 6;
        public float slackSag = 0.45f;
        public float tautWidth = 0.03f;
        public float slackWidth = 0.016f;
        public Color tautColor = new(1f, 0.9f, 0.42f);
        public Color slackColor = new(0.5f, 0.52f, 0.56f);

        LineRenderer _lr;

        void Awake()
        {
            _lr = GetComponent<LineRenderer>();
            _lr.useWorldSpace = true;
        }

        void LateUpdate()
        {
            if (footAttach == null || railSlot == null) return;

            float tension = controller == null ? 1f
                : Mathf.Clamp01(isLeft ? controller.LeftTension : controller.RightTension);

            Vector3 pFoot = footAttach.position;
            Vector3 pSlot = railSlot.position;
            Vector3 pBelow = belowRail != null ? belowRail.position : pSlot + Vector3.down * 0.4f;
            float facing = controller != null ? controller.FacingSign : 1f;
            Vector3 pThumb = pBelow + new Vector3(facing * (isLeft ? -1f : 1f) * thumbFanX, -thumbDrop, -thumbTowardCamera);

            int seg = Mathf.Max(3, segmentsPerSpan);
            _lr.positionCount = seg * 3 + 1;
            int idx = 0;
            WriteSpan(pFoot, pSlot, seg, 0f, ref idx);
            WriteSpan(pSlot, pBelow, seg, 0f, ref idx);
            WriteSpan(pBelow, pThumb, seg, slackSag * (1f - tension), ref idx, lastPoint: true);

            _lr.widthMultiplier = Mathf.Lerp(slackWidth, tautWidth, tension);
            Color c = Color.Lerp(slackColor, tautColor, tension);
            _lr.startColor = c;
            _lr.endColor = c;
        }

        void WriteSpan(Vector3 a, Vector3 b, int seg, float sag, ref int idx, bool lastPoint = false)
        {
            int count = lastPoint ? seg + 1 : seg;
            for (int i = 0; i < count; i++)
            {
                float u = i / (float)seg;
                Vector3 p = Vector3.Lerp(a, b, u);
                p.y -= sag * 4f * u * (1f - u);
                _lr.SetPosition(idx++, p);
            }
        }
    }
}

using System;
using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1.x prototype only — contains NO combat.
    /// Plain reference holder that <c>PuppetPrototypeBuilder</c> fills in so the
    /// runtime scripts can reach every physics part without name lookups.
    ///
    /// One jointed puppet, two feet on a rail, two control ropes. The rope VISUAL
    /// now runs foot -> rail slot -> below the arena -> the player's thumb region
    /// (nothing above the puppet). The rope FORCE is applied at the foot toward a
    /// point just below the rail (planting the foot).
    /// </summary>
    public sealed class PuppetRig : MonoBehaviour
    {
        [Serializable]
        public struct Leg
        {
            public Rigidbody upperLeg;
            public Rigidbody lowerLeg;
            public Rigidbody foot;

            [Tooltip("pelvis -> upperLeg (driven: squat + partial lean)")]
            public ConfigurableJoint hip;
            [Tooltip("upperLeg -> lowerLeg (driven: squat)")]
            public ConfigurableJoint knee;
            [Tooltip("lowerLeg -> foot (driven: squat)")]
            public ConfigurableJoint ankle;
            [Tooltip("foot -> world: the rail constraint (small X slide, everything else locked)")]
            public ConfigurableJoint railJoint;

            [Tooltip("Point on the foot the rope is drawn from / force applied at.")]
            public Transform ropeAttach;
            [Tooltip("Where the rope passes through the rail (visual guide).")]
            public Transform railSlot;
            [Tooltip("A little below the rail — the rope bends here on its way down.")]
            public Transform belowRail;
            [Tooltip("Where the rope FORCE points (just below the rail — plants the foot).")]
            public Transform forceAnchor;

            [Tooltip("This foot's home X on the rail (world), captured by the builder.")]
            public float railHomeX;
        }

        [Header("Layout")]
        public PlayerSide side = PlayerSide.Left;
        [Tooltip("+1 = puppet faces +X (Left side), -1 = faces -X (Right side). Captured by builder.")]
        public float facingSign = 1f;

        [Header("Core bodies")]
        public Rigidbody pelvis;
        public Rigidbody torso;
        public Rigidbody head;

        [Header("Core joints")]
        [Tooltip("pelvis -> torso (driven: full lean follow)")]
        public ConfigurableJoint spine;
        [Tooltip("torso -> head (driven: partial lean follow)")]
        public ConfigurableJoint neck;
        [Tooltip("pelvis -> world: locks depth POSITION + carries the 2-axis lean drive")]
        public ConfigurableJoint pelvisPlaneJoint;

        [Header("Legs")]
        public Leg left;
        public Leg right;

        [Header("Weapon")]
        public Rigidbody sword;

        [Header("Reference values (captured by the builder)")]
        public float standingPelvisHeight = 1.06f;
        public float standingHeadHeight = 1.79f;
        [Tooltip("Foot separation in the authored standing pose.")]
        public float standingFootSeparation = 0.32f;
    }
}

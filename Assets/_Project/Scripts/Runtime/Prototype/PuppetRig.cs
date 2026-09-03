using System;
using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1 / 1.1 prototype only — contains NO combat.
    /// Plain reference holder that <c>PuppetPrototypeBuilder</c> fills in so the
    /// runtime scripts can reach every physics part without name lookups.
    ///
    /// One jointed puppet, two feet constrained to a horizontal rail, two control
    /// ropes. The rope VISUAL runs to an off-centre pulley (mirrorable per side);
    /// the rope FORCE is applied at the foot toward a symmetric overhead anchor.
    /// </summary>
    public sealed class PuppetRig : MonoBehaviour
    {
        [Serializable]
        public struct Leg
        {
            public Rigidbody upperLeg;
            public Rigidbody lowerLeg;
            public Rigidbody foot;

            [Tooltip("pelvis -> upperLeg (driven)")]
            public ConfigurableJoint hip;
            [Tooltip("upperLeg -> lowerLeg (driven)")]
            public ConfigurableJoint knee;
            [Tooltip("lowerLeg -> foot (driven)")]
            public ConfigurableJoint ankle;
            [Tooltip("foot -> world: the rail constraint (small X slide, Y/Z locked)")]
            public ConfigurableJoint railJoint;

            [Tooltip("Where the rope is DRAWN to (off-centre, mirrors per side).")]
            public Transform pulley;
            [Tooltip("Where the rope FORCE points (symmetric, straight above the foot home).")]
            public Transform forceAnchor;
            [Tooltip("Point on the foot the rope force is applied at.")]
            public Transform ropeAttach;

            [Tooltip("This foot's home X on the rail (world), captured by the builder.")]
            public float railHomeX;
        }

        [Header("Layout")]
        public PlayerSide side = PlayerSide.Left;

        [Header("Core bodies")]
        public Rigidbody pelvis;
        public Rigidbody torso;
        public Rigidbody head;

        [Header("Core joints")]
        [Tooltip("pelvis -> torso (driven)")]
        public ConfigurableJoint spine;
        [Tooltip("torso -> head (driven)")]
        public ConfigurableJoint neck;
        [Tooltip("pelvis -> world: keeps depth locked and carries the upright drive")]
        public ConfigurableJoint pelvisPlaneJoint;

        [Header("Legs")]
        public Leg left;
        public Leg right;

        [Header("Reference values (captured by the builder)")]
        public float standingPelvisHeight = 1.06f;
        [Tooltip("Foot separation in the authored standing pose.")]
        public float standingFootSeparation = 0.40f;
    }
}

using System;
using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1 prototype only — contains NO combat.
    /// Plain reference holder that <c>PuppetPrototypeBuilder</c> fills in so the
    /// runtime scripts can reach every physics part without name lookups.
    ///
    /// One jointed puppet, two feet constrained to a horizontal rail, two control
    /// ropes whose gameplay force is applied at the two feet.
    /// </summary>
    public sealed class PuppetRig : MonoBehaviour
    {
        [Serializable]
        public struct Leg
        {
            public Rigidbody upperLeg;
            public Rigidbody lowerLeg;
            public Rigidbody foot;

            [Tooltip("pelvis -> upperLeg (driven by this side's tension)")]
            public ConfigurableJoint hip;
            [Tooltip("upperLeg -> lowerLeg (driven by this side's tension)")]
            public ConfigurableJoint knee;
            [Tooltip("lowerLeg -> foot")]
            public ConfigurableJoint ankle;
            [Tooltip("foot -> world: the rail constraint (slides on X, locked on Y/Z)")]
            public ConfigurableJoint railJoint;

            [Tooltip("Overhead point the rope visually runs from.")]
            public Transform pulley;
            [Tooltip("Point on the foot the rope pull force is applied at.")]
            public Transform ropeAttach;
        }

        [Header("Core bodies")]
        public Rigidbody pelvis;
        public Rigidbody torso;
        public Rigidbody head;

        [Header("Core joints")]
        [Tooltip("pelvis -> torso (driven by combined tension)")]
        public ConfigurableJoint spine;
        [Tooltip("torso -> head (driven by combined tension)")]
        public ConfigurableJoint neck;
        [Tooltip("pelvis -> world: keeps the puppet in the 2.5D screen plane (locks depth)")]
        public ConfigurableJoint pelvisPlaneJoint;

        [Header("Legs")]
        public Leg left;
        public Leg right;

        /// <summary>Approx. standing pelvis height, captured by the builder for the debug HUD.</summary>
        public float standingPelvisHeight = 1.06f;
    }
}

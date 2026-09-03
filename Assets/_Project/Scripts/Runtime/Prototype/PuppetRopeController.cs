using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1.1 prototype. Two normalized tension values (0 = rope slack,
    /// 1 = rope taut) drive a jointed puppet, fully symmetrically:
    ///
    ///   squat depth  = f(combined tension)   -> both legs, identical -> no L/R bias
    ///   lean angle   = f(tension difference) -> pelvis + spine roll, signed
    ///
    ///   both taut  -> stands tall
    ///   both slack  -> deep, coherent crouch (knees forward, feet planted apart)
    ///   one taut     -> half-squat + clear lean toward the taut rope
    ///
    /// No animation. Every pose is ConfigurableJoint slerp drives + a rope pull
    /// applied at each foot. Numbers here are a tuning BASELINE, not a spec.
    /// </summary>
    [RequireComponent(typeof(PuppetRig))]
    public sealed class PuppetRopeController : MonoBehaviour
    {
        [Header("Tension response")]
        [Range(0.3f, 4f)] public float tensionGamma = 1.25f;
        [Min(1f)] public float tensionSmoothing = 14f;

        [Header("Squat — driven by COMBINED tension (symmetric)")]
        [Tooltip("0 = squat depth is purely the combined tension; 1 = purely each leg's own rope.")]
        [Range(0f, 1f)] public float perSideSquatWeight = 0.25f;
        [Tooltip("Knee flexion (deg, about the sagittal axis) at full squat.")]
        public float kneeSquatDeg = 100f;
        public float hipSquatDeg = 62f;
        public float ankleSquatDeg = -40f;
        public float legSquatSlackSpring = 600f;
        public float legSquatStandSpring = 2800f;
        public float legSquatDamper = 120f;
        public float legSquatMaxForce = 22000f;

        [Header("Lean — driven by tension DIFFERENCE (signed, symmetric)")]
        [Tooltip("Degrees of lean when one rope is fully taut and the other fully slack. " +
                 "Positive result = LEFT rope pulls the puppet BACKWARD (away from opponent); " +
                 "negate this field to make the Left rope pull forward instead.")]
        public float leanFromImbalance = 24f;
        [Range(0f, 1f)] public float spineLeanFollow = 0.35f;
        [Range(0f, 1f)] public float neckLeanFollow = 0.2f;
        [Tooltip("How much the hips counter-rotate so the thighs stay vertical while the pelvis leans.")]
        [Range(0f, 1f)] public float hipLeanCounter = 0.5f;

        [Header("Pelvis → world drive (upright + lean)")]
        public float pelvisSlackSpring = 2800f;
        public float pelvisStandSpring = 4600f;
        public float pelvisDamper = 260f;
        public float pelvisMaxForce = 45000f;

        [Header("Spine + neck drive")]
        public float spineSlackSpring = 2200f;
        public float spineStandSpring = 3000f;
        public float spineDamper = 150f;
        public float spineMaxForce = 18000f;

        [Header("Torso balance nudge (small AddTorque assist)")]
        public float torsoUprightAssist = 45f;
        public float torsoUprightDamping = 8f;
        public float maxAssistTorque = 110f;

        [Header("Rope pull — force at each foot toward its (symmetric) overhead anchor")]
        public float ropePull = 40f;

        [Header("Physics solver (runtime only — never touches ProjectSettings)")]
        [Min(1)] public int solverIterations = 26;
        [Min(1)] public int solverVelocityIterations = 14;

        [Header("Tuning override (ignores input while ON — for MCP/inspector testing)")]
        public bool debugOverrideInput;
        [Range(0f, 1f)] public float debugLeft = 1f;
        [Range(0f, 1f)] public float debugRight = 1f;

        // ---- read-only state ----
        public float LeftInput { get; private set; }
        public float RightInput { get; private set; }
        public float LeftTension { get; private set; }
        public float RightTension { get; private set; }
        public float CombinedTension => 0.5f * (LeftTension + RightTension);

        /// <summary>Raw signed torso roll about world Z (debug only — screen space).</summary>
        public float TorsoRollZDeg
        {
            get
            {
                if (_rig == null || _rig.torso == null) return 0f;
                float z = _rig.torso.transform.eulerAngles.z;
                return z > 180f ? z - 360f : z;
            }
        }

        /// <summary>
        /// Signed lean in the FACING frame: positive = leaning toward the opponent
        /// (forward), negative = leaning away (backward). Side-independent.
        /// </summary>
        public float ForwardLeanDeg => -_leanPolarity * TorsoRollZDeg;

        public float FootSeparation
        {
            get
            {
                if (_rig == null || _rig.left.foot == null || _rig.right.foot == null) return 0f;
                return Mathf.Abs(_rig.right.foot.position.x - _rig.left.foot.position.x);
            }
        }

        public float PelvisHeight => _rig != null && _rig.pelvis != null ? _rig.pelvis.position.y : 0f;

        PuppetRig _rig;
        float _leanPolarity = 1f;

        void Awake()
        {
            _rig = GetComponent<PuppetRig>();
            _leanPolarity = _rig.side == PlayerSide.Left ? 1f : -1f;
            Physics.defaultSolverIterations = Mathf.Max(Physics.defaultSolverIterations, solverIterations);
            Physics.defaultSolverVelocityIterations = Mathf.Max(Physics.defaultSolverVelocityIterations, solverVelocityIterations);
            IgnoreAdjacentCollisions();
        }

        /// <summary>
        /// Ignore collisions only between parts that are directly jointed together
        /// (they always overlap at the joint) plus a couple of near neighbours.
        /// Everything else keeps colliding — crucially LowerLeg_L vs LowerLeg_R and
        /// the two feet — so the legs can never pass through each other.
        /// </summary>
        void IgnoreAdjacentCollisions()
        {
            Pair(_rig.pelvis, _rig.torso);
            Pair(_rig.torso, _rig.head);
            IgnoreLegChain(_rig.left);
            IgnoreLegChain(_rig.right);

            var uL = FindPart("UpperArm_L"); var lL = FindPart("LowerArm_L");
            var uR = FindPart("UpperArm_R"); var lR = FindPart("LowerArm_R");
            Pair(_rig.torso, uL); Pair(uL, lL);
            Pair(_rig.torso, uR); Pair(uR, lR);
        }

        void IgnoreLegChain(in PuppetRig.Leg leg)
        {
            Pair(_rig.pelvis, leg.upperLeg);
            Pair(leg.upperLeg, leg.lowerLeg);
            Pair(leg.lowerLeg, leg.foot);
        }

        Rigidbody FindPart(string n)
        {
            foreach (var rb in GetComponentsInChildren<Rigidbody>())
                if (rb.name == n) return rb;
            return null;
        }

        static void Pair(Rigidbody a, Rigidbody b)
        {
            if (a == null || b == null) return;
            foreach (var x in a.GetComponentsInChildren<Collider>())
            foreach (var y in b.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(x, y, true);
        }

        public void SetInput(float left, float right)
        {
            LeftInput = Mathf.Clamp01(left);
            RightInput = Mathf.Clamp01(right);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float kf = 1f - Mathf.Exp(-tensionSmoothing * dt);
            float leftGoal = debugOverrideInput ? debugLeft : LeftInput;
            float rightGoal = debugOverrideInput ? debugRight : RightInput;
            LeftTension = Mathf.Lerp(LeftTension, leftGoal, kf);
            RightTension = Mathf.Lerp(RightTension, rightGoal, kf);
            if (!IsFinite(LeftTension)) LeftTension = 0f;
            if (!IsFinite(RightTension)) RightTension = 0f;

            float l = Shape(LeftTension);
            float r = Shape(RightTension);
            float combined = 0.5f * (l + r);
            float diff = l - r;

            float squatL = Mathf.Lerp(1f - combined, 1f - l, perSideSquatWeight);
            float squatR = Mathf.Lerp(1f - combined, 1f - r, perSideSquatWeight);
            float leanDeg = diff * leanFromImbalance * _leanPolarity;

            DriveLeg(_rig.left, squatL, leanDeg, combined);
            DriveLeg(_rig.right, squatR, leanDeg, combined);

            // pelvis: upright + lean, anchored to the world
            SetDrive(_rig.pelvisPlaneJoint,
                Mathf.Lerp(pelvisSlackSpring, pelvisStandSpring, combined), pelvisDamper, pelvisMaxForce);
            SetTargetLocal(_rig.pelvisPlaneJoint, Quaternion.Euler(0f, 0f, leanDeg));

            // spine + neck follow the lean
            SetDrive(_rig.spine, Mathf.Lerp(spineSlackSpring, spineStandSpring, combined), spineDamper, spineMaxForce);
            SetTargetLocal(_rig.spine, Quaternion.Euler(0f, 0f, leanDeg * spineLeanFollow));
            SetDrive(_rig.neck, Mathf.Lerp(spineSlackSpring * 0.4f, spineStandSpring * 0.35f, combined), spineDamper, spineMaxForce * 0.4f);
            SetTargetLocal(_rig.neck, Quaternion.Euler(0f, 0f, leanDeg * neckLeanFollow));

            TorsoAssist(leanDeg);

            RopePull(_rig.left, l);
            RopePull(_rig.right, r);
        }

        float Shape(float t)
        {
            t = Mathf.Clamp01(IsFinite(t) ? t : 0f);
            return Mathf.Pow(t, tensionGamma);
        }

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        void DriveLeg(in PuppetRig.Leg leg, float squat, float leanDeg, float combined)
        {
            float squatSpring = Mathf.Lerp(legSquatSlackSpring, legSquatStandSpring, combined);

            // Screen-plane (roll) component: hips counter-rotate so the thighs stay
            // roughly vertical while the pelvis leans; knees/ankles stay planar.
            float hipRoll = -leanDeg * hipLeanCounter;

            DriveJoint(leg.hip, Quaternion.Euler(squat * hipSquatDeg, 0f, hipRoll), squatSpring, legSquatDamper, legSquatMaxForce);
            DriveJoint(leg.knee, Quaternion.Euler(squat * kneeSquatDeg, 0f, 0f), squatSpring, legSquatDamper, legSquatMaxForce);
            DriveJoint(leg.ankle, Quaternion.Euler(squat * ankleSquatDeg, 0f, 0f), squatSpring, legSquatDamper, legSquatMaxForce);
        }

        void DriveJoint(ConfigurableJoint j, Quaternion target, float spring, float damper, float maxForce)
        {
            SetDrive(j, spring, damper, maxForce);
            SetTargetLocal(j, target);
        }

        void TorsoAssist(float leanDeg)
        {
            var body = _rig.torso;
            if (body == null) return;
            Vector3 desiredUp = Quaternion.Euler(0f, 0f, leanDeg) * Vector3.up;
            Vector3 err = Vector3.Cross(body.transform.up, desiredUp);
            Vector3 torque = err * torsoUprightAssist - body.angularVelocity * torsoUprightDamping;
            body.AddTorque(Vector3.ClampMagnitude(torque, maxAssistTorque), ForceMode.Acceleration);
        }

        void RopePull(in PuppetRig.Leg leg, float t)
        {
            if (leg.foot == null) return;
            Transform anchor = leg.forceAnchor != null ? leg.forceAnchor : leg.pulley;
            if (anchor == null) return;
            Vector3 from = leg.ropeAttach != null ? leg.ropeAttach.position : leg.foot.worldCenterOfMass;
            Vector3 dir = anchor.position - from;
            float d = dir.magnitude;
            if (!(d > 1e-4f) || !IsFinite(t)) return; // also rejects NaN d
            leg.foot.AddForceAtPosition(dir / d * (ropePull * Mathf.Clamp01(t)), from, ForceMode.Force);
        }

        static void SetDrive(ConfigurableJoint j, float spring, float damper, float maxForce)
        {
            if (j == null) return;
            var d = j.slerpDrive;
            d.positionSpring = spring;
            d.positionDamper = damper;
            d.maximumForce = maxForce;
            j.slerpDrive = d;
        }

        /// <summary>
        /// Drives a ConfigurableJoint toward <paramref name="targetLocalRotation"/>
        /// (child rotation relative to the connected body). Rig is authored
        /// standing, so identity == "hold the standing pose".
        /// </summary>
        static void SetTargetLocal(ConfigurableJoint joint, Quaternion targetLocalRotation)
        {
            if (joint == null) return;
            if (!IsFinite(targetLocalRotation.x) || !IsFinite(targetLocalRotation.y)
                || !IsFinite(targetLocalRotation.z) || !IsFinite(targetLocalRotation.w)) return;

            Vector3 right = joint.axis.normalized;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;

            Quaternion worldToJoint = Quaternion.LookRotation(forward, up);
            Quaternion result = Quaternion.Inverse(worldToJoint)
                                * Quaternion.Inverse(targetLocalRotation) // startLocalRotation == identity
                                * worldToJoint;

            // The ConfigurableJoint setter is strict about normalisation — guard
            // against float drift so it never logs "Invalid quaternion".
            float n = Mathf.Sqrt(result.x * result.x + result.y * result.y
                                 + result.z * result.z + result.w * result.w);
            if (!(n > 1e-4f) || !IsFinite(n)) return;
            joint.targetRotation = new Quaternion(result.x / n, result.y / n, result.z / n, result.w / n);
        }
    }
}

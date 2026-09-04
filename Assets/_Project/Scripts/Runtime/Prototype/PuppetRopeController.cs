using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1.2 prototype. Four-axis puppet control from two ropes:
    ///
    ///   tension L / R      -> each foot's rope (0 slack .. 1 taut)
    ///   combined tension   -> SQUAT depth (both legs, symmetric)
    ///   tension difference -> FORWARD / BACKWARD lean (toward / away from opponent)
    ///   averaged horizontal -> INWARD / OUTWARD lean (depth axis, toward / away from camera)
    ///
    /// Forward/back and depth are composed into ONE world-space "target up" vector
    /// (FromToRotation, so there is never any yaw / back-turning). The pelvis is
    /// driven to that; spine + neck follow a fraction more.
    ///
    /// The rig stays a CONTROLLED 2.5D+ structure: only two body-lean axes are
    /// unlocked (fight-plane + depth), yaw is hard-locked everywhere, feet never
    /// leave the rail. No animation — everything is ConfigurableJoint slerp drives
    /// plus a small downward rope pull. Numbers are a tuning BASELINE, not a spec.
    /// </summary>
    [RequireComponent(typeof(PuppetRig))]
    public sealed class PuppetRopeController : MonoBehaviour
    {
        [Header("Tension response — Phase 1.2 (fast)")]
        [Range(0.3f, 4f)] public float tensionGamma = 1.2f;
        [Min(1f)] public float tensionSmoothing = 45f;
        [Min(1f)] public float depthSmoothing = 30f;

        [Header("Squat — driven by COMBINED tension (symmetric)")]
        [Range(0f, 1f)] public float perSideSquatWeight = 0.22f;
        [Tooltip("Hip flexes +; knee flexes - (OPPOSITE sense) so the leg compresses into a real squat.")]
        public float hipSquatDeg = 62f;
        public float kneeSquatDeg = -118f;
        public float ankleSquatDeg = 55f;
        public float legSlackSpring = 900f;
        public float legStandSpring = 7200f;
        public float legDamper = 340f;
        public float legMaxForce = 46000f;
        [Tooltip("Ankle drive is a fraction of the knee/hip drive so it follows rather than fights.")]
        [Range(0.05f, 1f)] public float ankleSpringScale = 0.35f;

        [Header("Forward / Backward lean — tension DIFFERENCE (signed, symmetric)")]
        [Tooltip("Pelvis lean (deg) at one-rope-full / other-slack. Torso ends ~30% higher.")]
        public float forwardBackGain = 44f;
        [Tooltip("Extra multiplier on the BACKWARD half so it matches forward amplitude.")]
        public float backwardBoost = 1.06f;
        [Range(0f, 0.8f)] public float spineFollow = 0.38f;
        [Range(0f, 0.8f)] public float neckFollow = 0.26f;
        [Tooltip("Negate to swap which rope pulls forward (default: Right rope = forward).")]
        public float forwardBackSign = 1f;

        [Header("Inward / Outward lean — averaged HORIZONTAL input (depth axis)")]
        [Tooltip("Pelvis depth lean (deg) at full inward / outward input. + = OUTWARD (toward camera).")]
        public float depthGain = 36f;
        [Tooltip("How much the ankles/legs follow the depth lean so the body tilts as one piece from the feet up.")]
        [Range(0f, 1f)] public float depthLegFollow = 0.85f;
        [Tooltip("How much the hips follow the depth lean relative to pelvis.")]
        [Range(0f, 1f)] public float depthHipFollow = 0.25f;

        [Header("Pelvis → world drive (carries the 2-axis lean)")]
        public float pelvisSlackSpring = 1200f;
        public float pelvisStandSpring = 10500f;
        public float pelvisDamper = 540f;
        public float pelvisMaxForce = 95000f;

        [Header("Spine + neck drive")]
        public float spineSlackSpring = 1500f;
        public float spineStandSpring = 7200f;
        public float spineDamper = 340f;
        public float spineMaxForce = 32000f;

        [Header("Torso balance assist (small AddTorque)")]
        public float torsoUprightAssist = 38f;
        public float torsoUprightDamping = 12f;
        public float maxAssistTorque = 150f;

        [Header("Rope pull — small downward force at each foot (plants it on the rail)")]
        public float ropePull = 45f;

        [Header("Physics solver (runtime only — never touches ProjectSettings)")]
        [Min(1)] public int solverIterations = 30;
        [Min(1)] public int solverVelocityIterations = 22;
        [Min(1f)] public float partMaxAngularVelocity = 28f;

        [Header("Tuning override (ignores input while ON — for MCP/inspector testing)")]
        public bool debugOverrideInput;
        [Range(0f, 1f)] public float debugLeft = 1f;
        [Range(0f, 1f)] public float debugRight = 1f;
        [Range(-1f, 1f)] public float debugDepth;
        [Range(-1f, 1f)] public float debugArm;
        public float debugArmVelocity;

        [Header("Arm Combat Control — Right Arm (Sword)")]
        public float armExtendPitchGain = 38f;     // shoulder reaches forward
        public float armSlashYawGain = 34f;        // shoulder slashes across inward
        public float armElbowExtendGain = 50f;     // elbow opens / extends forward
        public float armActiveSpring = 850f;
        public float armRelaxedSpring = 400f;
        public float armDamper = 45f;
        public float armMaxForce = 35000f;
        public float armSwipeImpulseScale = 16f;
        [Min(1f)] public float armSmoothing = 35f;

        // ---- input state ----
        public float LeftInput { get; private set; }
        public float RightInput { get; private set; }
        public float DepthInput { get; private set; }
        public float ArmInput { get; private set; }
        public float ArmVelocity { get; private set; }

        // ---- smoothed physics state ----
        public float LeftTension { get; private set; }
        public float RightTension { get; private set; }
        public float DepthValue { get; private set; }
        public float ArmValue { get; private set; }
        public float CombinedTension => 0.5f * (LeftTension + RightTension);
        public float FacingSign => _facingSign;

        // ---- lean readouts, in the FACING frame, side-independent ----
        /// <summary>+ = leaning toward the opponent (forward), - = away (backward).</summary>
        public float ForwardLeanDeg
        {
            get
            {
                if (_rig == null || _rig.torso == null) return 0f;
                Vector3 up = _rig.torso.transform.up;
                return Mathf.Asin(Mathf.Clamp(up.x * _facingSign, -1f, 1f)) * Mathf.Rad2Deg;
            }
        }

        /// <summary>+ = leaning OUTWARD (toward the camera / -Z), - = INWARD (+Z). Screen-consistent.</summary>
        public float DepthLeanDeg
        {
            get
            {
                if (_rig == null || _rig.torso == null) return 0f;
                Vector3 up = _rig.torso.transform.up;
                return Mathf.Asin(Mathf.Clamp(-up.z, -1f, 1f)) * Mathf.Rad2Deg;
            }
        }

        /// <summary>Total tilt of the torso from vertical (deg).</summary>
        public float CombinedLeanDeg =>
            _rig != null && _rig.torso != null ? Vector3.Angle(_rig.torso.transform.up, Vector3.up) : 0f;

        public float FootSeparation =>
            _rig != null && _rig.left.foot != null && _rig.right.foot != null
                ? Mathf.Abs(_rig.right.foot.position.x - _rig.left.foot.position.x)
                : 0f;

        public float PelvisHeight => _rig != null && _rig.pelvis != null ? _rig.pelvis.position.y : 0f;

        PuppetRig _rig;
        float _facingSign = 1f;

        // ---- response-time probe (dev tooling; harmless when idle) ----
        struct Sample { public float t, fwd, depth; }
        readonly Sample[] _probe = new Sample[240];
        int _probeCount;
        float _probeT0;
        bool _probing;

        /// <summary>Start recording ForwardLean/DepthLean vs time (call, then step the input).</summary>
        public void BeginProbe()
        {
            _probeCount = 0;
            _probeT0 = Time.time;
            _probing = true;
        }

        /// <summary>time from BeginProbe to first crossing `frac` of `finalFwd` (deg), and to `frac` of finalDepth.</summary>
        public string ProbeReport(float finalFwd, float finalDepth, float frac = 0.8f)
        {
            _probing = false;
            float tFirst = -1f, tFwd = -1f, tDepth = -1f;
            float needFwd = Mathf.Abs(finalFwd) * frac;
            float needDepth = Mathf.Abs(finalDepth) * frac;
            for (int i = 0; i < _probeCount; i++)
            {
                var s = _probe[i];
                if (tFirst < 0f && (Mathf.Abs(s.fwd) > 2f || Mathf.Abs(s.depth) > 2f)) tFirst = s.t;
                if (tFwd < 0f && needFwd > 0.1f && Mathf.Abs(s.fwd) >= needFwd) tFwd = s.t;
                if (tDepth < 0f && needDepth > 0.1f && Mathf.Abs(s.depth) >= needDepth) tDepth = s.t;
            }
            return $"samples={_probeCount} firstResponse={tFirst:0.000}s  to{frac * 100:0}%fwd={tFwd:0.000}s  to{frac * 100:0}%depth={tDepth:0.000}s";
        }

        void Awake()
        {
            _rig = GetComponent<PuppetRig>();
            _facingSign = _rig.facingSign != 0f ? Mathf.Sign(_rig.facingSign)
                                                : (_rig.side == PlayerSide.Left ? 1f : -1f);

            Physics.defaultSolverIterations = Mathf.Max(Physics.defaultSolverIterations, solverIterations);
            Physics.defaultSolverVelocityIterations = Mathf.Max(Physics.defaultSolverVelocityIterations, solverVelocityIterations);

            foreach (var rb in GetComponentsInChildren<Rigidbody>())
                rb.maxAngularVelocity = Mathf.Max(rb.maxAngularVelocity, partMaxAngularVelocity);

            IgnoreAdjacentCollisions();
        }

        void IgnoreAdjacentCollisions()
        {
            Pair(_rig.pelvis, _rig.torso);
            Pair(_rig.torso, _rig.head);
            IgnoreLegChain(_rig.left);
            IgnoreLegChain(_rig.right);

            var uL = FindPart("UpperArm_L"); var lL = FindPart("LowerArm_L"); var hL = FindPart("Hand_L");
            var uR = FindPart("UpperArm_R"); var lR = FindPart("LowerArm_R"); var hR = FindPart("Hand_R");
            Pair(_rig.torso, uL); Pair(_rig.torso, lL); Pair(_rig.torso, hL);
            Pair(uL, lL); Pair(lL, hL); Pair(uL, hL);
            Pair(_rig.torso, uR); Pair(_rig.torso, lR); Pair(_rig.torso, hR);
            Pair(uR, lR); Pair(lR, hR); Pair(uR, hR);
            Pair(uL, uR); Pair(lL, lR); Pair(hL, hR);

            var sw = _rig.sword != null ? _rig.sword : FindPart("Sword_R");
            if (sw != null)
            {
                Pair(sw, hR);
                Pair(sw, lR);
                Pair(sw, uR);
                Pair(sw, _rig.torso);
                Pair(sw, _rig.head);
            }
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

        /// <summary>Fed by <see cref="PuppetRopeInput"/>.</summary>
        public void SetInput(float left, float right, float depth, float arm = 0f, float armVelocity = 0f)
        {
            LeftInput = Mathf.Clamp01(Finite(left));
            RightInput = Mathf.Clamp01(Finite(right));
            DepthInput = Mathf.Clamp(Finite(depth), -1f, 1f);
            ArmInput = Mathf.Clamp(Finite(arm), -1f, 1f);
            ArmVelocity = Finite(armVelocity);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float kt = 1f - Mathf.Exp(-tensionSmoothing * dt);
            float kd = 1f - Mathf.Exp(-depthSmoothing * dt);

            float lGoal = debugOverrideInput ? debugLeft : LeftInput;
            float rGoal = debugOverrideInput ? debugRight : RightInput;
            float dGoal = debugOverrideInput ? debugDepth : DepthInput;
            float aGoal = debugOverrideInput ? debugArm : ArmInput;
            float aVelGoal = debugOverrideInput ? debugArmVelocity : ArmVelocity;

            LeftTension = Finite(Mathf.Lerp(LeftTension, lGoal, kt));
            RightTension = Finite(Mathf.Lerp(RightTension, rGoal, kt));
            DepthValue = Finite(Mathf.Lerp(DepthValue, dGoal, kd));

            float ka = 1f - Mathf.Exp(-armSmoothing * dt);
            ArmValue = Finite(Mathf.Lerp(ArmValue, aGoal, ka));

            DriveSwordArm(ArmValue, aVelGoal);

            float l = Shape(LeftTension);
            float r = Shape(RightTension);
            float combined = 0.5f * (l + r);
            float squatL = Mathf.Lerp(1f - combined, 1f - l, perSideSquatWeight);
            float squatR = Mathf.Lerp(1f - combined, 1f - r, perSideSquatWeight);

            // ---- compose the 2-axis lean into one world rotation ----
            //   forward/back = rotation about world Z (the puppet's left-right axis)
            //   depth        = rotation about world X (the puppet's forward axis)
            //
            // Dynamic Collapse:
            //   When tension is active, target is commanded directly by rope tension difference.
            //   When tension is released (combined -> 0), target smoothly follows the puppet's current
            //   physical lean angle instead of forcing it to vertical 0°, letting gravity, inertia,
            //   and natural body weight collapse the puppet in the direction of its current motion.
            float fbCmd = (r - l) * forwardBackGain * forwardBackSign;
            float fbRaw = fbCmd < 0f ? fbCmd * backwardBoost : fbCmd;

            float currentFb = ForwardLeanDeg;
            float activeBlend = Mathf.Clamp01(Mathf.Max(combined, Mathf.Abs(r - l) * 0.8f) * 1.6f);
            float fbDeg = Mathf.Lerp(currentFb, fbRaw, activeBlend);

            float depthDeg = DepthValue * depthGain;

            Quaternion leanWorld = Quaternion.Euler(-depthDeg, 0f, -_facingSign * fbDeg);

            // ---- pelvis: full lean, anchored to the world with full-body Z arc swing ----
            SetDrive(_rig.pelvisPlaneJoint,
                Mathf.Lerp(pelvisSlackSpring, pelvisStandSpring, combined), pelvisDamper, pelvisMaxForce);
            SetTargetWorld(_rig.pelvisPlaneJoint, leanWorld);

            // Guide pelvis Z coordinate so the entire lower body tilts from the feet up
            if (_rig.pelvisPlaneJoint != null)
            {
                float targetZ = Mathf.Sin(depthDeg * Mathf.Deg2Rad) * (_rig.standingPelvisHeight * 0.65f);
                _rig.pelvisPlaneJoint.targetPosition = new Vector3(0f, 0f, targetZ);
            }

            // ---- spine + neck: follow the same 2-axis lean a fraction more ----
            SetDrive(_rig.spine, Mathf.Lerp(spineSlackSpring, spineStandSpring, combined), spineDamper, spineMaxForce);
            SetTargetWorld(_rig.spine, Quaternion.Slerp(Quaternion.identity, leanWorld, spineFollow));
            SetDrive(_rig.neck, Mathf.Lerp(spineSlackSpring * 0.4f, spineStandSpring * 0.35f, combined), spineDamper, spineMaxForce * 0.4f);
            SetTargetWorld(_rig.neck, Quaternion.Slerp(Quaternion.identity, leanWorld, neckFollow));

            // ---- legs: squat (fight plane) + depth follow so the body stays one piece from ankles up ----
            DriveLeg(_rig.left, squatL, depthDeg, combined);
            DriveLeg(_rig.right, squatR, depthDeg, combined);

            TorsoAssist(leanWorld, combined);

            RopePull(_rig.left, l);
            RopePull(_rig.right, r);

            if (_probing && _probeCount < _probe.Length)
                _probe[_probeCount++] = new Sample { t = Time.time - _probeT0, fwd = ForwardLeanDeg, depth = DepthLeanDeg };
        }

        void DriveSwordArm(float armVal, float armVel)
        {
            if (_rig == null || _rig.rightArm.shoulder == null) return;

            var shoulder = _rig.rightArm.shoulder;
            var elbow = _rig.rightArm.elbow;

            float spring = Mathf.Lerp(armRelaxedSpring, armActiveSpring, Mathf.Clamp01(Mathf.Abs(armVal) * 1.6f));

            // Target relative rotation for shoulder:
            // Slerp drive requires SetTargetWorld.
            // When armVal > 0 (thrust/slash):
            //   pitch around joint Z swings upper arm forward (+facing)
            //   yaw around joint Y sweeps arm across torso inward (-Z in world)
            // When armVal < 0 (retract):
            //   pitch pulls upper arm back
            //   yaw pulls upper arm outward
            float shoulderPitch = -_facingSign * (armVal * armExtendPitchGain);
            float shoulderYaw = -_facingSign * (armVal * armSlashYawGain);
            Quaternion shoulderTarget = Quaternion.Euler(0f, shoulderYaw, shoulderPitch);

            SetDrive(shoulder, spring, armDamper, armMaxForce);
            SetTargetWorld(shoulder, shoulderTarget);

            if (elbow != null)
            {
                // BendElbow axis = (0, 0, 1), hinge limit is [lowFight: -10f, highFight: 135f].
                // Negative angle extends the elbow; positive flexes it tighter.
                float elbowDeg = -armVal * armElbowExtendGain;
                Quaternion elbowTarget = Quaternion.Euler(elbowDeg, 0f, 0f);
                SetDrive(elbow, spring * 0.9f, armDamper * 0.9f, armMaxForce);
                elbow.targetRotation = Quaternion.Inverse(elbowTarget);
            }

            // Swipe momentum / physical inertia:
            // Fast swipe imparts torque to upper arm, lower arm, and sword bodies
            if (Mathf.Abs(armVel) > 0.15f)
            {
                Vector3 torqueUpper = new Vector3(0f, -_facingSign * armVel * 0.7f, _facingSign * armVel * 0.9f) * armSwipeImpulseScale;
                Vector3 torqueLower = new Vector3(0f, -_facingSign * armVel * 0.9f, _facingSign * armVel * 1.3f) * armSwipeImpulseScale;

                if (_rig.rightArm.upperArm != null)
                    _rig.rightArm.upperArm.AddTorque(torqueUpper, ForceMode.Acceleration);
                if (_rig.rightArm.lowerArm != null)
                    _rig.rightArm.lowerArm.AddTorque(torqueLower, ForceMode.Acceleration);
                if (_rig.sword != null)
                    _rig.sword.AddTorque(torqueLower * 0.5f, ForceMode.Acceleration);
            }
        }

        void DriveLeg(in PuppetRig.Leg leg, float squat, float depthDeg, float combined)
        {
            float spring = Mathf.Lerp(legSlackSpring, legStandSpring, combined);

            // Hip & Knee flex in the fight plane; Ankle tilts the leg column from the foot in depth
            Quaternion hipRot = Quaternion.Euler(depthDeg * depthHipFollow, 0f, squat * hipSquatDeg);
            Quaternion kneeRot = Quaternion.Euler(0f, 0f, squat * kneeSquatDeg);
            Quaternion ankleRot = Quaternion.Euler(depthDeg * depthLegFollow, 0f, squat * ankleSquatDeg);

            DriveJoint(leg.hip, hipRot, spring, legDamper, legMaxForce);
            DriveJoint(leg.knee, kneeRot, spring, legDamper, legMaxForce);
            DriveJoint(leg.ankle, ankleRot,
                spring * ankleSpringScale, legDamper * ankleSpringScale, legMaxForce * ankleSpringScale);
        }

        void DriveJoint(ConfigurableJoint j, Quaternion targetRelative, float spring, float damper, float maxForce)
        {
            SetDrive(j, spring, damper, maxForce);
            SetTargetWorld(j, targetRelative);
        }

        void TorsoAssist(Quaternion leanWorld, float combined)
        {
            var body = _rig.torso;
            if (body == null) return;
            Vector3 desiredUp = leanWorld * Vector3.up;
            Vector3 err = Vector3.Cross(body.transform.up, desiredUp);
            float assistScale = Mathf.Lerp(0.15f, 1f, combined);
            Vector3 torque = err * (torsoUprightAssist * assistScale) - body.angularVelocity * torsoUprightDamping;
            body.AddTorque(Vector3.ClampMagnitude(torque, maxAssistTorque), ForceMode.Acceleration);
        }

        void RopePull(in PuppetRig.Leg leg, float t)
        {
            if (leg.foot == null) return;
            Transform anchor = leg.forceAnchor;
            if (anchor == null) return;
            Vector3 from = leg.ropeAttach != null ? leg.ropeAttach.position : leg.foot.worldCenterOfMass;
            Vector3 dir = anchor.position - from;
            float d = dir.magnitude;
            if (!(d > 1e-4f)) return;
            leg.foot.AddForceAtPosition(dir / d * (ropePull * Mathf.Clamp01(t)), from, ForceMode.Force);
        }

        float Shape(float t)
        {
            t = Mathf.Clamp01(Finite(t));
            return Mathf.Pow(t, tensionGamma);
        }

        static float Finite(float v) => (float.IsNaN(v) || float.IsInfinity(v)) ? 0f : v;

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
        /// Drives a ConfigurableJoint toward <paramref name="target"/> (child rotation
        /// relative to the connected body, in world axes). REQUIRES the joint to be
        /// authored with axis = (1,0,0), secondaryAxis = (0,1,0) so the joint space is
        /// identity and targetRotation is simply the inverse of the desired rotation.
        /// </summary>
        static void SetTargetWorld(ConfigurableJoint joint, Quaternion target)
        {
            if (joint == null) return;
            float n = Mathf.Sqrt(target.x * target.x + target.y * target.y
                                 + target.z * target.z + target.w * target.w);
            if (!(n > 1e-4f) || float.IsNaN(n) || float.IsInfinity(n)) return;
            var unit = new Quaternion(target.x / n, target.y / n, target.z / n, target.w / n);
            joint.targetRotation = Quaternion.Inverse(unit);
        }
    }
}

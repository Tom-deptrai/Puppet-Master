using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1 prototype. Turns two normalized tension values (0 = rope fully
    /// slack, 1 = rope fully taut) into physics forces on a jointed puppet.
    ///
    /// The puppet is a marionette-style planar rig on a rail. The ropes' tension
    /// is what powers the standing:
    ///
    ///  * both taut  -> the pelvis→world joint stiffens toward upright and the
    ///                  leg joints straighten -> the puppet rises and stands.
    ///  * both slack  -> those drives go limp; a small residual tone keeps a low,
    ///                   knees-buckled crouch instead of a dead-ragdoll heap.
    ///  * one taut     -> that leg straightens while the other stays buckled, the
    ///                    pelvis rolls and the torso leans -> left/right asymmetry.
    ///
    /// No animation — every pose comes from ConfigurableJoint slerp drives plus a
    /// rope pull applied at each foot. All numbers here are a tuning BASELINE.
    /// </summary>
    [RequireComponent(typeof(PuppetRig))]
    public sealed class PuppetRopeController : MonoBehaviour
    {
        [Header("Tension response")]
        [Range(0.3f, 4f)] public float tensionGamma = 1.3f;
        [Min(1f)] public float tensionSmoothing = 14f;

        [Header("Pelvis → world drive  (the main 'stand up' force)")]
        public float pelvisSlackSpring = 340f;
        public float pelvisStandSpring = 4200f;
        public float pelvisSlackDamper = 42f;
        public float pelvisStandDamper = 260f;
        public float pelvisMaxForce = 40000f;
        [Tooltip("Extra pelvis lean (deg) from the left/right tension difference.")]
        public float leanFromImbalance = 12f;

        [Header("Leg drive — hip + knee (per side)")]
        public float legSlackSpring = 150f;
        public float legStandSpring = 2600f;
        public float legSlackDamper = 18f;
        public float legStandDamper = 150f;
        public float legMaxForce = 20000f;
        [Tooltip("Hip splay (deg) when the rope is fully slack.")]
        public float hipFoldAngle = 5f;
        [Tooltip("Knee buckle (deg) when the rope is fully slack.")]
        public float kneeFoldAngle = 52f;
        [Tooltip("Left/right legs buckle in mirror so a slack puppet sinks straight down.")]
        public bool mirrorRightLeg = true;

        [Header("Ankle drive (per side)")]
        public float ankleSlackSpring = 60f;
        public float ankleStandSpring = 700f;
        public float ankleDamper = 30f;
        public float ankleMaxForce = 6000f;
        public float ankleFoldAngle = 12f;

        [Header("Spine + neck drive (combined tension)")]
        public float spineSlackSpring = 230f;
        public float spineStandSpring = 2600f;
        public float spineSlackDamper = 20f;
        public float spineStandDamper = 150f;
        public float spineMaxForce = 16000f;
        public float spineFoldAngle = 8f;
        public float neckFoldAngle = 10f;

        [Header("Torso balance nudge (small AddTorque assist)")]
        public float torsoUprightAssist = 25f;
        public float torsoUprightDamping = 6f;
        public float maxAssistTorque = 60f;

        [Header("Rope pull — force applied AT each foot, toward its pulley")]
        public float ropePull = 45f;

        [Header("Physics solver (runtime only — never touches ProjectSettings)")]
        [Min(1)] public int solverIterations = 24;
        [Min(1)] public int solverVelocityIterations = 12;

        [Header("Tuning override (ignores input while ON — for MCP/inspector testing)")]
        public bool debugOverrideInput;
        [Range(0f, 1f)] public float debugLeft = 1f;
        [Range(0f, 1f)] public float debugRight = 1f;

        // ---- read-only state for the HUD / rope visuals ----
        public float LeftInput { get; private set; }
        public float RightInput { get; private set; }
        public float LeftTension { get; private set; }
        public float RightTension { get; private set; }
        public float CombinedTension => 0.5f * (LeftTension + RightTension);

        PuppetRig _rig;

        void Awake()
        {
            _rig = GetComponent<PuppetRig>();
            Physics.defaultSolverIterations = Mathf.Max(Physics.defaultSolverIterations, solverIterations);
            Physics.defaultSolverVelocityIterations = Mathf.Max(Physics.defaultSolverVelocityIterations, solverVelocityIterations);
            IgnoreSelfCollisions();
        }

        void IgnoreSelfCollisions()
        {
            var cols = GetComponentsInChildren<Collider>();
            for (int i = 0; i < cols.Length; i++)
            for (int k = i + 1; k < cols.Length; k++)
                Physics.IgnoreCollision(cols[i], cols[k], true);
        }

        /// <summary>Fed by <see cref="PuppetRopeInput"/>. Values are clamped to 0..1.</summary>
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

            float l = Shape(LeftTension);
            float r = Shape(RightTension);
            float c = 0.5f * (l + r);
            float leanZ = (l - r) * leanFromImbalance;

            // 1) pelvis is held upright by the ropes' combined pull
            SetDrive(_rig.pelvisPlaneJoint,
                Mathf.Lerp(pelvisSlackSpring, pelvisStandSpring, c),
                Mathf.Lerp(pelvisSlackDamper, pelvisStandDamper, c),
                pelvisMaxForce);
            SetTargetLocal(_rig.pelvisPlaneJoint, Quaternion.Euler(0f, 0f, leanZ));

            // 2) legs straighten with their own side's tension
            DriveLeg(_rig.left, l, +1f);
            DriveLeg(_rig.right, r, mirrorRightLeg ? -1f : +1f);

            // 3) spine + neck follow combined tension
            DriveBend(_rig.spine, c, spineFoldAngle, leanZ,
                spineSlackSpring, spineStandSpring, spineSlackDamper, spineStandDamper, spineMaxForce);
            DriveBend(_rig.neck, c, neckFoldAngle, leanZ * 0.4f,
                spineSlackSpring * 0.4f, spineStandSpring * 0.35f, spineSlackDamper, spineStandDamper, spineMaxForce * 0.4f);

            // 4) tiny AddTorque nudge so the torso doesn't lag the pelvis
            TorsoAssist(c, leanZ);

            // 5) the required rope force, applied at the feet
            RopePull(_rig.left, l);
            RopePull(_rig.right, r);
        }

        float Shape(float t) => Mathf.Pow(Mathf.Clamp01(t), tensionGamma);

        void DriveLeg(in PuppetRig.Leg leg, float t, float sideSign)
        {
            SetDrive(leg.hip,
                Mathf.Lerp(legSlackSpring, legStandSpring, t),
                Mathf.Lerp(legSlackDamper, legStandDamper, t), legMaxForce);
            SetDrive(leg.knee,
                Mathf.Lerp(legSlackSpring, legStandSpring, t),
                Mathf.Lerp(legSlackDamper, legStandDamper, t), legMaxForce);
            SetDrive(leg.ankle,
                Mathf.Lerp(ankleSlackSpring, ankleStandSpring, t), ankleDamper, ankleMaxForce);

            Quaternion hipFold = Quaternion.Euler(0f, 0f, sideSign * hipFoldAngle);
            Quaternion kneeFold = Quaternion.Euler(0f, 0f, sideSign * -kneeFoldAngle);
            Quaternion ankleFold = Quaternion.Euler(0f, 0f, sideSign * ankleFoldAngle);
            SetTargetLocal(leg.hip, Quaternion.Slerp(hipFold, Quaternion.identity, t));
            SetTargetLocal(leg.knee, Quaternion.Slerp(kneeFold, Quaternion.identity, t));
            SetTargetLocal(leg.ankle, Quaternion.Slerp(ankleFold, Quaternion.identity, t));
        }

        void DriveBend(ConfigurableJoint joint, float t, float foldAngle, float leanZ,
            float slackSpring, float standSpring, float slackDamper, float standDamper, float maxForce)
        {
            if (joint == null) return;
            SetDrive(joint, Mathf.Lerp(slackSpring, standSpring, t), Mathf.Lerp(slackDamper, standDamper, t), maxForce);
            Quaternion fold = Quaternion.Euler(0f, 0f, foldAngle);
            Quaternion upright = Quaternion.Euler(0f, 0f, leanZ);
            SetTargetLocal(joint, Quaternion.Slerp(fold, upright, t));
        }

        void TorsoAssist(float t, float leanZ)
        {
            var body = _rig.torso;
            if (body == null) return;
            Vector3 desiredUp = Quaternion.Euler(0f, 0f, leanZ) * Vector3.up;
            Vector3 err = Vector3.Cross(body.transform.up, desiredUp);
            Vector3 torque = err * (torsoUprightAssist * t) - body.angularVelocity * (torsoUprightDamping * t);
            body.AddTorque(Vector3.ClampMagnitude(torque, maxAssistTorque), ForceMode.Acceleration);
        }

        void RopePull(in PuppetRig.Leg leg, float t)
        {
            if (leg.foot == null || leg.pulley == null) return;
            Vector3 from = leg.ropeAttach != null ? leg.ropeAttach.position : leg.foot.worldCenterOfMass;
            Vector3 dir = leg.pulley.position - from;
            float d = dir.magnitude;
            if (d < 1e-4f) return;
            leg.foot.AddForceAtPosition(dir / d * (ropePull * t), from, ForceMode.Force);
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
        /// (the child's rotation relative to the connected body). The rig is
        /// authored standing, so identity == "hold the standing pose".
        /// </summary>
        static void SetTargetLocal(ConfigurableJoint joint, Quaternion targetLocalRotation)
        {
            if (joint == null) return;
            Vector3 right = joint.axis;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            Vector3 up = Vector3.Cross(forward, right).normalized;

            Quaternion worldToJoint = Quaternion.LookRotation(forward, up);
            Quaternion result = Quaternion.Inverse(worldToJoint);
            result *= Quaternion.Inverse(targetLocalRotation); // startLocalRotation == identity
            result *= worldToJoint;
            joint.targetRotation = result;
        }
    }
}

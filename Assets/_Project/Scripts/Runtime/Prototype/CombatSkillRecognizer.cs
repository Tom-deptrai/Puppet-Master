using UnityEngine;

namespace PuppetMaster.Prototype
{
    public enum CombatSkill
    {
        None,
        HorizontalSlash,
        Thrust,
        OverheadStrike,
        Guard,
    }

    /// <summary>
    /// Phase 1.3 prototype. Reads the real sword motion produced by the existing
    /// two-thumb controller and names it. It never moves the rig, never applies a
    /// force and never changes a joint: recognition follows player-created pose,
    /// velocity, momentum and timing only.
    ///
    /// Recognition frame — everything is measured at the BLADE TIP, decomposed into
    /// the facing frame:
    ///   forward  = dot(tipVelocity, facing)      toward the opponent
    ///   lateral  = dot(tipVelocity, Vector3.fwd) across the depth axis
    ///   vertical = tipVelocity.y
    ///
    /// The four skills are separated by which of those dominates, plus how the blade
    /// itself is oriented relative to its own travel:
    ///   Thrust          tip travels ALONG the blade axis, blade points at the
    ///                   opponent, low rotation. A stab, not a cut.
    ///   OverheadStrike  the tip was raised near head height, then falls with the
    ///                   downward component dominating a real drop distance.
    ///   HorizontalSlash a rotating sweep whose horizontal travel dominates the
    ///                   vertical — the sword-arm axis produces exactly this arc.
    ///   Guard           a deliberate HELD retracted sword arm with the blade tilted
    ///                   up across the body and the sword essentially still.
    ///
    /// Standing still is NOT a skill: Guard requires a sustained sword-arm input, so
    /// the neutral rest pose reports None.
    /// </summary>
    [RequireComponent(typeof(PuppetRig), typeof(PuppetRopeController))]
    public sealed class CombatSkillRecognizer : MonoBehaviour
    {
        [Header("Blade sampling")]
        [Tooltip("Distance from the grip to the blade tip, in sword local space.")]
        [Min(0.1f)] public float bladeTipOffset = 0.81f;
        [Tooltip("Distance from the grip to the middle of the blade (guard band test).")]
        [Min(0.05f)] public float bladeMidOffset = 0.40f;
        [Tooltip("Smoothing time for the tip velocity, in seconds. Small = twitchy.")]
        [Min(0.001f)] public float velocitySmoothing = 0.045f;

        [Header("Attack gate (shared)")]
        [Tooltip("No attack is considered below this tip speed.")]
        [Min(0f)] public float attackMinTipSpeed = 1.60f;
        [Tooltip("The puppet must still be on its feet — a collapsing rag doll is not an attack.")]
        [Range(0f, 1f)] public float attackMinStandingRatio = 0.60f;

        [Header("Horizontal slash")]
        [Min(0f)] public float slashMinHorizontalSpeed = 1.50f;
        [Tooltip("Horizontal travel must beat vertical travel by this factor.")]
        [Min(0.1f)] public float slashHorizontalDominance = 1.25f;
        [Min(0f)] public float slashMinAngularSpeed = 2.50f;
        [Tooltip("How much of the horizontal travel may point AWAY from the opponent. " +
                 "Recovering from a lunge whips the sword backwards; that is not an attack.")]
        [Range(0f, 1f)] public float slashMaxBackwardFraction = 0.35f;

        [Header("Thrust")]
        [Tooltip("Blade must point at the opponent.")]
        [Range(0f, 1f)] public float thrustMinBladeAlignment = 0.80f;
        [Tooltip("The tip must travel ALONG its own blade axis — that is what makes it a stab.")]
        [Range(0f, 1f)] public float thrustMinAlongBlade = 0.65f;
        [Tooltip("A thrust is judged on forward reach, not raw speed - it has its own gate.")]
        [Min(0f)] public float thrustMinForwardSpeed = 0.85f;
        [Tooltip("A stab barely rotates; above this it is a cut.")]
        [Min(0f)] public float thrustMaxAngularSpeed = 3.50f;
        [Tooltip("Forward reach must beat the drop by this factor - a blade that is mainly "
                 + "falling is a chop that happens to be aimed forward, not a stab.")]
        [Min(0f)] public float thrustForwardDominance = 1.00f;

        [Header("Overhead strike")]
        [Tooltip("The tip peak must reach at least this far relative to head height (negative = below).")]
        public float overheadMinPeakRelativeToHead = 0.05f;
        [Tooltip("How long a raise stays valid as preparation, in seconds.")]
        [Min(0.05f)] public float overheadPreparationWindow = 1.10f;
        [Tooltip("How far the tip must have dropped from its peak.")]
        [Min(0f)] public float overheadMinDrop = 0.30f;
        [Min(0f)] public float overheadMinDownSpeed = 1.20f;
        [Tooltip("Downward travel must be at least this fraction of the horizontal travel.")]
        [Min(0f)] public float overheadDownDominance = 0.35f;
        [Min(0f)] public float overheadMinAngularSpeed = 1.80f;
        [Tooltip("How fast the remembered tip peak decays when the sword is not raised, m/s.")]
        [Min(0f)] public float overheadPeakDecayRate = 0.55f;
        [Tooltip("While the tip falls faster than this the peak is frozen, so the drop " +
                 "distance measures the real stroke instead of chasing the blade down.")]
        [Min(0f)] public float overheadPeakFreezeSpeed = 0.30f;

        [Header("Guard — a deliberate HELD pose, never the rest pose")]
        [Tooltip("Sword arm must be held at or below this (retracted) to count as guarding.")]
        [Range(-1f, 0f)] public float guardMaxArmInput = -0.30f;
        [Tooltip("Blade must be tilted up across the body, not drooping forward.")]
        [Range(0f, 1f)] public float guardMinBladeUp = 0.28f;
        [Tooltip("Hand must be carried in front of the torso.")]
        [Min(0f)] public float guardMinForwardOffset = 0.12f;
        [Min(0f)] public float guardBandBelowTorso = 0.45f;
        [Min(0f)] public float guardBandAboveHead = 0.35f;
        [Min(0f)] public float guardMaxTipSpeed = 0.90f;
        [Min(0f)] public float guardMaxAngularSpeed = 1.60f;
        [Tooltip("The sword arm must be settled, not mid-swipe.")]
        [Min(0f)] public float guardMaxArmVelocity = 1.20f;
        [Tooltip("The pose must be held this long before Guard is reported.")]
        [Min(0f)] public float guardHoldTime = 0.18f;
        [Tooltip("Guard survives this long after the pose breaks, so it cannot flicker.")]
        [Min(0f)] public float guardReleaseGrace = 0.12f;

        [Header("Recognition timing")]
        [Tooltip("How long an attack name stays on screen once recognised.")]
        [Min(0.05f)] public float actionDisplayTime = 0.24f;
        [Tooltip("Dead time after an attack before the next one can be recognised. It has "
                 + "to outlast a follow-through: the tail of a chop still drives the blade "
                 + "forward and would otherwise be named a second time.")]
        [Min(0f)] public float actionCooldown = 0.45f;
        public bool logTransitions = true;

        // ---- readouts (HUD / MCP tuning) ----
        public CombatSkill CurrentSkill { get; private set; }
        /// <summary>Speed of the grip. Kept for the HUD; recognition uses the tip.</summary>
        public float SwordSpeed { get; private set; }
        public float SwordAngularSpeed { get; private set; }
        public float TipSpeed { get; private set; }
        public float ForwardTipSpeed { get; private set; }
        public float LateralTipSpeed { get; private set; }
        public float VerticalTipSpeed { get; private set; }
        public float HorizontalTipSpeed { get; private set; }
        public float BladeAlignment { get; private set; }
        public float BladeUpAlignment { get; private set; }
        public float AlongBladeFraction { get; private set; }
        public float TipPeakHeight { get; private set; }
        public float ArmInput { get; private set; }
        public bool IsGuarding => CurrentSkill == CombatSkill.Guard;

        PuppetRig _rig;
        PuppetRopeController _controller;
        Rigidbody _sword;

        Vector3 _previousSwordPosition;
        Quaternion _previousSwordRotation;
        Vector3 _previousTipPosition;
        Vector3 _tipVelocity;
        bool _hasPreviousSample;

        float _tipPeakTime = -99f;
        float _guardCandidateSince = -1f;
        float _guardLostAt = -1f;
        bool _guardHeld;
        float _actionUntil = -1f;
        float _nextActionTime;

        void Awake()
        {
            _rig = GetComponent<PuppetRig>();
            _controller = GetComponent<PuppetRopeController>();
            _sword = _rig != null ? _rig.sword : null;
        }

        void OnDisable()
        {
            _hasPreviousSample = false;
            _guardHeld = false;
            SetSkill(CombatSkill.None);
        }

        void FixedUpdate()
        {
            if (_rig == null || _controller == null || _sword == null ||
                _rig.torso == null || _rig.head == null || _rig.pelvis == null ||
                _rig.rightArm.hand == null)
            {
                SetSkill(CombatSkill.None);
                return;
            }

            float now = Time.time;
            float dt = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            Vector3 facing = Vector3.right * _controller.FacingSign;
            Vector3 bladeDirection = _sword.transform.up;
            Vector3 tipPosition = _sword.transform.TransformPoint(0f, bladeTipOffset, 0f);

            ArmInput = _controller.ArmValue;

            if (!_hasPreviousSample)
            {
                StoreSample(tipPosition);
                TipPeakHeight = tipPosition.y;
                _tipPeakTime = now;
                _tipVelocity = Vector3.zero;
                SetSkill(CombatSkill.None);
                return;
            }

            // A rigid locked joint reports large solver-correction velocities on the
            // sword Rigidbody even while its visible pose is perfectly still (~2.7 m/s
            // and ~10 rad/s at rest). Transform deltas measure the pose the player can
            // actually see, which is the only thing recognition may react to.
            float inverseDt = 1f / dt;
            SwordSpeed = (_sword.position - _previousSwordPosition).magnitude * inverseDt;
            SwordAngularSpeed = Quaternion.Angle(_previousSwordRotation, _sword.rotation) *
                                Mathf.Deg2Rad * inverseDt;

            Vector3 rawTipVelocity = (tipPosition - _previousTipPosition) * inverseDt;
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(velocitySmoothing, 0.001f));
            _tipVelocity = Vector3.Lerp(_tipVelocity, rawTipVelocity, k);
            StoreSample(tipPosition);

            ForwardTipSpeed = Vector3.Dot(_tipVelocity, facing);
            LateralTipSpeed = Vector3.Dot(_tipVelocity, Vector3.forward);
            VerticalTipSpeed = _tipVelocity.y;
            HorizontalTipSpeed = new Vector2(ForwardTipSpeed, LateralTipSpeed).magnitude;
            TipSpeed = _tipVelocity.magnitude;
            BladeAlignment = Vector3.Dot(bladeDirection, facing);
            BladeUpAlignment = Vector3.Dot(bladeDirection, Vector3.up);
            AlongBladeFraction = TipSpeed > 0.05f
                ? Vector3.Dot(_tipVelocity / TipSpeed, bladeDirection)
                : 0f;

            // ---- overhead preparation: remember how high the tip was carried ----
            if (tipPosition.y >= TipPeakHeight)
            {
                TipPeakHeight = tipPosition.y;
                _tipPeakTime = now;
            }
            else if (VerticalTipSpeed > -overheadPeakFreezeSpeed)
            {
                // Only forget a raise while the blade is NOT in a downward stroke,
                // otherwise the remembered peak chases the tip down and the drop
                // distance of a real chop never accumulates.
                TipPeakHeight = Mathf.Max(tipPosition.y, TipPeakHeight - overheadPeakDecayRate * dt);
            }

            bool standing = _rig.standingPelvisHeight <= 0.001f ||
                            _rig.pelvis.position.y >= _rig.standingPelvisHeight * attackMinStandingRatio;

            // ---- attacks ----
            if (now >= _actionUntil && now >= _nextActionTime && standing)
            {
                // A stab is judged on reach, not on raw speed, so it carries its own
                // gate: blade pointed at the opponent AND travelling along its own axis.
                bool thrust = ForwardTipSpeed >= thrustMinForwardSpeed &&
                              ForwardTipSpeed >= -VerticalTipSpeed * thrustForwardDominance &&
                              BladeAlignment >= thrustMinBladeAlignment &&
                              AlongBladeFraction >= thrustMinAlongBlade &&
                              SwordAngularSpeed <= thrustMaxAngularSpeed;

                bool fast = TipSpeed >= attackMinTipSpeed;

                bool raised = TipPeakHeight >= _rig.head.position.y + overheadMinPeakRelativeToHead &&
                              now - _tipPeakTime <= overheadPreparationWindow;
                bool overhead = fast && raised &&
                                TipPeakHeight - tipPosition.y >= overheadMinDrop &&
                                -VerticalTipSpeed >= overheadMinDownSpeed &&
                                -VerticalTipSpeed >= HorizontalTipSpeed * overheadDownDominance &&
                                SwordAngularSpeed >= overheadMinAngularSpeed;

                // A stroke launched from ABOVE your own head is an overhead strike by
                // definition, so the raise locks the slash out until it is spent.
                bool slash = fast && !raised &&
                             HorizontalTipSpeed >= slashMinHorizontalSpeed &&
                             HorizontalTipSpeed >= Mathf.Abs(VerticalTipSpeed) * slashHorizontalDominance &&
                             ForwardTipSpeed >= -HorizontalTipSpeed * slashMaxBackwardFraction &&
                             SwordAngularSpeed >= slashMinAngularSpeed;

                // Most specific first. A stab and a cut are mutually exclusive through
                // the angular-speed test, so this order only breaks genuine ties.
                if (thrust) { BeginAction(CombatSkill.Thrust, now); return; }
                if (overhead) { BeginAction(CombatSkill.OverheadStrike, now); return; }
                if (slash) { BeginAction(CombatSkill.HorizontalSlash, now); return; }
            }

            if (now < _actionUntil) return;

            // ---- guard ----
            Vector3 bladeMidpoint = _sword.transform.TransformPoint(0f, bladeMidOffset, 0f);
            float forwardHandOffset = Vector3.Dot(_rig.rightArm.hand.position - _rig.torso.position, facing);

            bool guardPose = ArmInput <= guardMaxArmInput &&
                             BladeUpAlignment >= guardMinBladeUp &&
                             forwardHandOffset >= guardMinForwardOffset &&
                             bladeMidpoint.y >= _rig.torso.position.y - guardBandBelowTorso &&
                             bladeMidpoint.y <= _rig.head.position.y + guardBandAboveHead &&
                             TipSpeed <= guardMaxTipSpeed &&
                             SwordAngularSpeed <= guardMaxAngularSpeed &&
                             Mathf.Abs(_controller.EffectiveArmVelocity) <= guardMaxArmVelocity &&
                             standing;

            if (guardPose)
            {
                if (_guardCandidateSince < 0f) _guardCandidateSince = now;
                _guardLostAt = -1f;
                if (now - _guardCandidateSince >= guardHoldTime) _guardHeld = true;
            }
            else if (_guardHeld)
            {
                // Hysteresis: one noisy frame must not drop an established guard.
                if (_guardLostAt < 0f) _guardLostAt = now;
                if (now - _guardLostAt > guardReleaseGrace)
                {
                    _guardHeld = false;
                    _guardCandidateSince = -1f;
                    _guardLostAt = -1f;
                }
            }
            else
            {
                _guardCandidateSince = -1f;
            }

            SetSkill(_guardHeld ? CombatSkill.Guard : CombatSkill.None);
        }

        void BeginAction(CombatSkill skill, float now)
        {
            _guardHeld = false;
            _guardCandidateSince = -1f;
            _guardLostAt = -1f;
            SetSkill(skill);
            _actionUntil = now + actionDisplayTime;
            _nextActionTime = _actionUntil + actionCooldown;
            // A landed attack consumes its wind-up, so the same raise cannot fire twice.
            TipPeakHeight = _previousTipPosition.y;
            _tipPeakTime = now;
        }

        void StoreSample(Vector3 tipPosition)
        {
            _previousSwordPosition = _sword.position;
            _previousSwordRotation = _sword.rotation;
            _previousTipPosition = tipPosition;
            _hasPreviousSample = true;
        }

        void SetSkill(CombatSkill skill)
        {
            if (CurrentSkill == skill) return;
            CurrentSkill = skill;
            if (logTransitions && skill != CombatSkill.None)
                Debug.Log($"[Combat Skill] {skill} | tip={TipSpeed:0.00} m/s " +
                          $"(fwd={ForwardTipSpeed:0.00} lat={LateralTipSpeed:0.00} vert={VerticalTipSpeed:0.00}) | " +
                          $"angular={SwordAngularSpeed:0.00} rad/s | bladeFwd={BladeAlignment:0.00} " +
                          $"bladeUp={BladeUpAlignment:0.00} alongBlade={AlongBladeFraction:0.00} | " +
                          $"peakY={TipPeakHeight:0.00} | arm={ArmInput:0.00}");
        }
    }
}

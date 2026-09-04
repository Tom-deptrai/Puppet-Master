using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Prototype AI opponent. Drives the SAME PuppetRopeController input channels
    /// the player uses — no teleport, no auto-hit, no damage cheats. Intentionally
    /// imperfect: reaction delay + probabilistic Guard/Parry.
    /// </summary>
    [RequireComponent(typeof(PuppetRopeController), typeof(PuppetRig))]
    public sealed class PuppetAIOpponent : MonoBehaviour
    {
        public enum AIState
        {
            Guard,
            Approach,
            Attack,
            Recover,
            Defend,
        }

        public enum AttackKind
        {
            HorizontalSlash,
            Thrust,
            OverheadStrike,
        }

        [Header("Tuning")]
        [Range(0f, 1f)] public float aggression = 0.55f;
        [Range(0.05f, 1.2f)] public float reactionTime = 0.35f;
        [Range(0f, 1f)] public float guardChance = 0.55f;
        [Range(0f, 1f)] public float parryChance = 0.35f;
        [Min(0.2f)] public float attackCooldown = 1.35f;
        [Min(0.1f)] public float recoverTime = 0.55f;
        [Min(0.1f)] public float defendHoldTime = 0.55f;
        [Min(0.2f)] public float approachTime = 0.70f;
        [Min(0.2f)] public float guardHoldMin = 0.60f;
        [Min(0.2f)] public float guardHoldMax = 1.40f;

        [Header("Attack mix (weights)")]
        public float weightSlash = 1f;
        public float weightThrust = 1f;
        public float weightOverhead = 0.75f;

        [Header("Refs")]
        public PuppetCombatHealth playerHealth;
        public CombatSkillRecognizer playerSkills;
        public PuppetRopeController playerController;

        [Header("Debug")]
        public bool logStateChanges = true;

        public AIState State { get; private set; } = AIState.Guard;
        public AttackKind CurrentAttack { get; private set; }

        PuppetRig _rig;
        PuppetRopeController _controller;
        PuppetCombatHealth _health;
        CombatSkillRecognizer _skills;

        float _stateUntil;
        float _nextAttackTime;
        float _attackPhaseT0;
        float _pendingThreatAt = -1f;
        CombatSkill _pendingThreatSkill = CombatSkill.None;
        bool _defendAsParry;
        float _seed;

        void Awake()
        {
            _rig = GetComponent<PuppetRig>();
            _controller = GetComponent<PuppetRopeController>();
            _health = GetComponent<PuppetCombatHealth>();
            _skills = GetComponent<CombatSkillRecognizer>();
            _seed = Random.value * 1000f;
        }

        void Start()
        {
            AutoFindPlayer();
            Enter(AIState.Guard, Random.Range(guardHoldMin, guardHoldMax));
        }

        public void ResetAI()
        {
            _pendingThreatAt = -1f;
            _pendingThreatSkill = CombatSkill.None;
            _nextAttackTime = Time.time + attackCooldown * 0.5f;
            Enter(AIState.Guard, Random.Range(guardHoldMin, guardHoldMax));
        }

        public void NotifyKO()
        {
            // Hold a soft standing-slack pose; controller KO path collapses the body.
            _controller.SetInput(0.15f, 0.15f, 0f, 0f, 0f);
            enabled = false;
        }

        void AutoFindPlayer()
        {
            if (playerHealth != null && playerSkills != null) return;
            foreach (var h in FindObjectsByType<PuppetCombatHealth>(FindObjectsSortMode.None))
            {
                if (h == _health) continue;
                var rig = h.GetComponent<PuppetRig>();
                if (rig != null && rig.side == PlayerSide.Left)
                {
                    playerHealth = h;
                    playerSkills = rig.skillRecognition != null
                        ? rig.skillRecognition
                        : h.GetComponent<CombatSkillRecognizer>();
                    playerController = h.GetComponent<PuppetRopeController>();
                    break;
                }
            }
        }

        void FixedUpdate()
        {
            if (_health != null && _health.IsKO)
            {
                _controller.SetInput(0.1f, 0.1f, 0f, 0f, 0f);
                return;
            }

            AutoFindPlayer();
            ObserveThreat();

            float now = Time.time;
            switch (State)
            {
                case AIState.Guard:
                    DriveGuard();
                    if (TryEnterDefend(now)) break;
                    if (now >= _stateUntil)
                    {
                        if (Random.value < aggression && now >= _nextAttackTime)
                            BeginApproachOrAttack(now);
                        else
                            Enter(AIState.Guard, Random.Range(guardHoldMin, guardHoldMax));
                    }
                    break;

                case AIState.Approach:
                    DriveApproach(now);
                    if (TryEnterDefend(now)) break;
                    if (now >= _stateUntil)
                        BeginAttack(now);
                    break;

                case AIState.Attack:
                    DriveAttack(now);
                    if (now >= _stateUntil)
                    {
                        _nextAttackTime = now + attackCooldown;
                        Enter(AIState.Recover, recoverTime);
                    }
                    break;

                case AIState.Recover:
                    DriveRecover();
                    if (TryEnterDefend(now)) break;
                    if (now >= _stateUntil)
                        Enter(AIState.Guard, Random.Range(guardHoldMin, guardHoldMax));
                    break;

                case AIState.Defend:
                    DriveDefend(now);
                    if (now >= _stateUntil)
                        Enter(AIState.Recover, recoverTime * 0.6f);
                    break;
            }
        }

        void ObserveThreat()
        {
            if (playerSkills == null) return;
            CombatSkill skill = playerSkills.CurrentSkill;
            bool attacking = skill == CombatSkill.HorizontalSlash ||
                             skill == CombatSkill.Thrust ||
                             skill == CombatSkill.OverheadStrike;
            if (!attacking) return;
            if (_pendingThreatAt > 0f) return;
            _pendingThreatSkill = skill;
            _pendingThreatAt = Time.time + Random.Range(reactionTime * 0.75f, reactionTime * 1.25f);
        }

        bool TryEnterDefend(float now)
        {
            if (_pendingThreatAt < 0f || now < _pendingThreatAt) return false;
            _pendingThreatAt = -1f;

            // Already attacking through — finish the swing more often when aggressive.
            if (State == AIState.Attack && Random.value < aggression * 0.5f)
                return false;

            float roll = Random.value;
            if (roll < guardChance)
            {
                _defendAsParry = false;
                Enter(AIState.Defend, defendHoldTime);
                return true;
            }
            if (roll < guardChance + parryChance)
            {
                _defendAsParry = true;
                Enter(AIState.Defend, defendHoldTime * 0.7f);
                return true;
            }
            return false;
        }

        void BeginApproachOrAttack(float now)
        {
            if (Random.value < 0.55f)
                Enter(AIState.Approach, approachTime);
            else
                BeginAttack(now);
        }

        void BeginAttack(float now)
        {
            CurrentAttack = PickAttack();
            _attackPhaseT0 = now;
            float duration = CurrentAttack == AttackKind.OverheadStrike ? 0.95f :
                             CurrentAttack == AttackKind.Thrust ? 0.70f : 0.65f;
            Enter(AIState.Attack, duration);
        }

        AttackKind PickAttack()
        {
            float total = weightSlash + weightThrust + weightOverhead;
            float r = Random.value * total;
            if (r < weightSlash) return AttackKind.HorizontalSlash;
            if (r < weightSlash + weightThrust) return AttackKind.Thrust;
            return AttackKind.OverheadStrike;
        }

        void DriveGuard()
        {
            // Retracted sword arm held still — matches CombatSkillRecognizer Guard pose.
            _controller.SetInput(0.85f, 0.85f, 0.05f * Noise(0.4f), -0.62f, 0f);
        }

        void DriveApproach(float now)
        {
            float t = Mathf.InverseLerp(_stateUntil - approachTime, _stateUntil, now);
            // Lean slightly toward the player (Right rope = forward for both sides by default).
            float forward = Mathf.Lerp(0.55f, 0.95f, t);
            float rear = Mathf.Lerp(0.75f, 0.45f, t);
            float arm = Mathf.Lerp(-0.35f, 0.15f, t);
            _controller.SetInput(rear, forward, Noise(0.25f) * 0.15f, arm, 0f);
        }

        void DriveAttack(float now)
        {
            float t = now - _attackPhaseT0;
            switch (CurrentAttack)
            {
                case AttackKind.HorizontalSlash:
                    // Wind-up retract then fast extend with high arm velocity.
                    if (t < 0.18f)
                        _controller.SetInput(0.7f, 0.75f, 0.1f, -0.45f, -4f);
                    else if (t < 0.45f)
                        _controller.SetInput(0.55f, 0.95f, Noise(1f) * 0.35f, 1.0f, 10f);
                    else
                        _controller.SetInput(0.7f, 0.8f, 0f, 0.35f, 1f);
                    break;

                case AttackKind.Thrust:
                    if (t < 0.15f)
                        _controller.SetInput(0.65f, 0.85f, 0f, -0.25f, -2f);
                    else if (t < 0.50f)
                        _controller.SetInput(0.40f, 1.0f, 0f, 0.95f, 6f);
                    else
                        _controller.SetInput(0.7f, 0.85f, 0f, 0.4f, 0f);
                    break;

                case AttackKind.OverheadStrike:
                    // Raise (retract + stand tall) then chop down/forward.
                    if (t < 0.35f)
                        _controller.SetInput(0.95f, 0.95f, -0.1f, -0.85f, -3f);
                    else if (t < 0.70f)
                        _controller.SetInput(0.50f, 1.0f, 0.05f, 1.0f, 12f);
                    else
                        _controller.SetInput(0.75f, 0.8f, 0f, 0.2f, 0f);
                    break;
            }
        }

        void DriveRecover()
        {
            _controller.SetInput(0.8f, 0.8f, 0f, -0.25f, -1f);
        }

        void DriveDefend(float now)
        {
            if (_defendAsParry)
            {
                float t = (_stateUntil - now);
                // Beat into the incoming cut: quick extend across the midline.
                if (t > defendHoldTime * 0.35f)
                    _controller.SetInput(0.8f, 0.85f, 0f, 0.85f, 9f);
                else
                    _controller.SetInput(0.85f, 0.85f, 0f, -0.55f, -4f);
            }
            else
            {
                DriveGuard();
            }
        }

        float Noise(float freq)
        {
            return Mathf.Sin((Time.time + _seed) * freq * Mathf.PI * 2f);
        }

        void Enter(AIState next, float duration)
        {
            if (State != next && logStateChanges)
                Debug.Log($"[AI] {name}: {State} → {next}" +
                          (next == AIState.Attack ? $" ({CurrentAttack})" : "") +
                          (next == AIState.Defend ? (_defendAsParry ? " PARRY" : " GUARD") : ""));
            State = next;
            _stateUntil = Time.time + Mathf.Max(0.05f, duration);
        }
    }
}

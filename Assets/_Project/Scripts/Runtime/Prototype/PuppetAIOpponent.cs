using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Prototype AI opponent. Drives the SAME PuppetRopeController input channels
    /// the player uses — no teleport, no auto-hit, no damage cheats.
    ///
    /// Attack focus (foundation pass): Horizontal Slash only, with a readable
    /// Prep → Load → Swing → Follow-through → Recover → Guard sequence.
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

        public enum SlashPhase
        {
            None,
            Prep,
            Load,
            Swing,
            FollowThrough,
        }

        [Header("Tuning")]
        [Range(0f, 1f)] public float aggression = 0.60f;
        [Range(0.05f, 1.2f)] public float reactionTime = 0.35f;
        [Range(0f, 1f)] public float guardChance = 0.55f;
        [Range(0f, 1f)] public float parryChance = 0.20f;
        [Min(0.2f)] public float attackCooldown = 1.60f;
        [Min(0.1f)] public float recoverTime = 0.70f;
        [Min(0.1f)] public float defendHoldTime = 0.55f;
        [Min(0.2f)] public float approachTime = 0.55f;
        [Min(0.2f)] public float guardHoldMin = 0.70f;
        [Min(0.2f)] public float guardHoldMax = 1.50f;

        [Header("Horizontal Slash timing (seconds)")]
        [Min(0.05f)] public float slashPrepTime = 0.40f;
        [Min(0.05f)] public float slashLoadTime = 0.25f;
        [Min(0.05f)] public float slashSwingTime = 0.40f;
        [Min(0.05f)] public float slashFollowTime = 0.30f;

        [Header("Refs")]
        public PuppetCombatHealth playerHealth;
        public CombatSkillRecognizer playerSkills;
        public PuppetRopeController playerController;

        [Header("Debug")]
        public bool logStateChanges = true;

        public AIState State { get; private set; } = AIState.Guard;
        public SlashPhase CurrentSlashPhase { get; private set; } = SlashPhase.None;

        PuppetRig _rig;
        PuppetRopeController _controller;
        PuppetCombatHealth _health;
        CombatSkillRecognizer _skills;

        float _stateUntil;
        float _nextAttackTime;
        float _attackPhaseT0;
        float _pendingThreatAt = -1f;
        bool _defendAsParry;
        float _seed;
        float _slashSign = 1f;

        float SlashTotalDuration => slashPrepTime + slashLoadTime + slashSwingTime + slashFollowTime;

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
            CurrentSlashPhase = SlashPhase.None;
            _nextAttackTime = Time.time + attackCooldown * 0.5f;
            Enter(AIState.Guard, Random.Range(guardHoldMin, guardHoldMax));
        }

        public void NotifyKO()
        {
            _controller.SetInput(0.15f, 0.15f, 0f, 0f, 0f);
            CurrentSlashPhase = SlashPhase.None;
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
                    CurrentSlashPhase = SlashPhase.None;
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
                    CurrentSlashPhase = SlashPhase.None;
                    DriveApproach(now);
                    if (TryEnterDefend(now)) break;
                    if (now >= _stateUntil)
                        BeginSlash(now);
                    break;

                case AIState.Attack:
                    DriveHorizontalSlash(now);
                    if (now >= _stateUntil)
                    {
                        _nextAttackTime = now + attackCooldown;
                        CurrentSlashPhase = SlashPhase.None;
                        Enter(AIState.Recover, recoverTime);
                    }
                    break;

                case AIState.Recover:
                    CurrentSlashPhase = SlashPhase.None;
                    DriveRecover();
                    if (TryEnterDefend(now)) break;
                    if (now >= _stateUntil)
                        Enter(AIState.Guard, Random.Range(guardHoldMin, guardHoldMax));
                    break;

                case AIState.Defend:
                    CurrentSlashPhase = SlashPhase.None;
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
            _pendingThreatAt = Time.time + Random.Range(reactionTime * 0.75f, reactionTime * 1.25f);
        }

        bool TryEnterDefend(float now)
        {
            if (_pendingThreatAt < 0f || now < _pendingThreatAt) return false;
            _pendingThreatAt = -1f;

            // Never abort mid-swing — finish the readable slash.
            if (State == AIState.Attack) return false;

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
            if (Random.value < 0.40f)
                Enter(AIState.Approach, approachTime);
            else
                BeginSlash(now);
        }

        void BeginSlash(float now)
        {
            _attackPhaseT0 = now;
            _slashSign = Random.value < 0.5f ? 1f : -1f;
            CurrentSlashPhase = SlashPhase.None;
            Enter(AIState.Attack, SlashTotalDuration);
            // Apply Prep pose immediately so the wind-up is visible from frame 0.
            SetSlashPhase(SlashPhase.Prep);
            _controller.SetInput(0.90f, 0.85f, _slashSign * 0.35f, -0.55f, -4f);
            if (logStateChanges)
                Debug.Log($"[AI] {name}: HORIZONTAL SLASH start (sign={_slashSign:+0;-0})");
        }

        void DriveGuard()
        {
            _controller.SetInput(0.88f, 0.88f, 0.04f * Noise(0.35f), -0.65f, 0f);
        }

        void DriveApproach(float now)
        {
            float t = Mathf.InverseLerp(_stateUntil - approachTime, _stateUntil, now);
            float forward = Mathf.Lerp(0.60f, 0.90f, t);
            float rear = Mathf.Lerp(0.80f, 0.55f, t);
            float arm = Mathf.Lerp(-0.55f, -0.30f, t);
            _controller.SetInput(rear, forward, _slashSign * 0.12f, arm, -1f);
        }

        /// <summary>
        /// Readable Horizontal Slash driven only through SetInput:
        /// Prep (pull back) → Load (lean/momentum) → Swing (fast lateral cut)
        /// → Follow-through → Recover (outer state) → Guard.
        ///
        /// Arm retract stays moderate so the tip never rises into Overhead territory;
        /// the readable cut is the depth-axis lateral sweep + fast arm extend.
        /// </summary>
        void DriveHorizontalSlash(float now)
        {
            float t = now - _attackPhaseT0;
            float prepEnd = slashPrepTime;
            float loadEnd = prepEnd + slashLoadTime;
            float swingEnd = loadEnd + slashSwingTime;

            if (t < prepEnd)
            {
                SetSlashPhase(SlashPhase.Prep);
                // Pull sword back along the guard line + open outward — not a raise.
                float u = t / Mathf.Max(0.01f, slashPrepTime);
                float arm = Mathf.Lerp(-0.45f, -0.72f, u);
                float depth = Mathf.Lerp(0f, _slashSign * 0.65f, u);
                _controller.SetInput(0.90f, 0.85f, depth, arm, -6f);
            }
            else if (t < loadEnd)
            {
                SetSlashPhase(SlashPhase.Load);
                // Lean into the cut; keep tip below head height; coil the lateral swing.
                float u = (t - prepEnd) / Mathf.Max(0.01f, slashLoadTime);
                float depth = Mathf.Lerp(_slashSign * 0.65f, _slashSign * 0.25f, u);
                float forward = Mathf.Lerp(0.80f, 0.95f, u);
                float rear = Mathf.Lerp(0.90f, 0.55f, u);
                _controller.SetInput(rear, forward, depth, -0.72f, -2f);
            }
            else if (t < swingEnd)
            {
                SetSlashPhase(SlashPhase.Swing);
                // Fast arm extend + depth sweep across the opponent — the visible slash.
                float u = (t - loadEnd) / Mathf.Max(0.01f, slashSwingTime);
                float smooth = Mathf.SmoothStep(0f, 1f, u);
                float arm = Mathf.Lerp(-0.72f, 1.0f, smooth);
                float depth = Mathf.Lerp(_slashSign * 0.25f, -_slashSign * 0.85f, smooth);
                float armVel = Mathf.Lerp(10f, 18f, u);
                _controller.SetInput(0.55f, 0.98f, depth, arm, armVel);
            }
            else
            {
                SetSlashPhase(SlashPhase.FollowThrough);
                float u = (t - swingEnd) / Mathf.Max(0.01f, slashFollowTime);
                float arm = Mathf.Lerp(1.0f, 0.25f, u);
                float depth = Mathf.Lerp(-_slashSign * 0.85f, -_slashSign * 0.20f, u);
                float armVel = Mathf.Lerp(5f, 0.5f, u);
                _controller.SetInput(0.75f, 0.85f, depth, arm, armVel);
            }
        }

        void DriveRecover()
        {
            _controller.SetInput(0.85f, 0.85f, 0f, -0.45f, -3f);
        }

        void DriveDefend(float now)
        {
            if (_defendAsParry)
            {
                float remaining = _stateUntil - now;
                if (remaining > defendHoldTime * 0.35f)
                    _controller.SetInput(0.85f, 0.85f, 0f, 0.70f, 7f);
                else
                    _controller.SetInput(0.88f, 0.88f, 0f, -0.60f, -3f);
            }
            else
            {
                DriveGuard();
            }
        }

        void SetSlashPhase(SlashPhase phase)
        {
            if (CurrentSlashPhase == phase) return;
            CurrentSlashPhase = phase;
            if (logStateChanges)
                Debug.Log($"[AI] {name}: Slash {phase}");
        }

        float Noise(float freq)
        {
            return Mathf.Sin((Time.time + _seed) * freq * Mathf.PI * 2f);
        }

        void Enter(AIState next, float duration)
        {
            if (State != next && logStateChanges)
                Debug.Log($"[AI] {name}: {State} → {next}" +
                          (next == AIState.Defend ? (_defendAsParry ? " PARRY" : " GUARD") : ""));
            State = next;
            _stateUntil = Time.time + Mathf.Max(0.05f, duration);
        }
    }
}

using System;
using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Prototype HP / damage / KO. Damage only arrives through weapon collision
    /// resolution — never applied directly by AI or animation.
    /// </summary>
    [RequireComponent(typeof(PuppetRig))]
    public sealed class PuppetCombatHealth : MonoBehaviour
    {
        [Header("HP")]
        [Min(1f)] public float maxHP = 100f;

        [Header("Damage by HitQuality")]
        public float invalidDamage = 0f;
        public float glancingDamage = 4f;
        public float cleanDamage = 14f;
        public float heavyDamage = 26f;

        [Header("Body part multipliers")]
        public float headMultiplier = 1.55f;
        public float torsoMultiplier = 1.00f;
        public float armMultiplier = 0.55f;
        public float legMultiplier = 0.65f;
        public float unknownMultiplier = 0.80f;

        [Header("Impact scaling")]
        [Tooltip("Extra damage fraction from ImpactStrength above this baseline.")]
        public float impactBaseline = 3f;
        public float impactBonusPerUnit = 0.08f;
        public float maxImpactBonus = 0.55f;

        [Header("Hit contact tracking")]
        [Tooltip("Minimum time before the same attacker can damage this puppet again.")]
        [Min(0.05f)] public float hitCooldown = 0.55f;
        [Tooltip("While the same sword stays in continuous contact, no second damage.")]
        [Min(0.05f)] public float contactRefreshWindow = 0.20f;

        [Header("KO")]
        public bool logEvents = true;

        public float CurrentHP { get; private set; }
        public bool IsKO { get; private set; }
        public CombatHitReport LatestHit { get; private set; }
        public bool HasLatestHit => LatestHit.IsValid;

        public event Action<PuppetCombatHealth> OnKO;
        public event Action<CombatHitReport> OnHitTaken;

        PuppetRig _rig;
        PuppetRopeController _controller;
        PuppetRopeInput _input;
        PuppetAIOpponent _ai;

        float _lastDamageTime = -999f;
        int _lastAttackerId = -1;
        float _lastContactTime = -999f;

        void Awake()
        {
            _rig = GetComponent<PuppetRig>();
            _controller = GetComponent<PuppetRopeController>();
            _input = GetComponent<PuppetRopeInput>();
            _ai = GetComponent<PuppetAIOpponent>();
            CurrentHP = maxHP;
        }

        void OnEnable()
        {
            if (!IsKO) CurrentHP = Mathf.Clamp(CurrentHP, 0f, maxHP);
        }

        public float BodyMultiplier(BodyPartKind part)
        {
            switch (part)
            {
                case BodyPartKind.Head: return headMultiplier;
                case BodyPartKind.Torso: return torsoMultiplier;
                case BodyPartKind.Arm: return armMultiplier;
                case BodyPartKind.Leg: return legMultiplier;
                default: return unknownMultiplier;
            }
        }

        public float BaseDamageFor(HitQuality quality)
        {
            switch (quality)
            {
                case HitQuality.Glancing: return glancingDamage;
                case HitQuality.Clean: return cleanDamage;
                case HitQuality.Heavy: return heavyDamage;
                default: return invalidDamage;
            }
        }

        public float ComputeDamage(HitQuality quality, float impactStrength, BodyPartKind part)
        {
            float baseDmg = BaseDamageFor(quality);
            if (baseDmg <= 0.001f) return 0f;
            float impactBonus = Mathf.Clamp(
                (impactStrength - impactBaseline) * impactBonusPerUnit, 0f, maxImpactBonus);
            return baseDmg * BodyMultiplier(part) * (1f + impactBonus);
        }

        /// <summary>
        /// Returns false if this contact should not deal damage (KO / cooldown / same contact).
        /// </summary>
        public bool TryBeginHitWindow(int attackerInstanceId, float now)
        {
            if (IsKO) return false;
            if (attackerInstanceId == _lastAttackerId &&
                (now - _lastContactTime) <= contactRefreshWindow)
            {
                _lastContactTime = now;
                return false;
            }
            if (attackerInstanceId == _lastAttackerId && (now - _lastDamageTime) < hitCooldown)
                return false;
            return true;
        }

        public void RegisterContact(int attackerInstanceId, float now)
        {
            _lastAttackerId = attackerInstanceId;
            _lastContactTime = now;
        }

        public bool ApplyResolvedHit(in CombatHitReport report, int attackerInstanceId)
        {
            if (IsKO) return false;

            LatestHit = report;
            OnHitTaken?.Invoke(report);

            if (report.outcome == CombatOutcome.Block || report.outcome == CombatOutcome.Parry)
            {
                RegisterContact(attackerInstanceId, report.time);
                if (logEvents)
                    Debug.Log($"[Combat] {report.defenderName} {report.outcome} vs {report.skill}");
                return true;
            }

            if (report.damage <= 0.001f)
            {
                RegisterContact(attackerInstanceId, report.time);
                return true;
            }

            if (!TryBeginHitWindow(attackerInstanceId, report.time))
                return false;

            CurrentHP = Mathf.Max(0f, CurrentHP - report.damage);
            _lastDamageTime = report.time;
            RegisterContact(attackerInstanceId, report.time);

            if (logEvents)
            {
                Debug.Log(
                    $"[Combat Damage] {report.attackerName} → {report.defenderName} | " +
                    $"{report.skill} / {report.quality} / {report.bodyPart} | " +
                    $"impact={report.impactStrength:0.00} dmg={report.damage:0.0} | HP={CurrentHP:0.0}");
            }

            if (CurrentHP <= 0f && !IsKO)
                EnterKO();

            return true;
        }

        public void EnterKO()
        {
            if (IsKO) return;
            IsKO = true;
            CurrentHP = 0f;
            if (_controller != null) _controller.SetKO(true);
            if (_input != null) _input.enabled = false;
            if (_ai != null) _ai.NotifyKO();
            if (logEvents) Debug.Log($"[Combat KO] {name} KO");
            OnKO?.Invoke(this);
        }

        public void ResetRound()
        {
            IsKO = false;
            CurrentHP = maxHP;
            _lastDamageTime = -999f;
            _lastAttackerId = -1;
            _lastContactTime = -999f;
            LatestHit = default;
            if (_controller != null) _controller.SetKO(false);
            if (_input != null) _input.enabled = true;
            if (_ai != null)
            {
                _ai.enabled = true;
                _ai.ResetAI();
            }
            if (logEvents) Debug.Log($"[Combat] {name} round reset — HP={CurrentHP:0}");
        }
    }
}

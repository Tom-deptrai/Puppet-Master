using System;
using UnityEngine;

namespace PuppetMaster.Prototype
{
    public enum ImpactCategory
    {
        Weak,
        Medium,
        Strong
    }

    [Serializable]
    public struct CombatImpactData
    {
        public string bodyPart;
        public Vector3 contactPoint;
        public Vector3 normal;
        public Vector3 relativeVelocity;
        public float relativeSpeed;
        public float impactStrength;
        public ImpactCategory category;
        public float time;
    }

    /// <summary>
    /// Phase 1 combat loop: sword collisions resolve as BODY HIT / BLOCK / PARRY,
    /// then HitQuality + damage through PuppetCombatHealth. Sword-vs-sword uses the
    /// native contact solver; parry adds an extra deflect impulse on the attacker blade.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SwordCollisionHandler : MonoBehaviour
    {
        [Header("Classification Thresholds")]
        public float weakThreshold = 2.0f;
        public float mediumThreshold = 5.0f;

        [Header("Impulse & Reaction")]
        public float impulseForceScale = 1.8f;
        public float maxImpulse = 35.0f;
        public float hitCooldown = 0.15f;
        [Range(0f, 1f)] public float blockBodyImpulseScale = 0.15f;

        [Header("Block")]
        [Tooltip("Minimum clash relative speed to register a deliberate BLOCK.")]
        public float blockMinRelativeSpeed = 0.8f;

        [Header("Parry")]
        public float parryMinRelativeSpeed = 2.4f;
        public float parryMinDefenderTipSpeed = 1.1f;
        public float parryMinDefenderArmVelocity = 2.0f;
        [Range(0f, 1f)] public float parryMinBladeCross = 0.25f;
        [Tooltip("Defender tip velocity must oppose the attack direction at least this much.")]
        [Range(-1f, 1f)] public float parryMinOpposingDot = 0.05f;
        public float parryImpulse = 9.5f;
        public float parryMaxImpulse = 18f;
        public float parryAttackerTorque = 14f;

        [Header("Hit quality")]
        public CombatHitQuality.Settings hitQuality = CombatHitQuality.Settings.Default;

        [Header("Debug")]
        public bool logHits = true;

        public CombatImpactData LatestImpact { get; private set; }
        public CombatHitReport LatestCombatHit { get; private set; }
        public bool HasImpact => _hasImpact;
        public bool HasCombatHit => LatestCombatHit.IsValid;

        Rigidbody _swordRb;
        PuppetRig _ownerRig;
        PuppetCombatHealth _ownerHealth;
        CombatSkillRecognizer _ownerSkills;
        PuppetRopeController _ownerController;
        Transform _ownerRoot;
        float _lastHitTime = -999f;
        Collider _lastHitCollider;
        bool _hasImpact;
        int _attackerId;
        static int _nextAttackerId = 1;

        void Awake()
        {
            _swordRb = GetComponent<Rigidbody>();
            _attackerId = _nextAttackerId++;
            CacheOwner(GetComponentInParent<PuppetRig>());
        }

        public void SetOwner(PuppetRig rig) => CacheOwner(rig);

        void CacheOwner(PuppetRig rig)
        {
            _ownerRig = rig;
            if (rig != null)
            {
                _ownerRoot = rig.transform;
                _ownerHealth = rig.GetComponent<PuppetCombatHealth>();
                _ownerSkills = rig.skillRecognition != null
                    ? rig.skillRecognition
                    : rig.GetComponent<CombatSkillRecognizer>();
                _ownerController = rig.GetComponent<PuppetRopeController>();
            }
            else
            {
                _ownerRoot = transform.root;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (_ownerRoot != null && collision.transform.IsChildOf(_ownerRoot))
                return;
            if (collision.gameObject.name.Contains("Ground") || collision.gameObject.name.Contains("Rail"))
                return;

            float now = Time.time;
            if (collision.collider == _lastHitCollider && (now - _lastHitTime) < hitCooldown)
                return;
            if (collision.contactCount == 0)
                return;

            if (_ownerHealth != null && _ownerHealth.IsKO)
                return;

            ContactPoint contact = collision.GetContact(0);
            Rigidbody targetRb = collision.rigidbody;

            Vector3 swordPointVel = _swordRb != null ? _swordRb.GetPointVelocity(contact.point) : Vector3.zero;
            Vector3 targetPointVel = targetRb != null ? targetRb.GetPointVelocity(contact.point) : Vector3.zero;
            Vector3 relVel = swordPointVel - targetPointVel;
            float relSpeed = relVel.magnitude;
            float dotNormal = Mathf.Abs(Vector3.Dot(relVel, contact.normal));
            float effectiveSpeed = Mathf.Max(dotNormal, relSpeed * 0.5f);
            float mass = _swordRb != null ? _swordRb.mass : 1.2f;
            float strength = mass * effectiveSpeed;

            ImpactCategory cat = strength < weakThreshold ? ImpactCategory.Weak
                : strength < mediumThreshold ? ImpactCategory.Medium
                : ImpactCategory.Strong;

            // ---- sword vs sword: Block / Parry / Clash ----
            if (targetRb != null && IsSwordBody(targetRb))
            {
                ResolveSwordClash(collision, contact, targetRb, relVel, relSpeed, strength, cat, now);
                return;
            }

            // ---- sword vs body ----
            ResolveBodyHit(collision, contact, targetRb, relVel, relSpeed, strength, cat, now);
        }

        void ResolveSwordClash(
            Collision collision, ContactPoint contact, Rigidbody defenderSword,
            Vector3 relVel, float relSpeed, float strength, ImpactCategory cat, float now)
        {
            var defenderRig = defenderSword.GetComponentInParent<PuppetRig>();
            var defenderSkills = defenderRig != null
                ? (defenderRig.skillRecognition != null
                    ? defenderRig.skillRecognition
                    : defenderRig.GetComponent<CombatSkillRecognizer>())
                : null;
            var defenderHealth = defenderRig != null
                ? defenderRig.GetComponent<PuppetCombatHealth>()
                : null;
            var defenderController = defenderRig != null
                ? defenderRig.GetComponent<PuppetRopeController>()
                : null;

            if (defenderHealth != null && defenderHealth.IsKO)
            {
                if (logHits) Debug.Log("WEAPON CLASH (defender KO)");
                return;
            }

            // Both swords receive OnCollisionEnter. Only the attacking blade resolves
            // Block/Parry so we do not double-count or invert defender roles.
            if (!IsClashAttacker(defenderSkills))
                return;

            Vector3 attackerBlade = transform.up;
            Vector3 defenderBlade = defenderSword.transform.up;
            float bladeCross = Vector3.Cross(attackerBlade.normalized, defenderBlade.normalized).magnitude;

            float defenderTip = defenderSkills != null ? defenderSkills.TipSpeed : 0f;
            float defenderArmVel = defenderController != null
                ? Mathf.Abs(defenderController.EffectiveArmVelocity)
                : 0f;
            bool defenderGuarding = defenderSkills != null && defenderSkills.IsGuarding;

            Vector3 attackDir = relSpeed > 0.05f ? relVel.normalized : (_ownerRig != null
                ? Vector3.right * _ownerRig.facingSign
                : Vector3.right);
            // Defender tip velocity opposing the attack (meeting the cut).
            Vector3 defenderTipVel = EstimateTipVelocity(defenderSword, defenderSkills);
            float opposing = -Vector3.Dot(defenderTipVel.normalized, attackDir);
            if (defenderTipVel.sqrMagnitude < 0.01f) opposing = 0f;

            bool activeParryMotion =
                defenderTip >= parryMinDefenderTipSpeed ||
                defenderArmVel >= parryMinDefenderArmVelocity;
            // Standing still in Guard is Block, not Parry.
            bool notPassiveGuardOnly = !defenderGuarding || activeParryMotion;

            bool parry = activeParryMotion &&
                         notPassiveGuardOnly &&
                         relSpeed >= parryMinRelativeSpeed &&
                         bladeCross >= parryMinBladeCross &&
                         opposing >= parryMinOpposingDot &&
                         !defenderGuarding; // Guard-held = Block priority

            // If actively moving into the blow while also near guard pose, still allow parry
            // when tip/arm velocity clearly exceeds guard stillness.
            if (!parry && activeParryMotion && !defenderGuarding &&
                relSpeed >= parryMinRelativeSpeed && bladeCross >= parryMinBladeCross)
                parry = true;

            // Re-evaluate: if guarding AND actively beating into the attack, prefer Parry.
            if (defenderGuarding && activeParryMotion &&
                defenderTip >= parryMinDefenderTipSpeed * 1.15f &&
                relSpeed >= parryMinRelativeSpeed &&
                bladeCross >= parryMinBladeCross &&
                opposing >= parryMinOpposingDot)
                parry = true;

            bool block = !parry && defenderGuarding && relSpeed >= blockMinRelativeSpeed;

            CombatOutcome outcome = CombatOutcome.Clash;
            if (parry) outcome = CombatOutcome.Parry;
            else if (block) outcome = CombatOutcome.Block;

            if (parry)
                ApplyParryDeflection(contact, defenderSword, relVel, strength);

            _lastHitTime = now;
            _lastHitCollider = collision.collider;
            _hasImpact = true;
            LatestImpact = new CombatImpactData
            {
                bodyPart = defenderSword.name,
                contactPoint = contact.point,
                normal = contact.normal,
                relativeVelocity = relVel,
                relativeSpeed = relSpeed,
                impactStrength = strength,
                category = cat,
                time = now
            };

            var report = new CombatHitReport
            {
                attackerName = _ownerRig != null ? _ownerRig.name : name,
                defenderName = defenderRig != null ? defenderRig.name : defenderSword.name,
                skill = _ownerSkills != null ? _ownerSkills.CurrentSkill : CombatSkill.None,
                quality = HitQuality.Invalid,
                impactCategory = cat,
                impactStrength = strength,
                bodyPart = BodyPartKind.Arm,
                bodyPartName = defenderSword.name,
                damage = 0f,
                outcome = outcome,
                time = now
            };
            LatestCombatHit = report;
            CombatMatchController.NotifyHit(report);

            if (defenderHealth != null)
                defenderHealth.ApplyResolvedHit(report, _attackerId);

            if (logHits)
            {
                if (outcome == CombatOutcome.Parry)
                    Debug.Log($"PARRY | rel={relSpeed:0.00} tipDef={defenderTip:0.00} cross={bladeCross:0.00} opp={opposing:0.00}");
                else if (outcome == CombatOutcome.Block)
                    Debug.Log($"BLOCK | rel={relSpeed:0.00} guard=ON strength={strength:0.00}");
                else
                    Debug.Log("WEAPON CLASH");
            }
        }

        void ApplyParryDeflection(ContactPoint contact, Rigidbody defenderSword, Vector3 relVel, float strength)
        {
            if (_swordRb == null || _swordRb.isKinematic) return;

            Vector3 away = (_swordRb.worldCenterOfMass - defenderSword.worldCenterOfMass);
            if (away.sqrMagnitude < 1e-4f)
                away = contact.normal;
            away.Normalize();

            // Prefer deflecting along the attacker's incoming direction reversed + contact normal.
            Vector3 deflect = (-relVel.normalized * 0.55f + away * 0.45f + contact.normal * 0.35f).normalized;
            float mag = Mathf.Clamp(parryImpulse + strength * 0.35f, parryImpulse * 0.6f, parryMaxImpulse);
            _swordRb.AddForceAtPosition(deflect * mag, contact.point, ForceMode.Impulse);

            Vector3 torqueAxis = Vector3.Cross(Vector3.up, deflect);
            if (torqueAxis.sqrMagnitude > 1e-4f)
                _swordRb.AddTorque(torqueAxis.normalized * parryAttackerTorque, ForceMode.Impulse);
        }

        void ResolveBodyHit(
            Collision collision, ContactPoint contact, Rigidbody targetRb,
            Vector3 relVel, float relSpeed, float strength, ImpactCategory cat, float now)
        {
            string partName = collision.collider != null
                ? collision.collider.gameObject.name
                : collision.gameObject.name;
            if (targetRb != null)
                partName = targetRb.gameObject.name;

            BodyPartKind partKind = BodyPartUtil.Classify(partName);
            CombatSkill skill = _ownerSkills != null ? _ownerSkills.CurrentSkill : CombatSkill.None;
            Vector3 facing = _ownerRig != null ? Vector3.right * _ownerRig.facingSign : Vector3.right;
            Vector3 bladeDir = transform.up;

            HitQuality quality = CombatHitQuality.Evaluate(
                skill, strength, cat, bladeDir, relVel, facing, partKind, hitQuality);

            var defenderRig = targetRb != null
                ? targetRb.GetComponentInParent<PuppetRig>()
                : collision.transform.GetComponentInParent<PuppetRig>();
            var defenderHealth = defenderRig != null
                ? defenderRig.GetComponent<PuppetCombatHealth>()
                : null;

            if (defenderHealth != null && defenderHealth.IsKO)
                return;

            float damage = 0f;
            if (defenderHealth != null)
                damage = defenderHealth.ComputeDamage(quality, strength, partKind);

            // Physical hit reaction — scaled down dramatically if this would be a
            // blocked residual (body hits while defender guards are rare; swords
            // usually catch first). Keep full impulse for real body hits.
            if (targetRb != null && !targetRb.isKinematic)
            {
                Vector3 pushDir = relSpeed > 0.05f ? relVel.normalized : -contact.normal;
                float impulseMag = Mathf.Clamp(strength * impulseForceScale, 0.4f, maxImpulse);
                if (defenderRig != null)
                {
                    var defSkills = defenderRig.skillRecognition != null
                        ? defenderRig.skillRecognition
                        : defenderRig.GetComponent<CombatSkillRecognizer>();
                    if (defSkills != null && defSkills.IsGuarding)
                        impulseMag *= blockBodyImpulseScale;
                }
                targetRb.AddForceAtPosition(pushDir * impulseMag, contact.point, ForceMode.Impulse);
            }

            _lastHitTime = now;
            _lastHitCollider = collision.collider;
            _hasImpact = true;
            LatestImpact = new CombatImpactData
            {
                bodyPart = partName,
                contactPoint = contact.point,
                normal = contact.normal,
                relativeVelocity = relVel,
                relativeSpeed = relSpeed,
                impactStrength = strength,
                category = cat,
                time = now
            };

            var report = new CombatHitReport
            {
                attackerName = _ownerRig != null ? _ownerRig.name : name,
                defenderName = defenderRig != null ? defenderRig.name : partName,
                skill = skill,
                quality = quality,
                impactCategory = cat,
                impactStrength = strength,
                bodyPart = partKind,
                bodyPartName = partName,
                damage = damage,
                outcome = CombatOutcome.BodyHit,
                time = now
            };
            LatestCombatHit = report;
            CombatMatchController.NotifyHit(report);

            if (defenderHealth != null)
                defenderHealth.ApplyResolvedHit(report, _attackerId);

            if (logHits)
            {
                Debug.Log(
                    $"[Combat Impact] Hit: <b>{partName}</b> | Skill: <b>{skill}</b> | " +
                    $"Quality: <b>{quality}</b> | RelVel: <b>{relSpeed:0.00}</b> | " +
                    $"Strength: <b>{strength:0.00}</b> | Dmg: <b>{damage:0.0}</b> | <b>{cat}</b>");
            }
        }

        static bool IsSwordBody(Rigidbody rb)
        {
            if (rb == null) return false;
            return rb.name.StartsWith("Sword_", StringComparison.Ordinal);
        }

        static bool IsAttackSkill(CombatSkill skill) =>
            skill == CombatSkill.HorizontalSlash ||
            skill == CombatSkill.Thrust ||
            skill == CombatSkill.OverheadStrike;

        bool IsClashAttacker(CombatSkillRecognizer defenderSkills)
        {
            bool iAttack = _ownerSkills != null && IsAttackSkill(_ownerSkills.CurrentSkill);
            bool theyAttack = defenderSkills != null && IsAttackSkill(defenderSkills.CurrentSkill);
            float myTip = _ownerSkills != null ? _ownerSkills.TipSpeed : 0f;
            float theirTip = defenderSkills != null ? defenderSkills.TipSpeed : 0f;

            if (iAttack && !theyAttack) return true;
            if (!iAttack && theyAttack) return false;
            if (iAttack && theyAttack) return myTip >= theirTip;

            // Neither recognised: higher tip speed claims the clash (Block still needs
            // the other side to be Guarding, which is checked later).
            if (myTip + theirTip < 0.2f) return true; // either may log WEAPON CLASH
            return myTip >= theirTip;
        }

        static Vector3 EstimateTipVelocity(Rigidbody sword, CombatSkillRecognizer skills)
        {
            if (sword == null) return Vector3.zero;
            float tipOffset = skills != null ? skills.bladeTipOffset : 0.81f;
            Vector3 tip = sword.transform.TransformPoint(0f, tipOffset, 0f);
            return sword.GetPointVelocity(tip);
        }
    }
}

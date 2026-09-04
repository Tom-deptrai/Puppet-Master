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
    /// Phase 1 Combat Prototype: Physical Weapon Collision & Impact Strength.
    /// Attached to the sword Rigidbody.
    /// Captures OnCollisionEnter against targets, computes relative velocity at the contact point,
    /// calculates ImpactStrength = mass * normalSpeed, classifies as Weak / Medium / Strong,
    /// and imparts a physical impulse to the hit body part.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SwordCollisionHandler : MonoBehaviour
    {
        [Header("Classification Thresholds")]
        [Tooltip("ImpactStrength below this value is classified as WEAK")]
        public float weakThreshold = 2.0f;

        [Tooltip("ImpactStrength below this value (and >= weakThreshold) is MEDIUM, above or equal is STRONG")]
        public float mediumThreshold = 5.0f;

        [Header("Impulse & Reaction")]
        [Tooltip("Multiplier from ImpactStrength to applied physical impulse (N*s)")]
        public float impulseForceScale = 1.8f;

        [Tooltip("Maximum impulse magnitude to avoid joint overstrain / explosion")]
        public float maxImpulse = 35.0f;

        [Tooltip("Minimum time between impacts on the same target to prevent frame jitter")]
        public float hitCooldown = 0.15f;

        [Header("Debug")]
        public bool logHits = true;

        public CombatImpactData LatestImpact { get; private set; }
        public bool HasImpact => _hasImpact;

        Rigidbody _swordRb;
        PuppetRig _ownerRig;
        Transform _ownerRoot;
        float _lastHitTime = -999f;
        Collider _lastHitCollider;
        bool _hasImpact;

        void Awake()
        {
            _swordRb = GetComponent<Rigidbody>();
            _ownerRig = GetComponentInParent<PuppetRig>();
            if (_ownerRig != null)
                _ownerRoot = _ownerRig.transform;
            else
                _ownerRoot = transform.root;
        }

        public void SetOwner(PuppetRig rig)
        {
            _ownerRig = rig;
            if (rig != null)
                _ownerRoot = rig.transform;
        }

        void OnCollisionEnter(Collision collision)
        {
            // Ignore collisions with own puppet body parts
            if (_ownerRoot != null && collision.transform.IsChildOf(_ownerRoot))
                return;

            // Ignore ground / floor / rail collisions
            if (collision.gameObject.name.Contains("Ground") || collision.gameObject.name.Contains("Rail"))
                return;

            float now = Time.time;
            if (collision.collider == _lastHitCollider && (now - _lastHitTime) < hitCooldown)
                return;

            if (collision.contactCount == 0)
                return;

            ContactPoint contact = collision.GetContact(0);
            Rigidbody targetRb = collision.rigidbody;

            // Compute velocities at contact point
            Vector3 swordPointVel = _swordRb != null ? _swordRb.GetPointVelocity(contact.point) : Vector3.zero;
            Vector3 targetPointVel = targetRb != null ? targetRb.GetPointVelocity(contact.point) : Vector3.zero;
            Vector3 relVel = swordPointVel - targetPointVel;
            float relSpeed = relVel.magnitude;

            // Normal speed: component of relative velocity acting into the contact surface
            float dotNormal = Mathf.Abs(Vector3.Dot(relVel, contact.normal));
            // Blend with linear speed so sweeping/glancing attacks still carry substantial momentum
            float effectiveSpeed = Mathf.Max(dotNormal, relSpeed * 0.5f);

            float mass = _swordRb != null ? _swordRb.mass : 1.2f;
            float strength = mass * effectiveSpeed;

            // Classification
            ImpactCategory cat;
            if (strength < weakThreshold)
                cat = ImpactCategory.Weak;
            else if (strength < mediumThreshold)
                cat = ImpactCategory.Medium;
            else
                cat = ImpactCategory.Strong;

            // Identify body part
            string partName = collision.collider != null ? collision.collider.gameObject.name : collision.gameObject.name;
            if (collision.rigidbody != null && collision.rigidbody.gameObject != null)
                partName = collision.rigidbody.gameObject.name;

            // Physical Hit Reaction: apply impulse to target body part
            if (targetRb != null && !targetRb.isKinematic)
            {
                Vector3 pushDir = relSpeed > 0.05f ? relVel.normalized : -contact.normal;
                float impulseMag = Mathf.Clamp(strength * impulseForceScale, 0.4f, maxImpulse);
                targetRb.AddForceAtPosition(pushDir * impulseMag, contact.point, ForceMode.Impulse);
            }

            // Record data
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

            if (logHits)
            {
                Debug.Log($"[Combat Impact] Hit: <b>{partName}</b> | RelVel: <b>{relSpeed:0.00} m/s</b> | Strength: <b>{strength:0.00}</b> | Category: <b>{cat}</b>");
            }
        }
    }
}

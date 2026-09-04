using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Combines CombatSkill + ImpactStrength + blade alignment + body part +
    /// contact relative velocity into a HitQuality. Skill-less flailing is forced
    /// toward Invalid / Glancing so random swinging cannot outpace intentional strikes.
    /// </summary>
    public static class CombatHitQuality
    {
        [System.Serializable]
        public struct Settings
        {
            [Header("Impact gates")]
            public float glancingMaxImpact;
            public float cleanMinImpact;
            public float heavyMinImpact;

            [Header("Direction / alignment")]
            [Range(0f, 1f)] public float slashMinLateralFraction;
            [Range(0f, 1f)] public float thrustMinAlongBlade;
            [Range(0f, 1f)] public float thrustMinBladeFacing;
            [Range(0f, 1f)] public float overheadMinDownFraction;
            [Range(0f, 1f)] public float minTowardTarget;

            public static Settings Default => new Settings
            {
                glancingMaxImpact = 2.2f,
                cleanMinImpact = 3.0f,
                heavyMinImpact = 6.0f,
                slashMinLateralFraction = 0.45f,
                thrustMinAlongBlade = 0.55f,
                thrustMinBladeFacing = 0.55f,
                overheadMinDownFraction = 0.40f,
                minTowardTarget = 0.15f,
            };
        }

        public static HitQuality Evaluate(
            CombatSkill skill,
            float impactStrength,
            ImpactCategory category,
            Vector3 bladeDirection,
            Vector3 relativeVelocity,
            Vector3 attackerFacing,
            BodyPartKind bodyPart,
            in Settings settings)
        {
            float relSpeed = relativeVelocity.magnitude;
            Vector3 relDir = relSpeed > 0.05f ? relativeVelocity / relSpeed : attackerFacing;
            float towardTarget = Vector3.Dot(relDir, attackerFacing);
            float alongBlade = relSpeed > 0.05f
                ? Vector3.Dot(relDir, bladeDirection.normalized)
                : 0f;
            float bladeFacing = Vector3.Dot(bladeDirection.normalized, attackerFacing);
            float lateral = Mathf.Abs(Vector3.Dot(relDir, Vector3.forward));
            float down = Mathf.Max(0f, -relDir.y);

            // No recognised skill: only a tiny glancing nick is possible, and only
            // if the impact itself is already meaningful. Pure flailing does nothing.
            if (skill == CombatSkill.None || skill == CombatSkill.Guard)
            {
                if (impactStrength >= settings.cleanMinImpact && towardTarget >= settings.minTowardTarget)
                    return HitQuality.Glancing;
                return HitQuality.Invalid;
            }

            bool directionOk = false;
            switch (skill)
            {
                case CombatSkill.HorizontalSlash:
                    directionOk = lateral >= settings.slashMinLateralFraction * 0.5f &&
                                  towardTarget >= -0.15f &&
                                  Mathf.Abs(alongBlade) < 0.85f;
                    break;
                case CombatSkill.Thrust:
                    directionOk = alongBlade >= settings.thrustMinAlongBlade &&
                                  bladeFacing >= settings.thrustMinBladeFacing &&
                                  towardTarget >= settings.minTowardTarget;
                    break;
                case CombatSkill.OverheadStrike:
                    directionOk = down >= settings.overheadMinDownFraction &&
                                  towardTarget >= -0.05f;
                    break;
            }

            // Edge / wrong-side contact against limbs still counts but is weaker.
            bool limbEdge = bodyPart == BodyPartKind.Arm || bodyPart == BodyPartKind.Leg;
            if (!directionOk)
            {
                if (impactStrength >= settings.cleanMinImpact)
                    return HitQuality.Glancing;
                return HitQuality.Invalid;
            }

            if (impactStrength < settings.glancingMaxImpact || category == ImpactCategory.Weak)
                return HitQuality.Glancing;

            if (impactStrength >= settings.heavyMinImpact && category == ImpactCategory.Strong && !limbEdge)
                return HitQuality.Heavy;

            if (impactStrength >= settings.heavyMinImpact && category == ImpactCategory.Strong && limbEdge)
                return HitQuality.Clean;

            if (impactStrength >= settings.cleanMinImpact || category >= ImpactCategory.Medium)
                return HitQuality.Clean;

            return HitQuality.Glancing;
        }
    }
}

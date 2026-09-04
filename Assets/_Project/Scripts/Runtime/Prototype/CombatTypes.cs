using System;
using UnityEngine;

namespace PuppetMaster.Prototype
{
    public enum HitQuality
    {
        Invalid,
        Glancing,
        Clean,
        Heavy,
    }

    public enum CombatOutcome
    {
        BodyHit,
        Block,
        Parry,
        Clash,
    }

    public enum BodyPartKind
    {
        Unknown,
        Head,
        Torso,
        Arm,
        Leg,
    }

    [Serializable]
    public struct CombatHitReport
    {
        public string attackerName;
        public string defenderName;
        public CombatSkill skill;
        public HitQuality quality;
        public ImpactCategory impactCategory;
        public float impactStrength;
        public BodyPartKind bodyPart;
        public string bodyPartName;
        public float damage;
        public CombatOutcome outcome;
        public float time;

        public bool IsValid => time > 0f;
    }

    public static class BodyPartUtil
    {
        public static BodyPartKind Classify(string partName)
        {
            if (string.IsNullOrEmpty(partName)) return BodyPartKind.Unknown;
            string n = partName;
            if (n.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0) return BodyPartKind.Head;
            if (n.IndexOf("Torso", StringComparison.OrdinalIgnoreCase) >= 0) return BodyPartKind.Torso;
            if (n.IndexOf("Pelvis", StringComparison.OrdinalIgnoreCase) >= 0) return BodyPartKind.Torso;
            if (n.IndexOf("Arm", StringComparison.OrdinalIgnoreCase) >= 0) return BodyPartKind.Arm;
            if (n.IndexOf("Hand", StringComparison.OrdinalIgnoreCase) >= 0) return BodyPartKind.Arm;
            if (n.IndexOf("Deltoid", StringComparison.OrdinalIgnoreCase) >= 0) return BodyPartKind.Arm;
            if (n.IndexOf("Leg", StringComparison.OrdinalIgnoreCase) >= 0) return BodyPartKind.Leg;
            if (n.IndexOf("Foot", StringComparison.OrdinalIgnoreCase) >= 0) return BodyPartKind.Leg;
            return BodyPartKind.Unknown;
        }
    }
}

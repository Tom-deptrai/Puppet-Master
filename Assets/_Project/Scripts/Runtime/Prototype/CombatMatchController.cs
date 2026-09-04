using UnityEngine;
using UnityEngine.InputSystem;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Lightweight scene hub for the combat prototype: shared last-hit readout and
    /// round reset (R). Does not network or manage matchmaking.
    /// </summary>
    public sealed class CombatMatchController : MonoBehaviour
    {
        public PuppetCombatHealth playerHealth;
        public PuppetCombatHealth opponentHealth;
        public Key resetKey = Key.R;

        public static CombatMatchController Instance { get; private set; }
        public CombatHitReport LastHit { get; private set; }
        public bool HasLastHit => LastHit.IsValid;

        void Awake()
        {
            Instance = this;
            if (playerHealth == null || opponentHealth == null)
                AutoWire();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb[resetKey].wasPressedThisFrame)
                ResetRound();
        }

        public void AutoWire()
        {
            var all = FindObjectsByType<PuppetCombatHealth>(FindObjectsSortMode.None);
            foreach (var h in all)
            {
                var rig = h.GetComponent<PuppetRig>();
                if (rig == null) continue;
                if (rig.side == PlayerSide.Left && playerHealth == null)
                    playerHealth = h;
                else if (rig.side == PlayerSide.Right && opponentHealth == null)
                    opponentHealth = h;
            }
            if (playerHealth == null && all.Length > 0) playerHealth = all[0];
            if (opponentHealth == null && all.Length > 1) opponentHealth = all[1];
        }

        public static void NotifyHit(in CombatHitReport report)
        {
            if (Instance != null)
                Instance.LastHit = report;
        }

        public void ResetRound()
        {
            if (playerHealth == null || opponentHealth == null)
                AutoWire();
            playerHealth?.ResetRound();
            opponentHealth?.ResetRound();
            LastHit = default;
            Debug.Log("[Combat] Round reset (R)");
        }
    }
}

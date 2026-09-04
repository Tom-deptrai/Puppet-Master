using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1.2 prototype. Bare IMGUI overlay so the 4-axis mechanic can be
    /// judged. Lean is reported in the FACING frame (forward = toward opponent)
    /// and the camera-depth frame (outward = toward camera). Not meant to be pretty.
    /// </summary>
    public sealed class PuppetDebugHUD : MonoBehaviour
    {
        public PuppetRopeController controller;
        public PuppetRig rig;
        public PuppetRopeInput input;
        public PuppetCombatHealth playerHealth;
        public PuppetCombatHealth opponentHealth;
        public PuppetAIOpponent opponentAI;
        public CombatMatchController match;
        public bool drawZones = true;

        static readonly Color LeftTint = new(0.30f, 0.60f, 1f);
        static readonly Color RightTint = new(1f, 0.55f, 0.30f);

        Texture2D _px;
        GUIStyle _label, _header, _small;

        void Awake()
        {
            _px = new Texture2D(1, 1);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
        }

        void Start()
        {
            if (playerHealth == null && rig != null)
                playerHealth = rig.combatHealth != null ? rig.combatHealth : rig.GetComponent<PuppetCombatHealth>();
            if (match == null)
                match = FindFirstObjectByType<CombatMatchController>();
            if (match != null)
            {
                if (playerHealth == null) playerHealth = match.playerHealth;
                if (opponentHealth == null) opponentHealth = match.opponentHealth;
            }
            if (opponentHealth == null)
            {
                foreach (var h in FindObjectsByType<PuppetCombatHealth>(FindObjectsSortMode.None))
                {
                    if (h == playerHealth) continue;
                    opponentHealth = h;
                    opponentAI = h.GetComponent<PuppetAIOpponent>();
                    break;
                }
            }
        }

        void OnDestroy()
        {
            if (_px != null) Destroy(_px);
        }

        void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            _header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true };
        }

        void OnGUI()
        {
            EnsureStyles();
            float w = Screen.width, h = Screen.height;

            bool facesRight = rig != null && rig.side == PlayerSide.Left;
            string facing = facesRight ? "+X ▶ (opponent right)" : "◀ -X (opponent left)";

            if (drawZones)
            {
                GUI.color = new Color(LeftTint.r, LeftTint.g, LeftTint.b, 0.05f);
                GUI.DrawTexture(new Rect(0, 0, w * 0.5f, h), _px);
                GUI.color = new Color(RightTint.r, RightTint.g, RightTint.b, 0.05f);
                GUI.DrawTexture(new Rect(w * 0.5f, 0, w * 0.5f, h), _px);
                GUI.color = new Color(1, 1, 1, 0.3f);
                GUI.DrawTexture(new Rect(w * 0.5f - 1, 0, 2, h), _px);
                GUI.color = Color.white;
                GUI.Label(new Rect(w * 0.5f - 240, h - 28, 230, 20),
                    "◀ LEFT THUMB   ↕rope L  ↔xL", _small);
                GUI.Label(new Rect(w * 0.5f + 12, h - 28, 260, 20),
                    "RIGHT THUMB ▶   ↕rope R  ↔xR", _small);
            }

            var panel = new Rect(12, 36, 390, 560);
            GUI.color = new Color(0, 0, 0, 0.65f);
            GUI.DrawTexture(panel, _px);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(panel.x + 12, panel.y + 8, panel.width - 24, panel.height - 16));
            string sideStr = rig != null ? rig.side.ToString() : "?";
            GUILayout.Label($"PUPPET — Combat Loop Prototype   <size=11>[{sideStr} · faces {facing}]</size>", _header);

            // ---- HP / KO ----
            float pHP = playerHealth != null ? playerHealth.CurrentHP : -1f;
            float oHP = opponentHealth != null ? opponentHealth.CurrentHP : -1f;
            bool pKO = playerHealth != null && playerHealth.IsKO;
            bool oKO = opponentHealth != null && opponentHealth.IsKO;
            GUILayout.Label(
                $"Player HP: <b>{(pHP >= 0f ? pHP.ToString("0") : "?")}</b>/100" +
                (pKO ? "  <color=#ff4444><b>PLAYER KO</b></color>" : ""), _label);
            GUILayout.Label(
                $"Target HP: <b>{(oHP >= 0f ? oHP.ToString("0") : "?")}</b>/100" +
                (oKO ? "  <color=#ff4444><b>TARGET KO</b></color>" : ""), _label);
            if (opponentAI != null)
                GUILayout.Label($"AI State: <b>{opponentAI.State}</b>" +
                    (opponentAI.State == PuppetAIOpponent.AIState.Attack
                        ? $" / <b>{opponentAI.CurrentSlashPhase}</b>"
                        : ""), _small);

            if (controller != null)
            {
                Bar("Left rope", controller.LeftTension, LeftTint);
                Bar("Right rope", controller.RightTension, RightTint);

                float depthIn = input != null ? input.DepthValue : controller.DepthValue;
                SignedBar("Depth input", depthIn, new Color(0.55f, 0.85f, 0.55f));

                float armIn = input != null ? input.RightArmValue : controller.ArmValue;
                SignedBar("Sword arm", armIn, new Color(1f, 0.45f, 0.35f));

                float fwd = controller.ForwardLeanDeg;
                float dep = controller.DepthLeanDeg;
                float mag = controller.CombinedLeanDeg;

                GUILayout.Label(
                    $"Fwd/Back : <b>{fwd:+0.0;-0.0;0.0}°</b>  {(Mathf.Abs(fwd) < 2f ? "—" : fwd > 0 ? "FORWARD" : "BACKWARD")}", _label);
                GUILayout.Label(
                    $"In/Out   : <b>{dep:+0.0;-0.0;0.0}°</b>  {(Mathf.Abs(dep) < 2f ? "—" : dep > 0 ? "OUTWARD" : "INWARD")}", _label);
                GUILayout.Label($"Lean magnitude : <b>{mag:0.0}°</b>", _label);

                float pelvisY = controller.PelvisHeight;
                float standY = rig != null ? Mathf.Max(0.01f, rig.standingPelvisHeight) : 1f;
                GUILayout.Label($"Pelvis : <b>{pelvisY:0.00}</b> m  (<b>{pelvisY / standY * 100f:0}%</b> standing)", _label);

                var skills = rig != null ? rig.skillRecognition : null;
                if (skills == null)
                    skills = GetComponent<CombatSkillRecognizer>();
                if (skills != null)
                {
                    GUILayout.Label($"<b>Current Skill:</b> {skills.CurrentSkill}", _label);
                    GUILayout.Label(
                        $"Tip speed : <b>{skills.TipSpeed:0.00} m/s</b>  |  " +
                        $"Angular: <b>{skills.SwordAngularSpeed:0.00} rad/s</b>", _label);
                    GUILayout.Label(
                        $"Arm Input: <b>{skills.ArmInput:+0.00;-0.00;0.00}</b>  |  " +
                        $"Guard: <b>{(skills.IsGuarding ? "ON" : "OFF")}</b>", _label);
                }

                CombatHitReport hit = default;
                bool hasHit = false;
                if (match != null && match.HasLastHit) { hit = match.LastHit; hasHit = true; }
                else if (playerHealth != null && playerHealth.HasLatestHit) { hit = playerHealth.LatestHit; hasHit = true; }
                else if (opponentHealth != null && opponentHealth.HasLatestHit) { hit = opponentHealth.LatestHit; hasHit = true; }

                if (hasHit)
                {
                    float age = Time.time - hit.time;
                    string outcomeCol = hit.outcome == CombatOutcome.Parry ? "#66ff99" :
                                        hit.outcome == CombatOutcome.Block ? "#66ccff" :
                                        hit.outcome == CombatOutcome.BodyHit ? "#ff8866" : "#cccccc";
                    GUILayout.Label($"<b>LAST HIT ({age:0.0}s):</b>", _label);
                    GUILayout.Label(
                        $"  Skill: <b>{hit.skill}</b>  Quality: <b>{hit.quality}</b>\n" +
                        $"  Impact: <b>{hit.impactStrength:0.00}</b> ({hit.impactCategory})  Part: <b>{hit.bodyPart}</b>\n" +
                        $"  Damage: <b>{hit.damage:0.0}</b>  " +
                        $"<color={outcomeCol}><b>{hit.outcome}</b></color>", _label);
                }
                else
                {
                    GUILayout.Label("<b>COMBAT:</b> No hits yet", _small);
                }
            }

            GUILayout.Label("<size=11>A/L rope · Q/E depth · J/K sword · R reset · touch debug bottom</size>", _small);
            GUILayout.EndArea();
        }

        void Bar(string name, float v, Color c)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: <b>{v:0.00}</b>", _label, GUILayout.Width(128));
            Rect r = GUILayoutUtility.GetRect(150, 14, GUILayout.ExpandWidth(true));
            GUI.color = new Color(1, 1, 1, 0.15f); GUI.DrawTexture(r, _px);
            GUI.color = c; GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), _px);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }

        void SignedBar(string name, float v, Color c)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: <b>{v:+0.00;-0.00;0.00}</b>", _label, GUILayout.Width(128));
            Rect r = GUILayoutUtility.GetRect(150, 14, GUILayout.ExpandWidth(true));
            GUI.color = new Color(1, 1, 1, 0.15f); GUI.DrawTexture(r, _px);
            float mid = r.x + r.width * 0.5f;
            float half = r.width * 0.5f * Mathf.Clamp(Mathf.Abs(v), 0f, 1f);
            GUI.color = c;
            GUI.DrawTexture(v >= 0f ? new Rect(mid, r.y, half, r.height) : new Rect(mid - half, r.y, half, r.height), _px);
            GUI.color = new Color(1, 1, 1, 0.5f); GUI.DrawTexture(new Rect(mid - 1, r.y, 2, r.height), _px);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }
    }
}

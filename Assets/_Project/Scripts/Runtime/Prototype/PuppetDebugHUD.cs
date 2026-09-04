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

            var panel = new Rect(12, 36, 364, 320);
            GUI.color = new Color(0, 0, 0, 0.65f);
            GUI.DrawTexture(panel, _px);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(panel.x + 12, panel.y + 8, panel.width - 24, panel.height - 16));
            string sideStr = rig != null ? rig.side.ToString() : "?";
            GUILayout.Label($"PUPPET — Unified 2-Thumb Control   <size=11>[{sideStr} · faces {facing}]</size>", _header);

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

                float sep = controller.FootSeparation;
                float standSep = rig != null ? rig.standingFootSeparation : 0.32f;
                GUILayout.Label($"Foot separation : <b>{sep:0.00}</b> m  (stand {standSep:0.00})", _label);

                // ---- Combat Physics Prototype Info ----
                var swordHandler = rig != null ? rig.swordCollision : null;
                if (swordHandler == null && rig != null && rig.sword != null)
                    swordHandler = rig.sword.GetComponent<SwordCollisionHandler>();

                if (swordHandler != null && swordHandler.HasImpact)
                {
                    var hit = swordHandler.LatestImpact;
                    float age = Time.time - hit.time;
                    string catColor = hit.category == ImpactCategory.Strong ? "#ff4444" :
                                      hit.category == ImpactCategory.Medium ? "#ffbb33" : "#aaaaaa";
                    GUILayout.Label(
                        $"<b>LAST HIT ({age:0.0}s ago):</b> {hit.bodyPart} | <b>{hit.relativeSpeed:0.00} m/s</b>\n" +
                        $"Strength: <b>{hit.impactStrength:0.00}</b>  [<color={catColor}><b>{hit.category.ToString().ToUpper()}</b></color>]", _label);
                }
                else
                {
                    GUILayout.Label("<b>COMBAT:</b> No hits yet (swing sword at target)", _small);
                }
            }

            GUILayout.Label("<size=11>A/L rope · Q/E depth · J/K sword arm · 2-thumb: depth=(xL+xR)/2 · sword=(xR-xL)/2</size>", _small);
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

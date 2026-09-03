using UnityEngine;

namespace PuppetMaster.Prototype
{
    /// <summary>
    /// Phase 1 prototype. Bare IMGUI overlay so the mechanic can be judged:
    /// left/right tension (0.00–1.00), pelvis height and torso uprightness, plus
    /// faint tints marking the two control zones. Not meant to be pretty.
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
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            _header = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, richText = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
        }

        void OnGUI()
        {
            EnsureStyles();
            float w = Screen.width, h = Screen.height;

            if (drawZones)
            {
                GUI.color = new Color(LeftTint.r, LeftTint.g, LeftTint.b, 0.05f);
                GUI.DrawTexture(new Rect(0, 0, w * 0.5f, h), _px);
                GUI.color = new Color(RightTint.r, RightTint.g, RightTint.b, 0.05f);
                GUI.DrawTexture(new Rect(w * 0.5f, 0, w * 0.5f, h), _px);
                GUI.color = new Color(1, 1, 1, 0.35f);
                GUI.DrawTexture(new Rect(w * 0.5f - 1, 0, 2, h), _px);
                GUI.color = Color.white;
                GUI.Label(new Rect(w * 0.5f - 200, h - 30, 190, 22), "◀ LEFT CONTROL", _header);
                GUI.Label(new Rect(w * 0.5f + 12, h - 30, 210, 22), "RIGHT CONTROL ▶", _header);
            }

            var panel = new Rect(12, 40, 320, 150);
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(panel, _px);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(panel.x + 12, panel.y + 8, panel.width - 24, panel.height - 16));
            GUILayout.Label("PUPPET PROTOTYPE — Phase 1", _header);

            if (controller != null)
            {
                Bar("Left Tension", controller.LeftTension, LeftTint);
                Bar("Right Tension", controller.RightTension, RightTint);

                float pelvisY = rig != null && rig.pelvis != null ? rig.pelvis.position.y : 0f;
                float standY = rig != null ? Mathf.Max(0.01f, rig.standingPelvisHeight) : 1f;
                float rel = Mathf.Clamp01(pelvisY / standY);
                float torsoUp = rig != null && rig.torso != null
                    ? Vector3.Dot(rig.torso.transform.up, Vector3.up)
                    : 0f;

                GUILayout.Label(
                    $"Pelvis height : <b>{pelvisY:0.00}</b> m  (<b>{rel * 100f:0}%</b> of standing)", _label);
                GUILayout.Label($"Torso upright : <b>{torsoUp:0.00}</b>   (1 = vertical)", _label);
            }

            GUILayout.Label(
                "<size=12>Hold  A / L  pull ropes  ·  Space  = both  ·  drag DOWN in a zone to pull</size>",
                _small);
            GUILayout.EndArea();
        }

        void Bar(string name, float v, Color c)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}: <b>{v:0.00}</b>", _label, GUILayout.Width(150));
            Rect r = GUILayoutUtility.GetRect(160, 16, GUILayout.ExpandWidth(true));
            GUI.color = new Color(1, 1, 1, 0.15f);
            GUI.DrawTexture(r, _px);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), _px);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }
    }
}

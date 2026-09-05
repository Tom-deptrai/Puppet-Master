using System.Collections.Generic;
using System.IO;
using PuppetMaster.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PuppetMaster.Editor
{
    /// <summary>
    /// Visual-only swap: attaches knight/arena sprites onto the existing physics
    /// prototype without touching joints, colliders, ropes, combat, or AI.
    /// </summary>
    public static class KnightVisualApplier
    {
        const string ScenePath = "Assets/_Project/Scenes/PuppetPrototype.unity";
        const string PartsDir = "Assets/_Project/Art/Characters/Knight/Parts";
        const string ArenaBgPath = "Assets/_Project/Art/Arena/KnightArena/arena_bg.png";
        const string ArenaRailPath = "Assets/_Project/Art/Arena/KnightArena/arena_rail.png";
        const string FlagPath = "Assets/_Project/EditorTemp/ApplyKnightVisuals.flag";
        const string DoneMarker = "Assets/_Project/EditorTemp/KnightVisualsApplied.txt";

        static readonly string[] PartNames =
        {
            "Head", "Torso", "Pelvis", "Sword",
            "UpperArm_L", "LowerArm_L", "Hand_L",
            "UpperArm_R", "LowerArm_R", "Hand_R",
            "UpperLeg_L", "LowerLeg_L", "Foot_L",
            "UpperLeg_R", "LowerLeg_R", "Foot_R",
        };

        [InitializeOnLoadMethod]
        static void AutoApplyFromFlag()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(FlagPath)) return;
                try { File.Delete(FlagPath); }
                catch { /* ignore */ }
                ApplyAll();
            };
        }

        [MenuItem("Puppet Master/Visuals/Apply Knight + Arena Art")]
        public static void ApplyAll()
        {
            ConfigureImports();
            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ApplyArena();
            foreach (var rig in Object.FindObjectsByType<PuppetRig>(FindObjectsSortMode.None))
                ApplyPuppet(rig);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            Directory.CreateDirectory(Path.GetDirectoryName(DoneMarker));
            File.WriteAllText(DoneMarker, $"ok {System.DateTime.UtcNow:o}");
            Debug.Log("[KnightVisualApplier] Applied knight + arena visuals to PuppetPrototype.unity (physics untouched).");
        }

        static void ConfigureImports()
        {
            foreach (var name in PartNames)
                ConfigureSprite($"{PartsDir}/{name}.png", pixelsPerUnit: 256f, filter: FilterMode.Bilinear);

            ConfigureSprite(ArenaBgPath, pixelsPerUnit: 100f, filter: FilterMode.Bilinear);
            ConfigureSprite(ArenaRailPath, pixelsPerUnit: 256f, filter: FilterMode.Bilinear);
        }

        static void ConfigureSprite(string path, float pixelsPerUnit, FilterMode filter)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[KnightVisualApplier] Missing texture: {path}");
                return;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }
            if (importer == null) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.filterMode = filter;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static Sprite LoadSprite(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void ApplyArena()
        {
            var bgSprite = LoadSprite(ArenaBgPath);
            var railSprite = LoadSprite(ArenaRailPath);

            // Hide prototype ground mesh; keep collider.
            var ground = GameObject.Find("Ground");
            if (ground != null)
                HideRenderers(ground);

            // Background plane behind the fight (visual only).
            var existingBg = GameObject.Find("ArenaBackground");
            if (existingBg != null) Object.DestroyImmediate(existingBg);
            if (bgSprite != null)
            {
                var bg = new GameObject("ArenaBackground");
                bg.transform.SetPositionAndRotation(new Vector3(0f, 1.15f, 2.4f), Quaternion.identity);
                var sr = bg.AddComponent<SpriteRenderer>();
                sr.sprite = bgSprite;
                sr.sortingOrder = -200;
                FitSpriteWorldHeight(sr.transform, bgSprite, 3.6f);
                // Widen a bit for the 3/4 camera framing.
                var s = sr.transform.localScale;
                s.x *= 1.15f;
                sr.transform.localScale = s;
            }

            ApplyRailVisual("Rail_Left", railSprite);
            ApplyRailVisual("Rail_Right", railSprite);

            // Slot guide cubes are visual clutter now.
            HideByName("Slot_L");
            HideByName("Slot_R");
            HideByName("Rail_Left_Groove");
            HideByName("Rail_Right_Groove");

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.07f, 0.06f);
            }
        }

        static void ApplyRailVisual(string railName, Sprite railSprite)
        {
            var rail = GameObject.Find(railName);
            if (rail == null) return;

            HideRenderers(rail);
            foreach (Transform child in rail.transform)
                HideRenderers(child.gameObject);

            var old = rail.transform.Find("RailVisual");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            if (railSprite == null) return;

            var vis = new GameObject("RailVisual");
            vis.transform.SetParent(rail.transform, false);
            vis.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            vis.transform.localRotation = Quaternion.identity;
            var sr = vis.AddComponent<SpriteRenderer>();
            sr.sprite = railSprite;
            sr.sortingOrder = -50;
            // Match authored rail length (~1.05m world X scale of cube).
            FitSpriteWorldWidth(vis.transform, railSprite, 1.05f);
        }

        static void ApplyPuppet(PuppetRig rig)
        {
            if (rig == null) return;

            float facing = Mathf.Sign(rig.facingSign == 0f ? 1f : rig.facingSign);
            bool flipX = facing < 0f;

            Attach(rig.head != null ? rig.head.transform : null, "Head", 20, 0.30f, flipX);
            Attach(rig.torso != null ? rig.torso.transform : null, "Torso", 10, 0.58f, flipX);
            Attach(rig.pelvis != null ? rig.pelvis.transform : null, "Pelvis", 9, 0.38f, flipX);

            Attach(rig.leftArm.upperArm != null ? FindVisualHost(rig.leftArm.upperArm.transform) : null, "UpperArm_L", 14, 0.34f, flipX);
            Attach(rig.leftArm.lowerArm != null ? FindVisualHost(rig.leftArm.lowerArm.transform) : null, "LowerArm_L", 15, 0.30f, flipX);
            Attach(rig.leftArm.hand != null ? rig.leftArm.hand.transform : null, "Hand_L", 16, 0.12f, flipX);

            Attach(rig.rightArm.upperArm != null ? FindVisualHost(rig.rightArm.upperArm.transform) : null, "UpperArm_R", 12, 0.34f, flipX);
            Attach(rig.rightArm.lowerArm != null ? FindVisualHost(rig.rightArm.lowerArm.transform) : null, "LowerArm_R", 13, 0.30f, flipX);
            Attach(rig.rightArm.hand != null ? rig.rightArm.hand.transform : null, "Hand_R", 17, 0.12f, flipX);

            Attach(rig.left.upperLeg != null ? FindVisualHost(rig.left.upperLeg.transform) : null, "UpperLeg_L", 6, 0.42f, flipX);
            Attach(rig.left.lowerLeg != null ? FindVisualHost(rig.left.lowerLeg.transform) : null, "LowerLeg_L", 7, 0.40f, flipX);
            Attach(rig.left.foot != null ? FindVisualHost(rig.left.foot.transform) : null, "Foot_L", 8, 0.28f, flipX, preferWidth: true);

            Attach(rig.right.upperLeg != null ? FindVisualHost(rig.right.upperLeg.transform) : null, "UpperLeg_R", 4, 0.42f, flipX);
            Attach(rig.right.lowerLeg != null ? FindVisualHost(rig.right.lowerLeg.transform) : null, "LowerLeg_R", 5, 0.40f, flipX);
            Attach(rig.right.foot != null ? FindVisualHost(rig.right.foot.transform) : null, "Foot_R", 8, 0.28f, flipX, preferWidth: true);

            AttachSword(rig.sword != null ? rig.sword.transform : null, flipX);

            // Hide leftover primitive face / joint-cap meshes under this puppet.
            HideNamedMeshes(rig.transform, new HashSet<string>
            {
                "Face", "Deltoid_Mesh", "Elbow_L_Mesh", "Elbow_R_Mesh",
                "Handle_Mesh", "Pommel_Mesh", "Guard_Mesh", "Blade_Mesh", "Mesh",
            });
        }

        /// <summary>
        /// Bone segments keep identity rotation on the Rigidbody; visual mesh child
        /// is already oriented along the bone. Prefer parenting the sprite to the
        /// Rigidbody root so it tracks physics without inheriting mesh scale.
        /// </summary>
        static Transform FindVisualHost(Transform part) => part;

        static void Attach(
            Transform host, string partName, int sorting, float worldSize,
            bool flipX, bool preferWidth = false)
        {
            if (host == null) return;

            var sprite = LoadSprite($"{PartsDir}/{partName}.png");
            HideRenderers(host.gameObject);

            var old = host.Find("KnightVisual");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            if (sprite == null)
            {
                Debug.LogWarning($"[KnightVisualApplier] Missing sprite for {partName}");
                return;
            }

            var vis = new GameObject("KnightVisual");
            vis.transform.SetParent(host, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;

            // Profile art faces +X. Mirror for puppets that face -X.
            float sx = flipX ? -1f : 1f;
            vis.transform.localScale = new Vector3(sx, 1f, 1f);

            var sr = vis.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sorting;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;

            if (preferWidth)
                FitSpriteWorldWidth(vis.transform, sprite, worldSize, preserveFlipX: flipX);
            else
                FitSpriteWorldHeight(vis.transform, sprite, worldSize, preserveFlipX: flipX);

            // Slight Z bias so overlapping limbs read cleanly in 3/4 view.
            var lp = vis.transform.localPosition;
            lp.z = partName.Contains("_L") ? 0.02f : partName.Contains("_R") ? -0.02f : 0f;
            vis.transform.localPosition = lp;
        }

        static void AttachSword(Transform sword, bool flipX)
        {
            if (sword == null) return;

            HideRenderers(sword.gameObject);
            foreach (Transform child in sword)
                HideRenderers(child.gameObject);

            var old = sword.Find("KnightVisual");
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var sprite = LoadSprite($"{PartsDir}/Sword.png");
            if (sprite == null) return;

            var vis = new GameObject("KnightVisual");
            vis.transform.SetParent(sword, false);
            // Art sword is diagonal (~45°). Physics blade axis is local +Y.
            vis.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            vis.transform.localPosition = new Vector3(0f, 0.28f, 0f);

            var sr = vis.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 25;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            FitSpriteWorldHeight(vis.transform, sprite, 0.95f, preserveFlipX: flipX);
            if (flipX)
            {
                var s = vis.transform.localScale;
                s.x = -Mathf.Abs(s.x);
                vis.transform.localScale = s;
            }
        }

        static void FitSpriteWorldHeight(Transform t, Sprite sprite, float worldHeight, bool preserveFlipX = false)
        {
            if (sprite == null || worldHeight <= 1e-5f) return;
            float h = sprite.bounds.size.y;
            if (h < 1e-5f) return;
            float scale = worldHeight / h;
            float sx = preserveFlipX && t.localScale.x < 0f ? -scale : scale;
            t.localScale = new Vector3(sx, scale, 1f);
        }

        static void FitSpriteWorldWidth(Transform t, Sprite sprite, float worldWidth, bool preserveFlipX = false)
        {
            if (sprite == null || worldWidth <= 1e-5f) return;
            float w = sprite.bounds.size.x;
            if (w < 1e-5f) return;
            float scale = worldWidth / w;
            float sx = preserveFlipX && t.localScale.x < 0f ? -scale : scale;
            t.localScale = new Vector3(sx, scale, 1f);
        }

        static void HideRenderers(GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponents<Renderer>())
                r.enabled = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                // Keep LineRenderer ropes visible.
                if (r is LineRenderer) continue;
                if (r.gameObject.name == "KnightVisual" || r.gameObject.name == "RailVisual" || r.gameObject.name == "ArenaBackground")
                    continue;
                r.enabled = false;
            }
        }

        static void HideNamedMeshes(Transform root, HashSet<string> names)
        {
            if (root == null) return;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!names.Contains(t.name)) continue;
                foreach (var r in t.GetComponents<Renderer>())
                    r.enabled = false;
            }
        }

        static void HideByName(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) HideRenderers(go);
        }
    }
}

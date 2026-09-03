using System.Collections.Generic;
using System.IO;
using PuppetMaster.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PuppetMaster.Editor
{
    /// <summary>
    /// Phase 1 tool (no gameplay logic of its own). Generates the physics
    /// prototype scene from scratch — one jointed puppet, two feet constrained to
    /// a rail, two control ropes — and wires up the runtime scripts.
    ///
    /// Re-runnable: always overwrites <c>Assets/_Project/Scenes/PuppetPrototype.unity</c>.
    /// Never touches Bootstrap. Run from  Puppet Master ▸ Phase 1 ▸ Build Puppet Prototype Scene
    /// or headless via  -executeMethod PuppetMaster.Editor.PuppetPrototypeBuilder.BuildFromCommandLine
    /// </summary>
    public static class PuppetPrototypeBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/PuppetPrototype.unity";
        const string MatDir = "Assets/_Project/Materials/Prototype";

        // --- rig geometry (standing pose, metres) ---
        const float LegX = 0.12f;   // half distance between the feet
        const float ArmX = 0.28f;
        const float FootY = 0.06f;

        [MenuItem("Puppet Master/Phase 1/Build Puppet Prototype Scene")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Material mBody = Mat("Puppet_Body", new Color(0.74f, 0.62f, 0.45f));
            Material mLimb = Mat("Puppet_Arm", new Color(0.60f, 0.49f, 0.36f));
            Material mLeg = Mat("Puppet_Leg", new Color(0.49f, 0.55f, 0.63f));
            Material mFoot = Mat("Puppet_Foot", new Color(0.96f, 0.76f, 0.24f));
            Material mRail = Mat("Rail", new Color(0.85f, 0.30f, 0.28f));
            Material mGround = Mat("Ground", new Color(0.13f, 0.14f, 0.16f));
            Material mPulley = Mat("Pulley", new Color(0.30f, 0.32f, 0.36f));
            Material mRope = UnlitMat("Rope", Color.white);

            ConfigureCameraAndLight();
            BuildEnvironment(mGround, mRail);

            // ---- rig root ----
            var puppetGo = new GameObject("Puppet");
            var puppet = puppetGo.transform;

            var pelvis = Part("Pelvis", PrimitiveType.Cube, new Vector3(0f, 1.06f, 0f), new Vector3(0.30f, 0.20f, 0.22f), 10f, mBody, puppet, 2.0f);
            var torso = Part("Torso", PrimitiveType.Cube, new Vector3(0f, 1.40f, 0f), new Vector3(0.34f, 0.44f, 0.22f), 13f, mBody, puppet, 2.0f);
            var head = Part("Head", PrimitiveType.Sphere, new Vector3(0f, 1.78f, 0f), new Vector3(0.22f, 0.22f, 0.22f), 2.6f, mBody, puppet, 1.4f);

            var uArmL = Part("UpperArm_L", PrimitiveType.Capsule, new Vector3(-ArmX, 1.37f, 0f), new Vector3(0.09f, 0.17f, 0.09f), 1.6f, mLimb, puppet, 0.9f);
            var lArmL = Part("LowerArm_L", PrimitiveType.Capsule, new Vector3(-ArmX, 1.02f, 0f), new Vector3(0.08f, 0.15f, 0.08f), 1.0f, mLimb, puppet, 0.9f);
            var uArmR = Part("UpperArm_R", PrimitiveType.Capsule, new Vector3(ArmX, 1.37f, 0f), new Vector3(0.09f, 0.17f, 0.09f), 1.6f, mLimb, puppet, 0.9f);
            var lArmR = Part("LowerArm_R", PrimitiveType.Capsule, new Vector3(ArmX, 1.02f, 0f), new Vector3(0.08f, 0.15f, 0.08f), 1.0f, mLimb, puppet, 0.9f);

            var uLegL = Part("UpperLeg_L", PrimitiveType.Capsule, new Vector3(-LegX, 0.76f, 0f), new Vector3(0.12f, 0.22f, 0.12f), 6.5f, mLeg, puppet, 1.0f);
            var lLegL = Part("LowerLeg_L", PrimitiveType.Capsule, new Vector3(-LegX, 0.32f, 0f), new Vector3(0.11f, 0.21f, 0.11f), 4.5f, mLeg, puppet, 1.0f);
            var footL = Part("Foot_L", PrimitiveType.Cube, new Vector3(-LegX, FootY, 0.03f), new Vector3(0.12f, 0.07f, 0.26f), 4.0f, mFoot, puppet, 3.0f);
            var uLegR = Part("UpperLeg_R", PrimitiveType.Capsule, new Vector3(LegX, 0.76f, 0f), new Vector3(0.12f, 0.22f, 0.12f), 6.5f, mLeg, puppet, 1.0f);
            var lLegR = Part("LowerLeg_R", PrimitiveType.Capsule, new Vector3(LegX, 0.32f, 0f), new Vector3(0.11f, 0.21f, 0.11f), 4.5f, mLeg, puppet, 1.0f);
            var footR = Part("Foot_R", PrimitiveType.Cube, new Vector3(LegX, FootY, 0.03f), new Vector3(0.12f, 0.07f, 0.26f), 4.0f, mFoot, puppet, 3.0f);

            // ---- joints: all authored in the standing pose, all PURELY PLANAR ----
            //   angular X = flex in the screen plane (about world Z) -> the only free axis
            //   angular Y (yaw) and angular Z (pitch into screen) are locked  -> stays 2.5D
            var spine = Bend(torso, pelvis, new Vector3(0f, 1.17f, 0f), -40f, 50f, driven: true);
            var neck = Bend(head, torso, new Vector3(0f, 1.64f, 0f), -40f, 45f, driven: true);

            var shoulderL = Bend(uArmL, torso, new Vector3(-ArmX, 1.52f, 0f), -120f, 120f, driven: false);
            var elbowL = Bend(lArmL, uArmL, new Vector3(-ArmX, 1.19f, 0f), -10f, 135f, driven: false);
            var shoulderR = Bend(uArmR, torso, new Vector3(ArmX, 1.52f, 0f), -120f, 120f, driven: false);
            var elbowR = Bend(lArmR, uArmR, new Vector3(ArmX, 1.19f, 0f), -10f, 135f, driven: false);

            var hipL = Bend(uLegL, pelvis, new Vector3(-LegX, 0.97f, 0f), -85f, 85f, driven: true);
            var kneeL = Bend(lLegL, uLegL, new Vector3(-LegX, 0.54f, 0f), -100f, 100f, driven: true);
            var ankleL = Bend(footL, lLegL, new Vector3(-LegX, 0.11f, 0f), -45f, 45f, driven: true);
            var hipR = Bend(uLegR, pelvis, new Vector3(LegX, 0.97f, 0f), -85f, 85f, driven: true);
            var kneeR = Bend(lLegR, uLegR, new Vector3(LegX, 0.54f, 0f), -100f, 100f, driven: true);
            var ankleR = Bend(footR, lLegR, new Vector3(LegX, 0.11f, 0f), -45f, 45f, driven: true);

            // ---- rail constraint: each foot slides on world X, locked on Y/Z ----
            var railL = RailJoint(footL, new Vector3(-LegX, footL.position.y, 0f));
            var railR = RailJoint(footR, new Vector3(LegX, footR.position.y, 0f));

            // ---- pelvis -> world: keeps the puppet planar AND carries the main
            //      "stand up" drive. The controller ramps this joint's slerp spring
            //      with combined tension: taut = pelvis firmly held upright,
            //      slack = limp so the puppet sags. X/Y slide free (rise & shift),
            //      Z locked (depth), pitch/yaw locked, screen-plane lean driven.
            var plane = pelvis.gameObject.AddComponent<ConfigurableJoint>();
            plane.connectedBody = null;
            plane.autoConfigureConnectedAnchor = false;
            plane.anchor = Vector3.zero;
            plane.connectedAnchor = pelvis.position;
            plane.axis = new Vector3(0f, 0f, 1f);
            plane.secondaryAxis = new Vector3(0f, 1f, 0f);
            plane.xMotion = ConfigurableJointMotion.Free;
            plane.yMotion = ConfigurableJointMotion.Free;
            plane.zMotion = ConfigurableJointMotion.Locked;
            plane.angularXMotion = ConfigurableJointMotion.Limited; // screen-plane lean
            plane.angularYMotion = ConfigurableJointMotion.Locked;
            plane.angularZMotion = ConfigurableJointMotion.Locked;
            plane.lowAngularXLimit = new SoftJointLimit { limit = -80f };
            plane.highAngularXLimit = new SoftJointLimit { limit = 80f };
            plane.rotationDriveMode = RotationDriveMode.Slerp;
            plane.slerpDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = 0f };
            plane.projectionMode = JointProjectionMode.PositionAndRotation;
            plane.projectionDistance = 0.05f;
            plane.enablePreprocessing = false;
            plane.configuredInWorldSpace = false;

            // ---- pulleys ----
            var pulleys = new GameObject("Pulleys").transform;
            var pulleyL = Pulley("Pulley_L", new Vector3(-0.42f, 2.55f, 0f), pulleys, mPulley);
            var pulleyR = Pulley("Pulley_R", new Vector3(0.42f, 2.55f, 0f), pulleys, mPulley);
            var attachL = RopeAttach(footL, "RopeAttach_L");
            var attachR = RopeAttach(footR, "RopeAttach_R");

            // ---- runtime scripts (all on the Puppet root) ----
            var rig = puppetGo.AddComponent<PuppetRig>();
            rig.pelvis = pelvis; rig.torso = torso; rig.head = head;
            rig.spine = spine; rig.neck = neck; rig.pelvisPlaneJoint = plane;
            rig.standingPelvisHeight = pelvis.position.y;
            rig.left = new PuppetRig.Leg
            {
                upperLeg = uLegL, lowerLeg = lLegL, foot = footL,
                hip = hipL, knee = kneeL, ankle = ankleL, railJoint = railL,
                pulley = pulleyL, ropeAttach = attachL,
            };
            rig.right = new PuppetRig.Leg
            {
                upperLeg = uLegR, lowerLeg = lLegR, foot = footR,
                hip = hipR, knee = kneeR, ankle = ankleR, railJoint = railR,
                pulley = pulleyR, ropeAttach = attachR,
            };

            var controller = puppetGo.AddComponent<PuppetRopeController>();
            var input = puppetGo.AddComponent<PuppetRopeInput>();
            var hud = puppetGo.AddComponent<PuppetDebugHUD>();
            hud.controller = controller; hud.rig = rig; hud.input = input;

            // ---- rope visuals ----
            var ropes = new GameObject("Ropes").transform;
            MakeRope("Rope_L", ropes, pulleyL, attachL, isLeft: true, controller, mRope);
            MakeRope("Rope_R", ropes, pulleyR, attachR, isLeft: false, controller, mRope);

            // ---- save ----
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = puppetGo;

            Debug.Log(
                "[Phase 1] Built PuppetPrototype.unity\n" +
                $"  scene   : {ScenePath}\n" +
                "  puppet  : 13 bodies (pelvis, torso, head, 2x upper/lower arm, 2x upper/lower leg, 2x foot)\n" +
                "  joints  : ConfigurableJoint everywhere; hips/knees/spine/neck slerp-driven by tension\n" +
                "  rail    : each foot -> world joint, X slide +/-0.32 m, Y/Z locked, spring to home X\n" +
                "  ropes   : Rope_L/Rope_R pull force applied AT Foot_L/Foot_R toward their pulleys\n" +
                "  play it : press Play, hold A / L (or Space), or drag down in a screen half");
        }

        // Headless entry point for CI / -executeMethod.
        public static void BuildFromCommandLine()
        {
            Build();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // --------------------------------------------------------------------

        static void ConfigureCameraAndLight()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetPositionAndRotation(new Vector3(0f, 1.30f, -5.15f), Quaternion.Euler(3f, 0f, 0f));
                cam.fieldOfView = 46f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
                cam.nearClipPlane = 0.05f;
            }

            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(48f, -25f, 0f);
                sun.intensity = 1.15f;
                sun.shadows = LightShadows.Soft;
            }
        }

        static void BuildEnvironment(Material ground, Material rail)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
            g.name = "Ground";
            g.transform.position = Vector3.zero;
            g.transform.localScale = new Vector3(4f, 1f, 2f);
            g.GetComponent<MeshRenderer>().sharedMaterial = ground;

            var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r.name = "Rail";
            Object.DestroyImmediate(r.GetComponent<BoxCollider>()); // visual only — the joint is the constraint
            r.transform.position = new Vector3(0f, 0f, 0f);
            r.transform.localScale = new Vector3(3.4f, 0.06f, 0.5f);
            r.GetComponent<MeshRenderer>().sharedMaterial = rail;
        }

        static Rigidbody Part(string name, PrimitiveType prim, Vector3 pos, Vector3 size,
            float mass, Material mat, Transform parent, float angularDamping)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);

            var mesh = GameObject.CreatePrimitive(prim);
            mesh.name = "Mesh";
            mesh.transform.SetParent(go.transform, worldPositionStays: false);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.transform.localScale = size;
            mesh.GetComponent<MeshRenderer>().sharedMaterial = mat;

            switch (prim)
            {
                case PrimitiveType.Sphere:
                    var sc = go.AddComponent<SphereCollider>();
                    sc.radius = size.x * 0.5f;
                    break;
                case PrimitiveType.Capsule:
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.radius = Mathf.Min(size.x, size.z) * 0.5f;
                    cc.height = size.y * 2f; // Unity capsule mesh is 2 units tall
                    cc.direction = 1;        // Y
                    break;
                default:
                    var bc = go.AddComponent<BoxCollider>();
                    bc.size = size;
                    break;
            }

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = angularDamping;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.maxAngularVelocity = 12f;
            rb.solverIterations = 40;
            rb.solverVelocityIterations = 20;
            return rb;
        }

        /// <summary>
        /// ConfigurableJoint connecting <paramref name="child"/> to <paramref name="parent"/>
        /// at <paramref name="worldAnchor"/>. PURELY PLANAR: the only free angular axis
        /// is X (rotation about world Z = the screen plane). Yaw and into-screen pitch
        /// are hard-locked so the whole rig stays 2.5D.
        /// </summary>
        static ConfigurableJoint Bend(Rigidbody child, Rigidbody parent, Vector3 worldAnchor,
            float xLow, float xHigh, bool driven, float passiveSpring = 6f)
        {
            var j = child.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = parent;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = child.transform.InverseTransformPoint(worldAnchor);
            j.connectedAnchor = parent.transform.InverseTransformPoint(worldAnchor);
            j.axis = new Vector3(0f, 0f, 1f);
            j.secondaryAxis = new Vector3(0f, 1f, 0f);

            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = ConfigurableJointMotion.Limited;
            j.angularYMotion = ConfigurableJointMotion.Locked;
            j.angularZMotion = ConfigurableJointMotion.Locked;

            j.lowAngularXLimit = new SoftJointLimit { limit = xLow };
            j.highAngularXLimit = new SoftJointLimit { limit = xHigh };

            var noSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
            j.angularXLimitSpring = noSpring;
            j.angularYZLimitSpring = noSpring;

            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.slerpDrive = new JointDrive
            {
                positionSpring = driven ? 0f : passiveSpring,
                positionDamper = driven ? 0f : passiveSpring * 0.25f,
                maximumForce = driven ? 0f : 120f,
            };

            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.04f;
            j.projectionAngle = 12f;
            j.enablePreprocessing = false;
            j.configuredInWorldSpace = false;
            return j;
        }

        static ConfigurableJoint RailJoint(Rigidbody foot, Vector3 worldHome)
        {
            var j = foot.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = null;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero;
            j.connectedAnchor = worldHome;
            j.axis = new Vector3(1f, 0f, 0f);        // slide along world X
            j.secondaryAxis = new Vector3(0f, 1f, 0f);

            j.xMotion = ConfigurableJointMotion.Limited;   // small slide along the rail
            j.yMotion = ConfigurableJointMotion.Locked;    // never leaves the rail
            j.zMotion = ConfigurableJointMotion.Locked;
            j.linearLimit = new SoftJointLimit { limit = 0.10f };
            j.linearLimitSpring = new SoftJointLimitSpring { spring = 1200f, damper = 40f };

            // Foot is bolted flat to the rail (only slides on X). The ankle joint
            // gives the lower leg its screen-plane freedom above the foot.
            j.angularXMotion = ConfigurableJointMotion.Locked;
            j.angularYMotion = ConfigurableJointMotion.Locked;
            j.angularZMotion = ConfigurableJointMotion.Locked;

            // Firm pull back toward this foot's home X so the two feet keep their
            // spacing and never cross over or skate apart.
            j.xDrive = new JointDrive { positionSpring = 1600f, positionDamper = 45f, maximumForce = 6000f };

            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.02f;
            j.projectionAngle = 5f;
            j.enablePreprocessing = false;
            return j;
        }

        static Transform Pulley(string name, Vector3 pos, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * 0.09f;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        static Transform RopeAttach(Rigidbody foot, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(foot.transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, 0.04f, -0.07f); // top-back of the foot
            return go.transform;
        }

        static void MakeRope(string name, Transform parent, Transform pulley, Transform attach,
            bool isLeft, PuppetRopeController controller, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: true);

            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.widthMultiplier = 0.014f;
            lr.numCapVertices = 3;
            lr.numCornerVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;

            var rv = go.AddComponent<RopeVisual>();
            rv.controller = controller;
            rv.pulley = pulley;
            rv.footAttach = attach;
            rv.isLeft = isLeft;
        }

        static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static Material Mat(string name, Color c) => LoadOrCreateMat(name, c, "Universal Render Pipeline/Lit");
        static Material UnlitMat(string name, Color c) => LoadOrCreateMat(name, c, "Universal Render Pipeline/Unlit");

        static Material LoadOrCreateMat(string name, Color c, string shader)
        {
            Directory.CreateDirectory(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var s = Shader.Find(shader) ?? Shader.Find("Standard");
                m = new Material(s);
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = c;
            return m;
        }
    }
}

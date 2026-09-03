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
    /// Phase 1.1 tool. Generates the physics prototype scene from scratch:
    /// one jointed puppet standing on its side of the arena, FACING the (imagined)
    /// opponent, two feet fore/aft on a rail, two control ropes tied to the feet
    /// whose pulleys hang BEHIND the puppet (out of the combat zone).
    ///
    /// Facing model:
    ///   PlayerSide.Left  -> puppet on the -X side, faces +X  (opponent to the right)
    ///   PlayerSide.Right -> puppet on the +X side, faces -X  (opponent to the left)
    ///   facingSign = +1 (Left) / -1 (Right); "forward" = toward the opponent.
    ///
    /// Re-runnable — overwrites Assets/_Project/Scenes/PuppetPrototype.unity.
    /// Never touches Bootstrap.
    /// </summary>
    public static class PuppetPrototypeBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/PuppetPrototype.unity";
        const string MatDir = "Assets/_Project/Materials/Prototype";

        // stance / layout (metres)
        const float ArenaOffset = 1.0f;   // how far the puppet sits from arena centre
        const float StanceHalf = 0.16f;   // half the fore/aft foot spacing
        const float FootY = 0.06f;
        const float ShoulderZ = 0.15f;    // anatomical left/right = +/-Z

        [MenuItem("Puppet Master/Phase 1/Build Puppet Prototype — Left side")]
        public static void BuildLeft() => Build(PlayerSide.Left);

        [MenuItem("Puppet Master/Phase 1/Build Puppet Prototype — Right side (mirror test)")]
        public static void BuildRight() => Build(PlayerSide.Right);

        public static void Build(PlayerSide side)
        {
            float sideSign = side.Sign();          // -1 Left, +1 Right
            float originX = sideSign * ArenaOffset; // puppet's X
            float facing = -sideSign;              // +1 = faces +X (Left), -1 = faces -X (Right)

            // point `d` metres in front of the puppet (toward the opponent)
            float Fwd(float d) => originX + facing * d;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Material mBody = Mat("Puppet_Body", new Color(0.74f, 0.62f, 0.45f));
            Material mLimb = Mat("Puppet_Arm", new Color(0.60f, 0.49f, 0.36f));
            Material mLeg = Mat("Puppet_Leg", new Color(0.49f, 0.55f, 0.63f));
            Material mFoot = Mat("Puppet_Foot", new Color(0.96f, 0.76f, 0.24f));
            Material mFace = Mat("Puppet_Face", new Color(0.95f, 0.35f, 0.30f));
            Material mRail = Mat("Rail", new Color(0.85f, 0.30f, 0.28f));
            Material mGround = Mat("Ground", new Color(0.13f, 0.14f, 0.16f));
            Material mPulley = Mat("Pulley", new Color(0.30f, 0.32f, 0.36f));
            Material mRope = UnlitMat("Rope", Color.white);

            ConfigureCameraAndLight(originX, facing);
            BuildEnvironment(originX, mGround, mRail);

            var puppetGo = new GameObject($"Puppet_{side}");
            var puppet = puppetGo.transform;

            // ---- torso column (kept X-centred over the stance for L/R symmetry) ----
            var pelvis = Part("Pelvis", PrimitiveType.Cube, new Vector3(Fwd(0f), 1.06f, 0f), new Vector3(0.24f, 0.20f, 0.30f), 10f, mBody, puppet, 2.2f);
            var torso = Part("Torso", PrimitiveType.Cube, new Vector3(Fwd(0f), 1.40f, 0f), new Vector3(0.26f, 0.44f, 0.34f), 13f, mBody, puppet, 2.2f);
            var head = Part("Head", PrimitiveType.Sphere, new Vector3(Fwd(0f), 1.79f, 0f), new Vector3(0.22f, 0.22f, 0.22f), 2.6f, mBody, puppet, 1.6f);

            // a small "nose" so the facing direction is unmistakable
            var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
            face.name = "Face";
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.transform.SetParent(head.transform, false);
            face.transform.localPosition = new Vector3(facing * 0.12f, 0f, 0f);
            face.transform.localScale = new Vector3(0.06f, 0.09f, 0.13f);
            face.GetComponent<MeshRenderer>().sharedMaterial = mFace;

            // ---- arms: light forward guard at the puppet's sides (±Z), not spread to camera ----
            var uArmL = Part("UpperArm_L", PrimitiveType.Capsule, new Vector3(Fwd(0f), 1.36f, ShoulderZ), new Vector3(0.08f, 0.15f, 0.08f), 1.5f, mLimb, puppet, 1.0f);
            var lArmL = Part("LowerArm_L", PrimitiveType.Capsule, new Vector3(Fwd(0.09f), 1.15f, ShoulderZ), new Vector3(0.07f, 0.14f, 0.07f), 1.0f, mLimb, puppet, 1.0f);
            var uArmR = Part("UpperArm_R", PrimitiveType.Capsule, new Vector3(Fwd(0f), 1.36f, -ShoulderZ), new Vector3(0.08f, 0.15f, 0.08f), 1.5f, mLimb, puppet, 1.0f);
            var lArmR = Part("LowerArm_R", PrimitiveType.Capsule, new Vector3(Fwd(0.09f), 1.15f, -ShoulderZ), new Vector3(0.07f, 0.14f, 0.07f), 1.0f, mLimb, puppet, 1.0f);

            // ---- legs: fore/aft on the rail, EXACTLY mirrored fore↔aft so the
            //      lean is symmetric. Foot_L leads (toward opponent). ----
            float legZ = 0f;
            var uLegL = Part("UpperLeg_L", PrimitiveType.Capsule, new Vector3(Fwd(0.07f), 0.76f, legZ), new Vector3(0.12f, 0.22f, 0.12f), 6.5f, mLeg, puppet, 1.1f);
            var lLegL = Part("LowerLeg_L", PrimitiveType.Capsule, new Vector3(Fwd(0.12f), 0.32f, legZ), new Vector3(0.11f, 0.21f, 0.11f), 4.5f, mLeg, puppet, 1.1f);
            var footL = Part("Foot_L", PrimitiveType.Cube, new Vector3(Fwd(StanceHalf), FootY, legZ), new Vector3(0.26f, 0.07f, 0.13f), 4.0f, mFoot, puppet, 3.0f);
            var uLegR = Part("UpperLeg_R", PrimitiveType.Capsule, new Vector3(Fwd(-0.07f), 0.76f, legZ), new Vector3(0.12f, 0.22f, 0.12f), 6.5f, mLeg, puppet, 1.1f);
            var lLegR = Part("LowerLeg_R", PrimitiveType.Capsule, new Vector3(Fwd(-0.12f), 0.32f, legZ), new Vector3(0.11f, 0.21f, 0.11f), 4.5f, mLeg, puppet, 1.1f);
            var footR = Part("Foot_R", PrimitiveType.Cube, new Vector3(Fwd(-StanceHalf), FootY, legZ), new Vector3(0.26f, 0.07f, 0.13f), 4.0f, mFoot, puppet, 3.0f);

            // ---- joints. Planar in the fight plane (X-Y): only rotation about
            //      world Z is free; yaw + roll-into-screen are hard-locked. ----
            var spine = Bend(torso, pelvis, new Vector3(Fwd(-0.01f), 1.17f, 0f), -55f, 55f, driven: true);
            var neck = Bend(head, torso, new Vector3(Fwd(0.01f), 1.64f, 0f), -50f, 50f, driven: true);

            var shoulderL = Bend(uArmL, torso, new Vector3(Fwd(0f), 1.49f, ShoulderZ), -120f, 120f, driven: false, passiveSpring: 40f);
            var elbowL = Bend(lArmL, uArmL, new Vector3(Fwd(0.03f), 1.25f, ShoulderZ), -10f, 140f, driven: false, passiveSpring: 30f);
            var shoulderR = Bend(uArmR, torso, new Vector3(Fwd(0f), 1.49f, -ShoulderZ), -120f, 120f, driven: false, passiveSpring: 40f);
            var elbowR = Bend(lArmR, uArmR, new Vector3(Fwd(0.03f), 1.25f, -ShoulderZ), -10f, 140f, driven: false, passiveSpring: 30f);

            var hipL = Bend(uLegL, pelvis, new Vector3(Fwd(0.035f), 0.97f, legZ), -95f, 95f, driven: true);
            var kneeL = Bend(lLegL, uLegL, new Vector3(Fwd(0.11f), 0.54f, legZ), -120f, 120f, driven: true);
            var ankleL = Bend(footL, lLegL, new Vector3(Fwd(0.15f), 0.11f, legZ), -55f, 55f, driven: true);
            var hipR = Bend(uLegR, pelvis, new Vector3(Fwd(-0.035f), 0.97f, legZ), -95f, 95f, driven: true);
            var kneeR = Bend(lLegR, uLegR, new Vector3(Fwd(-0.11f), 0.54f, legZ), -120f, 120f, driven: true);
            var ankleR = Bend(footR, lLegR, new Vector3(Fwd(-0.15f), 0.11f, legZ), -55f, 55f, driven: true);

            // ---- rail: each foot slides a little along X, locked on Y/Z ----
            var railL = RailJoint(footL, new Vector3(Fwd(StanceHalf), footL.position.y, 0f));
            var railR = RailJoint(footR, new Vector3(Fwd(-StanceHalf), footR.position.y, 0f));

            // ---- pelvis -> world: planar lock + upright/lean drive ----
            var plane = pelvis.gameObject.AddComponent<ConfigurableJoint>();
            plane.connectedBody = null;
            plane.autoConfigureConnectedAnchor = false;
            plane.anchor = Vector3.zero;
            plane.connectedAnchor = pelvis.position;
            plane.axis = new Vector3(0f, 0f, 1f);
            plane.secondaryAxis = new Vector3(0f, 1f, 0f);
            plane.xMotion = ConfigurableJointMotion.Free;   // shuffle fore/aft, rise/fall
            plane.yMotion = ConfigurableJointMotion.Free;
            plane.zMotion = ConfigurableJointMotion.Locked; // stay in the fight plane
            plane.angularXMotion = ConfigurableJointMotion.Limited; // forward/back lean
            plane.angularYMotion = ConfigurableJointMotion.Locked;
            plane.angularZMotion = ConfigurableJointMotion.Locked;
            plane.lowAngularXLimit = new SoftJointLimit { limit = -70f };
            plane.highAngularXLimit = new SoftJointLimit { limit = 70f };
            plane.rotationDriveMode = RotationDriveMode.Slerp;
            plane.slerpDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = 0f };
            plane.projectionMode = JointProjectionMode.PositionAndRotation;
            plane.projectionDistance = 0.05f;
            plane.enablePreprocessing = false;
            plane.configuredInWorldSpace = false;

            // ---- ropes. Pulleys hang straight above each foot (never toward the
            //      opponent / arena centre). Puppet already sits off-centre, so the
            //      whole rope rig stays in this player's third of the screen. ----
            var pulleys = new GameObject("Pulleys").transform;
            var pulleyL = Pulley("Pulley_L", new Vector3(Fwd(StanceHalf), 3.35f, 0.12f), pulleys, mPulley);
            var pulleyR = Pulley("Pulley_R", new Vector3(Fwd(-StanceHalf), 3.05f, -0.12f), pulleys, mPulley);
            var forceAnchorL = ForceAnchor("ForceAnchor_L", new Vector3(Fwd(StanceHalf), 2.6f, 0f), pulleys);
            var forceAnchorR = ForceAnchor("ForceAnchor_R", new Vector3(Fwd(-StanceHalf), 2.6f, 0f), pulleys);
            var attachL = RopeAttach(footL, "RopeAttach_L", facing);
            var attachR = RopeAttach(footR, "RopeAttach_R", facing);

            // ---- runtime scripts ----
            var rig = puppetGo.AddComponent<PuppetRig>();
            rig.side = side;
            rig.pelvis = pelvis; rig.torso = torso; rig.head = head;
            rig.spine = spine; rig.neck = neck; rig.pelvisPlaneJoint = plane;
            rig.standingPelvisHeight = pelvis.position.y;
            rig.standingFootSeparation = Mathf.Abs(footL.position.x - footR.position.x);
            rig.left = new PuppetRig.Leg
            {
                upperLeg = uLegL, lowerLeg = lLegL, foot = footL,
                hip = hipL, knee = kneeL, ankle = ankleL, railJoint = railL,
                pulley = pulleyL, forceAnchor = forceAnchorL, ropeAttach = attachL,
                railHomeX = footL.position.x,
            };
            rig.right = new PuppetRig.Leg
            {
                upperLeg = uLegR, lowerLeg = lLegR, foot = footR,
                hip = hipR, knee = kneeR, ankle = ankleR, railJoint = railR,
                pulley = pulleyR, forceAnchor = forceAnchorR, ropeAttach = attachR,
                railHomeX = footR.position.x,
            };

            var controller = puppetGo.AddComponent<PuppetRopeController>();
            var input = puppetGo.AddComponent<PuppetRopeInput>();
            var hud = puppetGo.AddComponent<PuppetDebugHUD>();
            hud.controller = controller; hud.rig = rig; hud.input = input;

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
                $"[Phase 1.1] Built PuppetPrototype.unity  (side = {side}, faces {(facing > 0 ? "+X" : "-X")})\n" +
                $"  puppet X {originX:0.00}, feet fore/aft on rail, pulleys BEHIND at ~X {Fwd(-0.5f):0.0}\n" +
                "  Foot_L leads toward the opponent; Left Rope = Foot_L, Right Rope = Foot_R\n" +
                "  squat = f(combined tension), lean = f(tension difference) — both symmetric\n" +
                "  Play, then hold A / L / Space or drag DOWN in a screen half");
        }

        public static void BuildFromCommandLine()
        {
            Build(PlayerSide.Left);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // --------------------------------------------------------------------

        static void ConfigureCameraAndLight(float originX, float facing)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                // side-on, looking across the fight plane; puppet sits to one
                // side with open space in front of it (where the opponent goes)
                cam.transform.SetPositionAndRotation(
                    new Vector3(originX + facing * 0.15f, 1.32f, -5.1f),
                    Quaternion.Euler(4f, 0f, 0f));
                cam.fieldOfView = 45f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
                cam.nearClipPlane = 0.05f;
            }

            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(45f, 35f * -facing, 0f);
                sun.intensity = 1.15f;
                sun.shadows = LightShadows.Soft;
            }
        }

        static void BuildEnvironment(float originX, Material ground, Material rail)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
            g.name = "Ground";
            g.transform.position = Vector3.zero;
            g.transform.localScale = new Vector3(6f, 1f, 2f);
            g.GetComponent<MeshRenderer>().sharedMaterial = ground;

            var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r.name = "Rail";
            Object.DestroyImmediate(r.GetComponent<BoxCollider>());
            r.transform.position = new Vector3(originX, 0f, 0f);
            r.transform.localScale = new Vector3(1.1f, 0.06f, 0.42f);
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
                    go.AddComponent<SphereCollider>().radius = size.x * 0.5f;
                    break;
                case PrimitiveType.Capsule:
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.radius = Mathf.Min(size.x, size.z) * 0.5f;
                    cc.height = size.y * 2f;
                    cc.direction = 1;
                    break;
                default:
                    go.AddComponent<BoxCollider>().size = size;
                    break;
            }

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = angularDamping;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.maxAngularVelocity = 12f;
            rb.solverIterations = 44;
            rb.solverVelocityIterations = 22;
            return rb;
        }

        /// <summary>
        /// ConfigurableJoint child->parent at worldAnchor. Free axis = rotation
        /// about world Z (the fight plane). Yaw + into-screen roll hard-locked.
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
                maximumForce = driven ? 0f : 150f,
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
            j.axis = new Vector3(1f, 0f, 0f);
            j.secondaryAxis = new Vector3(0f, 1f, 0f);

            j.xMotion = ConfigurableJointMotion.Limited;   // small fore/aft slide
            j.yMotion = ConfigurableJointMotion.Locked;    // never leaves the rail
            j.zMotion = ConfigurableJointMotion.Locked;
            j.linearLimit = new SoftJointLimit { limit = 0.10f };
            j.linearLimitSpring = new SoftJointLimitSpring { spring = 1500f, damper = 45f };

            j.angularXMotion = ConfigurableJointMotion.Locked;
            j.angularYMotion = ConfigurableJointMotion.Locked;
            j.angularZMotion = ConfigurableJointMotion.Locked;

            // firm pull back to this foot's home X -> feet keep their stagger,
            // never cross, and the pelvis can't drop by merging the feet
            j.xDrive = new JointDrive { positionSpring = 2600f, positionDamper = 60f, maximumForce = 9000f };

            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.02f;
            j.projectionAngle = 4f;
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

        static Transform ForceAnchor(string name, Vector3 pos, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = pos;
            return go.transform;
        }

        static Transform RopeAttach(Rigidbody foot, string name, float facing)
        {
            var go = new GameObject(name);
            go.transform.SetParent(foot.transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(-facing * 0.09f, 0.04f, 0f); // heel side
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

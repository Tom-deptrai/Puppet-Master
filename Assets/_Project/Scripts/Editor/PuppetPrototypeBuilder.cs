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
    /// Phase 1.2 tool. Generates the physics prototype scene from scratch:
    /// one jointed puppet on its side of the arena FACING the opponent, two feet
    /// fore/aft on a rail, two control ropes whose visuals now run DOWN through a
    /// rail slot and off the bottom of the screen (nothing above the puppet).
    ///
    /// Facing model:
    ///   PlayerSide.Left  -> puppet at -X, faces +X  (opponent to the right)
    ///   PlayerSide.Right -> puppet at +X, faces -X  (opponent to the left)
    ///   facingSign = +1 (Left) / -1 (Right).
    ///
    /// Joint axis convention (Phase 1.2): every driven ConfigurableJoint is built
    /// with axis = (1,0,0), secondaryAxis = (0,1,0). That makes the joint space
    /// identity, so the controller just sets targetRotation = Inverse(worldTarget).
    ///   angularX  (world X) = INWARD / OUTWARD depth lean   (limited, per joint)
    ///   angularY  (world Y) = yaw                            (HARD LOCKED)
    ///   angularZ  (world Z) = forward/back lean + squat      (limited)
    ///
    /// Re-runnable — overwrites Assets/_Project/Scenes/PuppetPrototype.unity.
    /// Never touches Bootstrap.
    /// </summary>
    public static class PuppetPrototypeBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/PuppetPrototype.unity";
        const string MatDir = "Assets/_Project/Materials/Prototype";

        const float ArenaOffset = 1.0f;
        const float StanceHalf = 0.16f;
        const float FootY = 0.085f;
        const float ShoulderZ = 0.19f;

        [MenuItem("Puppet Master/Phase 1/Build Puppet Prototype — Left side")]
        public static void BuildLeft() => Build(PlayerSide.Left);

        [MenuItem("Puppet Master/Phase 1/Build Puppet Prototype — Right side (mirror test)")]
        public static void BuildRight() => Build(PlayerSide.Right);

        public static void Build(PlayerSide side)
        {
            float sideSign = side.Sign();           // -1 Left, +1 Right
            float originX = sideSign * ArenaOffset;
            float facing = -sideSign;               // +1 faces +X, -1 faces -X
            float Fwd(float d) => originX + facing * d;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Material mBody = Mat("Puppet_Body", new Color(0.74f, 0.62f, 0.45f));
            Material mLimb = Mat("Puppet_Arm", new Color(0.60f, 0.49f, 0.36f));
            Material mLeg = Mat("Puppet_Leg", new Color(0.49f, 0.55f, 0.63f));
            Material mFoot = Mat("Puppet_Foot", new Color(0.96f, 0.76f, 0.24f));
            Material mFace = Mat("Puppet_Face", new Color(0.95f, 0.35f, 0.30f));
            Material mRail = Mat("Rail", new Color(0.85f, 0.30f, 0.28f));
            Material mSlot = Mat("Rail_Slot", new Color(0.16f, 0.17f, 0.20f));
            Material mGround = Mat("Ground", new Color(0.13f, 0.14f, 0.16f));
            Material mRope = UnlitMat("Rope", Color.white);

            ConfigureCameraAndLight(originX, facing);
            BuildEnvironment(originX, mGround, mRail);

            var puppetGo = new GameObject($"Puppet_{side}");
            var puppet = puppetGo.transform;

            // ---- torso column (X-centred over the stance) ----
            var pelvis = Part("Pelvis", PrimitiveType.Cube, new Vector3(Fwd(0f), 1.06f, 0f), new Vector3(0.24f, 0.20f, 0.30f), 10f, mBody, puppet, 2.4f);
            var torso = Part("Torso", PrimitiveType.Cube, new Vector3(Fwd(0f), 1.40f, 0f), new Vector3(0.26f, 0.44f, 0.34f), 13f, mBody, puppet, 2.4f);
            var head = Part("Head", PrimitiveType.Sphere, new Vector3(Fwd(0f), 1.79f, 0f), new Vector3(0.22f, 0.22f, 0.22f), 2.6f, mBody, puppet, 1.8f);

            var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
            face.name = "Face";
            Object.DestroyImmediate(face.GetComponent<Collider>());
            face.transform.SetParent(head.transform, false);
            face.transform.localPosition = new Vector3(facing * 0.12f, 0f, 0f);
            face.transform.localScale = new Vector3(0.06f, 0.09f, 0.13f);
            face.GetComponent<MeshRenderer>().sharedMaterial = mFace;

            // ---- arms: exactly 2 arms total (Shoulder -> Upper Arm -> Elbow Joint -> Forearm -> Hand) ----
            var (uArmL, lArmL, handL, elbowL) = BuildArm("L", ShoulderZ, mLimb, mLimb, mFoot, puppet, facing, originX);
            var (uArmR, lArmR, handR, elbowR) = BuildArm("R", -ShoulderZ, mLimb, mLimb, mFoot, puppet, facing, originX);

            // ---- legs: fore/aft on the rail, exact fore↔aft mirror.
            //      Foot_R LEADS (toward opponent, +facing); Foot_L is the rear foot.
            //      This keeps each rope on its own screen half (no crossing under the rail):
            //      Left rope -> Foot_L (rear, this player's back) -> left thumb zone. ----
            var uLegL = Part("UpperLeg_L", PrimitiveType.Capsule, new Vector3(Fwd(-0.07f), 0.76f, 0f), new Vector3(0.12f, 0.22f, 0.12f), 6.5f, mLeg, puppet, 1.6f);
            var lLegL = Part("LowerLeg_L", PrimitiveType.Capsule, new Vector3(Fwd(-0.12f), 0.32f, 0f), new Vector3(0.11f, 0.21f, 0.11f), 4.5f, mLeg, puppet, 2.4f);
            var footL = Part("Foot_L", PrimitiveType.Cube, new Vector3(Fwd(-StanceHalf), FootY, 0f), new Vector3(0.26f, 0.07f, 0.13f), 4.2f, mFoot, puppet, 3.0f);
            var uLegR = Part("UpperLeg_R", PrimitiveType.Capsule, new Vector3(Fwd(0.07f), 0.76f, 0f), new Vector3(0.12f, 0.22f, 0.12f), 6.5f, mLeg, puppet, 1.6f);
            var lLegR = Part("LowerLeg_R", PrimitiveType.Capsule, new Vector3(Fwd(0.12f), 0.32f, 0f), new Vector3(0.11f, 0.21f, 0.11f), 4.5f, mLeg, puppet, 2.4f);
            var footR = Part("Foot_R", PrimitiveType.Cube, new Vector3(Fwd(StanceHalf), FootY, 0f), new Vector3(0.26f, 0.07f, 0.13f), 4.2f, mFoot, puppet, 3.0f);

            // ---- joints. axis=(1,0,0), secondary=(0,1,0):
            //      angZ = fight plane (fwd/back + squat), angX = depth, angY = yaw(LOCKED). ----
            var spine = Bend(torso, pelvis, new Vector3(Fwd(0f), 1.17f, 0f), fight: 75f, depth: 55f, driven: true);
            var neck = Bend(head, torso, new Vector3(Fwd(0f), 1.64f, 0f), fight: 55f, depth: 55f, driven: true);

            var shoulderL = Bend(uArmL, torso, new Vector3(Fwd(0f), 1.50f, ShoulderZ), fight: 120f, depth: 75f, driven: false, passiveSpring: 45f);
            var shoulderR = Bend(uArmR, torso, new Vector3(Fwd(0f), 1.50f, -ShoulderZ), fight: 120f, depth: 75f, driven: false, passiveSpring: 45f);

            var hipL = Bend(uLegL, pelvis, new Vector3(Fwd(-0.035f), 0.97f, 0f), fight: 140f, depth: 50f, driven: true);
            var kneeL = Bend(lLegL, uLegL, new Vector3(Fwd(-0.11f), 0.54f, 0f), fight: 150f, depth: 0f, driven: true);
            var ankleL = Bend(footL, lLegL, new Vector3(Fwd(-0.15f), 0.11f, 0f), fight: 60f, depth: 18f, driven: true);
            var hipR = Bend(uLegR, pelvis, new Vector3(Fwd(0.035f), 0.97f, 0f), fight: 140f, depth: 50f, driven: true);
            var kneeR = Bend(lLegR, uLegR, new Vector3(Fwd(0.11f), 0.54f, 0f), fight: 150f, depth: 0f, driven: true);
            var ankleR = Bend(footR, lLegR, new Vector3(Fwd(0.15f), 0.11f, 0f), fight: 60f, depth: 18f, driven: true);

            // ---- rail: each foot slides a little on X; everything else locked flat ----
            var railL = RailJoint(footL, new Vector3(Fwd(-StanceHalf), footL.position.y, 0f));
            var railR = RailJoint(footR, new Vector3(Fwd(StanceHalf), footR.position.y, 0f));

            // ---- pelvis -> world: Z position locked (stay in the fight plane),
            //      2-axis lean drive (fight plane + depth), yaw hard-locked. ----
            var plane = pelvis.gameObject.AddComponent<ConfigurableJoint>();
            plane.connectedBody = null;
            plane.autoConfigureConnectedAnchor = false;
            plane.anchor = Vector3.zero;
            plane.connectedAnchor = pelvis.position;
            plane.axis = new Vector3(1f, 0f, 0f);
            plane.secondaryAxis = new Vector3(0f, 1f, 0f);
            plane.xMotion = ConfigurableJointMotion.Free;   // shuffle fore/aft, rise/fall
            plane.yMotion = ConfigurableJointMotion.Free;
            plane.zMotion = ConfigurableJointMotion.Locked; // never leaves the fight plane
            plane.angularXMotion = ConfigurableJointMotion.Limited; // depth lean
            plane.angularYMotion = ConfigurableJointMotion.Locked;  // NO yaw
            plane.angularZMotion = ConfigurableJointMotion.Limited; // fwd/back lean
            plane.lowAngularXLimit = new SoftJointLimit { limit = -55f };
            plane.highAngularXLimit = new SoftJointLimit { limit = 55f };
            plane.angularZLimit = new SoftJointLimit { limit = 78f };
            plane.rotationDriveMode = RotationDriveMode.Slerp;
            plane.slerpDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = 0f };
            plane.projectionMode = JointProjectionMode.PositionAndRotation;
            plane.projectionDistance = 0.05f;
            plane.enablePreprocessing = false;
            plane.configuredInWorldSpace = false;

            // ---- rope rig: NOTHING above the puppet. Foot_L is the REAR foot. ----
            var rigging = new GameObject("Rigging").transform;
            RailSlotGuide("Slot_L", Fwd(-StanceHalf), rigging, mSlot);
            RailSlotGuide("Slot_R", Fwd(StanceHalf), rigging, mSlot);
            var railSlotL = Marker("RailSlot_L", new Vector3(Fwd(-StanceHalf), 0.05f, 0f), rigging);
            var railSlotR = Marker("RailSlot_R", new Vector3(Fwd(StanceHalf), 0.05f, 0f), rigging);
            var belowL = Marker("BelowRail_L", new Vector3(Fwd(-StanceHalf - 0.05f), -0.02f, -0.95f), rigging);
            var belowR = Marker("BelowRail_R", new Vector3(Fwd(StanceHalf + 0.05f), -0.02f, -0.95f), rigging);
            var forceL = Marker("ForceAnchor_L", new Vector3(Fwd(-StanceHalf), -0.30f, 0f), rigging);
            var forceR = Marker("ForceAnchor_R", new Vector3(Fwd(StanceHalf), -0.30f, 0f), rigging);
            var attachL = RopeAttach(footL, "RopeAttach_L");
            var attachR = RopeAttach(footR, "RopeAttach_R");

            // ---- runtime scripts ----
            var rig = puppetGo.AddComponent<PuppetRig>();
            rig.side = side;
            rig.facingSign = facing;
            rig.pelvis = pelvis; rig.torso = torso; rig.head = head;
            rig.spine = spine; rig.neck = neck; rig.pelvisPlaneJoint = plane;
            rig.standingPelvisHeight = pelvis.position.y;
            rig.standingHeadHeight = head.position.y;
            rig.standingFootSeparation = Mathf.Abs(footL.position.x - footR.position.x);
            rig.left = new PuppetRig.Leg
            {
                upperLeg = uLegL, lowerLeg = lLegL, foot = footL,
                hip = hipL, knee = kneeL, ankle = ankleL, railJoint = railL,
                ropeAttach = attachL, railSlot = railSlotL, belowRail = belowL, forceAnchor = forceL,
                railHomeX = footL.position.x,
            };
            rig.right = new PuppetRig.Leg
            {
                upperLeg = uLegR, lowerLeg = lLegR, foot = footR,
                hip = hipR, knee = kneeR, ankle = ankleR, railJoint = railR,
                ropeAttach = attachR, railSlot = railSlotR, belowRail = belowR, forceAnchor = forceR,
                railHomeX = footR.position.x,
            };

            var controller = puppetGo.AddComponent<PuppetRopeController>();
            var input = puppetGo.AddComponent<PuppetRopeInput>();
            var hud = puppetGo.AddComponent<PuppetDebugHUD>();
            hud.controller = controller; hud.rig = rig; hud.input = input;

            var ropes = new GameObject("Ropes").transform;
            MakeRope("Rope_L", ropes, controller, attachL, railSlotL, belowL, isLeft: true, mRope);
            MakeRope("Rope_R", ropes, controller, attachR, railSlotR, belowR, isLeft: false, mRope);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = puppetGo;

            Debug.Log(
                $"[Phase 1.2] Built PuppetPrototype.unity  (side = {side}, faces {(facing > 0 ? "+X" : "-X")})\n" +
                "  4-axis: tension L/R, fwd/back = tension diff, in/out = averaged horizontal input\n" +
                "  joints axis=(1,0,0): angZ = fight plane, angX = depth, angY = yaw LOCKED\n" +
                "  ropes run foot -> rail slot -> below the rail -> off the bottom of the screen\n" +
                "  Play: A / L / Space (rope) · Q / E (inward/outward) · drag ↓ tension ↔ depth");
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
                // 3/4 view: ahead of the puppet (opponent side) + a little above,
                // so BOTH forward/back and inward/outward lean read clearly.
                Vector3 camPos = new Vector3(originX + facing * 1.5f, 1.62f, -4.7f);
                Vector3 look = new Vector3(originX - facing * 0.05f, 1.02f, 0.05f);
                cam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(look - camPos));
                cam.fieldOfView = 44f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
                cam.nearClipPlane = 0.05f;
            }

            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(48f, 40f * -facing, 0f);
                sun.intensity = 1.2f;
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
            r.transform.position = new Vector3(originX, 0.025f, 0f);
            r.transform.localScale = new Vector3(1.20f, 0.05f, 0.24f);
            r.GetComponent<MeshRenderer>().sharedMaterial = rail;
        }

        static (Rigidbody uArm, Rigidbody lArm, Rigidbody hand, ConfigurableJoint elbow) BuildArm(
            string suffix, float shoulderZ, Material mArm, Material mForearm, Material mHand,
            Transform parent, float facing, float originX)
        {
            float Fwd(float d) => originX + facing * d;

            Vector3 uArmPos = new Vector3(Fwd(0.01f), 1.34f, shoulderZ);
            Vector3 elbowPos = new Vector3(Fwd(0.03f), 1.18f, shoulderZ);
            Vector3 lArmPos = new Vector3(Fwd(0.10f), 1.06f, shoulderZ);
            Vector3 wristPos = new Vector3(Fwd(0.17f), 0.95f, shoulderZ);
            Vector3 handPos = new Vector3(Fwd(0.20f), 0.92f, shoulderZ);

            var uArm = Part($"UpperArm_{suffix}", PrimitiveType.Capsule, uArmPos, new Vector3(0.09f, 0.14f, 0.09f), 2.0f, mArm, parent, 1.8f);

            var elbowSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            elbowSphere.name = $"Elbow_{suffix}_Mesh";
            Object.DestroyImmediate(elbowSphere.GetComponent<Collider>());
            elbowSphere.transform.SetParent(uArm.transform, worldPositionStays: true);
            elbowSphere.transform.position = elbowPos;
            elbowSphere.transform.localScale = new Vector3(0.095f, 0.095f, 0.095f);
            elbowSphere.GetComponent<MeshRenderer>().sharedMaterial = mArm;

            var lArm = Part($"LowerArm_{suffix}", PrimitiveType.Capsule, lArmPos, new Vector3(0.08f, 0.13f, 0.08f), 1.5f, mForearm, parent, 2.0f);
            var hand = Part($"Hand_{suffix}", PrimitiveType.Cube, handPos, new Vector3(0.08f, 0.07f, 0.08f), 0.8f, mHand, parent, 2.5f);

            var elbow = Bend(lArm, uArm, elbowPos, fight: 120f, depth: 35f, driven: false, passiveSpring: 35f);
            var wrist = Bend(hand, lArm, wristPos, fight: 60f, depth: 25f, driven: false, passiveSpring: 40f);

            return (uArm, lArm, hand, elbow);
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
            rb.maxAngularVelocity = 28f;
            rb.solverIterations = 48;
            rb.solverVelocityIterations = 24;
            return rb;
        }

        /// <summary>
        /// ConfigurableJoint child->parent at worldAnchor.
        /// axis=(1,0,0), secondary=(0,1,0)  ->  joint space is identity.
        ///   angularZ = fight plane (fwd/back + squat)  -> Limited to `fight`
        ///   angularX = depth (inward/outward)          -> Limited to ±`depth` (0 => Locked)
        ///   angularY = yaw                             -> HARD LOCKED
        /// </summary>
        static ConfigurableJoint Bend(Rigidbody child, Rigidbody parent, Vector3 worldAnchor,
            float fight, float depth, bool driven, float passiveSpring = 6f)
        {
            var j = child.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = parent;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = child.transform.InverseTransformPoint(worldAnchor);
            j.connectedAnchor = parent.transform.InverseTransformPoint(worldAnchor);
            j.axis = new Vector3(1f, 0f, 0f);
            j.secondaryAxis = new Vector3(0f, 1f, 0f);

            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = depth > 0.5f ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Locked;
            j.angularYMotion = ConfigurableJointMotion.Locked;   // NO yaw, ever
            j.angularZMotion = ConfigurableJointMotion.Limited;
            j.lowAngularXLimit = new SoftJointLimit { limit = -Mathf.Max(1f, depth) };
            j.highAngularXLimit = new SoftJointLimit { limit = Mathf.Max(1f, depth) };
            j.angularZLimit = new SoftJointLimit { limit = Mathf.Max(1f, fight) };

            var noSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
            j.angularXLimitSpring = noSpring;
            j.angularYZLimitSpring = noSpring;

            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.slerpDrive = new JointDrive
            {
                positionSpring = driven ? 0f : passiveSpring,
                positionDamper = driven ? 0f : passiveSpring * 0.25f,
                maximumForce = driven ? 0f : 180f,
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
            j.linearLimitSpring = new SoftJointLimitSpring { spring = 1800f, damper = 55f };

            j.angularXMotion = ConfigurableJointMotion.Locked; // foot stays flat on the rail
            j.angularYMotion = ConfigurableJointMotion.Locked;
            j.angularZMotion = ConfigurableJointMotion.Locked;

            j.xDrive = new JointDrive { positionSpring = 3000f, positionDamper = 70f, maximumForce = 12000f };

            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.02f;
            j.projectionAngle = 4f;
            j.enablePreprocessing = false;
            return j;
        }

        static void RailSlotGuide(string name, float x, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = new Vector3(x, 0.048f, 0f);
            go.transform.localScale = new Vector3(0.06f, 0.012f, 0.18f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static Transform Marker(string name, Vector3 pos, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = pos;
            return go.transform;
        }

        static Transform RopeAttach(Rigidbody foot, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(foot.transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, -0.02f, 0f); // bottom of the foot
            return go.transform;
        }

        static void MakeRope(string name, Transform parent, PuppetRopeController controller,
            Transform attach, Transform slot, Transform below, bool isLeft, Material mat)
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
            rv.footAttach = attach;
            rv.railSlot = slot;
            rv.belowRail = below;
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

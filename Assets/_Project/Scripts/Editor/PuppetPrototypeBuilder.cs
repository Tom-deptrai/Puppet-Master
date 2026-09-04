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
        const float FootHeight = 0.07f;
        const float FootLength = 0.26f;
        const float FootWidth = 0.13f;
        const float ShoulderZ = 0.25f;

        const float RailHeight = 0.05f;
        const float RailWidth = 0.15f;
        const float RailLength = 1.50f;
        const float RailTopY = FootY - FootHeight * 0.5f; // 0.050f
        const float RailCenterY = RailTopY - RailHeight * 0.5f; // 0.025f

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
            Material mBlade = Mat("Weapon_Blade", new Color(0.85f, 0.88f, 0.92f));
            Material mHilt = Mat("Weapon_Hilt", new Color(0.25f, 0.20f, 0.16f));
            Material mGuard = Mat("Weapon_Guard", new Color(0.75f, 0.62f, 0.35f));

            ConfigureCameraAndLight(originX, facing);
            BuildEnvironment(originX, mGround, mRail, mSlot);

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

            // ---- arms: exactly 2 arms total in a realistic guard pose ----
            var (uArmL, lArmL, handL, elbowL, wristL) = BuildArm("L", ShoulderZ, mLimb, mLimb, mFoot, puppet, facing, originX);
            var (uArmR, lArmR, handR, elbowR, wristR) = BuildArm("R", -ShoulderZ, mLimb, mLimb, mFoot, puppet, facing, originX);
            var sword = BuildSword(handR, puppet, facing, originX, mBlade, mHilt, mGuard);

            // ---- legs: fore/aft on the rail, exact fore↔aft mirror.
            //      Foot_R LEADS (toward opponent, +facing); Foot_L is the rear foot.
            //      This keeps each rope on its own screen half (no crossing under the rail):
            //      Left rope -> Foot_L (rear, this player's back) -> left thumb zone. ----
            var uLegL = Part("UpperLeg_L", PrimitiveType.Capsule, new Vector3(Fwd(-0.07f), 0.76f, 0f), new Vector3(0.12f, 0.22f, 0.12f), 6.5f, mLeg, puppet, 1.6f);
            var lLegL = Part("LowerLeg_L", PrimitiveType.Capsule, new Vector3(Fwd(-0.12f), 0.32f, 0f), new Vector3(0.11f, 0.21f, 0.11f), 4.5f, mLeg, puppet, 2.4f);
            var footL = Part("Foot_L", PrimitiveType.Cube, new Vector3(Fwd(-StanceHalf), FootY, 0f), new Vector3(FootLength, FootHeight, FootWidth), 4.2f, mFoot, puppet, 3.0f);
            var uLegR = Part("UpperLeg_R", PrimitiveType.Capsule, new Vector3(Fwd(0.07f), 0.76f, 0f), new Vector3(0.12f, 0.22f, 0.12f), 6.5f, mLeg, puppet, 1.6f);
            var lLegR = Part("LowerLeg_R", PrimitiveType.Capsule, new Vector3(Fwd(0.12f), 0.32f, 0f), new Vector3(0.11f, 0.21f, 0.11f), 4.5f, mLeg, puppet, 2.4f);
            var footR = Part("Foot_R", PrimitiveType.Cube, new Vector3(Fwd(StanceHalf), FootY, 0f), new Vector3(FootLength, FootHeight, FootWidth), 4.2f, mFoot, puppet, 3.0f);

            // ---- joints. axis=(1,0,0), secondary=(0,1,0):
            //      angZ = fight plane (fwd/back + squat), angX = depth, angY = yaw(LOCKED). ----
            var spine = Bend(torso, pelvis, new Vector3(Fwd(0f), 1.17f, 0f), fight: 75f, depth: 55f, driven: true);
            var neck = Bend(head, torso, new Vector3(Fwd(0f), 1.64f, 0f), fight: 55f, depth: 55f, driven: true);

            var shoulderL = BendShoulder(uArmL, torso, new Vector3(Fwd(0.02f), 1.46f, ShoulderZ), 75f, 45f, 40f, 350f, 40f);
            var shoulderR = BendShoulder(uArmR, torso, new Vector3(Fwd(0.02f), 1.46f, -ShoulderZ), 85f, 55f, 60f, 750f, 65f);

            var hipL = Bend(uLegL, pelvis, new Vector3(Fwd(-0.035f), 0.97f, 0f), fight: 140f, depth: 55f, driven: true);
            var kneeL = Bend(lLegL, uLegL, new Vector3(Fwd(-0.11f), 0.54f, 0f), fight: 150f, depth: 15f, driven: true);
            var ankleL = Bend(footL, lLegL, new Vector3(Fwd(-0.15f), 0.11f, 0f), fight: 60f, depth: 45f, driven: true);
            var hipR = Bend(uLegR, pelvis, new Vector3(Fwd(0.035f), 0.97f, 0f), fight: 140f, depth: 55f, driven: true);
            var kneeR = Bend(lLegR, uLegR, new Vector3(Fwd(0.11f), 0.54f, 0f), fight: 150f, depth: 15f, driven: true);
            var ankleR = Bend(footR, lLegR, new Vector3(Fwd(0.15f), 0.11f, 0f), fight: 60f, depth: 45f, driven: true);

            // ---- rail: each foot slides a little on X; everything else locked flat ----
            var railL = RailJoint(footL, new Vector3(Fwd(-StanceHalf), FootY, 0f));
            var railR = RailJoint(footR, new Vector3(Fwd(StanceHalf), FootY, 0f));

            // ---- pelvis -> world: Z position allows depth arc swing,
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
            plane.zMotion = ConfigurableJointMotion.Limited; // allows full-body depth lean swing
            plane.linearLimit = new SoftJointLimit { limit = 0.55f };
            plane.linearLimitSpring = new SoftJointLimitSpring { spring = 6000f, damper = 150f };
            plane.zDrive = new JointDrive { positionSpring = 8000f, positionDamper = 350f, maximumForce = 60000f };

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
            var railSlotL = Marker("RailSlot_L", new Vector3(Fwd(-StanceHalf), RailTopY, 0f), rigging);
            var railSlotR = Marker("RailSlot_R", new Vector3(Fwd(StanceHalf), RailTopY, 0f), rigging);
            var belowL = Marker("BelowRail_L", new Vector3(Fwd(-StanceHalf), -0.06f, 0f), rigging);
            var belowR = Marker("BelowRail_R", new Vector3(Fwd(StanceHalf), -0.06f, 0f), rigging);
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
            rig.sword = sword;
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
            rig.leftArm = new PuppetRig.Arm
            {
                upperArm = uArmL, lowerArm = lArmL, hand = handL,
                shoulder = shoulderL, elbow = elbowL, wrist = wristL,
            };
            rig.rightArm = new PuppetRig.Arm
            {
                upperArm = uArmR, lowerArm = lArmR, hand = handR,
                shoulder = shoulderR, elbow = elbowR, wrist = wristR,
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

        static void BuildEnvironment(float originX, Material ground, Material rail, Material groove)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
            g.name = "Ground";
            g.transform.position = Vector3.zero;
            g.transform.localScale = new Vector3(6f, 1f, 2f);
            g.GetComponent<MeshRenderer>().sharedMaterial = ground;

            var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r.name = "Rail";
            Object.DestroyImmediate(r.GetComponent<BoxCollider>());
            r.transform.position = new Vector3(originX, RailCenterY, 0f);
            r.transform.localScale = new Vector3(RailLength, RailHeight, RailWidth);
            r.GetComponent<MeshRenderer>().sharedMaterial = rail;

            var gr = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gr.name = "Rail_Groove";
            Object.DestroyImmediate(gr.GetComponent<BoxCollider>());
            gr.transform.SetParent(r.transform, worldPositionStays: false);
            gr.transform.localPosition = new Vector3(0f, 0.501f, 0f);
            gr.transform.localScale = new Vector3(1f, 0.02f, 0.28f);
            gr.GetComponent<MeshRenderer>().sharedMaterial = groove;
        }

        static (Rigidbody uArm, Rigidbody lArm, Rigidbody hand, ConfigurableJoint elbow, ConfigurableJoint wrist) BuildArm(
            string suffix, float shoulderZ, Material mArm, Material mForearm, Material mHand,
            Transform parent, float facing, float originX)
        {
            float Fwd(float d) => originX + facing * d;
            bool isRight = suffix == "R";

            Vector3 uArmPos;
            Vector3 elbowPos;
            Vector3 lArmPos;
            Vector3 wristPos;
            Vector3 handPos;

            if (isRight)
            {
                // Right arm: Holds sword forward in front of body.
                // Shoulder is at shoulderZ (-0.25m), flared outside torso box (ends at -0.17m).
                // Elbow is positioned forward and laterally with clear gap.
                // Forearm points forward-upward, hand in front of chest.
                uArmPos = new Vector3(Fwd(0.09f), 1.35f, shoulderZ * 1.02f);
                elbowPos = new Vector3(Fwd(0.16f), 1.23f, shoulderZ * 0.96f);
                lArmPos = new Vector3(Fwd(0.28f), 1.33f, shoulderZ * 0.82f);
                wristPos = new Vector3(Fwd(0.36f), 1.40f, shoulderZ * 0.70f);
                handPos = new Vector3(Fwd(0.40f), 1.43f, shoulderZ * 0.64f);
            }
            else
            {
                // Left arm: Natural guard/balance pose.
                // Shoulder flared at shoulderZ (+0.25m), elbow clear of torso,
                // forearm flexed forward-upward protecting chest/head.
                uArmPos = new Vector3(Fwd(0.07f), 1.36f, shoulderZ * 1.02f);
                elbowPos = new Vector3(Fwd(0.14f), 1.25f, shoulderZ * 0.98f);
                lArmPos = new Vector3(Fwd(0.22f), 1.34f, shoulderZ * 0.86f);
                wristPos = new Vector3(Fwd(0.28f), 1.41f, shoulderZ * 0.76f);
                handPos = new Vector3(Fwd(0.31f), 1.44f, shoulderZ * 0.70f);
            }

            var uArm = Part($"UpperArm_{suffix}", PrimitiveType.Capsule, uArmPos, new Vector3(0.075f, 0.12f, 0.075f), 1.8f, mArm, parent, 1.8f);

            var elbowSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            elbowSphere.name = $"Elbow_{suffix}_Mesh";
            Object.DestroyImmediate(elbowSphere.GetComponent<Collider>());
            elbowSphere.transform.SetParent(uArm.transform, worldPositionStays: true);
            elbowSphere.transform.position = elbowPos;
            elbowSphere.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f);
            elbowSphere.GetComponent<MeshRenderer>().sharedMaterial = mArm;

            var lArm = Part($"LowerArm_{suffix}", PrimitiveType.Capsule, lArmPos, new Vector3(0.065f, 0.11f, 0.065f), 1.4f, mForearm, parent, 2.0f);
            var hand = Part($"Hand_{suffix}", PrimitiveType.Cube, handPos, new Vector3(0.07f, 0.07f, 0.07f), 0.6f, mHand, parent, 2.5f);

            // Elbow: Hinge in sagittal/fight plane. Allows forward guard extension/flexion.
            var elbow = BendElbow(lArm, uArm, elbowPos, lowFight: -10f, highFight: 135f, depth: 20f, spring: isRight ? 750f : 280f);

            // Wrist: Keeps hand firmly at the end of the forearm in guard
            var wrist = Bend(hand, lArm, wristPos, fight: 35f, depth: 25f, driven: false, passiveSpring: isRight ? 600f : 240f);

            return (uArm, lArm, hand, elbow, wrist);
        }

        static Rigidbody BuildSword(
            Rigidbody hand, Transform parent, float facing, float originX,
            Material mBlade, Material mHilt, Material mGuard)
        {
            Vector3 gripPos = hand.transform.position;

            // Blade direction: points forward toward opponent (+facing) and angled ~25 deg above horizontal
            Vector3 bladeDir = new Vector3(facing * 0.85f, 0.50f, -0.15f).normalized;
            Quaternion swordRot = Quaternion.FromToRotation(Vector3.up, bladeDir);

            var swordGo = new GameObject("Sword_R");
            swordGo.transform.SetParent(parent, worldPositionStays: true);
            swordGo.transform.SetPositionAndRotation(gripPos, swordRot);

            // 1. Handle (cylindrical grip)
            var handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            handle.name = "Handle_Mesh";
            Object.DestroyImmediate(handle.GetComponent<Collider>());
            handle.transform.SetParent(swordGo.transform, worldPositionStays: false);
            handle.transform.localPosition = new Vector3(0f, -0.03f, 0f);
            handle.transform.localRotation = Quaternion.identity;
            handle.transform.localScale = new Vector3(0.032f, 0.07f, 0.032f); // diameter 0.032m, length 0.14m
            handle.GetComponent<MeshRenderer>().sharedMaterial = mHilt;

            // Pommel at bottom of handle
            var pommel = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pommel.name = "Pommel_Mesh";
            Object.DestroyImmediate(pommel.GetComponent<Collider>());
            pommel.transform.SetParent(swordGo.transform, worldPositionStays: false);
            pommel.transform.localPosition = new Vector3(0f, -0.105f, 0f);
            pommel.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
            pommel.GetComponent<MeshRenderer>().sharedMaterial = mGuard;

            // 2. Crossguard (crossbar between handle and blade)
            var guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            guard.name = "Guard_Mesh";
            Object.DestroyImmediate(guard.GetComponent<Collider>());
            guard.transform.SetParent(swordGo.transform, worldPositionStays: false);
            guard.transform.localPosition = new Vector3(0f, 0.045f, 0f);
            guard.transform.localRotation = Quaternion.identity;
            guard.transform.localScale = new Vector3(0.12f, 0.022f, 0.04f);
            guard.GetComponent<MeshRenderer>().sharedMaterial = mGuard;

            // 3. Blade (straight flat blade)
            var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blade.name = "Blade_Mesh";
            Object.DestroyImmediate(blade.GetComponent<Collider>());
            blade.transform.SetParent(swordGo.transform, worldPositionStays: false);
            blade.transform.localPosition = new Vector3(0f, 0.44f, 0f);
            blade.transform.localRotation = Quaternion.identity;
            blade.transform.localScale = new Vector3(0.045f, 0.74f, 0.015f); // length 0.74m
            blade.GetComponent<MeshRenderer>().sharedMaterial = mBlade;

            // Colliders on the sword Rigidbody
            var bladeCol = swordGo.AddComponent<BoxCollider>();
            bladeCol.center = new Vector3(0f, 0.44f, 0f);
            bladeCol.size = new Vector3(0.045f, 0.74f, 0.015f);

            var handleCol = swordGo.AddComponent<CapsuleCollider>();
            handleCol.center = new Vector3(0f, -0.03f, 0f);
            handleCol.radius = 0.025f;
            handleCol.height = 0.16f;
            handleCol.direction = 1; // Y-axis

            // Rigidbody
            var rb = swordGo.AddComponent<Rigidbody>();
            rb.mass = 1.2f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 1.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxAngularVelocity = 28f;
            rb.solverIterations = 48;
            rb.solverVelocityIterations = 24;

            // ConfigurableJoint to Hand_R
            var j = swordGo.AddComponent<ConfigurableJoint>();
            j.connectedBody = hand;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero; // at gripPos in sword local space
            j.connectedAnchor = hand.transform.InverseTransformPoint(gripPos);
            j.axis = new Vector3(1f, 0f, 0f);
            j.secondaryAxis = new Vector3(0f, 1f, 0f);

            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;

            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.01f;
            j.projectionAngle = 2f;
            j.enablePreprocessing = false;
            j.configuredInWorldSpace = false;

            return rb;
        }

        static ConfigurableJoint BendElbow(Rigidbody child, Rigidbody parent, Vector3 worldAnchor,
            float lowFight, float highFight, float depth, float spring)
        {
            var j = child.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = parent;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = child.transform.InverseTransformPoint(worldAnchor);
            j.connectedAnchor = parent.transform.InverseTransformPoint(worldAnchor);
            // Axis=(0,0,1) makes angularX correspond to rotation around world Z (the fight plane flexion)
            j.axis = new Vector3(0f, 0f, 1f);
            j.secondaryAxis = new Vector3(0f, 1f, 0f);

            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = ConfigurableJointMotion.Limited;
            j.angularYMotion = ConfigurableJointMotion.Locked; // No twist
            j.angularZMotion = ConfigurableJointMotion.Limited; // Depth compliance

            j.lowAngularXLimit = new SoftJointLimit { limit = lowFight };
            j.highAngularXLimit = new SoftJointLimit { limit = highFight };
            j.angularZLimit = new SoftJointLimit { limit = Mathf.Max(1f, depth) };

            var noSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
            j.angularXLimitSpring = noSpring;
            j.angularYZLimitSpring = noSpring;

            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.slerpDrive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = spring * 0.1f,
                maximumForce = 15000f,
            };

            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.03f;
            j.projectionAngle = 8f;
            j.enablePreprocessing = false;
            j.configuredInWorldSpace = false;
            return j;
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
        static ConfigurableJoint BendShoulder(Rigidbody arm, Rigidbody torso, Vector3 worldAnchor,
            float fightAngle, float depthAngle, float yawAngle, float spring, float damper)
        {
            var j = arm.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = torso;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = arm.transform.InverseTransformPoint(worldAnchor);
            j.connectedAnchor = torso.transform.InverseTransformPoint(worldAnchor);
            j.axis = new Vector3(1f, 0f, 0f);
            j.secondaryAxis = new Vector3(0f, 1f, 0f);

            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = depthAngle > 0.5f ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Locked;
            j.angularYMotion = yawAngle > 0.5f ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Locked;
            j.angularZMotion = ConfigurableJointMotion.Limited;

            j.lowAngularXLimit = new SoftJointLimit { limit = -Mathf.Max(1f, depthAngle) };
            j.highAngularXLimit = new SoftJointLimit { limit = Mathf.Max(1f, depthAngle) };
            j.angularYLimit = new SoftJointLimit { limit = Mathf.Max(1f, yawAngle) };
            j.angularZLimit = new SoftJointLimit { limit = Mathf.Max(1f, fightAngle) };

            var noSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
            j.angularXLimitSpring = noSpring;
            j.angularYZLimitSpring = noSpring;

            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.slerpDrive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
                maximumForce = 35000f,
            };

            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.03f;
            j.projectionAngle = 8f;
            j.enablePreprocessing = false;
            j.configuredInWorldSpace = false;
            return j;
        }

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
                maximumForce = driven ? 0f : 15000f,
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
            go.transform.position = new Vector3(x, RailTopY + 0.001f, 0f);
            go.transform.localScale = new Vector3(0.08f, 0.003f, RailWidth * 0.75f);
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
            go.transform.localPosition = new Vector3(0f, -FootHeight * 0.5f, 0f); // exact bottom of the foot
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

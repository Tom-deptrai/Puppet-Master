# Development Log — Puppet Master

Nhật ký phát triển theo thời gian. Ghi các thay đổi kỹ thuật, quyết định và kết
quả kiểm thử ở mức chi tiết. Các quyết định ở tầm dự án thì cập nhật vào
[`PROJECT_MASTER.md`](PROJECT_MASTER.md).

Định dạng: mới nhất ở trên cùng.

---

## 2026-09-04 — Phase 1.2: Advanced 4-axis puppet control

Hoàn thiện hệ điều khiển lõi trước khi thêm vũ khí. Báo cáo đầy đủ:
[`PhaseReports/PHASE_01_2_REPORT.md`](PhaseReports/PHASE_01_2_REPORT.md).
KHÔNG combat, KHÔNG vũ khí.

### Đổi convention trục joint (nền tảng cho mọi thứ khác)

Mọi ConfigurableJoint điều khiển giờ dựng với `axis=(1,0,0)`, `secondaryAxis=(0,1,0)`
→ joint-space = identity → controller chỉ cần `targetRotation = Inverse(worldTarget)`
(không còn conjugation mù mờ). Ba trục góc:

- `angularZ` (world Z) = **mặt phẳng chiến đấu** (forward/back lean + squat) — Limited
- `angularX` (world X) = **inward/outward** (depth lean) — Limited theo joint, khoá ở gối/bàn chân
- `angularY` (world Y) = **yaw** — **KHOÁ CỨNG ở mọi joint** → puppet không bao giờ quay lưng

### 1. Hệ 4 hướng (phần mới cốt lõi)

- `tension L/R` (0..1) mỗi dây.
- `combined tension` → **squat** (đối xứng cả hai chân).
- `tension difference (R−L)` → **forward/backward lean** (+ = về phía đối thủ).
- `average horizontal drag của 2 ngón` → **inward/outward lean** (+ = outward = nghiêng
  về phía camera / −Z; screen-consistent cho cả hai side).
- **Combine:** `leanWorld = AngleAxis(−facing·fbDeg, worldZ) * AngleAxis(−depthDeg, worldX)`
  — hai phép quay 1-trục world tường minh, **không bao giờ có thành phần quanh world Y**
  → diagonal (fwd+in/out) là một orientation thật, không có yaw ký sinh (đo được ≤ 1.6°).
- Pelvis→world joint gánh `leanWorld`; spine follow 0.26, neck 0.15.

### 2. Chống full-3D-ragdoll

1. `angularY` (yaw) khoá cứng mọi joint.
2. depth (`angularX`) chỉ mở ở pelvis/spine/neck/hip (±36–42°), nhỏ ở ankle (±12°),
   **khoá ở knee (0°) và foot**.
3. Pelvis khoá **z-position** (luôn ở mặt phẳng chiến đấu).
4. Foot: mọi angular khoá cứng vào world (bắt vít phẳng), chỉ trượt X ±0.10 m.
5. Depth là 1 DOF có kiểm soát, không phải twist tự do.

### 3. Tốc độ phản ứng (~4×+)

| | Phase 1.1 | Phase 1.2 |
|---|---|---|
| `tensionSmoothing` | 14 | **45** |
| input rise / fall | 3.6 / 2.3 | **12 / 10** |
| leg drive spring slack→stand | 240→2800 | **2600→7200** |
| pelvis spring slack→stand | 2800→4600 | **5200→10500** |
| spine spring slack→stand | 2200→3000 | **4200→7200** |
| part `maxAngularVelocity` | 12 | **28** |
| solver iters (per body) | 44 / 22 | **48 / 24** |

Đo bằng probe nội bộ (`PuppetRopeController.BeginProbe/ProbeReport`):
**phản ứng thấy được ~0.022–0.033 s**, **đạt 80% tư thế ~0.21–0.25 s** — trong mục tiêu
(0.1 s / 0.25–0.4 s), vẫn có inertia (không snap).

### 4. Crouch sâu hơn — nguyên nhân bug lớn

**Bug:** hip và knee cùng drive `+` quanh world Z → chân cuộn như lò xo thay vì gập
ép lại; knee kẹt ~60° dù target 138°; pelvis không xuống dưới ~58% và bị **jitter**
(limit-cycle của drive tự đánh nhau).
**Sửa:** `kneeSquatDeg = -118` (dấu **ngược** hip) → chân gập ép thật, gravity hỗ trợ,
pelvis xuống **~47–54%**, jitter biến mất (`vel` slack 0.15–0.24 → **0.04–0.09**).
`ankleSpringScale = 0.35` (ankle theo chứ không chống). Kinematics squat:
hip +62° / knee −118° / ankle +55° → bàn chân giữ phẳng (tổng ≈ 0).

### 5. Rope visual mới

Không còn dây lên trời. `RopeVisual`: `Foot → RailSlot (ở ray) → BelowRail (hướng
người xem, cao độ sàn) → điểm thumb (xuống + về phía camera + xoè trái/phải)`.
Ground opaque che phần dưới sàn nên dây "đi ra phía người chơi" thay vì xuyên sàn —
cùng ý nghĩa "hai ngón kéo dây từ dưới". Căng = thẳng/sáng/dày, chùng = võng/mờ/mảnh.
**Tách hoàn toàn khỏi lực gameplay** (lực kéo bàn chân **xuống** về `ForceAnchor` dưới
ray — "cắm" chân). Foot_R giờ là chân **dẫn** (Foot_L rear) để mỗi dây ở đúng nửa màn
hình, không chéo nhau.

### 6. Input mới

- `PuppetRopeInput.SetInput(left, right, depth)`. Mỗi ngón: dọc → tension bên đó,
  ngang → depth contribution (deadzone 16 px, full 170 px, clamp −1..1).
  `depth = trung bình 2 ngón`. Cả hai ngang **phải** cùng hướng mới ra depth.
- Desktop: `A/L` dây, `Space` cả hai, **`Q` inward / `E` outward**.
- EnhancedTouch: ownership zone cố định từ touch-start; hai ngón độc lập.

### 7. HUD

Thêm: Fwd/Back °, In/Out ° (kèm nhãn FORWARD/BACKWARD/INWARD/OUTWARD), Lean magnitude,
Depth input bar (signed), PlayerSide, Facing. Camera đổi sang 3/4 để thấy depth lean.

### Test (9-state + slack/taut + rapid + mirror), Left side

| State | fwd° | depth° | pelvis% | yaw° | vel | OK |
|---|---|---|---|---|---|---|
| A neutral (½/½/0) | +3 | +1.5 | 62 | −0.1 | 0.04 | ✅ |
| J slack (0/0/0) | −3.2 | +1.5 | 54 | −0.1 | 0.04 | ✅ |
| B/K taut (1/1/0) | +0.3 | 0.0 | 99 | 0.0 | 0.00 | ✅ |
| C forward (0/1/0) | **+67.3** | 0.0 | 68 | 0.0 | 0.03 | ✅ |
| D backward (1/0/0) | **−68.8** | +0.5 | 57 | 0.1 | 0.09 | ✅ |
| E inward (1/1/−1) | +1.4 | **−28.1** | 99 | 0.0 | 0.05 | ✅ |
| F outward (1/1/1) | +1.3 | **+28.0** | 99 | 0.0 | 0.05 | ✅ |
| G fwd+inward | +68.3 | −12.9 | 65 | −1.6 | 0.04 | ✅ |
| H fwd+outward | +68.3 | +12.9 | 65 | −0.2 | 0.04 | ✅ |
| I bwd+inward | ~−70 | ~−9 | ~48 | ~2 | ~0.1 | ✅ |
| bwd+outward | −71.2 | +8.9 | 48 | −1.4 | 0.11 | ✅ |
| L rapid transitions | — | — | — | peak angVel 0.9 | 0.05 | ✅ recover về A |
| M Right mirror | R-rope→forward +66.6° (giống Left, body-relative); +depth→outward | | | ≤0.7 | ✅ |

- **Fwd/back đối xứng:** +67.3 / −68.8 (lệch 1.5°, ~2%).
- **In/out đối xứng:** ±28°.
- Feet luôn tách (sep 0.26–0.38 m), không swap, `lowerLeg.z ≈ 0`, không xuyên.
- Console: **0 error / 0 warning** (Play + mọi state). Health Check Phase 0 vẫn PASSED.
  Bootstrap không đụng.

### Còn thử nghiệm / cần người dùng play-test

- Crouch ~50–54% (mục tiêu 40–45%) — sâu hơn 1.1 nhưng chưa đạt số; dáng "split-squat"
  rộng fore/aft. Ưu tiên ổn định.
- Diagonal: depth bị "ép" nhỏ lại (±13°) khi forward lean lớn (~68°) — hợp lý vật lý
  nhưng cảm giác depth yếu ở diagonal.
- Yaw ký sinh ≤ ~2° ở diagonal backward+depth (rất nhỏ, không phải spin).
- Rope visual đọc được nhưng chưa "đã mắt"; đoạn dưới ray ngắn.
- `debugLeft=1` = Right rope → forward (đảo `forwardBackSign` để hoán).
- 2 build hơi khác nhau (Left crouch 54%, Right 47%) — nhiễu vật lý.
- Multitouch chưa test trên thiết bị.

---

## 2026-09-04 — Phase 1.1: Tuning symmetry + rope layout + facing model

Chủ dự án chơi thử Phase 1, chốt 5 vấn đề + 1 bổ sung quan trọng về hệ quy chiếu.
Báo cáo đầy đủ: [`PhaseReports/PHASE_01_1_REPORT.md`](PhaseReports/PHASE_01_1_REPORT.md).

### Facing model (bổ sung — quan trọng nhất)

Puppet **không** quay mặt vào camera nữa. Mỗi puppet đứng một bên arena và **quay
mặt về phía đối thủ**:

- `PlayerSide.Left`  → puppet ở `X ≈ -1.0`, **facing +X** (đối thủ bên phải)
- `PlayerSide.Right` → puppet ở `X ≈ +1.0`, **facing -X** (đối thủ bên trái)
- `facingSign = -side.Sign()` (+1 cho Left). Mọi thứ trong builder tính từ
  `originX` + `Fwd(d)` = "cách puppet `d` mét về phía đối thủ".
- Quy chiếu tư thế: **Forward Lean** = nghiêng về phía đối thủ, **Backward Lean** =
  ra xa. HUD báo `ForwardLeanDeg` (dương = forward), độc lập với side.
- Input giữ theo **cơ thể** puppet: Left Rope = Foot_L, Right Rope = Foot_R.
  Mapping hiện tại: **Right rope căng → forward**, **Left rope căng → backward**
  (đảo dấu `leanFromImbalance` để hoán đổi).
- Body layout đổi: hai chân **fore/aft trên ray** (Foot_L dẫn trước), thân hẹp theo
  X + rộng theo Z, vai ở `±Z`, tay ở tư thế guard nhẹ hướng trước, có cục "mũi"
  (`Head/Face`) chỉ hướng facing để không nhầm.

### 1. Rope / pulley layout mới

- Puppet dời ra 1/3 màn hình bên mình (`originX = ±1.0`); vùng giữa (combat) sạch.
- Pulley treo **thẳng trên mỗi bàn chân** (`Fwd(±0.16)`, Y 3.05–3.35) — dây gần như
  thẳng đứng, **không cắt torso/head**, không hướng về phía đối thủ.
- Tách **lực** khỏi **hình**: `RopeVisual` vẽ dây tới `pulley`; lực kéo
  (`PuppetRopeController.RopePull`) tác động ở bàn chân về phía `forceAnchor` —
  điểm thẳng đứng ngay trên foot-home, đối xứng, không lệ thuộc pulley.
- `PuppetRig` thêm `side`, `Leg.forceAnchor`, `Leg.railHomeX`, `standingFootSeparation`.
- Mirror: 2 menu — *Puppet Master ▸ Phase 1 ▸ Build Puppet Prototype — Left side* và
  *— Right side (mirror test)*. `Build(PlayerSide)` dùng chung. Scene commit = Left.

### 2. Hai chân riêng biệt (sửa chồng chân)

- **Nguyên nhân Phase 1:** hai chân side-by-side, gối gập trong mặt phẳng màn hình
  ép hai cẳng chân chụm về giữa; `Physics.IgnoreCollision` tắt **toàn bộ** va chạm
  giữa các bộ phận → không có gì chặn chân xuyên nhau.
- **Sửa:** chân fore/aft (khác vị trí X trên ray) → hình học không còn ép chụm;
  rail `xDrive` spring **2600** giữ mỗi chân ở home (±0.16, cách nhau ~0.32 m),
  `xMotion` chỉ trượt ±0.10 m; `IgnoreAdjacentCollisions()` chỉ tắt va chạm giữa
  các cặp **nối khớp trực tiếp** → LowerLeg_L ↔ LowerLeg_R vẫn va chạm nhau.
- Kết quả mọi trạng thái: foot separation 0.29–0.36 m, không swap, không xuyên,
  hai cẳng chân luôn ở `Z ≈ 0` (không chồng theo chiều sâu).

### 3. Sửa asymmetry trái/phải

- **Nguyên nhân:** `spineFoldAngle`/`neckFoldAngle` (Phase 1) hardcode +8°/+10° về
  **một phía** (trục Z) làm target spine ở slack luôn lệch +Z; spring slack yếu để
  gravity khuếch đại (positive feedback) tới sát joint limit +50°. Torso slack lệch
  ~+48°. Cộng thêm lean-term → một chiều cộng dồn (28°), chiều kia gần triệt tiêu (1°).
- **Sửa:**
  1. Bỏ hẳn "fold" nghiêng của spine/neck. Spine/neck chỉ track `leanDeg` (đối xứng).
  2. Squat = `f(combined tension)` (giống nhau cả hai chân) → **không** còn lệch do
     chân dài/ngắn khác nhau.
  3. Lean = `(l - r) · leanFromImbalance · leanPolarity` — nguồn Z-lean **duy nhất**.
  4. Chân dựng đối xứng chính xác fore↔aft trong builder (part/anchor mirror đúng).
  5. Spine slack spring 2200, pelvis slack 2800, torso assist 45 (cap 110) → không
     còn runaway.
- Kết quả: `L1/R0 = -39.8°`, `L0/R1 = +41.3°` (lệch 1.5°, ~3.7%). Right side y hệt.

### 4. Biên độ nghiêng

- `leanFromImbalance = 24` → thực tế ~40° khi một dây full, dây kia 0 (trong dải
  mục tiêu 35–45°). Ổn định (vel ≤ 0.02), chân đúng ray, phục hồi về giữa khi trả
  tension đều.

### 5. Hạ thấp tư thế slack

- Squat driven by combined tension: `kneeSquatDeg 100`, `hipSquatDeg 62`,
  `ankleSquatDeg -40` (trục sagittal — mở khoá `angularZ`... thực chất vẫn quay
  quanh world Z vì puppet phẳng trong mặt phẳng X-Y; "sagittal" ở đây = forward/back).
  `legSquatSlackSpring 600`. Pelvis slack → **53%** standing (mục tiêu 50–60%).
  Gối gập rõ, head/torso thấp, coherent, chân vẫn tách.

### 6. Tư thế high

- Taut 1/1: pelvis **99%**, torso ~thẳng (lean +1.2°), chân tách 0.34 m, vel 0.00.
  Chênh HIGH↔LOW: 1.05 m ↔ 0.56 m — rõ ngay.

### Test (Left side, tất cả settle ~4–5 s)

| State | Pelvis | Lean (fwd+) | Foot sep | Vel | Ổn định |
|---|---|---|---|---|---|
| A  L0/R0 | 0.56 m (53%) | +3.4° | 0.29 m | 0.06 | ✅ |
| B  L1/R1 | 1.05 m (99%) | +1.2° | 0.34 m | 0.00 | ✅ |
| C  L1/R0 | 1.00 m | **−39.8°** (backward) | 0.35 m | 0.01 | ✅ |
| D  L0/R1 | 1.00 m | **+41.3°** (forward) | 0.36 m | 0.01 | ✅ |
| E  1/0→0/1→1/1→0/0 | — | chuyển mượt, maxAngVel ≤ 0.9 rad/s | 0.29–0.36 | — | ✅ phục hồi về A |
| F  crouch | leg Z-gap 0.00, knee X-gap 0.57 m, không xuyên | | | | ✅ |
| G  Right mirror | L1/R0 −39.8°, L0/R1 +41.3° (y hệt Left, body-relative) | | | | ✅ |

Console: **0 error / 0 warning** (Play + tất cả state). Health Check Phase 0 vẫn PASSED.

### Giá trị tuning trước → sau

| | Phase 1 | Phase 1.1 |
|---|---|---|
| Cơ chế lean | `leanFromImbalance` + fold spine hardcode | chỉ `(l−r)·gain·polarity`, đối xứng |
| Cơ chế squat | fold per-side (bất đối xứng) | `f(combined)` per-side weight 0.25 |
| leanFromImbalance | 12 (thực tế +28/−1, lệch) | 24 (thực tế ±40, lệch 1.5°) |
| knee/hip fold slack | 52° / 5° (screen-plane splay) | knee 100° / hip 62° (sagittal squat) |
| pelvis slack spring | 340 | 2800 |
| spine slack spring | 230 (+ fold bias) | 2200 (không bias) |
| pelvis @ slack | ~69% | ~53% |
| chân | side-by-side ±0.12, splay/chồng | fore/aft ±0.16, rail xDrive 2600, luôn tách |
| rope pull target | pulley (gần giữa) | forceAnchor (đối xứng, thẳng trên foot) |
| pulley | (±0.42, 2.55) gần giữa | thẳng trên mỗi foot ở 1/3 màn hình bên mình |
| self-collision | tắt hết | chỉ tắt cặp nối khớp; hai chân vẫn va nhau |
| facing | quay vào camera | quay về đối thủ, có Face marker, mirror theo side |

### Còn thử nghiệm / cần người dùng đánh giá

- Forward lean 41° trông khá sâu (torso gần ngang) — đủ hay giảm về ~35°?
- Slack 53% là "split-squat" rộng fore/aft — hình thể chấp nhận được không?
- Lean bias khi slack +3.4° (không hẳn 0) và lệch dư ~1.5° giữa forward/backward.
- Mapping Right rope = forward / Left rope = backward — đúng ý không? (đảo 1 dấu).
- Dây trước vẫn đi sát cạnh đầu (~5 cm) khi đứng — chấp nhận không?
- Tay guard còn mờ; head/cổ rủ hơi sâu ở slack.
- Camera framing bên Right hơi lệch tâm; foot slide range ±0.10 m có thể hơi nhỏ cho footwork sau này.
- Multitouch vẫn chưa test trên thiết bị.

---

## 2026-09-04 — Phase 1: Prototype cơ chế con rối + hai dây

**Mục tiêu:** prototype vật lý đầu tiên kiểm chứng cơ chế cốt lõi — một con rối
khớp nối, hai chân trên ray, hai dây điều khiển nối vào hai chân, hai vùng input
trái/phải, kéo dây căng → dựng cao, chùng → hạ thấp, điều khiển độc lập hai bên.
**Chưa** combat, chưa vũ khí, chưa puppet thứ hai.

### Đã tạo

| File | Vai trò |
|---|---|
| `Assets/_Project/Scenes/PuppetPrototype.unity` | Scene prototype (thêm vào Build Settings, **không** đụng Bootstrap) |
| `Scripts/Runtime/Prototype/PuppetRig.cs` | Holder tham chiếu tới mọi Rigidbody/Joint của con rối |
| `Scripts/Runtime/Prototype/PuppetRopeController.cs` | Biến tension 0→1 mỗi bên thành lực vật lý (toàn bộ tuning ở đây) |
| `Scripts/Runtime/Prototype/PuppetRopeInput.cs` | Đọc Input System → hai giá trị tension chuẩn hoá |
| `Scripts/Runtime/Prototype/RopeVisual.cs` | LineRenderer vẽ dây, căng = thẳng/sáng, chùng = võng/mờ |
| `Scripts/Runtime/Prototype/PuppetDebugHUD.cs` | IMGUI: Left/Right Tension, pelvis height, torso upright, vùng input |
| `Scripts/Editor/PuppetPrototypeBuilder.cs` | Dựng lại toàn bộ scene từ code (`Puppet Master ▸ Phase 1 ▸ Build Puppet Prototype Scene`) — re-runnable |

Scene được **sinh hoàn toàn bằng code** trong builder — chỉnh rig = sửa builder rồi
chạy lại menu, không dựng tay. Materials prototype trong `Assets/_Project/Materials/Prototype/`.

### Physics architecture

- **13 Rigidbody**: Pelvis, Torso, Head, Upper/LowerArm ×2, Upper/LowerLeg ×2, Foot ×2.
  Primitive (cube/capsule/sphere) làm mesh; collider set thủ công (transform part
  giữ scale = 1 để anchor joint sạch, mesh con mới scale).
  Mass: pelvis 10, torso 13, head 2.6, upperArm 1.6, lowerArm 1.0, upperLeg 6.5,
  lowerLeg 4.5, **foot 4.0** (chân nặng cho gốc đứng ổn định).
  `solverIterations 40 / velocity 20` mỗi body; `Physics.defaultSolverIterations`
  nâng lên 24 lúc runtime (không đụng Physics project settings).
  Va chạm giữa các bộ phận của rối bị tắt hết ở `PuppetRopeController.Awake()`.

- **Joint type: `ConfigurableJoint` toàn bộ.** Rig là **phẳng (2.5D)** — trục quay
  tự do duy nhất là `angularX` = quay quanh world Z (mặt phẳng màn hình). `angularY`
  (yaw) và `angularZ` (chúi vào/ra màn hình) **khoá cứng** ở mọi khớp → không lật
  ngang, không xoắn. Vị trí (x/y/z) khoá cứng ở mọi khớp nối bộ phận.
  Giới hạn `angularX`: spine −40/50°, neck −40/45°, vai ±120°, khuỷu −10/135°,
  hông ±85°, gối ±100°, cổ chân ±45°.

- **Cơ chế "dựng"** đến từ **slerp drive** của các khớp, spring lerp theo tension:
  - `hip`, `knee`, `ankle` — mỗi bên theo tension bên đó. Slack: spring thấp +
    target "gập" (hông 5°, gối 52°, cổ chân 12° — **mirror** trái/phải để rối
    sụp thẳng xuống). Taut: spring cao (hip/knee ~2600) + target = identity (thẳng).
  - `spine`, `neck` — theo tension trung bình.
  - **`pelvisPlaneJoint` (pelvis → world)** là lực "đứng" chính: slerp drive
    spring ramp 340 → 4200 theo tension trung bình, target = thẳng đứng + lean.
    Đây là "controlled stabilization" mà PROJECT_MASTER §5 cho phép — khi thả dây
    (slack) spring gần như tắt và rối tự sụp.
  - Một `AddTorque` nhỏ (assist 25, cap 60) giữ torso không trễ so với pelvis.

### Ray ở chân

- `pelvisPlaneJoint`: pelvis khoá **z** (độ sâu) → cả rối nằm trong mặt phẳng màn
  hình. x/y tự do (rối nhấc lên/hạ xuống, dịch ngang được).
- Mỗi chân: `ConfigurableJoint` foot → **world** (`connectedBody = null`):
  - `xMotion Limited ±0.10 m` (spring 1200) — trượt tới/lui trong phạm vi nhỏ trên ray.
  - `yMotion / zMotion Locked` — chân **không rời ray**.
  - Toàn bộ angular **Locked** — bàn chân bắt vít phẳng vào ray; khớp cổ chân phía
    trên mới cho cẳng chân xoay trong mặt phẳng màn hình.
  - `xDrive` spring 1600 kéo chân về X gốc (±0.12 m) → hai chân giữ khoảng cách,
    không dạt ra/chéo nhau.

### Dây điều khiển & logic căng/chùng

- Hai `Pulley` (điểm neo trên cao, `(±0.42, 2.55, 0)`). Mỗi dây = `LineRenderer`
  từ pulley xuống `RopeAttach` (đỉnh sau bàn chân tương ứng). **Không** nối vào
  vai/lưng/đầu.
- `RopeVisual`: căng → 2 điểm gần thẳng, dày, vàng sáng; chùng → 16 điểm võng
  parabol, mảnh, xám. Sag ∝ (1 − tension).
- **Lực gameplay của dây tác động ĐÚNG vào bàn chân**: `PuppetRopeController.RopePull`
  gọi `foot.AddForceAtPosition(hướng-về-pulley · ropePull · tension, ropeAttach)`.
  `ropePull = 45`. Phần lớn lực đứng bị ray hấp thụ; giá trị **tension** mới là
  thứ điều khiển spring các khớp ở trên.
- **Hai căng** → pelvis drive cứng + chân thẳng → đứng cao (pelvis ~1.05 m ≈ 99% chuẩn, torso upright ≈ 1.00).
- **Hai chùng** → drive nhão → rối gập gối, hạ trọng tâm (pelvis ~0.73 m ≈ 69%, torso ≈ 0.69) — **coherent crouch, không phải đống ragdoll**.
- **Một căng một chùng** → chân bên căng thẳng, bên chùng gập; pelvis nghiêng,
  torso lean về phía dây căng (~20–28°). Ổn định.
- Chuyển trạng thái mượt (tension smoothing 14/s) — không rung vô hạn, không joint
  explosion, không chân thoát ray (đã test snap qua lại nhiều lần, maxAngVel < 1 rad/s).

### Input

- `com.unity.inputsystem` sẵn có. Hai vùng: nửa trái / nửa phải màn hình.
- **Mobile:** `EnhancedTouch` — mỗi ngón điều khiển vùng nó chạm xuống đầu tiên;
  **kéo XUỐNG** từ điểm chạm = kéo dây căng (`dragFullPixels = 230`). Hai ngón độc lập.
- **Desktop test:** chuột trái kéo trong một vùng (một vùng/lần); bàn phím
  `A` = dây trái, `L` = dây phải, `Space` = cả hai (cho phép test cả hai tay).
- Mỗi bên có giá trị 0.00 (chùng) … 1.00 (căng), rise 3.6/s, fall 2.3/s
  (`pull / hold / release`). Không phải joystick combat.
- Đã test qua MCP: bơm sự kiện bàn phím A/L/Space → HUD/tension/tư thế phản ứng
  đúng; thả phím → về crouch. **Multitouch: code chuẩn EnhancedTouch, cần xác nhận
  trên thiết bị thật.**

### Giá trị tuning chính hiện tại (BASELINE — chưa chốt)

| Nhóm | Slack | Stand |
|---|---|---|
| pelvis→world spring / damper | 340 / 42 | 4200 / 260 |
| hip+knee spring / damper | 150 / 18 | 2600 / 150 |
| ankle spring | 60 | 700 |
| spine+neck spring / damper | 230 / 20 | 2600 / 150 |
| fold target | hip 5° · knee 52° · ankle 12° · spine 8° · neck 10° | identity |

`tensionGamma 1.3` · `tensionSmoothing 14` · `leanFromImbalance 12°` ·
`ropePull 45` · `torsoUprightAssist 25 (cap 60)`.
Bật `PuppetRopeController.debugOverrideInput` để ép tension test không cần input.

### Thay đổi project settings

- `EditorBuildSettings`: thêm `PuppetPrototype.unity` (Bootstrap giữ nguyên).
- `PlayerSettings.runInBackground = 0 → 1` — để Play Mode chạy khi Editor không
  focus (cần cho vòng lặp test qua MCP). Diff `ProjectSettings.asset` lớn là do
  Unity 6.5 tự migrate `serializedVersion 24 → 29` khi đụng PlayerSettings — vô
  hại. **Cân nhắc lại `runInBackground` trước khi release mobile** (game đối kháng
  thường muốn pause khi vào nền).
- `ProjectSettings/SceneTemplateSettings.json` — Unity tự tạo khi `NewScene`.

### Còn thử nghiệm / cần người chơi đánh giá

- Toàn bộ số tuning ở trên là baseline để **chơi thử rồi chỉnh**, chưa đưa vào PROJECT_MASTER.
- Rig phẳng 2.5D: gập gối là "buckle" trong mặt phẳng màn hình (kiểu marionette),
  **không** phải squat 3D thật. Cần xác nhận cảm giác này ổn cho hướng combat sau.
- `pelvisPlaneJoint` gánh phần lớn việc giữ thăng bằng — có "assist" hơi nhiều;
  người chơi cần đánh giá xem có còn cảm giác "vật lý" đủ hay quá cứng.
- Lean khi lệch tension (~20–28°) — đủ hay quá nhiều?
- Slack: đầu/cổ rủ khá sâu; feet vẫn dạt nhẹ ±0.03–0.04 m.
- Bàn phím/chuột đã test; **multitouch chưa test trên thiết bị**.
- HUD panel che nhẹ tay trái con rối khi đứng.

---

## 2026-09-04 — Sửa Unity MCP (hoàn tất, không phải Phase 1)

**Vấn đề:** Claude Desktop báo MCP `unity` = *Failed / Server disconnected*;
`tools/list` rỗng dù Unity Editor đang mở.

**Nguyên nhân (2 lỗi độc lập):**
1. **Thiếu Editor bridge.** Unity CLI (`unity mcp`, `unity status`, `unity list`)
   nói chuyện với Editor qua package chính thức **`com.unity.pipeline`**. Project
   chưa có package này → `hasPipelinePackage: false`, `pipelineServer.isReachable: false`
   → `unity mcp` expose **0 tool**.
2. **Đường dẫn lệnh tương đối.** Cả 2 file config MCP dùng `command: "unity"`. App GUI
   (Claude Desktop / Claude Code) không kế thừa `PATH` của shell nên không tìm thấy
   `unity` (nằm ở `~/.unity/bin`) → server không khởi động được. (`godot` chạy được
   vì nó dùng đường dẫn tuyệt đối.)

**Đã sửa:**
- `unity pipeline install --project-path <project>` → thêm `com.unity.pipeline@0.6.0-exp.1`
  vào `Packages/manifest.json` (đây là bridge **chính thức** của Unity CLI, không phải
  MCP bên thứ ba).
- Khởi động lại Unity Editor để nó resolve + compile package. Pipeline server chạy tại
  `http://127.0.0.1:7800`, `unity status` → `state: ready`, `unity list` → **149 tool**.
- Claude Desktop: `unity mcp configure claude --project-path <project> --yes` (lệnh
  chính thức) → sửa `command` thành đường dẫn tuyệt đối `~/.unity/bin/unity` và ghim
  `--project-path`. Backup: `claude_desktop_config.json.bak-phase0-*`.
- Claude Code (`~/.claude.json`, không có `claude` CLI nên sửa JSON có backup): entry
  `unity-editor-mcp` → `command: /Users/maccuatao/.unity/bin/unity`,
  `args: ["mcp","--project-path","<project>"]`. Backup: `~/.claude.json.bak-phase0-*`.
- Mở project trong Editor 6.5 làm URP tự migrate `Mobile/PC_RPAsset.asset`
  (`k_AssetVersion 12→13`), `URPProjectSettings.asset`, `PackageManagerSettings.asset`
  sang định dạng mới — vô hại, đã commit.

**Kiểm chứng (qua MCP, chỉ thao tác đọc):**
- `initialize` → `unity-mcp 1.0.0-beta.6`, protocol `2025-06-18`
- `tools/list` → **149 tool**
- `get_scene_hierarchy` → đọc đúng scene `Bootstrap` (Main Camera / Directional Light / Global Volume)
- `list_open_scenes`, `get_build_settings`, `get_console_logs` → OK, **0 error** trong console
- Không thay đổi gameplay.

**Cần khởi động lại:**
- **Claude Desktop:** phải thoát & mở lại để nạp config mới.
- **Claude Code:** phiên hiện tại không thấy tool `unity` (MCP nạp lúc mở phiên) —
  mở **phiên mới** khi Editor đang chạy project này.
- **Unity Editor:** *không* cần restart nữa; giữ mở để MCP hoạt động (pid hiện tại 9760).

---

## 2026-09-04 — Phase 0: Khởi tạo nền móng kỹ thuật

**Mục tiêu:** Thiết lập project Unity sạch, chuyên nghiệp, sẵn sàng phát triển
cho iOS + Android. **Không** phát triển gameplay.

### Môi trường (đã kiểm tra)

| Thành phần | Trạng thái |
|---|---|
| macOS | 26.5.2, Apple Silicon (arm64) |
| Git | 2.50.1 |
| GitHub | Xác thực qua `gh` (tài khoản `Tom-deptrai`), có quyền push (HTTPS) |
| Unity Editor | `6000.5.10f1` (Unity 6.5) — có sẵn iOS + Android Build Support |
| Unity CLI | `unity` `1.0.0-beta.6` (`/Users/<user>/.unity/bin/unity`) |
| Unity MCP | Server `unity mcp` chạy được (stdio); cần Unity Editor mở + khởi động lại phiên MCP để lộ tool |
| Xcode | 26.6 (build 17F113), `xcode-select` trỏ `/Applications/Xcode.app` |
| iOS runtimes | 18.4, 26.5 (simulator) |
| Android SDK/NDK | Có, bundle theo Unity (NDK r27c, build-tools 36, platforms 34/36/37) + `~/Library/Android/sdk` |
| JDK | Unity OpenJDK 17 (bundled) + Homebrew OpenJDK 23 (hệ thống) |
| Blender | **Chưa cài** (chưa cần cho Phase 0) |
| Git LFS | Đã cài (`git-lfs` 3.8.0 qua Homebrew), bật ở mức repo (`--local`) |

### Tạo project

- Nền tảng: bung template Unity **"3D (URP)"** (`com.unity.template.3d-cross-platform` 17.0.14)
  đi kèm Editor, làm gốc project — có sẵn cấu hình URP cho cả PC và Mobile.
- Đặt project ngay tại gốc repo (`Assets/`, `Packages/`, `ProjectSettings/`).
- `ProjectSettings/ProjectVersion.txt` ghim `6000.5.10f1 (3bd4f66ad299)`.

### Package đang dùng (`Packages/manifest.json`)

Giữ tối thiểu — chỉ những gì Phase 0 thật sự cần:

| Package | Vai trò |
|---|---|
| `com.unity.render-pipelines.universal` | URP (render pipeline chính) |
| `com.unity.inputsystem` | Input System mới — multitouch |
| `com.unity.ugui` | UI |
| `com.unity.test-framework` | Nền tảng test / CI sau này |
| `com.unity.ide.rider`, `com.unity.ide.visualstudio` | Sinh project cho IDE |

Đã **gỡ** khỏi template: `com.unity.ai.navigation` (không dùng NavMesh cho lối
chơi trên ray), `com.unity.collab-proxy` (dùng Git), `com.unity.timeline`,
`com.unity.visualscripting` (chưa cần). Các built-in module do Unity tự quản lý.

**Không** thêm: networking, Firebase/backend, ads/IAP, analytics — theo đúng phạm vi.

### Cấu hình nền tảng

- **Hướng màn hình:** AutoRotation nhưng chỉ cho phép Landscape trái/phải
  (`allowedAutorotateToPortrait = 0`, `...PortraitUpsideDown = 0`).
- **60 FPS:** `Assets/_Project/Scripts/Runtime/AppBootstrap.cs` đặt
  `Application.targetFrameRate = 60` và `QualitySettings.vSyncCount = 0` khi khởi
  động (`RuntimeInitializeOnLoadMethod`). Đây là hạ tầng ứng dụng, không phải gameplay.
- **Input:** Active Input Handling = *Input System Package (New)* (`activeInputHandler = 1`),
  Input System `1.20.0`. Giữ asset `InputSystem_Actions.inputactions` mặc định của
  template làm điểm khởi đầu, đặt trong `Assets/_Project/Settings/Input/`.
- **URP `17.5.0`:** 2 quality level *Mobile* / *PC*, mỗi level có URP Asset riêng
  (Mobile + PC Renderer). Editor mặc định xem ở level *Mobile* (`m_CurrentQuality = 0`).
  Đồ hoạ: iOS = Metal (auto); Android = Vulkan → GLES3; nén texture Android = ASTC;
  multithreaded rendering bật cho cả hai.
- **iOS:** bundle id `com.puppetmaster.game`, iOS target tối thiểu `15.0`
  (mức sàn Unity 6.5 tự nâng lên — đặt `13.0` sẽ bị kẹp về `15.0`), universal
  (iPhone + iPad), fullscreen, ẩn status bar, IL2CPP (bắt buộc).
- **Android:** bundle id `com.puppetmaster.game`, min SDK **26** (Android 8.0 — mức
  sàn Unity 6.5 tự nâng lên), target SDK auto, scripting backend **IL2CPP**,
  kiến trúc **ARM64**.
- **Physics:** giữ mặc định hợp lý của Unity (gravity −9.81, solver iterations 6,
  fixed timestep 0.02, reuse collision callbacks bật). **Chưa** tinh chỉnh cho
  gameplay — việc tuning khớp/solver/timestep/layer để lại cho phase "Puppet feels good".
- **Serialization:** Force Text + Visible Meta Files (chuẩn cho Git).

> ⚠️ `com.puppetmaster.game`, company name "Puppet Master", iOS/Android SDK là
> **giá trị tạm** hợp lý cho giai đoạn chuẩn bị. Chốt lại trước khi đăng ký App
> Store / Google Play.

### Cấu trúc project

```
Assets/_Project/
  Art/  Audio/  Materials/  Models/  Prefabs/  UI/  VFX/  Physics/   (rỗng, có .gitkeep)
  Scenes/Bootstrap.unity          scene khởi động tối thiểu (camera + light + URP volume)
  Scripts/Runtime/                PuppetMaster.Runtime.asmdef  + AppBootstrap.cs
  Scripts/Editor/                 PuppetMaster.Editor.asmdef
  Settings/                       URP asset (Mobile/PC RP + Renderer), volume profile
  Settings/Input/                 InputSystem_Actions.inputactions
```

Đã xoá cruft của template: `Assets/TutorialInfo/`, `Assets/Readme.asset`.
Code/asset dự án nằm gọn trong `Assets/_Project/`, tách khỏi package & sample.

### Git

- `.gitignore` chuẩn Unity (Library/, Temp/, Logs/, obj/, Build(s)/, UserSettings/,
  `*.csproj`, `*.sln`, `*.slnx`, `.vscode/`, `.idea/`, `.DS_Store`,
  `*.apk/*.aab/*.ipa` …). Đã xác nhận `Library/`, `Temp/`, `Logs/` không bị commit.
- `.gitattributes`: ép text + smart-merge (`unityyamlmerge`) cho YAML của Unity;
  **các pattern Git LFS đã bật sẵn** cho model/texture/audio/video/font/native lib —
  hiện chưa khớp file nào (Phase 0 không có nhị phân lớn). Khi thêm asset lớn đầu tiên
  thì đã hoạt động ngay.
- Git LFS bật ở mức repo: `git lfs install --local`. Merge driver `unityyamlmerge`
  trỏ tới `.../Unity.app/Contents/Helpers/UnityYAMLMerge` (config local `.git/config`).
- Commit Phase 0 nằm trên commit `60e4d0b` (bản `Docs/PROJECT_MASTER.md` do chủ dự án thêm).

### Kiểm thử (2026-09-04)

| Kiểm tra | Kết quả |
|---|---|
| Mở project bằng Unity (batch + GUI) | ✅ mở được, Editor GUI đang chạy |
| Compile | ✅ **0 error, 0 warning** (assembly `PuppetMaster.Runtime` build OK) |
| Resolve package | ✅ 27 package, `packages-lock.json` sinh lại sạch |
| Scene `Bootstrap.unity` | ✅ mở được (3 root: Main Camera, Directional Light, Global Volume), có trong Build Settings |
| Health check (`Puppet Master ▸ Phase 0 ▸ Health Check`) | ✅ **PASSED** toàn bộ mục |
| Switch build target → **iOS** | ✅ `Targeting platform: iOS`, iOS module nạp OK, 0 error |
| Switch build target → **Android** | ✅ `Targeting platform: Android`, Android module nạp OK, 0 error |
| Unity MCP đọc project | ⚠️ **một phần** — xem bên dưới |

**Unity MCP:** server `unity mcp` (Unity CLI `1.0.0-beta.6`) chạy và trả lời đúng
giao thức MCP (`initialize` → `serverInfo: unity-mcp`), nhưng `tools/list` trả về
**rỗng** kể cả khi Editor đang mở. Cầu nối phía Editor chưa hoạt động. Chưa cài thêm
package nào để xử lý (theo quy tắc không cài package thừa). Cần: (a) một phiên trợ lý
mới khởi động **khi Editor đang mở project này**, và có thể (b) bật tính năng MCP
trong Unity (Preferences/Project Settings) hoặc chờ bản CLI ổn định hơn.

### Còn mở / chưa chắc

- `com.puppetmaster.game`, company name "Puppet Master": giá trị tạm — chốt trước khi
  đăng ký store.
- Unity MCP chưa lộ tool (xem trên).
- Tuning physics cho puppet (solver iterations, fixed timestep, layer matrix, joint):
  để phase "Puppet feels good".
- Curl error tới `*.cloud.unity3d.com` trong log batch = telemetry/licensing offline,
  không ảnh hưởng project.

---

## 2026-09-04 — Fix inward / outward puppet lean

### Vấn đề đã xử lý
- **Nguyên nhân trước đây:**
  1. Phép nhân quaternion `fbRot * depthRot` (`Rz * Rx`) trong `PuppetRopeController` sinh ra yaw góc Y ký sinh (~20°) khi kết hợp forward/back với depth. Do `angularYMotion = Locked` trên mọi joint, solver vật lý liên tục triệt tiêu và xung đột với drive, làm suy giảm chuyển động depth.
  2. Tương tự trong `DriveLeg`, phép nhân `AngleAxis(squat, Z) * depthHip` sinh ra góc xoay Y ký sinh trên khớp háng (hip) vốn đã khoá Y.
  3. Giới hạn góc và tỉ lệ follow của spine/neck/hip cho depth trước đó quá thấp (`depthGain = 22f`, `spineFollow = 0.26f`), khiến pelvis, torso và head không uốn đủ biên độ nhìn thấy rõ.

### Thay đổi đã thực hiện
- `PuppetRopeController.cs`:
  - Đổi cách dựng `leanWorld` sang `Quaternion.Euler(-depthDeg, 0f, -_facingSign * fbDeg)` (tương đương `depthRot * fbRot` / `Rx * Rz`), đảm bảo góc yaw quanh trục Y luôn tuyệt đối = 0.00°, loại bỏ hoàn toàn xung đột với constraint `angularYMotion = Locked`.
  - Nâng `depthGain = 28f`, `spineFollow = 0.32f`, `neckFollow = 0.20f`, `depthHipFollow = 0.35f`.
  - Cập nhật `DriveLeg` dùng Euler góc thuần Z và X không chứa thành phần Y ký sinh.
- `PuppetPrototypeBuilder.cs`:
  - Nâng giới hạn góc depth (`angularXLimit`) trên `pelvisPlaneJoint`, `spine`, `neck` lên `±45°` và `hip` lên `±40°`.
- **Phím test trên Mac:** `Q` = INWARD (nghiêng vào trong), `E` = OUTWARD (nghiêng ra ngoài), kết hợp với `A` (lùi), `L` (tiến), `Space` (căng cả 2 dây).
- Đã test và xác nhận trong Play Mode qua Unity MCP: INWARD (~28°), OUTWARD (~28°), Forward (~67°), Backward (~64°), kết hợp diagonal đều hoạt động mượt mà, không yaw ký sinh, giữ chặt chân trên ray.

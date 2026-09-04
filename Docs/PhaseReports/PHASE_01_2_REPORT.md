# Phase 1.2 Report — Advanced four-axis puppet control

**Ngày:** 2026-09-04 · **Commit:** xem cuối · **Scene:** `Assets/_Project/Scenes/PuppetPrototype.unity`

Hoàn thiện hệ điều khiển lõi của puppet **trước khi thêm vũ khí**. Không combat, không
vũ khí, không sang Phase 2. Giữ nguyên các phần Phase 1.1 đang đúng (facing đối thủ,
Left/Right rope theo giải phẫu, feet separated, rail constraint, forward/backward
semantic, side mirror architecture).

---

## 1. Kiến trúc mới

### 1.1 Convention trục joint

Mọi `ConfigurableJoint` **được điều khiển** giờ dựng với:

```
axis          = (1, 0, 0)
secondaryAxis = (0, 1, 0)
```

→ "joint space" = identity → controller chỉ cần
`joint.targetRotation = Quaternion.Inverse(worldTargetRotation)` (đã chuẩn hoá + guard).
Không còn phép conjugation `Inverse(space)·…·space` khó suy luận của Phase 1.x.

Ba trục góc, ý nghĩa cố định:

| Trục joint | Trục world | Vai trò | Trạng thái |
|---|---|---|---|
| `angularZ` | world **Z** | mặt phẳng chiến đấu: **forward/back lean + squat** | Limited |
| `angularX` | world **X** | **inward/outward** (depth lean) | Limited theo joint (0 = khoá) |
| `angularY` | world **Y** | **yaw** | **KHOÁ CỨNG mọi joint** |

### 1.2 Scripts

| File | Vai trò |
|---|---|
| `PlayerSide.cs` | enum + `.Sign()` (−1 Left / +1 Right) |
| `PuppetRig.cs` | holder tham chiếu; thêm `facingSign`, `Leg.railSlot/belowRail/forceAnchor`, `standingHeadHeight` |
| `PuppetRopeInput.cs` | Input System → `SetInput(left, right, depth)` |
| `PuppetRopeController.cs` | tension → squat + 2-axis lean; drive joint 1 lần/FixedUpdate; probe response-time |
| `RopeVisual.cs` | LineRenderer: foot → slot → below → thumb region |
| `PuppetDebugHUD.cs` | HUD 10 dòng |
| `PuppetPrototypeBuilder.cs` (Editor) | `Build(PlayerSide)` + 2 menu (Left / Right mirror) |

Controller vẫn là **một** MonoBehaviour (để mọi joint target set một chỗ, không 2
script tranh nhau), nhưng chia rõ theo method: `DriveLeg`, `TorsoAssist`, `RopePull`,
`SetTargetWorld`, `SetDrive`.

---

## 2. Control mapping

Hai ngón, hai dây — không joystick, không nút.

| Input | Kết quả |
|---|---|
| **Kéo DỌC** ngón trong zone của nó | tension dây bên đó (0..1) |
| **Kéo NGANG**, lấy trung bình 2 ngón | depth axis (−1..1) |
| Desktop `A` / `L` | Left / Right rope = 1 |
| Desktop `Space` | cả hai = 1 |
| Desktop `Q` / `E` | inward (−1) / outward (+1) |

- Zone: nửa trái màn hình = Left rope = **Foot_L**; nửa phải = Right rope = **Foot_R**.
  Mapping **theo cơ thể puppet**, không đổi theo PlayerSide.
- EnhancedTouch: mỗi ngón "sở hữu" zone từ lúc touch-start (finger crossing không đổi
  chủ). Ngang có deadzone 16 px, full 170 px, clamp.

---

## 3. Cách tính

### 3.1 Tension

```
LeftTension  = smooth(LeftInput,  k = 1 − e^(−45·dt))     // rất nhanh
RightTension = smooth(RightInput, k)
l = pow(LeftTension,  1.2)      // shaping gamma
r = pow(RightTension, 1.2)
combined = 0.5·(l + r)          // -> squat
```

### 3.2 Forward / Backward

```
fbRaw  = (r − l) · forwardBackGain(44) · forwardBackSign(+1)   // + = FORWARD (toward opponent)
fbDeg  = fbRaw < 0 ? fbRaw · backwardBoost(1.06) : fbRaw       // đối xứng hoá backward
fbRot  = AngleAxis(−facingSign · fbDeg, worldZ)
```

- `facingSign` = +1 (Left, faces +X) / −1 (Right, faces −X).
- `(r − l)` > 0 ⇔ Right rope căng hơn ⇔ FORWARD. Left rope căng ⇔ BACKWARD.
  (Đảo `forwardBackSign` để hoán.)
- **Đọc lại (side-independent):** `ForwardLeanDeg = asin(clamp(torso.up.x · facingSign))`
  — dương = nghiêng về đối thủ.

### 3.3 Inward / Outward (depth)

```
DepthValue = smooth(DepthInput, k = 1 − e^(−30·dt))
depthDeg   = DepthValue · depthGain(22)        // + = OUTWARD
depthRot   = AngleAxis(−depthDeg, worldX)
```

- Định nghĩa: **Outward** = nghiêng về phía camera (world −Z). **Inward** = ra xa
  camera (world +Z). Screen-consistent cho **cả hai** side (depth định nghĩa trong
  không gian camera, không phải opponent-relative). Input "cả hai ngón kéo phải" →
  outward; "cả hai kéo trái" → inward.
- **Đọc lại:** `DepthLeanDeg = asin(clamp(−torso.up.z))` — dương = outward.

### 3.4 Combine diagonal

```
leanWorld = fbRot · depthRot          // hai phép quay 1-trục world, thứ tự: depth trước rồi fwd/back
pelvisPlaneJoint  <- leanWorld
spine             <- Slerp(identity, leanWorld, 0.26)
neck              <- Slerp(identity, leanWorld, 0.15)
hip/knee/ankle    <- AngleAxis(squat·°, worldZ) · AngleAxis(−depthDeg·follow, worldX)
```

- `fbRot` quanh **world Z**, `depthRot` quanh **world X** — **không phép quay nào
  quanh world Y** → **không yaw ký sinh** (đo được ≤ 1.6° kể cả diagonal extreme).
- Khi cả hai axis ≠ 0, `leanWorld` là một orientation kết hợp thật, không phải
  "apply forward rồi overwrite depth".
- **Đánh đổi:** khi forward lean lớn (~68°), up-vector gần trục X → depth bị "ép"
  nhỏ lại (~±13° thay vì ±28° khi đứng thẳng). Hợp lý vật lý (không thể nghiêng
  ngang nhiều khi đã gập người 68°) nhưng cảm giác depth ở diagonal yếu hơn.

---

## 4. Joint / axis mở & khoá

`Bend(child, parent, anchor, fight, depth, driven)` — `fight` = limit `angularZ`,
`depth` = limit `angularX` (0 ⇒ Locked). `angularY` **luôn** Locked.

| Joint | angularZ (fight) | angularX (depth) | angularY (yaw) | driven |
|---|---|---|---|---|
| pelvis → world | ±78° | ±40° | **LOCK** | ✔ (leanWorld) |
| spine | ±75° | ±42° | **LOCK** | ✔ |
| neck | ±55° | ±42° | **LOCK** | ✔ |
| hip L/R | ±140° | ±36° | **LOCK** | ✔ (squat + depth follow) |
| knee L/R | ±150° | **0 (LOCK)** | **LOCK** | ✔ (squat) |
| ankle L/R | ±60° | ±12° | **LOCK** | ✔ (squat · 0.35) |
| shoulder L/R | ±120° | ±70° | **LOCK** | passive |
| elbow L/R | ±130° | ±25° | **LOCK** | passive |
| foot → world (rail) | LOCK | LOCK | LOCK | X-slide ±0.10 m, xDrive về home |
| pelvis → world (pos) | zMotion **LOCK** (giữ mặt phẳng chiến đấu); x/y Free | | | |

---

## 5. Ngăn full-3D ragdoll (CONTROLLED 2.5D+)

1. **Yaw (`angularY`) khoá cứng ở TẤT CẢ joint** → puppet không thể quay lưng
   khỏi đối thủ. Đo: `pelvis.eulerAngles.y` ≤ ~2° ở mọi state kể cả diagonal.
2. **Depth chỉ mở giới hạn ở nửa trên** (pelvis/spine/neck/hip). **Gối và bàn chân
   khoá depth** → cẳng chân luôn phẳng (`lowerLeg.z ≈ 0` đo được ở mọi state).
3. **Pelvis khoá z-position** → toàn thân ở mặt phẳng chiến đấu, depth chỉ là *lean*
   (nghiêng), không phải *dịch chuyển* vào chiều sâu.
4. **Bàn chân bắt vít phẳng vào ray** (mọi angular khoá world), chỉ trượt X ±0.10 m.
5. **Lean = tích 2 phép quay 1-trục world (Z rồi X)** — không bao giờ tạo phép quay
   quanh Y → không twist/spin.
6. Depth là **1 DOF có kiểm soát** (drive + limit), không unlock hàng loạt.

---

## 6. Rope visual layout mới

- **Không còn dây lên trời.** Phía trên puppet hoàn toàn sạch.
- Đường đi: `Foot → RailSlot (ngay ở ray, có cục "slot guide") → BelowRail (ra phía
  người xem, cao độ sàn) → điểm thumb (xuống + về phía camera + xoè trái/phải)`.
- Ground opaque nên phần "dưới sàn" bị che → dây đọc như **đi ra phía người chơi**
  (không xuyên sàn giả tạo) — cùng ý nghĩa "hai ngón kéo dây từ bên dưới".
- Căng → thẳng / vàng sáng / dày (0.03). Chùng → võng parabol / xám mờ / mảnh (0.016).
- **Tách hoàn toàn khỏi lực gameplay.** `RopeVisual` chỉ vẽ. Lực: `PuppetRopeController.
  RopePull` kéo bàn chân **xuống** về `ForceAnchor` (dưới ray) ∝ tension — "cắm" chân
  vào ray.
- **Foot_R giờ là chân DẪN** (Foot_L = chân sau) để mỗi dây nằm đúng nửa màn hình
  của nó → **không chéo nhau** dưới ray. `Left rope = Foot_L` giữ nguyên.
- Mirror: file có `thumbFanX` (Left xoè một bên, Right bên kia) — dựa `isLeft`, không
  hardcode side.

*(Rope visual đọc được nhưng đoạn dưới ray còn ngắn — điểm cần đánh giá.)*

---

## 7. Response tuning trước → sau

| Tham số | Phase 1.1 | Phase 1.2 |
|---|---|---|
| `tensionSmoothing` | 14 | **45** |
| input rise / fall | 3.6 / 2.3 | **12 / 10** |
| depth smoothing | — | 30 (ctrl) + 9 (input) |
| leg drive spring slack → stand | 240 → 2800 | **2600 → 7200** |
| leg drive damper | 120 | **340** (ankle × 0.35) |
| pelvis→world spring slack → stand | 2800 → 4600 | **5200 → 10500** |
| pelvis damper | 260 | **540** |
| spine spring slack → stand | 2200 → 3000 | **4200 → 7200** |
| spine damper | 150 | **340** |
| part `maxAngularVelocity` | 12 | **28** |
| per-body solver iters | 44 / 22 | **48 / 24** |
| torso AddTorque assist / damp | 45 / 8 | **38 / 12** (nhẹ hơn, damp hơn) |

**Đo bằng probe nội bộ** (`PuppetRopeController.BeginProbe()` rồi step input, đọc
`ProbeReport(finalAngle, …)` — 240 sample @ FixedUpdate):

| | Mục tiêu | Đo được |
|---|---|---|
| Phản ứng thấy được (>2°) | ~0.10 s | **0.022 – 0.033 s** |
| Đạt ~80% tư thế lean | 0.25 – 0.40 s | **0.21 – 0.25 s** |

→ Nhanh hơn Phase 1.1 rõ rệt (ước lượng > 4×), vẫn giữ inertia (không snap tức thì).

---

## 8. Crouch tuning

**Bug tìm ra:** Phase 1.x drive hip **và** knee cùng dấu `+` quanh world Z → chân
"cuộn" như lò xo thay vì gập ép; knee kẹt ~60° dù target 138°; pelvis không xuống
dưới ~58% **và jitter** (`vel` slack 0.15–0.24 — limit-cycle của 2 drive tự đánh nhau).

**Sửa:** `kneeSquatDeg = −118` (dấu **ngược** hip `+62`). Chân gập ép thật, gravity
hỗ trợ:

| | Phase 1.1 | Phase 1.2 |
|---|---|---|
| pelvis @ slack | ~53% | **~47–54%** (Left build 54%, Right 47%) |
| slack `vel` (jitter) | 0.15–0.24 | **0.04–0.09** |
| kinematics | — | hip +62° / knee −118° / ankle +55° → bàn chân tổng ≈ 0 (phẳng) |
| `ankleSpringScale` | (ankle = knee) | **0.35** (ankle theo, không chống) |

*(Chưa đạt 40–45% — dáng "split-squat" rộng fore/aft ở ~50%. Ưu tiên ổn định.)*

---

## 9. Lean angle thực tế

| Axis | Mục tiêu | Đo (Left) | Đối xứng |
|---|---|---|---|
| Forward | 65–70° | **+67.3°** | vs backward **−68.8°** → lệch **1.5° (~2%)** ✅ |
| Backward | 65–70° | **−68.8°** | |
| Inward | ±25–35° | **−28.1°** | vs outward **+28.0°** → lệch 0.1° ✅ |
| Outward | ±25–35° | **+28.0°** | |
| Diagonal (fwd+out) | — | fwd +68.3°, depth +12.9°, mag 72.9°, yaw −0.2° | |

---

## 10. Side mirror result

| | PlayerSide.Left | PlayerSide.Right |
|---|---|---|
| Vị trí | X ≈ −1.0 | X ≈ +1.0 |
| Facing | +X (đối thủ bên phải) | −X (đối thủ bên trái) |
| Foot_R (dẫn) | +X | −X |
| Rope rig | phía −X (sau lưng) | phía +X (sau lưng) |
| **Left rope căng** | −39.8° … thực ra: Right rope căng → **+66.6° FORWARD** | Right rope căng → **+66.6° FORWARD** (giống hệt, body-relative) |
| **+depth input** | OUTWARD (torso.up.z < 0) | OUTWARD (torso.up.z < 0) — screen-consistent |
| yaw | ≤ 0.7° | ≤ 0.7° |

→ Forward/backward semantic **không đảo sai** khi mirror. Inward/outward định nghĩa
trong không gian camera (screen-consistent), báo cáo rõ ràng. Input nhất quán.

---

## 11. 9-state test result (Left side)

| # | State | fwd° | depth° | mag° | pelvis% | yaw° | sep m | vel | Ổn định |
|---|---|---|---|---|---|---|---|---|---|
| A | neutral (½/½/0) | +3 | +1.5 | 3 | 62 | −0.1 | 0.30 | 0.04 | ✅ |
| B | forward (0/1/0) | **+67.3** | 0.0 | 67 | 68 | 0.0 | 0.26 | 0.03 | ✅ |
| C | backward (1/0/0) | **−68.8** | +0.5 | 69 | 57 | 0.1 | 0.34 | 0.09 | ✅ |
| D | inward (1/1/−1) | +1.4 | **−28.1** | 28 | 99 | 0.0 | 0.33 | 0.05 | ✅ |
| E | outward (1/1/1) | +1.3 | **+28.0** | 28 | 99 | 0.0 | 0.33 | 0.05 | ✅ |
| F | fwd + inward (0/1/−1) | +68.3 | −12.9 | 73 | 65 | −1.6 | 0.26 | 0.04 | ✅ |
| G | fwd + outward (0/1/1) | +68.3 | +12.9 | 73 | 65 | −0.2 | 0.26 | 0.04 | ✅ |
| H | bwd + inward (1/0/−1) | ~−70 | ~−9 | ~71 | ~48 | ~2.0 | ~0.25 | ~0.10 | ✅ |
| I | bwd + outward (1/0/1) | −71.2 | +8.9 | 74 | 48 | −1.4 | 0.25 | 0.11 | ✅ |
| J | both slack (0/0/0) | −3.2 | +1.5 | 4 | 54 | −0.1 | 0.38 | 0.04 | ✅ |
| K | both taut (1/1/0) | +0.3 | 0.0 | 0 | 99 | 0.0 | 0.33 | 0.00 | ✅ |
| L | rapid: fwd→bwd→in→out→diag→neutral | peak angVel 0.9 rad/s, không explosion; **phục hồi về A** | | | | | | ✅ |
| M | side mirror | xem §10 | | | | | | ✅ |

**Stability requirements** (§18 prompt):
0 joint explosion ✅ · 0 uncontrolled spin ✅ (yaw ≤ 2°) · feet never leave rail ✅ ·
feet remain distinct (sep 0.25–0.38 m) ✅ · no major penetration ✅ (`lowerLeg.z ≈ 0`) ·
no permanent lean after neutral ✅ (fwd/depth về ±3°) · no oscillation runaway ✅ ·
no yaw away from opponent ✅ · fast recovery ✅ · side mirror works ✅.

---

## 12. Console result

**0 error, 0 warning** — Play Mode + toàn bộ 9-state + rapid + cả hai side.
Health Check Phase 0 vẫn **PASSED**. Bootstrap scene không đụng.

---

## 13. Cần người dùng play-test

1. **Crouch ~50–54%** (mục tiêu 40–45%). Sâu hơn 1.1 nhưng chưa đạt số; dáng
   "split-squat" rộng fore/aft. Muốn sâu hơn / gọn hơn?
2. **Diagonal depth bị ép nhỏ** (±13° khi forward 68°). Vật lý hợp lý — nhưng cảm
   giác depth ở diagonal có yếu quá không?
3. **Yaw ký sinh ≤ ~2°** ở diagonal backward+depth (rất nhỏ, không phải spin). OK không?
4. **Mapping Right rope = forward / Left rope = backward** — đúng ý? (đảo `forwardBackSign`).
5. **Inward = +Z (ra xa camera), Outward = −Z (về camera)** — hướng này hợp trực giác?
6. **Rope visual** đọc được nhưng đoạn dưới ray ngắn, chưa "đã mắt".
7. **Backward lean crouch pelvis xuống ~48–57%** — nhìn có bị "ngồi bệt" không?
8. Foot slide ±0.10 m — đủ cho footwork tới/lui sau này?
9. Hai build hơi khác (Left 54% / Right 47% crouch) — nhiễu vật lý, cần chốt.
10. **Multitouch chưa test trên thiết bị thật** (code EnhancedTouch chuẩn; đã test
    phím/probe qua MCP).

---

## 14. Không làm (đúng phạm vi)

weapon · sword · combat · damage · HP · KO · hit detection · parry · counter · AI ·
second puppet · multiplayer · networking · matchmaking · backend · Blender ·
production model · skins · monetization.

---

**Files:** `Scripts/Runtime/Prototype/` (PlayerSide, PuppetRig, PuppetRopeController,
PuppetRopeInput, RopeVisual, PuppetDebugHUD — tất cả sửa/viết lại), `Scripts/Editor/
PuppetPrototypeBuilder.cs`, `Scenes/PuppetPrototype.unity`, `Materials/Prototype/
Rail_Slot.mat` *mới*. `PROJECT_MASTER.md §2` (nguyên lý gameplay) không đụng.

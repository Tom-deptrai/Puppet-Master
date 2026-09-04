# Development Log — Puppet Master

Nhật ký phát triển theo thời gian. Ghi các thay đổi kỹ thuật, quyết định và kết
quả kiểm thử ở mức chi tiết. Các quyết định ở tầm dự án thì cập nhật vào
[`PROJECT_MASTER.md`](PROJECT_MASTER.md).

Định dạng: mới nhất ở trên cùng.

---

## 2026-09-04 — Complete combat loop prototype with AI opponent

Hoàn thiện combat loop prototype: Parry/Block → HitQuality → Damage/HP → KO → AI Opponent.
Giữ nguyên movement, rope control, arm control, weapon physics và CombatSkillRecognizer.
Không multiplayer, không art/VFX/UI production, không stamina/combo.

### 1. Block / Parry (`SwordCollisionHandler`)
- Sword-vs-sword phân nhánh: **BLOCK** (defender Guard + clash), **PARRY** (defender chủ động đỡ với tip/arm velocity + relative speed + blade cross), hoặc **CLASH** thuần vật lý.
- Block: damage = 0, không body-hit impulse phụ; physics contact solver vẫn phản lực.
- Parry: damage = 0 + impulse/torque lệch kiếm attacker (physics-based, không teleport/animation).
- Chỉ kiếm “attacker” resolve clash (tránh double-count khi cả hai Rigidbody nhận OnCollisionEnter).
- Threshold SerializeField để tuning.

### 2. HitQuality
- `CombatHitQuality`: Invalid / Glancing / Clean / Heavy từ CombatSkill + ImpactStrength + hướng lưỡi + body part + relative velocity.
- Skill None/Guard → Invalid hoặc Glancing thấp — flailing không phải chiến thuật tối ưu.

### 3. Damage + HP (`PuppetCombatHealth`)
- Max HP 100. Damage = base(HitQuality) × bodyMultiplier × impactBonus.
- Head 1.55 / Torso 1.0 / Arm 0.55 / Leg 0.65.
- Hit cooldown + contact refresh chống damage liên tục khi kiếm còn dính.
- Block/Parry = 0 damage.

### 4. KO
- HP ≤ 0 → tắt input/AI, `PuppetRopeController.SetKO` giảm spring → sụp vật lý; kiếm vẫn joint.
- Không tự hồi sinh. **R** = reset round (`CombatMatchController`).

### 5. AI Opponent (`PuppetAIOpponent`)
- Target thành full puppet `Puppet_Right` cùng controller/physics với Player.
- States: Guard → Approach → Attack (Slash/Thrust/Overhead) → Recover; Defend (Guard hoặc Parry attempt).
- Chỉ `SetInput` — không teleport, không auto-hit, không damage cheat.
- Params: aggression, reactionTime (≈0.2–0.5s), guardChance, parryChance, attackCooldown.

### HUD / Scene
- Player HP, Target HP, KO banner, Last Hit (Skill/Quality/Impact/Part/Damage/Outcome), AI State.
- Scene rebuild: `Puppet Master ▸ Phase 1 ▸ Build Combat Prototype`.

### Kiểm thử Play Mode (Unity MCP)
- Cả hai bắt đầu 100 HP; Clean torso ~15 dmg; Glancing arm ~2; Heavy head cao; Invalid = 0.
- Block/Parry report giữ HP; KO → pelvis sụp (~0.49m), AI tắt; R reset về 100/Guard.
- Free fight vài giây: AI chuyển state, có Clash/hit nhẹ, sword joints Infinity + grip ổn, 0 error console.

### Test phím
`A/L` dây · `Q/E` depth · `J/K` sword arm · `R` reset round.

---

## 2026-09-04 — Rebuild puppet arm rig for natural anatomy

### Nguyên nhân thật sự khiến arm rig nhìn sai
`BuildArm` cũ tạo `UpperArm` / `LowerArm` / `Hand` như các part rời, mỗi part đặt
bằng toạ độ world riêng và **luôn ở rotation = identity**. Capsule identity trong
Unity nằm **thẳng đứng theo Y**, trong khi đường cánh tay lại chạy chéo xuống–ra
trước. Hệ quả:
- Capsule UpperArm (0.24 m) không thể nối điểm shoulder và điểm elbow cách nhau
  ~0.28 m theo phương chéo → hở rõ giữa vai và upper arm.
- Forearm là capsule đứng riêng, lệch ra trước upper arm → "gập góc bất thường".
- `Elbow_Mesh` là sphere gắn cứng vào upper arm ở một điểm world mà upper arm
  không thật sự chạm tới → nhìn như quả cầu nổi giữa không trung.
- Chuỗi toạ độ còn taper dần theo Z (`shoulderZ*0.98 → *0.84`) làm forearm quặp
  vào trong về phía ngực.
Tóm lại: rig không sai spring hay elbow angle — nó sai **hình học gốc**: segment
không được orient theo xương, độ dài không bằng khoảng cách khớp, anchor không
nằm ở đầu xương.

### Phần đã rebuild
- `PuppetPrototypeBuilder.BuildArm` viết lại hoàn toàn:
  - Chuỗi giải phẫu 1 mạch: `shoulderPt → elbowPt → wristPt → handPt`, tất cả
    **đồng phẳng theo Z** (z = ±0.17 = cạnh torso, hết quặp vào trong).
  - Góc dựng từ phương thẳng đứng: upper arm 42° (R) / 40° (L) hướng xuống–ra
    trước; elbow bend build 30° (R) / 28° (L).
  - Mỗi xương = `BoneSegment` mới: Rigidbody đặt tại **trung điểm xương, rotation
    identity** (giữ joint-space = world như chân/spine), còn **mesh capsule VÀ
    CapsuleCollider (child riêng) được xoay đúng theo trục xương** và scale đúng
    bằng khoảng cách 2 khớp → segment phủ khít shoulder→elbow→wrist, không hở.
  - Bỏ elbow sphere nổi. Thay bằng `JointCap`: "Deltoid" dán vào **torso** phủ
    ổ vai, "Elbow" dán vào **forearm** ngay điểm khớp → không bao giờ tách rời.
  - `BendShoulder` chuyển vào trong `BuildArm` (trả về qua tuple), anchor đúng
    tại `shoulderPt`.
  - `BendElbow`: hinge mặt phẳng đánh, limit `-6°..45°` (không gập nhọn được),
    spring 1800 (R) / 1300 (L), giữ compliance depth 16°.
  - Wrist joint anchor tại `wristPt`; sword grip vẫn tại tâm `Hand_R`
    (`BuildSword` không đổi ngoài việc bám theo vị trí hand mới).
- Không đụng: rope control, foot/rail, forward/back & in/out lean, responsiveness,
  camera, sword physics (chỉ điểm bám vào tay).

### Elbow neutral angle
- Build pose: 30° (R) / 28° (L).
- Sau khi ổn định dưới trọng lực + kiếm ở neutral: ~8° (L), ~1–5° (R — do
  `PuppetRopeController.DriveSwordArm` giữ elbow phải bằng `armRelaxedSpring`).
  Cả hai đọc là "upper arm và forearm gần thẳng hàng, cong nhẹ tự nhiên" — đúng
  yêu cầu.

### Kiểm thử Play Mode (debugOverrideInput để dựng đứng)
- Mỗi bên đúng 1 arm; `Shoulder → UpperArm → Elbow → Forearm → Hand` nối liền
  mạch, không joint nổi.
- Kiếm nằm đúng trong `Hand_R` (khoảng cách grip–hand ~0.006 m).
- Lean in/out (Q/E) + thrust/retract mạnh (J/K, vel ±8): cánh tay giữ cấu trúc,
  kiếm có quán tính, không bay khỏi tay, **không joint explosion**, không lỗi
  console. Về neutral thì phục hồi đúng pose ban đầu.
- Đã xác nhận mirror trên `PlayerSide.Right`.

---

## 2026-09-04 — Refine arm posture and increase puppet responsiveness

Chỉnh sửa hình dáng cánh tay tự nhiên và tăng tốc độ phản ứng của toàn bộ puppet ~1.5x:

### 1. Sửa Hình Dáng Cánh Tay (Natural Arm Posture & Physics Joint Limits)
- **Chuẩn hoá tư thế thẳng / cong nhẹ tự nhiên:**
  - Thay đổi hệ toạ độ authored cho cả cánh tay phải (Right Arm cầm kiếm) và cánh tay trái (Left Arm):
    - Khớp vai $\to$ Cánh tay trên (Upper Arm) $\to$ Khuỷu tay (Elbow) $\to$ Cẳng tay (Forearm) $\to$ Bàn tay (Hand) tạo thành một đường thẳng vươn về phía trước - chúc nhẹ xuống tự nhiên (độ dốc đồng hướng, góc lệch giữa upper arm và forearm chỉ ~7° thay vì gập nhọn chữ V như trước).
    - Toạ độ Right Arm: UpperArm tại $(0.12, 1.36, -0.245)$, Elbow tại $(0.22, 1.26, -0.238)$, Forearm tại $(0.32, 1.18, -0.226)$, Wrist tại $(0.42, 1.10, -0.215)$, Hand tại $(0.46, 1.07, -0.210)$.
  - **Giới hạn góc khớp khuỷu tay (`BendElbow`):**
    - Đặt `lowFight: -8f`, `highFight: 45f` (thay vì 135° gập sâu không tự nhiên), lò xo giữ tư thế `spring: 800f / 350f`.
    - Giữ trọn vẹn joint vật lý và tính năng xoay co duỗi khi đâm kiếm / vung chém, nhưng loại bỏ hoàn toàn khả năng bị co gập biến dạng hay tạo góc kỳ quặc.
  - **Cân chỉnh Weapon Physics (Kiếm):**
    - Bàn tay cầm kiếm đặt tại cao độ chuẩn $Y = 1.07\text{m}$, lưỡi kiếm hướng thẳng về phía ngực đối thủ với góc nghiêng tự nhiên (`bladeDir = Vector3(facing * 0.85, 0.45, -0.08)`).

### 2. Tăng Tốc Độ Phản Ứng Toàn Bộ Puppet (~1.5x Responsiveness)
Tăng đồng bộ toàn chuỗi từ Input Filter, Controller Smoothing, đến Physics Springs/Dampers (tuân thủ Critical Damping $c \propto \sqrt{k}$ để tăng tốc mà không gây giật hay rung lắc):
- **Input Speeds (`PuppetRopeInput.cs`):**
  - `tensionRiseSpeed`: $12 \to 18$ (1.5x).
  - `tensionFallSpeed`: $10 \to 15$ (1.5x).
  - `depthSpeed`: $9 \to 13.5$ (1.5x).
  - `armSpeed`: $16 \to 24$ (1.5x), `armReturnSpeed`: $5.5 \to 8$.
- **Controller Smoothing (`PuppetRopeController.cs`):**
  - `tensionSmoothing`: $45 \to 65$ (~1.45x).
  - `depthSmoothing`: $30 \to 45$ (1.5x).
  - `armSmoothing`: $35 \to 50$ (~1.43x).
- **Physics Drives & Limits:**
  - `legStandSpring`: $7200 \to 10800$, `legDamper`: $340 \to 420$, `legSlackSpring`: $900 \to 1350$.
  - `pelvisStandSpring`: $10500 \to 15500$, `pelvisDamper`: $540 \to 670$, `pelvisSlackSpring`: $1200 \to 1800$.
  - `spineStandSpring`: $7200 \to 10800$, `spineDamper`: $340 \to 420$, `spineSlackSpring`: $1500 \to 2250$.
  - `torsoUprightAssist`: $38 \to 55$, `torsoUprightDamping`: $12 \to 16$.
  - `ropePull`: $45 \to 65$.
  - `plane.zDrive`: `positionSpring` $8000 \to 12000$, `positionDamper` $350 \to 450$.
- **Kết quả đo kiểm vật lý (Internal Probe):**
  - Thời gian đạt 80% biên độ nghiêng (forward/backward lean): từ ~0.30s giảm xuống còn **0.196s** (tăng tốc độ phản ứng 1.53x).
  - Khôi phục đứng/ngồi (stand/crouch recovery) và phản ứng Inward/Outward nhanh hơn rõ rệt, duy trì độ đầm, quán tính vật lý và không hề bị rung/nổ joint.

---

## 2026-09-04 — Unify two thumb body and sword control

Hợp nhất hệ thống điều khiển ngang đối xứng dựa trên cả hai ngón cái (2-Thumb Control) để điều khiển đồng thời Full-Body Depth Lean và Sword Arm:

### 1. Công thức Mapping Đối Xứng
- **Độ dời ngang chuẩn hoá:**
  - $x_L$: Độ dời ngang chuẩn hoá của ngón cái trái (Left Thumb) $\in [-1.0, +1.0]$.
  - $x_R$: Độ dời ngang chuẩn hoá của ngón cái phải (Right Thumb) $\in [-1.0, +1.0]$.
- **Ghép nối đối xứng:**
  $$\text{DepthInput} = \frac{x_L + x_R}{2}$$
  $$\text{SwordArmInput} = \frac{x_R - x_L}{2}$$
  $$\text{SwordArmVelocity} = \frac{v_R - v_L}{2}$$

### 2. Ý Nghĩa Cơ Học & Trải Nghiệm Điều Khiển
1. **Common-Mode Movement (Hai ngón di chuyển cùng hướng, cùng biên độ):**
   - $x_L \approx x_R \implies \text{DepthInput} \approx x$, $\text{SwordArmInput} \approx 0$.
   - Puppet nghiêng toàn thân Inward / Outward theo chiều sâu không gian 2.5D mà cánh tay cầm kiếm vẫn giữ nguyên tư thế thủ ổn định.
2. **Differential-Mode Movement (Hai ngón di chuyển ngược hướng hoặc có độ chênh):**
   - $x_R > x_L \implies \text{SwordArmInput} > 0$: Đâm kiếm / vung chém kiếm ra trước.
   - $x_R < x_L \implies \text{SwordArmInput} < 0$: Thu kiếm về sát sườn phòng thủ.
   - Thành phần Common-Mode triệt tiêu, thân puppet giữ thăng bằng thẳng đứng không bị nghiêng ngoài ý muốn.
3. **Combination Movement (Kết hợp cả hai):**
   - Cho phép người chơi vừa nghiêng né đòn theo chiều sâu vừa vung kiếm phản công mượt mà.

### 3. Horizontal Deadzone & Giữ Nguyên Các Cơ Chế Hiện Có
- Thêm `horizontalDeadzonePixels = 16f` cho cả hai thumb: loại bỏ hoàn toàn hiện tượng rung/nhiễu ngang khi người chơi chỉ thực hiện thao tác kéo dọc để chỉnh độ căng dây chân (`Foot_L`, `Foot_R`).
- Giữ nguyên toàn bộ:
  - Vertical drag: Left Thumb $\to$ Left Rope tension, Right Thumb $\to$ Right Rope tension.
  - Forward / backward lean và Squat dựa trên chênh lệch và tổng lực căng dây.
  - Phím desktop debug: `Q` / `E` tiếp tục chỉnh Depth; `J` / `K` tiếp tục chỉnh Sword Arm; `A` / `L` / `Space` chỉnh dây.
  - Quán tính vật lý (inertia), va chạm, rail, và rig cánh tay.

### 4. Kiểm Thử Play Mode
- Hai input ngang giống nhau ($x_L = x_R = 1$) $\implies$ Chỉ Depth lean tối đa, cánh tay giữ nguyên guard.
- Hai input ngang đối nhau ($x_L = -1, x_R = 1$) $\implies$ Chỉ Sword Arm vung ra trước tối đa, thân đứng thẳng (Depth = 0°).
- Combination ($x_L = 0, x_R = 1$) $\implies$ Cả Depth lean (0.5) và Sword Arm (0.5) cùng hoạt động đồng thời.
- Kéo dây dọc (Forward lean, Backward lean, Squat) hoạt động độc lập và hoàn toàn ổn định.

---

## 2026-09-04 — Add prototype arm combat control


Triển khai Arm Combat Control Prototype cho hệ điều khiển 2 ngón tay (2-thumb control) và sửa tư thế neutral của cánh tay:

### 1. Sửa Arm Posture (Không animation cứng, dùng Physics/Joints)
- **Tách cánh tay khỏi thân:**
  - Tăng khoảng cách khớp vai `ShoulderZ` từ `0.19m` lên `0.25m` (mặt bên thân người tại $Z = \pm 0.17\text{m}$, đưa vai mở rộng ra ngoài thân $0.08\text{m}$).
  - Cập nhật toạ độ neutral của cánh tay: Upper Arm đặt tại $Z = \pm 0.255\text{m}$, Elbow tại $Z = \pm 0.245\text{m}$ (tạo khoảng hở rõ rệt giữa cùi chỏ và thân, không còn ép sát hay dính vào torso).
  - Cẳng tay vươn ra trước ngực: Forearm đặt tại $X = \text{Fwd}(0.28\text{m})$, Bàn tay cầm kiếm tại $X = \text{Fwd}(0.40\text{m})$ (cách mặt trước ngực hơn $0.14\text{m}$), nhìn rất rõ và thoáng.
  - Lưỡi kiếm (`Sword_R`) hướng thẳng về phía đối thủ (`+facing * 0.85`, chếch lên ~25°), hoàn toàn không chạm hay xuyên vào thân.
- **Tay trái (Left Arm):**
  - Giữ nguyên tư thế thủ/thăng bằng (guard/balance) tự nhiên ở phía trước ngực ($X = \text{Fwd}(0.31\text{m})$, $Z = +0.18\text{m}$), khớp vai có lò xo thụ động `350f`, không dính vào thân.

### 2. Arm Combat Control (2-Thumb Input & Physics Inertia)
- **Mapping điều khiển:**
  - **Left Thumb:**
    - Vuốt dọc (Vertical): Lực căng dây chân sau (`Foot_L`).
    - Vuốt ngang (Horizontal): Độ nghiêng chiều sâu Inward / Outward (`Depth`, tương đương Q/E trên desktop).
  - **Right Thumb:**
    - Vuốt dọc (Vertical): Lực căng dây chân trước (`Foot_R`).
    - Vuốt ngang (Horizontal): Điều khiển cánh tay phải cầm kiếm (`RightArmTarget` từ -1.0 đến +1.0).
  - **Desktop testing keys:** `J` = Kéo kiếm về thủ / tích thế (-1.0), `K` = Đâm kiếm / chém ngang ra trước (+1.0).
- **Cơ chế Joint Drive & Momentum:**
  - Tạo hàm `BendShoulder` với ConfigurableJoint mở các giới hạn góc (Pitch 85°, Yaw 60°, Roll/Depth 55°), cho phép cánh tay vung chém linh hoạt trong không gian 3D.
  - Khớp vai và khuỷu tay được dẫn động slerp drive:
    - Khi vuốt ngang phải (+): Khớp vai xoay vươn ra trước (Pitch) và quét vào trong (Yaw), khuỷu tay mở thẳng ra để đâm/chém về phía đối thủ.
    - Khi vuốt ngang trái (-): Khớp vai kéo lùi về sau, khuỷu tay co chặt thu kiếm về sát sườn.
  - Đo vận tốc vuốt ngang (`RightArmVelocity`): Vuốt càng nhanh thì torque trợ lực truyền vào UpperArm, LowerArm và Sword càng lớn, tạo tốc độ vung kiếm nhanh và uy lực.
  - Khi buông tay: Giá trị điều khiển hồi dần về neutral guard (`armReturnSpeed = 5.5f/s`), lò xo khớp nhả mềm (`armRelaxedSpring = 400f`). Khối lượng vật lý của kiếm (1.2kg) và cánh tay tiếp tục đà vung tự nhiên (follow-through inertia) mà không hề bị snap tức thì.

### 3. Kiểm thử Play Mode
- Arm không dính thân, cùi chỏ có khoảng hở rõ.
- Bàn tay và kiếm nằm rõ ràng phía trước.
- Vuốt nhanh tạo momentum lớn, vuốt chậm tạo chuyển động êm.
- Khi buông ngón tay, kiếm vung theo quán tính rồi từ từ ổn định lại tư thế guard.
- Hệ thống rope lean (Forward/Backward và Inward/Outward) hoạt động hoàn toàn bình thường, không joint explosion.
- Cân xứng chính xác cho cả Left side và Right side.

---

## 2026-09-04 — Fix full body depth lean arms and rail alignment


Giải quyết triệt để 3 vấn đề kỹ thuật vật lý, rig và môi trường:

### 1. Sửa Inward / Outward thành Full-Body Lean
- **Nguyên nhân trước đây:** `pelvisPlaneJoint.zMotion` bị khoá cứng (`Locked` ở $Z=0$). Khi chân cắm tại $Z=0$ và pelvis bị ghim tại $Z=0$, hai chân không thể nghiêng sang trục Z mà bị kéo căng thẳng đứng; độ uốn chỉ diễn ra từ thắt lưng lên đầu. Ngoài ra, khớp cổ chân (`ankle`) có `ankle.child = Foot` và `ankle.parent = LowerLeg` nên cần đảo dấu góc drive để đẩy cẳng chân cùng chiều với thân.
- **Đã sửa:**
  - Mở `pelvisPlaneJoint.zMotion = Limited` (limit 0.55m, spring 6000, damper 150) kèm `zDrive` dẫn động toạ độ Z của pelvis theo cung nghiêng $Z = -\sin(\text{depthDeg}) \times H \times 0.65$.
  - Mở góc xoay depth (`angularXLimit`) của cổ chân lên `±45°` và gối lên `±15°`.
  - Điều khiển góc cổ chân `ankleRot = Quaternion.Euler(depthDeg * depthLegFollow, 0, squat * ankleSquatDeg)` với `depthLegFollow = 0.85f`.
- **Kết quả đo thực tế trong Play Mode:**
  - `Foot_L`: Roll = 0.0° (cố định trên ray tại Z = 0.00m).
  - `LowerLeg_L`: Roll = -27.1° (bắt đầu nghiêng từ cổ chân, lệch Z = -0.09m).
  - `UpperLeg_L`: Roll = -30.5° (tiếp nối góc cẳng chân, lệch Z = -0.30m).
  - `Pelvis`: Roll = -37.4° (lệch Z = -0.46m).
  - `Torso`: Roll = -58.7° (lệch Z = -0.73m).
  - `Head`: Roll = -74.8° (lệch Z = -1.08m).
  - Toàn bộ cơ thể tạo thành một đường nghiêng liên tục, thống nhất từ bàn chân trên ray lên tới đầu.
  - Yaw drift duy trì triệt để ≤ 0.7° (không xoay lưng hay biến dạng 3D).

### 2. Sửa hệ tay (Cánh tay chân thực & Tư thế Guard)
- **Tái cấu trúc rig tay:**
  - Cánh tay gồm đúng 2 tay giải phẫu: `Shoulder` $\to$ `UpperArm` (Capsule 0.075x0.12x0.075) $\to$ `Elbow` (khớp cầu thị giác 0.08m) $\to$ `LowerArm` (Capsule 0.065x0.11x0.065) $\to$ `Hand` (Box 0.07x0.07x0.07).
  - Khớp khuỷu tay (`BendElbow`): Đặt trục `axis = (0, 0, 1)`, giới hạn góc gập `lowAngularXLimit = -5°` (chặn đứng hoàn toàn hiện tượng bẻ ngược / hyperextension) và `highAngularXLimit = 130°` (cho phép co tay tự nhiên).
  - Khớp vai và cổ tay có spring giữ tay ở tư thế guard (Upper arm hướng ra trước, Forearm gập lên trước ngực, Hand nắm đấm che cằm/ngực).
  - Tắt va chạm giữa các bộ phận tay và thân (`IgnoreAdjacentCollisions`) để tránh tay bị giật hoặc xuyên thấu thân.

### 3. Sửa Rail Alignment
- **Nguyên nhân trước đây:** Ray trước đó có kích thước ngắn và chưa có rãnh trượt (groove) rõ nét, các slot guide đặt chìm; góc camera 3/4 tạo cảm giác ray bị lệch tâm so với bước chân.
- **Đã sửa:**
  - Kéo dài thanh ray lên `1.60m` (cao `0.05m`, rộng `0.22m`), đặt tâm tại `(originX, 0.025f, 0f)`.
  - Thêm rãnh trượt cơ học `Rail_Groove` nằm giữa ray tại `Y = 0.0505m`.
  - Mặt trên của ray nằm chính xác tại `Y = 0.050m`, khớp hoàn hảo với đáy bàn chân (`FootY = 0.085m`, chiều cao `0.07m` $\implies$ đáy bàn chân `Y = 0.050m`).
  - Cân xứng tuyệt đối cho cả PlayerSide Left (`originX = -1.0`) và Right (`originX = +1.0`).
  - Cập nhật `RopeVisual` nhân `FacingSign` để dây điều khiển fan ra đúng vùng ngón tay mà không bị chéo khi đổi bên.

---

## 2026-09-04 — Refine puppet lean, collapse, arms and rail

Giải quyết 4 vấn đề kỹ thuật vật lý và rig theo yêu cầu:

### 1. Tăng Inward / Outward Lean (~2x)
- Tăng `depthGain` lên `36f` (kết hợp `spineFollow = 0.38f`, `neckFollow = 0.26f`, `depthHipFollow = 0.40f`).
- Nâng giới hạn góc `angularXLimit` (depth) trên các joint (`pelvisPlaneJoint`, `spine`, `neck` lên `±55°`, `hip` lên `±50°`, `ankle` lên `±18°`).
- Kết quả kiểm thử Play Mode:
  - Torso depth lean đạt ~52.1° ở extreme (mục tiêu 45–50°).
  - Pelvis depth lean đạt ~27.2–27.9° (mục tiêu 30–35°).
  - Head depth lean đạt ~65.8–67.7° (toàn thân uốn đồng bộ theo depth).
  - Yaw drift duy trì triệt để ~0–1° (không xoay lưng hay biến dạng 3D).
  - Bàn chân giữ vững 100% trên ray (Y = 0.085m).

### 2. Sửa cơ chế hạ trọng tâm khi thả dây (Dynamic Collapse)
- Loại bỏ việc ép target về góc đứng 0° khi tension giảm:
  - Tính `activeBlend` dựa trên độ căng dây.
  - Khi tension chùng (`combined → 0`), `fbDeg` mượt mà theo sát góc nghiêng thực tế `ForwardLeanDeg` hiện tại thay vì ép về đứng thẳng.
  - Giảm spring slack (`pelvisSlackSpring = 1200f`, `spineSlackSpring = 1500f`, `legSlackSpring = 900f`) và scale `torsoUprightAssist` theo tension để gravity, quán tính (inertia) và joint constraints quyết định hướng sụp.
- Kết quả:
  - Đang nghiêng forward thả dây → sụp tự nhiên về phía forward (ForwardLeanDeg giữ ~61.2°, pelvis hạ từ 1.06m xuống 0.56m).
  - Đang nghiêng backward thả dây → sụp tự nhiên về phía backward (ForwardLeanDeg giữ -69.3°, pelvis hạ xuống 0.33m).
  - Đang nghiêng depth thả dây → sụp theo hướng depth.
  - Khi kéo căng dây trở lại → puppet đứng thẳng dậy ngay tức thì (recovers to 1.05m standing in < 0.2s).

### 3. Sửa hệ tay (Đúng 2 tay giải phẫu rõ ràng)
- Tái cấu trúc rig tay trong builder với `BuildArm`:
  - Shoulder → Upper Arm (`CapsuleCollider`) → Elbow Joint (hình cầu thị giác `Elbow_Mesh` tại khớp) → Forearm/Lower Arm (`CapsuleCollider`) → Hand (`BoxCollider`).
  - Khớp Elbow (`ConfigurableJoint`) và Wrist (`ConfigurableJoint`) có spring tự nhiên để tay co guard phía trước.
  - Loại bỏ hoàn toàn cảm giác 4 tay (tổng cộng đúng 2 tay: Left arm ở `+ShoulderZ`, Right arm ở `-ShoulderZ`).
  - Sẵn sàng cấu trúc Hand để gắn vũ khí ở các phase sau.

### 4. Sửa Rail bị lệch
- Cân chỉnh hình học và toạ độ:
  - Đặt `Rail` tại `(originX, 0.025f, 0f)`, kích thước `(1.20f, 0.05f, 0.24f)` để mặt trên của ray nằm chính xác tại `Y = 0.050m`.
  - Bàn chân (`FootY = 0.085f`, chiều cao `0.07f`) tiếp xúc đáy đúng tại `Y = 0.050m` nằm khít trên ray.
  - Rail slots nằm ngay dưới 2 bàn chân `Y = 0.048m`.
  - Đối xứng hoàn hảo cho cả `PlayerSide.Left` (`originX = -1.0`) và `PlayerSide.Right` (`originX = +1.0`).

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

---

## 2026-09-04 — Fix rail alignment to foot movement axis

### Vấn đề đã xử lý
- **Nguyên nhân thật sự của rail bị lệch:**
  1. **Bất đồng bộ hệ toạ độ (Coordinate Basis Disconnect):** Kích thước và toạ độ của `Rail` (`1.60f, 0.05f, 0.22f`), `Rail_Groove` (`0.28` local scale) và `Slot_L`/`Slot_R` (`0.18f` width) được khai báo độc lập với kích thước bàn chân (`FootWidth = 0.13f`, `FootHeight = 0.07f`, `FootY = 0.085f`). Chiều rộng rail quá lớn ($0.22m$) kết hợp với góc nhìn camera 3/4 từ trên xuống ($18^\circ$ pitch) tạo hiệu ứng thị sai (perspective parallax) làm mặt trước của thanh ray nhô ra ngoài quá nhiều so với chân.
  2. **Dây RopeVisual cắt chéo qua ray:** Điểm neo `BelowRail` trước đây bị lệch ra phía trước ở $Z = -0.95m$ và $Y = -0.02m$, khiến đoạn `LineRenderer` từ khe ray đi chéo qua mặt trên ray hướng về camera thay vì đi thẳng đứng xuống dưới sàn tại $Z = 0$, tạo cảm giác như dây kéo thanh ray lệch ra phía trước.

### Thay đổi đã thực hiện
- `PuppetPrototypeBuilder.cs`:
  - Đồng bộ hoá toàn bộ hằng số hình học từ chung một coordinate basis:
    - Bàn chân: `FootHeight = 0.07f`, `FootLength = 0.26f`, `FootWidth = 0.13f`, `FootY = 0.085f` (đáy bàn chân tại $Y = 0.050f$).
    - Rail: `RailHeight = 0.05f`, `RailWidth = 0.15f`, `RailLength = 1.50f`, `RailTopY = 0.050f` (mặt trên phẳng đúng với đáy bàn chân), `RailCenterY = 0.025f`.
    - `Rail` đặt tại `(originX, 0.025f, 0f)`, `Rail_Groove` đặt chính giữa đỉnh ray chạy dọc trục trượt $Z = 0$.
    - `RopeAttach` đặt chính xác tại đáy bàn chân `localPosition = (0f, -FootHeight * 0.5f, 0f)`.
    - `BelowRail` và `ForceAnchor` nằm thẳng đứng tại `(Fwd(±StanceHalf), Y, 0f)` (Z = 0) giúp dây luồn thẳng qua khe ray xuống dưới sàn mà không bao giờ cắt qua ray.
- Đã test và xác nhận trong Play Mode trên cả `PlayerSide.Left` và `PlayerSide.Right`:
  - Hai bàn chân trượt dọc chính xác trên trục ray tại $Z = 0$, đáy bàn chân tiếp xúc phẳng hoàn toàn với mặt trên của rail.
  - Rail visual nằm hoàn toàn chính giữa đường trượt bàn chân cả trong Scene và Game View.

---

## 2026-09-04 — Add prototype sword physics to right hand

### Thay đổi đã thực hiện
- **Cấu trúc kiếm (`Sword_R`):**
  - Gồm `Handle_Mesh` (cylinder hilt), `Pommel_Mesh` (sphere), `Guard_Mesh` (crossbar) và `Blade_Mesh` (box blade dài 0.74m).
  - Có `BoxCollider` (blade) và `CapsuleCollider` (handle/hilt).
  - `Rigidbody`: `mass = 1.2f`, `linearDamping = 0.05f`, `angularDamping = 1.2f`, `collisionDetectionMode = ContinuousDynamic`.
- **Kết nối khớp (Joint):**
  - Gắn vào `Hand_R` bằng `ConfigurableJoint` (khoá 6 DOF: linear locked, angular locked với `projectionMode = PositionAndRotation`, `projectionDistance = 0.01f`, `projectionAngle = 2f`).
  - Lực inertia và moment quán tính của kiếm truyền trực tiếp vào khớp cổ tay (`Wrist`), khuỷu tay (`Elbow`), và vai (`Shoulder`).
- **Tùy chỉnh tư thế tay cầm kiếm:**
  - Nâng `maximumForce` trên các khớp thụ động từ `180f` lên `15000f` để lực đàn hồi không bị bão hòa sớm dưới trọng lượng của vũ khí.
  - Điều chỉnh nhẹ độ cứng tư thế thủ (guard posture): `shoulderR` spring `750f`, `elbowR` spring `700f`, `wristR` spring `550f`.
  - Căn hướng kiếm trong thế thủ hướng thẳng về phía đối thủ (~25° so với phương ngang).
- **Collision Filtering:**
  - Thêm `Pair` ignores trong `PuppetRopeController.IgnoreAdjacentCollisions()` giữa `sword` và `Hand_R`, `LowerArm_R`, `UpperArm_R`, `Torso`, `Head` để tránh tự va chạm gây rung lắc.
- **Kiểm thử Play Mode:**
  - Đứng bình thường: kiếm nằm chắc chắn trong tay, tư thế thủ đẹp.
  - Lean trước/sau (A/L) và nghiêng sâu In/Out (Q/E): kiếm chuyển động theo tự nhiên, truyền quán tính và dao động thực tế vào cánh tay.
  - Chuyển hướng nhanh: kiếm có đà nhưng không bay khỏi tay, không joint explosion.
  - Đã kiểm tra đối xứng gương trên cả `PlayerSide.Left` và `PlayerSide.Right`.

---

## 2026-09-04 — Add weapon collision and impact prototype

### Mục tiêu hoàn thành
Khởi động prototype vật lý chiến đấu: Weapon Collision + Physical Impact giữa Player và Target thứ hai mà không thêm HP, damage system, KO, AI hay VFX lớn.

### Thay đổi đã thực hiện
1. **Target thứ hai (`Target_Dummy`):**
   - Đặt trên thanh ray riêng `Rail_Right` tại $X = +0.58m$ (khoảng cách đấu giữa 2 puppet là $1.16m$, ở thế thủ cách nhau ~6cm an toàn).
   - Cấu trúc đầy đủ Rigidbody, Collider và Joint (`Target_Pelvis`, `Target_Torso`, `Target_Head`, `Target_UpperArm`, `Target_LowerArm`, `Target_Hand`, `Target_UpperLeg`, `Target_LowerLeg`, `Target_Foot`).
   - Khớp có lò xo thụ động (`passiveSpring` 1200f – 5500f) giúp target tự đứng vững trên ray, phản ứng giật lùi khi bị đánh và tự hồi phục về tư thế đứng ban đầu mà không bị sụp hay nổ khớp.
   - Sử dụng bảng màu xám/thép đối lập để phân biệt rõ với Player.
2. **Weapon Collision (`SwordCollisionHandler.cs`):**
   - Gắn trực tiếp lên `Sword_R` (có `BoxCollider` và `Rigidbody` ContinuousDynamic).
   - Dùng `OnCollisionEnter` bắt va chạm vật lý thực: trích xuất điểm tiếp xúc (`contact.point`), pháp tuyến (`contact.normal`), nhận diện tên body part bị trúng.
   - Đọc vận tốc tiếp điểm của kiếm qua `swordRb.GetPointVelocity(contact.point)` kết hợp vận tốc tương đối `relVelocity`.
3. **Tính `ImpactStrength` & Phân loại:**
   - Công thức: $\text{ImpactStrength} = m_{\text{sword}} \times \max(|v_{\text{rel}} \cdot n|, 0.5 \times |v_{\text{rel}}|)$.
   - Ngưỡng Inspector:
     - **WEAK**: $< 2.0$ (chạm nhẹ, tiếp xúc chậm).
     - **MEDIUM**: $2.0 – 5.0$ (vung kiếm tốc độ vừa).
     - **STRONG**: $\ge 5.0$ (vung nhanh có đà lớn).
4. **Hit Reaction vật lý:**
   - Truyền xung lực `AddForceAtPosition(pushDir * impulseMag, contact.point, ForceMode.Impulse)` vào đúng vị trí tiếp xúc của Rigidbody bị đánh.
   - Độ lớn xung lực tỉ lệ với `ImpactStrength` (có clamp giới hạn an toàn), cú đánh mạnh tạo phản ứng rõ rệt hơn cú nhẹ, không teleport, không animation, không nổ joint.
5. **Debug HUD:**
   - Cập nhật `PuppetDebugHUD` hiển thị body part vừa trúng, relative velocity, ImpactStrength và phân loại (WEAK / MEDIUM / STRONG).
6. **Kiểm thử Play Mode:**
   - Xác nhận va chạm vật lý thực hoạt động trên `Target_Torso`, `Target_LowerArm_R`, `Target_Hand_R`.
   - Chạm nhẹ được phân loại WEAK (0.83 – 1.47 m/s, strength 0.50 – 1.32).
   - Vung vừa được phân loại MEDIUM (2.82 – 4.72 m/s, strength 2.61 – 4.98).
   - Vung mạnh được phân loại STRONG (6.21 m/s, strength 7.44).
   - Target nhận impulse nảy giật lùi đúng hướng, không nổ khớp, chân giữ chặt trên ray.
   - Project Health Check PASSED 100%.

---

## 2026-09-04 — Fix sword attachment and add target weapon

- Xác nhận `Sword_R` không bị break joint và không có script auto-reattach: `breakForce`/`breakTorque` vốn là Infinity. Hiện tượng tuột rồi trở lại là constraint drift khi joint khóa 6-DOF tắt preprocessing, cộng với torque được bơm trực tiếp vào sword; `JointProjection` sau đó snap kiếm về anchor.
- Dùng chung một cấu hình `ConfigurableJoint` chắc chắn cho hai kiếm: nối đúng `Hand_R`, anchor và connectedAnchor tại tâm grip, khóa toàn bộ linear/angular motion, `breakForce`/`breakTorque = Infinity`, tắt collision nội bộ với hand/arm/body, bật preprocessing và siết projection còn 0.002 m / 0.5°.
- Bỏ torque trực tiếp lên sword; torque/momentum vẫn truyền vật lý từ upper arm/lower arm qua wrist, hand và weapon joint. Rigidbody sword vẫn giữ mass, inertia, gravity, collider và Continuous Dynamic collision.
- Thêm `Sword_Target` Rigidbody + blade/handle Collider vào `Target_Hand_R`, dùng cùng attachment chắc chắn và tư thế guard thụ động; không thêm AI hay điều khiển.
- Sword-vs-sword dùng phản lực contact solver nguyên bản (không cộng impulse hit reaction lần hai) và log `WEAPON CLASH`. Play Mode xác nhận clash, cả hai kiếm giữ trong tay sau chuỗi swing/lean/depth nhanh, không có joint break/explosion.

---

## 2026-09-04 — Complete combat skill recognition prototype

Nhận diện 4 skill từ chuyển động kiếm thật do người chơi tạo ra. Không thêm HP, damage, KO, AI, multiplayer, combo, animation attack hay nút Attack/Skill. Recognizer chỉ **đọc** trạng thái, không tác động lên rig/joint/Rigidbody.

### `CombatSkillRecognizer.cs`
- Đo tại **mũi kiếm** bằng transform delta (không dùng `Rigidbody.linearVelocity`: joint khoá 6-DOF báo ~2.7 m/s / ~10 rad/s nhiễu solver ngay cả khi kiếm đứng yên). Vận tốc mũi kiếm được làm mượt EMA 45 ms rồi phân tích theo hệ facing: `forward / lateral / vertical`.
- **Thrust** — mũi kiếm đi **dọc trục lưỡi** (`alongBlade ≥ 0.65`), lưỡi chĩa vào đối thủ (`bladeFwd ≥ 0.80`), tầm với vượt độ rơi (`forward ≥ -vertical`), xoay thấp (`≤ 3.5 rad/s`). Đâm không cần nhanh nên có cổng riêng `forward ≥ 0.85 m/s`.
- **OverheadStrike** — mũi kiếm từng được nâng **trên đỉnh đầu** (`peak ≥ head + 0.05`), rơi ≥ 0.30 m với tốc độ xuống ≥ 1.2 m/s. Đỉnh cao được **đóng băng khi lưỡi đang bổ xuống** để quãng rơi đo đúng cú chém thay vì chạy theo lưỡi.
- **HorizontalSlash** — vòng quét ngang lấn trục dọc 1.25×, xoay ≥ 2.5 rad/s. Bị khoá khi kiếm đang giơ trên đầu (đó là đà của overhead) và khi mũi kiếm lùi ra xa đối thủ (đó là đà hồi sau cú lao, không phải đòn đánh).
- **Guard** — tư thế **giữ có chủ ý**: `ArmValue ≤ -0.30`, lưỡi ngửa lên che thân, kiếm gần như đứng yên; có dwell 0.18 s và grace 0.12 s nên không nhấp nháy. Đứng yên (arm = 0) **không** phải Guard.
- Không nhận đòn khi puppet đang đổ (`pelvis < 60%` chiều cao đứng). Cooldown 0.45 s để đà hồi của một cú không bị đặt tên lần hai.

### Sửa neutral pose (nguyên nhân Guard mất ổn định)
- Kiếm Player rest pose `(facing*0.85, 0.45, -0.08)` → `(facing*0.60, 0.78, -0.08)`.
- Kiếm Target rest pose `(facing*0.30, 0.95, 0.08)` → `(facing*-0.34, 0.92, 0.16)` — vác chếch **ra xa** Player, vẫn nằm trong tầm swing để test sword-vs-sword.
- Giữ nguyên khoảng cách sàn đấu 1.16 m đã validate ở phase va chạm (không đẩy hai puppet ra xa).
- Kết quả: lúc đứng yên **không còn contact nào** giữa kiếm Player và Target (`ComputePenetration` = NONE, khoảng cách gần nhất ~0.19 m).

### Kiểm thử Play Mode (kịch bản tự động qua `debugOverrideInput`)
| Bài test | Kết quả |
|---|---|
| Neutral đứng yên 5 s (×2) | 0/251 frame nhận skill |
| Guard giữ `arm=-0.60`, 4 s (×2) | 201/201 frame = Guard, ổn định tuyệt đối |
| Horizontal slash (vung tay nhanh) ×3 | 3/3 đúng, mỗi lần bắn 1 lần |
| Thrust (lao người, lưỡi ngang) ×3 | 3/3 đúng |
| Overhead strike (nâng kiếm rồi bổ) ×3 | 3/3 đúng |
| Stress 20 s lean + depth liên tục, tay kiếm nghỉ | 1 nhận diện ở frame chuyển trạng thái đầu tiên / 1001 frame |

- Không có false-positive đáng kể; neutral và chuyển động thân thường sạch.
- Sword attachment nguyên vẹn sau toàn bộ chuỗi swing: joint `Sword_R` → `Hand_R` OK, lệch grip 0.0019 m, `Sword_Target` OK, không joint break, Console 0 error.
- Không đổi control, Rigidbody hay cấu hình joint nào.

### HUD
`PuppetDebugHUD` bổ sung: Current Skill, tip speed + angular, phân rã fwd/lat/vert, blade fwd/up + along-blade, Arm Input và Guard ON/OFF.

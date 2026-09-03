# Phase 1.1 Report — Tune puppet stance symmetry and rope layout

**Ngày:** 2026-09-04 · **Commit:** xem cuối file · **Scene:** `Assets/_Project/Scenes/PuppetPrototype.unity`

Phase 1.1 sửa 5 vấn đề chủ dự án nêu sau khi chơi thử Phase 1, cộng một **bổ sung
quan trọng về hệ quy chiếu** (puppet phải quay mặt về phía đối thủ, không nhìn
camera). Không thêm combat, không sang Phase 2.

---

## 1. Facing model

### Mô hình đang dùng

Mỗi puppet đứng một bên arena và **quay mặt về phía đối thủ** dọc trục **X thế giới**.

| | `PlayerSide.Left` | `PlayerSide.Right` |
|---|---|---|
| Vị trí puppet | `originX ≈ -1.0` (nửa trái) | `originX ≈ +1.0` (nửa phải) |
| Hướng mặt (`facingSign`) | **+X** (`+1`) | **-X** (`-1`) |
| Đối thủ (giả định) | phía `+X` (giữa màn hình) | phía `-X` (giữa màn hình) |
| Rope/pulley | treo phía sau (−X so với puppet) | treo phía sau (+X so với puppet) |
| Camera | side-on, lệch nhẹ về phía trước puppet | mirror |

Builder tính **mọi toạ độ** từ `originX` và một hàm
`Fwd(d) = originX + facingSign * d` = "điểm cách puppet `d` mét **về phía đối thủ**".
Đổi `side` → `facingSign` đổi dấu → toàn bộ layout mirror, không hardcode bên nào.

Dấu hiệu hình ảnh: một cục hộp đỏ `Head/Face` gắn ở `+facingSign·X` của đầu — luôn
chỉ về phía đối thủ, để không nhầm "đứng xoè nhìn camera".

### Quy chiếu Forward / Backward

- **Forward Lean** = nghiêng **về phía đối thủ**.
- **Backward Lean** = nghiêng **ra xa đối thủ**.
- `PuppetRopeController.ForwardLeanDeg` (dương = forward) tính từ góc roll world-Z
  thực của torso, nhân `-leanPolarity` (`+1` Left / `-1` Right) → **cùng ý nghĩa
  body-relative cho cả hai side**. HUD hiển thị số này + nhãn `FORWARD` / `BACKWARD`.
- Trên màn hình:
  - Left: nghiêng sang **phải** = forward · sang **trái** = backward.
  - Right: nghiêng sang **trái** = forward · sang **phải** = backward.

### Input vẫn theo cơ thể puppet

`Left Rope → Foot_L` (chân dẫn, phía trước), `Right Rope → Foot_R` (chân sau) —
**không đổi theo side**. Zone input = nửa trái / nửa phải màn hình = ngón trái / phải.

Mapping tư thế hiện tại (baseline, đảo dấu `leanFromImbalance` để hoán đổi):

| Kéo | Kết quả |
|---|---|
| **Right rope** căng, Left chùng | **Forward lean** (~+40°) |
| **Left rope** căng, Right chùng | **Backward lean** (~−40°) |
| Cả hai căng | đứng cao, ~thẳng |
| Cả hai chùng | crouch sâu (~53% chiều cao đứng) |

### Side mirror hoạt động thế nào

- `PuppetRig.side` (`PlayerSide`). `PlayerSide.Sign()` = `-1` Left / `+1` Right.
- **Builder:** 2 menu `Puppet Master ▸ Phase 1 ▸ Build Puppet Prototype — Left side`
  / `— Right side (mirror test)`, cùng gọi `Build(PlayerSide)`. Tất cả part, joint
  anchor, rail home, pulley, forceAnchor, camera, ánh sáng đều là hàm của
  `originX` + `Fwd()`. Scene commit dùng **Left**.
- **Controller:** `leanPolarity` (từ `rig.side`) đổi dấu Z-lean world; `ForwardLeanDeg`
  cũng đổi dấu → gameplay body-relative **giống hệt** hai bên (đã kiểm chứng:
  `L1/R0 = -39.8°`, `L0/R1 = +41.3°` cho **cả** Left và Right).
- **Chưa làm** (không cần ở phase này): UI đổi side, hai puppet cùng lúc, PvP.

---

## 2. Nguyên nhân & sửa: chân chồng/lẫn vào nhau

### Nguyên nhân (Phase 1)

1. Hai chân **side-by-side** (cùng vị trí forward, khác nhau theo trục screen-X).
   Khi gối gập trong mặt phẳng màn hình để hạ pelvis, hình học **ép hai cẳng chân
   chụm về giữa** → chúng cắt nhau.
2. `PuppetRopeController` gọi `Physics.IgnoreCollision(...)` cho **mọi cặp** bộ phận
   của puppet (chuẩn ragdoll để tránh jitter) → **không có gì** chặn chân xuyên chân.
3. Rail home spring yếu (Phase 1.0: 120 → sau tweak 1600) + hip splay kéo bàn chân
   dạt ra rồi lệch.

### Giải pháp (Phase 1.1)

| Biện pháp | Chi tiết |
|---|---|
| Chân **fore/aft trên ray** | `Foot_L` ở `Fwd(+0.16)`, `Foot_R` ở `Fwd(-0.16)` — khác vị trí **dọc ray**, không còn ép chụm khi hạ thấp. |
| Rail `xDrive` cứng | spring **2600**, damper 60 → mỗi bàn chân bám chặt home X. |
| Rail `xMotion` hẹp | `Limited ±0.10 m` (trượt tới/lui nhỏ), `y/z Locked`, **mọi angular Locked** (bàn chân bắt vít phẳng vào ray). |
| Self-collision có chọn lọc | `IgnoreAdjacentCollisions()` chỉ tắt va chạm giữa các cặp **nối khớp trực tiếp** (pelvis↔đùi, đùi↔cẳng, cẳng↔bàn, torso↔tay…). **LowerLeg_L ↔ LowerLeg_R và hai bàn chân vẫn va chạm** → không thể xuyên nhau. |
| Squat đối xứng | cả hai chân nhận cùng `squat = f(combined tension)` → không lệch. |

### Kết quả (mọi state)

- Foot separation **0.29–0.36 m** (đứng 0.32 m) — luôn tách, không swap.
- Hai cẳng chân luôn ở `Z ≈ 0.00` (không chồng theo chiều sâu).
- Crouch: knee X-gap ~0.57 m, không collider penetration.
- Pelvis **không** hạ bằng cách nhập hai chân — nó hạ do gối gập, chân giữ nguyên chỗ.

---

## 3. Nguyên nhân & sửa: asymmetry trái/phải

### Nguyên nhân

- Phase 1 drive spine/neck về một "fold" nghiêng hardcode **+8°/+10° quanh trục Z**
  (một phía) ở trạng thái slack. Rig phẳng nên "fold" này = **nghiêng ngang**.
- Spring slack yếu → gravity khuếch đại độ nghiêng đó (positive feedback: càng
  nghiêng, moment trọng lực cùng chiều càng lớn) tới **sát joint limit +50°**.
  Đo được: torso ở slack lệch **+47.8°**.
- Khi một dây căng: `leanFromImbalance` cộng ±12° vào target. Chiều **+** trùng với
  bias (+) → dồn lại 28°. Chiều **−** gần triệt tiêu bias → ~1°. ⇒ bất đối xứng.
- Chưa kể builder Phase 1 dựng hai chân **không mirror chính xác** (`hip` anchor
  `+0.03` vs `-0.06`, v.v.).

### Giải pháp

1. **Bỏ hẳn** fold nghiêng của spine/neck. Spine/neck chỉ track `leanDeg` (đối xứng).
2. **Squat tách khỏi lean:** squat depth = `f(combined tension)`, áp **giống nhau**
   cho hai chân (`perSideSquatWeight = 0.25` cho một chút khác biệt per-rope) →
   không còn lệch trọng tâm do chân dài/ngắn khác nhau.
3. **Lean = nguồn Z duy nhất:** `leanDeg = (l - r) · leanFromImbalance · leanPolarity`.
4. Builder dựng chân **mirror fore↔aft chính xác** (part & anchor).
5. Spring đủ mạnh chống runaway: spine slack **2200**, pelvis slack **2800**,
   torso AddTorque assist **45** (cap 110).

### Kết quả

| | Trước (Phase 1) | Sau (Phase 1.1) |
|---|---|---|
| Slack 0/0 torso lean | **+47.8°** (bias lớn) | **+3.4°** |
| L1/R0 | +28° | **−39.8°** |
| L0/R1 | +1° | **+41.3°** |
| Lệch giữa hai chiều | ~27° | **1.5° (~3.7%)** |

Right side: `L1/R0 = -39.8°`, `L0/R1 = +41.3°` — **y hệt** Left (body-relative).

---

## 4. Rope / pulley layout mới

- Puppet dời ra **1/3 màn hình bên mình** (`originX = ±1.0`). Vùng giữa (nơi hai
  puppet sẽ giao chiến) **trống**.
- Mỗi pulley treo **thẳng đứng ngay trên bàn chân tương ứng** (`Fwd(±0.16)`,
  `Y` 3.05 / 3.35, lệch `Z` ±0.12 để phân biệt hai dây từ camera side-on).
  → dây **gần thẳng đứng**, không hướng về phía đối thủ, **không cắt qua torso/head**
  (dây trước đi sát cạnh đầu ~5 cm — điểm cần đánh giá).
- **Tách lực khỏi hình:**
  - `RopeVisual` vẽ `LineRenderer` từ `pulley` xuống `RopeAttach` (mặt sau bàn chân).
    Căng = thẳng/dày/sáng; chùng = võng parabol/mảnh/mờ.
  - Lực gameplay (`PuppetRopeController.RopePull`) tác động **ở bàn chân** về phía
    `Leg.forceAnchor` — điểm **thẳng đứng ngay trên foot-home**, đối xứng, **không**
    lệ thuộc vị trí pulley (nên đổi layout pulley không phá đối xứng lực).
- `PuppetRig` thêm: `side`, `Leg.forceAnchor`, `Leg.pulley` (visual), `Leg.railHomeX`,
  `standingFootSeparation`.

---

## 5. Body layout & tuning

Rig vẫn **phẳng trong mặt phẳng chiến đấu X-Y**: trục quay tự do duy nhất ở mọi
khớp là **quanh world Z**; yaw + chúi vào/ra màn hình khoá cứng → luôn 2.5D.

Thay đổi so với Phase 1:

- Chân **fore/aft trên ray** (`Foot_L` dẫn về phía đối thủ).
- Thân **hẹp theo X, rộng theo Z** (0.26 × 0.44 × 0.34) cho side-view.
- Vai ở **`±Z` (0.15)** = trái/phải giải phẫu; tay ở **guard nhẹ hướng trước**,
  không xoè sang camera.
- `Head/Face` marker chỉ hướng facing.

### Giá trị tuning trước → sau

| Tham số | Phase 1 | Phase 1.1 |
|---|---|---|
| Cơ chế lean | `leanFromImbalance` + fold spine hardcode | `(l−r)·gain·polarity` (đối xứng) |
| Cơ chế squat | fold per-side (bất đối xứng) | `f(combined)` (`perSideSquatWeight 0.25`) |
| `leanFromImbalance` | 12 | **24** (thực tế ±40°) |
| knee / hip fold @ slack | 52° / 5° (splay trong screen-plane) | knee **100°** / hip **62°** (squat forward/back) |
| `ankleSquatDeg` | — | **−40°** |
| `legSquat*Spring` slack/stand | 150 / 2600 | **600 / 2800** |
| pelvis→world spring slack/stand | 340 / 4200 | **2800 / 4600** |
| spine spring slack/stand | 230 (+bias) / 2600 | **2200 (không bias) / 3000** |
| torso assist / cap | 25 / 60 | **45 / 110** |
| rope pull → target | `pulley` (gần giữa) | `forceAnchor` (đối xứng, thẳng trên foot) |
| pulley | (±0.42, 2.55) gần giữa màn hình | thẳng trên mỗi foot, ở 1/3 bên mình |
| rail xDrive spring / limit | 1600 / ±0.05–0.10 | **2600 / ±0.10** |
| self-collision | tắt **toàn bộ** | chỉ tắt cặp nối khớp (hai chân vẫn va nhau) |
| pelvis @ slack | ~69% | **~53%** |
| facing | quay vào camera | quay về đối thủ + Face marker + mirror theo side |

*(Toàn bộ số này là **baseline để chơi thử tiếp**, không chốt vào `PROJECT_MASTER.md`.)*

---

## 6. Kết quả test

Left side, mỗi state settle ~4–5 s. `debugOverrideInput` ép tension để test.

| Test | Pelvis | Lean (forward +) | Foot sep | Vel pelvis | Ổn định |
|---|---|---|---|---|---|
| **A** L0 / R0 | 0.56 m (**53%**) | +3.4° | 0.29 m | 0.06 | ✅ crouch sâu, coherent |
| **B** L1 / R1 | 1.05 m (**99%**) | +1.2° | 0.34 m | 0.00 | ✅ đứng cao |
| **C** L1 / R0 | 1.00 m | **−39.8°** BACKWARD | 0.35 m | 0.01 | ✅ |
| **D** L0 / R1 | 1.00 m | **+41.3°** FORWARD | 0.36 m | 0.01 | ✅ |
| **E** 1/0→0/1→1/1→0/0 | — | chuyển mượt, maxAngVel ≤ 0.9 rad/s | 0.29–0.36 | — | ✅ phục hồi đúng về A |
| **F** crouch check | leg Z-gap 0.00 · knee X-gap 0.57 m · **không** penetration | | | | ✅ |
| **G** Right mirror | `L1/R0 = -39.8°`, `L0/R1 = +41.3°` — **giống hệt Left** (body-relative) | | | | ✅ |

- **Đối xứng:** |−39.8| vs |+41.3| → lệch **1.5°** (~3.7%).
- **Biên độ lean:** ~40° (mục tiêu 35–45°). Không lật, không joint explosion, chân đúng ray.
- **Chênh HIGH↔LOW:** 1.05 m ↔ 0.56 m — rõ ngay.
- **Console:** 0 error / 0 warning (Play + mọi state). Health Check Phase 0 vẫn PASSED.
- **Bootstrap** không đụng.

Screenshots: [`Phase01_1/`](Phase01_1/)

| File | Nội dung |
|---|---|
| `both_slack.png` | Left, L0/R0 — crouch sâu 53% |
| `both_taut.png` | Left, L1/R1 — đứng cao 99%, facing +X |
| `left_taut_right_slack.png` | Left, L1/R0 — backward lean −40° |
| `left_slack_right_taut.png` | Left, L0/R1 — forward lean +41° |
| `left_side_rope_layout.png` | Left, đứng — pulley thẳng trên foot, dây không vào vùng giữa |
| `right_side_rope_layout.png` | Right, đứng — mirror, facing −X |
| `mirror_layout_test.png` | Right, crouch — mirror layout |

---

## 7. Điểm cần chủ dự án đánh giá

1. **Forward lean 41°** trông khá sâu (torso gần ngang). Đủ, hay giảm về ~35°?
2. **Slack 53%** là "split-squat" rộng fore/aft — hình thể chấp nhận được không, hay
   muốn dáng squat gọn hơn?
3. **Mapping** Right rope = forward / Left rope = backward — đúng ý? (đảo dấu 1 field).
4. **Lean bias** khi slack là +3.4° (chưa hẳn 0); còn lệch dư ~1.5° giữa fwd/back.
5. **Dây trước** đi sát cạnh đầu ~5 cm khi đứng — chấp nhận, hay cần tách xa hơn?
6. **Tay guard** còn mờ; head/cổ rủ hơi sâu ở slack.
7. **Camera framing** bên Right hơi lệch tâm.
8. **Foot slide range ±0.10 m** — có thể hơi nhỏ cho footwork (bước tới/lui) sau này.
9. **Multitouch** vẫn **chưa test trên thiết bị thật** (code EnhancedTouch chuẩn, đã
   test bàn phím/chuột qua MCP).

---

## 8. Không làm (đúng phạm vi)

weapon · combat · damage · HP · AI · multiplayer · matchmaking · Blender/model đẹp ·
skins · arena hoàn chỉnh · ranking · backend · UI đổi side.

---

**Files thay đổi:** `Assets/_Project/Scripts/Runtime/Prototype/` (PlayerSide.cs *mới*,
PuppetRig.cs, PuppetRopeController.cs, PuppetDebugHUD.cs), `Scripts/Editor/PuppetPrototypeBuilder.cs`,
`Scenes/PuppetPrototype.unity`, `Materials/Prototype/Puppet_Face.mat` *mới*, tài liệu này.
Không đụng `PROJECT_MASTER.md §2` (nguyên lý gameplay giữ nguyên).

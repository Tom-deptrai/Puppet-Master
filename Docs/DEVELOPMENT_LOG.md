# Development Log — Puppet Master

Nhật ký phát triển theo thời gian. Ghi các thay đổi kỹ thuật, quyết định và kết
quả kiểm thử ở mức chi tiết. Các quyết định ở tầm dự án thì cập nhật vào
[`PROJECT_MASTER.md`](PROJECT_MASTER.md).

Định dạng: mới nhất ở trên cùng.

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

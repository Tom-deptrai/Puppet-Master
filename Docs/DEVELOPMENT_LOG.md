# Development Log — Puppet Master

Nhật ký phát triển theo thời gian. Ghi các thay đổi kỹ thuật, quyết định và kết
quả kiểm thử ở mức chi tiết. Các quyết định ở tầm dự án thì cập nhật vào
[`PROJECT_MASTER.md`](PROJECT_MASTER.md).

Định dạng: mới nhất ở trên cùng.

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

# `Assets/_Project/`

Toàn bộ **code và asset của game Puppet Master** nằm trong thư mục này, tách biệt
hoàn toàn khỏi package của Unity và sample/template. Không đặt asset dự án ra
ngoài `_Project/`.

| Thư mục | Nội dung |
|---|---|
| `Art/` | Sprite, texture nguồn, concept, icon |
| `Audio/` | SFX, nhạc, audio mixer |
| `Materials/` | Material, shader graph |
| `Models/` | Model 3D (`.fbx` … → Git LFS), rig, animation |
| `Prefabs/` | Prefab (puppet, vũ khí, arena, UI…) |
| `Scenes/` | Scene. `Bootstrap.unity` là scene khởi động hiện tại |
| `Scripts/Runtime/` | Code chạy trong game — assembly `PuppetMaster.Runtime` |
| `Scripts/Editor/` | Công cụ/editor script — assembly `PuppetMaster.Editor` |
| `Settings/` | URP Render Pipeline asset (Mobile/PC), Renderer, Volume Profile |
| `Settings/Input/` | `InputSystem_Actions.inputactions` (Input System) |
| `UI/` | UI Toolkit / uGUI, layout, style |
| `VFX/` | Particle, VFX Graph, hiệu ứng va chạm |
| `Physics/` | Physic Material, cấu hình vật lý dùng chung |

Các thư mục rỗng giữ chỗ bằng `.gitkeep` — xoá khi đã có nội dung thật.

> Chưa có gameplay. Xem `Docs/PROJECT_MASTER.md` để hiểu định hướng, và
> `Docs/DEVELOPMENT_LOG.md` để biết trạng thái kỹ thuật hiện tại.

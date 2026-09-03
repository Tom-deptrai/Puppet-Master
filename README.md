# Puppet Master

> 1v1 physics puppet-fighting game for mobile (iOS + Android). Built with Unity 6 + URP.

Puppet Master là một game đối kháng vật lý 1 đấu 1 trên mobile. Mỗi người chơi
điều khiển một con rối được cấu tạo từ các khớp vật lý, chỉ bằng cách kéo/thả
hai sợi dây bằng hai ngón cái. Không có nút Attack/Block/Skill — mọi đòn đánh
đến từ physics, momentum và cách phối hợp hai dây.

Xem chi tiết định hướng tại [`Docs/PROJECT_MASTER.md`](Docs/PROJECT_MASTER.md).

---

## Trạng thái dự án

| | |
|---|---|
| Giai đoạn hiện tại | **Phase 0 — Technical foundation** (đã hoàn tất) |
| Unity | `6000.5.10f1` (Unity 6.5) |
| Render pipeline | Universal Render Pipeline (URP) |
| Nền tảng mục tiêu | iOS + Android (landscape, 60 FPS) |
| Input | Unity Input System (multitouch) |
| Gameplay | **Chưa phát triển** (theo đúng phạm vi Phase 0) |

## Yêu cầu môi trường

- macOS (Apple Silicon)
- Unity Editor `6000.5.10f1` kèm module **iOS Build Support** và **Android Build Support**
- Xcode (cho build iOS)
- Git + [Git LFS](https://git-lfs.com/) (bắt buộc trước khi commit asset nhị phân lớn)

## Mở project

1. Clone repo:
   ```bash
   git clone https://github.com/Tom-deptrai/Puppet-Master.git
   ```
2. Mở bằng Unity Hub → chọn đúng version `6000.5.10f1`.
3. Scene khởi động: [`Assets/_Project/Scenes/Bootstrap.unity`](Assets/_Project/Scenes/Bootstrap.unity).

## Cấu trúc thư mục

```
Assets/_Project/        # Toàn bộ code & asset của game (tách khỏi package/sample)
  Art  Audio  Materials  Models  Prefabs  Scenes
  Scripts/Runtime  Scripts/Editor
  Settings  Settings/Input
  UI  VFX  Physics
Packages/               # Khai báo package (manifest.json)
ProjectSettings/        # Cấu hình project
Docs/                   # Tài liệu dự án
```

## Tài liệu

- [`Docs/PROJECT_MASTER.md`](Docs/PROJECT_MASTER.md) — nguyên tắc thiết kế & kỹ thuật cốt lõi
- [`Docs/DEVELOPMENT_LOG.md`](Docs/DEVELOPMENT_LOG.md) — nhật ký phát triển theo giai đoạn
- [`Docs/AI_WORKFLOW.md`](Docs/AI_WORKFLOW.md) — quy trình phát triển dự án bằng AI

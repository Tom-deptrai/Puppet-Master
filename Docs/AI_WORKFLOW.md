# AI Workflow — Puppet Master

Dự án này được phát triển chủ yếu bằng AI (Claude Code / trợ lý lập trình).
Chủ dự án đóng vai trò **game director + người chơi thử**, không lập trình tay và
không dựng 3D tay. Tài liệu này mô tả cách làm việc để giữ dự án sạch và đúng hướng.

## 1. Nguồn sự thật

Thứ tự ưu tiên khi có mâu thuẫn:

1. **Yêu cầu mới nhất của chủ dự án** trong phiên làm việc hiện tại.
2. [`PROJECT_MASTER.md`](PROJECT_MASTER.md) — context/định hướng sống (living document).
3. [`DEVELOPMENT_LOG.md`](DEVELOPMENT_LOG.md) — trạng thái & quyết định gần đây.
4. Ý tưởng cũ hơn trong lịch sử chat / commit.

`PROJECT_MASTER.md` **không** phải spec khóa cứng. Ý tưởng cũ không tự động là
ràng buộc. Kết quả chơi thử được ưu tiên hơn lý thuyết.

## 2. Kỷ luật theo phase

- Mỗi phiên làm việc có **một phạm vi rõ ràng**. Làm đúng phạm vi đó.
- **Không** tự nhảy sang phase sau, **không** thêm tính năng tương lai chỉ vì
  `PROJECT_MASTER.md` có liệt kê.
- Dừng đúng ranh giới phase được yêu cầu và báo cáo, chờ chỉ đạo tiếp.
- Ưu tiên bản implement nhỏ nhất có thể kiểm thử được cho phase hiện tại.

Các mốc lớn (xem `PROJECT_MASTER.md` §12):
`Puppet feels good` → `Combat feels good` → `Presentable` → `Online` → `Release`.

## 3. Quy trình mỗi phiên

1. `git fetch` / `git pull` branch `main` để lấy bản mới nhất.
2. Đọc `PROJECT_MASTER.md` + phần mới nhất của `DEVELOPMENT_LOG.md`.
3. Xác nhận phạm vi công việc với chủ dự án nếu chưa rõ.
4. Thực hiện thay đổi nhỏ, tập trung.
5. Mở Unity kiểm tra: không có compile error, scene chạy, (nếu liên quan) build
   target iOS/Android chuyển được.
6. Ghi vào `DEVELOPMENT_LOG.md`: đã đổi gì, đã test gì, còn gì chưa chắc.
7. `git commit` với message rõ ràng, `git push` lên `main`.
8. Báo cáo ngắn gọn cho chủ dự án. Dừng ở ranh giới phase.

## 4. Ai làm gì

**AI được chủ động làm:**
- Thao tác kỹ thuật: cấu hình Unity, project settings, cấu trúc thư mục, script,
  package, git (add/commit/push theo yêu cầu), tài liệu.
- Cài công cụ **an toàn, tự động** khi thật sự cần (vd: `git-lfs` qua Homebrew).
- Sửa lỗi mình gây ra và kiểm tra lại.

**AI dừng lại và hỏi khi:**
- Cần quyền / mật khẩu / thao tác tay của chủ dự án (đăng nhập Apple/Google,
  ký app, tạo tài khoản, thanh toán…).
- Thay đổi khó đảo ngược hoặc ra bên ngoài (đổi repo, xoá dữ liệu, publish).
- Quyết định vượt phạm vi phase hiện tại hoặc mâu thuẫn với `PROJECT_MASTER.md`.

## 5. Ranh giới không được vượt (trừ khi có chỉ đạo mới rõ ràng)

- Không phát triển gameplay ngoài phạm vi phase đang làm.
- Không thêm multiplayer / networking khi prototype physics chưa chứng minh vui.
- Không thêm Firebase / backend / analytics sớm.
- Không thêm ads / IAP sớm.
- Không mua hoặc tải asset trả phí.
- Không tự ý đổi repository hoặc lịch sử git đã push.
- Không cài hàng loạt package "để dành".

## 6. Quy ước kỹ thuật

- Toàn bộ code & asset của game nằm trong `Assets/_Project/`.
- Code runtime → `Scripts/Runtime/` (assembly `PuppetMaster.Runtime`).
  Code editor → `Scripts/Editor/` (assembly `PuppetMaster.Editor`).
- Namespace gốc: `PuppetMaster`.
- Giữ serialization = Force Text để git diff/merge được.
- Asset nhị phân lớn (model/texture/audio) → Git LFS (đã cấu hình pattern sẵn).
- Mỗi thay đổi cấu hình project nên kèm một dòng trong `DEVELOPMENT_LOG.md`.

## 7. Unity MCP

- MCP server: lệnh `unity mcp` (Unity CLI chính thức).
- Cần Unity Editor **đang mở** project này để MCP thao tác trực tiếp lên Editor.
- Nếu tool MCP chưa xuất hiện trong phiên trợ lý: mở Editor rồi khởi động lại
  phiên trợ lý để nạp lại MCP.

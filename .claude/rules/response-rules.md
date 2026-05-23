# Response & Editing Rules

## 1. Trước khi xóa file
Luôn hỏi xác nhận trước khi xóa bất kỳ file nào, kể cả file tạm hay không dùng nữa.

## 2. Phong cách trả lời
- Ngắn gọn, đúng trọng tâm — không giải thích thừa, không tóm tắt lại những gì vừa làm
- Dùng markdown chỉ khi cần thiết (code block, bảng, danh sách)
- Không dùng emoji trừ khi được yêu cầu

## 3. Cập nhật docs
- Mỗi khi thêm feature mới hoặc thay đổi behavior của service, tạo/cập nhật file doc tương ứng.
```
./docs/<service-name>/<feature-name>.md

```

## 4. Chỉ được phép build khi thay đổi code. Không cần phải run thật.

## 5. Scope
 - Khi làm việc với service cụ thể, CHỈ đọc files trong folder đó.
 - Không cần đọc các service khác trừ khi được yêu cầu rõ ràng.

## 6. Comments in code: English only
- Mọi comment trong source code (`.cs`, `.ts`, `.tsx`, `.sql`, …) phải viết bằng **tiếng Anh** — bao gồm `//`, `/* */`, XML doc (`/// <summary>`), TODO, inline annotation.
- Áp dụng cho code mới và comment thêm vào file cũ. Không bắt buộc rewrite comment tiếng Việt có sẵn trừ khi đang edit chính đoạn đó.
- Trả lời và chat với user vẫn tiếng Việt như bình thường — quy tắc này chỉ áp dụng cho nội dung viết vào file source.
- Domain string cho user cuối (vd: `Error.Message` trong `Domain/Errors/*`, validation messages) KHÔNG phải comment — giữ theo convention hiện tại của project (thường tiếng Việt).
- File doc trong `docs/**/*.md` vẫn viết tiếng Việt như hiện nay.

**Ví dụ:**
- Thêm endpoint product-search vào Catalog → `./docs/catalog/product-search.md`
- Thêm consumer mới như sync-product-info ở Search → `./docs/search/sync-product-info.md`

**Doc cần có tối thiểu:**
- Mục đích / chức năng
- Các endpoint hoặc event liên quan
- Bất kỳ config / env variable nào cần thiết

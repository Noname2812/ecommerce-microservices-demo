# Response & Editing Rules

## 1. Trước khi xóa file
Luôn hỏi xác nhận trước khi xóa bất kỳ file nào, kể cả file tạm hay không dùng nữa.

## 2. Phong cách trả lời
- Ngắn gọn, đúng trọng tâm — không giải thích thừa, không tóm tắt lại những gì vừa làm
- Dùng markdown chỉ khi cần thiết (code block, bảng, danh sách)
- Không dùng emoji trừ khi được yêu cầu

## 3. Cập nhật docs
Mỗi khi thêm feature mới hoặc thay đổi behavior của service, tạo/cập nhật file doc tương ứng:

```
./docs/<service-name>/<feature-name>.md
```

**Ví dụ:**
- Thêm endpoint product-search vào Catalog → `./docs/catalog/product-search.md`
- Thêm consumer mới như sync-product-info ở Search → `./docs/search/sync-product-info.md`

**Doc cần có tối thiểu:**
- Mục đích / chức năng
- Các endpoint hoặc event liên quan
- Bất kỳ config / env variable nào cần thiết

# RabbitMQ (docker-compose)

Broker **không** dùng `definitions.json` import: **MassTransit** tạo exchange, queue, binding khi ứng dụng chạy (`SetKebabCaseEndpointNameFormatter` → tên queue kiểu `order-created` từ consumer class, v.v.).

Nếu cần tên queue/exchange cố định (ví dụ trùng tài liệu `order.events`), cấu hình tường minh trong `ReceiveEndpoint` / publish topology — không khai báo trùng trong file JSON để tránh “dead” queue.

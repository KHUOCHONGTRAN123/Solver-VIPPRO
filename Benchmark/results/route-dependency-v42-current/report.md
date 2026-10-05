# Báo cáo tối ưu RouteClearing — 299/299 đã kiểm chứng

Đã giải thành công 299/299 level mẫu bằng cùng phiên bản solver; mỗi level không quá 100.000 lần mở rộng trạng thái và RAM đỉnh 2 GiB. Đủ 299 lời giải đã qua guard, replay và kiểm tra trạng thái cuối.

Đã replay đủ 299 lời giải: hết cat, tất cả hole chơi được hoàn tất và hết hàng đợi box/tower. completion-audit.json xác nhận ngân sách, phiên bản nguồn và phạm vi kiểm chứng.

Ngân sách đánh giá: 100.000 lần mở rộng trạng thái và RAM đỉnh 2 GiB mỗi level. Không dùng thời gian làm tiêu chí dừng solver. Các lượt accelerator có giới hạn tính toán rồi chuyển sang tìm kiếm đầy đủ.

Trên cùng 299 level: baseline 45133822 lần mở rộng; bản mới 914398 lần. RAM đỉnh bản mới: 121102336 byte. Các counter baseline của level bị dừng là cận dưới, không phải chi phí giải thành công.

Thay đổi: giữ mục tiêu hole/vị trí ăn cụ thể; kiểm tra chỗ nhường đường; mở rộng phụ thuộc qua các hole chắn đường nhường; tính yêu cầu vùng cần dọn riêng cho từng hole; kiểm tra sức chứa và vòng phụ thuộc cat–box–layer trước khi chốt nước ăn. Giữ tìm kiếm dự phòng và không quay lui qua nước ăn đã chốt.

Hạn chế: các mô hình khả thi là nới lỏng; pass không chứng minh thắng cuối cùng. Lập kế hoạch đường cho linked target còn dựa vào fallback. Số lần mở rộng không phản ánh đầy đủ số thao tác bên trong từng lần; RAM đo theo peak working set của worker. Bộ 299 level mẫu không chứng minh khả năng trên mọi level.

Chi tiết từng level và nguồn chạy: comparison.csv, metrics.csv. Kết quả thiếu và source hash: summary.json. Audit: guard-audit.json. Chỉ xác nhận hoàn thành sau khi đủ 299 kết quả cùng source, trong ngân sách, replay hợp lệ và kiểm tra final state.

Cải thiện bổ sung: tìm kiếm theo lớp giữ phương án của nhiều nhóm hole; tìm kiếm hàng đợi ưu tiên cho giai đoạn còn tối đa 4 hole; bộ đệm nước đi có giới hạn; lượt ưu tiên đường ăn cat mang chìa khóa khi các lượt thường chưa tìm được nước ăn và còn hole khóa.

Mục tiêu tiếp theo: giảm chi phí các level nặng nhất dựa trên comparison.csv; cải thiện dự đoán phụ thuộc hole liên kết; bổ sung level ngoài tập mẫu để kiểm tra tính tổng quát. Giữ replay và ngân sách làm điều kiện bắt buộc cho mỗi thay đổi.

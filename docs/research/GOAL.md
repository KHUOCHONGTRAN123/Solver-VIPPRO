# Goal nghiên cứu solver — cập nhật 2026-10-06

Tài liệu này bổ sung điều kiện nghiệm thu vào goal hiện có theo yêu cầu người dùng: thời gian giải mỗi level dưới 1 giây. Giữ nguyên toàn bộ phạm vi và yêu cầu ban đầu.

## Phạm vi

Chỉ nghiên cứu và benchmark 21 level có expanded > 10.000 trong benchmark phát hành 1.0.0: Level00269, Level00206, Level00233, Level00117, Level00246, Level00267, Level00274, Level00101, Level00289, Level00239, Level00259, Level00268, Level00100, Level00213, Level00184, Level00149, Level00176, Level00219, Level00186, Level00225, Level00216.

## Điều kiện hoàn thành

Mỗi level trong cả 21 level phải đồng thời đáp ứng:

- Status Solved và lời giải replay hợp lệ theo mechanic, gồm kiểm tra bằng engine v42 độc lập.
- Expanded < 10.000, cộng dồn toàn lần giải theo cùng cách đếm của bản 1.0.0. Không đổi cách đếm hoặc trả về chưa giải khi hết ngân sách để đạt ngưỡng.
- Median solveTimeMs < 1.000 ms: build Release, cùng máy và runtime, chạy tuần tự, một lượt warm-up chính level đó rồi ba lượt đo. Cả ba lượt phải Solved, replay hợp lệ và expanded < 10.000.

Thời gian giải gồm parsing, validation, precompute, search và replay nội bộ; không gồm file I/O, serialize kết quả hoặc audit độc lập bên ngoài. Lưu cả ba mẫu, không chọn mẫu nhanh nhất. Đo baseline bằng cùng quy trình để so sánh công bằng.

Được phép thay đổi nước đi và thứ tự tìm kiếm so với v42. Ghi lại baseline, kết quả từng level, thời gian, chất lượng lời giải và thay đổi thuật toán. Chỉ hoàn thành khi 21/21 level thỏa toàn bộ điều kiện.

Goal đã được tạo lại và active theo yêu cầu người dùng: tiếp tục chạy nghiên cứu với cả hai ngưỡng.

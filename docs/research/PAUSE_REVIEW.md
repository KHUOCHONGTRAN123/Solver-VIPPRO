# Đánh giá khi tạm dừng — 2026-10-07

Người dùng yêu cầu thử xong mô hình bốn nhóm rồi pause goal. Mục tiêu gốc giữ nguyên: một thuật toán chung đạt 21/21 Solved, replay v42 đúng, expanded <10.000 mọi mẫu, median solveTimeMs <1.000 ms; Release tuần tự, warm-up từng level rồi ba mẫu.

## Mốc đã kiểm chứng

| Level | Expanded mỗi mẫu | Median ms | Biến thể có chứng cứ |
|---|---:|---:|---|
| Level00149 | 7.770 | 882,24 | distance-cache-acceptance |
| Level00176 | 5.586 | 524,95 | dependency-beam-176 |
| Level00269 | 2.096 | 213,17 | pattern-tier-acceptance |
| Level00274 | 9.585 | 923,28 | distance-cache-acceptance |

Các mốc trên đều có warm-up Solved, ba mẫu Solved/replay đúng, hash DLL/level/plan và lời giải. Đây là bốn level đạt riêng lẻ qua các biến thể; không phải bốn level cùng đạt trên một binary. Bản distance-cache đã nghiệm thu hai level 149/274; các biến thể có lợi cho 176 gây hồi quy ở các level khác. 17 level còn lại chưa có nghiệm thu đầy đủ đạt cả hai ngưỡng trong bộ dữ liệu nghiên cứu. verified-milestones.json là danh sách kiểm tra từ các summary, không thay nghiệm thu một phiên bản chung.

Baseline DLL 1.0.0 đã hoàn tất 21 level, warm-up riêng + ba mẫu, 63/63 Solved và replay đúng. Bộ screening toàn 21 của pattern-tier có 19 Solved/replay đúng, hai Cancelled; bảy mẫu đạt cả hai ngưỡng. Screening một mẫu không phải nghiệm thu.

## Thử nghiệm cuối

Mô hình bốn nhóm với miền nén vẫn không tìm lần ăn tiếp theo trong 10.000 trạng thái ở hai fixture stalled của 206/233. Bốn level đại diện đã đo một mẫu; 269/274 đạt, 149 quá 1 giây, 176 quá cả hai ngưỡng. Đã tắt mô hình nghiên cứu và build lại cache-tier. Không để tiến trình nghiên cứu chạy tiếp khi pause.

## Đánh giá tính khả thi

- Dưới 10.000 trạng thái là khả thi cho một phần bộ level, đã có chứng cứ trực tiếp. Chưa có cơ sở cam kết đạt toàn bộ 21 nhanh: hai pha khó của 206/233 vẫn vượt ngân sách với nhiều phương pháp và chưa xác định được dead-end hay search chưa đủ mạnh.
- Dưới 1 giây cũng đã đạt ở một số level. Nó khó hơn mục tiêu số trạng thái vì parsing, validation, bảng heuristic, kiểm tra continuation và replay đều tính vào thời gian. Ví dụ mô hình cuối 149 chỉ 6.864 trạng thái nhưng 1.099 ms. 149/274 ở các bản đã nghiệm thu cũng gần ngưỡng, nên thay đổi nhỏ có thể làm hồi quy.
- Không thể cam kết ít thời gian nghiên cứu để đạt mục tiêu kép 21/21. Các thay đổi vừa qua thường cải thiện một nhóm và làm nhóm khác tệ hơn. Đánh giá hiện tại: mục tiêu toàn bộ 21 dưới cả hai ngưỡng còn cần nghiên cứu đáng kể, không phải một đợt tối ưu ngắn có thể hứa chắc kết quả.

Nếu ưu tiên sớm có tool tạo level, có thể cân nhắc dùng thời gian dưới 1 giây làm tiêu chí lọc level mới sinh, và dùng bộ 21 để theo dõi cải tiến dần. Đây chỉ là đề xuất để người dùng quyết định mục tiêu mới; goal hiện tại chưa bị thu hẹp hoặc đánh dấu hoàn thành.

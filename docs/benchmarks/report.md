# Solver 1.0 — kết quả kiểm chứng và hiệu năng

Đủ 299 level giữ cùng status, nước đi, event ăn và counter v42; mỗi mẫu được replay và kiểm tra trạng thái cuối. Ba mẫu Release cho mỗi level/mode sau warm-up; worker chạy tuần tự.

39 level bị đánh dấu trong lượt đầu được đo lại với một lượt warm-up chính level đó cho cả hai bản. CSV ghi loại warm-up từng level; số liệu ban đầu và danh sách đo lại được giữ trong Verification/results. Tổng dưới đây cộng các median đã chọn theo quy tắc này, không phải thời gian chạy batch liên tục.

6 level vẫn bị đánh dấu được kiểm tra thêm với ba lượt warm-up cùng level và chờ JIT nền500ms, sau đó lấy ba mẫu cho mỗi bản. Không chọn mẫu nhanh nhất.

Tổng các median thời gian: 532.932 s → 199.801 s, nhanh hơn 2.67 lần.
Tổng các median allocation: 263,662,645,032 → 33,212,453,672 byte, giảm 87.40%.
Tổng mở rộng: 914.398, tối đa 82,130/level. RAM đỉnh worker tối đa 164.27 MiB.

Median thời gian một level: 213.191 → 138.108 ms; p95 của median299level: 7468.471 → 2416.960 ms.

## Máy và phạm vi đo

CPU: Intel(R) Core(TM) i5-10400F CPU @ 2.90GHz; 12 logical processors; RAM 15.92 GiB; Microsoft Windows 11 Pro 10.0.26200; SDK 10.0.302; Release; ngày 2026-10-06 (Asia/Bangkok).

`solveTimeMs` gồm parsing, validation, precompute, search và replay nội bộ; không gồm file I/O, serialize kết quả hoặc audit ngoài solver. Peak RAM gồm toàn worker/warm-up/audit, là phép đo bảo thủ. Với 3 mẫu, p95 từng level là mẫu lớn nhất. Chi tiết GC và từng level nằm trong performance.csv.

## Các level chậm nhất sau tối ưu

| Level | Trước (median ms) | Sau (median ms) | Allocation sau (byte) |
|---|---:|---:|---:|
| Level00233 | 78341.372 | 30243.143 | 4,136,713,720 |
| Level00206 | 83488.776 | 21266.268 | 5,434,077,816 |
| Level00274 | 56152.723 | 17290.345 | 1,722,168,624 |
| Level00246 | 34665.247 | 13455.302 | 2,465,973,880 |
| Level00239 | 26370.885 | 7522.453 | 1,191,701,248 |
| Level00267 | 19485.490 | 6576.403 | 761,155,208 |
| Level00289 | 14547.559 | 6022.877 | 1,015,186,152 |
| Level00117 | 20384.991 | 5894.813 | 826,914,392 |
| Level00149 | 13275.253 | 5060.975 | 546,881,184 |
| Level00268 | 10114.713 | 4422.291 | 1,132,704,504 |

## Hồi quy cần rà soát

Level00021, Level00217

Điều kiện nghiệm thu theo kế hoạch áp dụng cho nhóm level nặng: ngưỡng p90 median baseline là 2240.320ms; một hồi quy có thời gian trước hoặc sau trên ngưỡng này sẽ chặn nghiệm thu. Còn 2 hồi quy ở level ngắn, 0 ở nhóm nặng. Các trường hợp ngắn được giữ công khai trong CSV và là mục tiêu cải thiện tiếp theo.

Cải thiện chính: mask64/128bit với fallback chính xác; checkpoint mechanics thay deep-copy; buffer clearance/Dijkstra tái sử dụng; cache rank/successors theo phase và state; heap giữ serial tie-break; bỏ snapshots/nhánh cấu hình thừa khỏi sản phẩm. Các tối ưu chỉ thay chi phí thực thi, không thay thứ tự thuật toán.

Bộ299level không chứng minh khả năng trên mọi level mới. Thời gian phụ thuộc máy/runtime. Mục tiêu tiếp theo: giảm chi phí các level đầu bảng, mở rộng tập level ngoài mẫu và tiếp tục dùng differential/replay làm điều kiện bắt buộc.

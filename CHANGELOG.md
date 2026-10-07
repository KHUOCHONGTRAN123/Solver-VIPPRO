# Changelog

## 2.0.0

- Chạy solver 1.0.0 tối đa 10.000 full-state expansions; khi chưa giải xong thì chạy solver cải tiến từ đầu level. Không chọn thuật toán bằng thời gian hoặc ID level.
- Giữ thứ tự moves và counter 1.0.0 cho level giải trong ngân sách. Lời giải chưa hoàn tất của lượt baseline không ghép vào lời giải cải tiến.
- `expanded` và `solveTimeMs` bao gồm cả hai lượt. Thêm `searchAlgorithm`, `baselineExpanded`, `improvedExpanded`; `algorithmVersion` là `2.0.0`.
- Thêm target corridor, relaxed pair/triple pattern ordering và cache cạnh/khoảng cách; giữ successor và replay mechanic chính xác, cancellation và lời gọi đồng thời.
- Dữ liệu benchmark riêng cho nhóm dễ, thử ngưỡng 5.000 và hybrid 10.000. Phương án 5.000 không được chọn.
- Không cam kết tổng hybrid <30.000 trạng thái/<3 giây. Hai level 206/233 còn là giới hạn đã biết; chưa chứng minh giải nhanh trên mọi level mới.

## 1.0.0

- Release solver v42, API .NET Standard 2.1, CLI .NET 10 và bundle Unity.

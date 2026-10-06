# Kiểm chứng Solver 1.0

`Verification/V42Reference/` là mã v42 đóng băng chỉ dành cho kiểm chứng. Các chiến lược cũ tồn tại trong archive này để làm oracle; chúng không được tham chiếu bởi thư viện hoặc CLI sản phẩm.

`Verification/V42Results/` giữ lời giải và audit v42. Bộ kiểm chứng so sánh status, từng move/đường đi/event ăn và counter `expanded`, rồi replay bằng engine v42 với kiểm tra các quy tắc khả thi. Trạng thái cuối phải hết cat và hàng đợi box/tower.

```powershell
dotnet build Verification/Verification.csproj -c Release
dotnet Verification/bin/Release/net10.0/Solver.Verification.dll tests .
dotnet Verification/bin/Release/net10.0/Solver.Verification.dll all .
```

`all` chạy mỗi level theo thứ tự, worker riêng cho reference rồi optimized, ba mẫu mỗi mode sau warm-up. Hash của binary, level và lời giải tham chiếu được ghi vào từng kết quả. Chỉ tiếp tục kết quả cũ khi provenance và đủ ba mẫu khớp. RAM được giám sát từ process bên ngoài; quá 2 GiB thì worker bị dừng. Callback kiểm chứng hủy khi counter vượt 100.000, không thay đổi API sản phẩm.

`solveTimeMs` đo parse tới kết quả, không gồm serialize/audit hậu kiểm. `allocatedBytes` dùng `GC.GetAllocatedBytesForCurrentThread` trên worker đồng bộ; GC0/1/2 được chụp ngay sau phần đo. Peak working set là RAM đỉnh cả worker, gồm warm-up và hậu kiểm, nên là phép đo bảo thủ.

CSV hiệu năng ghi thời gian median/p95 từng level trước/sau, allocation, GC và RAM. Với ba mẫu, p95 theo nearest-rank là mẫu lớn nhất; không dùng ba mẫu để suy luận khoảng tin cậy. Các level tăng trên 10% và ít nhất 2 ms median được đánh dấu cần rà soát, tránh coi nhiễu ở level cực ngắn là hồi quy đáng kể.

`Verification/Recheck.py` đo lại các level được đánh dấu sau lượt chính. Cả reference và optimized được warm-up một lần bằng chính level đó rồi lấy ba mẫu. Số liệu đầu được giữ ở `Verification/results/initial-measurements/`, danh sách ở `recheck.json`; CSV ghi rõ loại warm-up. Việc chọn đo lại dựa trên quy tắc cố định, không chọn mẫu nhanh nhất.

`SteadyRecheck.py` kiểm tra các hồi quy còn lại: ba lượt warm-up cùng level, chờ JIT nền 500 ms trong harness rồi lấy ba mẫu. Số liệu lượt trước được giữ ở `first-recheck/`. Khoảng chờ này không nằm trong solver hoặc thời gian solve.

Kế hoạch yêu cầu không có hồi quy lặp lại đáng kể ở level nặng. Báo cáo dùng p90 thời gian median baseline làm ngưỡng nhóm nặng; hồi quy có thời gian trước hoặc sau trên ngưỡng này sẽ chặn nghiệm thu. Hồi quy ở level ngắn vẫn được ghi đầy đủ, không che khỏi kết quả hoặc mục tiêu tiếp theo.

Số liệu thô cuối và các lượt đo lại được lưu trong `docs/benchmarks/raw-measurements.zip`; báo cáo CSV, summary và integration được giữ cạnh đó. Project consumer NuGet cần restore từ `artifacts/nuget` sau khi đóng gói. Unity được kiểm tra bằng Editor Mono 2022.3; chưa kiểm tra build player IL2CPP.

Chỉ xóa dữ liệu thử nghiệm cũ khi đủ299 level pass, cùng binary sản phẩm, tổng914.398 mở rộng, từng level dưới100.000 và2GiB, cùng trạng thái cuối, test integration và báo cáo hiệu năng đạt.

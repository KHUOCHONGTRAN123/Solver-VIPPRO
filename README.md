# CatDom Solver 1.0

Thư viện C# và CLI giải level game Cat với thuật toán v42 cố định. Chỉ cần gửi JSON level, không cần settings. Thư viện nhắm .NET Standard 2.1 cho Unity/.NET; CLI chạy .NET 10 hoặc bản Windows x64 tự chứa runtime.

```csharp
using CatDom.CoreSolver;
var result = CatLevelSolver.SolveLevel(levelJson);
```

Output gồm `algorithmVersion`, `status`, `message`, `expanded`, `solveTimeMs`, `moves`. Thời gian đo bằng Stopwatch gồm parsing tới kết quả/replay, không gồm file I/O hoặc serialize output. Có thể hủy bằng CancellationToken; thời gian không phải tiêu chí dừng.

```powershell
cat-solver.exe level.json --output result.json
Get-Content -Raw level.json | cat-solver.exe -
```

## Build và kiểm chứng

Cần .NET SDK 10.

```powershell
dotnet build Solver.slnx -c Release
dotnet Verification/bin/Release/net10.0/Solver.Verification.dll tests .
dotnet Verification/bin/Release/net10.0/Solver.Verification.dll all .
```

Kiểm chứng differential so với v42 đóng băng: 299 level phải giữ status, nước đi, event ăn và counter; mọi lời giải được replay. Benchmark đo ba mẫu từng level/mode, allocation, GC và RAM. Chi tiết kết quả nằm trong `docs/benchmarks/`.

## Phát hành

Sau khi acceptance đạt:

```powershell
& Tools/Package.ps1
```

Gói tạo trong `artifacts/`: NuGet `CatDom.Solver.1.0.0.nupkg`, ZIP CLI Windows x64, ZIP bộ DLL Unity và checksums. NuGet/ZIP được tạo local, không tự đăng lên dịch vụ công khai.

[API và Unity/CLI](docs/API.md) · [Cách kiểm chứng](docs/VERIFICATION.md) · [Báo cáo hiệu năng](docs/benchmarks/report.md)

Mã sản phẩm chỉ gồm engine v42 đã thu gọn. Mã v42 đầy đủ ở `Verification/V42Reference` là archive/oracle, không được đóng vào thư viện hoặc CLI. Tập299level không chứng minh khả năng trên mọi level mới.

Nghiệm thu trên máy đo: 299/299, tổng 914.398 mở rộng; tổng median thời gian 532,93 → 199,80 giây, allocation giảm 87,40%, RAM đỉnh 164,27 MiB. Hai level ngắn21 và217 còn hồi quy thời gian; nhóm nặng không có hồi quy đáng kể. Chi tiết và phạm vi đo nằm trong báo cáo, không phải cam kết thời gian trên máy khác.

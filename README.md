# Solver VIPPRO

C# solver cho các level game Cat. Mã solver nằm trong `IndependentSolver/Core/`, dữ liệu 299 level mẫu nằm trong `Levels/`.

Bản v42 đã giải và replay hợp lệ toàn bộ 299 level mẫu, với giới hạn 100.000 lần mở rộng trạng thái và RAM đỉnh 2 GiB/level. Tổng chi phí 914.398 lần mở rộng; level nặng nhất 82.130; RAM đỉnh 121.102.336 byte. Báo cáo và số liệu nằm trong `Benchmark/results/route-dependency-v42-current/`.

RouteClearing tìm đường ăn và dọn các hole chắn đường, giữ tìm kiếm dự phòng, không quay lui qua nước ăn đã chốt và không dùng thời gian làm tiêu chí dừng solver.

## Build và kiểm thử

Cần .NET SDK 10 và PowerShell.

```powershell
dotnet build Benchmark/Benchmark.csproj -c Release -o Benchmark/local-bin
dotnet Benchmark/local-bin/Benchmark.dll route-tests
```

Chạy lại 299 level với ngân sách tính toán/RAM:

```powershell
& Benchmark/Run-BoundedRoute.ps1 -Binary "$PWD/Benchmark/local-bin/Benchmark.dll" -Run route-local -Levels @(1..299) -MaxCalculations 100000 -MaxRamBytes 2147483648
```

File build và các kết quả trung gian được bỏ qua trong Git. Kết quả trên tập mẫu không đảm bảo khả năng giải mọi level ngoài tập này.

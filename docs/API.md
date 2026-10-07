# Solver 2.0

Solver 2.0.0 chạy thuật toán 1.0.0 với ngân sách 10.000 trạng thái full-state. Nếu cần mở rộng thêm trạng thái trước khi giải xong, solver bỏ lời giải chưa hoàn tất và chạy thuật toán cải tiến từ đầu level. Không dùng thời gian để chuyển thuật toán. Thư viện không phụ thuộc Unity, nhắm .NET Standard 2.1. CLI nhắm .NET 10; bản Windows x64 tự chứa runtime.

## Gọi thư viện

```csharp
using CatDom.CoreSolver;
using Newtonsoft.Json;

var result = CatLevelSolver.SolveLevel(levelJson);
string outputJson = JsonConvert.SerializeObject(result);
```

`levelJson` có định dạng như các file trong `Levels/`: `sizeX`, `sizeY`, `holeInfos`, `catInfos`, các box, layer, gate, link và mechanics tương ứng. Catalog shape được nhúng, không cần cung cấp cấu hình hoặc file phụ.

Có thể truyền `CancellationToken` để hủy. Mỗi lần gọi sở hữu toàn bộ trạng thái mutable; các lời gọi độc lập có thể chạy đồng thời. Input JSON không bị thay đổi.

## Output

Output có chín trường:

| Trường | Ý nghĩa |
|---|---|
| `algorithmVersion` | `2.0.0` |
| `searchAlgorithm` | `baseline` hoặc `baseline+improved` |
| `baselineExpanded` | Chi phí lượt thuật toán cũ, tối đa 10.000 |
| `improvedExpanded` | Chi phí lượt thuật toán cải tiến; bằng 0 nếu không chuyển |
| `status` | `Solved`, `NoNextCatReachable`, `InvalidInput`, `UnsupportedMechanics`, `Cancelled` |
| `message` | Thông tin kết quả hoặc lỗi |
| `expanded` | Tổng `baselineExpanded + improvedExpanded`, gồm cả lượt thử thất bại |
| `solveTimeMs` | Thời gian giải thực tế, số thực đơn vị mili giây |
| `moves` | Chuỗi nước đi theo thứ tự; mỗi move gồm `holeId`, `start`, `path`, `eaten` |

Ví dụ cấu trúc output (số liệu chỉ minh họa):

```json
{
  "algorithmVersion": "2.0.0",
  "searchAlgorithm": "baseline",
  "baselineExpanded": 8,
  "improvedExpanded": 0,
  "status": "Solved",
  "message": "All playable holes finished and cats cleared.",
  "expanded": 8,
  "solveTimeMs": 1.25,
  "moves": []
}
```

`path` giữ các cell đi qua, gồm cell bắt đầu. `eaten` giữ `holeId`, `color`, `catId`, `boxId`, `boxIndex`; giá trị `-1` biểu thị nguồn không tương ứng. Engine giữ thứ tự tự ăn ban đầu của v42.

### Phạm vi đo thời gian

`solveTimeMs` dùng `Stopwatch` từ lúc API nhận chuỗi JSON tới khi có kết quả. Bao gồm parse, validation, catalog/precompute cần thiết, tìm kiếm, replay nội bộ và tạo output; không gồm đọc file, khởi động CLI, serialize hoặc ghi JSON output.

Trường thời gian được trả cả khi input lỗi, mechanics không hỗ trợ hoặc bị hủy. Lần gọi đầu có thể gồm khởi tạo catalog/JIT nên chậm hơn lần gọi đã warm-up. Thời gian phụ thuộc máy, runtime và level; số liệu benchmark không phải cam kết thời gian cố định.

Solver không đặt deadline hoặc giới hạn tổng 100.000 bên trong. Ngân sách 10.000 chỉ giới hạn lượt baseline, không giới hạn lượt cải tiến. Nếu giải xong ngay ở trạng thái thứ 10.000, giữ lời giải baseline; chỉ chuyển khi cần trạng thái tiếp theo. `CancellationToken` vẫn hủy cả quá trình và giữ chi phí đã dùng. `NoNextCatReachable` nói về trạng thái đã chốt, không khẳng định không tồn tại một thứ tự ăn khác.

Ngưỡng 30.000/<3 giây trong nghiên cứu 19 level áp dụng cho lượt cải tiến độc lập, không phải tổng hybrid. Hybrid cộng thêm tối đa 10.000 trạng thái và thời gian baseline. Level00206/00233 chưa được chứng minh đạt chi phí/thời gian; fallback không bảo đảm mọi level đều giải nhanh. Version 2.0 thêm trường JSON và thay thuật toán/plan của nhóm vượt ngân sách; consumer không nên yêu cầu output chỉ có sáu trường như 1.0.0.

## CLI

```powershell
cat-solver.exe level.json
cat-solver.exe level.json --output result.json
Get-Content -Raw level.json | cat-solver.exe -
```

Không truyền đường dẫn thì CLI đọc một JSON level từ stdin. Stdout chỉ chứa JSON kết quả, trừ `--help`. Lỗi đọc/ghi file và sai cú pháp CLI được ghi ra stderr.

Mã thoát: 0 giải thành công; 1 không tìm được nước ăn tiếp theo; 2 input/CLI không hợp lệ hoặc mechanics không hỗ trợ; 130 bị hủy. Ctrl+C gửi cancellation, không đổi thuật toán.

## Unity

Chọn API Compatibility Level .NET Standard 2.1. Import `IndependentSolver.dll` và Newtonsoft.Json 13.0.3 tương thích; nếu project đã có Newtonsoft.Json tương thích thì không import thêm DLL trùng tên.

Thư viện không gọi Unity API. Với level nặng, đọc JSON trên main thread rồi chạy `SolveLevel` bằng `Task.Run`; chỉ cập nhật GameObject/UI trở lại main thread. Hủy bằng `CancellationTokenSource` khi scene hoặc tác vụ không còn cần kết quả.

```csharp
string json = levelAsset.text;
var result = await System.Threading.Tasks.Task.Run(
    () => CatLevelSolver.SolveLevel(json, cancellationToken));
```

NuGet package: `CatDom.Solver` 1.0.0. Trên .NET, package tự khai báo dependency Newtonsoft.Json; Unity có thể sử dụng bộ DLL trong `artifacts/unity/`.

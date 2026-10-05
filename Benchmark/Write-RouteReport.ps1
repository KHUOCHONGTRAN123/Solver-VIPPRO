param([string]$Run = 'route-final')
$ErrorActionPreference = 'Stop'
$folder = Join-Path $PSScriptRoot ('results/' + $Run)
$summary = Get-Content (Join-Path $folder 'summary.json') -Raw | ConvertFrom-Json
$replay = @(Get-Content (Join-Path $folder 'replay-audit.json') -Raw | ConvertFrom-Json)
if ($summary.total -ne 299 -or $replay.Count -ne 299) { throw 'Full299 summary and independent replay required' }
if (@($replay | Where-Object { $_.status -eq 'Solved' -and $_.replay -ne 'Solved' }).Count) { throw 'Replay audit failed' }
$solved = [int]$summary.counts.Solved
$percent = [math]::Round(100 * $solved / 299, 2)
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Base RouteClearing: kết quả 299 level')
$lines.Add('')
$lines.Add("Solver mới giải $solved/299 level ($percent%). Baseline giữ nguyên: 246/299; khôi phục $($summary.recovered) level baseline chưa giải được, hồi quy $($summary.regressions) level.")
$lines.Add('')
$lines.Add('## Luồng giải và những phần đã cải thiện')
$lines.Add('')
$lines.Add('Ở mỗi trạng thái sau lần ăn, kiểm tra mục tiêu khả thi bằng ComputeRelaxedGroupTarget với footprint, linked group, trục di chuyển, gate và hole bất động. RoutePolicy xếp hạng bố trí bằng chi phí đường và áp lực lên các mục tiêu khác; OccupancyPathDistance và LinkedRouteDistance hướng tìm kiếm đến hole có thể ăn và các vật cản cần dọn. GenerateUncached sinh nước đi theo mechanic đầy đủ. Chốt lần ăn tìm được rồi giải trạng thái tiếp theo, không quay lại quyết định ăn trước.')
$lines.Add('')
$lines.Add('Tìm chuỗi ngắn đến độ sâu 6 rồi chuyển sang DFS đầy đủ có visited và stack tường minh. Mốc 6 chỉ đổi cách duyệt; không giới hạn khả năng giải. Giữ toàn bộ lựa chọn bố trí hợp lệ, không cắt endpoint/beam. Các frame nhả danh sách nước đi anh em, tái sinh khi quay lại để giảm RAM. CompactParkingChain dùng nước đi hợp lệ để rút gọn đường đỗ hole trước khi chốt lần ăn. Hết đồ thị bố trí có thể tới mới báo NoNextCatReachable; người gọi vẫn có thể hủy bằng cancellation token.')
$lines.Add('')
$lines.Add('Các hướng cũ được giữ để đối chiếu: SearchStrategy gồm Default, SpaceFirst, Backtracking và RouteClearing. NextCatSearch gồm ShortestSequence, FirstProgress, IterativeDeepening, ProgressiveFirst, BestFirst, Beam. GoalAggregation gồm Minimum, Sum, Bottleneck, ReachableSum, ReachableBottleneck, BottleneckPlusSum, ReachableBottleneckPlusSum; còn các tùy chọn adaptive model, focused goal/path, endpoint parking và decision backtracking. Đợt đo này dùng CreateRouteClearingDefault với short-pass FirstProgress rồi DFS, không dùng các nhánh adaptive hay quay lui quyết định ăn.')
$lines.Add('')
$lines.Add('## Chi phí')
$lines.Add('')
$lines.Add('Bốn level189,206,233,246 được đánh dấu Cancelled/HighCostStopped theo yêu cầu dừng benchmark quá tốn tài nguyên. Worker không còn chạy khi kiểm tra cuối; không có số đo peak cuối. Expanded là cận dưới từ checkpoint cuối, RAM là cận dưới working set đã quan sát. Các tổng và phân vị dưới đây có trộn bốn số liệu chưa hoàn chỉnh này; không coi chúng là chi phí hoàn tất hay bằng chứng vô nghiệm. Các nước đi của bốn level là kế hoạch dở dang.')
$lines.Add('')
$lines.Add("Tổng số trạng thái mở rộng: $($summary.calculationsTotal); median $($summary.calculationsMedian), P95 $($summary.calculationsP95), lớn nhất $($summary.calculationsMax). Peak RAM từng level: median $($summary.ramMedianMiB) MiB, P95 $($summary.ramP95MiB) MiB, lớn nhất $($summary.ramMaxMiB) MiB.")
$lines.Add('')
$lines.Add("Trên $($summary.commonSolved) level cả hai bản cùng giải được: baseline mở rộng $($summary.commonBaselineCalculations) trạng thái, bản mới $($summary.commonNewCalculations). Baseline có ngân sách 10 giây và độ sâu 12; bản mới không có ngân sách thời gian hay trần độ sâu để bỏ cuộc, nên đây là so sánh kết quả dưới hai cấu hình khác nhau.")
$lines.Add('')
$lines.Add('Số lần tính toán quy ước là expanded states, gồm các lượt tìm ngắn lặp lại, tái sinh frame và rút gọn kế hoạch; không phải số phép toán CPU. Mỗi level chạy trong tiến trình mới. RAM là PeakWorkingSet64 sau Solve/replay và thu thập chẩn đoán, trước serialize kết quả cuối; bao gồm runtime. Baseline cũ không đo RAM. Không dùng thời gian để đánh giá chi phí.')
$lines.Add('')
$lines.Add('| Level tốn tính toán | Trạng thái mở rộng | Peak RAM MiB | Nước đi |')
$lines.Add('|---|---:|---:|---:|')
foreach ($row in $summary.expensiveLevels) { $lines.Add("| $($row.level) | $($row.calculations) | $($row.ramMiB) | $($row.moves) |") }
$lines.Add('')
$lines.Add('| Level tốn RAM | Peak RAM MiB | Trạng thái mở rộng | Nước đi |')
$lines.Add('|---|---:|---:|---:|')
foreach ($row in $summary.highRamLevels) { $lines.Add("| $($row.level) | $($row.ramMiB) | $($row.calculations) | $($row.moves) |") }
$lines.Add('')
$lines.Add('| Level có lời giải dài | Nước đi mới | Nước đi baseline | Trạng thái baseline |')
$lines.Add('|---|---:|---:|---|')
foreach ($row in (Import-Csv (Join-Path $folder 'comparison.csv') | Sort-Object { [int]$_.moves } -Descending | Select-Object -First 10)) {
    $lines.Add("| $($row.level) | $($row.moves) | $($row.baselineMoves) | $($row.baselineStatus) |")
}
$lines.Add('')
$lines.Add('Số nước đi baseline chỉ là lời giải hoàn chỉnh khi baselineStatus là Solved; các trạng thái khác chứa kế hoạch dở dang.')
$lines.Add('')
$lines.Add('## Kiểm chứng và giới hạn')
$lines.Add('')
$lines.Add('Build và test cơ bản: corridor cần dọn nhiều hole, bỏ qua deadline/depth cấu hình cũ, cancellation, gate, linked partner, trục linked không tương thích và 504 board nhỏ đối chiếu oracle. Đủ 299 JSON input và kết quả duy nhất; source/input hash khớp manifest, metric khớp JSON/memory, cả hai ngân sách thời gian bằng 0 và consumption backtracks bằng 0. Replay độc lập đọc kế hoạch đã lưu, chạy không timeout, kiểm tra cat, playable hole và hàng đợi box. Replay xác nhận core model; chưa phải kiểm thử trên game engine thật.')
$lines.Add('')
foreach ($row in $summary.failedLevels) { $lines.Add("Level chưa giải: $($row.level), trạng thái $($row.status).") }
$lines.Add('')
$lines.Add('Không quay lui qua lần ăn là giả định của base. NoNextCatReachable chỉ kết luận cho trạng thái đã chốt, không chứng minh level gốc vô nghiệm. DFS giữ tính đầy đủ trong pha trước lần ăn nhưng có thể mở rộng nhiều trạng thái và tạo kế hoạch dài. Chưa có bảo đảm tối ưu số nước đi hay RAM.')
$lines.Add('')
$lines.Add('## Mục tiêu tối ưu tiếp theo đề xuất')
$lines.Add('')
$lines.Add('Ưu tiên giảm số trạng thái mở rộng và RAM ở nhóm level chi phí cao trong bảng: biểu diễn rõ đường của hole mục tiêu và phụ thuộc các hole cản đường, xếp hạng việc dọn cản theo tiến triển thật tới lần ăn. Giữ bộ duyệt đầy đủ làm fallback, không quay lui qua lần ăn và không thêm timeout. So sánh cùng 299 level, cùng định nghĩa expanded states và peak RAM từng tiến trình; mục tiêu trước hết giữ tỷ lệ giải và replay, rồi giảm tổng/P95/maximum chi phí và số nước đi dài. Chỉ đề xuất ở giai đoạn này, chưa triển khai tối ưu tiếp theo.')
$lines.Add('')
$lines.Add('Dữ liệu kèm theo: metrics.csv, comparison.csv, summary.json, replay-audit.json, provenance.csv, manifest.json và kết quả/memory từng level. Baseline và các kết quả thử nghiệm cũ được giữ riêng.')
$lines | Set-Content -LiteralPath (Join-Path $folder 'report.md') -Encoding utf8
Write-Output "Wrote $folder/report.md"

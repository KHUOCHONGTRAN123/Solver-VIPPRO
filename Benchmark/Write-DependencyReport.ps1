param([string]$Run='route-dependency-current')
$ErrorActionPreference='Stop'
$folder=Join-Path $PSScriptRoot "results/$Run"
$rows=Import-Csv (Join-Path $folder 'metrics.csv')
$baseline=@{}
Import-Csv (Join-Path $PSScriptRoot 'results/route-final/metrics.csv')|ForEach-Object {$baseline[$_.level]=$_}
$comparison=foreach($row in $rows){
    $before=$baseline[$row.level]
    [pscustomobject]@{level=$row.level;beforeStatus=$before.status;afterStatus=$row.status;
        beforeCalculations=[long]$before.calculations;afterCalculations=[long]$row.calculations;
        beforeRamBytes=[long]$before.peakWorkingSetBytes;afterRamBytes=[long]$row.peakWorkingSetBytes;
        withinBudget=$row.withinBudget;sourceRun=$row.sourceRun}
}
$comparison|Export-Csv (Join-Path $folder 'comparison.csv') -NoTypeInformation
$auditPath=Join-Path $folder 'guard-audit.json'
$checked=0
if(Test-Path $auditPath){$checked=(Get-Content -Raw $auditPath|ConvertFrom-Json).checkedPlans}
$beforeSum=($comparison|Measure-Object beforeCalculations -Sum).Sum
$afterSum=($comparison|Measure-Object afterCalculations -Sum).Sum
$ram=($comparison|Measure-Object afterRamBytes -Maximum).Maximum
$count=@($rows).Count
$budgetCount=@($rows|Where-Object withinBudget -eq 'True').Count
$finalChecks='Kiểm tra trạng thái cuối vẫn đang chờ.'
$complete=$false
$completionPath=Join-Path $folder 'completion-audit.json'
if(Test-Path $completionPath){
    $completion=Get-Content -Raw $completionPath|ConvertFrom-Json
    $complete=$completion.status -eq 'Passed' -and $completion.levels -eq 299 -and $count -eq 299 -and $budgetCount -eq 299 -and $checked -eq 299
    if($complete){$finalChecks='Đã replay đủ 299 lời giải: hết cat, tất cả hole chơi được hoàn tất và hết hàng đợi box/tower. completion-audit.json xác nhận ngân sách, phiên bản nguồn và phạm vi kiểm chứng.'}
}
$statePath=Join-Path $folder 'partial-final-state-audit.json'
$boxPath=Join-Path $folder 'partial-box-audit.json'
if((Test-Path $statePath) -and (Test-Path $boxPath)){
    $stateAudit=Get-Content -Raw $statePath|ConvertFrom-Json
    $boxAudit=Get-Content -Raw $boxPath|ConvertFrom-Json
    $finalChecks="Đã kiểm tra trạng thái cuối $($stateAudit.inspected) level: hết cat trên bàn, các hole chơi được ban đầu đã hoàn tất; đã kiểm tra hết hàng đợi box/tower của $($boxAudit.inspected) level. Các con số này là phạm vi audit đã ghi, không tự mở rộng khi có kết quả mới."
}
$lines=@(
    '# Báo cáo tối ưu RouteClearing — kết quả đang kiểm chứng',
    '',
    "Đã có $count/299 kết quả; $budgetCount kết quả giải thành công trong ngân sách. Audit đã kiểm tra $checked lời giải của bản hợp nhất. Đây là kết quả từng phần; chưa chứng minh hoàn thành goal.",
    '',
    $finalChecks,
    '',
    'Ngân sách đánh giá: 100.000 lần mở rộng trạng thái và RAM đỉnh 2 GiB mỗi level. Không dùng thời gian làm tiêu chí dừng solver. Các lượt accelerator có giới hạn tính toán rồi chuyển sang tìm kiếm đầy đủ.',
    '',
    "Trên cùng $count level: baseline $beforeSum lần mở rộng; bản mới $afterSum lần. RAM đỉnh bản mới: $ram byte. Các counter baseline của level bị dừng là cận dưới, không phải chi phí giải thành công.",
    '',
    'Thay đổi: giữ mục tiêu hole/vị trí ăn cụ thể; kiểm tra chỗ nhường đường; mở rộng phụ thuộc qua các hole chắn đường nhường; tính yêu cầu vùng cần dọn riêng cho từng hole; kiểm tra sức chứa và vòng phụ thuộc cat–box–layer trước khi chốt nước ăn. Giữ tìm kiếm dự phòng và không quay lui qua nước ăn đã chốt.',
    '',
    'Hạn chế: các mô hình khả thi là nới lỏng; pass không chứng minh thắng cuối cùng. Lập kế hoạch đường cho linked target còn dựa vào fallback. Số lần mở rộng không phản ánh đầy đủ số thao tác bên trong từng lần; RAM đo theo peak working set của worker. Bộ 299 level mẫu không chứng minh khả năng trên mọi level.',
    '',
    'Chi tiết từng level và nguồn chạy: comparison.csv, metrics.csv. Kết quả thiếu và source hash: summary.json. Audit: guard-audit.json. Chỉ xác nhận hoàn thành sau khi đủ 299 kết quả cùng source, trong ngân sách, replay hợp lệ và kiểm tra final state.'
)
if($complete){
    $lines[0]='# Báo cáo tối ưu RouteClearing — 299/299 đã kiểm chứng'
    $lines[2]='Đã giải thành công 299/299 level mẫu bằng cùng phiên bản solver; mỗi level không quá 100.000 lần mở rộng trạng thái và RAM đỉnh 2 GiB. Đủ 299 lời giải đã qua guard, replay và kiểm tra trạng thái cuối.'
    $lines+=@('', 'Cải thiện bổ sung: tìm kiếm theo lớp giữ phương án của nhiều nhóm hole; tìm kiếm hàng đợi ưu tiên cho giai đoạn còn tối đa 4 hole; bộ đệm nước đi có giới hạn; lượt ưu tiên đường ăn cat mang chìa khóa khi các lượt thường chưa tìm được nước ăn và còn hole khóa.', '', 'Mục tiêu tiếp theo: giảm chi phí các level nặng nhất dựa trên comparison.csv; cải thiện dự đoán phụ thuộc hole liên kết; bổ sung level ngoài tập mẫu để kiểm tra tính tổng quát. Giữ replay và ngân sách làm điều kiện bắt buộc cho mỗi thay đổi.')
}
$lines|Set-Content (Join-Path $folder 'report.md')
Write-Output "Partial report written: $count results / $checked audited"



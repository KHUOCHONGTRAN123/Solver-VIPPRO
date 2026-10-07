# Thuật toán chốt cho 19 level

Ngày 2026-10-07, người dùng bỏ Level00206 và Level00233 khỏi goal để kết thúc nghiên cứu. Phạm vi cuối là 19 level còn lại trong nhóm 21 level baseline có expanded >10.000. Hai level bị loại không được tính là đạt; dữ liệu nghiên cứu cũ được giữ.

## Ngưỡng nghiệm thu

Mỗi level phải Solved, replay đúng bằng engine v42 độc lập, expanded cộng dồn <30.000 ở cả ba lượt, median solveTimeMs <3.000 ms. Build Release, cùng máy/runtime với baseline, chạy tuần tự, warm-up chính level một lượt rồi đo ba lượt. solveTimeMs gồm parse, validation, precompute, search và replay nội bộ, không gồm I/O, serialization hay audit ngoài solver.

## Pipeline được chốt

1. Thử tiêu thụ trực tiếp trước khi xếp hạng các vị trí đỗ.
2. Với tối đa bốn nhóm đang hoạt động: thử pair pattern 256 trạng thái, pair/triple 4.096, rồi frontier cho bàn nhỏ.
3. Với nhiều nhóm hơn: ưu tiên target corridor khi đầu vào có ít nhất 13 hole hoặc ít nhất hai tower. Chính sách được giữ qua các pha; không dùng ID level. Thử tối đa ba hole mục tiêu khác nhau, beam rộng 4, ngân sách mỗi target là min(64, max(16, 6 × số hole đầu vào)).
4. Tiếp theo thử beam tổng hợp rẻ rộng 4 / 128 trạng thái, rộng 16 / 512, pair pattern 256, pair/triple 2.048.
5. Khi các lượt có giới hạn thất bại: tìm theo độ sâu, dependency corridor, beam rộng và DFS. Lượt beam min đầu tiên dành 4.096 trạng thái trên đầu vào target-first, 16.384 trên đầu vào pressure-first. DFS được thử tiếp; fallback đầy đủ vẫn còn.

Terminal được kiểm tra khả năng tiếp tục theo các điều kiện relaxed, rồi chọn theo utility tiêu thụ và future rank. Parking chain được rút gọn bằng successor hợp lệ. Tất cả trạng thái full-state của lượt thử thất bại và rút gọn vẫn cộng vào expanded. Abstract pattern pops được ghi riêng; không đổi cách đếm để đạt ngưỡng.

WeightedRouteGraph cache các cạnh predecessor hợp lệ theo pha, hole và mặt nạ bất động; giữ nguyên gate, trục di chuyển và chi phí occupancy. Cache khoảng cách giữ tối đa 4.096 entries. Successor chính xác và replay kiểm tra mechanic thực tế.

## Phần nghiên cứu không chạy trong pipeline

Decision backtracking, additive pair ordering, mobility tie-break, early key-wide và joint four/five/six-body model được giữ để đối chiếu nhưng không được gọi từ pipeline chốt. Four-pattern switch tắt. Không tiếp tục nghiên cứu 206/233 trong goal này. Không có lời giải lưu sẵn hay special case theo ID level trong solver.

## Bằng chứng và cách tái lập

Binary chốt: `Verification/final-19-bin/IndependentSolver.dll`. Báo cáo có hash sản phẩm, engine độc lập, harness, catalog và từng level tại `final-19-acceptance/`. Mỗi level có warm-up status, ba mẫu, lời giải, expanded, thời gian, drags, steps và plan hash. Baseline ba mẫu của 19 level nằm trong `baseline-three-all/` (archive vẫn gồm đủ 21).

```powershell
dotnet build Verification/Verification.csproj -c Release --no-restore -m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false -o Verification/final-19-bin
dotnet Verification/final-19-bin/Solver.Verification.dll research-acceptance . final-19-acceptance current
dotnet Verification/final-19-bin/Solver.Verification.dll tests .
dotnet Verification/final-19-bin/Solver.Verification.dll route-graph-tests .
& ./docs/research/update-30k-report.ps1
```

Kết quả nghiệm thu cuối và so chất lượng lời giải với baseline: [GOAL_30K_STATUS.md](GOAL_30K_STATUS.md). Số drag/bước có thể tăng ở một số level; thuật toán không cam kết lời giải ngắn nhất. Ngưỡng thời gian được nghiệm thu trên máy đo, không phải cam kết mọi máy hoặc mọi level mới.

Lượt đo lại cuối có dao động ở 00216: ba mẫu đầu 2.470 / 3.297 / 3.174 ms, median 3.174 ms (không đạt). Đo riêng lại trên cùng binary sau khi cả batch kết thúc cho ba mẫu 2.454 / 2.375 / 2.275 ms, median 2.375 ms (đạt). Dữ liệu đầu được giữ ở `final-19-first-pass/`; lượt đo lại ở `final-19-timing-check/` và được đưa vào báo cáo cuối. Không bỏ hay sửa giá trị mẫu; kết quả đạt ngưỡng là bằng chứng theo quy trình đo, không bảo đảm mọi lượt chạy đều dưới ba giây.

Đây là chốt nghiên cứu thuật toán trong workspace; các gói phát hành 1.0.0 cũ được giữ nguyên, chưa tạo release mới.

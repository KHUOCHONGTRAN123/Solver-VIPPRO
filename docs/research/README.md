# Nghiên cứu ngưỡng 10.000 trạng thái và 1 giây

Điều kiện nghiệm thu đầy đủ nằm trong GOAL.md. Chỉ chạy 21 level từ benchmark phát hành có expanded > 10.000.

## Chạy

Build trên môi trường hiện tại cần MSBuild tuần tự:

```powershell
dotnet build Verification/Verification.csproj -c Release --no-restore -m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false -o Verification/research-bin
dotnet Verification/research-bin/Solver.Verification.dll research . target-beam
```

Lệnh research là sàng lọc một mẫu, không phải nghiệm thu. Có thể truyền danh sách level thuộc phạm vi, ngăn cách bằng dấu phẩy, sau tên thử nghiệm.

```powershell
dotnet Verification/research-bin/Solver.Verification.dll research-acceptance . acceptance current
dotnet Verification/research-bin/Solver.Verification.dll research-acceptance . baseline-three-samples baseline
```

Mode baseline dùng DLL 1.0.0 đã đóng gói tại artifacts/unity/IndependentSolver.dll; không dùng engine v42 chậm hơn làm baseline thời gian. Các lệnh nghiệm thu cũng hỗ trợ danh sách level cuối lệnh. Chạy các benchmark tuần tự, không chạy đồng thời. Bộ nghiệm thu lưu ba mẫu, lời giải, hash DLL/level/plan và thông tin máy/runtime. Mỗi mẫu được kiểm tra bằng replay v42 độc lập ngoài solveTimeMs.

## Tiến độ

- baseline: bản 1.0.0, một mẫu/level; đủ 21 level, expanded khớp CSV phát hành và mọi lời giải qua replay. Tổng thời gian lần đo này khoảng 151,45 giây.
- narrow-beam-16: beam rộng 16 trước dependency; một số level cải thiện mạnh, một số hồi quy. Trường passed của thử nghiệm cũ chỉ xét expanded, không phải điều kiện thời gian mới. Không dùng nó để tuyên bố nghiệm thu.
- beam-portfolio: thử beam rộng 4 và 16 với hai cách xếp hạng min/sum, gồm nhóm còn ít hole. Trường passed xét cả expanded và thời gian một mẫu, vẫn chưa phải nghiệm thu.
- target-beam: mở rộng portfolio bằng beam có mục tiêu hole/đích cố định, thêm trace tên phương pháp tìm kiếm để phân tích từng giai đoạn. Chưa có kết luận trước khi đo.
- target-beam-focused: đã chạy sáu level thuộc phạm vi. Target beam hẹp chưa khắc phục các trường hợp này. Level00233 bị Cancelled ở 153.428 trạng thái, ghi nhận thất bại.
- sum-first: thử beam sum rộng 128 với ngân sách 2.048 trạng thái trước beam min rộng/DFS, và trước frontier ở nhóm <=4 hole. Trace Level00101 ở bản trước cho thấy 16.384 lượt beam min và 12.000 DFS trước khi beam sum giải được với khoảng 636 lượt; bản sum-first giảm Level00101 từ 32.147 xuống 3.763 trạng thái trong sàng lọc. Thời gian mẫu đầu 1.428 ms vẫn vượt ngưỡng. Đang chạy đủ 21 level; chưa nghiệm thu.
- sum-first đã hoàn tất 21 mẫu: chỉ 6 level đạt hai ngưỡng sàng lọc; Level00233 và Level00246 Cancelled. Phương án này không đạt goal.
- cheap-beam: thử sum bỏ RouteClearancePenalty trong beam đầu và bỏ kéo liên tiếp cùng nhóm. Level00101 đạt 4.380 trạng thái / 817 ms, Level00213 đạt 3.342 / 708 ms trong một mẫu, nhưng một số level khác hồi quy. Chưa nghiệm thu.
- cheap-portfolio: thử beam sum nhẹ rộng 4, 16, 128 trước các accelerator đắt; cache điểm nhẹ theo phase/state/aggregation/keys. Cần đo mới; không suy ra đạt từ kết quả cheap-beam.
- decision-plan-focused: thử tìm kiếm theo sự kiện ăn, cho phép khôi phục checkpoint và thử tối đa bốn lựa chọn ăn khi phase sau không giải được trong ngân sách nhỏ. Tổng thử nghiệm giới hạn 6.000 lượt mở rộng trước fallback; mọi lượt được cộng vào expanded của kết quả. Bản đầu dùng beam nhẹ rộng 16 / 256 lượt mỗi phase. Hai mẫu đầu (Level00101, Level00117) chưa đạt, đều replay đúng.
- decision-narrow: biến thể dùng beam nhẹ rộng 4 / 512 lượt mỗi phase, nhằm đi sâu hơn trong chuỗi dọn đường. Đã build, chưa đo. Đây là thử nghiệm thay quyết định ăn, không chỉ thay traversal trong một phase.

Lượt cheap-beam đã hoàn tất 21 mẫu. Level00233 và Level00246 Cancelled; biến thể này không đạt goal. Những giá trị tốt nhất ở các biến thể khác nhau không được ghép theo ID level để tuyên bố nghiệm thu; nghiệm thu cần một thuật toán chung đạt đủ 21 level.

## Heuristic hình học theo nhóm

- pair-pattern-269: thêm bảng khoảng cách kéo giữa hai hole vào pipeline cheap-portfolio. Giai đoạn cuối bốn hole giảm mạnh nhưng toàn level vẫn 11.247 trạng thái / 1.260 ms; không đạt.
- pair-original-269: giữ traversal 1.0.0 cho giai đoạn >4 hole, thêm heuristic cặp ở giai đoạn 3–4 hole. Đã nghiệm thu riêng Level00269: ba mẫu đều 8.964 trạng thái, median 421,53 ms; tất cả replay độc lập đúng. Mỗi mẫu còn có 13.839 trạng thái trừu tượng, ghi riêng và tính chi phí xây vào solveTimeMs.
- baseline-three-269: DLL phát hành 1.0.0, cùng máy/runtime/quy trình: ba mẫu đều 82.130 trạng thái, median 3.084,20 ms, replay đúng. Đây là baseline ba mẫu đầu tiên; chưa đủ baseline 21 level.
- pair-selected-focused: mở rộng sang giai đoạn đông hole, chọn tối đa ba mục tiêu và ba nhóm cản đường/mục tiêu; hỗ trợ nhóm linked bằng footprint hợp nhất. Sáu level được đo; chưa level nào đạt cả hai ngưỡng. Không dùng milestone của pair-original để chứng nhận biến thể này.
- triple-pattern: bổ sung mô hình ba nhóm để tính tương tác giữa hai vật cản. Chỉ dùng khi còn ít nhất bốn nhóm movable, nên luôn là trạng thái trừu tượng, không phải vét toàn bộ cấu hình bàn cờ rồi bỏ khỏi expanded. Bảng cặp/ba nhóm chỉ xếp hạng; mọi nước đi thật vẫn do Generate và replay kiểm tra. Đã build, đang chờ đo.

`expanded` vẫn đếm các lần mở rộng full state và compact như bản 1.0.0. `abstractExpanded` ghi riêng số nút trong bảng heuristic; không thể bỏ qua phần chi phí này khi đánh giá thời gian. Cache bảng thuộc từng invocation, không tái sử dụng lời giải giữa các lần SolveLevel.

- triple-pattern đã đo đủ 21 level: chỉ Level00100 đạt hai ngưỡng một mẫu; Level00206 và Level00233 Cancelled. Chưa đạt goal.
- pattern-occupancy: kết hợp max khoảng cách pattern với OccupancyPathDistance/LinkedRouteDistance của toàn bàn, thêm depth để tránh đi vòng, cache điểm full state. Hai mẫu đầu Level00100 (1.698 / 610 ms) và Level00101 (760 / 236 ms) đạt sàng lọc; các mẫu sau còn nhiều thất bại. Chưa nghiệm thu.
- pressure-pattern: chuẩn bị pipeline kiểm tra ăn trực tiếp, beam sum nhẹ rộng 4 và 16, rồi pattern + occupancy, trước fallback gốc. Mục đích tránh chi phí xếp hạng clearance của lượt depth-2 và tránh xây pattern cho chuỗi mà beam nhẹ đã tìm được. Đã build, chưa đo.
- pressure-pattern đã đo đủ 21: bảy level đạt hai ngưỡng sàng lọc; hai level Cancelled. Level00269 đạt 2.053 / 215 ms trong một mẫu, nhưng biến thể chung chưa đạt goal.
- future-choice: khi utility ăn bằng nhau, so chi phí relaxed của lần ăn kế tiếp, tính trong checkpoint đang được dùng để kiểm tra continuation. Tám level đạt hai ngưỡng sàng lọc; Level00274 970,55 ms sát ngưỡng. Chưa nghiệm thu ba lượt. Level00176 giảm từ 118.199 / 18.672 ms của pressure-pattern xuống 14.181 / 4.065 ms nhưng vẫn chưa đạt.
- event-frontier: thay vì chốt terminal đầu tiên, so các terminal trong cùng lớp beam; pattern search xét thêm tối đa 16 nút sau terminal đầu. So các cấu hình đỗ khác nhau bằng utility/futureRank. Cache move giữ các giá trị mô phỏng đã hoàn tất trong cùng phase/state. Đang chuẩn bị chạy đủ 21 level.

Lượt event-frontier đang chạy đủ 21 level. Các mẫu đầu vẫn có hồi quy và Level00206 Cancelled, vì vậy chưa chứng minh đạt goal. Runtime Python bundled đã được kiểm tra: chưa có z3, ortools hoặc pysat. Nếu các heuristic hình học tiếp tục không đủ, hướng nghiên cứu tiếp theo là mô hình ràng buộc theo bước, với geometry/gates/link lấy từ engine hiện tại và mọi witness được kiểm tra bằng Generate/replay. Chưa triển khai hoặc đo hướng này; không dùng nó để tuyên bố thành công.

Các accelerator chỉ thay thứ tự tìm kiếm và vẫn có fallback. Không hardcode theo level, không đổi cách đếm expanded. Hủy do vượt ngân sách sàng lọc được ghi Cancelled/failed.

Goal chưa hoàn thành. Ngưỡng bắt buộc là 21/21 level Solved, replay đúng, expanded < 10.000 mỗi mẫu và median solveTimeMs < 1.000 ms.

### Constraint probe (chưa tích hợp vào solver)

Đã build công cụ `constraint-probe` trong Verification để lấy footprint, linked group, axis và cạnh qua gate từ engine hiện tại. Chỉ xuất các level thuộc phạm vi 21; tùy chọn `hardest` dùng input của subproblem có expanded lớn nhất trong kết quả v42 đã lưu. Đây là fixture nghiên cứu một pha, không phải hardcode nghiệm trong sản phẩm. Z3 5.1.0 được cài riêng trong `artifacts/research-python`; sản phẩm không thêm dependency.

Mô hình số nguyên + mảng tìm witness ở pha đầu Level00206 sau 48 ms, nhưng pha khó nhất của Level00206 và Level00233 trả `unknown` ở mọi độ dài đã thử với timeout 500 ms/check. Chuyển sang QF_BV (vị trí bit-vector, footprint bằng ite) giúp chứng minh `unsat` ở các độ dài ngắn nhanh hơn rõ rệt; vẫn chưa tìm được witness trong ngân sách thử nghiệm 5 giây. Kết quả và thống kê nằm trong `constraint-probe/*.probe.json` và `*.bitvector-probe.json`. Thời gian này là thí nghiệm Python một pha, không phải solveTimeMs nghiệm thu.

Chưa có witness để kiểm tra bằng Generate/replay độc lập; chưa có phép giải toàn level, chưa đạt ngưỡng thời gian. Không tính các kết quả này vào số level đạt goal. Hướng tiếp theo cần giảm kích thước mô hình bằng biến cho một lượt kéo hoặc giới hạn nhóm cản đường, thay vì tăng vô hạn số bước đơn vị.

Đã thử mô hình lượt kéo trong `constraint-probe/drag_probe.py`: SAT chọn endpoint bằng reachability tĩnh; BFS dưới cấu hình chướng ngại cụ thể kiểm tra mỗi drag và thêm cut qua biên bị chặn. Các cut giữ điều kiện mover/source và cho phép endpoint trong vùng đã tới hoặc mở ít nhất một ô biên. Kết quả Level00206 pha khó nhất: 25 lần check, 46 cut, không có witness, 5.297 ms tổng chi phí thử. Level00233 kiểm tra trực tiếp độ dài 6–8 drag đều `unknown` với timeout 200 ms/check, tổng 5.074 ms gồm xây mô hình Python. Đây vẫn là mô hình hình học của các body được xuất, không chứng nhận đầy đủ mechanic hoặc lời giải level. Chưa đưa Z3 vào sản phẩm vì chi phí đo được chưa đáp ứng mục tiêu.

### Pattern cost tiers

`pair-only-cost` bỏ bảng ba nhóm, giữ lựa chọn terminal của event-frontier: sáu level đều Solved/replay đúng nhưng chỉ 149 và 269 đạt sàng lọc. 274 hồi quy tới 17.981 trạng thái. `pair-first-cost` thêm dừng pattern ở terminal đầu, vẫn chỉ hai level đạt. Vì vậy không loại bỏ hoàn toàn bảng ba nhóm.

`pattern-tier-cost` thử cặp trước với ngân sách 256 full states, rồi dùng bảng ba nhóm nếu cần (4096 khi <=4 hole, 2048 khi đông hơn). Chi phí cả hai lượt vẫn cộng vào expanded; không dùng ID level để chọn thuật toán. Sáu level đều replay đúng; 149/269 đạt một mẫu, 274 đạt expanded 9.585 nhưng thời gian 1.003 ms trong mẫu sàng lọc. 176/239/268 chưa đạt.

`pattern-tier-acceptance` đã đo Release tuần tự, warm-up riêng từng level rồi ba lượt: 149 có expanded 7.770 cả ba, median 984,6221 ms; 269 có expanded 2.096 cả ba, median 213,1736 ms. Mọi mẫu Solved và replay độc lập v42 đúng. Assembly C766614EB6C53EF2908ABC9E3595ADAA1B16F9AB25B8CDDB6B7F4ADDA4A8C790. Đây là hai level của cùng biến thể, chưa nghiệm thu cả 21. 149 sát ngưỡng nên cần kiểm tra lại nếu có thay đổi thuật toán. Lời giải, hash, máy/runtime và ba mẫu được lưu trong thư mục nghiệm thu.

`pattern-tier-all` đã sàng lọc đủ 21: 19 Solved/replay đúng, 206/233 Cancelled; bảy mẫu đạt đồng thời hai ngưỡng (100,101,184,213,225,269,274). 149 dao động 1.008 ms ở lượt này. Chỉ là sàng lọc, không thay kết quả nghiệm thu. Có build một binary khác trong lúc lượt sàng lọc đang chạy, nên các thời gian sàng lọc không dùng để kết luận tốc độ tinh vi.

`single-anchor-acceptance` thử nhánh Generate riêng cho hole không linked. Ba level giữ nguyên expanded và lời giải replay đúng trong mọi mẫu; median 149=1.023 ms, 269=241 ms, 274=1.034 ms. Không chứng minh được cải thiện thời gian; đã gỡ nhánh này khỏi source và giữ thuật toán pattern-tier. Kết quả thí nghiệm vẫn được lưu để tránh lặp lại hướng không có bằng chứng hiệu quả. Goal chưa đạt.

`distance-cache-acceptance`: tăng giới hạn cache occupancy-distance từ 512 lên 4096, giữ key theo phase/hole/target/blockers và giữ nguyên thuật toán. Ba mẫu mỗi level giữ nguyên expanded và replay v42 đúng: 149=7.770 / median 882 ms (đạt), 274=9.585 / median 923 ms (đạt), 239=5.351 / median 1.873 ms (không đạt thời gian). Chi phí cache nằm trong solveTimeMs. Chưa chứng nhận 21 level; cache lớn cần đánh giá trên các trường hợp khó còn lại. Baseline đầy đủ 21 theo warm-up + ba mẫu được bổ sung trong `baseline-three-all` bằng DLL 1.0.0 phát hành.

`baseline-three-all` đã hoàn tất và được kiểm tra: đúng 21 level của phạm vi, mỗi level warm-up riêng + 3 mẫu Release chạy tuần tự; cả 63 mẫu Solved/replay độc lập v42 đúng, expanded ổn định trong mỗi level. Tất cả dùng DLL phát hành hash `C0A95EF04326B80061E51B5F5CB6D62282F452699BE356E60CB367152DC40F46`. Mỗi level có summary/provenance cùng 3 file sample chứa lời giải; drags/steps và planHash được ghi để so chất lượng nghiệm. Đây là baseline hoàn chỉnh theo quy trình yêu cầu, không phải biến thể mới đạt goal.

Các median baseline đáng chú ý: 206=19.016 ms / 64.364 trạng thái, 233=26.129 ms / 62.395, 216=1.565 ms / 10.072, 239=7.087 ms / 26.253, 274=16.260 ms / 47.332. Hồi quy expanded của 206/216 trong pattern-tier cho thấy phải sửa cách chọn terminal và thứ tự tìm kiếm; tăng cache chỉ giảm thời gian tính điểm. 21/21 của biến thể mới vẫn chưa đạt, goal còn hoạt động.

`terminal-utility`: bỏ futureRank khỏi lựa chọn terminal, chốt terminal đầu của beam. 176 hồi quy 118.711 trạng thái / 15,4 giây; 206/233 vẫn Cancelled; 216=37.302 / 4,7 giây. Vì vậy đã khôi phục futureRank. 269/274 đạt một mẫu nhưng không dùng để chứng nhận biến thể khác.

`short-chain-first`: khôi phục futureRank; với >4 hole kiểm tra đầy đủ radius hai drag và dependency trước các beam/pattern, bỏ lượt radius hai lặp lại ở fallback. Cả sáu level thử đều Solved/replay v42 đúng: 176=9.089 / 1.402 ms; 206=48.402 / 14.725 ms; 216=13.069 / 1.738 ms; 233=71.224 / 27.870 ms; 269=2.117 / 181 ms; 274=50.283 / 17.194 ms. Cải thiện hồi quy ở 206/216/176 nhưng gây hồi quy nghiêm trọng ở 274. Source hiện là biến thể nghiên cứu này; không phải bản nghiệm thu. Tiếp theo cần dùng tín hiệu cấu hình và lựa chọn terminal để tránh chốt đường gây giai đoạn sau khó, không chọn biến thể bằng ID level. Toàn bộ expanded cộng dồn cả short pass, dependency, beam, pattern và compact.

`dependency-beam`: sau direct, thử tối đa ba hole mục tiêu; beam rộng 4, mỗi mục tiêu 64 full states. Chỉ enqueue nhóm trong chuỗi dependency; rank theo occupancy-distance cùng số request chưa clear, mọi successor vẫn từ Generate, fallback đầy đủ được giữ. Bỏ radius hai trước beam và giữ futureRank, beam chốt terminal đầu. Sáu level đã đo: 176 đạt 5.586 / 614 ms, 206/233 Cancelled, 216=39.692 / 6.622 ms, 269=12.375 / 1.346 ms, 274=10.850 / 1.048 ms. Đây không phải biến thể đạt chung.

`dependency-beam-176` nghiệm thu riêng 176: cả ba mẫu expanded 5.586, Solved/replay v42 đúng, median 525 ms; drags 135. Kết quả này không được gộp với 149/274 của binary distance-cache để tuyên bố nhiều level đạt cùng một thuật toán. Vì hồi quy nhóm còn lại, đã bỏ lời gọi dependency-beam khỏi pipeline chính và khôi phục chọn terminal cùng lớp beam của cache-tier. Helper nghiên cứu vẫn ở source, không được pipeline gọi. Build Release của source khôi phục thành công, goal chưa hoàn thành.

### Event backtracking, complete arrangements

Đã sửa event exclusions của DecisionPlanner từ (hole, destination) thành (hole, full Positions). Cùng lần ăn nhưng bố trí các nhóm khác khác nhau phải được phép thử lại, vì chi phí tiếp diễn có thể rất khác. Khi quay lui, checkpoint và plan được khôi phục nhưng expanded luôn cộng dồn. Ngân sách 6.000 toàn accelerator / 512 mỗi pha chỉ chuyển sang fallback đầy đủ; không biến thành kết quả Solved hoặc che chi phí.

Ba biến thể `event-arrangements`, `event-portfolio`, `event-target-first` đều không tìm được toàn plan trong ngân sách trên các level đã thử. Biến thể đầu dùng direct + beam 4 + target; thứ hai thêm beam 16 và pattern khi <=4 hole; thứ ba ưu tiên target/pattern trước beam. Với 176, cả ba tốn hết 6.000 rồi fallback, tổng 20.552 trạng thái; 216 ở hai biến thể đầu tổng 43.270; 206/233 bị hủy ở ngưỡng sàng lọc. 269 tổng 8.096 / khoảng 0,6 giây là fallback thành công cộng đủ 6.000 thử nghiệm, không phải planner thành công. 274 tổng 15.585 và không đạt. Trace 176 cho thấy nhánh quay lui lặp pha khó quanh 47–53 drag, chạm ngân sách trước khi hoàn tất. Mọi mẫu Solved vẫn replay v42 đúng.

Đã gỡ lời gọi TryDecisionPlan khỏi Run để không giữ chi phí không có lợi trong pipeline chính. Mã sửa exclusion và portfolio nằm trong helper nghiên cứu chưa kích hoạt. Source chính trở về cache-tier; không tuyên bố goal đạt. Cần cải thiện việc chọn cấu hình trước pha khó hoặc mô hình search để các pha đó thật sự rẻ, thay vì chỉ thử nhiều nhánh trong cùng ngân sách.

### Footprint cost and exact stalled phases

`contact-penalty`: thay 8*overlap.PopCount bằng phạt 8 khi footprint chạm bất kỳ blocker (cả single và linked distance). Bảy level đo: chỉ 269/274 đạt một mẫu; 117 tăng lên 58.255, 176=32.587, 216=32.982, 206/233 Cancelled. Đã khôi phục phạt theo số ô. Không bỏ va chạm khỏi Generate hoặc replay.

`future-sum`: terminal futureRank dùng tổng route pressure thay minimum. 117=20.370 / 1.638 ms, 176=14.030 / 3.898 ms, 216=37.475 / 4.958 ms, 206/233 Cancelled; 269/274 đạt sàng lọc. Chưa cải thiện nhóm khó, đã khôi phục minimum.

Đã thêm `phase-fixture` trong Verification: replay một prefix của plan qua engine v42 độc lập, audit relaxed assignment, sau đó CaptureInput và lưu hash level/source/reference/fixture. Fixtures `phase-fixtures/Level00206.pattern-tier-all.48.input.json` và `Level00233.pattern-tier-all.137.input.json` là đúng hai pha stalled của biến thể chung, không phải pha của plan baseline. Cả hai còn 14 hole, không linked và 12 cat trên bàn; 206 có một hole locked với lockColorId 6 và ba cat mang keyColorId 6. Công cụ này là nghiên cứu, không dùng fixture làm lời giải hardcode trong solver.

`early-key-beam`: đưa beam rộng 4 / 128 states ưu tiên khóa trước các beam/pattern chung; 206 vẫn Cancelled sau 44 drag. `key-target`: tập trung tối đa ba hole có goal chứa key cần mở lock, beam rộng 4 / 64 states mỗi hole; chỉ nhận terminal đúng hole/anchor mục tiêu. 206 vẫn Cancelled ở 153.391 states sau 48 drag; 176 không cải thiện, 274 giữ 9.585 / 947 ms. Vì không chứng minh lợi ích, đã gỡ early key pass khỏi pipeline. Helper nghiên cứu vẫn lưu, source chính trở về cache-tier. Goal chưa đạt.

### Four-body compressed model and requested pause

Đã thêm phase-search cho hai fixture stalled, so pipeline/target/key/beam/pair/triple/dependency với ngân sách chẩn đoán tối đa 3 giây hoặc 10.000 trạng thái. Không phương pháp nào tìm được event trong hai fixture. Đây là diagnostic một pha, không nghiệm thu toàn level; không có event không chứng minh pha vô nghiệm.

Audit mọi cặp và tổ hợp ba nhóm phát hiện một số target unreachable trong mô hình relaxed, nhưng vẫn có target không bị loại; vì vậy chưa có chứng minh dead-end. FourPattern thu hẹp domain theo component tĩnh trước khi lập bảng bốn nhóm, luôn bỏ ít nhất một nhóm movable. Bảng giới hạn 500.000 entries / 25.000 abstract pops; zero trong bảng chưa hoàn tất là unknown, không dùng để prune. Chi phí và abstractExpanded ghi đầy đủ, không tính lẫn với expanded full-state.

`four-model-final`: bốn level đại diện đều Solved/replay v42 đúng, một mẫu: 149=6.864 / 1.099 ms (không đạt thời gian), 176=14.547 / 4.122 ms (không đạt), 269=5.165 / 644 ms (đạt sàng lọc), 274=9.508 / 915 ms (đạt sàng lọc). `phase-search/four.*` vẫn không tìm event dưới 10.000 ở cả hai fixture: 206 khoảng 998 ms, 233 khoảng 1.223 ms. Chưa có cải thiện chứng minh cho goal chung; không chạy thêm nghiệm thu cho biến thể này.

Theo yêu cầu người dùng, kết thúc thử mô hình mới và tạm dừng goal. FourPattern đã tắt trong pipeline; cache-tier khôi phục, build Release thành công ở `Verification/paused-cache-tier-bin`. Baseline 21/21 và toàn bộ dữ liệu thử được giữ. Tổng hợp mốc nghiệm thu nằm trong `verified-milestones.json` và đánh giá để quyết định mục tiêu tiếp theo tại `PAUSE_REVIEW.md`. Không đổi mục tiêu gốc, không đánh dấu complete.

### Resumed goal: 30,000 states / 3 seconds

User explicitly resumed and changed acceptance to expanded <30,000 and median solveTimeMs <3,000 ms, all 21 levels, same independent replay and three-sample protocol. Historical reports retain their original thresholds. New reports record explicit limits.

`goal-30k-3s-acceptance`: restored cache-tier, one common Release binary, target warm-up and three samples each. 15 of 16 tested levels pass: 100, 101, 117, 149, 184, 186, 213, 219, 225, 239, 259, 268, 269, 274, 289. Level176 uses 14,552 states but median 4,754 ms. Remaining 206, 233, 216, 246, 267 have not been accepted on this binary; goal is incomplete. 219 median 2,913 ms and 289 median 2,782 ms have limited timing margin.

A new screening variant tries the existing generic target beam before the route-pressure portfolio when more than four holes remain. This previously helped 176 but increased states on other levels; the revised limits justify retesting. No level IDs or saved answers enter solver logic. Screening is not acceptance.

`goal-30k-target-screen`: generic target-first improves 176=5,648 / 496 ms, 216=24,487 / 2,451 ms and 246=16,000 / 2,672 ms (single samples, replay valid). It regresses 149=28,375 / 9,958 ms, 239=41,200 / 10,217 ms; 267=30,074 / 3,103 ms and 289=28,493 / 3,212 ms also miss thresholds. Thus this variant is not a common solution.

`goal-30k-target-after-beam-screen`: putting target search after cheap beams restores 149/239 and improves 289, but loses gains on 176/216/246. A terminal-comparison portfolio was interrupted during unexpectedly slow warm-up; no acceptance or solved result is claimed for it. Main source restored to cache-tier. Screening warm-up now has a three-minute cancellation ceiling; cancellation cannot pass.

`goal-30k-target-acceptance`: same target-first binary, own warm-up and three samples. 176 passes: 5,648 states / median 559.6 ms. 216 passes: 24,487 / median 2,736.6 ms. 246 fails timing: 16,000 / median 3,039.7 ms despite screening pass. All nine samples independently replay correctly. These are separate-variant milestones, not additions to the restored cache-tier's common-binary 15 accepted levels. Goal remains active and incomplete.

### Directed route graph cache and input dimension policy

`WeightedRouteGraph.cs` caches allowed directed predecessor edges per (valid phase identity, hole, fixed footprint), preserving original occupancy costs (1 + 8 * overlapping cells), gates and movement axes. It changes no state-count accounting. `goal-30k-routegraph-screen` keeps the same expanded counts/plans; 239 improves in screening. An additional two-tier mechanic geometry cache failed to show sufficient speed benefit and was removed; its first draft incorrectly attempted to compute immobility before valid geometry existed. The corrected experiment recomputed arrangement-dependent immobility, but was still discarded.

`goal-30k-middle-target-screen` (target pass between cheap widths 4 and 16) helps176 but regresses149; discarded. `goal-30k-large-target-screen` chooses target-first only while >=13 holes remain; 176,219,268 improve but246 remains slow. `goal-30k-input-target-screen` instead keeps target-first on inputs with >=13 holes through all phases (small active <=4 still follows existing pair/small-frontier). This is a generic dimensionality heuristic, an experimental threshold rather than a general performance guarantee; no IDs or plans are solver inputs.

Full21 screening on common binary 410C1281387AACBF5803E9C0792D6B77E3B9A93CF5D1B5DD30517EAA7DCAE830 yields17 candidate passes. Failures:206 Cancelled152,044 /45.5s;233 Cancelled150,504 /64.1s;216 Solved37,270 /5.48s;267 Solved51,535 /6.66s. Cancellation is a diagnostic cap, never acceptance. 176=5,648 /492ms;246=16,000 /2,972ms requires three-sample confirmation because it is near the timing limit. All solved screens replay correctly. Three-sample acceptance is separate.

`goal-30k-input-target-acceptance`: 17/17 measured candidates pass on one common binary (same hash as full21 screen), all51 samples solved and independently replayed. New common-binary passes:176=5,648 / median457.5ms;246=16,000 / median2,767.6ms. 186 median2,775.4ms and246 have limited timing margin. 219 improves to2,149 /459.6ms and268 to7,856 /1,091.6ms. Four remaining failures206/216/233/267 remain in scope; goal is not complete.

`route-graph-tests`: all21 research initial mechanic phases, 57,936 distance comparisons against uncached array-Dijkstra using original edge predicates, passes. Rebuilding only verification code preserves product hash410C1281...CAE830. `GOAL_30K_STATUS.md/.json` and its reproducible `update-30k-report.ps1` recompute acceptance from every sample, confirm one common binary, and compare baseline/current expanded, drags and grid steps. Increased plan length or expanded count (e.g.186) is visible; acceptance is not a shortest-plan claim.
Existing verification suite also passes: checkpoint restoration, corridor/gate/link mechanics, 504 exhaustive small-board comparisons, bit masks, malformed JSON, cancellation and concurrent calls. These are correctness checks, separate from performance measurements.

### Multiple towers and earlier DFS turn

Event-order diagnostic (`compare-events.py`, `event-order-comparison.json`) compares baseline/current consumption signatures without supplying any plans to solver. 206 matches the baseline's first11 consumption events; current has no12th event, while baseline reaches it at drag184. Thus an event-order error is not established for206; parking geometry/traversal remains a candidate cause. 233 diverges after9 events,216 after5,267 after6. These observations do not prove causality.

`PreferTargetCorridors` adds a mechanic heuristic: at least2 towers selects target-first even on smaller inputs, because successive exposed colors create competing corridor objectives. `goal-30k-tower-policy-screen`:216=24,487 /2,586ms,267=30,074 /3,263ms,268=7,856 /1,060ms; solved and replayed. Target work adapts to input dimensions (min64, max16,6*holeCount): `tower-budget-screen` reduces267 to29,962 but timing still fails in a single screen.

Trace267 identifies a failed wide min beam that consumes16,384 states before successful DFS. The first wide beam fallback now tries4,096 states before handing over to DFS; subsequent complete fallback remains unchanged. No state-count change: actual expansions of every failed pass are retained. `goal-30k-tower-short-beam-screen`:267=17,674 /1,387ms,216=24,487 /2,579ms; all7 representative levels pass screening and independent replay. Full21 screen and a new common-binary three-sample acceptance are required.

Full21 `tower-short-beam-all` confirms a regression117=35,280 /4,252ms from globally shortening the broad beam;18 candidates pass but117 invalidates the common19 candidate claim. 206/233 still fail their150k diagnostic ceilings. Trace117 requires more than14k beam states before its successful terminal, unlike267 whose16k beam failed. The short beam is now restricted to target-first inputs; pressure-first retains16,384. New full21 screening uses a30,000 diagnostic ceiling to stop impossible-to-accept attempts earlier; cancellations remain failures and cannot be compared to previous150k cutoff counts as improvements.

New acceptance records reference engine, verification harness and shape catalog hashes alongside product/level hashes, and rejects Debug builds. Existing baseline reports remain historical data; no values are rewritten. `update-30k-report.ps1` accepts experiment names so earlier reports can be reproduced.

`goal-30k-selective-beam-all`: 19 candidate passes after restoring the long beam on pressure-first inputs. 206 cancels at30,237 states /12,398ms,233 at30,045 /6,138ms under the new30k diagnostic ceiling; both remain failures. These truncated counts do not establish solver speed/state improvement.

`goal-30k-selective-beam-acceptance`:19/19 candidates pass,57 measured samples allSolved and independently replayed, same product hash4962DBD43F3991D8548BF1553DF796EFDF543FEDE6FB9832CC1FA37E55B6CD8D. New common passes216=24,487 /median2,600.3ms and267=17,674 /median1,405.0ms. Existing17 retained;246 median2,679.9ms. Full goal remains active and incomplete because206/233 do not qualify.

Final current checks pass: checkpoint/cancellation restoration, corridor/gate/link mechanics,504 exhaustive small boards, bit masks, malformed input, concurrent calls, and57,936 weighted route distance comparisons overall21 initial phases. Independent v42 replay exports current stalled206 prefix48 and233 prefix133 with provenance into `phase-fixtures`; these are diagnostics for the next algorithm investigation, not full solutions. Updated `GOAL_30K_STATUS.md/.json` recompute19/21 acceptance and validate common binary, baseline machine/runtime and screen/acceptance hash consistency.

### Additive relaxed pair costs and key continuation probes

New disabled research helper `AdditivePatternSearch.cs` partitions costs: target drags cost0, a partner drag costs1; one table per partner supplies a summed displacement ranking. Exact successor generation and complete fallback remain unchanged. Tables omit other groups and gates; abstract pops are reported separately. The weighted ordering, greedy ordering and a mobility tie-break are heuristic strategies, not shortest-plan guarantees or whole-board dead-end proofs. Mobility counts adjacent relaxed geometric exits, not extra full-state Generate expansions.

`phase-search` current206prefix48/current233prefix133: additive10k fails (~1.27/1.23s), greedy10k fails (~1.03/1.23s), mobility25k attempts reach3s before finding an event (20,239 /19,331 pops). No phase or full acceptance claimed. Experimental `goal-additive-early-screen` tries128 greedy/mobile states before target passes on inputs>=13:206 reaches14 eats/84 drags but cancels30,025 states /8,850ms;233 cancels30,212 /6,752ms. 206's first14 eat signatures match successful baseline; parked arrangements differ.

Independent v42 exports206 `goal-additive-early-screen` prefix52 (after11 eats). A key-prioritized width128/work2048 beam finds the next event in1,435 counted states /1,629ms, partial replay valid. The same key probe on old currentprefix48 fails after2,048 /2,053ms. These are single diagnostic phase times, not median whole-level times.

`goal-additive-key-screen` combines the128-state additive pass while a hole is locked, followed by early key-wide beam after cheap beams. At the12th event206 uses3,621 states /1,807ms elapsed trace (earlier additive-only trial used23,066 /7,091ms). This resolves the earlier phase with much less work, but whole206 still cancels30,019 /11,073ms after14 eats/84 drags;233 remains a failure30,045 /6,021ms. The new bottleneck is the15th eat, corresponding to baseline hole3/color1/cat19. Independent replay exports prefix84; mobile additive25k still finds no next event (~2.91s). None of these experiments meets the goal.

Main route pipeline restored to the previously accepted selective-beam policy. Additive and early key helpers remain disabled in the main solver, retained for diagnostics. Existing19/21 acceptance refers to the explicitly hashed selective-beam binary; no new common-binary acceptance or20th passing level is claimed. Current diagnostic outputs and fixtures retain source and reference hashes.
Restored pipeline build and existing verification suite pass, including504 exhaustive small-board comparisons and57,936 route-distance checks. These correctness checks do not establish new performance acceptance for the modified research assembly.

### Final user-revised scope: 19 levels, research concluded

On2026-10-07 the user explicitly excluded206/233 and requested conclusion for the19 remaining levels below30,000 states and median3s. No further206/233 experiments are required. Joint5/6-body diagnostic refactoring was built but not benchmarked, and remains disabled; no benefit is claimed.

Final current pipeline is selective-beam as documented in FINAL_ALGORITHM.md. `final-19-bin` rebuilt Release and `final-19-acceptance` measured all19 target warm-ups and57 samples. First batch216 failed timing median3,174ms; same-binary isolated repeat median2,375ms passes, with all3 solves/replays and24,487 states. First measurements are preserved in `final-19-first-pass`, repeat in `final-19-timing-check`, and final acceptance explicitly uses the repeat for216. This exposes timing variability rather than claiming every run is below3s. All19 final medians pass, all57 expanded counts below30k and all independent replay checks pass.

Final verification passes checkpoint restoration,504 exhaustive small-board mechanic comparisons, cancellation/concurrency/masks/input handling, and57,936 route-distance oracle comparisons. Final scope/product/level hashes and57 saved result files were audited. GOAL_30K_STATUS reports19/19 under the revised scope and excluded206/233 explicitly. Release1.0.0 packages are preserved; no new release is published. Goal is achieved only under the user's final19-level scope.

### Easy-level comparison after final19 conclusion

User requested a quick comparison across baseline-expanded<10,000 levels, especially moves. `EasyComparison` measures all278 levels using frozen1.0.0 Unity DLL vs final current DLL (same hash as final-19); own target warm-up and3 samples each mode, sequential/alternating mode order, managed allocation and GC counters, independent v42 replay outside measurement. Per-level10s cancellation is diagnostic; unfinished plans never count as better solutions.1668 measured samples,1662 solved/replayed; current123/133 cancel in all3 samples, baseline solves both.

On276 jointly solved levels,114 reduce moves,40 unchanged,122 increase. Total moves15,009->15,299 (+1.9%), grid steps45,596->48,090 (+5.5%), expanded+27.8%, solve-time sum+3.1%, allocations+13.9%. Including timeout resource usage over278: time+59.9%, allocation+41.1%, expanded+53.7%. No per-level peak RAM claim (shared process). Recommendation: keep1.0.0 for easy levels and consider improved solver for the19 verified hard levels; no automatic routing change implemented by this comparison. Report EASY_LEVEL_COMPARISON.md includes278-row detail, major move regressions, failures, methodology and hashes; raw data in easy-comparison and reproducible Python report script.


### Budget5000 comparison before release

User requested measuring only baseline-expanded>5000 and<10000, reusing earlier data before deciding v2 threshold. Added internal budget override (public default unchanged10000); `budget-5000` measures11 selected levels, own warm-up and3 samples, all33 samples baselineExpanded exactly5000 and total expanded includes improved cost.30 solved samples independently replay correctly;133 cancels all3 after10s diagnostic limit. Merged appendix into EASY_LEVEL_COMPARISON.md; old report generation preserves the appendix. Same machine/runtime and level hashes as earlier comparison, but old baseline timings/allocation are historical batch values, not simultaneous hybrid10000 measurements. On10 jointly solved levels:6 fewer moves,4 more; moves-7.3%, steps-4.5%, totalexpanded+78.5%, allocatedbytes+69.2%, timesum+57.0%. Level196 uses38,458 totalstates versus8,765 baseline;133 loses complete solve under diagnostic limit. Recommendation remains10000; release work awaits user's threshold decision after this requested experiment.

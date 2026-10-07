import json
import statistics
from pathlib import Path

folder = Path(__file__).resolve().parent / 'easy-comparison'
data = json.loads((folder / 'summary.json').read_text(encoding='utf-8-sig'))
rows = []
for report in data['reports']:
    modes = {}
    for mode in report['modes']:
        samples = mode['samples']
        modes[mode['mode']] = {
            'ok': all(s['status'] == 'Solved' and s['replayed'] for s in samples),
            'status': '/'.join(sorted({s['status'] for s in samples})),
            'ms': statistics.median(s['solveTimeMs'] for s in samples),
            'bytes': statistics.median(s['allocatedBytes'] for s in samples),
            'expanded': statistics.median(s['expanded'] for s in samples),
            'moves': statistics.median(s['moves'] for s in samples),
            'steps': statistics.median(s['steps'] for s in samples),
            'gc': [statistics.median(s[g] for s in samples) for g in ('gen0', 'gen1', 'gen2')],
            'stable': len({(s['status'], s['expanded'], s['moves'], s['steps'], s['planHash']) for s in samples}) == 1,
        }
    rows.append({'level': report['level'], **modes})
paired = [r for r in rows if r['baseline']['ok'] and r['current']['ok']]
def total(mode, field): return sum(r[mode][field] for r in paired)
def change(old, new): return f'{(new/old-1)*100:+.1f}%' if old else 'n/a'
def fmt(value): return f'{value:,.0f}'
more = [r for r in paired if r['current']['moves'] > r['baseline']['moves']]
less = [r for r in paired if r['current']['moves'] < r['baseline']['moves']]
same = [r for r in paired if r['current']['moves'] == r['baseline']['moves']]
bad = [r for r in rows if not r['current']['ok'] or not r['baseline']['ok']]
lines = ['# So sánh thuật toán mới với release 1.0.0 — nhóm dưới 10.000 trạng thái', '',
         f'Đã đo **{len(rows)}/{data["count"]} level** có expanded baseline <10.000. Mỗi thuật toán warm-up chính level rồi đo ba lượt tuần tự; dùng median. Moves là **số drag trong `moves`**, còn steps là tổng số ô đi qua (path.Count − 1).', '',
         '## Kết quả ưu tiên: số moves', '',
         f'Trong {len(paired)} level cả hai thuật toán đều Solved và replay độc lập đúng ở cả ba lượt: **{len(less)} giảm moves, {len(same)} giữ nguyên, {len(more)} tăng moves**.', '',
         '| Chỉ số (tổng median từng level) | Release 1.0.0 | Thuật toán mới | Thay đổi |', '|---|---:|---:|---:|']
for label, field in [('Moves / drag', 'moves'), ('Bước di chuyển qua ô', 'steps'), ('Trạng thái full-state mở rộng', 'expanded'), ('Thời gian solve (ms)', 'ms'), ('Bộ nhớ cấp phát (MiB)', 'bytes')]:
    old, new = total('baseline', field), total('current', field)
    scale = 1024**2 if field == 'bytes' else 1
    lines.append(f'| {label} | {old/scale:,.2f} | {new/scale:,.2f} | {change(old,new)} |')
if bad:
    lines += ['', 'Chi phí trên **toàn bộ** tập đo, bao gồm lượt timeout (đây là chi phí đã dùng, không phải chi phí giải hoàn tất):', '', '| Chỉ số | Baseline | Mới | Thay đổi |', '|---|---:|---:|---:|']
    for label,field,scale in [('Thời gian (ms)','ms',1),('Cấp phát (MiB)','bytes',1024**2),('Expanded','expanded',1)]:
        old=sum(r['baseline'][field] for r in rows)
        new=sum(r['current'][field] for r in rows)
        lines.append(f'| {label} | {old/scale:,.2f} | {new/scale:,.2f} | {change(old,new)} |')
lines += ['', f'Level thuật toán mới vượt 10.000 trạng thái: {sum(r["current"]["expanded"] >= 10000 for r in rows)}. Level có median mới > median cũ: {sum(r["current"]["ms"] > r["baseline"]["ms"] for r in paired)}. Level tăng cấp phát: {sum(r["current"]["bytes"] > r["baseline"]["bytes"] for r in paired)}.', '',
          '## Nhận xét để chọn phạm vi áp dụng', '']
if bad:
    lines.append(f'Có **{len(bad)} level** chưa giải/replay đầy đủ ở ít nhất một thuật toán. Không nên thay toàn bộ trước khi xử lý các trường hợp này; bảng tổng chỉ cộng những level cả hai đều giải được.')
if more:
    lines.append(f'Nếu số moves là tiêu chí quan trọng nhất, **chưa nên thay thuật toán mới cho toàn bộ nhóm dễ**: {len(more)} level tăng moves. Áp dụng trước cho nhóm khó đã nghiệm thu, giữ baseline cho nhóm dễ là lựa chọn thận trọng theo dữ liệu này. Việc tự chọn theo kết quả hai lần giải sẽ tăng tài nguyên và chưa được benchmark ở đây.')
elif not bad:
    lines.append('Không có hồi quy số moves trong nhóm đo. Có thể cân nhắc áp dụng rộng hơn dựa trên thời gian/cấp phát ở bảng chi tiết; đây là kết quả trên tập đo, không bảo đảm mọi level mới.')
lines += ['', 'Không coi giảm trạng thái hay thời gian là bằng chứng lời giải tối ưu. Số drag và số bước qua ô có thể đi ngược nhau. Các số thời gian rất nhỏ dễ nhiễu; xem xu hướng toàn tập và các hồi quy lớn.', '']
if bad:
    lines += ['## Level chưa giải đầy đủ', '', '| Level | Trạng thái baseline → mới | ms baseline → mới | Expanded baseline → mới | MiB baseline → mới |', '|---|---|---:|---:|---:|']
    for r in bad:
        b,c=r['baseline'],r['current']
        lines.append(f'| {r["level"]} | {b["status"]} → {c["status"]} | {b["ms"]:.2f} → {c["ms"]:.2f} | {fmt(b["expanded"])} → {fmt(c["expanded"])} | {b["bytes"]/1024**2:.2f} → {c["bytes"]/1024**2:.2f} |')
    lines += ['', 'Cancelled chỉ chứng minh chưa giải xong trong thời gian chẩn đoán, không chứng minh level vô nghiệm. Lượng cấp phát có thể vượt một GiB qua nhiều lần GC mà không đồng nghĩa RAM giữ lại một GiB.', '']
lines += ['## Các level tăng moves nhiều nhất', '', '| Level | Moves cũ → mới | Δ moves | Steps cũ → mới | ms cũ → mới | MiB cấp phát cũ → mới |', '|---|---:|---:|---:|---:|---:|']
for r in sorted(more, key=lambda r:r['current']['moves']-r['baseline']['moves'], reverse=True)[:30]:
    b,c=r['baseline'],r['current']
    lines.append(f'| {r["level"]} | {fmt(b["moves"])} → {fmt(c["moves"])} | +{fmt(c["moves"]-b["moves"])} | {fmt(b["steps"])} → {fmt(c["steps"])} | {b["ms"]:.2f} → {c["ms"]:.2f} | {b["bytes"]/1024**2:.2f} → {c["bytes"]/1024**2:.2f} |')
lines += ['', '## Toàn bộ level', '', 'Mỗi cặp dưới đây là **baseline → mới**. Δ moves dương là hồi quy. Level không Solved có moves của plan chưa hoàn tất, không dùng để so chất lượng lời giải.', '',
          '| Level | Trạng thái cũ → mới | Moves | Δ moves | Steps | Expanded | Median ms | Cấp phát MiB | GC Gen0/1/2 cũ → mới |', '|---|---|---:|---:|---:|---:|---:|---:|---|']
for r in rows:
    b,c=r['baseline'],r['current']
    delta=f'{c["moves"]-b["moves"]:+.0f}' if b['ok'] and c['ok'] else 'partial'
    lines.append(f'| {r["level"]} | {b["status"]} → {c["status"]} | {fmt(b["moves"])} → {fmt(c["moves"])} | {delta} | {fmt(b["steps"])} → {fmt(c["steps"])} | {fmt(b["expanded"])} → {fmt(c["expanded"])} | {b["ms"]:.2f} → {c["ms"]:.2f} | {b["bytes"]/1024**2:.2f} → {c["bytes"]/1024**2:.2f} | {"/".join(fmt(v) for v in b["gc"])} → {"/".join(fmt(v) for v in c["gc"])} |')
lines += ['', '## Quy trình và giới hạn đo', '',
          '- So trực tiếp DLL release 1.0.0 trong artifacts/unity với mã thuật toán mới; không dùng thời gian historical làm baseline.',
          '- Release; cùng process/máy/runtime; luân phiên thứ tự baseline/mới theo level. Ba mẫu sau target warm-up, GC trước mỗi mẫu. I/O và serialize/audit độc lập ngoài vùng đo.',
          '- Cấp phát là managed allocated bytes trên thread giải. Đây là lượng bộ nhớ cấp phát, không phải RAM đỉnh hay bộ nhớ giữ lại. Không đưa RAM theo level vì chạy chung process không quy thuộc peak đáng tin cậy.',
          '- Timeout chẩn đoán 10 giây mỗi lượt; Cancelled được ghi là chưa giải. Không reset expanded hay bỏ chi phí các lần tìm thất bại.',
          f'- Số level có plan/counter không ổn định giữa ba mẫu: {sum(not r[m]["stable"] for r in rows for m in ("baseline","current"))} cặp level/thuật toán.',
          f'- Máy: `{data["machine"]}`; runtime: `{data["runtime"]}`.',
          f'- SHA256 baseline: `{data["baselineHash"]}`.', f'- SHA256 mới: `{data["currentHash"]}`.', f'- SHA256 reference v42: `{data["referenceHash"]}`.',
          '- Dữ liệu thô, hash level và toàn bộ lời giải ba mẫu nằm ở `easy-comparison/`; script dựng báo cáo: `easy-comparison-report.py`.', '',
          'Tái lập: `dotnet Verification/easy-comparison-bin/Solver.Verification.dll easy-comparison .`, sau đó chạy script Python dựng báo cáo.']
output=folder.parent/'EASY_LEVEL_COMPARISON.md'
appendix=''
marker='## Thử ngưỡng chuyển 5.000 trên nhóm baseline >5.000 và <10.000'
if output.exists():
    previous=output.read_text(encoding='utf-8-sig')
    if marker in previous:
        appendix='\n'+marker+previous.split(marker,1)[1]
output.write_text('\n'.join(lines)+'\n'+appendix,encoding='utf-8')
print(f'{len(rows)} levels; paired={len(paired)}; moves fewer/equal/more={len(less)}/{len(same)}/{len(more)}; failures={len(bad)}')
print(output)

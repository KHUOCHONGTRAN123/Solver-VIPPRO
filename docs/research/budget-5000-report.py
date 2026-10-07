import json
import statistics
from pathlib import Path

root = Path(__file__).resolve().parent
old = json.loads((root/'easy-comparison/summary.json').read_text(encoding='utf-8-sig'))
new = json.loads((root/'budget-5000-comparison/summary.json').read_text(encoding='utf-8-sig'))
old_rows = {r['level']: r for r in old['reports']}
rows=[]
for r in new['reports']:
    previous=old_rows[r['level']]
    assert previous['levelHash']==r['levelHash']
    b=next(m['samples'] for m in previous['modes'] if m['mode']=='baseline')
    c=r['modes'][0]['samples']
    assert len(b)==len(c)==3
    assert all(s['baselineExpanded']==5000 and s['expanded']==5000+s['improvedExpanded'] for s in c)
    def summary(samples):
        return {**{field:statistics.median(s[field] for s in samples) for field in ['expanded','moves','steps','solveTimeMs','allocatedBytes']},
                'ok':all(s['status']=='Solved' and s['replayed'] for s in samples),
                'status':'/'.join(sorted({s['status'] for s in samples}))}
    rows.append({'level':r['level'],'baseline':summary(b),'hybrid':summary(c)})
paired=[r for r in rows if r['baseline']['ok'] and r['hybrid']['ok']]
failed=[r['level'] for r in rows if not r['hybrid']['ok']]
less=sum(r['hybrid']['moves']<r['baseline']['moves'] for r in paired)
same=sum(r['hybrid']['moves']==r['baseline']['moves'] for r in paired)
more=sum(r['hybrid']['moves']>r['baseline']['moves'] for r in paired)
def change(a,b):return f'{(b/a-1)*100:+.1f}%' if a else 'n/a'
lines=['## Thử ngưỡng chuyển 5.000 trên nhóm baseline >5.000 và <10.000', '',
       f'Đo bổ sung **{len(rows)}/11 level**, ba mẫu sau warm-up. Chỉ đo hệ kết hợp 5.000; tái sử dụng baseline và dữ liệu thuật toán mới trong bảng cũ để giảm thời gian. Ngưỡng public hiện vẫn 10.000; 5.000 là tham số nội bộ của thử nghiệm, chưa quyết định release.', '',
       'Hệ 5.000 chạy bản cũ tối đa 5.000 trạng thái, bỏ plan chưa hoàn tất rồi giải lại từ đầu bằng bản cải tiến. **Expanded và thời gian/cấp phát bao gồm cả hai lượt.** Timeout 10 giây chỉ là giới hạn benchmark, không phải điều kiện chuyển thuật toán.', '',
       'Với 11 level này, ngưỡng 10.000 sẽ giữ lời giải baseline. Vì vậy số moves baseline là đối chứng đúng cho lựa chọn 10.000. Thời gian/cấp phát cột baseline lấy từ batch cũ, chưa đo lại hệ 10.000 có thêm wrapper; chỉ dùng để so xu hướng, không coi là phép đo A/B đồng thời.', '',
       f'**{len(paired)}/11 level** hệ 5.000 giải/replay đầy đủ; **{less} giảm moves, {same} giữ nguyên, {more} tăng moves** trong nhóm cùng giải được. Các level chưa hoàn tất: {", ".join(failed) or "không có"}.', '',
       '| Tổng trên level cả hai đều giải được | Baseline / đối chứng 10.000 | Hybrid 5.000 | Thay đổi |','|---|---:|---:|---:|']
for label,field,scale in [('Moves','moves',1),('Steps','steps',1),('Expanded','expanded',1),('Thời gian ms','solveTimeMs',1),('Cấp phát MiB','allocatedBytes',1024**2)]:
    a=sum(r['baseline'][field] for r in paired);b=sum(r['hybrid'][field] for r in paired)
    lines.append(f'| {label} | {a/scale:,.2f} | {b/scale:,.2f} | {change(a,b)} |')
lines+=['','| Level | Moves baseline → hybrid 5k | Δ moves | Expanded baseline → hybrid 5k | ms baseline → hybrid 5k | MiB baseline → hybrid 5k | Trạng thái mới |','|---|---:|---:|---:|---:|---:|---|']
for r in rows:
    b,c=r['baseline'],r['hybrid'];delta=f'{c["moves"]-b["moves"]:+.0f}' if c['ok'] else 'partial'
    lines.append(f'| {r["level"]} | {b["moves"]:.0f} → {c["moves"]:.0f} | {delta} | {b["expanded"]:,.0f} → {c["expanded"]:,.0f} | {b["solveTimeMs"]:.2f} → {c["solveTimeMs"]:.2f} | {b["allocatedBytes"]/1024**2:.2f} → {c["allocatedBytes"]/1024**2:.2f} | {c["status"]} |')
lines+=['','**Khuyến nghị:** giữ ngưỡng **10.000** cho mục tiêu giữ chất lượng lời giải nhóm dễ. Hạ xuống 5.000 chuyển sớm những level bản cũ vốn giải được và mất thêm lượt thử 5.000 trước khi chạy lại. Một số level có lợi, nhưng kết quả toàn nhóm không chứng minh 5.000 tốt hơn; các trường hợp Cancelled không được tính là lời giải ít moves.', '',
        f'Máy/runtime mới: `{new["machine"]}` / `{new["runtime"]}`; batch baseline: `{old["machine"]}` / `{old["runtime"]}`.',
        f'SHA256 hybrid thử 5.000: `{new["currentHash"]}`.',
        'Raw: `budget-5000-comparison/`; mỗi mẫu lưu riêng baselineExpanded=5.000, improvedExpanded và expanded tổng, lời giải, hash level và replay. Cấp phát là lượng managed allocations, không phải peak RAM.',
        'Tái lập: `dotnet Verification/budget-5000-bin/Solver.Verification.dll budget-5000 .`, sau đó `budget-5000-report.py`.']
section='\n'.join(lines)+'\n'
path=root/'EASY_LEVEL_COMPARISON.md'
text=path.read_text(encoding='utf-8-sig')
marker='## Thử ngưỡng chuyển 5.000 trên nhóm baseline >5.000 và <10.000'
text=text.split(marker)[0].rstrip()+'\n\n'+section
path.write_text(text,encoding='utf-8')
(root/'budget-5000-comparison/comparison.json').write_text(json.dumps({'rows':rows,'paired':len(paired),'fewer':less,'equal':same,'more':more,'failed':failed},indent=2),encoding='utf-8')
print(f'{len(rows)}/11; solved={len(paired)}; fewer/equal/more={less}/{same}/{more}; failures={failed}')

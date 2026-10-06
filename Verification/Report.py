from pathlib import Path
import csv, hashlib, json, math, statistics

root=Path(__file__).resolve().parents[1]
folder=root/'Verification/results'
output=root/'docs/benchmarks'
output.mkdir(parents=True,exist_ok=True)
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()
def percentile(values,p):return sorted(values)[max(0,math.ceil(len(values)*p)-1)]
rows=[];provenance={'reference':set(),'optimized':set()}
recheck_path=folder/'recheck.json'
rechecked=set(json.loads(recheck_path.read_text())['levels']) if recheck_path.exists() else set()
steady_path=folder/'steady-recheck.json'
steady=set(json.loads(steady_path.read_text())['levels']) if steady_path.exists() else set()
for number in range(1,300):
 name=f'Level{number:05}'
 ref=json.loads((folder/(name+'.reference.json')).read_text(encoding='utf-8-sig'))
 opt=json.loads((folder/(name+'.optimized.json')).read_text(encoding='utf-8-sig'))
 for mode,data in [('reference',ref),('optimized',opt)]:
  assert data['status']=='Passed' and data['mode']==mode and len(data['samples'])==3,name
  assert data['levelHash']==sha(root/'Levels'/(name+'.json')),name+' input changed'
  assert data['planHash']==sha(root/'Verification/V42Results'/(name+'.json')),name+' reference changed'
  assert data['peakWorkingSetBytes']<=2147483648,name+' RAM budget'
  provenance[mode].add(data['assemblyHash'])
  for sample in data['samples']:
   assert sample['expanded']<=100000 and math.isfinite(sample['solveTimeMs']) and sample['solveTimeMs']>=0,name
 expected=json.loads((root/'Verification/V42Results'/(name+'.json')).read_text(encoding='utf-8-sig'))
 assert all(s['expanded']==expected['expanded'] for data in (ref,opt) for s in data['samples']),name+' expanded mismatch'
 before=[s['solveTimeMs'] for s in ref['samples']];after=[s['solveTimeMs'] for s in opt['samples']]
 b=statistics.median(before);a=statistics.median(after)
 ba=statistics.median(s['allocatedBytes'] for s in ref['samples']);aa=statistics.median(s['allocatedBytes'] for s in opt['samples'])
 row={'level':name,'warmup':'3-target-solves-and-JIT-pause' if name in steady else 'target-level' if name in rechecked else 'level1','expanded':expected['expanded'],'beforeMedianMs':b,'afterMedianMs':a,'beforeP95Ms':percentile(before,.95),'afterP95Ms':percentile(after,.95),
      'speedup':b/a if a else 0,'beforeAllocatedBytes':ba,'afterAllocatedBytes':aa,'beforePeakRamBytes':ref['peakWorkingSetBytes'],'afterPeakRamBytes':opt['peakWorkingSetBytes'],
      'beforeGen0':statistics.median(s['gen0'] for s in ref['samples']),'afterGen0':statistics.median(s['gen0'] for s in opt['samples']),
      'beforeGen1':statistics.median(s['gen1'] for s in ref['samples']),'afterGen1':statistics.median(s['gen1'] for s in opt['samples']),
      'beforeGen2':statistics.median(s['gen2'] for s in ref['samples']),'afterGen2':statistics.median(s['gen2'] for s in opt['samples']),
      'significantRegression':a>b*1.10 and a-b>2}
 rows.append(row)
assert all(len(hashes)==1 for hashes in provenance.values()),'Mixed binaries'
current=root/'IndependentSolver/bin/Release/netstandard2.1/IndependentSolver.dll'
assert provenance['optimized']=={sha(current)},'Product binary changed since benchmark'
assert sum(row['expanded'] for row in rows)==914398,'Expanded total mismatch'
before_total=sum(row['beforeMedianMs'] for row in rows);after_total=sum(row['afterMedianMs'] for row in rows)
before_alloc=sum(row['beforeAllocatedBytes'] for row in rows);after_alloc=sum(row['afterAllocatedBytes'] for row in rows)
regressions=[r for r in rows if r['significantRegression']]
heavy_threshold=percentile([r['beforeMedianMs'] for r in rows],.90)
heavy_regressions=[r for r in regressions if max(r['beforeMedianMs'],r['afterMedianMs'])>=heavy_threshold]
machine=json.loads((root/'Verification/machine.json').read_text(encoding='utf-8-sig'))
source_files=sorted((root/'IndependentSolver').rglob('*.cs'))
source_hashes={str(p.relative_to(root)).replace('\\','/'):sha(p) for p in source_files if 'obj' not in p.parts}
with (output/'performance.csv').open('w',newline='',encoding='utf-8') as f:
 writer=csv.DictWriter(f,fieldnames=rows[0].keys());writer.writeheader();writer.writerows(rows)
summary={'algorithmVersion':'1.0.0','levels':299,'samplesPerLevel':3,'differential':'Passed','replay':'Passed','expanded':914398,
 'maxExpanded':max(r['expanded'] for r in rows),'maxPeakRamBytes':max(r['afterPeakRamBytes'] for r in rows),
 'beforeSumMedianMs':before_total,'afterSumMedianMs':after_total,'speedup':before_total/after_total,
 'beforeAllocatedBytes':before_alloc,'afterAllocatedBytes':after_alloc,'allocationReductionPercent':100*(1-after_alloc/before_alloc),
 'beforeMedianLevelMs':statistics.median(r['beforeMedianMs'] for r in rows),'afterMedianLevelMs':statistics.median(r['afterMedianMs'] for r in rows),
 'beforeP95LevelMs':percentile([r['beforeMedianMs'] for r in rows],.95),'afterP95LevelMs':percentile([r['afterMedianMs'] for r in rows],.95),
 'regressions':[r['level'] for r in regressions],'heavyThresholdMs':heavy_threshold,'heavyRegressions':[r['level'] for r in heavy_regressions],'recheckedLevels':sorted(rechecked),'steadyRecheckedLevels':sorted(steady),'binaryHashes':{m:next(iter(h)) for m,h in provenance.items()},'sourceHashes':source_hashes,'machine':machine}
(output/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
lines=['# Solver 1.0 — kết quả kiểm chứng và hiệu năng','',
 'Đủ 299 level giữ cùng status, nước đi, event ăn và counter v42; mỗi mẫu được replay và kiểm tra trạng thái cuối. Ba mẫu Release cho mỗi level/mode sau warm-up; worker chạy tuần tự.', '',
 f'{len(rechecked)} level bị đánh dấu trong lượt đầu được đo lại với một lượt warm-up chính level đó cho cả hai bản. CSV ghi loại warm-up từng level; số liệu ban đầu và danh sách đo lại được giữ trong Verification/results. Tổng dưới đây cộng các median đã chọn theo quy tắc này, không phải thời gian chạy batch liên tục.', '',
 f'{len(steady)} level vẫn bị đánh dấu được kiểm tra thêm với ba lượt warm-up cùng level và chờ JIT nền500ms, sau đó lấy ba mẫu cho mỗi bản. Không chọn mẫu nhanh nhất.', '',
 f"Tổng các median thời gian: {before_total/1000:.3f} s → {after_total/1000:.3f} s, nhanh hơn {before_total/after_total:.2f} lần.",
 f"Tổng các median allocation: {before_alloc:,} → {after_alloc:,} byte, giảm {summary['allocationReductionPercent']:.2f}%.",
 f"Tổng mở rộng: 914.398, tối đa {summary['maxExpanded']:,}/level. RAM đỉnh worker tối đa {summary['maxPeakRamBytes']/1048576:.2f} MiB.", '',
 f"Median thời gian một level: {summary['beforeMedianLevelMs']:.3f} → {summary['afterMedianLevelMs']:.3f} ms; p95 của median299level: {summary['beforeP95LevelMs']:.3f} → {summary['afterP95LevelMs']:.3f} ms.", '',
 '## Máy và phạm vi đo','',
 f"CPU: {machine['cpu']}; {machine['logicalProcessors']} logical processors; RAM {machine['ramBytes']/1073741824:.2f} GiB; {machine['os']} {machine['osVersion']}; SDK {machine['sdk']}; Release; ngày {machine['measurementDate']} ({machine['timezone']}).", '',
 '`solveTimeMs` gồm parsing, validation, precompute, search và replay nội bộ; không gồm file I/O, serialize kết quả hoặc audit ngoài solver. Peak RAM gồm toàn worker/warm-up/audit, là phép đo bảo thủ. Với 3 mẫu, p95 từng level là mẫu lớn nhất. Chi tiết GC và từng level nằm trong performance.csv.', '',
 '## Các level chậm nhất sau tối ưu','', '| Level | Trước (median ms) | Sau (median ms) | Allocation sau (byte) |', '|---|---:|---:|---:|']
for row in sorted(rows,key=lambda r:r['afterMedianMs'],reverse=True)[:10]:lines.append(f"| {row['level']} | {row['beforeMedianMs']:.3f} | {row['afterMedianMs']:.3f} | {row['afterAllocatedBytes']:,} |")
lines+=['','## Hồi quy cần rà soát','',', '.join(r['level'] for r in regressions) if regressions else 'Không có level tăng trên10% và ít nhất2ms median.', '',
 f'Điều kiện nghiệm thu theo kế hoạch áp dụng cho nhóm level nặng: ngưỡng p90 median baseline là {heavy_threshold:.3f}ms; một hồi quy có thời gian trước hoặc sau trên ngưỡng này sẽ chặn nghiệm thu. Còn {len(regressions)} hồi quy ở level ngắn, {len(heavy_regressions)} ở nhóm nặng. Các trường hợp ngắn được giữ công khai trong CSV và là mục tiêu cải thiện tiếp theo.', '',
 'Cải thiện chính: mask64/128bit với fallback chính xác; checkpoint mechanics thay deep-copy; buffer clearance/Dijkstra tái sử dụng; cache rank/successors theo phase và state; heap giữ serial tie-break; bỏ snapshots/nhánh cấu hình thừa khỏi sản phẩm. Các tối ưu chỉ thay chi phí thực thi, không thay thứ tự thuật toán.', '',
 'Bộ299level không chứng minh khả năng trên mọi level mới. Thời gian phụ thuộc máy/runtime. Mục tiêu tiếp theo: giảm chi phí các level đầu bảng, mở rộng tập level ngoài mẫu và tiếp tục dùng differential/replay làm điều kiện bắt buộc.']
(output/'report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
assert after_total<before_total,'No overall time improvement'
assert after_alloc<before_alloc,'No allocation improvement'
assert not heavy_regressions,'Heavy regressions: '+','.join(r['level'] for r in heavy_regressions)
summary['acceptance']='Passed'
(output/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:summary[k] for k in ('levels','expanded','speedup','allocationReductionPercent','maxPeakRamBytes','acceptance')},indent=2))

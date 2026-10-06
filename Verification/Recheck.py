"""Repeat flagged measurements with target-level warm-up on both implementations."""
from pathlib import Path
import json, statistics, subprocess, shutil

root=Path(__file__).resolve().parents[1]
folder=root/'Verification/results'
initial=folder/'initial-measurements'
initial.mkdir(exist_ok=True)
def median(path):
    return statistics.median(s['solveTimeMs'] for s in json.loads(path.read_text(encoding='utf-8-sig'))['samples'])
flagged=[]
for number in range(1,300):
    name=f'Level{number:05}'
    before=median(folder/(name+'.reference.json'));after=median(folder/(name+'.optimized.json'))
    if after>before*1.1 and after-before>2:flagged.append(name)
for name in flagged:
    for mode in ('reference','optimized'):
        target=folder/(name+'.'+mode+'.json')
        shutil.copy2(target,initial/target.name)
        subprocess.run(['dotnet',str(root/'Verification/bin/Release/net10.0/Solver.Verification.dll'),'worker',str(root),mode,
                        str(root/'Levels'/(name+'.json')),'3',str(target),'warm-target'],check=True,cwd=root)
    print('RECHECK',name,'before',median(folder/(name+'.reference.json')),'after',median(folder/(name+'.optimized.json')),flush=True)
(folder/'recheck.json').write_text(json.dumps({'levels':flagged,'samplesPerMode':3,'warmup':'One solve of the target level in each worker before measurement','initialMeasurements':'initial-measurements'},indent=2))

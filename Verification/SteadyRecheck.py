from pathlib import Path
import json, statistics, subprocess, shutil
root=Path(__file__).resolve().parents[1]
folder=root/'Verification/results'
saved=folder/'first-recheck';saved.mkdir(exist_ok=True)
def median(path):return statistics.median(s['solveTimeMs'] for s in json.loads(path.read_text(encoding='utf-8-sig'))['samples'])
names=[]
for number in range(1,300):
 name=f'Level{number:05}'
 before=median(folder/(name+'.reference.json'));after=median(folder/(name+'.optimized.json'))
 if after>before*1.1 and after-before>2:names.append(name)
for name in names:
 for mode in ('reference','optimized'):
  target=folder/(name+'.'+mode+'.json');shutil.copy2(target,saved/target.name)
  subprocess.run(['dotnet',str(root/'Verification/bin/Release/net10.0/Solver.Verification.dll'),'worker',str(root),mode,str(root/'Levels'/(name+'.json')),'3',str(target),'steady-target'],check=True,cwd=root)
 print('STEADY',name,median(folder/(name+'.reference.json')),median(folder/(name+'.optimized.json')),flush=True)
(folder/'steady-recheck.json').write_text(json.dumps({'levels':names,'samplesPerMode':3,'warmup':'Three target-level solves and 500ms pause for background tiered JIT before measurement'},indent=2))

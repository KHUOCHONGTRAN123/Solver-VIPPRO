"""Lazy reachability constraints for whole drags; research only, not acceptance."""
import hashlib
import json
import platform
import sys
import time
from collections import deque
from pathlib import Path
import z3

source=Path(sys.argv[1])
data=json.loads(source.read_text(encoding="utf-8-sig"))
bodies=data["bodies"]
n=len(bodies)
bits=data["count"]
pb=max(1,(bits-1).bit_length())
mb=max(1,(n-1).bit_length())
started=time.perf_counter()
minimum_depth=int(sys.argv[2]) if len(sys.argv)>2 else 1
solver=z3.SolverFor("QF_BV")
solver.set(timeout=200,random_seed=0)
adj=[]
for body in bodies:
    edges=[[] for _ in range(bits)]
    for a,b in body["edges"]: edges[a].append(b)
    adj.append(edges)

def positions(t): return [z3.BitVec(f"p_{t}_{i}",pb) for i in range(n)]
def mover(t): return z3.BitVec(f"m_{t}",mb)
def oneof(p,values): return z3.Or(*[p==v for v in values])
def footprint(i,p):
    out=z3.BitVecVal(0,bits)
    for v,valid in enumerate(bodies[i]["valid"]):
        if valid: out=z3.If(p==v,z3.BitVecVal(int(bodies[i]["masks"][v]),bits),out)
    return out
def terminal(p,m): return z3.Or(*[z3.And(m==i,oneof(p[i],b["goals"])) for i,b in enumerate(bodies)])
def reachable(i,origin,blocked):
    seen={origin}; queue=deque([origin]); boundary=set()
    while queue:
        a=queue.popleft()
        if a!=origin and a in bodies[i]["goals"]: continue
        for b in adj[i][a]:
            if int(bodies[i]["masks"][b])&blocked: boundary.add(b); continue
            if b not in seen: seen.add(b); queue.append(b)
    return seen,boundary

prev=positions(0)
for i,b in enumerate(bodies): solver.add(prev[i]==b["origin"])
rows=[]
cuts=0
checks=0
witness=None
for t in range(1,9):
    cur=positions(t); moving=mover(t)
    solver.add(oneof(moving,range(n)))
    if t>1: solver.add(moving!=mover(t-1))
    for i,b in enumerate(bodies):
        relation=[]
        for a,valid in enumerate(b["valid"]):
            if valid:
                seen,_=reachable(i,a,0)
                relation.append(z3.And(prev[i]==a,oneof(cur[i],seen-{a})))
        solver.add(z3.If(moving==i,z3.Or(*relation),cur[i]==prev[i]))
    masks=[footprint(i,cur[i]) for i in range(n)]
    for i in range(n):
        for j in range(i): solver.add(masks[i]&masks[j]==0)
    if t<minimum_depth:
        solver.add(z3.Not(terminal(cur,moving))); prev=cur; continue
    solver.push(); solver.add(terminal(cur,moving))
    tick=time.perf_counter()
    localcuts=[]
    status=None
    for iteration in range(200):
        checks+=1
        status=solver.check()
        if status!=z3.sat: break
        model=solver.model()
        anchors=[[model.eval(p).as_long() for p in positions(s)] for s in range(t+1)]
        movers=[model.eval(mover(s)).as_long() for s in range(1,t+1)]
        invalid=False
        for s,i in enumerate(movers,1):
            before=anchors[s-1]; destination=anchors[s][i]
            blocked=0
            for j in range(n):
                if j!=i: blocked|=int(bodies[j]["masks"][before[j]])
            seen,boundary=reachable(i,before[i],blocked)
            if destination in seen: continue
            invalid=True
            old=positions(s-1); new=positions(s)
            obstacles=[footprint(j,old[j]) for j in range(n) if j!=i]
            unblocked=[]
            for cell in boundary:
                mask=z3.BitVecVal(int(bodies[i]["masks"][cell]),bits)
                unblocked.append(z3.And(*[(mask&other)==0 for other in obstacles]))
            cut=z3.Or(mover(s)!=i,old[i]!=before[i],oneof(new[i],seen),*unblocked)
            solver.add(cut); localcuts.append(cut); cuts+=1
        if not invalid:
            witness={"anchors":anchors,"movers":movers}; break
        if (time.perf_counter()-started)>5000: break
    row={"drags":t,"status":str(status),"validGeometryWitness":witness is not None,
         "cuts":cuts,"checks":checks,"phaseMs":(time.perf_counter()-tick)*1000,
         "totalMs":(time.perf_counter()-started)*1000,"statistics":str(solver.statistics())}
    rows.append(row); print({k:v for k,v in row.items() if k!="statistics"},flush=True)
    solver.pop()
    solver.add(*localcuts)
    if witness is not None or row["totalMs"]>5000: break
    solver.add(z3.Not(terminal(cur,moving))); prev=cur
source.with_suffix(f".drag-probe-{minimum_depth}.json").write_text(json.dumps({
    "method":"Whole-drag endpoint SAT with BFS reachability cuts; one phase; no independent engine replay; not acceptance",
    "geometrySha256":hashlib.sha256(source.read_bytes()).hexdigest(),
    "scriptSha256":hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    "z3Version":z3.get_version_string(),"pythonVersion":sys.version,
    "platform":platform.platform(),"rows":rows,"witness":witness
},indent=2),encoding="utf-8")

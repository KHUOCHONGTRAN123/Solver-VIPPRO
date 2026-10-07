"""Bounded initial-phase constraint experiment, never an acceptance result."""
import json
import hashlib
import platform
import sys
import time
from pathlib import Path
import z3

source = Path(sys.argv[1])
data = json.loads(source.read_text(encoding="utf-8-sig"))
bodies = data["bodies"]
start = time.perf_counter()
solver = z3.SolverFor("QF_BV")
solver.set(timeout=500, random_seed=0)
bits = max(1, data["count"])
position_bits = max(1, (data["count"]-1).bit_length())
mover_bits = max(1, (len(bodies)-1).bit_length())


def mask(i, pos):
    value = z3.BitVecVal(0, bits)
    for p, valid in enumerate(bodies[i]["valid"]):
        if valid:
            value = z3.If(pos == p, z3.BitVecVal(int(bodies[i]["masks"][p]), bits), value)
    return value


def positions(t):
    return [z3.BitVec(f"p_{t}_{i}", position_bits) for i in range(len(bodies))]


def terminal(pos, moving):
    return z3.Or(*[z3.And(moving == i, z3.Or(*[pos[i] == g for g in b["goals"]]))
                   for i, b in enumerate(bodies)])


previous = positions(0)
for i, body in enumerate(bodies):
    solver.add(previous[i] == body["origin"])
rows = []
for t in range(1, 25):
    current = positions(t)
    moving = z3.BitVec(f"m_{t}", mover_bits)
    solver.add(z3.Or(*[moving == i for i in range(len(bodies))]))
    for i, body in enumerate(bodies):
        transitions = z3.Or(*[z3.And(previous[i] == a, current[i] == b)
                              for a, b in body["edges"]])
        solver.add(z3.If(moving == i, transitions, current[i] == previous[i]))
    current_masks = [mask(i, current[i]) for i in range(len(bodies))]
    for i in range(len(bodies)):
        for j in range(i):
            solver.add(current_masks[i] & current_masks[j] == 0)
    solver.push()
    solver.add(terminal(current, moving))
    tick = time.perf_counter()
    status = solver.check()
    row = {"steps": t, "status": str(status), "checkMs": (time.perf_counter()-tick)*1000,
           "totalMs": (time.perf_counter()-start)*1000,
           "statistics": str(solver.statistics())}
    if status == z3.sat:
        model = solver.model()
        row["anchors"] = [[model.eval(p).as_long() for p in positions(s)] for s in range(t+1)]
        row["movers"] = [model.eval(z3.BitVec(f"m_{s}", mover_bits)).as_long() for s in range(1,t+1)]
    rows.append(row)
    print(t, status, round(row["checkMs"], 2), flush=True)
    solver.pop()
    if status == z3.sat or row["totalMs"] > 5000:
        break
    solver.add(z3.Not(terminal(current, moving)))
    previous = current
source.with_suffix(".bitvector-probe.json").write_text(json.dumps({
    "method": "One initial phase, unit-step symbolic encoding; no independent replay; not acceptance",
    "z3Version": z3.get_version_string(), "geometry": source.name, "rows": rows
    ,"geometrySha256": hashlib.sha256(source.read_bytes()).hexdigest(),
    "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    "pythonVersion": sys.version, "platform": platform.platform()
}, indent=2), encoding="utf-8")

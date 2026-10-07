"""Diagnostic event-order comparison. No plans are fed back into the solver."""
import csv
import json
from pathlib import Path

research = Path(__file__).resolve().parent
root = research.parent.parent
with (root / "docs/benchmarks/performance.csv").open(encoding="utf-8-sig") as stream:
    scope = {row["level"] for row in csv.DictReader(stream) if int(row["expanded"]) > 10000}
assert len(scope) == 21

def events(moves):
    result = []
    for drag, move in enumerate(moves):
        for event in move["eaten"]:
            result.append({"drag": drag, "event": event, "signature": [event.get(field) for field in ("holeId", "color", "catId", "boxId", "boxIndex")]})
    return result

rows = []
for level in ("Level00206", "Level00233", "Level00216", "Level00267"):
    assert level in scope
    baseline = json.loads((research / "baseline-three-all" / f"{level}.sample0.json").read_text(encoding="utf-8-sig"))
    current = json.loads((research / "goal-30k-input-target-screen" / f"{level}.json").read_text(encoding="utf-8-sig"))
    left, right = events(baseline["moves"]), events(current["solution"]["moves"])
    same = 0
    while same < min(len(left), len(right)) and left[same]["signature"] == right[same]["signature"]:
        same += 1
    row = {"level": level, "matchedEvents": same, "baselineEvents": len(left), "currentEvents": len(right),
           "baselineNext": left[same] if same < len(left) else None,
           "currentNext": right[same] if same < len(right) else None,
           "note": "Different event order is an observation, not proof of the cause of a slow or stalled solve."}
    rows.append(row)
    print(level, "common prefix events:", same, "baseline/current next drags:", row["baselineNext"]["drag"] if row["baselineNext"] else None, row["currentNext"]["drag"] if row["currentNext"] else None)
(research / "event-order-comparison.json").write_text(json.dumps(rows, indent=2), encoding="utf-8")

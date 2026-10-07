using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private readonly Dictionary<(bool[][] phase, int target, int other), int[]> pairPatterns
                = new Dictionary<(bool[][], int, int), int[]>();
            private sealed class PairComponents
            {
                internal int[][] ids;
                internal int[][][] groups;
            }
            private sealed class PatternBody
            {
                internal int root;
                internal bool horizontal = true, vertical = true;
                internal CellMask[] masks;
                internal bool[] valid;
                internal int[] goals;
            }

            private List<PatternBody> PatternBodies()
            {
                var bodies = new List<PatternBody>();
                for (int root = 0; root < capacities.Length; root++)
                {
                    int origin = current.GetValue(root);
                    if (origin < 0) continue;
                    var group = linkedGroups[root];
                    int representative = -1;
                    bool draggable = true;
                    var body = new PatternBody { root = root, masks = new CellMask[count], valid = new bool[count] };
                    foreach (int member in group)
                        if (current.GetValue(member) >= 0)
                        {
                            if (representative < 0) representative = member;
                            draggable &= CanDrag(member, current.GetValue(member));
                            body.horizontal &= board.holes[member].movementType != 1;
                            body.vertical &= board.holes[member].movementType != 2;
                        }
                    if (representative != root || !draggable || !body.horizontal && !body.vertical) continue;
                    var goalsForBody = new List<int>();
                    for (int anchor = 0; anchor < count; anchor++)
                    {
                        bool legal = true, terminal = false;
                        int dx = CellAt(anchor).x - CellAt(origin).x, dy = CellAt(anchor).y - CellAt(origin).y;
                        CellMask footprint = CellMask.Zero;
                        foreach (int member in group)
                        {
                            int p = current.GetValue(member);
                            if (p < 0) continue;
                            var cell = new Cell(CellAt(p).x + dx, CellAt(p).y + dy);
                            if (!Inside(cell)) { legal = false; break; }
                            int destination = Index(cell);
                            if (!valid[member][destination]) { legal = false; break; }
                            footprint |= masks[member][destination];
                            terminal |= goals[member][destination];
                        }
                        if (!legal || !(footprint & routeFixedMask).IsZero) continue;
                        body.valid[anchor] = true;
                        body.masks[anchor] = footprint;
                        if (terminal) goalsForBody.Add(anchor);
                    }
                    if (body.masks[origin].IsZero) continue;
                    body.goals = goalsForBody.ToArray();
                    bodies.Add(body);
                }
                return bodies;
            }

            // Relaxed two-body geometry: other groups and directional gates are
            // omitted. One abstract edge is one reachable drag, not one grid step.
            // This is only a search ordering heuristic, never a dead-end proof.
            private PairComponents BuildPairComponents(PatternBody moving, PatternBody blocker)
            {
                var result = new PairComponents { ids = new int[count][], groups = new int[count][][] };
                var queue = new int[count];
                for (int fixedAnchor = 0; fixedAnchor < count; fixedAnchor++)
                {
                    CheckBudget();
                    var ids = new int[count];
                    for (int p = 0; p < count; p++) ids[p] = -1;
                    var groups = new List<int[]>();
                    result.ids[fixedAnchor] = ids;
                    if (blocker.valid[fixedAnchor])
                    {
                        var blocked = routeFixedMask | blocker.masks[fixedAnchor];
                        bool Legal(int p) => moving.valid[p] && (moving.masks[p] & blocked).IsZero;
                        for (int start = 0; start < count; start++)
                        {
                            if (ids[start] >= 0 || !Legal(start)) continue;
                            int group = groups.Count, head = 0, tail = 1;
                            queue[0] = start;
                            ids[start] = group;
                            while (head < tail)
                            {
                                int p = queue[head++];
                                foreach (int q in neighbors[p])
                                    if (ids[q] < 0 && Legal(q) && (CellAt(p).x != CellAt(q).x ? moving.horizontal : moving.vertical))
                                    {
                                        ids[q] = group;
                                        queue[tail++] = q;
                                    }
                            }
                            var members = new int[tail];
                            Array.Copy(queue, members, tail);
                            groups.Add(members);
                        }
                    }
                    result.groups[fixedAnchor] = groups.ToArray();
                }
                return result;
            }

            private int[] PairPattern(PatternBody target, PatternBody other)
            {
                var key = (valid, target.root, other.root);
                if (pairPatterns.TryGetValue(key, out var cached)) return cached;
                var targetComponents = BuildPairComponents(target, other);
                var otherComponents = BuildPairComponents(other, target);
                var distances = new int[count * count];
                for (int i = 0; i < distances.Length; i++) distances[i] = int.MaxValue;
                var queue = new int[distances.Length];
                var targetDone = new bool[count][];
                var otherDone = new bool[count][];
                for (int p = 0; p < count; p++)
                {
                    targetDone[p] = new bool[targetComponents.groups[p].Length];
                    otherDone[p] = new bool[otherComponents.groups[p].Length];
                }
                int head = 0, tail = 0;
                foreach (int a in target.goals)
                    for (int b = 0; b < count; b++)
                        if (targetComponents.ids[b][a] >= 0 && otherComponents.ids[a][b] >= 0)
                        {
                            int state = a * count + b;
                            if (distances[state] == 0) continue;
                            distances[state] = 0;
                            queue[tail++] = state;
                        }
                while (head < tail)
                {
                    CheckBudget();
                    int state = queue[head++], a = state / count, b = state % count;
                    result.abstractExpanded++;
                    int cost = distances[state] + 1;
                    int component = targetComponents.ids[b][a];
                    if (component >= 0 && !targetDone[b][component])
                    {
                        targetDone[b][component] = true;
                        foreach (int next in targetComponents.groups[b][component])
                        {
                            int destination = next * count + b;
                            if (distances[destination] != int.MaxValue) continue;
                            distances[destination] = cost;
                            queue[tail++] = destination;
                        }
                    }
                    component = otherComponents.ids[a][b];
                    if (component >= 0 && !otherDone[a][component])
                    {
                        otherDone[a][component] = true;
                        foreach (int next in otherComponents.groups[a][component])
                        {
                            int destination = a * count + next;
                            if (distances[destination] != int.MaxValue) continue;
                            distances[destination] = cost;
                            queue[tail++] = destination;
                        }
                    }
                }
                if (pairPatterns.Count >= 32) pairPatterns.Clear();
                pairPatterns[key] = distances;
                return distances;
            }

            private bool useFourPatterns = false; // Research switch; experiment remains disabled.
            private bool SearchPairPatterns(int workLimit = 4096, bool includeTriples = false)
            {
                if (count > 128) return false;
                var active = PatternBodies();
                // With only two active bodies this abstraction would be the whole
                // arrangement graph. Keep this heuristic strictly abstract.
                if (active.Count < 3) return false;
                TraceStage("pair-pattern-build");
                var targets = new List<(int hole, List<(int other, int[] distances)> pairs, int first, int second, int[] triple, PatternBody body, FourPatternTable four)>();
                var orderedTargets = new List<(PatternBody body, long score)>();
                foreach (var body in active)
                    if (body.goals.Length > 0)
                    {
                        int origin = current.GetValue(body.root);
                        var blockers = Occupancy(current) & ~body.masks[origin];
                        long score = linkedGroups[body.root].Length == 1 ? OccupancyPathDistance(body.root, origin, blockers)
                            : LinkedRouteDistance(body.root, current, blockers);
                        orderedTargets.Add((body, score));
                    }
                orderedTargets.Sort((a,b) => a.score != b.score ? a.score.CompareTo(b.score) : a.body.root.CompareTo(b.body.root));
                foreach (var item in orderedTargets)
                {
                        if (!useAdditivePatterns && active.Count > 4 && targets.Count >= 3) break;
                        var target = item.body;
                        if (useAdditivePatterns && linkedGroups[target.root].Length == 1 &&
                            OccupancyPathDistance(target.root, current.GetValue(target.root), routeFixedMask) >= long.MaxValue / 8)
                            continue;
                        var relevant = linkedGroups[target.root].Length == 1
                            ? RouteDependencyGroups(target.root, current, -1, out var requests) : new HashSet<int>();
                        var partners = new List<PatternBody>(active);
                        partners.Remove(target);
                        partners.Sort((a,b) =>
                        {
                            int relevance = relevant.Contains(b.root).CompareTo(relevant.Contains(a.root));
                            if (relevance != 0) return relevance;
                            var origin = CellAt(current.GetValue(target.root));
                            var ac = CellAt(current.GetValue(a.root)); var bc = CellAt(current.GetValue(b.root));
                            int ad = Math.Abs(ac.x-origin.x)+Math.Abs(ac.y-origin.y), bd = Math.Abs(bc.x-origin.x)+Math.Abs(bc.y-origin.y);
                            return ad != bd ? ad.CompareTo(bd) : a.root.CompareTo(b.root);
                        });
                        var pairs = new List<(int, int[])>();
                        foreach (var other in partners)
                        {
                            if (!useAdditivePatterns && !useFourPatterns && pairs.Count >= 3) break;
                            pairs.Add((other.root, useAdditivePatterns ? AdditivePairPattern(target, other) : PairPattern(target, other)));
                        }
                        // Build cubic tables only in the second search tier after
                        // the bounded pass using quadratic two-body tables fails.
                        int[] triple = includeTriples && active.Count >= 4 && count <= 81 && partners.Count >= 2
                            ? TriplePattern(target, partners[0], partners[1]) : null;
                        var four = useFourPatterns && active.Count >= 5
                            ? FourPattern(target, partners[0], partners[1], partners[2]) : null;
                        targets.Add((target.root, pairs, partners[0].root, partners[1].root, triple, target, four));
                }
                if (targets.Count == 0) return false;
                TraceStage("pair-pattern-search");
                var scoreCache = new Dictionary<Positions, long>();
                long Rank(Positions state, int depth)
                {
                    if (scoreCache.TryGetValue(state, out long cachedScore))
                        return cachedScore + (additiveGreedy ? 0 : depth * 1048576L);
                    long bestRank = long.MaxValue;
                    foreach (var target in targets)
                    {
                        int max = 0, sum = 0;
                        foreach (var pair in target.pairs)
                        {
                            int value = pair.distances[state.GetValue(target.hole) * count + state.GetValue(pair.other)];
                            if (value == int.MaxValue) value = 10000;
                            max = Math.Max(max, value);
                            sum += value;
                        }
                        if (target.triple != null)
                        {
                            int index = state.GetValue(target.hole) * count * count + state.GetValue(target.first) * count + state.GetValue(target.second);
                            int distance = target.triple[index];
                            max = Math.Max(max, distance == 0 ? 10000 : distance - 1);
                        }
                        if (target.four != null)
                        {
                            int distance = target.four.Distance(state);
                            if (distance > 0) max = Math.Max(max, distance - 1);
                            else if (target.four.complete) max = Math.Max(max, 10000);
                        }
                        int position = state.GetValue(target.hole);
                        var blockers = Occupancy(state) & ~target.body.masks[position];
                        long occupancyCost = linkedGroups[target.hole].Length == 1
                            ? OccupancyPathDistance(target.hole, position, blockers)
                            : LinkedRouteDistance(target.hole, state, blockers);
                        int observed = (int)Math.Min(10000, occupancyCost / 8);
                        max = Math.Max(max, observed);
                        long rank = useAdditivePatterns
                            ? 4L * (additiveGreedy ? Math.Max(sum + 1, observed) : sum + 1) * 1048576 + sum * 1024L + Math.Min(1023, occupancyCost)
                            : 4L * max * 1048576 + sum * 1024L + Math.Min(1023, occupancyCost);
                        bestRank = Math.Min(bestRank, rank);
                    }
                    if (additiveMobility && bestRank < long.MaxValue / 2)
                        bestRank -= Math.Min(1023, PatternMobility(active, state)) * 1024L;
                    if (scoreCache.Count >= 32768) scoreCache.Clear();
                    scoreCache[state] = bestRank;
                    return bestRank + (additiveGreedy ? 0 : depth * 1048576L);
                }
                int serial = 0, work = 0;
                var open = new StableMinHeap<Node>();
                var queued = new Dictionary<Positions, int> { [current] = 0 };
                var closed = new Dictionary<Positions, int>();
                Candidate chosenTerminal = null;
                Node chosenParent = null;
                int finishWork = workLimit;
                open.Add((Rank(current, 0), serial++, new Node()));
                while (open.Count > 0 && work < finishWork)
                {
                    CheckBudget();
                    var node = open.Pop().node;
                    var state = node.move == null ? current : node.move.state;
                    if (closed.TryGetValue(state, out int oldDepth) && (additiveGreedy || oldDepth <= node.depth)) continue;
                    closed[state] = node.depth;
                    stats.expanded++;
                    result.expanded++;
                    work++;
                    if ((work & 255) == 0) context.progress?.Invoke(result);
                    var moves = Generate(state);
                    stats.generated += moves.Count;
                    Candidate terminal = null;
                    foreach (var move in moves)
                        if (AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0)) terminal = move;
                    if (terminal != null)
                    {
                        if (chosenTerminal == null)
                            finishWork = work;
                        if (chosenTerminal == null || CompareTerminal(terminal, chosenTerminal) < 0)
                        {
                            chosenTerminal = terminal;
                            chosenParent = node;
                        }
                        continue;
                    }
                    foreach (var move in moves)
                    {
                        if (move.terminal) continue;
                        int depth = node.depth + 1;
                        if (queued.TryGetValue(move.state, out int previous) && previous <= depth) continue;
                        queued[move.state] = depth;
                        open.Add((Rank(move.state, depth), serial++, new Node { parent = node, move = move, depth = depth }));
                    }
                }
                if (chosenTerminal != null)
                {
                    best = CompactParkingChain(new Node { parent = chosenParent, move = chosenTerminal, depth = chosenParent.depth + 1 });
                    bestDepth = best.depth;
                    return true;
                }
                return false;
            }
        }
    }
}

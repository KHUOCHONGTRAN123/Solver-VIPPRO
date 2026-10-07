using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private sealed class FourPatternTable
            {
                internal int[] roots, strides, distances;
                internal int[][] anchors, indexes;
                internal bool complete;
                internal int Distance(Positions state)
                {
                    int packed = 0;
                    for (int i = 0; i < roots.Length; i++)
                    {
                        int anchor = state.GetValue(roots[i]);
                        if (anchor < 0 || indexes[i][anchor] < 0) return 0;
                        packed += indexes[i][anchor] * strides[i];
                    }
                    // Zero is unknown, including after a bounded table build.
                    return distances[packed];
                }
            }

            // Four-body relaxation, with domains restricted to static connected
            // components. It always omits at least one movable body and never
            // counts abstract nodes as full-state expansions.
            private FourPatternTable FourPattern(PatternBody target, PatternBody first,
                PatternBody second, PatternBody third, int workLimit = 25000)
            {
                return JointPattern(new[] { target, first, second, third }, workLimit);
            }

            private FourPatternTable JointPattern(PatternBody[] bodies, int workLimit = 25000)
            {
                int size = bodies.Length;
                if (size < 2 || PatternBodies().Count <= size) return null;
                var target = bodies[0];
                var table = new FourPatternTable
                {
                    roots = new int[size], strides = new int[size],
                    anchors = new int[size][], indexes = new int[size][]
                };
                int total = 1;
                for (int i = 0; i < size; i++)
                {
                    var body = bodies[i];
                    table.roots[i] = body.root;
                    int origin = current.GetValue(body.root);
                    if (!body.valid[origin]) return null;
                    var seen = new bool[count];
                    var queue = new List<int> { origin };
                    seen[origin] = true;
                    for (int head = 0; head < queue.Count; head++)
                    {
                        int p = queue[head];
                        foreach (int q in neighbors[p])
                            if (!seen[q] && body.valid[q] &&
                                (CellAt(p).x != CellAt(q).x ? body.horizontal : body.vertical))
                            { seen[q] = true; queue.Add(q); }
                    }
                    queue.Sort();
                    table.anchors[i] = queue.ToArray();
                    var indexes = new int[count];
                    for (int p = 0; p < count; p++) indexes[p] = -1;
                    for (int p = 0; p < queue.Count; p++) indexes[queue[p]] = p;
                    table.indexes[i] = indexes;
                    table.strides[i] = total;
                    if (queue.Count == 0 || total > 500000 / queue.Count) return null;
                    total *= queue.Count;
                }
                table.distances = new int[total];
                var open = new int[total];
                var goalsForTarget = new HashSet<int>(target.goals);
                int tail = 0;
                var coordinates = new int[size];
                void Decode(int state)
                {
                    for (int i = 0; i < size; i++)
                        coordinates[i] = state / table.strides[i] % table.anchors[i].Length;
                }
                for (int state = 0; state < total; state++)
                {
                    if ((state & 4095) == 0) CheckBudget();
                    Decode(state);
                    if (!goalsForTarget.Contains(table.anchors[0][coordinates[0]])) continue;
                    CellMask occupied = CellMask.Zero;
                    bool legal = true;
                    for (int i = 0; i < size; i++)
                    {
                        var mask = bodies[i].masks[table.anchors[i][coordinates[i]]];
                        if (!(mask & occupied).IsZero) { legal = false; break; }
                        occupied |= mask;
                    }
                    if (!legal) continue;
                    table.distances[state] = 1;
                    open[tail++] = state;
                }
                var rows = new Dictionary<int, TripleRow>[size];
                for (int i = 0; i < size; i++) rows[i] = new Dictionary<int, TripleRow>();
                int headIndex = 0;
                while (headIndex < tail && headIndex < workLimit)
                {
                    CheckBudget();
                    int state = open[headIndex++];
                    Decode(state);
                    result.abstractExpanded++;
                    int cost = table.distances[state] + 1;
                    for (int moving = 0; moving < size; moving++)
                    {
                        int rowKey = state - coordinates[moving] * table.strides[moving];
                        if (!rows[moving].TryGetValue(rowKey, out var row))
                        {
                            CellMask blocked = CellMask.Zero;
                            for (int i = 0; i < size; i++) if (i != moving)
                                blocked |= bodies[i].masks[table.anchors[i][coordinates[i]]];
                            var body = bodies[moving];
                            var anchors = table.anchors[moving];
                            var ids = new int[anchors.Length];
                            for (int i = 0; i < ids.Length; i++) ids[i] = -1;
                            var groups = new List<int[]>();
                            for (int start = 0; start < anchors.Length; start++)
                            {
                                if (ids[start] >= 0 || !(body.masks[anchors[start]] & blocked).IsZero) continue;
                                var connected = new List<int> { start };
                                ids[start] = groups.Count;
                                for (int next = 0; next < connected.Count; next++)
                                {
                                    int p = anchors[connected[next]];
                                    foreach (int q in neighbors[p])
                                    {
                                        int index = table.indexes[moving][q];
                                        if (index >= 0 && ids[index] < 0 && (body.masks[q] & blocked).IsZero &&
                                            (CellAt(p).x != CellAt(q).x ? body.horizontal : body.vertical))
                                        { ids[index] = groups.Count; connected.Add(index); }
                                    }
                                }
                                groups.Add(connected.ToArray());
                            }
                            row = new TripleRow { ids = ids, groups = groups.ToArray(), expanded = new bool[groups.Count] };
                            rows[moving][rowKey] = row;
                        }
                        int component = row.ids[coordinates[moving]];
                        if (component < 0 || row.expanded[component]) continue;
                        row.expanded[component] = true;
                        foreach (int index in row.groups[component])
                        {
                            int destination = rowKey + index * table.strides[moving];
                            if (table.distances[destination] != 0) continue;
                            table.distances[destination] = cost;
                            open[tail++] = destination;
                        }
                    }
                }
                table.complete = headIndex == tail;
                return table;
            }
        }
    }
}

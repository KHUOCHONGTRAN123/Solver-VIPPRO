using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private readonly Dictionary<(bool[][] phase, int target, int first, int second), int[]> triplePatterns
                = new Dictionary<(bool[][], int, int, int), int[]>();
            private sealed class TripleRow
            {
                internal int[] ids;
                internal int[][] groups;
                internal bool[] expanded;
            }

            private TripleRow TripleComponents(PatternBody moving, PatternBody first, int a, PatternBody second, int b)
            {
                var ids = new int[count];
                for (int p = 0; p < count; p++) ids[p] = -1;
                var groups = new List<int[]>();
                if (first.valid[a] && second.valid[b] && (first.masks[a] & second.masks[b]).IsZero)
                {
                    var blocked = routeFixedMask | first.masks[a] | second.masks[b];
                    var queue = new int[count];
                    bool Legal(int p) => moving.valid[p] && (moving.masks[p] & blocked).IsZero;
                    for (int start = 0; start < count; start++)
                    {
                        if (ids[start] >= 0 || !Legal(start)) continue;
                        int group = groups.Count, head = 0, tail = 1;
                        queue[0] = start; ids[start] = group;
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
                        var members = new int[tail]; Array.Copy(queue, members, tail); groups.Add(members);
                    }
                }
                return new TripleRow { ids = ids, groups = groups.ToArray(), expanded = new bool[groups.Count] };
            }

            // Stored value is drag distance + 1; zero means unreachable in this
            // abstract model. This graph always omits at least one movable group.
            private int[] TriplePattern(PatternBody target, PatternBody first, PatternBody second)
            {
                var key = (valid, target.root, first.root, second.root);
                if (triplePatterns.TryGetValue(key, out var cached)) return cached;
                int square = count * count, total = square * count;
                var distances = new int[total];
                var queue = new int[total];
                var rows0 = new TripleRow[square];
                var rows1 = new TripleRow[square];
                var rows2 = new TripleRow[square];
                int head = 0, tail = 0;
                foreach (int a in target.goals)
                    for (int b = 0; b < count; b++)
                        if (first.valid[b] && (target.masks[a] & first.masks[b]).IsZero)
                            for (int c = 0; c < count; c++)
                                if (second.valid[c] && (second.masks[c] & (target.masks[a] | first.masks[b])).IsZero)
                                {
                                    int state = a * square + b * count + c;
                                    if (distances[state] != 0) continue;
                                    distances[state] = 1;
                                    queue[tail++] = state;
                                }
                while (head < tail)
                {
                    CheckBudget();
                    int state = queue[head++], a = state / square, b = state / count % count, c = state % count;
                    result.abstractExpanded++;
                    int cost = distances[state] + 1;
                    int rowKey = b * count + c;
                    var row = rows0[rowKey] ?? (rows0[rowKey] = TripleComponents(target, first, b, second, c));
                    int component = row.ids[a];
                    if (component >= 0 && !row.expanded[component])
                    {
                        row.expanded[component] = true;
                        foreach (int next in row.groups[component])
                        {
                            int destination = next * square + b * count + c;
                            if (distances[destination] != 0) continue;
                            distances[destination] = cost; queue[tail++] = destination;
                        }
                    }
                    rowKey = a * count + c;
                    row = rows1[rowKey] ?? (rows1[rowKey] = TripleComponents(first, target, a, second, c));
                    component = row.ids[b];
                    if (component >= 0 && !row.expanded[component])
                    {
                        row.expanded[component] = true;
                        foreach (int next in row.groups[component])
                        {
                            int destination = a * square + next * count + c;
                            if (distances[destination] != 0) continue;
                            distances[destination] = cost; queue[tail++] = destination;
                        }
                    }
                    rowKey = a * count + b;
                    row = rows2[rowKey] ?? (rows2[rowKey] = TripleComponents(second, target, a, first, b));
                    component = row.ids[c];
                    if (component >= 0 && !row.expanded[component])
                    {
                        row.expanded[component] = true;
                        foreach (int next in row.groups[component])
                        {
                            int destination = a * square + b * count + next;
                            if (distances[destination] != 0) continue;
                            distances[destination] = cost; queue[tail++] = destination;
                        }
                    }
                }
                if (triplePatterns.Count >= 8) triplePatterns.Clear();
                triplePatterns[key] = distances;
                return distances;
            }
        }
    }
}

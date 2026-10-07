using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private bool useAdditivePatterns;
            private bool additiveGreedy;
            private bool additiveMobility;
            private readonly Dictionary<(bool[][] phase, int target, int other), int[]> additivePairPatterns
                = new Dictionary<(bool[][], int, int), int[]>();

            private bool SearchAdditivePatterns(int workLimit = 10000, bool greedy = false, bool mobility = false)
            {
                bool previous = useAdditivePatterns;
                bool previousGreedy = additiveGreedy;
                bool previousMobility = additiveMobility;
                try
                {
                    useAdditivePatterns = true;
                    additiveGreedy = greedy;
                    additiveMobility = mobility;
                    return SearchPairPatterns(workLimit);
                }
                finally { useAdditivePatterns = previous; additiveGreedy = previousGreedy; additiveMobility = previousMobility; }
            }

            private int PatternMobility(List<PatternBody> bodies, Positions state)
            {
                int mobility = 0;
                var occupied = Occupancy(state);
                foreach (var body in bodies)
                {
                    int origin = state.GetValue(body.root);
                    var blockers = occupied & ~body.masks[origin];
                    foreach (int next in neighbors[origin])
                        if (body.valid[next] && (body.masks[next] & blockers).IsZero &&
                            (CellAt(origin).x != CellAt(next).x ? body.horizontal : body.vertical))
                            mobility++;
                }
                return mobility;
            }

            // Cost partition: target drags cost zero, partner drags cost one.
            // Summing one table per partner charges each other moving body once.
            // Omitted bodies/gates relax geometry; these costs only rank search.
            private int[] AdditivePairPattern(PatternBody target, PatternBody other)
            {
                var key = (valid, target.root, other.root);
                if (additivePairPatterns.TryGetValue(key, out var cached)) return cached;
                var targetComponents = BuildPairComponents(target, other);
                var otherComponents = BuildPairComponents(other, target);
                var distances = new int[count * count];
                for (int i = 0; i < distances.Length; i++) distances[i] = int.MaxValue;
                var targetDone = new bool[count][];
                var otherDone = new bool[count][];
                for (int p = 0; p < count; p++)
                {
                    targetDone[p] = new bool[targetComponents.groups[p].Length];
                    otherDone[p] = new bool[otherComponents.groups[p].Length];
                }
                var open = new StableMinHeap<int>();
                int serial = 0;
                foreach (int a in target.goals)
                    for (int b = 0; b < count; b++)
                        if (targetComponents.ids[b][a] >= 0 && otherComponents.ids[a][b] >= 0)
                        {
                            int state = a * count + b;
                            if (distances[state] == 0) continue;
                            distances[state] = 0;
                            open.Add((0, serial++, state));
                        }
                while (open.Count > 0)
                {
                    CheckBudget();
                    var entry = open.Pop();
                    int state = entry.node, a = state / count, b = state % count;
                    int cost = distances[state];
                    if (entry.score != cost) continue;
                    result.abstractExpanded++;
                    int component = targetComponents.ids[b][a];
                    if (component >= 0 && !targetDone[b][component])
                    {
                        targetDone[b][component] = true;
                        foreach (int next in targetComponents.groups[b][component])
                        {
                            int destination = next * count + b;
                            if (distances[destination] <= cost) continue;
                            distances[destination] = cost;
                            open.Add((cost, serial++, destination));
                        }
                    }
                    component = otherComponents.ids[a][b];
                    if (component >= 0 && !otherDone[a][component])
                    {
                        otherDone[a][component] = true;
                        foreach (int next in otherComponents.groups[a][component])
                        {
                            int destination = a * count + next;
                            if (distances[destination] <= cost + 1) continue;
                            distances[destination] = cost + 1;
                            open.Add((cost + 1, serial++, destination));
                        }
                    }
                }
                if (additivePairPatterns.Count >= 64) additivePairPatterns.Clear();
                additivePairPatterns[key] = distances;
                return distances;
            }
        }
    }
}

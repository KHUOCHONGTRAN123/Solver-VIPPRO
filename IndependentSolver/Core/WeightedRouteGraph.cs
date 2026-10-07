using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private sealed class WeightedRouteGraph
            {
                internal bool[] allowed;
                internal int[][] predecessors;
            }

            private readonly Dictionary<(bool[][] phase, int hole, CellMask fixedMask), WeightedRouteGraph> weightedRouteGraphs
                = new Dictionary<(bool[][], int, CellMask), WeightedRouteGraph>();

            // Occupancy penalties vary with parking arrangements; legal directed
            // edges do not change until the consumption/mechanic phase changes.
            private WeightedRouteGraph RouteGraph(int hole)
            {
                var key = (valid, hole, routeFixedMask);
                if (weightedRouteGraphs.TryGetValue(key, out var graph))
                    return graph;
                graph = new WeightedRouteGraph { allowed = new bool[count], predecessors = new int[count][] };
                for (int p = 0; p < count; p++)
                    graph.allowed[p] = valid[hole][p] && (masks[hole][p] & routeFixedMask).IsZero;
                for (int destination = 0; destination < count; destination++)
                {
                    CheckBudget();
                    if (!graph.allowed[destination])
                    {
                        graph.predecessors[destination] = Array.Empty<int>();
                        continue;
                    }
                    var edges = new List<int>(4);
                    foreach (int p in neighbors[destination])
                        if (graph.allowed[p] && CanStep(hole, p, destination) &&
                            CanEnterCat(hole, destination, Direction(CellAt(p), CellAt(destination))))
                            edges.Add(p);
                    graph.predecessors[destination] = edges.ToArray();
                }
                if (weightedRouteGraphs.Count >= 256)
                    weightedRouteGraphs.Clear();
                weightedRouteGraphs[key] = graph;
                return graph;
            }
        }
    }
}

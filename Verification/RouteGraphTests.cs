using System.Reflection;
using Product = CatDom.CoreSolver;

internal static class RouteGraphTests
{
    internal static void Run(string root)
    {
        var levels = File.ReadAllLines(Path.Combine(root, "docs/benchmarks/performance.csv")).Skip(1)
            .Select(line => line.Split(',')).Where(row => long.Parse(row[2]) > 10000).Select(row => row[0]).ToArray();
        if (levels.Length != 21) throw new Exception("Research scope changed.");
        var catalog = Product.IO.SolverJson.ReadCatalog(File.ReadAllText(Path.Combine(root, "IndependentSolver/IO/HoleShapes.json")));
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = typeof(Product.CatLevelSolver).GetNestedType("Engine", BindingFlags.NonPublic)!;
        int comparisons = 0;
        foreach (string level in levels)
        {
            var input = Product.IO.SolverJson.ReadLevel(File.ReadAllText(Path.Combine(root, "Levels", level + ".json")), catalog);
            var engine = Activator.CreateInstance(type, flags, null,
                new object[] { input, new Product.EngineContext(), CancellationToken.None, new Product.EngineResult() }, null)!;
            object Call(string method, params object[] arguments) => type.GetMethod(method, flags)!.Invoke(engine, arguments)!;
            object Field(string name) => type.GetField(name, flags)!.GetValue(engine)!;
            Call("Consume", -1);
            Call("Prepare");
            int count = (int)Field("count");
            var valid = (bool[][])Field("valid");
            var masks = (Product.CellMask[][])Field("masks");
            var neighbors = (int[][])Field("neighbors");
            var goals = (int[][])Field("goalAnchors");
            var fixedMask = (Product.CellMask)Field("routeFixedMask");
            var current = Field("current");
            var occupied = (Product.CellMask)Call("Occupancy", current);
            for (int hole = 0; hole < input.holes.Count; hole++)
            {
                int origin = (int)current.GetType().GetMethod("GetValue", flags)!.Invoke(current, new object[] { hole })!;
                if (origin < 0) continue;
                foreach (var blockers in new[] { fixedMask, occupied ^ masks[hole][origin] })
                    foreach (int target in new[] { -1, goals[hole].FirstOrDefault(-1) }.Distinct())
                    {
                        const long infinity = long.MaxValue / 8;
                        var costs = Enumerable.Repeat(infinity, count).ToArray();
                        var allowed = new bool[count];
                        var penalties = new long[count];
                        var settled = new bool[count];
                        for (int p = 0; p < count; p++)
                        {
                            allowed[p] = valid[hole][p] && (masks[hole][p] & fixedMask).IsZero;
                            penalties[p] = 8L * (masks[hole][p] & blockers).PopCount();
                        }
                        foreach (int p in target < 0 ? goals[hole] : new[] { target })
                            if (allowed[p]) costs[p] = penalties[p];
                        // Independent array Dijkstra follows the original edge
                        // predicate directly, without the new cached graph.
                        for (int step = 0; step < count; step++)
                        {
                            int next = -1;
                            for (int p = 0; p < count; p++)
                                if (!settled[p] && costs[p] < infinity && (next < 0 || costs[p] < costs[next])) next = p;
                            if (next < 0) break;
                            settled[next] = true;
                            foreach (int p in neighbors[next])
                            {
                                int dx = next % input.width - p % input.width, dy = next / input.width - p / input.width;
                                int direction = dy > 0 ? 0 : dx > 0 ? 1 : dy < 0 ? 2 : 3;
                                if (!allowed[p] || !(bool)Call("CanStep", hole, p, next) || !(bool)Call("CanEnterCat", hole, next, direction)) continue;
                                costs[p] = Math.Min(costs[p], costs[next] + 1 + penalties[p]);
                            }
                        }
                        for (int anchor = 0; anchor < count; anchor++)
                        {
                            long expected = costs[anchor];
                            if (!valid[hole][anchor])
                            {
                                expected = infinity;
                                foreach (int q in neighbors[anchor])
                                    if (valid[hole][q] && (bool)Call("CanStep", hole, anchor, q)) expected = Math.Min(expected, costs[q] + 1);
                            }
                            long actual = (long)Call("OccupancyPathDistance", hole, anchor, blockers, target);
                            if (actual != expected) throw new Exception($"Route graph differs: {level} hole={hole} anchor={anchor} target={target}: {actual} != {expected}");
                            comparisons++;
                        }
                    }
            }
        }
        Console.WriteLine($"PASS route graph: {levels.Length} research levels, {comparisons} distances vs uncached edge oracle");
    }
}

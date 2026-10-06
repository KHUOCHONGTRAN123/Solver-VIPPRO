using CatDom.Reference.V42;

internal static class RouteTests
{
    static HoleInput Hole(int id, int color, int x, int y, int movement = 0) => new HoleInput
    { id = id, color = color, remaining = 1, position = new Cell(x, y), movementType = movement, footprint = new[] { new Cell(0, 0) } };
    static void Assert(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    internal static void Run()
    {
        // Two blockers must leave the horizontal target's corridor before eating.
        var corridor = new BoardInput { width = 4, height = 2 };
        corridor.holes.Add(Hole(0, 1, 0, 0, 2));
        corridor.holes.Add(Hole(1, 1000, 1, 0));
        corridor.holes.Add(Hole(2, 1000, 2, 0));
        corridor.cats.Add(new CatInput { id = 0, color = 1, position = new Cell(3, 0) });
        var config = SolverConfig.CreateRouteClearingDefault();
        config.maxSubproblemDepth = 1; config.maxSolveSeconds = double.Epsilon;
        // Unrelated legacy flags must not turn on cross-consumption backtracking or deadlines.
        config.useAdaptiveModels = true; config.useDecisionBacktracking = true;
        var result = V1TestAdapter.Solve(corridor, config);
        Assert(result.status == SolveStatus.Solved, "Route clearing must ignore time/depth caps.");
        Assert(result.moves.Count >= 3 && result.backtracks == 0, "Both blockers must be cleared without consumption backtracking.");
        Assert(V1TestAdapter.VerifyPlan(corridor, result.moves, 0).status == SolveStatus.Solved, "Corridor replay failed.");
        // B can slide vertically, but its two-cell footprint always covers row 1.
        // A is horizontal on row 1: 'B can move' does not mean it can clear A.
        var trapped = new BoardInput { width = 4, height = 3 };
        trapped.holes.Add(Hole(0, 1, 0, 1, 2));
        var blockerHole = Hole(1, 1000, 1, 0, 1);
        blockerHole.footprint = new[] { new Cell(0, 0), new Cell(0, 1) };
        trapped.holes.Add(blockerHole);
        trapped.cats.Add(new CatInput { id = 0, color = 1, position = new Cell(3, 1) });
        Assert(V1TestAdapter.FindNextCat(trapped, SolverConfig.CreateRouteClearingDefault()).status == SolveStatus.NoNextCatReachable,
            "A movable blocker that cannot leave the corridor must not produce a false plan.");
        trapped.holes[0].movementType = 0;
        var alternate = V1TestAdapter.FindNextCat(trapped, SolverConfig.CreateRouteClearingDefault());
        Assert(alternate.status == SolveStatus.NextCatFound || alternate.status == SolveStatus.Solved,
            "An impossible straight corridor must not prune a target with an alternative route.");
        var dependency = new BoardInput { width = 4, height = 3 };
        dependency.holes.Add(Hole(0, 1, 0, 0, 2));
        dependency.holes.Add(Hole(1, 1000, 1, 0, 1));
        dependency.holes.Add(Hole(2, 1000, 1, 1, 2));
        dependency.cats.Add(new CatInput { id = 0, color = 1, position = new Cell(3, 0) });
        var dependencyResult = V1TestAdapter.Solve(dependency, SolverConfig.CreateRouteClearingDefault());
        Assert(dependencyResult.status == SolveStatus.Solved && dependencyResult.moves.Count >= 3
            && V1TestAdapter.VerifyPlan(dependency, dependencyResult.moves, 0).status == SolveStatus.Solved,
            "C must clear B's parking path before B can clear A's target corridor.");
        var deeperDependency = new BoardInput { width = 4, height = 3 };
        deeperDependency.holes.Add(Hole(0, 1, 0, 0, 2));
        deeperDependency.holes.Add(Hole(1, 1000, 1, 0, 1));
        var wideBlocker = Hole(2, 1000, 1, 1, 2);
        wideBlocker.footprint = new[] { new Cell(0, 0), new Cell(1, 0) };
        deeperDependency.holes.Add(wideBlocker);
        deeperDependency.holes.Add(Hole(3, 1000, 3, 1, 1));
        deeperDependency.cats.Add(new CatInput { id = 0, color = 1, position = new Cell(3, 0) });
        var deeperResult = V1TestAdapter.Solve(deeperDependency, SolverConfig.CreateRouteClearingDefault());
        Assert(deeperResult.status == SolveStatus.Solved && deeperResult.moves.Count >= 4
            && V1TestAdapter.VerifyPlan(deeperDependency, deeperResult.moves, 0, true).status == SolveStatus.Solved,
            "D must free C's parking region, C must free B's parking region, then B must clear A.");
        var assignment = new BoardInput { width = 4, height = 2 };
        assignment.holes.Add(Hole(0, 1, 3, 1));
        var offsetHole = Hole(1, 1, 1, 1);
        offsetHole.footprint = new[] { new Cell(1, 0) };
        assignment.holes.Add(offsetHole);
        assignment.cats.Add(new CatInput { id = 0, color = 1, position = new Cell(0, 0) });
        assignment.cats.Add(new CatInput { id = 1, color = 1, position = new Cell(3, 0) });
        var assignmentResult = V1TestAdapter.Solve(assignment, SolverConfig.CreateRouteClearingDefault());
        Assert(assignmentResult.status == SolveStatus.Solved && assignmentResult.moves.First(m => m.eaten.Count > 0).holeId == 1
            && V1TestAdapter.VerifyPlan(assignment, assignmentResult.moves, 0).status == SolveStatus.Solved,
            "The easy cat must be reserved for the hole that cannot fit the edge cat, without committed backtracking.");
        var wallAssignment = new BoardInput { width = 4, height = 3 };
        wallAssignment.obstacles.Add(new Cell(0, 1));
        wallAssignment.holes.Add(Hole(0, 1, 3, 1));
        var tallHole = Hole(1, 1, 2, 0);
        tallHole.footprint = new[] { new Cell(0, 0), new Cell(0, 1) };
        wallAssignment.holes.Add(tallHole);
        wallAssignment.cats.Add(new CatInput { id = 0, color = 1, position = new Cell(0, 0) });
        wallAssignment.cats.Add(new CatInput { id = 1, color = 1, position = new Cell(3, 0) });
        var wallResult = V1TestAdapter.Solve(wallAssignment, SolverConfig.CreateRouteClearingDefault());
        var firstWallConsumption = wallResult.moves.FirstOrDefault(m => m.eaten.Count > 0);
        Assert(wallResult.status == SolveStatus.Solved && firstWallConsumption != null
            && !(firstWallConsumption.holeId == 0 && firstWallConsumption.eaten.Any(e => e.catId == 1))
            && wallResult.backtracks == 0 && V1TestAdapter.VerifyPlan(wallAssignment, wallResult.moves, 0).status == SolveStatus.Solved,
            "Permanent obstacles must be respected when reserving cats for remaining hole capacities.");
        var cycle = new BoardInput { width = 4, height = 4 };
        cycle.obstacles.Add(new Cell(3, 2));
        cycle.holes.Add(Hole(0, 0, 0, 1));
        var layered = Hole(1, 0, 0, 0);
        layered.layerColors = new[] { 0, 1 }; layered.layerCounts = new[] { 1, 1 };
        cycle.holes.Add(layered);
        var square = Hole(2, 2, 1, 0);
        square.footprint = new[] { new Cell(0, 0), new Cell(1, 0), new Cell(0, 1), new Cell(1, 1) };
        cycle.holes.Add(square); cycle.holes.Add(Hole(3, 3, 3, 3));
        cycle.cats.Add(new CatInput { id = 0, color = 0, position = new Cell(0, 2) });
        cycle.cats.Add(new CatInput { id = 1, color = 1, position = new Cell(1, 2) });
        cycle.cats.Add(new CatInput { id = 2, color = 3, position = new Cell(0, 3) });
        cycle.boxes.Add(new BoxInput { id = 0, mouth = new Cell(2, 2), position = new Cell(3, 2), direction = 1, colors = new[] { 2, 0 } });
        var cycleResult = V1TestAdapter.Solve(cycle, SolverConfig.CreateRouteClearingDefault());
        Assert(cycleResult.status == SolveStatus.Solved && cycleResult.moves.First(m => m.eaten.Any(e => e.catId == 0)).holeId == 1
            && cycleResult.backtracks == 0 && V1TestAdapter.VerifyPlan(cycle, cycleResult.moves, 0, true).status == SolveStatus.Solved,
            "Reserve the layer-unlocking cat even while an unrelated consumption hides a future box/cat cycle.");
        corridor.holes[0].gates = new[] { new GateInput { local = new Cell(0, 0), directions = 2 } };
        result = V1TestAdapter.Solve(corridor, SolverConfig.CreateRouteClearingDefault());
        Assert(result.status == SolveStatus.NoNextCatReachable, "Gate forbids the only horizontal entry.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert(V1TestAdapter.Solve(corridor, SolverConfig.CreateRouteClearingDefault(), cancelled.Token).status == SolveStatus.Cancelled,
            "User cancellation must remain supported.");
        var linked = new BoardInput { width = 3, height = 3 };
        linked.holes.Add(Hole(0, 1, 0, 0)); linked.holes.Add(Hole(1, 2, 1, 0));
        linked.links.Add(new LinkInput { holeId1 = 0, holeId2 = 1 });
        linked.cats.Add(new CatInput { id = 0, color = 1, position = new Cell(1, 2) });
        linked.cats.Add(new CatInput { id = 1, color = 2, position = new Cell(2, 1) });
        result = V1TestAdapter.Solve(linked, SolverConfig.CreateRouteClearingDefault());
        Assert(result.status == SolveStatus.Solved && V1TestAdapter.VerifyPlan(linked, result.moves, 0).status == SolveStatus.Solved,
            "Linked partners must move together and remain valid after one finishes.");
        linked.holes[0].movementType = 1; linked.holes[1].movementType = 2;
        Assert(V1TestAdapter.Solve(linked, SolverConfig.CreateRouteClearingDefault()).status == SolveStatus.NoNextCatReachable,
            "A linked group with incompatible axes cannot move.");
        // One-cat boards: compare complete parking search against existing complete
        // search, so different committed consumption choices cannot affect the oracle.
        int compared = 0;
        for (int target = 0; target < 9; target++)
        for (int cat = 0; cat < 9; cat++)
        for (int blocker = 0; blocker < 9; blocker++)
        {
            if (target == cat || target == blocker || cat == blocker) continue;
            var board = new BoardInput { width = 3, height = 3 };
            board.holes.Add(Hole(0, 1, target % 3, target / 3, compared % 3));
            board.holes.Add(Hole(1, 1000, blocker % 3, blocker / 3));
            board.cats.Add(new CatInput { id = 0, color = 1, position = new Cell(cat % 3, cat / 3) });
            // A deterministic obstacle strengthens the comparison beyond empty boards.
            int wall = (target + cat + blocker) % 9;
            if (wall != target && wall != cat && wall != blocker) board.obstacles.Add(new Cell(wall % 3, wall / 3));
            var expected = V1TestAdapter.Solve(board, new SolverConfig { strategy = SearchStrategy.Backtracking, maxSolveSeconds = 0, maxSubproblemDepth = 100 });
            var actual = V1TestAdapter.Solve(board, SolverConfig.CreateRouteClearingDefault());
            Assert((expected.status == SolveStatus.Solved) == (actual.status == SolveStatus.Solved), $"Small-board disagreement #{compared}.");
            Assert(actual.status == SolveStatus.Solved || actual.status == SolveStatus.NoNextCatReachable, "Unexpected route status.");
            compared++;
        }
        Console.WriteLine($"PASS: corridor, gate, cancellation, links, and {compared} exhaustive small-board comparisons.");
    }
}

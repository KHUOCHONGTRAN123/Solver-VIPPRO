using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    [Serializable]
    public struct Cell : IEquatable<Cell>
    {
        public int x, y;
        public Cell(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Cell other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => unchecked(x * 397 ^ y);
        public override string ToString() => $"({x},{y})";
    }

    public enum SearchStrategy { Default, SpaceFirst, Backtracking, RouteClearing }
    public enum NextCatSearch { ShortestSequence, FirstProgress, IterativeDeepening, ProgressiveFirst, BestFirst, Beam }
    public enum GoalAggregation { Minimum, Sum, Bottleneck, ReachableSum, ReachableBottleneck, BottleneckPlusSum, ReachableBottleneckPlusSum }

    [Serializable]
    public sealed class SolverConfig
    {
        public int maxSubproblemDepth = 12;
        public int blockedCellWeight = 8;
        public int distanceWeight = 1;
        public bool preferFinishingHole = true;
        public bool preferShorterDragOnTie = true;
        public double maxSolveSeconds = 10;
        public SearchStrategy strategy = SearchStrategy.Default;
        public NextCatSearch nextCatSearch = NextCatSearch.ShortestSequence;
        public bool useReachabilityDistance;
        public bool useTargetSpecificDistance;
        public bool useOccupancyPathDistance;
        public bool useEndpointParking;
        public bool useTargetAwareEndpoint;
        public bool selectEndpointByState;
        public bool useTargetAwareEndpointRetry;
        public bool useComplementaryDeep;
        public bool useFocusedGoalRanking;
        public bool requireFocusedTerminal;
        // Diagnostic only: exact settled inputs for next-cat experiments.
        public bool captureSubproblemInputs;
        // Zero keeps the legacy full-level search budget. Replay uses the level deadline.
        public double maxNextCatSearchSeconds;
        // Scales adaptive fallback budgets; Default retains its fixed 25 ms budget.
        public double fallbackBudgetScale = 1;
        public double earlyFallbackBudgetScale = 1;
        public bool earlyFallbackBudgetOnRevisitOnly;
        public bool useAdaptiveModels;
        public bool useDefaultCompletionRetry;
        public bool useDecisionBacktracking;
        public bool useDecisionModelFeedback;
        public bool usePolicyForTerminalChoice;
        public bool prioritizeRestrictedHoles;
        public bool useConsumptionUtility;
        public bool preferNovelConsumption;
        public bool useHeuristicCache;
        public bool useGeneratedMoveCache;
        public bool generatedMoveCacheRootsOnly;
        public GoalAggregation goalAggregation;
        public bool useStaticRouteBlockers;
        public int staticRouteBlockedWeight = 8;
        public bool useComplementaryAggregation;
        public bool complementaryUsesRouteMinimum;
        public bool complementaryUsesDistanceOnly;
        public bool complementaryUsesEndpointAggregation;
        public bool selectComplementaryByState;
        public bool useStateAwareBottleneckBudget;
        public bool complementaryAggregationRequiresWaiting;
        public bool preferDistinctConsumption;
        public double diversityPassSeconds = 2.5;
        public int decisionAlternativeLimit = 2;
        public int beamWidth = 32;
        public bool balanceBeamByMovedHole;
        public bool useBalancedBeamOnAlternative;
        public bool useBalancedBeamRetry;
        public bool beamVisitSelectedOnly;
        public bool useFrontierBeamRetry;
        public bool pruneConsecutiveGroupDrags;
        public bool pruneGroupDragsOnAlternative;
        public bool preserveFeedbackWithAlternativePruning;
        public bool interleavePrunedDefaultAlternatives;
        public bool captureContinuationStats;
        public double fallbackContinuationSeconds;
        public double continuationRemainingBudgetFraction;
        public bool extendContinuationOnCatProgress;
        public bool captureBeamFrontierStats;
        public bool selectBalancedRetryByFrontier;
        public bool deferBalancedBeamRetry;
        public bool limitDeferredContinuationsOnly;
        public bool useWideDeepBeam;
        [NonSerialized] public Action<SolveResult> progress;
        public bool useRouteDepthFirst;
        public static SolverConfig CreateRouteClearingDefault() => new SolverConfig
        {
            strategy = SearchStrategy.RouteClearing,
            useRouteDepthFirst = true,
            maxSolveSeconds = 0,
            maxNextCatSearchSeconds = 0,
            useReachabilityDistance = true,
            useTargetSpecificDistance = true,
            useOccupancyPathDistance = true
        };
        public static SolverConfig CreateNextCatDefault() => new SolverConfig
        {
            strategy = SearchStrategy.Default,
            nextCatSearch = NextCatSearch.FirstProgress,
            maxSubproblemDepth = 6,
            blockedCellWeight = 8, distanceWeight = 1,
            useReachabilityDistance = true,
            preferFinishingHole = true, preferShorterDragOnTie = true,
            maxSolveSeconds = 0.025, maxNextCatSearchSeconds = 0.025
        };
        public SolverConfig Copy() => (SolverConfig)MemberwiseClone();
    }

    [Serializable]
    public sealed class HoleInput
    {
        public int id, color, remaining;
        // 0: free, 1: vertical, 2: horizontal (Hole.holeInfo.movementType).
        public int movementType;
        public int numIced, hiddenCount;
        public int lockColorId = -1;
        public bool locked;
        // Remaining layers, starting at the currently active layer.
        public int[] layerColors = Array.Empty<int>(), layerCounts = Array.Empty<int>();
        public int layerOffset;
        public GateInput[] gates = Array.Empty<GateInput>();
        public Cell position;
        public Cell[] footprint = Array.Empty<Cell>();
    }

    [Serializable]
    public sealed class CatInput
    {
        public int id, color, numIced;
        public int keyColorId = -1, pickaxeColorId = -1;
        public Cell position;
        public CatInput Copy() => (CatInput)MemberwiseClone();
    }

    [Serializable] public struct GateInput { public Cell local; public int directions; }
    [Serializable] public struct LinkInput { public int holeId1, holeId2; }
    [Serializable] public struct ColorPathInput { public Cell position; public int color; }
    [Serializable] public sealed class CoverInput
    {
        public int id, remainingHits;
        public Cell[] cells = Array.Empty<Cell>();
    }

    [Serializable]
    public sealed class BoxInput
    {
        public int id;
        public Cell mouth;
        public int[] colors = Array.Empty<int>();
        public CatInput[] cats = Array.Empty<CatInput>();
        public Cell position;
        public int direction, requiredHolesToUnlock;
        public bool tower;
    }

    [Serializable]
    public sealed class BoardInput
    {
        public int width, height;
        public List<Cell> obstacles = new List<Cell>();
        public List<HoleInput> holes = new List<HoleInput>();
        public List<CatInput> cats = new List<CatInput>();
        public List<BoxInput> boxes = new List<BoxInput>();
        public List<LinkInput> links = new List<LinkInput>();
        public List<ColorPathInput> colorPaths = new List<ColorPathInput>();
        public List<CoverInput> covers = new List<CoverInput>();
        public List<string> ignoredMechanics = new List<string>();

        public BoardInput Copy()
        {
            var copy = new BoardInput { width = width, height = height };
            copy.obstacles.AddRange(obstacles);
            copy.ignoredMechanics.AddRange(ignoredMechanics);
            copy.links.AddRange(links); copy.colorPaths.AddRange(colorPaths);
            foreach (var h in holes) copy.holes.Add(new HoleInput { id = h.id, color = h.color, remaining = h.remaining, movementType = h.movementType, numIced = h.numIced, hiddenCount = h.hiddenCount, lockColorId = h.lockColorId, locked = h.locked, layerOffset = h.layerOffset, layerColors = (int[])h.layerColors.Clone(), layerCounts = (int[])h.layerCounts.Clone(), gates = (GateInput[])h.gates.Clone(), position = h.position, footprint = (Cell[])h.footprint.Clone() });
            foreach (var c in cats) copy.cats.Add(c.Copy());
            foreach (var b in boxes)
            {
                var queue = new CatInput[b.cats.Length]; for (int i = 0; i < queue.Length; i++) queue[i] = b.cats[i].Copy();
                copy.boxes.Add(new BoxInput { id = b.id, mouth = b.mouth, position = b.position, direction = b.direction, tower = b.tower, requiredHolesToUnlock = b.requiredHolesToUnlock, colors = (int[])b.colors.Clone(), cats = queue });
            }
            foreach (var c in covers) copy.covers.Add(new CoverInput { id = c.id, remainingHits = c.remainingHits, cells = (Cell[])c.cells.Clone() });
            return copy;
        }
    }

    public enum SolveStatus { Solved, NoSolutionWithinDepthLimit, TimedOut, Cancelled, InvalidInput, UnsupportedMechanics, NextCatFound, NextCatBudgetExhausted, NoNextCatReachable }

    [Serializable]
    public sealed class HoleSnapshot
    {
        public int id, remaining, color, layer, numIced, hiddenCount;
        public bool locked;
        public Cell position;
        public bool finished;
    }

    [Serializable]
    public sealed class BoxSnapshot { public int id, consumed, remainingHolesToUnlock; }
    [Serializable] public sealed class CatSnapshot { public int id, numIced; }
    [Serializable] public sealed class CoverSnapshot { public int id, remainingHits; }

    [Serializable]
    public sealed class BoardSnapshot
    {
        public List<HoleSnapshot> holes = new List<HoleSnapshot>();
        public List<int> remainingCatIds = new List<int>();
        public List<BoxSnapshot> boxes = new List<BoxSnapshot>();
        public List<CatSnapshot> cats = new List<CatSnapshot>();
        public List<CoverSnapshot> covers = new List<CoverSnapshot>();
    }

    [Serializable]
    public sealed class EatEvent
    {
        public int holeId, color;
        public int catId = -1;
        public int boxId = -1;
        public int boxIndex = -1;
    }

    [Serializable]
    public sealed class SolverMove
    {
        public int holeId;
        public Cell start;
        public List<Cell> path = new List<Cell>();
        public List<EatEvent> eaten = new List<EatEvent>();
        public BoardSnapshot expected;
    }

    [Serializable]
    public sealed class SubproblemStats
    {
        public long prunedParking;
        public long prunedConsecutiveGroupDrags;
        public long heuristicCacheHits;
        public long generatedMoveCacheHits;
        public long generatedMoveRequests, generatedMoveCacheMisses, generatedMoveCacheBypassed, generatedMoveCacheEvictions;
        public BoardInput input;
        public int startMoveIndex, eaten;
        public int decisionAlternativeLimit;
        public int decisionAttempt;
        public int decisionAlternative;
        public bool prunedDefaultTurn;
        public string outcome;
        public int depth, firstSolutionDepth, uniqueBoards;
        public long expanded, duplicates, reopened, generated;
        public double seconds;
        public double continuationStartSeconds, continuationEndSeconds, continuationSeconds;
        public long continuationExpanded;
        public int continuationProgressExtensions;
        public bool ownsContinuationBudget;
        public double continuationBudgetSeconds;
        public bool continuationRestored;
        public int beamCompetitiveFrontiers, beamConcentratedFrontiers, beamMaxDominancePermille;
        public string continuationOutcome;
        public List<ModelAttempt> models = new List<ModelAttempt>();
    }

    [Serializable]
    public sealed class ModelAttempt
    {
        public string model, outcome;
        public double seconds;
        public long expanded;
        public int beamCompetitiveFrontiers, beamConcentratedFrontiers;
        public int depthLimit, beamWidth;
    }

    [Serializable]
    public sealed class SolveResult
    {
        public SolveStatus status;
        public string message;
        public SolverConfig config;
        public List<SolverMove> moves = new List<SolverMove>();
        public List<EatEvent> initialEaten = new List<EatEvent>();
        public BoardSnapshot initialExpected;
        public BoardSnapshot finalState;
        public List<SubproblemStats> subproblems = new List<SubproblemStats>();
        public List<string> ignoredMechanics = new List<string>();
        public double seconds;
        public long expanded;
        public long backtracks;
        public int decisionRestarts;
        public int preparedPhaseCacheHits;
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace CatDom.Reference.V42
{
    /// <summary>Stateless entry point. Each invocation owns all search state.</summary>
    public static class CatLevelSolver
    {
        public static SolveResult Solve(BoardInput input, SolverConfig config, CancellationToken cancellationToken = default)
            => SolveCore(input, config, cancellationToken, false);

        /// <summary>Find one consumption event; NextCatFound does not certify a complete level.</summary>
        public static SolveResult FindNextCat(BoardInput input)
            => FindNextCat(input, SolverConfig.CreateNextCatDefault());

        public static SolveResult FindNextCat(BoardInput input, SolverConfig config, CancellationToken cancellationToken = default)
            => SolveCore(input, config, cancellationToken, true);

        /// <summary>Replay an existing plan without searching; optionally audit the relaxed pruning rule at each settled move.</summary>
        public static SolveResult VerifyPlan(BoardInput input, List<SolverMove> moves, double maxSeconds = 10, bool auditRelaxedAssignment = false)
        {
            var clock = Stopwatch.StartNew(); var result = new SolveResult(); Engine engine = null;
            try
            {
                var config = new SolverConfig { maxSolveSeconds = maxSeconds }; Validate(input, config);
                if (moves == null || input.ignoredMechanics.Count > 0) throw new ArgumentException("Plan or supported input required for replay.");
                result.config = config; engine = new Engine(input.Copy(), config, CancellationToken.None, clock, result);
                engine.Replay(moves, auditRelaxedAssignment); result.moves.AddRange(moves);
                result.message = "Existing plan replayed against current mechanics.";
            }
            catch (TimeoutException) { result.status = SolveStatus.TimedOut; result.message = "Plan verification budget exhausted."; }
            catch (ArgumentException ex) { result.status = SolveStatus.InvalidInput; result.message = ex.Message; }
            finally { result.seconds = clock.Elapsed.TotalSeconds; if (engine != null) result.finalState = engine.Snapshot(); }
            return result;
        }

        private static SolveResult SolveCore(BoardInput input, SolverConfig config, CancellationToken cancellationToken, bool nextOnly)
        {
            var clock = Stopwatch.StartNew();
            var result = new SolveResult();
            Engine engine = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Validate(input, config);
                result.config = config.Copy();
                if (result.config.strategy == SearchStrategy.RouteClearing)
                {
                    // Route clearing owns no deadlines and never revisits committed consumption.
                    result.config.maxSolveSeconds = 0;
                    result.config.maxNextCatSearchSeconds = 0;
                    result.config.useAdaptiveModels = false;
                    result.config.useDecisionBacktracking = false;
                    result.config.useEndpointParking = false;
                    result.config.useFocusedGoalRanking = false;
                    result.config.requireFocusedTerminal = false;
                }
                if (nextOnly && result.config.maxNextCatSearchSeconds > 0)
                    result.config.maxSolveSeconds = result.config.maxSolveSeconds > 0
                        ? Math.Min(result.config.maxSolveSeconds, result.config.maxNextCatSearchSeconds) : result.config.maxNextCatSearchSeconds;
                if (input.ignoredMechanics.Count > 0)
                {
                    result.ignoredMechanics.AddRange(input.ignoredMechanics);
                    result.status = SolveStatus.UnsupportedMechanics;
                    result.message = "Cannot certify a solution with unsupported mechanics: " + string.Join(", ", input.ignoredMechanics);
                    return result;
                }
                engine = new Engine(input.Copy(), result.config, cancellationToken, clock, result);
                engine.Run(nextOnly);
                if (result.status == SolveStatus.Solved || result.status == SolveStatus.NextCatFound)
                {
                    // Verify every grid step with the complete dynamic model before
                    // publishing a plan. Replay shares the original solve deadline.
                    var replayResult = new SolveResult();
                    var replay = new Engine(input.Copy(), result.config, cancellationToken, clock, replayResult);
                    replay.Replay(result.moves);
                    if (result.status == SolveStatus.Solved && replayResult.status != SolveStatus.Solved) throw new ArgumentException("Generated plan failed mechanic replay.");
                }
            }
            catch (OperationCanceledException) { result.status = SolveStatus.Cancelled; result.message = "Search cancelled."; }
            catch (NextCatBudgetException) { result.status = SolveStatus.NextCatBudgetExhausted; result.message = "Next-cat search budget exhausted; another policy may find progress on this settled board."; }
            catch (TimeoutException) { result.status = nextOnly ? SolveStatus.NextCatBudgetExhausted : SolveStatus.TimedOut; result.message = "Search time budget exhausted; this does not prove the board unsolvable."; }
            catch (ArgumentException ex) { result.status = SolveStatus.InvalidInput; result.message = ex.Message; }
            finally
            {
                result.seconds = clock.Elapsed.TotalSeconds;
                if (engine != null) result.finalState = engine.Snapshot();
            }
            return result;
        }

        private static void Validate(BoardInput b, SolverConfig c)
        {
            if (b == null || c == null) throw new ArgumentException("Input/config is null.");
            if (!Enum.IsDefined(typeof(SearchStrategy), c.strategy)) throw new ArgumentException("Unknown search strategy.");
            if (!Enum.IsDefined(typeof(NextCatSearch), c.nextCatSearch)) throw new ArgumentException("Unknown next-cat search mode.");
            if (!Enum.IsDefined(typeof(GoalAggregation), c.goalAggregation)) throw new ArgumentException("Unknown goal aggregation.");
            if (c.staticRouteBlockedWeight < 0) throw new ArgumentException("Invalid static-route blocker weight.");
            if (c.decisionAlternativeLimit < 1 || c.decisionAlternativeLimit > 64) throw new ArgumentException("Invalid decision alternative limit.");
            if (c.beamWidth < 1 || c.beamWidth > 256) throw new ArgumentException("Invalid beam width.");
            if (c.fallbackBudgetScale <= 0 || double.IsNaN(c.fallbackBudgetScale) || double.IsInfinity(c.fallbackBudgetScale)) throw new ArgumentException("Invalid fallback budget scale.");
            if (c.earlyFallbackBudgetScale <= 0 || double.IsNaN(c.earlyFallbackBudgetScale) || double.IsInfinity(c.earlyFallbackBudgetScale)) throw new ArgumentException("Invalid early fallback budget scale.");
            if (c.diversityPassSeconds <= 0 || double.IsNaN(c.diversityPassSeconds) || double.IsInfinity(c.diversityPassSeconds)) throw new ArgumentException("Invalid diversity pass budget.");
            if (c.maxNextCatSearchSeconds < 0 || double.IsNaN(c.maxNextCatSearchSeconds) || double.IsInfinity(c.maxNextCatSearchSeconds)) throw new ArgumentException("Invalid next-cat budget.");
            if (b.width <= 0 || b.height <= 0 || (long)b.width * b.height > int.MaxValue) throw new ArgumentException("Invalid board dimensions.");
            if (c.maxSubproblemDepth < 1 || c.blockedCellWeight < 0 || c.distanceWeight < 0 || c.maxSolveSeconds < 0 || double.IsNaN(c.maxSolveSeconds) || double.IsInfinity(c.maxSolveSeconds)) throw new ArgumentException("Invalid solver configuration.");
            if (b.holes == null || b.cats == null || b.boxes == null || b.obstacles == null || b.links == null || b.covers == null || b.colorPaths == null || b.ignoredMechanics == null) throw new ArgumentException("Input collections cannot be null.");
            bool Inside(Cell p) => p.x >= 0 && p.y >= 0 && p.x < b.width && p.y < b.height;
            var occupied = new HashSet<Cell>();
            foreach (var p in b.obstacles) { if (!Inside(p)) throw new ArgumentException("Obstacle outside board."); occupied.Add(p); }
            var ids = new HashSet<int>();
            var holeCells = new Dictionary<Cell, int>();
            foreach (var h in b.holes)
            {
                if (h == null || !ids.Add(h.id) || h.remaining <= 0 || h.movementType < 0 || h.movementType > 2 || h.footprint == null || h.footprint.Length == 0 || !Inside(h.position)) throw new ArgumentException("Invalid hole definition or duplicate ID.");
                if (h.layerColors == null || h.layerCounts == null || h.gates == null || h.layerColors.Length != h.layerCounts.Length || h.layerColors.Length > 2 || h.numIced < 0 || h.hiddenCount < 0) throw new ArgumentException("Invalid hole mechanics.");
                for (int i = 0; i < h.layerCounts.Length; i++) if (h.layerCounts[i] <= 0) throw new ArgumentException("Layer capacities must be positive.");
                foreach (var gate in h.gates) if (gate.directions < 0 || gate.directions > 15 || Array.IndexOf(h.footprint, gate.local) < 0) throw new ArgumentException("Invalid gate footprint/directions.");
                foreach (var d in h.footprint)
                {
                    var p = new Cell(h.position.x + d.x, h.position.y + d.y);
                    if (!Inside(p) || !occupied.Add(p)) throw new ArgumentException("Hole footprint overlaps another hole/obstacle or board boundary.");
                    holeCells[p] = h.color;
                }
            }
            ids.Clear(); var catCells = new HashSet<Cell>();
            foreach (var cat in b.cats)
            {
                if (cat == null || !ids.Add(cat.id) || cat.numIced < 0 || !Inside(cat.position) || !catCells.Add(cat.position)) throw new ArgumentException("Invalid cat or duplicate cat position/ID.");
                if (occupied.Contains(cat.position) && (!holeCells.TryGetValue(cat.position, out var color) || color != cat.color)) throw new ArgumentException("Cat overlaps an obstacle or wrong-color hole.");
            }
            ids.Clear();
            foreach (var box in b.boxes)
            {
                if (box == null || !ids.Add(box.id) || !Inside(box.mouth) || box.colors == null || box.cats == null || box.requiredHolesToUnlock < 0 || (!box.tower && (box.direction < 0 || box.direction > 3))) throw new ArgumentException("Invalid box mouth/queue/ID.");
                if (box.cats.Length != 0 && box.cats.Length != box.colors.Length) throw new ArgumentException("Box metadata length differs from queue.");
            }
            var holeIds = new HashSet<int>(); foreach (var h in b.holes) holeIds.Add(h.id);
            foreach (var link in b.links) if (link.holeId1 == link.holeId2 || !holeIds.Contains(link.holeId1) || !holeIds.Contains(link.holeId2)) throw new ArgumentException("Invalid linked hole IDs.");
            ids.Clear();
            foreach (var cover in b.covers)
            {
                if (cover == null || !ids.Add(cover.id) || cover.remainingHits < 0 || cover.cells == null) throw new ArgumentException("Invalid cover.");
                foreach (var cell in cover.cells) if (!Inside(cell)) throw new ArgumentException("Cover outside board.");
            }
            foreach (var path in b.colorPaths) if (!Inside(path.position)) throw new ArgumentException("Color path outside board.");
        }

        private sealed class NextCatBudgetException : Exception { }
        private sealed class ContinuationBudgetException : Exception { }

        private sealed class Positions : IEquatable<Positions>
        {
            private int[] materialized;
            private readonly int length;
            private readonly bool packed;
            private readonly ulong word0, word1;
            private static readonly int[] hashPowers = MakeHashPowers();
            private readonly int hash;
            internal BigInteger occupied;
            internal bool hasOccupancy;
            private static int[] MakeHashPowers()
            {
                var powers = new int[16]; powers[0] = 1;
                unchecked { for (int i = 1; i < powers.Length; i++) powers[i] = powers[i - 1] * 31; }
                return powers;
            }
            internal int GetValue(int index)
            {
                if (materialized != null) return materialized[index];
                return (int)(((index < 8 ? word0 : word1) >> ((index & 7) * 8)) & 255UL) - 1;
            }
            internal int[] values
            {
                get
                {
                    if (materialized == null)
                    {
                        var array = new int[length];
                        for (int i = 0; i < length; i++) array[i] = GetValue(i);
                        materialized = array;
                    }
                    return materialized;
                }
            }
            internal Positions(int[] initial)
            {
                length = initial.Length; materialized = initial;
                packed = length <= 16;
                foreach (int p in initial) if (p < -1 || p >= 255) packed = false;
                unchecked { hash = 17; foreach (int p in initial) hash = hash * 31 + p; }
                if (packed) for (int i = 0; i < length; i++)
                {
                    ulong value = (ulong)(initial[i] + 1) << ((i & 7) * 8);
                    if (i < 8) word0 |= value; else word1 |= value;
                }
            }
            private Positions(Positions source, int[] members, int translation)
            {
                length = source.length; packed = source.packed;
                foreach (int member in members)
                {
                    int old = source.GetValue(member);
                    if (old >= 0 && (old + translation < 0 || old + translation >= 255)) packed = false;
                }
                if (!packed)
                {
                    materialized = (int[])source.values.Clone();
                    foreach (int member in members) if (materialized[member] >= 0) materialized[member] += translation;
                    unchecked { hash = 17; foreach (int p in materialized) hash = hash * 31 + p; }
                    return;
                }
                ulong first = source.word0, second = source.word1; hash = source.hash;
                foreach (int member in members)
                {
                    int old = source.GetValue(member); if (old < 0) continue;
                    int shift = (member & 7) * 8; ulong mask = 255UL << shift, value = (ulong)(old + translation + 1) << shift;
                    if (member < 8) first = (first & ~mask) | value; else second = (second & ~mask) | value;
                    unchecked { hash += translation * hashPowers[length - member - 1]; }
                }
                word0 = first; word1 = second;
            }
            internal Positions Moved(int[] members, int translation) => new Positions(this, members, translation);
            public bool Equals(Positions other)
            {
                if (other == null || hash != other.hash || length != other.length) return false;
                if (packed && other.packed) return word0 == other.word0 && word1 == other.word1;
                for (int i = 0; i < length; i++) if (GetValue(i) != other.GetValue(i)) return false;
                return true;
            }
            public override bool Equals(object obj) => obj is Positions other && Equals(other);
            public override int GetHashCode() => hash;
        }

        private sealed class Candidate
        {
            internal int hole, destination;
            internal Positions state;
            internal int[] pathParents;
            internal int pathLength;
            private int[] materializedPath;
            internal int[] path
            {
                get
                {
                    if (materializedPath == null)
                    {
                        materializedPath = new int[pathLength];
                        int at = destination;
                        for (int index = pathLength - 1; index >= 0; index--)
                        { materializedPath[index] = at; at = pathParents[at]; }
                    }
                    return materializedPath;
                }
            }
            internal bool terminal;
            internal long score;
            internal long? consumptionUtility;
            internal bool? routeContinuation;
            internal string consumptionPhase;
            internal Candidate CopyGenerated() => new Candidate
            { hole = hole, destination = destination, state = state, pathParents = pathParents, pathLength = pathLength, terminal = terminal };
        }

        private sealed class Node
        {
            internal Node parent;
            internal Candidate move;
            internal int depth;
        }

        private sealed class Engine
        {
            private BigInteger routeFixedMask;
            private readonly Dictionary<string, bool> relaxedProgressionCache = new Dictionary<string, bool>();
            private readonly Dictionary<(bool[][] phase, int hole, int target, Positions state), long> routeClearanceScores
                = new Dictionary<(bool[][], int, int, Positions), long>();
            private BoardInput board;
            private ISearchPolicy policy;
            private SolverConfig config;
            private readonly CancellationToken token;
            private readonly Stopwatch clock;
            private readonly SolveResult result;
            private readonly int count;
            private readonly BigInteger[][] masks;
            private readonly int[][] neighbors;
            private readonly int[] capacities, boxOffsets;
            private readonly int[] layers;
            private readonly int[][] linkedGroups;
            private readonly bool[] catsAlive;
            private readonly BigInteger[] catMasks, boxMasks;
            private Positions current;
            private bool[][] valid, goals;
            private int[][] goalAnchors, distances;
            private Dictionary<long, int[]> targetDistances;
            private int[][] catEntryBlocks;
            private readonly Dictionary<(bool[][] phase, int hole, int target, BigInteger blockers, int blockedWeight, int distanceWeight), long[]> occupancyDistances = new Dictionary<(bool[][], int, int, BigInteger, int, int), long[]>();
            private sealed class PreparedPhase
            {
                internal bool[][] valid, goals;
                internal int[][] distances, anchors;
                internal Dictionary<long, int[]> targets;
                internal int[][] catEntryBlocks;
            }
            private readonly Dictionary<string, PreparedPhase> preparedPhases = new Dictionary<string, PreparedPhase>();
            private readonly Dictionary<(bool[][] phase, int hole, int anchor), BigInteger> staticRoutes = new Dictionary<(bool[][], int, int), BigInteger>();
            private Dictionary<Positions, int> visited;
            private readonly Dictionary<Positions, long> heuristicCache = new Dictionary<Positions, long>();
            private readonly Queue<Positions> heuristicCacheOrder = new Queue<Positions>();
            private bool cacheHeuristic;
            private SubproblemStats stats;
            private int bestDepth;
            private Node best;
            private int searchDepthLimit;
            private double nextCatDeadline;
            private double diversityDeadline;
            private double continuationDeadline;
            private int continuationBestRemaining;
            private SubproblemStats continuationOwnerStats;

            private bool ShouldOwnContinuationBudget(string model)
            {
                return continuationDeadline <= 0 && config.fallbackContinuationSeconds > 0
                    && model != "Default" && model != "PrunedDefaultTurn" && model != "PrunedDefaultAlternative"
                    && (!config.limitDeferredContinuationsOnly || model.StartsWith("MoveBalancedBeamDeferred", StringComparison.Ordinal));
            }

            private double ContinuationWindowSeconds()
            {
                double seconds = config.fallbackContinuationSeconds;
                if (config.continuationRemainingBudgetFraction > 0 && config.maxSolveSeconds > 0)
                    seconds = Math.Min(seconds, Math.Max(0, config.maxSolveSeconds - clock.Elapsed.TotalSeconds)
                        * Math.Min(1, config.continuationRemainingBudgetFraction));
                return seconds;
            }

            private int RemainingCatCount()
            {
                int remaining = 0;
                foreach (bool alive in catsAlive) if (alive) remaining++;
                for (int i = 0; i < boxOffsets.Length; i++) remaining += board.boxes[i].colors.Length - boxOffsets[i];
                return remaining;
            }

            private void RefreshContinuationProgress()
            {
                if (!config.extendContinuationOnCatProgress || continuationDeadline <= 0) return;
                int remaining = RemainingCatCount();
                if (remaining >= continuationBestRemaining) return;
                continuationBestRemaining = remaining;
                continuationDeadline = clock.Elapsed.TotalSeconds + ContinuationWindowSeconds();
                if (continuationOwnerStats != null) continuationOwnerStats.continuationProgressExtensions++;
            }
            private HashSet<string> excludedTerminals;
            private HashSet<long> excludedGoalAnchors;
            private HashSet<string> attemptedConsumptionPhases;
            private static readonly int[] feedbackModels = { 6, 3, 2, 5, 4, 1, 7 };
            private readonly HashSet<string> failedDecisions = new HashSet<string>();
            private readonly Dictionary<string, bool> relaxedGroupTargets = new Dictionary<string, bool>();

            // Policies only order search. All transitions and replay use this Engine.
            private interface ISearchPolicy { long Rank(Engine engine, Positions state); }
            private sealed class DefaultPolicy : ISearchPolicy
            { public long Rank(Engine engine, Positions state) => engine.Score(state); }
            private sealed class FocusedGoalPolicy : ISearchPolicy
            {
                private readonly int hole, target;
                internal FocusedGoalPolicy(int hole, int target) { this.hole = hole; this.target = target; }
                internal bool Accepts(Positions state) => state.GetValue(hole) == target;
                public long Rank(Engine engine, Positions state) => engine.GoalScore(state, hole, target);
            }
            private sealed class SpacePolicy : ISearchPolicy
            {
                public long Rank(Engine engine, Positions state)
                {
                    long baseScore = engine.Score(state);
                    if (baseScore == long.MaxValue) return baseScore;
                    var occupied = engine.Occupancy(state); int mobility = 0;
                    for (int i = 0; i < engine.capacities.Length; i++)
                    {
                        int p = state.GetValue(i); if (p < 0 || !engine.CanDrag(i, p)) continue;
                        var others = occupied ^ engine.masks[i][p];
                        foreach (int q in engine.neighbors[p])
                            if (engine.CanStep(i, p, q) && engine.valid[i][q] && (engine.masks[i][q] & others).IsZero) mobility++;
                    }
                    return baseScore * 4 - mobility * 8L;
                }
            }

            internal Engine(BoardInput board, SolverConfig config, CancellationToken token, Stopwatch clock, SolveResult result)
            {
                this.board = board; this.config = config; this.token = token; this.clock = clock; this.result = result;
                policy = config.strategy == SearchStrategy.SpaceFirst ? (ISearchPolicy)new SpacePolicy() : new DefaultPolicy();
                count = board.width * board.height;
                result.ignoredMechanics.AddRange(board.ignoredMechanics);
                capacities = new int[board.holes.Count]; boxOffsets = new int[board.boxes.Count];
                layers = new int[board.holes.Count];
                // LinkWith builds a transitive group; a finished member is excluded from
                // movement without disconnecting the surviving members of that group.
                var groupIds = new int[board.holes.Count]; for (int i = 0; i < groupIds.Length; i++) groupIds[i] = i;
                foreach (var link in board.links)
                {
                    int a = board.holes.FindIndex(h => h.id == link.holeId1), b = board.holes.FindIndex(h => h.id == link.holeId2);
                    int old = groupIds[b], replacement = groupIds[a];
                    for (int i = 0; i < groupIds.Length; i++) if (groupIds[i] == old) groupIds[i] = replacement;
                }
                linkedGroups = new int[groupIds.Length][];
                for (int i = 0; i < groupIds.Length; i++)
                {
                    var members = new List<int>(); for (int j = 0; j < groupIds.Length; j++) if (groupIds[j] == groupIds[i]) members.Add(j);
                    linkedGroups[i] = members.ToArray();
                }
                catsAlive = new bool[board.cats.Count]; catMasks = new BigInteger[catsAlive.Length]; boxMasks = new BigInteger[boxOffsets.Length];
                for (int i = 0; i < catsAlive.Length; i++) { catsAlive[i] = true; catMasks[i] = Bit(board.cats[i].position); }
                for (int i = 0; i < boxOffsets.Length; i++)
                {
                    var box = board.boxes[i];
                    if (!box.tower) boxMasks[i] = Bit(box.mouth);
                    else foreach (var delta in new[] { new Cell(0, 1), new Cell(1, 0), new Cell(0, -1), new Cell(-1, 0) })
                    {
                        var p = new Cell(box.position.x + delta.x, box.position.y + delta.y);
                        if (Inside(p)) boxMasks[i] |= Bit(p);
                    }
                }
                BigInteger obstacles = BigInteger.Zero;
                foreach (var p in board.obstacles) obstacles |= Bit(p);
                masks = new BigInteger[board.holes.Count][];
                var positions = new int[board.holes.Count];
                for (int i = 0; i < board.holes.Count; i++)
                {
                    CheckBudget();
                    var h = board.holes[i]; capacities[i] = h.remaining; positions[i] = Index(h.position);
                    masks[i] = new BigInteger[count];
                    for (int p = 0; p < count; p++)
                    {
                        BigInteger mask = BigInteger.Zero;
                        foreach (var d in h.footprint)
                        {
                            int x = p % board.width + d.x, y = p / board.width + d.y;
                            if (x < 0 || y < 0 || x >= board.width || y >= board.height) { mask = BigInteger.Zero; break; }
                            mask |= BigInteger.One << (x + board.width * y);
                        }
                        if ((mask & obstacles).IsZero) masks[i][p] = mask;
                    }
                }
                neighbors = new int[count][];
                for (int p = 0; p < count; p++)
                {
                    var list = new List<int>(4);
                    if (p / board.width + 1 < board.height) list.Add(p + board.width);
                    if (p / board.width > 0) list.Add(p - board.width);
                    if (p % board.width > 0) list.Add(p - 1);
                    if (p % board.width + 1 < board.width) list.Add(p + 1);
                    neighbors[p] = list.ToArray();
                }
                current = new Positions(positions);
            }

            private int Index(Cell p) => p.x + board.width * p.y;
            private bool Inside(Cell p) => p.x >= 0 && p.y >= 0 && p.x < board.width && p.y < board.height;
            private Cell CellAt(int p) => new Cell(p % board.width, p / board.width);
            private BigInteger Bit(Cell p) => BigInteger.One << Index(p);
            private int Manhattan(int a, int b) => Math.Abs(a % board.width - b % board.width) + Math.Abs(a / board.width - b / board.width);
            private int Color(int hole) => board.holes[hole].layerColors.Length == 0 ? board.holes[hole].color : board.holes[hole].layerColors[layers[hole]];
            private BigInteger CoverMask()
            {
                BigInteger mask = BigInteger.Zero;
                foreach (var cover in board.covers) if (cover.remainingHits > 0) foreach (var cell in cover.cells) mask |= Bit(cell);
                return mask;
            }
            private bool CanDrop(int hole, int anchor)
            {
                var h = board.holes[hole];
                return anchor >= 0 && h.numIced == 0 && !h.locked && h.hiddenCount == 0 && (masks[hole][anchor] & CoverMask()).IsZero;
            }
            private bool CanDrag(int hole, int anchor)
            {
                var h = board.holes[hole];
                return anchor >= 0 && h.numIced == 0 && !h.locked && (masks[hole][anchor] & CoverMask()).IsZero;
            }
            private int GateBits(int hole, Cell local)
            {
                if (layers[hole] + board.holes[hole].layerOffset != 0) return 0; // First-layer gates fade on layer transition.
                foreach (var gate in board.holes[hole].gates) if (gate.local.Equals(local)) return gate.directions;
                return 0;
            }
            private int Direction(Cell from, Cell to) => to.y > from.y ? 0 : to.x > from.x ? 1 : to.y < from.y ? 2 : 3;
            private bool CanEnterCat(int hole, int anchor, int direction)
            {
                if (board.holes[hole].gates.Length == 0 || layers[hole] + board.holes[hole].layerOffset != 0) return true;
                // Gate entry depends on the settled mechanic phase and anchor,
                // not on other hole positions or the chosen search policy.
                var entries = catEntryBlocks[hole];
                if (entries == null)
                {
                    entries = new int[count];
                    for (int p = 0; p < count; p++) entries[p] = -1;
                    catEntryBlocks[hole] = entries;
                }
                if (entries[anchor] >= 0) return (entries[anchor] & (1 << direction)) == 0;
                var origin = CellAt(anchor);
                int blockedDirections = 0;
                for (int index = 0; index < board.cats.Count; index++)
                {
                    var cat = board.cats[index];
                    if (!catsAlive[index] || (masks[hole][anchor] & catMasks[index]).IsZero) continue;
                    var local = new Cell(cat.position.x - origin.x, cat.position.y - origin.y);
                    blockedDirections |= GateBits(hole, local);
                }
                entries[anchor] = blockedDirections;
                return (blockedDirections & (1 << direction)) == 0;
            }
            private bool BoxTouches(int hole, int anchor, int boxIndex)
            {
                var box = board.boxes[boxIndex];
                if (boxOffsets[boxIndex] >= box.colors.Length || box.requiredHolesToUnlock > 0 || box.colors[boxOffsets[boxIndex]] != Color(hole)) return false;
                if ((masks[hole][anchor] & boxMasks[boxIndex]).IsZero) return false;
                var origin = CellAt(anchor);
                foreach (var local in board.holes[hole].footprint)
                {
                    if (BoxTouchesCell(hole, origin, local, box)) return true;
                }
                return false;
            }
            private bool BoxTouchesCell(int hole, Cell origin, Cell local, BoxInput box)
            {
                var cell = new Cell(origin.x + local.x, origin.y + local.y);
                bool touches = box.tower ? Math.Abs(cell.x - box.position.x) + Math.Abs(cell.y - box.position.y) == 1 : cell.Equals(box.mouth);
                return touches && (GateBits(hole, local) & (1 << Direction(cell, box.position))) == 0;
            }
            private bool CanStep(int hole, int from, int to)
            {
                int type = board.holes[hole].movementType;
                return type == 0 || (type == 1 ? from % board.width == to % board.width : from / board.width == to / board.width);
            }
            private void CheckBudget()
            {
                token.ThrowIfCancellationRequested();
                if (config.maxSolveSeconds > 0 && clock.Elapsed.TotalSeconds >= config.maxSolveSeconds) throw new TimeoutException();
                if (diversityDeadline > 0 && clock.Elapsed.TotalSeconds >= diversityDeadline) throw new NextCatBudgetException();
                if (continuationDeadline > 0 && clock.Elapsed.TotalSeconds >= continuationDeadline) throw new ContinuationBudgetException();
                if (nextCatDeadline > 0 && clock.Elapsed.TotalSeconds >= nextCatDeadline) throw new NextCatBudgetException();
            }

            internal void Run(bool nextOnly)
            {
                result.initialEaten = Consume(); result.initialExpected = Snapshot();
                if (nextOnly && result.initialEaten.Count > 0)
                { result.status = Cleared() ? SolveStatus.Solved : SolveStatus.NextCatFound; return; }
                if (config.useAdaptiveModels && config.useDecisionBacktracking && !nextOnly)
                {
                    int initialLimit = config.decisionAlternativeLimit; bool initialDiversity = config.preferDistinctConsumption, solved = false;
                    try
                    {
                        if (initialDiversity)
                        {
                            var rootBoard = board.Copy(); var rootPositions = current;
                            var rootCapacities = (int[])capacities.Clone(); var rootLayers = (int[])layers.Clone();
                            var rootOffsets = (int[])boxOffsets.Clone(); var rootAlive = (bool[])catsAlive.Clone();
                            diversityDeadline = Math.Min(clock.Elapsed.TotalSeconds + config.diversityPassSeconds, config.maxSolveSeconds > 0 ? config.maxSolveSeconds * 0.25 : double.PositiveInfinity);
                            try { solved = SearchDecisions(); }
                            catch (NextCatBudgetException) { result.subproblems.Add(new SubproblemStats { outcome = "DiversityPassBudgetExhausted" }); }
                            finally { diversityDeadline = 0; config.preferDistinctConsumption = false; }
                            if (!solved)
                            {
                                board = rootBoard; current = rootPositions; result.moves.Clear();
                                Array.Copy(rootCapacities, capacities, capacities.Length); Array.Copy(rootLayers, layers, layers.Length);
                                Array.Copy(rootOffsets, boxOffsets, boxOffsets.Length); Array.Copy(rootAlive, catsAlive, catsAlive.Length);
                                failedDecisions.Clear(); result.decisionRestarts++;
                            }
                        }
                        while (!solved)
                        {
                            solved = SearchDecisions();
                            if (solved || config.decisionAlternativeLimit >= Math.Max(8, initialLimit)) break;
                            // A false result restores the root settled state. Memoized
                            // bounded failures cannot carry into a wider search pass.
                            failedDecisions.Clear(); result.decisionRestarts++;
                            config.decisionAlternativeLimit = Math.Min(8, config.decisionAlternativeLimit * 2);
                            CheckBudget();
                        }
                    }
                    finally { config.decisionAlternativeLimit = initialLimit; config.preferDistinctConsumption = initialDiversity; diversityDeadline = 0; }
                    result.status = solved ? SolveStatus.Solved : SolveStatus.NoSolutionWithinDepthLimit;
                    result.message = solved ? "Adaptive model plan found with decision backtracking." : "Adaptive decision search exhausted its bounded alternatives; this does not prove unsolvability.";
                    return;
                }
                if (config.strategy == SearchStrategy.Backtracking && !nextOnly)
                {
                    stats = new SubproblemStats(); result.subproblems.Add(stats);
                    var seen = new Dictionary<string, int>(); double start = clock.Elapsed.TotalSeconds;
                    bool solved;
                    try { solved = SearchComplete(0, seen); }
                    finally { stats.seconds = clock.Elapsed.TotalSeconds - start; stats.uniqueBoards = seen.Count; }
                    result.status = solved ? SolveStatus.Solved : SolveStatus.NoSolutionWithinDepthLimit;
                    result.message = solved ? "Complete plan found with cross-consumption backtracking." : "Backtracking frontier exhausted within the parking depth limit; this does not prove unsolvability.";
                    return;
                }
                while (!Cleared())
                {
                    CheckBudget();
                    stats = new SubproblemStats { startMoveIndex = result.moves.Count, input = config.captureSubproblemInputs ? CaptureInput() : null };
                    result.subproblems.Add(stats);
                    double start = clock.Elapsed.TotalSeconds;
                    nextCatDeadline = config.maxNextCatSearchSeconds > 0 ? start + config.maxNextCatSearchSeconds : 0;
                    try
                    {
                        Prepare(); best = null; bestDepth = config.maxSubproblemDepth + 1;
                        config.progress?.Invoke(result);
                        // Between consumption events all mechanic flags/targets are fixed.
                        // Parking cannot create a target for a non-draggable component.
                        if (!HasMovableTarget())
                        {
                            stats.outcome = "NoMovableTargets";
                            result.status = config.strategy == SearchStrategy.RouteClearing ? SolveStatus.NoNextCatReachable : SolveStatus.NoSolutionWithinDepthLimit;
                            result.message = "No movable component has a consumption target on this settled board; revisit an earlier decision.";
                            return;
                        }
                        if (config.strategy == SearchStrategy.RouteClearing) SearchRouteClearing();
                        else if (config.useAdaptiveModels) SearchAdaptive();
                        else SearchNext();
                    }
                    catch (TimeoutException) { stats.outcome = "TimedOut"; throw; }
                    catch (NextCatBudgetException) { stats.outcome = "BudgetExhausted"; throw; }
                    finally { stats.seconds = clock.Elapsed.TotalSeconds - start; nextCatDeadline = 0; }
                    if (best == null)
                    {
                        result.status = config.strategy == SearchStrategy.RouteClearing ? SolveStatus.NoNextCatReachable : SolveStatus.NoSolutionWithinDepthLimit;
                        stats.outcome = config.strategy == SearchStrategy.RouteClearing ? "FrontierExhausted" : "DepthExhausted";
                        result.message = config.strategy == SearchStrategy.RouteClearing
                            ? "All reachable parking states exhausted without a consumption event."
                            : "No next-cat solution within the configured depth on the chosen greedy board.";
                        return;
                    }
                    stats.depth = bestDepth;
                    var chain = new List<Candidate>();
                    for (Node n = best; n.parent != null; n = n.parent) chain.Add(n.move);
                    chain.Reverse();
                    foreach (var candidate in chain)
                    {
                        var move = new SolverMove { holeId = board.holes[candidate.hole].id, start = CellAt(current.values[candidate.hole]) };
                        foreach (int p in candidate.path) move.path.Add(CellAt(p));
                        current = candidate.state; move.eaten = Consume(candidate.hole); move.expected = Snapshot(); result.moves.Add(move);
                        stats.eaten += move.eaten.Count;
                    }
                    stats.outcome = "NextCatFound";
                    if (nextOnly)
                    { result.status = Cleared() ? SolveStatus.Solved : SolveStatus.NextCatFound; result.message = "Next consumption found; remaining decisions are not searched."; return; }
                }
                result.status = SolveStatus.Solved; result.message = "All playable holes finished and cats cleared in the core model.";
            }

            // Iterative deepening has no configured maximum. Route pressure orders
            // legal parking moves, but never removes them. Consumption remains on
            // the committed side of Run, outside this parking search.
            private sealed class RoutePolicy : ISearchPolicy
            {
                public long Rank(Engine engine, Positions state)
                {
                    var aggregation = engine.config.goalAggregation;
                    try
                    {
                        engine.config.goalAggregation = GoalAggregation.Minimum;
                        long minimum = engine.Score(state);
                        engine.config.goalAggregation = GoalAggregation.ReachableSum;
                        long pressure = engine.Score(state);
                        return minimum >= long.MaxValue / 1024 ? long.MaxValue
                            : minimum * 1024 + Math.Min(pressure, 1023);
                    }
                    finally { engine.config.goalAggregation = aggregation; }
                }
            }

            private bool prioritizeRouteKeys;
            private void SearchRouteClearing()
            {
                policy = new RoutePolicy();
                config.nextCatSearch = NextCatSearch.FirstProgress;
                int activeHoles = 0;
                foreach (int p in current.values) if (p >= 0) activeHoles++;
                if (activeHoles <= 4) { SearchSmallRouteFrontier(); return; }
                int previousCount = -1;
                for (searchDepthLimit = 1; ; searchDepthLimit++)
                {
                    token.ThrowIfCancellationRequested();
                    best = null; bestDepth = int.MaxValue;
                    visited = new Dictionary<Positions, int>();
                    Search(current, new Node());
                    stats.uniqueBoards += visited.Count;
                    config.progress?.Invoke(result);
                    if (best != null) return;
                    if (searchDepthLimit == 2 && SearchRouteDependencies()) return;
                    // Every state within this parking radius was visited. With no
                    // new states in the next radius, the reachable graph is closed.
                    if (visited.Count == previousCount) return;
                    previousCount = visited.Count;
                    // Finish cheap short-chain passes before the complete explicit
                    // stack search. This is a traversal switch, never a depth cap.
                    if (config.useRouteDepthFirst && searchDepthLimit >= 3)
                    {
                        visited.Clear();
                        // A dependency corridor can miss a useful temporary
                        // relocation. Try a broader target-fixed ordering before
                        // the exhaustive arrangement traversal.
                        if (SearchRouteBeam(false)) return;
                        SearchRouteDepthFirst(12000);
                        if (best != null) return;
                        bool hasLock = false;
                        for (int i = 0; i < capacities.Length; i++)
                            if (current.GetValue(i) >= 0 && board.holes[i].locked) { hasLock = true; break; }
                        if (hasLock)
                        {
                        prioritizeRouteKeys = true;
                        try { if (SearchRouteBeam(false)) return; }
                        finally { prioritizeRouteKeys = false; }
                        }
                        if (best != null || SearchRouteBeam(true)) return;
                        if (SearchRouteDependencies(false, true)) return;
                        SearchRouteDepthFirst(); return;
                    }
                }
            }
            private void SearchSmallRouteFrontier()
            {
                int serial = 0;
                var open = new SortedSet<(long score, int serial, Node node)>(Comparer<(long score, int serial, Node node)>.Create(
                    (a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.serial.CompareTo(b.serial)));
                var seen = new HashSet<Positions> { current };
                open.Add((0, serial++, new Node()));
                while (open.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var entry = open.Min; open.Remove(entry);
                    var node = entry.node; var state = node.move == null ? current : node.move.state;
                    stats.expanded++; result.expanded++;
                    if ((stats.expanded & 4095) == 0) config.progress?.Invoke(result);
                    var moves = Generate(state); stats.generated += moves.Count;
                    Candidate terminal = null;
                    foreach (var move in moves)
                        if (AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0)) terminal = move;
                    if (terminal != null)
                    {
                        best = CompactParkingChain(new Node { parent = node, move = terminal, depth = node.depth + 1 });
                        bestDepth = best.depth; stats.firstSolutionDepth = bestDepth; stats.uniqueBoards += seen.Count; return;
                    }
                    foreach (var move in moves)
                    {
                        if (move.terminal || !seen.Add(move.state)) continue;
                        long score = policy.Rank(this, move.state);
                        open.Add((score, serial++, new Node { parent = node, move = move, depth = node.depth + 1 }));
                    }
                }
                stats.uniqueBoards += seen.Count;
            }
            // A layered accelerator reaches longer clearance chains without a
            // best-first plateau consuming all work at shallow arrangements.
            // Keep alternatives for different moved groups; failure is not a proof.
            private bool SearchRouteBeam(bool aggregateRoutes)
            {
                var frontier = new List<Node> { new Node() };
                var seen = new HashSet<Positions>();
                int work = 0;
                while (frontier.Count > 0 && work < 16384)
                {
                    var pending = new Dictionary<Positions, Node>();
                    foreach (var node in frontier)
                    {
                        token.ThrowIfCancellationRequested();
                        var state = node.move == null ? current : node.move.state;
                        if (!seen.Add(state)) continue;
                        stats.expanded++; result.expanded++; work++;
                        if ((work & 255) == 0) config.progress?.Invoke(result);
                        var moves = Generate(state); stats.generated += moves.Count;
                        Candidate terminal = null;
                        foreach (var move in moves)
                            if (AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0)) terminal = move;
                        if (terminal != null)
                        {
                            best = CompactParkingChain(new Node { parent = node, move = terminal, depth = node.depth + 1 });
                            bestDepth = best.depth; stats.firstSolutionDepth = bestDepth; return true;
                        }
                        foreach (var move in moves)
                        {
                            if (move.terminal || seen.Contains(move.state) || pending.ContainsKey(move.state)) continue;
                            var aggregation = config.goalAggregation;
                            try
                            {
                                // Clearing a dense board can require moving a hole
                                // away from the currently closest cat. Rank this
                                // accelerator by all reachable consumption routes.
                                if (aggregateRoutes)
                                {
                                    config.goalAggregation = GoalAggregation.ReachableSum;
                                    move.score = Score(move.state);
                                }
                                else move.score = policy.Rank(this, move.state);
                            }
                            finally { config.goalAggregation = aggregation; }
                            pending.Add(move.state, new Node { parent = node, move = move, depth = node.depth + 1 });
                        }
                        if (work >= 16384) break;
                    }
                    var ordered = new List<Node>(pending.Values);
                    ordered.Sort((a, b) => Compare(a.move, b.move));
                    frontier = new List<Node>();
                    var selected = new HashSet<Positions>();
                    var groups = new HashSet<int>();
                    foreach (var node in ordered) groups.Add(node.move.hole);
                    int quota = Math.Max(1, 128 / Math.Max(1, groups.Count));
                    var counts = new Dictionary<int, int>();
                    foreach (var node in ordered)
                    {
                        counts.TryGetValue(node.move.hole, out int used);
                        if (used >= quota) continue;
                        counts[node.move.hole] = used + 1;
                        frontier.Add(node); selected.Add(node.move.state);
                    }
                    foreach (var node in ordered)
                    {
                        if (frontier.Count >= 128) break;
                        if (selected.Add(node.move.state)) frontier.Add(node);
                    }
                }
                stats.uniqueBoards += seen.Count;
                return false;
            }
            // A bounded focused traversal is an ordering accelerator, not a
            // solvability test. Failure always returns to the complete search.
            private bool SearchRouteDependencies(bool broaden = false, bool distanceOnly = false)
            {
                var targets = new List<(int hole, int destination, long score)>();
                // The broad pass ranks progress for every hole, including linked
                // groups. It must not spend its entire effort pursuing one
                // single-hole target while a linked group can make progress.
                if (broaden) targets.Add((0, 0, 0));
                for (int i = 0; i < capacities.Length; i++)
                {
                    if (broaden) break;
                    int p = current.GetValue(i);
                    if (p < 0 || linkedGroups[i].Length != 1 || !CanDrag(i, p) || goalAnchors[i].Length == 0) continue;
                    var blockers = Occupancy(current) ^ masks[i][p];
                    foreach (int destination in goalAnchors[i])
                    {
                        long score = OccupancyPathDistance(i, p, blockers, destination) + (distanceOnly ? 0 : RouteClearancePenalty(i, current, blockers, destination));
                        if (score < long.MaxValue / 8) targets.Add((i, destination, score));
                    }
                }
                targets.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score)
                    : a.hole != b.hole ? a.hole.CompareTo(b.hole) : a.destination.CompareTo(b.destination));
                int focusedWork = 0;
                foreach (var target in targets)
                {
                    if (focusedWork >= (distanceOnly ? 8192 : broaden ? 16384 : 32768)) break;
                    int serial = 0;
                    var open = new SortedSet<(long score, int serial, Node node)>(Comparer<(long score, int serial, Node node)>.Create(
                        (a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.serial.CompareTo(b.serial)));
                    var seen = new HashSet<Positions>();
                    var queuedDepth = new Dictionary<Positions, int> { [current] = 0 };
                    open.Add((target.score * 16, serial++, new Node()));
                    for (int expansion = 0; expansion < (distanceOnly ? 4096 : broaden ? 16384 : 1024) && open.Count > 0
                        && focusedWork < (distanceOnly ? 8192 : broaden ? 16384 : 32768); expansion++, focusedWork++)
                    {
                        token.ThrowIfCancellationRequested(); var entry = open.Min; open.Remove(entry);
                        var node = entry.node; var state = node.move == null ? current : node.move.state;
                        if (!seen.Add(state)) continue;
                        stats.expanded++; result.expanded++;
                        if ((expansion & 255) == 0) config.progress?.Invoke(result);
                        var moves = Generate(state); stats.generated += moves.Count;
                        Candidate terminal = null;
                        foreach (var move in moves) if (AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0)) terminal = move;
                        if (terminal != null)
                        {
                            best = CompactParkingChain(new Node { parent = node, move = terminal, depth = node.depth + 1 });
                            bestDepth = best.depth; stats.firstSolutionDepth = bestDepth; return true;
                        }
                        var requestedClearance = new Dictionary<int, BigInteger>();
                        var relevant = broaden ? null : RouteDependencyGroups(target.hole, state, target.destination, out requestedClearance);
                        foreach (var move in moves)
                        {
                            if (move.terminal || seen.Contains(move.state) || (!broaden && !distanceOnly && !relevant.Contains(move.hole))) continue;
                            int depth = node.depth + 1;
                            if (queuedDepth.TryGetValue(move.state, out int previousDepth) && previousDepth <= depth)
                            { stats.duplicates++; continue; }
                            long score;
                            if (broaden) score = policy.Rank(this, move.state) / 1024;
                            else
                            {
                                int p = move.state.GetValue(target.hole);
                                var blockers = Occupancy(move.state) ^ masks[target.hole][p];
                                score = OccupancyPathDistance(target.hole, p, blockers, target.destination)
                                    + (distanceOnly ? 0 : RouteClearancePenalty(target.hole, move.state, blockers, target.destination));
                            }
                            if (score >= long.MaxValue / 8) continue;
                            // Commuting drags can enqueue the same arrangement many
                            // times before it is popped. Keep only an improving
                            // depth; bound this accelerator's bookkeeping too.
                            if (queuedDepth.Count >= 32768) queuedDepth.Clear();
                            queuedDepth[move.state] = depth;
                            int uncleared = 0;
                            foreach (var request in requestedClearance)
                            {
                                int position = move.state.GetValue(request.Key);
                                if (position >= 0 && !(masks[request.Key][position] & request.Value).IsZero) uncleared++;
                            }
                            open.Add((score * 16 + uncleared * (distanceOnly ? 0 : broaden ? 16L : 128L) + node.depth + 1,
                                serial++, new Node { parent = node, move = move, depth = node.depth + 1 }));
                            if (open.Count > 2048) open.Remove(open.Max);
                        }
                    }
                    stats.uniqueBoards += seen.Count;
                }
                return false;
            }

            private HashSet<int> RouteDependencyGroups(int hole, Positions state, int target, out Dictionary<int, BigInteger> requestedClearance)
            {
                requestedClearance = new Dictionary<int, BigInteger>();
                var requests = requestedClearance;
                var relevant = new HashSet<int> { hole };
                int anchor = state.GetValue(hole); var blockers = Occupancy(state) ^ masks[hole][anchor];
                OccupancyPathDistance(hole, anchor, blockers, target);
                var key = (valid, hole, target, blockers, config.blockedCellWeight, config.distanceWeight);
                if (!occupancyDistances.TryGetValue(key, out var costs)) return relevant;
                BigInteger corridor = masks[hole][anchor]; var routeSeen = new bool[count];
                for (int step = 0; step < count && (target >= 0 ? anchor != target : !goals[hole][anchor]); step++)
                {
                    routeSeen[anchor] = true; int next = -1;
                    foreach (int q in neighbors[anchor])
                        if (!routeSeen[q] && valid[hole][q] && (masks[hole][q] & routeFixedMask).IsZero
                            && CanStep(hole, anchor, q) && CanEnterCat(hole, q, Direction(CellAt(anchor), CellAt(q)))
                            && (next < 0 || costs[q] < costs[next])) next = q;
                    if (next < 0) return relevant;
                    anchor = next; corridor |= masks[hole][anchor];
                }
                var pending = new Queue<int>();
                void IncludeBlockers(BigInteger path)
                {
                    for (int i = 0; i < capacities.Length; i++)
                    {
                        int p = state.GetValue(i);
                        if (p < 0 || i == hole || (masks[i][p] & path).IsZero) continue;
                        foreach (int member in linkedGroups[i])
                        {
                            if (state.GetValue(member) < 0 || member == hole) continue;
                            relevant.Add(member);
                            requests.TryGetValue(member, out var previous);
                            var required = previous | path;
                            if (required != previous) { requests[member] = required; pending.Enqueue(member); }
                        }
                    }
                }
                IncludeBlockers(corridor);
                while (pending.Count > 0)
                {
                    int blocker = pending.Dequeue(), start = state.GetValue(blocker);
                    var forbidden = requests[blocker];
                    // Linked groups remain available to the focused traversal;
                    // rigid parking feasibility is checked by Generate and Rank.
                    if (linkedGroups[blocker].Length > 1) continue;
                    var parents = new int[count]; for (int p = 0; p < count; p++) parents[p] = -1;
                    var queue = new Queue<int>(); queue.Enqueue(start); parents[start] = start; int parking = -1;
                    while (queue.Count > 0 && parking < 0)
                    {
                        int p = queue.Dequeue();
                        foreach (int q in neighbors[p])
                        {
                            if (parents[q] >= 0 || !valid[blocker][q] || !CanStep(blocker, p, q)
                                || !CanEnterCat(blocker, q, Direction(CellAt(p), CellAt(q)))
                                || !(masks[blocker][q] & (routeFixedMask & ~masks[blocker][start])).IsZero) continue;
                            parents[q] = p;
                            if (goals[blocker][q] || (masks[blocker][q] & forbidden).IsZero) { parking = q; break; }
                            queue.Enqueue(q);
                        }
                    }
                    if (parking < 0) continue;
                    BigInteger parkingPath = BigInteger.Zero;
                    for (int p = parking; p != start; p = parents[p]) parkingPath |= masks[blocker][p];
                    IncludeBlockers(parkingPath);
                }
                return relevant;
            }

            private sealed class RouteFrame
            {
                internal Node node;
                internal List<Candidate> moves;
                internal int next;
                internal bool expanded;
            }

            // Explicit stack avoids the runtime recursion limit. All reachable
            // parking arrangements remain searchable, without depth/time limits.
            private void SearchRouteDepthFirst(int acceleratorLimit = 0)
            {
                long startingWork = result.expanded;
                var seen = new HashSet<Positions>();
                var stack = new List<RouteFrame> { new RouteFrame { node = new Node() } };
                var cachedFrames = new Queue<(RouteFrame frame, List<Candidate> moves)>();
                int cachedCandidates = 0;
                try
                {
                    while (stack.Count > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (acceleratorLimit > 0 && result.expanded - startingWork >= acceleratorLimit) return;
                        var frame = stack[stack.Count - 1];
                        var state = frame.node.move == null ? current : frame.node.move.state;
                        if (frame.moves == null)
                        {
                            if (!frame.expanded && !seen.Add(state)) { stats.duplicates++; stack.RemoveAt(stack.Count - 1); continue; }
                            frame.expanded = true;
                            stats.expanded++; result.expanded++;
                            if ((stats.expanded & 4095) == 0) config.progress?.Invoke(result);
                            frame.moves = Generate(state); stats.generated += frame.moves.Count;
                            Candidate terminal = null;
                            foreach (var move in frame.moves)
                                if (AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0)) terminal = move;
                            if (terminal != null)
                            {
                                bestDepth = frame.node.depth + 1; stats.firstSolutionDepth = bestDepth;
                                best = CompactParkingChain(new Node { parent = frame.node, move = terminal, depth = bestDepth });
                                bestDepth = best.depth; return;
                            }
                            if (frame.node.move != null)
                            {
                                // Generate enumerates every reachable parking
                                // placement of a rigid group in one drag. Two
                                // consecutive drags of that same group have a
                                // direct equivalent already generated at its parent.
                                int previousHole = frame.node.move.hole;
                                stats.prunedConsecutiveGroupDrags += frame.moves.RemoveAll(m => !m.terminal && m.hole == previousHole);
                            }
                            foreach (var move in frame.moves) move.score = policy.Rank(this, move.state);
                            frame.moves.Sort(Compare);
                            // Keep a bounded working set of sibling lists. Nearby
                            // returns reuse their exact ordering instead of running
                            // every hole's movement BFS and scoring again.
                            cachedFrames.Enqueue((frame, frame.moves));
                            cachedCandidates += frame.moves.Count;
                            while (cachedCandidates > 8192 && cachedFrames.Count > 1)
                            {
                                var oldest = cachedFrames.Dequeue();
                                cachedCandidates -= oldest.moves.Count;
                                if (ReferenceEquals(oldest.frame.moves, oldest.moves)) oldest.frame.moves = null;
                            }
                        }
                        if (frame.next >= frame.moves.Count) { stack.RemoveAt(stack.Count - 1); continue; }
                        var candidate = frame.moves[frame.next++];
                        if (candidate.terminal) continue;
                        if (seen.Contains(candidate.state)) { stats.duplicates++; continue; }
                        stack.Add(new RouteFrame { node = new Node { parent = frame.node, move = candidate, depth = frame.node.depth + 1 } });
                    }
                }
                finally { stats.uniqueBoards = seen.Count; }
            }

            // Skip detours when one exact drag can reach a later arrangement on
            // the discovered chain. No committed consumption is undone.
            private Node CompactParkingChain(Node end)
            {
                var chain = new List<Node>();
                for (var node = end; node.parent != null; node = node.parent) chain.Add(node);
                chain.Reverse();
                var later = new Dictionary<Positions, int>();
                for (int i = 0; i < chain.Count; i++) later[chain[i].move.state] = i;
                var compact = new Node(); var state = current; int index = -1;
                while (index < chain.Count - 1)
                {
                    token.ThrowIfCancellationRequested(); stats.expanded++; result.expanded++;
                    var moves = Generate(state); stats.generated += moves.Count;
                    Candidate selected = null; int selectedIndex = index;
                    foreach (var move in moves)
                    {
                        if (move.terminal)
                        {
                            if (!AcceptTerminal(move)) continue;
                            if (selected == null || !selected.terminal || CompareTerminal(move, selected) < 0) selected = move;
                            selectedIndex = chain.Count - 1;
                        }
                        else if ((selected == null || !selected.terminal) && later.TryGetValue(move.state, out int target) && target > selectedIndex)
                        { selected = move; selectedIndex = target; }
                    }
                    if (selected == null) throw new ArgumentException("Discovered parking chain lost its exact successor.");
                    compact = new Node { parent = compact, move = selected, depth = compact.depth + 1 };
                    state = selected.state; index = selectedIndex;
                }
                return compact;
            }

            private void SearchNext()
            {
                heuristicCache.Clear(); heuristicCacheOrder.Clear(); cacheHeuristic = config.useHeuristicCache;
                try
                {
                if (config.useFocusedGoalRanking) policy = SelectFocusedGoalPolicy();
                best = null; bestDepth = config.maxSubproblemDepth + 1;
                if (config.nextCatSearch == NextCatSearch.BestFirst) { SearchBestFirst(); return; }
                if (config.nextCatSearch == NextCatSearch.Beam) { SearchBeam(); return; }
                bool staged = config.nextCatSearch == NextCatSearch.IterativeDeepening || config.nextCatSearch == NextCatSearch.ProgressiveFirst;
                searchDepthLimit = staged ? 1 : config.maxSubproblemDepth;
                while (true)
                {
                    visited = new Dictionary<Positions, int>();
                    try { Search(current, new Node()); }
                    finally { stats.uniqueBoards += visited.Count; }
                    if (best != null || !staged || searchDepthLimit >= config.maxSubproblemDepth) break;
                    searchDepthLimit = config.nextCatSearch == NextCatSearch.ProgressiveFirst
                        ? (int)Math.Min(config.maxSubproblemDepth, (long)searchDepthLimit * 2) : searchDepthLimit + 1;
                }
                }
                finally { cacheHeuristic = false; heuristicCache.Clear(); heuristicCacheOrder.Clear(); }
            }

            private long RankState(Positions state)
            {
                CheckBudget();
                if (!cacheHeuristic) return policy.Rank(this, state);
                if (heuristicCache.TryGetValue(state, out long score)) { stats.heuristicCacheHits++; return score; }
                score = policy.Rank(this, state);
                // Keep the cache bounded without discarding every useful rank at once.
                // Its lifetime remains one model search, where the ranking policy is fixed.
                if (heuristicCache.Count >= 8192) heuristicCache.Remove(heuristicCacheOrder.Dequeue());
                heuristicCache.Add(state, score); heuristicCacheOrder.Enqueue(state); return score;
            }

            private void SearchBestFirst()
            {
                var heap = new List<(Node node, long priority, long order)>(); long sequence = 0;
                bool Less(int a, int b) => heap[a].priority < heap[b].priority || heap[a].priority == heap[b].priority && heap[a].order < heap[b].order;
                void Push(Node node, long score)
                {
                    long priority = score > (long.MaxValue - node.depth) / 4 ? long.MaxValue : score * 4 + node.depth;
                    heap.Add((node, priority, sequence++)); int child = heap.Count - 1;
                    while (child > 0)
                    {
                        int parent = (child - 1) / 2; if (!Less(child, parent)) break;
                        var item = heap[child]; heap[child] = heap[parent]; heap[parent] = item; child = parent;
                    }
                }
                Node Pop()
                {
                    var node = heap[0].node; heap[0] = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
                    int parent = 0;
                    while (parent * 2 + 1 < heap.Count)
                    {
                        int child = parent * 2 + 1;
                        if (child + 1 < heap.Count && Less(child + 1, child)) child++;
                        if (!Less(child, parent)) break;
                        var item = heap[child]; heap[child] = heap[parent]; heap[parent] = item; parent = child;
                    }
                    return node;
                }
                visited = new Dictionary<Positions, int> { [current] = 0 }; Push(new Node(), 0);
                try
                {
                    while (heap.Count > 0)
                    {
                        CheckBudget(); var node = Pop(); var state = node.move == null ? current : node.move.state;
                        if (visited[state] < node.depth) continue;
                        stats.expanded++; result.expanded++;
                        var moves = Generate(state); stats.generated += moves.Count;
                        Candidate terminal = null;
                        foreach (var move in moves)
                            if (AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0)) terminal = move;
                        if (terminal != null)
                        {
                            bestDepth = node.depth + 1; best = new Node { parent = node, move = terminal, depth = bestDepth };
                            if (stats.firstSolutionDepth == 0) stats.firstSolutionDepth = bestDepth;
                            return;
                        }
                        if (node.depth + 1 >= config.maxSubproblemDepth) continue;
                        foreach (var move in moves)
                        {
                            CheckBudget(); if (move.terminal) continue;
                            int depth = node.depth + 1;
                            if (visited.TryGetValue(move.state, out int previous) && previous <= depth) { stats.duplicates++; continue; }
                            visited[move.state] = depth;
                            Push(new Node { parent = node, move = move, depth = depth }, RankState(move.state));
                        }
                    }
                }
                finally { stats.uniqueBoards += visited.Count; }
            }

            private void SearchBeam()
            {
                var frontier = new List<Node> { new Node() }; long sequence = 0;
                var next = new List<(Node parent, Candidate move, long score, long order)>();
                var pending = config.beamVisitSelectedOnly ? new HashSet<Positions>() : null;
                var buckets = config.balanceBeamByMovedHole ? new List<int>[capacities.Length] : null;
                var bucketOrder = config.balanceBeamByMovedHole ? new List<int>() : null;
                visited = new Dictionary<Positions, int> { [current] = 0 };
                try
                {
                    for (int depth = 0; depth < config.maxSubproblemDepth && frontier.Count > 0; depth++)
                    {
                        next.Clear();
                        pending?.Clear();
                        foreach (var node in frontier)
                        {
                            CheckBudget(); var state = node.move == null ? current : node.move.state;
                            stats.expanded++; result.expanded++;
                            var moves = Generate(state); stats.generated += moves.Count;
                            Candidate terminal = null;
                            foreach (var move in moves)
                                if (AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0)) terminal = move;
                            if (terminal != null)
                            {
                                bestDepth = depth + 1; best = new Node { parent = node, move = terminal, depth = bestDepth };
                                if (stats.firstSolutionDepth == 0) stats.firstSolutionDepth = bestDepth;
                                return;
                            }
                            if (depth + 1 == config.maxSubproblemDepth) continue;
                            foreach (var move in moves)
                            {
                                CheckBudget(); if (move.terminal) continue;
                                if (visited.TryGetValue(move.state, out int previous) && previous <= depth + 1) { stats.duplicates++; continue; }
                                if (pending != null)
                                { if (!pending.Add(move.state)) { stats.duplicates++; continue; } }
                                else visited[move.state] = depth + 1;
                                next.Add((node, move, RankState(move.state), sequence++));
                            }
                        }
                        CheckBudget(); next.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.order.CompareTo(b.order));
                        if (config.captureBeamFrontierStats && next.Count > config.beamWidth && config.beamWidth >= 2)
                        {
                            var selectedCounts = new int[capacities.Length];
                            int firstHole = next[0].move.hole; bool competingHole = false;
                            for (int index = 0; index < next.Count; index++)
                                if (next[index].move.hole != firstHole) { competingHole = true; break; }
                            if (competingHole)
                            {
                                int largest = 0;
                                for (int i = 0; i < config.beamWidth; i++)
                                { int n = ++selectedCounts[next[i].move.hole]; if (n > largest) largest = n; }
                                stats.beamCompetitiveFrontiers++;
                                int dominance = largest * 1000 / config.beamWidth;
                                stats.beamMaxDominancePermille = Math.Max(stats.beamMaxDominancePermille, dominance);
                                if (largest * 4 >= config.beamWidth * 3) stats.beamConcentratedFrontiers++;
                            }
                        }
                        frontier.Clear();
                        if (!config.balanceBeamByMovedHole)
                            for (int i = 0; i < Math.Min(config.beamWidth, next.Count); i++) frontier.Add(new Node { parent = next[i].parent, move = next[i].move, depth = depth + 1 });
                        else
                        {
                            // Preserve promising actions for different blockers.
                            // Each bucket keeps the original score/order ranking;
                            // round-robin selection avoids one moved hole occupying
                            // the entire bounded frontier.
                            bucketOrder.Clear();
                            for (int hole = 0; hole < buckets.Length; hole++) buckets[hole]?.Clear();
                            for (int index = 0; index < next.Count; index++)
                            {
                                CheckBudget(); int hole = next[index].move.hole;
                                if (buckets[hole] == null) buckets[hole] = new List<int>();
                                if (buckets[hole].Count == 0) bucketOrder.Add(hole);
                                buckets[hole].Add(index);
                            }
                            for (int round = 0; frontier.Count < Math.Min(config.beamWidth, next.Count); round++)
                                foreach (int hole in bucketOrder)
                                {
                                    if (frontier.Count >= config.beamWidth) break;
                                    if (round < buckets[hole].Count) { var item = next[buckets[hole][round]]; frontier.Add(new Node { parent = item.parent, move = item.move, depth = depth + 1 }); }
                                }
                        }
                        if (config.beamVisitSelectedOnly)
                            foreach (var node in frontier) { CheckBudget(); visited[node.move.state] = depth + 1; }
                    }
                }
                finally { stats.uniqueBoards += visited.Count; }
            }

            private string TerminalKey(Candidate move) => move.hole + ":" + string.Join(",", move.state.values);

            private bool SearchDecisions()
            {
                CheckBudget(); if (Cleared()) return true;
                string decisionKey = FullStateKey();
                if (failedDecisions.Contains(decisionKey))
                {
                    result.subproblems.Add(new SubproblemStats { startMoveIndex = result.moves.Count, outcome = "CachedBoundedFailure", input = config.captureSubproblemInputs ? CaptureInput() : null });
                    return false;
                }
                if (!HasRelaxedCatAssignment())
                {
                    failedDecisions.Add(decisionKey);
                    result.subproblems.Add(new SubproblemStats { startMoveIndex = result.moves.Count, outcome = "CapacityReachabilityFailure", input = config.captureSubproblemInputs ? CaptureInput() : null });
                    return false;
                }
                var savedBoard = board.Copy(); var savedCurrent = current;
                var savedCapacities = (int[])capacities.Clone(); var savedLayers = (int[])layers.Clone();
                var savedOffsets = (int[])boxOffsets.Clone(); var savedAlive = (bool[])catsAlive.Clone();
                int savedMoves = result.moves.Count;
                var excluded = new HashSet<string>();
                var attemptedGoals = config.useDecisionModelFeedback ? new HashSet<long>() : null;
                var consumptionOutcomes = new HashSet<string>();
                int attemptLimit = config.decisionAlternativeLimit * (config.preferDistinctConsumption ? 4 : 1);
                for (int alternative = 0, attempt = 0; alternative < config.decisionAlternativeLimit && attempt < attemptLimit; attempt++)
                {
                    CheckBudget();
                    SubproblemStats interruptedOwner = null;
                    stats = new SubproblemStats { startMoveIndex = savedMoves, decisionAlternativeLimit = config.decisionAlternativeLimit, decisionAttempt = attempt, decisionAlternative = alternative,
                        prunedDefaultTurn = config.interleavePrunedDefaultAlternatives && alternative > 0 && alternative % 2 == 0,
                        input = config.captureSubproblemInputs ? CaptureInput() : null };
                    result.subproblems.Add(stats); double start = clock.Elapsed.TotalSeconds;
                    var parentExclusions = excludedTerminals; excludedTerminals = excluded;
                    var parentGoals = excludedGoalAnchors; excludedGoalAnchors = attemptedGoals;
                    var parentConsumption = attemptedConsumptionPhases; attemptedConsumptionPhases = config.preferNovelConsumption ? consumptionOutcomes : null;
                    try
                    {
                        Prepare();
                        if (!HasMovableTarget()) { stats.outcome = "NoMovableTargets"; failedDecisions.Add(decisionKey); return false; }
                        int feedbackAlternative = config.interleavePrunedDefaultAlternatives ? (alternative + 1) / 2 : alternative;
                        int preferredModel = config.useDecisionModelFeedback && feedbackAlternative > 0 ? feedbackModels[(feedbackAlternative - 1) % feedbackModels.Length] : 0;
                        if (config.useConsumptionUtility && feedbackAlternative > 0 && feedbackAlternative % 2 == 1) preferredModel = 3;
                        if (config.prioritizeRestrictedHoles && feedbackAlternative > 0 && feedbackAlternative % 2 == 1)
                            for (int i = 0; i < current.values.Length; i++)
                                if (current.values[i] >= 0 && board.holes[i].movementType != 0 && CanDrag(i, current.values[i])) { preferredModel = 4; break; }
                        // Retry Default first, then diversify the beam only after
                        // an earlier consumption branch has been rejected.
                        if ((config.useBalancedBeamOnAlternative || (config.pruneGroupDragsOnAlternative && !config.preserveFeedbackWithAlternativePruning)) && attempt > 0) preferredModel = 0;
                        if (stats.prunedDefaultTurn) preferredModel = 0;
                        SearchAdaptive(alternative % 2 == 0, preferredModel, attempt > 0);
                    }
                    finally { stats.seconds = clock.Elapsed.TotalSeconds - start; excludedTerminals = parentExclusions; excludedGoalAnchors = parentGoals; attemptedConsumptionPhases = parentConsumption; }
                    if (best == null) { stats.outcome = "ModelsExhausted"; failedDecisions.Add(decisionKey); return false; }
                    var terminal = best.move; excluded.Add(TerminalKey(terminal)); stats.depth = bestDepth;
                    attemptedGoals?.Add((long)terminal.hole * count + terminal.destination);
                    var chain = new List<Candidate>();
                    for (Node n = best; n.parent != null; n = n.parent) chain.Add(n.move);
                    chain.Reverse();
                    foreach (var candidate in chain)
                    {
                        var move = new SolverMove { holeId = board.holes[candidate.hole].id, start = CellAt(current.values[candidate.hole]) };
                        foreach (int p in candidate.path) move.path.Add(CellAt(p));
                        current = candidate.state; move.eaten = Consume(candidate.hole); move.expected = Snapshot(); result.moves.Add(move);
                        stats.eaten += move.eaten.Count;
                    }
                    stats.outcome = "NextCatFound";
                    RefreshContinuationProgress();
                    bool repeatedOutcome = (config.preferDistinctConsumption || config.preferNovelConsumption) && !consumptionOutcomes.Add(FullStateKey(false));
                    bool repeated = config.preferDistinctConsumption && repeatedOutcome;
                    if (repeated) stats.outcome = "RepeatedConsumptionOutcome";
                    else
                    {
                        alternative++;
                        if (!config.captureContinuationStats && config.fallbackContinuationSeconds <= 0) { if (SearchDecisions()) return true; }
                        else
                        {
                            // Recursive search replaces the engine's stats field.
                            // Keep this decision's record while measuring its child.
                            var parentStats = stats; long beforeNodes = result.expanded;
                            double savedDeadline = continuationDeadline;
                            int savedBestRemaining = continuationBestRemaining;
                            var savedOwnerStats = continuationOwnerStats;
                            string findingModel = parentStats.models.Count > 0 ? parentStats.models[parentStats.models.Count - 1].model : "Default";
                            bool ownsDeadline = ShouldOwnContinuationBudget(findingModel);
                            parentStats.ownsContinuationBudget = ownsDeadline;
                            if (ownsDeadline)
                            {
                                parentStats.continuationBudgetSeconds = ContinuationWindowSeconds();
                                continuationDeadline = clock.Elapsed.TotalSeconds + parentStats.continuationBudgetSeconds;
                                continuationBestRemaining = RemainingCatCount();
                                continuationOwnerStats = parentStats;
                            }
                            if (config.captureContinuationStats) parentStats.continuationStartSeconds = clock.Elapsed.TotalSeconds;
                            try
                            {
                                bool solved = SearchDecisions();
                                if (config.captureContinuationStats) parentStats.continuationOutcome = solved ? "Solved" : "Exhausted";
                                if (solved) return true;
                            }
                            catch (ContinuationBudgetException)
                            {
                                if (config.captureContinuationStats) parentStats.continuationOutcome = "ContinuationBudgetExhausted";
                                if (!ownsDeadline) throw;
                                if (config.captureContinuationStats) interruptedOwner = parentStats;
                                // Child failures were computed with a shorter horizon.
                                // They cannot rule out revisiting that state with more time.
                                failedDecisions.Clear();
                            }
                            catch (NextCatBudgetException) { if (config.captureContinuationStats) parentStats.continuationOutcome = "PassBudgetExhausted"; throw; }
                            catch (TimeoutException) { if (config.captureContinuationStats) parentStats.continuationOutcome = "TimedOut"; throw; }
                            catch (OperationCanceledException) { if (config.captureContinuationStats) parentStats.continuationOutcome = "Cancelled"; throw; }
                            finally
                            {
                                if (ownsDeadline || !config.extendContinuationOnCatProgress)
                                {
                                    continuationDeadline = savedDeadline;
                                    continuationBestRemaining = savedBestRemaining;
                                    continuationOwnerStats = savedOwnerStats;
                                }
                                if (config.captureContinuationStats)
                                {
                                    parentStats.continuationEndSeconds = clock.Elapsed.TotalSeconds;
                                    parentStats.continuationSeconds = parentStats.continuationEndSeconds - parentStats.continuationStartSeconds;
                                    parentStats.continuationExpanded = result.expanded - beforeNodes;
                                }
                            }
                        }
                    }
                    result.backtracks++; result.moves.RemoveRange(savedMoves, result.moves.Count - savedMoves);
                    board = savedBoard.Copy(); current = savedCurrent;
                    Array.Copy(savedCapacities, capacities, capacities.Length); Array.Copy(savedLayers, layers, layers.Length);
                    Array.Copy(savedOffsets, boxOffsets, boxOffsets.Length); Array.Copy(savedAlive, catsAlive, catsAlive.Length);
                    if (interruptedOwner != null)
                    {
                        interruptedOwner.continuationRestored = result.moves.Count == savedMoves && FullStateKey() == decisionKey;
                        if (!interruptedOwner.continuationRestored) throw new InvalidOperationException("Continuation backtracking failed to restore the saved mechanic state.");
                    }
                }
                failedDecisions.Add(decisionKey); return false;
            }

            private static double AdaptiveModelBudget(SolverConfig original, int model, bool revisitingDecision)
            {
                if (model == 0) return 0.025;
                double budget = (model >= 7 ? 0.5 : 0.1) * original.fallbackBudgetScale;
                if (model < 7 && (!original.earlyFallbackBudgetOnRevisitOnly || revisitingDecision))
                    budget *= original.earlyFallbackBudgetScale;
                return budget;
            }

            private void SearchAdaptive(bool preferTarget = true, int preferredModel = 0, bool revisitingDecision = false)
            {
                var original = config; var originalPolicy = policy;
                try
                {
                    bool aggregate = original.goalAggregation != GoalAggregation.Minimum || original.useStaticRouteBlockers;
                    int complementaryModel = aggregate ? 9 : 8;
                    int originalModelCount = original.useComplementaryDeep ? 9 : 8;
                    int baseModelCount = originalModelCount + (aggregate ? 1 : 0);
                    int modelCount = baseModelCount + (aggregate && original.useComplementaryAggregation ? 1 : 0);
                    int targetRetrySlot = modelCount;
                    if (original.useTargetAwareEndpointRetry) modelCount++;
                    SolverConfig deferredBeamConfig = null; double deferredBeamBudget = 0;
                    SolverConfig wideDeepBeamConfig = null;
                    for (int slot = 0; slot < modelCount; slot++)
                    {
                        // Preserve the original feedback order, including its final
                        // Default retry. New aggregate search runs only after that list.
                        int model = slot == targetRetrySlot && original.useTargetAwareEndpointRetry ? 11 : slot == baseModelCount ? 10 : slot == originalModelCount ? 8 : preferredModel == 0 ? slot : slot == 0 ? preferredModel : slot == originalModelCount - 1 ? 0 : slot >= preferredModel ? slot + 1 : slot;
                        if (aggregate && slot < originalModelCount && model == 8) model = complementaryModel;
                        if (model == 11)
                        {
                            bool triedEndpoint = false, triedTarget = false;
                            foreach (var previous in stats.models)
                            { if (previous.model == "EndpointDeep") triedEndpoint = true; if (previous.model == "EndpointTargetDeep") triedTarget = true; }
                            int reachable = 0;
                            for (int i = 0; i < current.values.Length; i++)
                            {
                                int p = current.values[i];
                                if (p >= 0 && goalAnchors[i].Length > 0 && distances[i][p] != int.MaxValue && CanDrag(i, p)) reachable++;
                            }
                            if (!triedEndpoint || triedTarget || reachable < 2) continue;
                        }
                        bool selectedEndpoint = false;
                        if (model == 10 && (original.complementaryUsesRouteMinimum || original.complementaryUsesDistanceOnly || original.complementaryUsesEndpointAggregation || original.selectComplementaryByState))
                        {
                            // Try clearing routes only when every currently reachable
                            // goal-hole has another group occupying its chosen route.
                            bool found = false, clearRoute = false, waiting = false;
                            int movableCount = 0, waitingCount = 0, reachableCount = 0;
                            var occupied = Occupancy(current);
                            for (int i = 0; i < current.values.Length; i++)
                            {
                                int p = current.values[i];
                                if (original.selectComplementaryByState && p >= 0 && CanDrag(i, p)) movableCount++;
                                if ((original.complementaryUsesEndpointAggregation || original.selectComplementaryByState) && p >= 0 && goalAnchors[i].Length > 0 && CanDrag(i, p) && valid[i][p] && distances[i][p] == int.MaxValue) { waiting = true; waitingCount++; }
                                if (p < 0 || goalAnchors[i].Length == 0 || distances[i][p] == int.MaxValue || !CanDrag(i, p)) continue;
                                found = true; reachableCount++; var others = occupied;
                                foreach (int member in linkedGroups[i]) if (current.values[member] >= 0) others ^= masks[member][current.values[member]];
                                if ((StaticRouteMask(i, p) & others).IsZero) clearRoute = true;
                            }
                            selectedEndpoint = original.selectComplementaryByState && (movableCount >= 10 || waitingCount >= 2 || reachableCount == 1);
                            bool allowWaiting = original.complementaryUsesEndpointAggregation || selectedEndpoint;
                            if (!found || (clearRoute && !(allowWaiting && waiting))) continue;
                        }
                        if (model == 10 && original.complementaryAggregationRequiresWaiting)
                        {
                            // Spend the extra retry only when the settled graph has
                            // both active goals and goals blocked by the current phase.
                            // This selects a heuristic; it never rejects a legal state.
                            bool waiting = false, reachable = false;
                            for (int i = 0; i < current.values.Length; i++)
                            {
                                int p = current.values[i];
                                if (p < 0 || goalAnchors[i].Length == 0 || !CanDrag(i, p)) continue;
                                if (valid[i][p] && distances[i][p] == int.MaxValue) waiting = true;
                                else if (distances[i][p] != int.MaxValue) reachable = true;
                            }
                            if (!waiting || !reachable) continue;
                        }
                        config = SolverConfig.CreateNextCatDefault();
                        config.maxSolveSeconds = original.maxSolveSeconds;
                        config.useHeuristicCache = original.useHeuristicCache;
                        config.captureBeamFrontierStats = original.captureBeamFrontierStats || original.selectBalancedRetryByFrontier;
                        config.beamVisitSelectedOnly = original.beamVisitSelectedOnly;
                        config.pruneConsecutiveGroupDrags = original.pruneConsecutiveGroupDrags || (original.pruneGroupDragsOnAlternative && revisitingDecision)
                            || (original.interleavePrunedDefaultAlternatives && stats.prunedDefaultTurn);
                        config.useGeneratedMoveCache = original.useGeneratedMoveCache;
                        config.generatedMoveCacheRootsOnly = original.generatedMoveCacheRootsOnly;
                        config.preferNovelConsumption = original.preferNovelConsumption && model != 0;
                        if (model > 0) config.maxSubproblemDepth = 12;
                        double budget = AdaptiveModelBudget(original, model, revisitingDecision);
                        string name = "Default";
                        if (model == 0 && original.pruneGroupDragsOnAlternative && revisitingDecision) name = "PrunedDefaultAlternative";
                        if (model == 0 && original.interleavePrunedDefaultAlternatives && stats.prunedDefaultTurn) name = "PrunedDefaultTurn";
                        if (model == 1) { config.nextCatSearch = NextCatSearch.Beam; config.maxSubproblemDepth = 24;
                            config.balanceBeamByMovedHole = original.balanceBeamByMovedHole || (original.useBalancedBeamOnAlternative && revisitingDecision);
                            if (config.balanceBeamByMovedHole) config.beamWidth = original.beamWidth;
                            name = config.balanceBeamByMovedHole ? "MoveBalancedBeam" + config.beamWidth : "Beam"; }
                        if (model == 1 && config.beamVisitSelectedOnly) name = "Frontier" + name;
                        if (model == 2 || model == 5 || model >= 7)
                        {
                            config.nextCatSearch = NextCatSearch.BestFirst;
                            config.useTargetSpecificDistance = model == 5 ? !preferTarget : preferTarget;
                            name = config.useTargetSpecificDistance ? "TargetAware" : "BestFirst";
                            if (model >= 7) name += "Deep";
                        }
                        if ((model == 7 && original.useEndpointParking) || (model == complementaryModel && original.useComplementaryDeep && !original.useEndpointParking))
                        {
                            config.useEndpointParking = true; config.useTargetSpecificDistance = false;
                            config.maxSubproblemDepth = 24; name = "EndpointDeep";
                            bool targetEndpoint = original.useTargetAwareEndpoint;
                            if (original.selectEndpointByState)
                            {
                                int movable = 0, reachable = 0;
                                for (int i = 0; i < current.values.Length; i++)
                                {
                                    int p = current.values[i];
                                    if (p < 0 || !CanDrag(i, p)) continue;
                                    movable++;
                                    if (goalAnchors[i].Length > 0 && distances[i][p] != int.MaxValue) reachable++;
                                }
                                targetEndpoint |= movable >= 10 || reachable >= 4;
                            }
                            if (targetEndpoint)
                            { config.useTargetSpecificDistance = true; name = "EndpointTargetDeep"; }
                        }
                        if ((model == 8 || model == 10) && aggregate)
                        {
                            config.goalAggregation = original.goalAggregation;
                            config.useStaticRouteBlockers = original.useStaticRouteBlockers && model == 8;
                            config.staticRouteBlockedWeight = original.staticRouteBlockedWeight;
                            if (model == 10)
                                config.goalAggregation = original.goalAggregation == GoalAggregation.Sum ? GoalAggregation.ReachableSum
                                    : original.goalAggregation == GoalAggregation.Bottleneck ? GoalAggregation.ReachableBottleneck
                                    : original.goalAggregation == GoalAggregation.BottleneckPlusSum ? GoalAggregation.ReachableBottleneckPlusSum
                                    : original.goalAggregation == GoalAggregation.ReachableBottleneckPlusSum ? GoalAggregation.BottleneckPlusSum
                                    : original.goalAggregation == GoalAggregation.ReachableSum ? GoalAggregation.Sum
                                    : original.goalAggregation == GoalAggregation.Minimum ? GoalAggregation.Minimum : GoalAggregation.Bottleneck;
                            if (model == 10 && original.complementaryUsesRouteMinimum)
                            { config.goalAggregation = GoalAggregation.Minimum; config.useStaticRouteBlockers = true; }
                            if (model == 10 && original.complementaryUsesDistanceOnly)
                            { config.goalAggregation = GoalAggregation.ReachableBottleneck; config.blockedCellWeight = 0; config.useStaticRouteBlockers = false; }
                            config.useTargetSpecificDistance = false;
                            config.useEndpointParking = false; config.maxSubproblemDepth = 12;
                            if (model == 10 && original.complementaryUsesEndpointAggregation)
                            { config.goalAggregation = GoalAggregation.ReachableBottleneck; config.blockedCellWeight = 8; config.useStaticRouteBlockers = false; config.useEndpointParking = true; config.maxSubproblemDepth = 24; }
                            if (model == 10 && original.selectComplementaryByState)
                            { config.goalAggregation = GoalAggregation.ReachableBottleneck; config.blockedCellWeight = selectedEndpoint ? 8 : 0; config.useStaticRouteBlockers = false; config.useEndpointParking = selectedEndpoint; config.maxSubproblemDepth = selectedEndpoint ? 24 : 12; }
                            name = "Goal" + config.goalAggregation + "Deep";
                            if (model == 10 && original.complementaryUsesDistanceOnly) name = "DistanceOnly" + name;
                            if (model == 10 && original.complementaryUsesEndpointAggregation) name = "Endpoint" + name;
                            if (model == 10 && original.selectComplementaryByState) name = (selectedEndpoint ? "Endpoint" : "DistanceOnly") + name;
                            if (config.useStaticRouteBlockers) name = "Route" + name;
                        }
                        if (model == 11)
                        { config.useEndpointParking = true; config.useTargetSpecificDistance = true; config.maxSubproblemDepth = 24; name = "EndpointTargetRetry"; }
                        if (model == 6)
                        {
                            config.nextCatSearch = NextCatSearch.BestFirst; config.useTargetSpecificDistance = true; config.useFocusedGoalRanking = true;
                            config.useOccupancyPathDistance = original.useOccupancyPathDistance;
                            config.requireFocusedTerminal = original.requireFocusedTerminal;
                            name = config.useOccupancyPathDistance ? "PathFocused" : "FocusedGoal";
                            if (config.requireFocusedTerminal) name += "Strict";
                        }
                        if (model == 3) { config.strategy = SearchStrategy.SpaceFirst; name = "SpaceFirst"; }
                        if (model == 3 && original.useConsumptionUtility)
                        { config.strategy = SearchStrategy.Default; config.useConsumptionUtility = true; name = "ConsumptionUtility"; }
                        if (model == 4) { config.blockedCellWeight = 2; name = "LowBlocker"; }
                        if (model == 4 && original.prioritizeRestrictedHoles)
                        { config.blockedCellWeight = 8; config.prioritizeRestrictedHoles = true; name = "RestrictedCat"; }
                        config.usePolicyForTerminalChoice = (original.usePolicyForTerminalChoice && model != 0) || (original.useDecisionModelFeedback && (model == 3 || model == 6));
                        policy = config.strategy == SearchStrategy.SpaceFirst ? (ISearchPolicy)new SpacePolicy() : new DefaultPolicy();
                        if (original.useStateAwareBottleneckBudget && name == "GoalBottleneckDeep")
                        {
                            var aggregation = config.goalAggregation;
                            long pressure;
                            try { config.goalAggregation = GoalAggregation.ReachableBottleneck; pressure = Score(current); }
                            finally { config.goalAggregation = aggregation; }
                            if (pressure != long.MaxValue && pressure > 28)
                            { budget *= 0.5; name = "GoalBottleneckDeepShort"; }
                        }
                        double start = clock.Elapsed.TotalSeconds; long nodes = stats.expanded;
                        int beforeCompetitive = stats.beamCompetitiveFrontiers, beforeConcentrated = stats.beamConcentratedFrontiers;
                        var attempt = new ModelAttempt { model = name }; stats.models.Add(attempt);
                        nextCatDeadline = start + budget;
                        try { SearchNext(); attempt.outcome = best == null ? "DepthExhausted" : "NextCatFound"; }
                        catch (NextCatBudgetException) { best = null; attempt.outcome = "BudgetExhausted"; }
                        catch (TimeoutException) { attempt.outcome = "TimedOut"; throw; }
                        finally { attempt.seconds = clock.Elapsed.TotalSeconds - start; attempt.expanded = stats.expanded - nodes;
                            attempt.beamCompetitiveFrontiers = stats.beamCompetitiveFrontiers - beforeCompetitive;
                            attempt.beamConcentratedFrontiers = stats.beamConcentratedFrontiers - beforeConcentrated;
                            nextCatDeadline = 0; }
                        if (best != null) return;
                        CheckBudget();
                        if (model == 1 && original.useWideDeepBeam) wideDeepBeamConfig = config.Copy();
                        if (model == 1 && name == "Beam" && original.useBalancedBeamRetry && original.deferBalancedBeamRetry
                            && (!original.selectBalancedRetryByFrontier || attempt.beamConcentratedFrontiers >= 2))
                        { deferredBeamConfig = config.Copy(); deferredBeamBudget = budget; }
                        for (int retryKind = 0; model == 1 && name == "Beam" && retryKind < 2; retryKind++)
                        {
                            if (retryKind == 0 ? !original.useBalancedBeamRetry : !original.useFrontierBeamRetry) continue;
                            if (retryKind == 0 && original.deferBalancedBeamRetry) continue;
                            if (retryKind == 0 && original.selectBalancedRetryByFrontier && attempt.beamConcentratedFrontiers < 2) continue;
                            config.balanceBeamByMovedHole = retryKind == 0;
                            config.beamVisitSelectedOnly = retryKind == 1;
                            config.beamWidth = retryKind == 0 ? original.beamWidth : 32;
                            double retryStart = clock.Elapsed.TotalSeconds; long retryNodes = stats.expanded;
                            var retry = new ModelAttempt { model = retryKind == 0 ? "MoveBalancedBeamRetry" + config.beamWidth : "FrontierBeamRetry32" }; stats.models.Add(retry);
                            nextCatDeadline = retryStart + budget;
                            try { SearchNext(); retry.outcome = best == null ? "DepthExhausted" : "NextCatFound"; }
                            catch (NextCatBudgetException) { best = null; retry.outcome = "BudgetExhausted"; }
                            catch (TimeoutException) { retry.outcome = "TimedOut"; throw; }
                            finally { retry.seconds = clock.Elapsed.TotalSeconds - retryStart; retry.expanded = stats.expanded - retryNodes; nextCatDeadline = 0; }
                            if (best != null) return;
                            CheckBudget();
                        }
                        if (original.useDefaultCompletionRetry && slot == 0 && model == 0
                            && attempt.outcome == "BudgetExhausted" && attempt.expanded >= 512)
                        {
                            // A short wall-clock interruption can redirect a shallow
                            // decision to a different model. Retry the same bounded
                            // search before switching policies; the first Default
                            // attempt and the shared whole-level deadline stay intact.
                            double retryStart = clock.Elapsed.TotalSeconds; long retryNodes = stats.expanded;
                            var completion = new ModelAttempt { model = "DefaultCompletion" }; stats.models.Add(completion);
                            nextCatDeadline = retryStart + 0.075 * original.fallbackBudgetScale;
                            try { SearchNext(); completion.outcome = best == null ? "DepthExhausted" : "NextCatFound"; }
                            catch (NextCatBudgetException) { best = null; completion.outcome = "BudgetExhausted"; }
                            catch (TimeoutException) { completion.outcome = "TimedOut"; throw; }
                            finally { completion.seconds = clock.Elapsed.TotalSeconds - retryStart; completion.expanded = stats.expanded - retryNodes; nextCatDeadline = 0; }
                            if (best != null) return;
                            CheckBudget();
                        }
                    }
                    if (deferredBeamConfig != null)
                    {
                        CheckBudget(); config = deferredBeamConfig; config.balanceBeamByMovedHole = true;
                        config.beamWidth = original.beamWidth; policy = new DefaultPolicy();
                        double retryStart = clock.Elapsed.TotalSeconds; long retryNodes = stats.expanded;
                        var retry = new ModelAttempt { model = "MoveBalancedBeamDeferred" + config.beamWidth }; stats.models.Add(retry);
                        nextCatDeadline = retryStart + deferredBeamBudget;
                        try { SearchNext(); retry.outcome = best == null ? "DepthExhausted" : "NextCatFound"; }
                        catch (NextCatBudgetException) { best = null; retry.outcome = "BudgetExhausted"; }
                        catch (TimeoutException) { retry.outcome = "TimedOut"; throw; }
                        finally { retry.seconds = clock.Elapsed.TotalSeconds - retryStart; retry.expanded = stats.expanded - retryNodes; nextCatDeadline = 0; }
                        if (best != null) return;
                    }
                    if (original.useWideDeepBeam && wideDeepBeamConfig != null)
                    {
                        CheckBudget(); config = wideDeepBeamConfig; config.balanceBeamByMovedHole = true;
                        config.beamWidth = 128; config.maxSubproblemDepth = 48; policy = new DefaultPolicy();
                        double wideStart = clock.Elapsed.TotalSeconds; long wideNodes = stats.expanded;
                        var wide = new ModelAttempt { model = "WideDeepBeam128", depthLimit = 48, beamWidth = 128 }; stats.models.Add(wide);
                        nextCatDeadline = wideStart + 0.25 * original.fallbackBudgetScale;
                        try { SearchNext(); wide.outcome = best == null ? "DepthExhausted" : "NextCatFound"; }
                        catch (NextCatBudgetException) { best = null; wide.outcome = "BudgetExhausted"; }
                        catch (TimeoutException) { wide.outcome = "TimedOut"; throw; }
                        finally { wide.seconds = clock.Elapsed.TotalSeconds - wideStart; wide.expanded = stats.expanded - wideNodes; nextCatDeadline = 0; }
                    }
                }
                finally { config = original; policy = originalPolicy; nextCatDeadline = 0; }
            }

            internal void Replay(List<SolverMove> moves, bool auditRelaxedAssignment = false)
            {
                Consume();
                if (auditRelaxedAssignment && !HasRelaxedCatAssignment()) throw new ArgumentException("Relaxed assignment rejected the initial state of a replayable plan.");
                if (auditRelaxedAssignment && !Cleared())
                {
                    Prepare();
                    if (!HasRelaxedCatAssignment()) throw new ArgumentException("Remaining-cat assignment rejected the initial state of a replayable plan.");
                    if (!HasRelaxedProgression()) throw new ArgumentException("Relaxed progression rejected the initial state of a replayable plan.");
                    if (!HasRelaxedGroupTarget()) throw new ArgumentException("Relaxed group reachability rejected the initial state of a replayable plan.");
                }
                foreach (var move in moves)
                {
                    CheckBudget();
                    int hole = board.holes.FindIndex(h => h.id == move.holeId);
                    if (hole < 0 || current.values[hole] < 0 || !move.start.Equals(CellAt(current.values[hole])) || move.path.Count < 2 || !move.path[0].Equals(move.start)) throw new ArgumentException("Invalid replay move start/path.");
                    foreach (int member in linkedGroups[hole]) if (current.values[member] >= 0 && !CanDrag(member, current.values[member])) throw new ArgumentException("Replay attempted to drag a locked/frozen/covered group.");
                    for (int step = 1; step < move.path.Count; step++)
                    {
                        Prepare();
                        var from = move.path[step - 1]; var to = move.path[step];
                        if (Math.Abs(from.x - to.x) + Math.Abs(from.y - to.y) != 1) throw new ArgumentException("Replay path must use adjacent grid steps.");
                        var occupied = Occupancy(current); var group = linkedGroups[hole];
                        foreach (int member in group) if (current.values[member] >= 0) occupied ^= masks[member][current.values[member]];
                        int dx = to.x - from.x, dy = to.y - from.y;
                        var next = (int[])current.values.Clone();
                        foreach (int member in group) if (current.values[member] >= 0)
                        {
                            int old = current.values[member]; var cell = new Cell(old % board.width + dx, old / board.width + dy);
                            if (!Inside(cell)) throw new ArgumentException("Replay group moved outside board.");
                            int destination = Index(cell);
                            if (!CanStep(member, old, destination) || !valid[member][destination] || !(masks[member][destination] & occupied).IsZero || !CanEnterCat(member, destination, Direction(from, to))) throw new ArgumentException("Replay step violates a mechanic or collision.");
                            next[member] = destination;
                        }
                        var oldLayers = (int[])layers.Clone();
                        current = new Positions(next); Consume(hole);
                        if (step + 1 < move.path.Count) foreach (int member in group)
                            if (next[member] >= 0 && (current.values[member] < 0 || oldLayers[member] != layers[member])) throw new ArgumentException("Replay continued after group finish/layer transition.");
                    }
                    if (move.expected != null && !Matches(move.expected, Snapshot())) throw new ArgumentException("Replay mechanic snapshot differs from generated plan.");
                    if (auditRelaxedAssignment && !HasRelaxedCatAssignment()) throw new ArgumentException("Relaxed assignment rejected a settled move of a replayable plan.");
                    if (auditRelaxedAssignment && !Cleared())
                    {
                        Prepare();
                        if (!HasRelaxedCatAssignment()) throw new ArgumentException("Remaining-cat assignment rejected a settled move of a replayable plan.");
                        if (!HasRelaxedProgression()) throw new ArgumentException("Relaxed progression rejected a settled move of a replayable plan.");
                        if (!HasRelaxedGroupTarget()) throw new ArgumentException("Relaxed group reachability rejected a settled move of a replayable plan.");
                    }
                }
                result.status = Cleared() ? SolveStatus.Solved : SolveStatus.NoSolutionWithinDepthLimit;
            }

            private static bool Matches(BoardSnapshot a, BoardSnapshot b)
            {
                if (a.holes.Count != b.holes.Count || a.remainingCatIds.Count != b.remainingCatIds.Count || a.boxes.Count != b.boxes.Count || a.cats.Count != b.cats.Count || a.covers.Count != b.covers.Count) return false;
                for (int i = 0; i < a.holes.Count; i++)
                {
                    var x = a.holes[i]; var y = b.holes[i];
                    if (x.id != y.id || x.color != y.color || x.layer != y.layer || x.numIced != y.numIced || x.hiddenCount != y.hiddenCount || x.locked != y.locked || x.remaining != y.remaining || x.finished != y.finished || !x.position.Equals(y.position)) return false;
                }
                for (int i = 0; i < a.remainingCatIds.Count; i++) if (a.remainingCatIds[i] != b.remainingCatIds[i]) return false;
                for (int i = 0; i < a.boxes.Count; i++) if (a.boxes[i].id != b.boxes[i].id || a.boxes[i].consumed != b.boxes[i].consumed || a.boxes[i].remainingHolesToUnlock != b.boxes[i].remainingHolesToUnlock) return false;
                for (int i = 0; i < a.cats.Count; i++) if (a.cats[i].id != b.cats[i].id || a.cats[i].numIced != b.cats[i].numIced) return false;
                for (int i = 0; i < a.covers.Count; i++) if (a.covers[i].id != b.covers[i].id || a.covers[i].remainingHits != b.covers[i].remainingHits) return false;
                return true;
            }

            private bool Cleared()
            {
                // Sand holes (color >= 1000) are movable blockers, not win targets.
                for (int i = 0; i < board.holes.Count; i++) if (board.holes[i].color < 1000 && current.values[i] >= 0) return false;
                foreach (bool alive in catsAlive) if (alive) return false;
                for (int i = 0; i < boxOffsets.Length; i++) if (boxOffsets[i] < board.boxes[i].colors.Length) return false;
                return true;
            }

            internal BoardSnapshot Snapshot()
            {
                if (current == null) return null;
                var snapshot = new BoardSnapshot();
                for (int i = 0; i < capacities.Length; i++) snapshot.holes.Add(new HoleSnapshot { id = board.holes[i].id, color = Color(i), layer = layers[i] + board.holes[i].layerOffset, numIced = board.holes[i].numIced, hiddenCount = board.holes[i].hiddenCount, locked = board.holes[i].locked, remaining = capacities[i], finished = current.values[i] < 0, position = current.values[i] < 0 ? new Cell(-1, -1) : CellAt(current.values[i]) });
                for (int i = 0; i < catsAlive.Length; i++) if (catsAlive[i])
                {
                    snapshot.remainingCatIds.Add(board.cats[i].id);
                    snapshot.cats.Add(new CatSnapshot { id = board.cats[i].id, numIced = board.cats[i].numIced });
                }
                for (int i = 0; i < boxOffsets.Length; i++) snapshot.boxes.Add(new BoxSnapshot { id = board.boxes[i].id, consumed = boxOffsets[i], remainingHolesToUnlock = board.boxes[i].requiredHolesToUnlock });
                foreach (var cover in board.covers) snapshot.covers.Add(new CoverSnapshot { id = cover.id, remainingHits = cover.remainingHits });
                return snapshot;
            }

            private BoardInput CaptureInput()
            {
                var input = board.Copy(); var holeCopies = input.holes.ToArray(); input.holes.Clear(); input.cats.Clear(); input.links.Clear();
                for (int i = 0; i < capacities.Length; i++) if (current.values[i] >= 0)
                {
                    var h = holeCopies[i]; h.position = CellAt(current.values[i]); h.remaining = capacities[i]; h.color = Color(i);
                    int offset = layers[i]; h.layerOffset += offset;
                    if (offset > 0) h.gates = Array.Empty<GateInput>();
                    if (h.layerColors.Length > 0)
                    {
                        var colors = new int[h.layerColors.Length - offset]; var counts = new int[colors.Length];
                        Array.Copy(h.layerColors, offset, colors, 0, colors.Length); Array.Copy(h.layerCounts, offset, counts, 0, counts.Length);
                        counts[0] = capacities[i]; h.layerColors = colors; h.layerCounts = counts;
                    }
                    input.holes.Add(h);
                    // Preserve connectivity through finished intermediate members.
                    int first = -1;
                    foreach (int member in linkedGroups[i]) if (current.values[member] >= 0) { first = member; break; }
                    if (first == i) foreach (int member in linkedGroups[i]) if (member != i && current.values[member] >= 0)
                        input.links.Add(new LinkInput { holeId1 = board.holes[i].id, holeId2 = board.holes[member].id });
                }
                for (int i = 0; i < catsAlive.Length; i++) if (catsAlive[i]) input.cats.Add(board.cats[i].Copy());
                for (int b = 0; b < boxOffsets.Length; b++)
                {
                    var box = input.boxes[b]; int offset = boxOffsets[b]; var colors = new int[box.colors.Length - offset];
                    Array.Copy(box.colors, offset, colors, 0, colors.Length); box.colors = colors;
                    if (box.cats.Length > 0)
                    { var cats = new CatInput[colors.Length]; Array.Copy(box.cats, offset, cats, 0, cats.Length); box.cats = cats; }
                }
                return input;
            }

            private List<EatEvent> Consume(int dragged = -1)
            {
                var events = new List<EatEvent>();
                // OnDragUpdate resolves linked partners before the selected hole.
                var order = new List<int>();
                if (dragged >= 0)
                {
                    foreach (int member in linkedGroups[dragged]) if (member != dragged) order.Add(member);
                    order.Add(dragged);
                }
                for (int i = 0; i < capacities.Length; i++) if (!order.Contains(i)) order.Add(i);
                bool changed;
                do
                {
                    CheckBudget(); changed = false;
                    foreach (int i in order)
                    {
                        if (!CanDrop(i, current.values[i])) continue;
                        var mask = masks[i][current.values[i]]; int color = Color(i);
                        EatEvent eaten = null;
                        CatInput eatenCat = null;
                        // CheckDropCat visits local occupied cells in footprint order.
                        var origin = CellAt(current.values[i]);
                        foreach (var local in board.holes[i].footprint)
                        {
                            for (int c = 0; c < catsAlive.Length; c++)
                                if (catsAlive[c] && board.cats[c].numIced == 0 && board.cats[c].color == color && board.cats[c].position.Equals(new Cell(origin.x + local.x, origin.y + local.y)))
                                { catsAlive[c] = false; eatenCat = board.cats[c]; eaten = new EatEvent { holeId = board.holes[i].id, color = color, catId = board.cats[c].id }; break; }
                            if (eaten != null) break;
                        }
                        if (eaten == null) for (int sourceType = 0; sourceType < 2 && eaten == null; sourceType++)
                            foreach (var local in board.holes[i].footprint)
                            {
                                for (int b = 0; b < boxOffsets.Length; b++)
                                if (board.boxes[b].tower == (sourceType == 1) && BoxTouches(i, current.values[i], b) && BoxTouchesCell(i, origin, local, board.boxes[b]))
                                {
                                    int index = boxOffsets[b]++;
                                    if (board.boxes[b].cats.Length > 0) eatenCat = board.boxes[b].cats[index];
                                    eaten = new EatEvent { holeId = board.holes[i].id, color = color, boxId = board.boxes[b].id, boxIndex = index }; break;
                                }
                                if (eaten != null) break;
                            }
                        if (eaten == null) continue;
                        events.Add(eaten); capacities[i]--; changed = true;
                        CatFinished(eatenCat);
                        if (capacities[i] == 0)
                        {
                            if (layers[i] + 1 < board.holes[i].layerColors.Length)
                            { layers[i]++; capacities[i] = board.holes[i].layerCounts[layers[i]]; }
                            else
                            {
                                var p = (int[])current.values.Clone(); p[i] = -1; current = new Positions(p);
                                HoleFinished(i);
                            }
                        }
                        break;
                    }
                } while (changed);
                return events;
            }

            private void CatFinished(CatInput cat)
            {
                for (int i = 0; i < board.cats.Count; i++) if (catsAlive[i] && board.cats[i].numIced > 0) board.cats[i].numIced--;
                for (int b = 0; b < board.boxes.Count; b++) for (int c = boxOffsets[b]; c < board.boxes[b].cats.Length; c++)
                    if (board.boxes[b].cats[c].numIced > 0) board.boxes[b].cats[c].numIced--;
                if (cat == null) return;
                if (cat.keyColorId >= 0)
                {
                    int keys = 0;
                    for (int c = 0; c < catsAlive.Length; c++) if (catsAlive[c] && board.cats[c].keyColorId == cat.keyColorId) keys++;
                    for (int b = 0; b < boxOffsets.Length; b++) for (int c = boxOffsets[b]; c < board.boxes[b].cats.Length; c++) if (board.boxes[b].cats[c].keyColorId == cat.keyColorId) keys++;
                    if (keys == 0) foreach (var h in board.holes) if (h.lockColorId == cat.keyColorId) h.locked = false;
                }
                if (cat.pickaxeColorId >= 0) foreach (var cover in board.covers) if (cover.remainingHits > 0) { cover.remainingHits--; break; }
            }

            private void HoleFinished(int source)
            {
                for (int i = 0; i < board.holes.Count; i++) if (i != source && current.values[i] >= 0)
                {
                    if (board.holes[i].numIced > 0) board.holes[i].numIced--;
                    if (board.holes[i].hiddenCount > 0) board.holes[i].hiddenCount--;
                }
                foreach (var box in board.boxes) if (box.requiredHolesToUnlock > 0) box.requiredHolesToUnlock--;
            }

            private void Prepare()
            {
                CheckBudget();
                string phaseKey = config.useAdaptiveModels || config.strategy == SearchStrategy.Backtracking ? FullStateKey(false) : null;
                if (phaseKey != null && preparedPhases.TryGetValue(phaseKey, out var cached))
                {
                    valid = cached.valid; goals = cached.goals; distances = cached.distances;
                    goalAnchors = cached.anchors; targetDistances = cached.targets; catEntryBlocks = cached.catEntryBlocks; result.preparedPhaseCacheHits++;
                    return;
                }
                targetDistances = new Dictionary<long, int[]>();
                catEntryBlocks = new int[capacities.Length][];
                valid = new bool[capacities.Length][]; goals = new bool[capacities.Length][];
                distances = new int[capacities.Length][]; goalAnchors = new int[capacities.Length][];
                bool[] finishedCells = null; int[] finishedDistances = null;
                for (int i = 0; i < capacities.Length; i++)
                {
                    CheckBudget();
                    // Finished holes cannot generate moves or consume goals. Keep
                    // indexed arrays for linked groups, without rebuilding their graph.
                    if (current.values[i] < 0)
                    {
                        if (finishedCells == null)
                        {
                            finishedCells = new bool[count]; finishedDistances = new int[count];
                            for (int p = 0; p < count; p++) finishedDistances[p] = int.MaxValue;
                        }
                        valid[i] = finishedCells; goals[i] = finishedCells;
                        distances[i] = finishedDistances; goalAnchors[i] = Array.Empty<int>();
                        continue;
                    }
                    BigInteger bad = CoverMask(), target = BigInteger.Zero;
                    bool canEat = board.holes[i].numIced == 0 && !board.holes[i].locked && board.holes[i].hiddenCount == 0;
                    for (int c = 0; c < catsAlive.Length; c++) if (catsAlive[c])
                    { if (canEat && board.cats[c].numIced == 0 && board.cats[c].color == Color(i)) target |= catMasks[c]; else bad |= catMasks[c]; }
                    valid[i] = new bool[count]; goals[i] = new bool[count]; distances[i] = new int[count];
                    var anchors = new List<int>(); var queue = new Queue<int>();
                    for (int p = 0; p < count; p++)
                    {
                        distances[i][p] = int.MaxValue;
                        valid[i][p] = !masks[i][p].IsZero && (masks[i][p] & bad).IsZero;
                        if (valid[i][p]) foreach (var path in board.colorPaths)
                            if (!(masks[i][p] & Bit(path.position)).IsZero && (Color(i) >= 1000 || Color(i) < 0 || board.holes[i].hiddenCount > 0 || Color(i) % 10 != path.color)) { valid[i][p] = false; break; }
                        goals[i][p] = valid[i][p] && canEat && !(masks[i][p] & target).IsZero;
                        if (valid[i][p] && canEat && !goals[i][p]) for (int b = 0; b < boxOffsets.Length; b++)
                            if (BoxTouches(i, p, b)) { goals[i][p] = true; break; }
                        if (goals[i][p]) { anchors.Add(p); queue.Enqueue(p); distances[i][p] = 0; }
                    }
                    goalAnchors[i] = anchors.ToArray();
                    while (queue.Count > 0)
                    {
                        int p = queue.Dequeue();
                        foreach (int q in neighbors[p]) if (CanStep(i, p, q) && valid[i][q] && distances[i][q] == int.MaxValue)
                        { distances[i][q] = distances[i][p] + 1; queue.Enqueue(q); }
                    }
                }
                if (config.strategy == SearchStrategy.RouteClearing) routeFixedMask = ImmovableMask();
                if (phaseKey != null)
                {
                    if (preparedPhases.Count >= 128) preparedPhases.Clear();
                    preparedPhases[phaseKey] = new PreparedPhase { valid = valid, goals = goals, distances = distances, anchors = goalAnchors, targets = targetDistances, catEntryBlocks = catEntryBlocks };
                }
            }

            private BigInteger Occupancy(Positions state)
            {
                if (state.hasOccupancy) return state.occupied;
                BigInteger occupied = BigInteger.Zero;
                for (int i = 0; i < capacities.Length; i++) if (state.GetValue(i) >= 0) occupied |= masks[i][state.GetValue(i)];
                state.occupied = occupied; state.hasOccupancy = true;
                return occupied;
            }

            private bool HasMovableTarget()
            {
                if (config.useAdaptiveModels || config.strategy == SearchStrategy.RouteClearing) return HasRelaxedGroupTarget();
                for (int i = 0; i < capacities.Length; i++)
                {
                    if (current.values[i] < 0 || goalAnchors[i].Length == 0) continue;
                    bool draggable = true;
                    foreach (int member in linkedGroups[i])
                        if (current.values[member] >= 0 && !CanDrag(member, current.values[member])) { draggable = false; break; }
                    if (draggable) return true;
                }
                return false;
            }

            // Other holes are removed, but each surviving link member must follow
            // its own static legality and gates. Failure is a necessary dead end;
            // success remains optimistic and never certifies a playable move.
            private bool HasRelaxedGroupTarget()
            {
                CheckBudget();
                string key = FullStateKey();
                if (relaxedGroupTargets.TryGetValue(key, out bool reachable)) return reachable;
                reachable = ComputeRelaxedGroupTarget();
                if (relaxedGroupTargets.Count >= 4096) relaxedGroupTargets.Clear();
                relaxedGroupTargets[key] = reachable;
                return reachable;
            }

            private bool ComputeRelaxedGroupTarget()
            {
                BigInteger fixedMask = ImmovableMask();
                var checkedMembers = new HashSet<int>();
                for (int root = 0; root < capacities.Length; root++)
                {
                    if (current.values[root] < 0 || checkedMembers.Contains(root)) continue;
                    var group = linkedGroups[root]; bool draggable = true, target = false;
                    foreach (int member in group)
                    {
                        checkedMembers.Add(member);
                        if (current.values[member] < 0) continue;
                        if (!CanDrag(member, current.values[member])) draggable = false;
                        if (goalAnchors[member].Length > 0) target = true;
                    }
                    if (!draggable || !target) continue;
                    var seen = new bool[count]; var queue = new Queue<int>();
                    int initial = current.values[root]; seen[initial] = true; queue.Enqueue(initial);
                    while (queue.Count > 0)
                    {
                        CheckBudget(); int p = queue.Dequeue();
                        foreach (int q in neighbors[p])
                        {
                            if (seen[q]) continue;
                            int dx = q % board.width - initial % board.width, dy = q / board.width - initial / board.width;
                            int oldDx = p % board.width - initial % board.width, oldDy = p / board.width - initial / board.width;
                            bool legal = true, eats = false;
                            foreach (int member in group)
                            {
                                int origin = current.values[member]; if (origin < 0) continue;
                                var dest = new Cell(origin % board.width + dx, origin / board.width + dy);
                                if (!Inside(dest)) { legal = false; break; }
                                int destination = Index(dest), from = origin + oldDx + oldDy * board.width;
                                if (!valid[member][destination] || !(masks[member][destination] & fixedMask).IsZero || !CanStep(member, from, destination) || !CanEnterCat(member, destination, Direction(CellAt(p), CellAt(q)))) { legal = false; break; }
                                if (goals[member][destination]) eats = true;
                            }
                            if (!legal) continue;
                            if (eats) return true;
                            seen[q] = true; queue.Enqueue(q);
                        }
                    }
                }
                return false;
            }

            private BigInteger ImmovableMask()
            {
                var groups = new List<int[]>(); var roots = new List<int>(); var seen = new HashSet<int>();
                var fixedGroups = new HashSet<int>(); BigInteger fixedMask = BigInteger.Zero;
                for (int root = 0; root < capacities.Length; root++)
                {
                    if (current.values[root] < 0 || seen.Contains(root)) continue;
                    int index = groups.Count; var group = linkedGroups[root]; groups.Add(group); roots.Add(root);
                    bool fixedGroup = false, vertical = false, horizontal = false;
                    foreach (int member in group)
                    {
                        seen.Add(member); if (current.values[member] < 0) continue;
                        if (!CanDrag(member, current.values[member])) fixedGroup = true;
                        vertical |= board.holes[member].movementType == 1;
                        horizontal |= board.holes[member].movementType == 2;
                    }
                    if (fixedGroup || vertical && horizontal) fixedGroups.Add(index);
                }
                void AddMask(int index)
                {
                    foreach (int member in groups[index]) if (current.values[member] >= 0) fixedMask |= masks[member][current.values[member]];
                }
                foreach (int index in fixedGroups) AddMask(index);
                bool changed;
                do
                {
                    changed = false;
                    for (int index = 0; index < groups.Count; index++)
                    {
                        CheckBudget(); if (fixedGroups.Contains(index)) continue;
                        int origin = current.values[roots[index]]; bool movable = false;
                        foreach (int q in neighbors[origin])
                        {
                            int dx = q % board.width - origin % board.width, dy = q / board.width - origin / board.width;
                            bool legal = true;
                            foreach (int member in groups[index])
                            {
                                int p = current.values[member]; if (p < 0) continue;
                                var cell = new Cell(p % board.width + dx, p / board.width + dy);
                                if (!Inside(cell)) { legal = false; break; }
                                int destination = Index(cell);
                                if (!valid[member][destination] || !CanStep(member, p, destination) || !CanEnterCat(member, destination, Direction(CellAt(origin), CellAt(q))) || !(masks[member][destination] & fixedMask).IsZero) { legal = false; break; }
                            }
                            if (legal) { movable = true; break; }
                        }
                        if (movable) continue;
                        fixedGroups.Add(index); AddMask(index); changed = true;
                    }
                } while (changed);
                return fixedMask;
            }

            // Necessary matching only: ignore other holes, cats, covers, gates,
            // locks, ice, linked partners, queue order and layer order. Future
            // colors may be mixed along a path. Failure in this enlarged model
            // proves that a remaining cat cannot fit the available capacities.
            private bool HasRelaxedCatAssignment()
            {
                var pending = new List<(int color, BigInteger target)>();
                for (int c = 0; c < catsAlive.Length; c++) if (catsAlive[c]) pending.Add((board.cats[c].color, catMasks[c]));
                for (int b = 0; b < boxOffsets.Length; b++)
                    for (int c = boxOffsets[b]; c < board.boxes[b].colors.Length; c++) pending.Add((board.boxes[b].colors[c], boxMasks[b]));
                if (pending.Count == 0) return true;
                var units = new List<(int hole, int color)>(); var reachable = new bool[capacities.Length][];
                for (int i = 0; i < capacities.Length; i++)
                {
                    CheckBudget(); if (current.values[i] < 0) continue;
                    var colors = new HashSet<int>(); var hole = board.holes[i];
                    int last = hole.layerColors.Length == 0 ? layers[i] + 1 : hole.layerColors.Length;
                    for (int layer = layers[i]; layer < last; layer++)
                    {
                        int color = hole.layerColors.Length == 0 ? hole.color : hole.layerColors[layer];
                        colors.Add(color); int capacity = layer == layers[i] ? capacities[i] : hole.layerCounts[layer];
                        for (int unit = 0; unit < Math.Min(capacity, pending.Count); unit++) units.Add((i, color));
                    }
                    bool Allowed(int anchor)
                    {
                        // Masks already exclude immutable obstacles. Removable
                        // covers and other holes remain relaxed here.
                        if (masks[i][anchor].IsZero) return false;
                        foreach (int color in colors)
                        {
                            bool ok = true;
                            foreach (var path in board.colorPaths)
                                if (!(masks[i][anchor] & Bit(path.position)).IsZero && (color < 0 || color >= 1000 || color % 10 != path.color)) { ok = false; break; }
                            if (ok) return true;
                        }
                        return false;
                    }
                    var seen = new bool[count]; var queue = new Queue<int>();
                    int origin = current.values[i]; seen[origin] = true; queue.Enqueue(origin);
                    while (queue.Count > 0)
                    {
                        int p = queue.Dequeue();
                        foreach (int q in neighbors[p]) if (!seen[q] && CanStep(i, p, q) && Allowed(q)) { seen[q] = true; queue.Enqueue(q); }
                    }
                    reachable[i] = seen;
                }
                if (units.Count < pending.Count) return false;
                var edges = new bool[pending.Count][];
                for (int cat = 0; cat < pending.Count; cat++)
                {
                    CheckBudget(); edges[cat] = new bool[units.Count];
                    var access = new bool[capacities.Length];
                    for (int hole = 0; hole < capacities.Length; hole++)
                    {
                        if (reachable[hole] == null) continue;
                        for (int p = 0; p < count; p++)
                            if (reachable[hole][p] && !(masks[hole][p] & pending[cat].target).IsZero) { access[hole] = true; break; }
                    }
                    for (int unit = 0; unit < units.Count; unit++) edges[cat][unit] = pending[cat].color == units[unit].color && access[units[unit].hole];
                }
                var assigned = new int[units.Count]; for (int unit = 0; unit < assigned.Length; unit++) assigned[unit] = -1;
                bool Augment(int cat, bool[] seen)
                {
                    for (int unit = 0; unit < units.Count; unit++)
                    {
                        if (!edges[cat][unit] || seen[unit]) continue;
                        seen[unit] = true;
                        if (assigned[unit] < 0 || Augment(assigned[unit], seen)) { assigned[unit] = cat; return true; }
                    }
                    return false;
                }
                for (int cat = 0; cat < pending.Count; cat++) if (!Augment(cat, new bool[units.Count])) return false;
                return true;
            }

            private int[] generationQueue, generationPathLengths;
            // Monotone over-approximation of consumption dependencies. Ignore
            // hole occupancy, movement, gates, ice, locks and capacity quotas;
            // keep immutable geometry, live other-color cats, box queue order,
            // and the need to consume a layer color before unlocking the next.
            // Colors remain available after promotion. Greedy removal is sound
            // only because this enlarged model never loses an available color.
            private bool HasRelaxedProgression()
            {
                string key = FullStateKey();
                if (relaxedProgressionCache.TryGetValue(key, out bool cached)) return cached;
                var alive = (bool[])catsAlive.Clone(); var offsets = (int[])boxOffsets.Clone();
                // Preserve exposed tokens as witnesses for every possible hole.
                // Removing a cat greedily for one hole must not steal another
                // hole's hypothetical opportunity to unlock its next layer.
                var exposed = new Dictionary<int, BigInteger>();
                void Expose(int color, BigInteger target)
                {
                    exposed.TryGetValue(color, out var previous); exposed[color] = previous | target;
                }
                for (int c = 0; c < alive.Length; c++) if (alive[c]) Expose(board.cats[c].color, catMasks[c]);
                for (int b = 0; b < offsets.Length; b++)
                    if (offsets[b] < board.boxes[b].colors.Length) Expose(board.boxes[b].colors[offsets[b]], boxMasks[b]);
                var colors = new HashSet<int>[capacities.Length]; var witnessed = new HashSet<int>[capacities.Length];
                for (int i = 0; i < capacities.Length; i++)
                    if (current.GetValue(i) >= 0) { colors[i] = new HashSet<int> { Color(i) }; witnessed[i] = new HashSet<int>(); }
                bool changed;
                do
                {
                    token.ThrowIfCancellationRequested(); changed = false;
                    for (int i = 0; i < colors.Length; i++)
                    {
                        if (colors[i] == null) continue;
                        foreach (int color in new List<int>(colors[i]))
                        {
                            BigInteger blocked = BigInteger.Zero;
                            exposed.TryGetValue(color, out var targets);
                            for (int c = 0; c < alive.Length; c++) if (alive[c])
                            {
                                if (board.cats[c].color != color) blocked |= catMasks[c];
                            }
                            for (int p = 0; p < count; p++)
                            {
                                var mask = masks[i][p];
                                // CanDrop can consume at an already occupied
                                // anchor after a layer/box change, even if that
                                // footprint contains another-color cat. Only
                                // entering a new anchor requires cat clearance.
                                if (mask.IsZero || (p != current.GetValue(i) && !(mask & blocked).IsZero) || (mask & targets).IsZero) continue;
                                if (witnessed[i].Add(color)) changed = true;
                                bool consumed = false;
                                for (int c = 0; c < alive.Length; c++)
                                    if (alive[c] && board.cats[c].color == color && !(mask & catMasks[c]).IsZero)
                                    { alive[c] = false; consumed = true; }
                                for (int b = 0; b < offsets.Length; b++)
                                    if (offsets[b] < board.boxes[b].colors.Length && board.boxes[b].colors[offsets[b]] == color && !(mask & boxMasks[b]).IsZero)
                                    {
                                        offsets[b]++; consumed = true;
                                        if (offsets[b] < board.boxes[b].colors.Length) Expose(board.boxes[b].colors[offsets[b]], boxMasks[b]);
                                    }
                                if (consumed) changed = true;
                            }
                        }
                        var layersForHole = board.holes[i].layerColors;
                        for (int layer = layers[i] + 1; layer < layersForHole.Length; layer++)
                            if (witnessed[i].Contains(layersForHole[layer - 1]) && colors[i].Add(layersForHole[layer])) changed = true;
                    }
                } while (changed);
                bool possible = true;
                foreach (bool remains in alive) if (remains) possible = false;
                for (int b = 0; b < offsets.Length; b++) if (offsets[b] < board.boxes[b].colors.Length) possible = false;
                if (relaxedProgressionCache.Count >= 2048) relaxedProgressionCache.Clear();
                relaxedProgressionCache[key] = possible; return possible;
            }
            private readonly Dictionary<(bool[][] phase, Positions state, bool endpoints), (Candidate[] moves, long pruned)> generatedMoves
                = new Dictionary<(bool[][], Positions, bool), (Candidate[], long)>();
            private int generatedMoveCount;

            private List<Candidate> Generate(Positions state)
            {
                CheckBudget();
                if (!config.useGeneratedMoveCache) return GenerateUncached(state);
                stats.generatedMoveRequests++;
                if (config.generatedMoveCacheRootsOnly && !state.Equals(current))
                { stats.generatedMoveCacheBypassed++; return GenerateUncached(state); }
                var key = (valid, state, config.useEndpointParking);
                if (generatedMoves.TryGetValue(key, out var cached))
                {
                    stats.generatedMoveCacheHits++; stats.prunedParking += cached.pruned;
                    var copies = new List<Candidate>(cached.moves.Length);
                    foreach (var move in cached.moves)
                    { if ((copies.Count & 63) == 0) CheckBudget(); copies.Add(move.CopyGenerated()); }
                    return copies;
                }
                stats.generatedMoveCacheMisses++;
                long beforePruned = stats.prunedParking;
                var moves = GenerateUncached(state);
                // Keep canonical, unranked templates private. Search policies sort
                // and annotate their own copies; state and BFS parent trees are immutable.
                if (moves.Count <= 32768)
                {
                    if (generatedMoves.Count >= 256 || generatedMoveCount + moves.Count > 32768)
                    { stats.generatedMoveCacheEvictions += generatedMoves.Count; generatedMoves.Clear(); generatedMoveCount = 0; }
                    var templates = new Candidate[moves.Count];
                    for (int i = 0; i < moves.Count; i++)
                    { if ((i & 63) == 0) CheckBudget(); templates[i] = moves[i].CopyGenerated(); }
                    generatedMoves[key] = (templates, stats.prunedParking - beforePruned); generatedMoveCount += moves.Count;
                }
                return moves;
            }

            private List<Candidate> GenerateUncached(Positions state)
            {
                var result = new List<Candidate>(); var occupied = Occupancy(state);
                // Only parent trees escape through candidates. Queue entries and
                // lengths are overwritten before use and can be reused per engine.
                if (generationQueue == null) generationQueue = new int[count];
                if (generationPathLengths == null) generationPathLengths = new int[count];
                for (int i = 0; i < capacities.Length; i++)
                {
                    CheckBudget();
                    int start = state.GetValue(i); if (start < 0) continue;
                    var group = linkedGroups[i];
                    bool draggable = true; int representative = -1;
                    var blocked = occupied;
                    foreach (int member in group) if (state.GetValue(member) >= 0)
                    {
                        if (representative < 0) representative = member;
                        if (!CanDrag(member, state.GetValue(member))) draggable = false;
                        blocked ^= masks[member][state.GetValue(member)];
                    }
                    if (representative != i || !draggable) continue;
                    var parent = new int[count]; for (int p = 0; p < count; p++) parent[p] = -1;
                    var pathLengths = generationPathLengths; pathLengths[start] = 1;
                    int head = 0, tail = 0; generationQueue[tail++] = start; parent[start] = start;
                    while (head < tail)
                    {
                        CheckBudget();
                        int p = generationQueue[head++];
                        foreach (int q in neighbors[p])
                        {
                            if (parent[q] >= 0) continue;
                            int dx = q % board.width - start % board.width, dy = q / board.width - start / board.width;
                            int stepDx = q % board.width - p % board.width, stepDy = q / board.width - p / board.width;
                            int direction = Direction(CellAt(p), CellAt(q));
                            bool legal = true, terminal = false;
                            foreach (int member in group) if (state.GetValue(member) >= 0)
                            {
                                int old = state.GetValue(member);
                                var cell = new Cell(old % board.width + dx, old / board.width + dy);
                                if (!Inside(cell)) { legal = false; break; }
                                int destination = Index(cell);
                                var previous = new Cell(cell.x - stepDx, cell.y - stepDy);
                                if (!CanStep(member, Index(previous), destination) || !valid[member][destination] || !(masks[member][destination] & blocked).IsZero || !CanEnterCat(member, destination, direction)) { legal = false; break; }
                                terminal |= goals[member][destination];
                            }
                            if (!legal) continue;
                            parent[q] = p;
                            pathLengths[q] = pathLengths[p] + 1;
                            if (!terminal) generationQueue[tail++] = q;
                            if (!terminal && config.useEndpointParking && !IsParkingEndpoint(i, state, q - start, blocked))
                            { stats.prunedParking++; continue; }
                            var nextState = state.Moved(group, q - start);
                            // Parent links are assigned once in BFS. Candidates share this
                            // tree and only materialize a drag when it enters a plan.
                            // Only this linked group moved. Footprints are immutable across
                            // mechanic phases, so the other groups retain their occupancy.
                            var nextOccupied = blocked;
                            foreach (int member in group) if (nextState.GetValue(member) >= 0) nextOccupied |= masks[member][nextState.GetValue(member)];
                            nextState.occupied = nextOccupied; nextState.hasOccupancy = true;
                            result.Add(new Candidate { hole = i, destination = q, state = nextState, terminal = terminal, pathParents = parent, pathLength = pathLengths[q] });
                        }
                    }
                }
                return result;
            }

            // Select release positions only; Generate still traverses pruned anchors
            // and always emits consuming moves. Other models retain full parking.
            private bool IsParkingEndpoint(int hole, Positions positions, int translation, BigInteger blocked)
            {
                var group = linkedGroups[hole];
                foreach (int member in group) if (positions.GetValue(member) >= 0 && !CanDrag(member, positions.GetValue(member) + translation)) return true;
                int origin = positions.GetValue(hole) + translation, directions = 0;
                foreach (int q in neighbors[origin])
                {
                    CheckBudget(); int dx = q % board.width - origin % board.width, dy = q / board.width - origin / board.width;
                    int direction = Direction(CellAt(origin), CellAt(q)); bool legal = true;
                    foreach (int member in group)
                    {
                        int p = positions.GetValue(member); if (p < 0) continue; p += translation;
                        var cell = new Cell(p % board.width + dx, p / board.width + dy);
                        if (!Inside(cell)) { legal = false; break; }
                        int destination = Index(cell);
                        if (!CanStep(member, p, destination) || !valid[member][destination] || !(masks[member][destination] & blocked).IsZero || !CanEnterCat(member, destination, direction)) { legal = false; break; }
                    }
                    if (legal) directions |= 1 << direction;
                }
                return directions != 5 && directions != 10 && directions != 15;
            }

            private long Score(Positions state)
            {
                var occupied = Occupancy(state); long score = long.MaxValue, sum = 0, bottleneck = 0;
                for (int i = 0; i < capacities.Length; i++)
                {
                    int p = state.GetValue(i); if (p < 0 || !CanDrag(i, p)) continue;
                    if (config.strategy == SearchStrategy.RouteClearing)
                    {
                        bool draggable = true;
                        foreach (int member in linkedGroups[i])
                            if (state.GetValue(member) >= 0 && !CanDrag(member, state.GetValue(member))) draggable = false;
                        if (!draggable) continue;
                    }
                    // Aggregate only goals reachable in this mechanic phase. A hole
                    // behind other-colored cats may become relevant after consumption.
                    // Keep invalid starting anchors: a layer transition can leave a
                    // hole over an old-color cat, yet a legal exit can still reach a goal.
                    if ((config.goalAggregation == GoalAggregation.ReachableSum || config.goalAggregation == GoalAggregation.ReachableBottleneck || config.goalAggregation == GoalAggregation.ReachableBottleneckPlusSum) && (config.useReachabilityDistance || config.useTargetSpecificDistance)
                        && valid[i][p] && distances[i][p] == int.MaxValue) continue;
                    long holeScore = long.MaxValue;
                    if (config.useOccupancyPathDistance)
                    {
                        var blockers = occupied;
                        foreach (int member in linkedGroups[i]) if (state.GetValue(member) >= 0) blockers ^= masks[member][state.GetValue(member)];
                        holeScore = config.strategy == SearchStrategy.RouteClearing && linkedGroups[i].Length > 1
                            ? LinkedRouteDistance(i, state, blockers) : OccupancyPathDistance(i, p, blockers);
                        if (config.strategy == SearchStrategy.RouteClearing && linkedGroups[i].Length == 1)
                            holeScore += RouteClearancePenalty(i, state, blockers);
                        if (prioritizeRouteKeys && config.strategy == SearchStrategy.RouteClearing && linkedGroups[i].Length == 1)
                        {
                            foreach (int target in goalAnchors[i])
                            {
                                bool unlocks = false;
                                for (int c = 0; c < catsAlive.Length && !unlocks; c++)
                                {
                                    var cat = board.cats[c];
                                    if (!catsAlive[c] || cat.keyColorId < 0 || cat.color != Color(i)
                                        || (masks[i][target] & (BigInteger.One << Index(cat.position))).IsZero) continue;
                                    for (int h = 0; h < capacities.Length; h++)
                                        if (state.GetValue(h) >= 0 && board.holes[h].locked && board.holes[h].lockColorId == cat.keyColorId)
                                        { unlocks = true; break; }
                                }
                                if (!unlocks) continue;
                                long keyCost = OccupancyPathDistance(i, p, blockers, target);
                                if (keyCost >= long.MaxValue / 8) continue;
                                keyCost += RouteClearancePenalty(i, state, blockers, target);
                                holeScore = Math.Min(holeScore, keyCost / 8);
                            }
                        }
                    }
                    else
                    {
                        var others = occupied ^ masks[i][p];
                        int sharedDistance = config.useReachabilityDistance ? distances[i][p] : 0;
                        foreach (int target in goalAnchors[i])
                        {
                            int blocked = 0;
                            if (config.blockedCellWeight != 0)
                            {
                                BigInteger overlap = masks[i][target] & others;
                                while (!overlap.IsZero) { overlap &= overlap - BigInteger.One; blocked++; }
                            }
                            int distance = config.distanceWeight == 0 ? 0 : config.useTargetSpecificDistance ? TargetDistance(i, target, p) : config.useReachabilityDistance ? sharedDistance : Manhattan(p, target);
                            long value = (long)config.blockedCellWeight * blocked + (long)config.distanceWeight * distance;
                            if (value < holeScore) holeScore = value;
                        }
                    }
                    if (config.useStaticRouteBlockers && holeScore != long.MaxValue)
                    {
                        var others = occupied;
                        foreach (int member in linkedGroups[i]) if (state.GetValue(member) >= 0) others ^= masks[member][state.GetValue(member)];
                        var overlap = StaticRouteMask(i, p) & others; int blocked = 0;
                        while (!overlap.IsZero) { overlap &= overlap - BigInteger.One; blocked++; }
                        long penalty = (long)config.staticRouteBlockedWeight * blocked;
                        holeScore = holeScore > long.MaxValue - penalty ? long.MaxValue : holeScore + penalty;
                    }
                    score = Math.Min(score, holeScore);
                    if (config.goalAggregation == GoalAggregation.Minimum || holeScore == long.MaxValue) continue;
                    sum = sum > long.MaxValue - holeScore ? long.MaxValue : sum + holeScore;
                    bottleneck = Math.Max(bottleneck, holeScore);
                }
                if (score == long.MaxValue || config.goalAggregation == GoalAggregation.Minimum) return score;
                if (config.goalAggregation == GoalAggregation.BottleneckPlusSum || config.goalAggregation == GoalAggregation.ReachableBottleneckPlusSum)
                    return sum > long.MaxValue - bottleneck ? long.MaxValue : sum + bottleneck;
                return config.goalAggregation == GoalAggregation.Sum || config.goalAggregation == GoalAggregation.ReachableSum ? sum : bottleneck;
            }

            // One deterministic shortest static route provides a cheap pressure
            // estimate. It does not restrict Generate or prove a route impossible.
            private BigInteger StaticRouteMask(int hole, int anchor)
            {
                var key = (valid, hole, anchor);
                if (staticRoutes.TryGetValue(key, out var cached)) return cached;
                BigInteger route = BigInteger.Zero; int p = anchor;
                if (distances[hole][p] == int.MaxValue)
                {
                    // A changed layer can leave an invalid root with a legal exit.
                    if (valid[hole][p]) return route;
                    int exit = -1;
                    foreach (int q in neighbors[p]) if (valid[hole][q] && distances[hole][q] != int.MaxValue && CanStep(hole, p, q))
                        if (exit < 0 || distances[hole][q] < distances[hole][exit]) exit = q;
                    if (exit < 0) return route;
                    route |= masks[hole][p]; p = exit;
                }
                while (true)
                {
                    CheckBudget(); route |= masks[hole][p];
                    if (distances[hole][p] == 0) break;
                    int next = -1;
                    foreach (int q in neighbors[p]) if (valid[hole][q] && distances[hole][q] == distances[hole][p] - 1 && CanStep(hole, p, q)) { next = q; break; }
                    if (next < 0) break;
                    p = next;
                }
                if (staticRoutes.Count >= 4096) staticRoutes.Clear();
                staticRoutes[key] = route; return route;
            }

            // Inspect a suggested corridor before favoring its target. A blocker
            // must have a reachable parking footprint outside the whole corridor,
            // not merely one legal adjacent move. Other movable groups are ignored
            // in this test: a failed test discourages this route, never prunes the
            // search (another route or a consumption may unlock it).
            private long RouteClearancePenalty(int hole, Positions state, BigInteger blockers, int target = -1)
            {
                var key = (valid, hole, target, state);
                if (routeClearanceScores.TryGetValue(key, out long cached)) return cached;
                long value = ComputeRouteClearancePenalty(hole, state, blockers, target);
                if (routeClearanceScores.Count >= 4096) routeClearanceScores.Clear();
                routeClearanceScores[key] = value;
                return value;
            }

            private long ComputeRouteClearancePenalty(int hole, Positions state, BigInteger blockers, int target)
            {
                var key = (valid, hole, target, blockers, config.blockedCellWeight, config.distanceWeight);
                if (!occupancyDistances.TryGetValue(key, out var costs)) return 0;
                int anchor = state.GetValue(hole);
                BigInteger corridor = masks[hole][anchor];
                var routeSeen = new bool[count];
                for (int step = 0; step < count && (target >= 0 ? anchor != target : !goals[hole][anchor]); step++)
                {
                    routeSeen[anchor] = true; int next = -1; long bestCost = long.MaxValue;
                    foreach (int q in neighbors[anchor])
                    {
                        if (routeSeen[q] || !valid[hole][q] || !(masks[hole][q] & routeFixedMask).IsZero
                            || !CanStep(hole, anchor, q) || !CanEnterCat(hole, q, Direction(CellAt(anchor), CellAt(q)))) continue;
                        if (costs[q] < bestCost) { next = q; bestCost = costs[q]; }
                    }
                    if (next < 0 || bestCost >= long.MaxValue / 8) return 0;
                    anchor = next; corridor |= masks[hole][anchor];
                }
                if (target >= 0 ? anchor != target : !goals[hole][anchor]) return 0;
                long penalty = 0; var checkedMembers = new bool[capacities.Length];
                checkedMembers[hole] = true;
                for (int root = 0; root < capacities.Length; root++)
                {
                    if (checkedMembers[root] || state.GetValue(root) < 0) continue;
                    var group = linkedGroups[root]; BigInteger footprint = BigInteger.Zero;
                    bool draggable = true;
                    foreach (int member in group)
                    {
                        checkedMembers[member] = true; int p = state.GetValue(member);
                        if (p < 0) continue;
                        footprint |= masks[member][p]; draggable &= CanDrag(member, p);
                    }
                    if ((footprint & corridor).IsZero) continue;
                    if (!draggable) { penalty += 100000; continue; }
                    var settled = new bool[count]; var parkingCosts = new long[count]; var parkingGoals = new bool[count];
                    for (int p = 0; p < count; p++) parkingCosts[p] = long.MaxValue;
                    int origin = state.GetValue(root); parkingCosts[origin] = 0;
                    var parkingBlockers = Occupancy(state) & ~footprint;
                    bool cleared = false;
                    for (int step = 0; step < count && !cleared; step++)
                    {
                        token.ThrowIfCancellationRequested(); int parkingAnchor = -1;
                        for (int p = 0; p < count; p++)
                            if (!settled[p] && parkingCosts[p] != long.MaxValue && (parkingAnchor < 0 || parkingCosts[p] < parkingCosts[parkingAnchor])) parkingAnchor = p;
                        if (parkingAnchor < 0) break;
                        settled[parkingAnchor] = true;
                        if (parkingGoals[parkingAnchor]) { penalty += parkingCosts[parkingAnchor]; cleared = true; break; }
                        foreach (int q in neighbors[parkingAnchor])
                        {
                            if (settled[q]) continue;
                            int dx = q % board.width - origin % board.width, dy = q / board.width - origin / board.width;
                            int oldDx = parkingAnchor % board.width - origin % board.width;
                            int oldDy = parkingAnchor / board.width - origin / board.width;
                            bool legal = true, eats = false; BigInteger moved = BigInteger.Zero;
                            foreach (int member in group)
                            {
                                int p = state.GetValue(member); if (p < 0) continue;
                                var cell = new Cell(p % board.width + dx, p / board.width + dy);
                                if (!Inside(cell)) { legal = false; break; }
                                int dest = Index(cell), from = p + oldDx + oldDy * board.width;
                                if (!valid[member][dest] || !(masks[member][dest] & (routeFixedMask & ~footprint)).IsZero
                                    || !CanStep(member, from, dest) || !CanEnterCat(member, dest, Direction(CellAt(parkingAnchor), CellAt(q))))
                                { legal = false; break; }
                                moved |= masks[member][dest]; eats |= goals[member][dest];
                            }
                            if (!legal) continue;
                            var overlap = moved & parkingBlockers; int blocked = 0;
                            while (!overlap.IsZero) { overlap &= overlap - BigInteger.One; blocked++; }
                            long nextCost = parkingCosts[parkingAnchor] + 8 + (long)blocked * config.blockedCellWeight * 8;
                            if (nextCost < parkingCosts[q]) parkingCosts[q] = nextCost;
                            parkingGoals[q] = eats || (moved & corridor).IsZero;
                        }
                    }
                    if (!cleared) penalty += 100000;
                }
                return penalty;
            }

            // Heuristic only: other movable holes can be crossed for a penalty.
            // Exact Generate/Replay still enforce occupancy, links and gates.
            private long OccupancyPathDistance(int hole, int anchor, BigInteger blockers, int target = -1)
            {
                const long infinity = long.MaxValue / 8;
                var key = (valid, hole, target, blockers, config.blockedCellWeight, config.distanceWeight);
                if (!occupancyDistances.TryGetValue(key, out var costs))
                {
                    costs = new long[count]; var penalties = new long[count];
                    for (int p = 0; p < count; p++)
                    {
                        CheckBudget(); costs[p] = infinity;
                        var overlap = masks[hole][p] & blockers; int blocked = 0;
                        while (!overlap.IsZero) { overlap &= overlap - BigInteger.One; blocked++; }
                        penalties[p] = (long)config.blockedCellWeight * blocked;
                    }
                    var heap = new List<(int p, long cost)>();
                    void Push(int p, long cost)
                    {
                        heap.Add((p, cost)); int child = heap.Count - 1;
                        while (child > 0)
                        {
                            int parent = (child - 1) / 2;
                            if (heap[parent].cost <= heap[child].cost) break;
                            var item = heap[parent]; heap[parent] = heap[child]; heap[child] = item; child = parent;
                        }
                    }
                    bool Allowed(int p) => valid[hole][p] && (config.strategy != SearchStrategy.RouteClearing || (masks[hole][p] & routeFixedMask).IsZero);
                    if (target >= 0) { if (Allowed(target)) { costs[target] = penalties[target]; Push(target, costs[target]); } }
                    else foreach (int goal in goalAnchors[hole]) if (Allowed(goal)) { costs[goal] = penalties[goal]; Push(goal, costs[goal]); }
                    while (heap.Count > 0)
                    {
                        CheckBudget(); var item = heap[0]; heap[0] = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
                        int parent = 0;
                        while (parent * 2 + 1 < heap.Count)
                        {
                            int child = parent * 2 + 1;
                            if (child + 1 < heap.Count && heap[child + 1].cost < heap[child].cost) child++;
                            if (heap[parent].cost <= heap[child].cost) break;
                            var swap = heap[parent]; heap[parent] = heap[child]; heap[child] = swap; parent = child;
                        }
                        if (item.cost != costs[item.p]) continue;
                        foreach (int p in neighbors[item.p])
                        {
                            if (!Allowed(p) || !CanStep(hole, p, item.p) || !CanEnterCat(hole, item.p, Direction(CellAt(p), CellAt(item.p)))) continue;
                            long cost = item.cost + config.distanceWeight + penalties[p];
                            if (cost >= costs[p]) continue;
                            costs[p] = cost; Push(p, cost);
                        }
                    }
                    if (occupancyDistances.Count >= 512) occupancyDistances.Clear();
                    occupancyDistances[key] = costs;
                }
                if (valid[hole][anchor]) return costs[anchor];
                // Leaving an initially invalid footprint can still be legal.
                long resultCost = infinity;
                foreach (int q in neighbors[anchor]) if (valid[hole][q] && CanStep(hole, anchor, q)) resultCost = Math.Min(resultCost, costs[q] + config.distanceWeight);
                return resultCost;
            }

            // The route of a linked group must fit every surviving partner. Scoring
            // members independently can prioritize an impossible relaxed route.
            private long LinkedRouteDistance(int hole, Positions state, BigInteger blockers)
            {
                var members = linkedGroups[hole]; int root = -1;
                foreach (int member in members) if (current.GetValue(member) >= 0) { root = member; break; }
                if (root < 0) return long.MaxValue / 8;
                var key = (valid, root, -2, blockers, config.blockedCellWeight, config.distanceWeight);
                if (!occupancyDistances.TryGetValue(key, out var costs))
                {
                    const long infinity = long.MaxValue / 8;
                    costs = new long[count]; var legal = new bool[count]; var terminal = new bool[count];
                    var penalties = new long[count]; int origin = current.GetValue(root);
                    int Destination(int member, int anchor)
                    {
                        int p = current.GetValue(member);
                        var cell = new Cell(p % board.width + anchor % board.width - origin % board.width,
                            p / board.width + anchor / board.width - origin / board.width);
                        return Inside(cell) ? Index(cell) : -1;
                    }
                    for (int anchor = 0; anchor < count; anchor++)
                    {
                        costs[anchor] = infinity; legal[anchor] = true; BigInteger footprint = BigInteger.Zero;
                        foreach (int member in members)
                        {
                            if (current.GetValue(member) < 0) continue;
                            int p = Destination(member, anchor);
                            if (p < 0 || !valid[member][p] || !(masks[member][p] & routeFixedMask).IsZero)
                            { legal[anchor] = false; break; }
                            footprint |= masks[member][p]; terminal[anchor] |= goals[member][p];
                        }
                        var overlap = footprint & blockers; int blocked = 0;
                        while (!overlap.IsZero) { overlap &= overlap - BigInteger.One; blocked++; }
                        penalties[anchor] = (long)config.blockedCellWeight * blocked;
                    }
                    bool Edge(int from, int to)
                    {
                        int direction = Direction(CellAt(from), CellAt(to));
                        foreach (int member in members)
                        {
                            if (current.GetValue(member) < 0) continue;
                            int a = Destination(member, from), b = Destination(member, to);
                            if (a < 0 || b < 0 || !CanStep(member, a, b) || !CanEnterCat(member, b, direction)) return false;
                        }
                        return true;
                    }
                    // Small board: an array Dijkstra avoids allocating heap nodes.
                    var settled = new bool[count];
                    for (int p = 0; p < count; p++) if (legal[p] && terminal[p]) costs[p] = penalties[p];
                    for (int step = 0; step < count; step++)
                    {
                        token.ThrowIfCancellationRequested(); int next = -1;
                        for (int p = 0; p < count; p++) if (!settled[p] && costs[p] < infinity && (next < 0 || costs[p] < costs[next])) next = p;
                        if (next < 0) break; settled[next] = true;
                        foreach (int p in neighbors[next])
                        {
                            if (!legal[p] || !Edge(p, next)) continue;
                            long value = costs[next] + config.distanceWeight + penalties[p];
                            if (value < costs[p]) costs[p] = value;
                        }
                    }
                    if (occupancyDistances.Count >= 512) occupancyDistances.Clear();
                    occupancyDistances[key] = costs;
                }
                return costs[state.GetValue(root)];
            }

            private long GoalScore(Positions state, int hole, int target)
            {
                int p = state.GetValue(hole); if (p < 0) return long.MaxValue / 8;
                if (!config.useOccupancyPathDistance) return StaticGoalScore(state, hole, target);
                var blockers = Occupancy(state);
                foreach (int member in linkedGroups[hole]) if (state.GetValue(member) >= 0) blockers ^= masks[member][state.GetValue(member)];
                return OccupancyPathDistance(hole, p, blockers, target);
            }

            private long StaticGoalScore(Positions state, int hole, int target)
            {
                int p = state.GetValue(hole);
                if (p < 0) return long.MaxValue / 8;
                var overlap = masks[hole][target] & (Occupancy(state) ^ masks[hole][p]);
                int blocked = 0;
                while (!overlap.IsZero) { overlap &= overlap - BigInteger.One; blocked++; }
                return (long)config.blockedCellWeight * blocked + (long)config.distanceWeight * TargetDistance(hole, target, p);
            }

            private ISearchPolicy SelectFocusedGoalPolicy()
            {
                int selectedHole = -1, selectedTarget = -1; long selectedScore = long.MaxValue;
                for (int hole = 0; hole < capacities.Length; hole++)
                {
                    int p = current.values[hole]; if (p < 0 || !CanDrag(hole, p)) continue;
                    foreach (int target in goalAnchors[hole])
                    {
                        CheckBudget();
                        if (excludedGoalAnchors != null && excludedGoalAnchors.Contains((long)hole * count + target)) continue;
                        if (TargetDistance(hole, target, p) == int.MaxValue) continue;
                        long score = StaticGoalScore(current, hole, target);
                        if (score >= selectedScore) continue;
                        selectedHole = hole; selectedTarget = target; selectedScore = score;
                    }
                }
                return selectedHole < 0 ? (ISearchPolicy)new DefaultPolicy() : new FocusedGoalPolicy(selectedHole, selectedTarget);
            }

            private int TargetDistance(int hole, int target, int anchor)
            {
                long key = (long)hole * count + target;
                if (!targetDistances.TryGetValue(key, out var values))
                {
                    values = new int[count]; for (int p = 0; p < count; p++) values[p] = int.MaxValue;
                    var queue = new Queue<int>(); values[target] = 0; queue.Enqueue(target);
                    while (queue.Count > 0)
                    {
                        CheckBudget(); int p = queue.Dequeue();
                        foreach (int q in neighbors[p]) if (valid[hole][q] && CanStep(hole, q, p) && CanEnterCat(hole, p, Direction(CellAt(q), CellAt(p))) && values[q] == int.MaxValue)
                        { values[q] = values[p] + 1; queue.Enqueue(q); }
                    }
                    targetDistances[key] = values;
                }
                return values[anchor];
            }

            private int Compare(Candidate a, Candidate b)
            {
                int order = a.score.CompareTo(b.score);
                if (order == 0) order = distances[a.hole][a.destination].CompareTo(distances[b.hole][b.destination]);
                if (order == 0) order = board.holes[a.hole].id.CompareTo(board.holes[b.hole].id);
                return order != 0 ? order : a.destination.CompareTo(b.destination);
            }

            private int CompareTerminal(Candidate a, Candidate b)
            {
                if (config.preferNovelConsumption && attemptedConsumptionPhases != null && attemptedConsumptionPhases.Count > 0)
                {
                    ConsumptionUtility(a); ConsumptionUtility(b);
                    int noveltyOrder = attemptedConsumptionPhases.Contains(a.consumptionPhase).CompareTo(attemptedConsumptionPhases.Contains(b.consumptionPhase));
                    if (noveltyOrder != 0) return noveltyOrder;
                }
                if (config.useConsumptionUtility || config.strategy == SearchStrategy.RouteClearing)
                {
                    int utilityOrder = ConsumptionUtility(b).CompareTo(ConsumptionUtility(a));
                    if (utilityOrder != 0) return utilityOrder;
                }
                if (config.prioritizeRestrictedHoles)
                {
                    int restrictedOrder = (board.holes[a.hole].movementType == 0).CompareTo(board.holes[b.hole].movementType == 0);
                    if (restrictedOrder != 0) return restrictedOrder;
                    long optionsA = (long)goalAnchors[a.hole].Length * Math.Max(1, capacities[b.hole]) * board.holes[b.hole].footprint.Length;
                    long optionsB = (long)goalAnchors[b.hole].Length * Math.Max(1, capacities[a.hole]) * board.holes[a.hole].footprint.Length;
                    int optionsOrder = optionsA.CompareTo(optionsB);
                    if (optionsOrder != 0) return optionsOrder;
                }
                if (config.usePolicyForTerminalChoice)
                {
                    int policyOrder = RankState(a.state).CompareTo(RankState(b.state));
                    if (policyOrder != 0) return policyOrder;
                }
                int order = config.preferFinishingHole ? (capacities[a.hole] != 1).CompareTo(capacities[b.hole] != 1) : 0;
                if (order == 0 && config.preferShorterDragOnTie) order = a.pathLength.CompareTo(b.pathLength);
                if (order == 0) order = board.holes[a.hole].id.CompareTo(board.holes[b.hole].id);
                return order != 0 ? order : a.destination.CompareTo(b.destination);
            }

            private bool AcceptTerminal(Candidate move)
            {
                return move.terminal && (excludedTerminals == null || !excludedTerminals.Contains(TerminalKey(move)))
                    && (config.strategy != SearchStrategy.RouteClearing || HasRouteContinuation(move))
                    && (!config.requireFocusedTerminal || !(policy is FocusedGoalPolicy focused) || focused.Accepts(move.state));
            }

            // Inspect a candidate before committing its consumption. Failure of
            // the relaxed next-target test proves an immediate dead end; success
            // remains optimistic. No previously committed event is undone.
            private bool HasRouteContinuation(Candidate move)
            {
                if (move.routeContinuation.HasValue) return move.routeContinuation.Value;
                var savedBoard = board; var savedCurrent = current;
                var savedCapacities = (int[])capacities.Clone(); var savedLayers = (int[])layers.Clone();
                var savedOffsets = (int[])boxOffsets.Clone(); var savedAlive = (bool[])catsAlive.Clone();
                var savedValid = valid; var savedGoals = goals; var savedDistances = distances; var savedAnchors = goalAnchors;
                var savedTargets = targetDistances; var savedEntries = catEntryBlocks; var savedFixed = routeFixedMask;
                try
                {
                    board = savedBoard.Copy(); current = move.state; Consume(move.hole);
                    bool possible = Cleared();
                    if (!possible) { Prepare(); possible = HasRelaxedCatAssignment() && HasRelaxedProgression() && HasRelaxedGroupTarget(); }
                    move.routeContinuation = possible; return possible;
                }
                finally
                {
                    board = savedBoard; current = savedCurrent;
                    Array.Copy(savedCapacities, capacities, capacities.Length); Array.Copy(savedLayers, layers, layers.Length);
                    Array.Copy(savedOffsets, boxOffsets, boxOffsets.Length); Array.Copy(savedAlive, catsAlive, catsAlive.Length);
                    valid = savedValid; goals = savedGoals; distances = savedDistances; goalAnchors = savedAnchors;
                    targetDistances = savedTargets; catEntryBlocks = savedEntries; routeFixedMask = savedFixed;
                }
            }

            private long ConsumptionUtility(Candidate move)
            {
                if (move.consumptionUtility.HasValue) return move.consumptionUtility.Value;
                CheckBudget();
                var savedBoard = board; var savedCurrent = current;
                var savedCapacities = (int[])capacities.Clone(); var savedLayers = (int[])layers.Clone();
                var savedOffsets = (int[])boxOffsets.Clone(); var savedAlive = (bool[])catsAlive.Clone();
                var draggable = new bool[capacities.Length];
                for (int i = 0; i < draggable.Length; i++) draggable[i] = CanDrag(i, move.state.GetValue(i));
                try
                {
                    board = savedBoard.Copy(); current = move.state;
                    int eaten = Consume(move.hole).Count; long utility = eaten;
                    for (int i = 0; i < capacities.Length; i++)
                    {
                        if (move.state.GetValue(i) < 0) continue;
                        if (current.values[i] < 0) utility += board.holes[i].footprint.Length * 16L;
                        else if (!draggable[i] && CanDrag(i, current.values[i])) utility += board.holes[i].footprint.Length * 8L;
                    }
                    for (int b = 0; b < board.boxes.Count; b++)
                        if (savedBoard.boxes[b].requiredHolesToUnlock > 0 && board.boxes[b].requiredHolesToUnlock == 0) utility += 8;
                    move.consumptionPhase = FullStateKey(false);
                    move.consumptionUtility = utility; return utility;
                }
                finally
                {
                    board = savedBoard; current = savedCurrent;
                    Array.Copy(savedCapacities, capacities, capacities.Length); Array.Copy(savedLayers, layers, layers.Length);
                    Array.Copy(savedOffsets, boxOffsets, boxOffsets.Length); Array.Copy(savedAlive, catsAlive, catsAlive.Length);
                }
            }

            private void Search(Positions state, Node node)
            {
                CheckBudget();
                if (node.depth >= Math.Min(bestDepth - 1, searchDepthLimit)) return;
                if (visited.TryGetValue(state, out int previous))
                {
                    if (previous <= node.depth) { stats.duplicates++; return; }
                    stats.reopened++;
                }
                visited[state] = node.depth; stats.expanded++; result.expanded++;
                if (config.strategy == SearchStrategy.RouteClearing && (stats.expanded & 4095) == 0)
                    config.progress?.Invoke(result);
                var moves = Generate(state); stats.generated += moves.Count;
                Candidate terminal = null;
                foreach (var m in moves) if (AcceptTerminal(m) && (terminal == null || CompareTerminal(m, terminal) < 0)) terminal = m;
                if (terminal != null)
                {
                    bestDepth = node.depth + 1; best = new Node { parent = node, move = terminal, depth = bestDepth };
                    if (stats.firstSolutionDepth == 0) stats.firstSolutionDepth = bestDepth;
                    return;
                }
                if (node.depth + 1 >= Math.Min(bestDepth - 1, searchDepthLimit)) return;
                if (config.pruneConsecutiveGroupDrags && node.move != null)
                {
                    // Generate already reached every non-consumption placement of
                    // this rigid group from the parent. A second consecutive drag
                    // cannot change the settled mechanic phase or other groups.
                    int previousHole = node.move.hole;
                    stats.prunedConsecutiveGroupDrags += moves.RemoveAll(m => !m.terminal && m.hole == previousHole);
                }
                foreach (var m in moves) { CheckBudget(); m.score = RankState(m.state); }
                moves.Sort(Compare);
                foreach (var move in moves)
                {
                    // An excluded consumption cannot become a parking step: it would
                    // change mechanics before subsequent search states are generated.
                    if (move.terminal) continue;
                    Search(move.state, new Node { parent = node, move = move, depth = node.depth + 1 });
                    if (bestDepth == 1 || (best != null && (config.nextCatSearch == NextCatSearch.FirstProgress || config.nextCatSearch == NextCatSearch.ProgressiveFirst))) return;
                }
            }

            private string FullStateKey(bool includePositions = true)
            {
                var key = new System.Text.StringBuilder();
                for (int i = 0; i < capacities.Length; i++)
                {
                    var h = board.holes[i];
                    if (includePositions) key.Append(current.values[i]).Append(',');
                    key.Append(capacities[i]).Append(',').Append(layers[i]).Append(',').Append(h.numIced).Append(',').Append(h.hiddenCount).Append(',').Append(h.locked ? 1 : 0).Append(';');
                }
                for (int i = 0; i < catsAlive.Length; i++) key.Append(catsAlive[i] ? 1 : 0).Append(',').Append(board.cats[i].numIced).Append(';');
                for (int i = 0; i < boxOffsets.Length; i++)
                {
                    key.Append(boxOffsets[i]).Append(',').Append(board.boxes[i].requiredHolesToUnlock).Append(':');
                    foreach (var cat in board.boxes[i].cats) key.Append(cat.numIced).Append(',');
                    key.Append(';');
                }
                foreach (var cover in board.covers) key.Append(cover.remainingHits).Append(';');
                return key.ToString();
            }

            // Unlike greedy subproblems, terminal choices stay on the search stack.
            // Only consecutive parking moves count towards maxSubproblemDepth.
            private bool SearchComplete(int parkingDepth, Dictionary<string, int> seen)
            {
                CheckBudget();
                if (Cleared()) return true;
                string key = FullStateKey();
                if (seen.TryGetValue(key, out int previous))
                {
                    if (previous <= parkingDepth) { stats.duplicates++; return false; }
                    stats.reopened++;
                }
                seen[key] = parkingDepth;
                Prepare(); result.expanded++; stats.expanded++;
                stats.depth = Math.Max(stats.depth, parkingDepth + 1);
                var moves = Generate(current);
                stats.generated += moves.Count;
                foreach (var move in moves) move.score = RankState(move.state);
                moves.Sort((a, b) => a.terminal != b.terminal ? (a.terminal ? -1 : 1) : a.terminal ? CompareTerminal(a, b) : Compare(a, b));
                var savedBoard = board.Copy(); var savedCurrent = current;
                var savedCapacities = (int[])capacities.Clone(); var savedLayers = (int[])layers.Clone();
                var savedOffsets = (int[])boxOffsets.Clone(); var savedAlive = (bool[])catsAlive.Clone();
                foreach (var candidate in moves)
                {
                    if (!candidate.terminal && parkingDepth + 1 >= config.maxSubproblemDepth) continue;
                    CheckBudget();
                    var move = new SolverMove { holeId = board.holes[candidate.hole].id, start = CellAt(current.values[candidate.hole]) };
                    foreach (int p in candidate.path) move.path.Add(CellAt(p));
                    current = candidate.state; move.eaten = Consume(candidate.hole); move.expected = Snapshot();
                    result.moves.Add(move);
                    if (SearchComplete(move.eaten.Count > 0 ? 0 : parkingDepth + 1, seen)) return true;
                    result.moves.RemoveAt(result.moves.Count - 1); result.backtracks++;
                    board = savedBoard.Copy(); current = savedCurrent;
                    Array.Copy(savedCapacities, capacities, capacities.Length); Array.Copy(savedLayers, layers, layers.Length);
                    Array.Copy(savedOffsets, boxOffsets, boxOffsets.Length); Array.Copy(savedAlive, catsAlive, catsAlive.Length);
                    // Recursive preparation belongs to the child mechanic state.
                    Prepare();
                }
                return false;
            }
        }
    }
}


using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace CatDom.CoreSolver
{
    /// <summary>Stateless entry point. Each invocation owns all search state.</summary>
    public static partial class CatLevelSolver
    {
        internal static EngineResult SolveCore(BoardInput input, CancellationToken token, Action<EngineResult> progress = null)
        {
            var result = new EngineResult();
            var context = new EngineContext
            {
                progress = progress
            };
            Engine engine = null;
            try
            {
                token.ThrowIfCancellationRequested();
                Validate(input, context);
                if (input.ignoredMechanics.Count > 0)
                {
                    result.status = SolveStatus.UnsupportedMechanics;
                    result.message = "Unsupported mechanics: " + string.Join(", ", input.ignoredMechanics);
                    return result;
                }

                var original = input.Copy();
                engine = new Engine(input, context, token, result);
                engine.Run();
                if (result.status == SolveStatus.Solved)
                {
                    var replayResult = new EngineResult();
                    var replay = new Engine(original, context, token, replayResult);
                    replay.Replay(result.moves);
                    if (replayResult.status != SolveStatus.Solved)
                        throw new ArgumentException("Generated plan failed mechanic replay.");
                }
            }
            catch (OperationCanceledException)
            {
                result.status = SolveStatus.Cancelled;
                result.message = "Search cancelled.";
            }
            catch (ArgumentException ex)
            {
                result.status = SolveStatus.InvalidInput;
                result.message = ex.Message;
            }
            finally
            {
                if (engine != null)
                    result.finalState = engine.Snapshot();
            }

            return result;
        }

        internal static EngineResult VerifyPlan(BoardInput input, List<EngineMove> moves, bool auditRelaxedAssignment = false)
        {
            var result = new EngineResult();
            var context = new EngineContext();
            Validate(input, context);
            var engine = new Engine(input.Copy(), context, CancellationToken.None, result);
            engine.Replay(moves, auditRelaxedAssignment);
            result.finalState = engine.Snapshot();
            return result;
        }

        private static void Validate(BoardInput b, EngineContext c)
        {
            if (b == null)
                throw new ArgumentException("Input/context is null.");
            if (b.width <= 0 || b.height <= 0 || (long)b.width * b.height > int.MaxValue)
                throw new ArgumentException("Invalid board dimensions.");
            if (b.holes == null || b.cats == null || b.boxes == null || b.obstacles == null || b.links == null || b.covers == null || b.colorPaths == null || b.ignoredMechanics == null)
                throw new ArgumentException("Input collections cannot be null.");
            bool Inside(Cell p) => p.x >= 0 && p.y >= 0 && p.x < b.width && p.y < b.height;
            var occupied = new HashSet<Cell>();
            foreach (var p in b.obstacles)
            {
                if (!Inside(p))
                    throw new ArgumentException("Obstacle outside board.");
                occupied.Add(p);
            }

            var ids = new HashSet<int>();
            var holeCells = new Dictionary<Cell, int>();
            foreach (var h in b.holes)
            {
                if (h == null || !ids.Add(h.id) || h.remaining <= 0 || h.movementType < 0 || h.movementType > 2 || h.footprint == null || h.footprint.Length == 0 || !Inside(h.position))
                    throw new ArgumentException("Invalid hole definition or duplicate ID.");
                if (h.layerColors == null || h.layerCounts == null || h.gates == null || h.layerColors.Length != h.layerCounts.Length || h.layerColors.Length > 2 || h.numIced < 0 || h.hiddenCount < 0)
                    throw new ArgumentException("Invalid hole mechanics.");
                for (int i = 0; i < h.layerCounts.Length; i++)
                    if (h.layerCounts[i] <= 0)
                        throw new ArgumentException("Layer capacities must be positive.");
                foreach (var gate in h.gates)
                    if (gate.directions < 0 || gate.directions > 15 || Array.IndexOf(h.footprint, gate.local) < 0)
                        throw new ArgumentException("Invalid gate footprint/directions.");
                foreach (var d in h.footprint)
                {
                    var p = new Cell(h.position.x + d.x, h.position.y + d.y);
                    if (!Inside(p) || !occupied.Add(p))
                        throw new ArgumentException("Hole footprint overlaps another hole/obstacle or board boundary.");
                    holeCells[p] = h.color;
                }
            }

            ids.Clear();
            var catCells = new HashSet<Cell>();
            foreach (var cat in b.cats)
            {
                if (cat == null || !ids.Add(cat.id) || cat.numIced < 0 || !Inside(cat.position) || !catCells.Add(cat.position))
                    throw new ArgumentException("Invalid cat or duplicate cat position/ID.");
                if (occupied.Contains(cat.position) && (!holeCells.TryGetValue(cat.position, out var color) || color != cat.color))
                    throw new ArgumentException("Cat overlaps an obstacle or wrong-color hole.");
            }

            ids.Clear();
            foreach (var box in b.boxes)
            {
                if (box == null || !ids.Add(box.id) || !Inside(box.mouth) || box.colors == null || box.cats == null || box.requiredHolesToUnlock < 0 || (!box.tower && (box.direction < 0 || box.direction > 3)))
                    throw new ArgumentException("Invalid box mouth/queue/ID.");
                if (box.cats.Length != 0 && box.cats.Length != box.colors.Length)
                    throw new ArgumentException("Box metadata length differs from queue.");
            }

            var holeIds = new HashSet<int>();
            foreach (var h in b.holes)
                holeIds.Add(h.id);
            foreach (var link in b.links)
                if (link.holeId1 == link.holeId2 || !holeIds.Contains(link.holeId1) || !holeIds.Contains(link.holeId2))
                    throw new ArgumentException("Invalid linked hole IDs.");
            ids.Clear();
            foreach (var cover in b.covers)
            {
                if (cover == null || !ids.Add(cover.id) || cover.remainingHits < 0 || cover.cells == null)
                    throw new ArgumentException("Invalid cover.");
                foreach (var cell in cover.cells)
                    if (!Inside(cell))
                        throw new ArgumentException("Cover outside board.");
            }

            foreach (var path in b.colorPaths)
                if (!Inside(path.position))
                    throw new ArgumentException("Color path outside board.");
        }

        private sealed class Positions : IEquatable<Positions>
        {
            private int[] materialized;
            private readonly int length;
            private readonly bool packed;
            private readonly ulong word0, word1;
            private static readonly int[] hashPowers = MakeHashPowers();
            private readonly int hash;
            internal CellMask occupied;
            internal bool hasOccupancy;
            private static int[] MakeHashPowers()
            {
                var powers = new int[16];
                powers[0] = 1;
                unchecked
                {
                    for (int i = 1; i < powers.Length; i++)
                        powers[i] = powers[i - 1] * 31;
                }

                return powers;
            }

            internal int GetValue(int index)
            {
                if (materialized != null)
                    return materialized[index];
                return (int)(((index < 8 ? word0 : word1) >> ((index & 7) * 8)) & 255UL) - 1;
            }

            internal int[] values
            {
                get
                {
                    if (materialized == null)
                    {
                        var array = new int[length];
                        for (int i = 0; i < length; i++)
                            array[i] = GetValue(i);
                        materialized = array;
                    }

                    return materialized;
                }
            }

            internal Positions(int[] initial)
            {
                length = initial.Length;
                materialized = initial;
                packed = length <= 16;
                foreach (int p in initial)
                    if (p < -1 || p >= 255)
                        packed = false;
                unchecked
                {
                    hash = 17;
                    foreach (int p in initial)
                        hash = hash * 31 + p;
                }

                if (packed)
                    for (int i = 0; i < length; i++)
                    {
                        ulong value = (ulong)(initial[i] + 1) << ((i & 7) * 8);
                        if (i < 8)
                            word0 |= value;
                        else
                            word1 |= value;
                    }
            }

            private Positions(Positions source, int[] members, int translation)
            {
                length = source.length;
                packed = source.packed;
                foreach (int member in members)
                {
                    int old = source.GetValue(member);
                    if (old >= 0 && (old + translation < 0 || old + translation >= 255))
                        packed = false;
                }

                if (!packed)
                {
                    materialized = (int[])source.values.Clone();
                    foreach (int member in members)
                        if (materialized[member] >= 0)
                            materialized[member] += translation;
                    unchecked
                    {
                        hash = 17;
                        foreach (int p in materialized)
                            hash = hash * 31 + p;
                    }

                    return;
                }

                ulong first = source.word0, second = source.word1;
                hash = source.hash;
                foreach (int member in members)
                {
                    int old = source.GetValue(member);
                    if (old < 0)
                        continue;
                    int shift = (member & 7) * 8;
                    ulong mask = 255UL << shift, value = (ulong)(old + translation + 1) << shift;
                    if (member < 8)
                        first = (first & ~mask) | value;
                    else
                        second = (second & ~mask) | value;
                    unchecked
                    {
                        hash += translation * hashPowers[length - member - 1];
                    }
                }

                word0 = first;
                word1 = second;
            }

            internal Positions Moved(int[] members, int translation) => new Positions(this, members, translation);
            public bool Equals(Positions other)
            {
                if (other == null || hash != other.hash || length != other.length)
                    return false;
                if (packed && other.packed)
                    return word0 == other.word0 && word1 == other.word1;
                for (int i = 0; i < length; i++)
                    if (GetValue(i) != other.GetValue(i))
                        return false;
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
                        {
                            materializedPath[index] = at;
                            at = pathParents[at];
                        }
                    }

                    return materializedPath;
                }
            }

            internal bool terminal;
            internal long score;
            internal long? consumptionUtility;
            internal long? futureRank;
            internal bool? routeContinuation;
            internal string consumptionPhase;
        }

        private sealed class Node
        {
            internal Node parent;
            internal Candidate move;
            internal int depth;
        }

        private sealed partial class Engine
        {
            private CellMask routeFixedMask;
            private readonly Dictionary<string, bool> relaxedProgressionCache = new Dictionary<string, bool>();
            private readonly Dictionary<(bool[][] phase, Positions state, RouteAggregation aggregation, bool keys), long> cheapScoreCache
                = new Dictionary<(bool[][], Positions, RouteAggregation, bool), long>();
            private readonly Dictionary<(bool[][] phase, int hole, int target, Positions state), long> routeClearanceScores = new Dictionary<(bool[][], int, int, Positions), long>();
            private BoardInput board;
            private static readonly RoutePolicy SharedPolicy = new RoutePolicy();
            private readonly RoutePolicy policy = SharedPolicy;
            private EngineContext context;
            private readonly CancellationToken token;
            private readonly EngineResult result;
            private readonly int count;
            private readonly CellMask[][] masks;
            private readonly int[][] neighbors;
            private readonly int[] capacities, boxOffsets;
            private readonly int[] layers;
            private readonly int[][] linkedGroups;
            private readonly bool[] catsAlive;
            private readonly CellMask[] catMasks, boxMasks;
            private Positions current;
            private bool[][] valid, goals;
            private int[][] goalAnchors, distances;
            private Dictionary<long, int[]> targetDistances;
            private int[][] catEntryBlocks;
            private readonly Dictionary<(bool[][] phase, int hole, int target, CellMask blockers, int blockedWeight, int distanceWeight), long[]> occupancyDistances = new Dictionary<(bool[][], int, int, CellMask, int, int), long[]>();
            private Dictionary<Positions, int> visited;
            private SubproblemStats stats;
            private int bestDepth;
            private Node best;
            private int searchDepthLimit;
            private readonly Dictionary<string, bool> relaxedGroupTargets = new Dictionary<string, bool>();
            // Policies only order search. All transitions and replay use this Engine.

            internal Engine(BoardInput board, EngineContext context, CancellationToken token, EngineResult result)
            {
                this.board = board;
                this.context = context;
                this.token = token;
                this.result = result;
                
                count = board.width * board.height;
                InitializeScratch();
                result.ignoredMechanics.AddRange(board.ignoredMechanics);
                capacities = new int[board.holes.Count];
                boxOffsets = new int[board.boxes.Count];
                layers = new int[board.holes.Count];
                // LinkWith builds a transitive group; a finished member is excluded from
                // movement without disconnecting the surviving members of that group.
                var groupIds = new int[board.holes.Count];
                for (int i = 0; i < groupIds.Length; i++)
                    groupIds[i] = i;
                foreach (var link in board.links)
                {
                    int a = board.holes.FindIndex(h => h.id == link.holeId1), b = board.holes.FindIndex(h => h.id == link.holeId2);
                    int old = groupIds[b], replacement = groupIds[a];
                    for (int i = 0; i < groupIds.Length; i++)
                        if (groupIds[i] == old)
                            groupIds[i] = replacement;
                }

                linkedGroups = new int[groupIds.Length][];
                for (int i = 0; i < groupIds.Length; i++)
                {
                    var members = new List<int>();
                    for (int j = 0; j < groupIds.Length; j++)
                        if (groupIds[j] == groupIds[i])
                            members.Add(j);
                    linkedGroups[i] = members.ToArray();
                }

                catsAlive = new bool[board.cats.Count];
                catMasks = new CellMask[catsAlive.Length];
                boxMasks = new CellMask[boxOffsets.Length];
                for (int i = 0; i < catsAlive.Length; i++)
                {
                    catsAlive[i] = true;
                    catMasks[i] = Bit(board.cats[i].position);
                }

                for (int i = 0; i < boxOffsets.Length; i++)
                {
                    var box = board.boxes[i];
                    if (!box.tower)
                        boxMasks[i] = Bit(box.mouth);
                    else
                        foreach (var delta in new[]
                        {
                            new Cell(0, 1),
                            new Cell(1, 0),
                            new Cell(0, -1),
                            new Cell(-1, 0)
                        }

                        )
                        {
                            var p = new Cell(box.position.x + delta.x, box.position.y + delta.y);
                            if (Inside(p))
                                boxMasks[i] |= Bit(p);
                        }
                }

                CellMask obstacles = CellMask.Zero;
                foreach (var p in board.obstacles)
                    obstacles |= Bit(p);
                masks = new CellMask[board.holes.Count][];
                var positions = new int[board.holes.Count];
                for (int i = 0; i < board.holes.Count; i++)
                {
                    CheckBudget();
                    var h = board.holes[i];
                    capacities[i] = h.remaining;
                    positions[i] = Index(h.position);
                    masks[i] = new CellMask[count];
                    for (int p = 0; p < count; p++)
                    {
                        CellMask mask = CellMask.Zero;
                        foreach (var d in h.footprint)
                        {
                            int x = CellAt(p).x + d.x, y = CellAt(p).y + d.y;
                            if (x < 0 || y < 0 || x >= board.width || y >= board.height)
                            {
                                mask = CellMask.Zero;
                                break;
                            }

                            mask |= CellMask.One << (x + board.width * y);
                        }

                        if ((mask & obstacles).IsZero)
                            masks[i][p] = mask;
                    }
                }

                neighbors = new int[count][];
                for (int p = 0; p < count; p++)
                {
                    var list = new List<int>(4);
                    if (CellAt(p).y + 1 < board.height)
                        list.Add(p + board.width);
                    if (CellAt(p).y > 0)
                        list.Add(p - board.width);
                    if (CellAt(p).x > 0)
                        list.Add(p - 1);
                    if (CellAt(p).x + 1 < board.width)
                        list.Add(p + 1);
                    neighbors[p] = list.ToArray();
                }

                current = new Positions(positions);
            }

            private int Index(Cell p) => p.x + board.width * p.y;
            private bool Inside(Cell p) => p.x >= 0 && p.y >= 0 && p.x < board.width && p.y < board.height;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
            private Cell CellAt(int p) => (uint)p < (uint)count ? cellCoordinates[p] : new Cell(p % board.width, p / board.width);
            private CellMask Bit(Cell p) => CellMask.One << Index(p);
            private int Color(int hole) => board.holes[hole].layerColors.Length == 0 ? board.holes[hole].color : board.holes[hole].layerColors[layers[hole]];
            private CellMask CoverMask()
            {
                CellMask mask = CellMask.Zero;
                foreach (var cover in board.covers)
                    if (cover.remainingHits > 0)
                        foreach (var cell in cover.cells)
                            mask |= Bit(cell);
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
                if (layers[hole] + board.holes[hole].layerOffset != 0)
                    return 0; // First-layer gates fade on layer transition.
                foreach (var gate in board.holes[hole].gates)
                    if (gate.local.Equals(local))
                        return gate.directions;
                return 0;
            }

            private int Direction(Cell from, Cell to) => to.y > from.y ? 0 : to.x > from.x ? 1 : to.y < from.y ? 2 : 3;
            private bool CanEnterCat(int hole, int anchor, int direction)
            {
                if (board.holes[hole].gates.Length == 0 || layers[hole] + board.holes[hole].layerOffset != 0)
                    return true;
                // Gate entry depends on the settled mechanic phase and anchor,
                // not on other hole positions or the chosen search policy.
                var entries = catEntryBlocks[hole];
                if (entries == null)
                {
                    entries = new int[count];
                    for (int p = 0; p < count; p++)
                        entries[p] = -1;
                    catEntryBlocks[hole] = entries;
                }

                if (entries[anchor] >= 0)
                    return (entries[anchor] & (1 << direction)) == 0;
                var origin = CellAt(anchor);
                int blockedDirections = 0;
                for (int index = 0; index < board.cats.Count; index++)
                {
                    var cat = board.cats[index];
                    if (!catsAlive[index] || (masks[hole][anchor] & catMasks[index]).IsZero)
                        continue;
                    var local = new Cell(cat.position.x - origin.x, cat.position.y - origin.y);
                    blockedDirections |= GateBits(hole, local);
                }

                entries[anchor] = blockedDirections;
                return (blockedDirections & (1 << direction)) == 0;
            }

            private bool BoxTouches(int hole, int anchor, int boxIndex)
            {
                var box = board.boxes[boxIndex];
                if (boxOffsets[boxIndex] >= box.colors.Length || box.requiredHolesToUnlock > 0 || box.colors[boxOffsets[boxIndex]] != Color(hole))
                    return false;
                if ((masks[hole][anchor] & boxMasks[boxIndex]).IsZero)
                    return false;
                var origin = CellAt(anchor);
                foreach (var local in board.holes[hole].footprint)
                {
                    if (BoxTouchesCell(hole, origin, local, box))
                        return true;
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
                return type == 0 || (type == 1 ? CellAt(from).x == CellAt(to).x : CellAt(from).y == CellAt(to).y);
            }

            private void CheckBudget()
            {
                token.ThrowIfCancellationRequested();
                if (decisionPhaseLimit > 0 && (result.expanded - decisionPhaseStart >= decisionPhaseLimit || result.expanded >= decisionTotalEnd))
                    throw new DecisionBudgetExceeded();
            }
            private void TraceStage(string stage)
            {
                result.searchStage = stage;
                context.progress?.Invoke(result);
            }
            internal void Run()
            {
                result.initialEaten = Consume();
                while (!Cleared())
                {
                    CheckBudget();
                    stats = new SubproblemStats();
                    Prepare();
                    best = null;
                    bestDepth = 13;
                    context.progress?.Invoke(result);
                    if (!HasMovableTarget())
                    {
                        result.status = SolveStatus.NoNextCatReachable;
                        result.message = "No movable component has a consumption target.";
                        return;
                    }

                    SearchRouteClearing();
                    if (best == null)
                    {
                        result.status = SolveStatus.NoNextCatReachable;
                        result.message = "All reachable parking states exhausted without a consumption event.";
                        return;
                    }

                    var chain = new List<Candidate>();
                    for (Node n = best; n.parent != null; n = n.parent)
                        chain.Add(n.move);
                    chain.Reverse();
                    foreach (var candidate in chain)
                    {
                        var move = new EngineMove
                        {
                            holeId = board.holes[candidate.hole].id,
                            start = CellAt(current.GetValue(candidate.hole))
                        };
                        foreach (int p in candidate.path)
                            move.path.Add(CellAt(p));
                        current = candidate.state;
                        move.eaten = Consume(candidate.hole);
                        result.moves.Add(move);
                    }
                }

                result.status = SolveStatus.Solved;
                result.message = "All playable holes finished and cats cleared.";
            }

            // Iterative deepening has no configured maximum. Route pressure orders
            // legal parking moves, but never removes them. Consumption remains on
            // the committed side of Run, outside this parking search.
            private sealed class RoutePolicy
            {
                public long Rank(Engine engine, Positions state)
                {
                    var key = (engine.valid, state, engine.prioritizeRouteKeys);
                    if (engine.rankCache.TryGetValue(key, out long cached))
                        return cached;
                    var aggregation = engine.context.aggregation;
                    try
                    {
                        engine.context.aggregation = RouteAggregation.Minimum;
                        long minimum = engine.Score(state);
                        engine.context.aggregation = RouteAggregation.ReachableSum;
                        long pressure = engine.Score(state);
                        long rank = minimum >= long.MaxValue / 1024 ? long.MaxValue : minimum * 1024 + Math.Min(pressure, 1023);
                        if (engine.rankCache.Count >= 4096)
                            engine.rankCache.Clear();
                        engine.rankCache[key] = rank;
                        return rank;
                    }
                    finally
                    {
                        engine.context.aggregation = aggregation;
                    }
                }
            }

            private bool prioritizeRouteKeys;
            private void SearchRouteClearing()
            {
                // Test direct consumption without ranking every parking endpoint.
                searchDepthLimit = 1;
                bestDepth = int.MaxValue;
                visited = new Dictionary<Positions, int>();
                TraceStage("direct");
                Search(current, new Node());
                if (best != null)
                    return;
                int previousCount = visited.Count;
                int activeHoles = 0;
                foreach (int p in current.values)
                    if (p >= 0)
                        activeHoles++;
                if (activeHoles <= 4)
                {
                    if (SearchPairPatterns(256) || SearchPairPatterns(4096, includeTriples: true))
                        return;
                    SearchSmallRouteFrontier();
                    return;
                }
                // Global route pressure cheaply reaches short or long chains.
                // Pattern ranking follows when a narrow beam cannot clear them.
                // Keep the policy selected for a large input arrangement through
                // its phases: switching as bodies disappear changes event order.
                if (PreferTargetCorridors() && SearchTargetBeams() ||
                    SearchRouteBeam(true, 4, 128, cheapRank: true) ||
                    SearchRouteBeam(true, 16, 512, cheapRank: true) ||
                    SearchPairPatterns(256) || SearchPairPatterns(2048, includeTriples: true))
                    return;
                for (searchDepthLimit = 2;; searchDepthLimit++)
                {
                    token.ThrowIfCancellationRequested();
                    best = null;
                    bestDepth = int.MaxValue;
                    visited = new Dictionary<Positions, int>();
                    TraceStage("depth-" + searchDepthLimit);
                    Search(current, new Node());
                    stats.uniqueBoards += visited.Count;
                    context.progress?.Invoke(result);
                    if (best != null)
                        return;
                    if (searchDepthLimit == 2 && SearchRouteDependencies())
                        return;
                    // Every state within this parking radius was visited. With no
                    // new states in the next radius, the reachable graph is closed.
                    if (visited.Count == previousCount)
                        return;
                    previousCount = visited.Count;
                    // Finish cheap short-chain passes before the complete explicit
                    // stack search. This is a traversal switch, never a depth cap.
                    if (searchDepthLimit >= 3)
                    {
                        visited.Clear();
                        // A dependency corridor can miss a useful temporary
                        // relocation. Try a broader target-fixed ordering before
                        // the exhaustive arrangement traversal.
                        // Give DFS a turn before a failed wide beam spends the
                        // whole arrangement budget. Complete fallback remains.
                        if (SearchRouteBeam(false, 128, PreferTargetCorridors() ? 4096 : 16384))
                            return;
                        SearchRouteDepthFirst(12000);
                        if (best != null)
                            return;
                        bool hasLock = false;
                        for (int i = 0; i < capacities.Length; i++)
                            if (current.GetValue(i) >= 0 && board.holes[i].locked)
                            {
                                hasLock = true;
                                break;
                            }

                        if (hasLock)
                        {
                            prioritizeRouteKeys = true;
                            try
                            {
                                if (SearchRouteBeam(false))
                                    return;
                            }
                            finally
                            {
                                prioritizeRouteKeys = false;
                            }
                        }

                        if (best != null || SearchRouteBeam(true))
                            return;
                        if (SearchRouteDependencies(true))
                            return;
                        SearchRouteDepthFirst();
                        return;
                    }
                }
            }

            private void SearchSmallRouteFrontier()
            {
                TraceStage("small-frontier");
                int serial = 0;
                var open = new StableMinHeap<Node>();
                var seen = new HashSet<Positions>
                {
                    current
                };
                open.Add((0, serial++, new Node()));
                while (open.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var entry = open.Pop();
                    var node = entry.node;
                    var state = node.move == null ? current : node.move.state;
                    stats.expanded++;
                    result.expanded++;
                    if ((stats.expanded & 4095) == 0)
                        context.progress?.Invoke(result);
                    var moves = Generate(state);
                    stats.generated += moves.Count;
                    Candidate terminal = null;
                    foreach (var move in moves)
                        if (AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0))
                            terminal = move;
                    if (terminal != null)
                    {
                        best = CompactParkingChain(new Node { parent = node, move = terminal, depth = node.depth + 1 });
                        bestDepth = best.depth;
                        stats.firstSolutionDepth = bestDepth;
                        stats.uniqueBoards += seen.Count;
                        return;
                    }

                    foreach (var move in moves)
                    {
                        if (move.terminal || !seen.Add(move.state))
                            continue;
                        long score = policy.Rank(this, move.state);
                        open.Add((score, serial++, new Node { parent = node, move = move, depth = node.depth + 1 }));
                    }
                }

                stats.uniqueBoards += seen.Count;
            }

            // A layered accelerator reaches longer clearance chains without a
            // best-first plateau consuming all work at shallow arrangements.
            // Keep alternatives for different moved groups; failure is not a proof.
            private bool SearchRouteBeam(bool aggregateRoutes, int width = 128, int workLimit = 16384, int targetHole = -1, int targetDestination = -1, bool cheapRank = false)
            {
                TraceStage("beam-" + (cheapRank ? "cheap-" : "") + (targetHole >= 0 ? "target-" + targetHole + "-" + targetDestination : aggregateRoutes ? "sum" : "min") + "-" + width);
                var frontier = new List<Node>
                {
                    new Node()
                };
                var seen = new HashSet<Positions>();
                int work = 0;
                while (frontier.Count > 0 && work < workLimit)
                {
                    var pending = new Dictionary<Positions, Node>();
                    Candidate layerTerminal = null;
                    Node layerParent = null;
                    foreach (var node in frontier)
                    {
                        token.ThrowIfCancellationRequested();
                        var state = node.move == null ? current : node.move.state;
                        if (!seen.Add(state))
                            continue;
                        stats.expanded++;
                        result.expanded++;
                        work++;
                        if ((work & 255) == 0)
                            context.progress?.Invoke(result);
                        var moves = Generate(state);
                        stats.generated += moves.Count;
                        Candidate terminal = null;
                        foreach (var move in moves)
                            if ((!prioritizeRouteKeys || targetHole < 0 || move.hole == targetHole && move.destination == targetDestination)
                                && AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0))
                                terminal = move;
                        if (terminal != null)
                        {
                            if (layerTerminal == null || CompareTerminal(terminal, layerTerminal) < 0)
                            {
                                layerTerminal = terminal;
                                layerParent = node;
                            }
                            if (work >= workLimit) break;
                            continue;
                        }

                        Dictionary<int, CellMask> requestedClearance = null;
                        HashSet<int> relevant = targetHole >= 0 && cheapRank
                            ? RouteDependencyGroups(targetHole, state, targetDestination, out requestedClearance) : null;
                        foreach (var move in moves)
                        {
                            if (move.terminal || seen.Contains(move.state) || pending.ContainsKey(move.state))
                                continue;
                            if (relevant != null && !relevant.Contains(move.hole))
                                continue;
                            // Generate already includes every reachable endpoint in
                            // one drag. A second consecutive drag of the same group
                            // has an equivalent successor at the preceding layer.
                            if (cheapRank && node.move != null && move.hole == node.move.hole)
                                continue;
                            var aggregation = context.aggregation;
                            try
                            {
                                // Clearing a dense board can require moving a hole
                                // away from the currently closest cat. Rank this
                                // accelerator by all reachable consumption routes.
                                if (targetHole >= 0)
                                {
                                    int position = move.state.GetValue(targetHole);
                                    var blockers = Occupancy(move.state) ^ masks[targetHole][position];
                                    move.score = OccupancyPathDistance(targetHole, position, blockers, targetDestination)
                                        + (cheapRank ? 0 : RouteClearancePenalty(targetHole, move.state, blockers, targetDestination));
                                    if (requestedClearance != null)
                                        foreach (var request in requestedClearance)
                                        {
                                            int parked = move.state.GetValue(request.Key);
                                            if (parked >= 0 && !(masks[request.Key][parked] & request.Value).IsZero)
                                                move.score += 8;
                                        }
                                }
                                else if (aggregateRoutes)
                                {
                                    context.aggregation = RouteAggregation.ReachableSum;
                                    move.score = Score(move.state, !cheapRank);
                                }
                                else if (cheapRank)
                                {
                                    context.aggregation = RouteAggregation.Minimum;
                                    move.score = Score(move.state, false);
                                }
                                else
                                    move.score = policy.Rank(this, move.state);
                            }
                            finally
                            {
                                context.aggregation = aggregation;
                            }

                            pending.Add(move.state, new Node { parent = node, move = move, depth = node.depth + 1 });
                        }

                        if (work >= workLimit)
                            break;
                    }


                    if (layerTerminal != null)
                    {
                        best = CompactParkingChain(new Node { parent = layerParent, move = layerTerminal, depth = layerParent.depth + 1 });
                        bestDepth = best.depth;
                        stats.firstSolutionDepth = bestDepth;
                        return true;
                    }
                    var ordered = new List<Node>(pending.Values);
                    ordered.Sort((a, b) => Compare(a.move, b.move));
                    frontier = new List<Node>();
                    var selected = new HashSet<Positions>();
                    var groups = new HashSet<int>();
                    foreach (var node in ordered)
                        groups.Add(node.move.hole);
                    int quota = Math.Max(1, width / Math.Max(1, groups.Count));
                    var counts = new Dictionary<int, int>();
                    foreach (var node in ordered)
                    {
                        if (frontier.Count >= width)
                            break;
                        counts.TryGetValue(node.move.hole, out int used);
                        if (used >= quota)
                            continue;
                        counts[node.move.hole] = used + 1;
                        frontier.Add(node);
                        selected.Add(node.move.state);
                    }

                    foreach (var node in ordered)
                    {
                        if (frontier.Count >= width)
                            break;
                        if (selected.Add(node.move.state))
                            frontier.Add(node);
                    }
                }

                stats.uniqueBoards += seen.Count;
                return false;
            }

            private bool SearchKeyBeams()
            {
                var targets = new List<(int hole, int destination, long score)>();
                for (int hole = 0; hole < capacities.Length; hole++)
                {
                    int position = current.GetValue(hole);
                    if (position < 0 || linkedGroups[hole].Length != 1 || !CanDrag(hole, position))
                        continue;
                    var blockers = Occupancy(current) ^ masks[hole][position];
                    foreach (int destination in goalAnchors[hole])
                    {
                        bool unlocks = false;
                        for (int cat = 0; cat < catsAlive.Length && !unlocks; cat++)
                        {
                            var key = board.cats[cat];
                            if (!catsAlive[cat] || key.keyColorId < 0 || key.color != Color(hole)
                                || (masks[hole][destination] & catMasks[cat]).IsZero)
                                continue;
                            for (int locked = 0; locked < capacities.Length; locked++)
                                if (current.GetValue(locked) >= 0 && board.holes[locked].locked
                                    && board.holes[locked].lockColorId == key.keyColorId)
                                { unlocks = true; break; }
                        }
                        if (!unlocks) continue;
                        long score = OccupancyPathDistance(hole, position, blockers, destination);
                        if (score < long.MaxValue / 8) targets.Add((hole, destination, score));
                    }
                }
                targets.Sort((a,b) => a.score != b.score ? a.score.CompareTo(b.score)
                    : a.hole != b.hole ? a.hole.CompareTo(b.hole) : a.destination.CompareTo(b.destination));
                int attempts = 0;
                var tried = new HashSet<int>();
                foreach (var target in targets)
                    if (tried.Add(target.hole))
                    {
                        if (SearchRouteBeam(false, 4, 64, target.hole, target.destination, cheapRank: true)) return true;
                        if (++attempts >= 3) break;
                    }
                return false;
            }

            // A fixed target supplies a stable objective across layers. These
            // bounded passes only change traversal order; every successor is exact.
            private bool SearchTargetBeams()
            {
                var targets = new List<(int hole, int destination, long score)>();
                for (int hole = 0; hole < capacities.Length; hole++)
                {
                    int position = current.GetValue(hole);
                    if (position < 0 || linkedGroups[hole].Length != 1 || !CanDrag(hole, position))
                        continue;
                    var blockers = Occupancy(current) ^ masks[hole][position];
                    foreach (int destination in goalAnchors[hole])
                    {
                        long score = OccupancyPathDistance(hole, position, blockers, destination);
                        if (score >= long.MaxValue / 8)
                            continue;
                        score += RouteClearancePenalty(hole, current, blockers, destination);
                        targets.Add((hole, destination, score));
                    }
                }
                targets.Sort((a,b) => a.score != b.score ? a.score.CompareTo(b.score) : a.hole != b.hole ? a.hole.CompareTo(b.hole) : a.destination.CompareTo(b.destination));
                // Spread the first trials across holes before trying additional
                // destinations of the same hole.
                var tried = new HashSet<int>();
                int attempts = 0;
                int targetWork = Math.Min(64, Math.Max(16, board.holes.Count * 6));
                foreach (var target in targets)
                    if (tried.Add(target.hole))
                    {
                        if (SearchRouteBeam(false, 4, targetWork, target.hole, target.destination, cheapRank: true))
                            return true;
                        if (++attempts >= 3)
                            break;
                    }
                return false;
            }

            private bool PreferTargetCorridors()
            {
                if (board.holes.Count >= 13)
                    return true;
                // Several towers expose different next colors after consumption;
                // keep a corridor objective instead of summing all exposed routes.
                int towers = 0;
                foreach (var box in board.boxes)
                    if (box.tower && ++towers >= 2)
                        return true;
                return false;
            }

            private bool HasLockedHole()
            {
                for (int hole = 0; hole < capacities.Length; hole++)
                    if (current.GetValue(hole) >= 0 && board.holes[hole].locked)
                        return true;
                return false;
            }

            private bool SearchLockedRouteBeam()
            {
                if (!HasLockedHole()) return false;
                bool previous = prioritizeRouteKeys;
                try
                {
                    prioritizeRouteKeys = true;
                    return SearchRouteBeam(false, 128, 2048);
                }
                finally { prioritizeRouteKeys = previous; }
            }

            // A bounded focused traversal is an ordering accelerator, not a
            // solvability test. Failure always returns to the complete search.
            private bool SearchRouteDependencies(bool distanceOnly = false)
            {
                TraceStage(distanceOnly ? "dependencies-distance" : "dependencies-clearance");
                var targets = new List<(int hole, int destination, long score)>();
                for (int i = 0; i < capacities.Length; i++)
                {
                    int p = current.GetValue(i);
                    if (p < 0 || linkedGroups[i].Length != 1 || !CanDrag(i, p) || goalAnchors[i].Length == 0)
                        continue;
                    var blockers = Occupancy(current) ^ masks[i][p];
                    foreach (int destination in goalAnchors[i])
                    {
                        long score = OccupancyPathDistance(i, p, blockers, destination) + (distanceOnly ? 0 : RouteClearancePenalty(i, current, blockers, destination));
                        if (score < long.MaxValue / 8)
                            targets.Add((i, destination, score));
                    }
                }

                targets.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.hole != b.hole ? a.hole.CompareTo(b.hole) : a.destination.CompareTo(b.destination));
                int focusedWork = 0;
                foreach (var target in targets)
                {
                    if (focusedWork >= (distanceOnly ? 8192 : 32768))
                        break;
                    int serial = 0;
                    var open = new SortedSet<(long score, int serial, Node node)>(Comparer<(long score, int serial, Node node)>.Create((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.serial.CompareTo(b.serial)));
                    var seen = new HashSet<Positions>();
                    var queuedDepth = new Dictionary<Positions, int>
                    {
                        [current] = 0
                    };
                    open.Add((target.score * 16, serial++, new Node()));
                    for (int expansion = 0; expansion < (distanceOnly ? 4096 : 1024) && open.Count > 0 && focusedWork < (distanceOnly ? 8192 : 32768); expansion++, focusedWork++)
                    {
                        token.ThrowIfCancellationRequested();
                        var entry = open.Min;
                        open.Remove(entry);
                        var node = entry.node;
                        var state = node.move == null ? current : node.move.state;
                        if (!seen.Add(state))
                            continue;
                        stats.expanded++;
                        result.expanded++;
                        if ((expansion & 255) == 0)
                            context.progress?.Invoke(result);
                        var moves = Generate(state);
                        stats.generated += moves.Count;
                        Candidate terminal = null;
                        foreach (var move in moves)
                            if (AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0))
                                terminal = move;
                        if (terminal != null)
                        {
                            best = CompactParkingChain(new Node { parent = node, move = terminal, depth = node.depth + 1 });
                            bestDepth = best.depth;
                            stats.firstSolutionDepth = bestDepth;
                            return true;
                        }

                        var requestedClearance = new Dictionary<int, CellMask>();
                        var relevant = RouteDependencyGroups(target.hole, state, target.destination, out requestedClearance);
                        foreach (var move in moves)
                        {
                            if (move.terminal || seen.Contains(move.state) || (!distanceOnly && !relevant.Contains(move.hole)))
                                continue;
                            int depth = node.depth + 1;
                            if (queuedDepth.TryGetValue(move.state, out int previousDepth) && previousDepth <= depth)
                            {
                                stats.duplicates++;
                                continue;
                            }

                            long score;
                            {
                                int p = move.state.GetValue(target.hole);
                                var blockers = Occupancy(move.state) ^ masks[target.hole][p];
                                score = OccupancyPathDistance(target.hole, p, blockers, target.destination) + (distanceOnly ? 0 : RouteClearancePenalty(target.hole, move.state, blockers, target.destination));
                            }

                            if (score >= long.MaxValue / 8)
                                continue;
                            // Commuting drags can enqueue the same arrangement many
                            // times before it is popped. Keep only an improving
                            // depth; bound this accelerator's bookkeeping too.
                            if (queuedDepth.Count >= 32768)
                                queuedDepth.Clear();
                            queuedDepth[move.state] = depth;
                            int uncleared = 0;
                            foreach (var request in requestedClearance)
                            {
                                int position = move.state.GetValue(request.Key);
                                if (position >= 0 && !(masks[request.Key][position] & request.Value).IsZero)
                                    uncleared++;
                            }

                            open.Add((score * 16 + uncleared * (distanceOnly ? 0 : 128L) + node.depth + 1, serial++, new Node { parent = node, move = move, depth = node.depth + 1 }));
                            if (open.Count > 2048)
                                open.Remove(open.Max);
                        }
                    }

                    stats.uniqueBoards += seen.Count;
                }

                return false;
            }

            private HashSet<int> RouteDependencyGroups(int hole, Positions state, int target, out Dictionary<int, CellMask> requestedClearance)
            {
                requestedClearance = new Dictionary<int, CellMask>();
                var requests = requestedClearance;
                var relevant = new HashSet<int>
                {
                    hole
                };
                int anchor = state.GetValue(hole);
                var blockers = Occupancy(state) ^ masks[hole][anchor];
                OccupancyPathDistance(hole, anchor, blockers, target);
                var key = (valid, hole, target, blockers, 8, 1);
                if (!occupancyDistances.TryGetValue(key, out var costs))
                    return relevant;
                CellMask corridor = masks[hole][anchor];
                var routeSeen = routeSeenScratch;
                Array.Clear(routeSeen, 0, count);
                for (int step = 0; step < count && (target >= 0 ? anchor != target : !goals[hole][anchor]); step++)
                {
                    routeSeen[anchor] = true;
                    int next = -1;
                    foreach (int q in neighbors[anchor])
                        if (!routeSeen[q] && valid[hole][q] && (masks[hole][q] & routeFixedMask).IsZero && CanStep(hole, anchor, q) && CanEnterCat(hole, q, Direction(CellAt(anchor), CellAt(q))) && (next < 0 || costs[q] < costs[next]))
                            next = q;
                    if (next < 0)
                        return relevant;
                    anchor = next;
                    corridor |= masks[hole][anchor];
                }

                var pending = new Queue<int>();
                void IncludeBlockers(CellMask path)
                {
                    for (int i = 0; i < capacities.Length; i++)
                    {
                        int p = state.GetValue(i);
                        if (p < 0 || i == hole || (masks[i][p] & path).IsZero)
                            continue;
                        foreach (int member in linkedGroups[i])
                        {
                            if (state.GetValue(member) < 0 || member == hole)
                                continue;
                            relevant.Add(member);
                            requests.TryGetValue(member, out var previous);
                            var required = previous | path;
                            if (required != previous)
                            {
                                requests[member] = required;
                                pending.Enqueue(member);
                            }
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
                    if (linkedGroups[blocker].Length > 1)
                        continue;
                    var parents = new int[count];
                    for (int p = 0; p < count; p++)
                        parents[p] = -1;
                    var queue = new Queue<int>();
                    queue.Enqueue(start);
                    parents[start] = start;
                    int parking = -1;
                    while (queue.Count > 0 && parking < 0)
                    {
                        int p = queue.Dequeue();
                        foreach (int q in neighbors[p])
                        {
                            if (parents[q] >= 0 || !valid[blocker][q] || !CanStep(blocker, p, q) || !CanEnterCat(blocker, q, Direction(CellAt(p), CellAt(q))) || !(masks[blocker][q] & (routeFixedMask & ~masks[blocker][start])).IsZero)
                                continue;
                            parents[q] = p;
                            if (goals[blocker][q] || (masks[blocker][q] & forbidden).IsZero)
                            {
                                parking = q;
                                break;
                            }

                            queue.Enqueue(q);
                        }
                    }

                    if (parking < 0)
                        continue;
                    CellMask parkingPath = CellMask.Zero;
                    for (int p = parking; p != start; p = parents[p])
                        parkingPath |= masks[blocker][p];
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
                TraceStage("dfs-" + acceleratorLimit);
                long startingWork = result.expanded;
                var seen = new HashSet<Positions>();
                var stack = new List<RouteFrame>
                {
                    new RouteFrame
                    {
                        node = new Node()
                    }
                };
                var cachedFrames = new Queue<(RouteFrame frame, List<Candidate> moves)>();
                int cachedCandidates = 0;
                try
                {
                    while (stack.Count > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (acceleratorLimit > 0 && result.expanded - startingWork >= acceleratorLimit)
                            return;
                        var frame = stack[stack.Count - 1];
                        var state = frame.node.move == null ? current : frame.node.move.state;
                        if (frame.moves == null)
                        {
                            if (!frame.expanded && !seen.Add(state))
                            {
                                stats.duplicates++;
                                stack.RemoveAt(stack.Count - 1);
                                continue;
                            }

                            frame.expanded = true;
                            stats.expanded++;
                            result.expanded++;
                            if ((stats.expanded & 4095) == 0)
                                context.progress?.Invoke(result);
                            frame.moves = Generate(state);
                            stats.generated += frame.moves.Count;
                            Candidate terminal = null;
                            foreach (var move in frame.moves)
                                if (AcceptTerminal(move) && (terminal == null || CompareTerminal(move, terminal) < 0))
                                    terminal = move;
                            if (terminal != null)
                            {
                                bestDepth = frame.node.depth + 1;
                                stats.firstSolutionDepth = bestDepth;
                                best = CompactParkingChain(new Node { parent = frame.node, move = terminal, depth = bestDepth });
                                bestDepth = best.depth;
                                return;
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

                            foreach (var move in frame.moves)
                                move.score = policy.Rank(this, move.state);
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
                                if (ReferenceEquals(oldest.frame.moves, oldest.moves))
                                    oldest.frame.moves = null;
                            }
                        }

                        if (frame.next >= frame.moves.Count)
                        {
                            stack.RemoveAt(stack.Count - 1);
                            continue;
                        }

                        var candidate = frame.moves[frame.next++];
                        if (candidate.terminal)
                            continue;
                        if (seen.Contains(candidate.state))
                        {
                            stats.duplicates++;
                            continue;
                        }

                        stack.Add(new RouteFrame { node = new Node { parent = frame.node, move = candidate, depth = frame.node.depth + 1 } });
                    }
                }
                finally
                {
                    stats.uniqueBoards = seen.Count;
                }
            }

            // Skip detours when one exact drag can reach a later arrangement on
            // the discovered chain. No committed consumption is undone.
            private Node CompactParkingChain(Node end)
            {
                TraceStage("compact");
                var chain = new List<Node>();
                for (var node = end; node.parent != null; node = node.parent)
                    chain.Add(node);
                chain.Reverse();
                var later = new Dictionary<Positions, int>();
                for (int i = 0; i < chain.Count; i++)
                    later[chain[i].move.state] = i;
                var compact = new Node();
                var state = current;
                int index = -1;
                while (index < chain.Count - 1)
                {
                    token.ThrowIfCancellationRequested();
                    stats.expanded++;
                    result.expanded++;
                    var moves = Generate(state);
                    stats.generated += moves.Count;
                    Candidate selected = null;
                    int selectedIndex = index;
                    foreach (var move in moves)
                    {
                        if (move.terminal)
                        {
                            if (!AcceptTerminal(move))
                                continue;
                            if (selected == null || !selected.terminal || CompareTerminal(move, selected) < 0)
                                selected = move;
                            selectedIndex = chain.Count - 1;
                        }
                        else if ((selected == null || !selected.terminal) && later.TryGetValue(move.state, out int target) && target > selectedIndex)
                        {
                            selected = move;
                            selectedIndex = target;
                        }
                    }

                    if (selected == null)
                        throw new ArgumentException("Discovered parking chain lost its exact successor.");
                    compact = new Node
                    {
                        parent = compact,
                        move = selected,
                        depth = compact.depth + 1
                    };
                    state = selected.state;
                    index = selectedIndex;
                }

                return compact;
            }

            private long RankState(Positions state) => policy.Rank(this, state);
            internal void Replay(List<EngineMove> moves, bool auditRelaxedAssignment = false)
            {
                Consume();
                if (auditRelaxedAssignment && !HasRelaxedCatAssignment())
                    throw new ArgumentException("Relaxed assignment rejected the initial state of a replayable plan.");
                if (auditRelaxedAssignment && !Cleared())
                {
                    Prepare();
                    if (!HasRelaxedCatAssignment())
                        throw new ArgumentException("Remaining-cat assignment rejected the initial state of a replayable plan.");
                    if (!HasRelaxedProgression())
                        throw new ArgumentException("Relaxed progression rejected the initial state of a replayable plan.");
                    if (!HasRelaxedGroupTarget())
                        throw new ArgumentException("Relaxed group reachability rejected the initial state of a replayable plan.");
                }

                foreach (var move in moves)
                {
                    CheckBudget();
                    int hole = board.holes.FindIndex(h => h.id == move.holeId);
                    if (hole < 0 || current.GetValue(hole) < 0 || !move.start.Equals(CellAt(current.GetValue(hole))) || move.path.Count < 2 || !move.path[0].Equals(move.start))
                        throw new ArgumentException("Invalid replay move start/path.");
                    foreach (int member in linkedGroups[hole])
                        if (current.GetValue(member) >= 0 && !CanDrag(member, current.GetValue(member)))
                            throw new ArgumentException("Replay attempted to drag a locked/frozen/covered group.");
                    for (int step = 1; step < move.path.Count; step++)
                    {
                        Prepare();
                        var from = move.path[step - 1];
                        var to = move.path[step];
                        if (Math.Abs(from.x - to.x) + Math.Abs(from.y - to.y) != 1)
                            throw new ArgumentException("Replay path must use adjacent grid steps.");
                        var occupied = Occupancy(current);
                        var group = linkedGroups[hole];
                        foreach (int member in group)
                            if (current.GetValue(member) >= 0)
                                occupied ^= masks[member][current.GetValue(member)];
                        int dx = to.x - from.x, dy = to.y - from.y;
                        var next = (int[])current.values.Clone();
                        foreach (int member in group)
                            if (current.GetValue(member) >= 0)
                            {
                                int old = current.GetValue(member);
                                var cell = new Cell(CellAt(old).x + dx, CellAt(old).y + dy);
                                if (!Inside(cell))
                                    throw new ArgumentException("Replay group moved outside board.");
                                int destination = Index(cell);
                                if (!CanStep(member, old, destination) || !valid[member][destination] || !(masks[member][destination] & occupied).IsZero || !CanEnterCat(member, destination, Direction(from, to)))
                                    throw new ArgumentException("Replay step violates a mechanic or collision.");
                                next[member] = destination;
                            }

                        var oldLayers = (int[])layers.Clone();
                        current = new Positions(next);
                        Consume(hole);
                        if (step + 1 < move.path.Count)
                            foreach (int member in group)
                                if (next[member] >= 0 && (current.GetValue(member) < 0 || oldLayers[member] != layers[member]))
                                    throw new ArgumentException("Replay continued after group finish/layer transition.");
                    }

                    if (move.expected != null && !Matches(move.expected, Snapshot()))
                        throw new ArgumentException("Replay mechanic snapshot differs from generated plan.");
                    if (auditRelaxedAssignment && !HasRelaxedCatAssignment())
                        throw new ArgumentException("Relaxed assignment rejected a settled move of a replayable plan.");
                    if (auditRelaxedAssignment && !Cleared())
                    {
                        Prepare();
                        if (!HasRelaxedCatAssignment())
                            throw new ArgumentException("Remaining-cat assignment rejected a settled move of a replayable plan.");
                        if (!HasRelaxedProgression())
                            throw new ArgumentException("Relaxed progression rejected a settled move of a replayable plan.");
                        if (!HasRelaxedGroupTarget())
                            throw new ArgumentException("Relaxed group reachability rejected a settled move of a replayable plan.");
                    }
                }

                result.status = Cleared() ? SolveStatus.Solved : SolveStatus.NoSolutionWithinDepthLimit;
            }

            private static bool Matches(BoardSnapshot a, BoardSnapshot b)
            {
                if (a.holes.Count != b.holes.Count || a.remainingCatIds.Count != b.remainingCatIds.Count || a.boxes.Count != b.boxes.Count || a.cats.Count != b.cats.Count || a.covers.Count != b.covers.Count)
                    return false;
                for (int i = 0; i < a.holes.Count; i++)
                {
                    var x = a.holes[i];
                    var y = b.holes[i];
                    if (x.id != y.id || x.color != y.color || x.layer != y.layer || x.numIced != y.numIced || x.hiddenCount != y.hiddenCount || x.locked != y.locked || x.remaining != y.remaining || x.finished != y.finished || !x.position.Equals(y.position))
                        return false;
                }

                for (int i = 0; i < a.remainingCatIds.Count; i++)
                    if (a.remainingCatIds[i] != b.remainingCatIds[i])
                        return false;
                for (int i = 0; i < a.boxes.Count; i++)
                    if (a.boxes[i].id != b.boxes[i].id || a.boxes[i].consumed != b.boxes[i].consumed || a.boxes[i].remainingHolesToUnlock != b.boxes[i].remainingHolesToUnlock)
                        return false;
                for (int i = 0; i < a.cats.Count; i++)
                    if (a.cats[i].id != b.cats[i].id || a.cats[i].numIced != b.cats[i].numIced)
                        return false;
                for (int i = 0; i < a.covers.Count; i++)
                    if (a.covers[i].id != b.covers[i].id || a.covers[i].remainingHits != b.covers[i].remainingHits)
                        return false;
                return true;
            }

            private bool Cleared()
            {
                // Sand holes (color >= 1000) are movable blockers, not win targets.
                for (int i = 0; i < board.holes.Count; i++)
                    if (board.holes[i].color < 1000 && current.GetValue(i) >= 0)
                        return false;
                foreach (bool alive in catsAlive)
                    if (alive)
                        return false;
                for (int i = 0; i < boxOffsets.Length; i++)
                    if (boxOffsets[i] < board.boxes[i].colors.Length)
                        return false;
                return true;
            }

            internal BoardSnapshot Snapshot()
            {
                if (current == null)
                    return null;
                var snapshot = new BoardSnapshot();
                for (int i = 0; i < capacities.Length; i++)
                    snapshot.holes.Add(new HoleSnapshot { id = board.holes[i].id, color = Color(i), layer = layers[i] + board.holes[i].layerOffset, numIced = board.holes[i].numIced, hiddenCount = board.holes[i].hiddenCount, locked = board.holes[i].locked, remaining = capacities[i], finished = current.GetValue(i) < 0, position = current.GetValue(i) < 0 ? new Cell(-1, -1) : CellAt(current.GetValue(i)) });
                for (int i = 0; i < catsAlive.Length; i++)
                    if (catsAlive[i])
                    {
                        snapshot.remainingCatIds.Add(board.cats[i].id);
                        snapshot.cats.Add(new CatSnapshot { id = board.cats[i].id, numIced = board.cats[i].numIced });
                    }

                for (int i = 0; i < boxOffsets.Length; i++)
                    snapshot.boxes.Add(new BoxSnapshot { id = board.boxes[i].id, consumed = boxOffsets[i], remainingHolesToUnlock = board.boxes[i].requiredHolesToUnlock });
                foreach (var cover in board.covers)
                    snapshot.covers.Add(new CoverSnapshot { id = cover.id, remainingHits = cover.remainingHits });
                return snapshot;
            }

            private List<EatEvent> Consume(int dragged = -1)
            {
                var events = new List<EatEvent>();
                // OnDragUpdate resolves linked partners before the selected hole.
                var order = new List<int>();
                if (dragged >= 0)
                {
                    foreach (int member in linkedGroups[dragged])
                        if (member != dragged)
                            order.Add(member);
                    order.Add(dragged);
                }

                for (int i = 0; i < capacities.Length; i++)
                    if (!order.Contains(i))
                        order.Add(i);
                bool changed;
                do
                {
                    CheckBudget();
                    changed = false;
                    foreach (int i in order)
                    {
                        if (!CanDrop(i, current.GetValue(i)))
                            continue;
                        var mask = masks[i][current.GetValue(i)];
                        int color = Color(i);
                        EatEvent eaten = null;
                        CatInput eatenCat = null;
                        // CheckDropCat visits local occupied cells in footprint order.
                        var origin = CellAt(current.GetValue(i));
                        foreach (var local in board.holes[i].footprint)
                        {
                            for (int c = 0; c < catsAlive.Length; c++)
                                if (catsAlive[c] && board.cats[c].numIced == 0 && board.cats[c].color == color && board.cats[c].position.Equals(new Cell(origin.x + local.x, origin.y + local.y)))
                                {
                                    catsAlive[c] = false;
                                    eatenCat = board.cats[c];
                                    eaten = new EatEvent
                                    {
                                        holeId = board.holes[i].id,
                                        color = color,
                                        catId = board.cats[c].id
                                    };
                                    break;
                                }

                            if (eaten != null)
                                break;
                        }

                        if (eaten == null)
                            for (int sourceType = 0; sourceType < 2 && eaten == null; sourceType++)
                                foreach (var local in board.holes[i].footprint)
                                {
                                    for (int b = 0; b < boxOffsets.Length; b++)
                                        if (board.boxes[b].tower == (sourceType == 1) && BoxTouches(i, current.GetValue(i), b) && BoxTouchesCell(i, origin, local, board.boxes[b]))
                                        {
                                            int index = boxOffsets[b]++;
                                            if (board.boxes[b].cats.Length > 0)
                                                eatenCat = board.boxes[b].cats[index];
                                            eaten = new EatEvent
                                            {
                                                holeId = board.holes[i].id,
                                                color = color,
                                                boxId = board.boxes[b].id,
                                                boxIndex = index
                                            };
                                            break;
                                        }

                                    if (eaten != null)
                                        break;
                                }

                        if (eaten == null)
                            continue;
                        events.Add(eaten);
                        capacities[i]--;
                        changed = true;
                        CatFinished(eatenCat);
                        if (capacities[i] == 0)
                        {
                            if (layers[i] + 1 < board.holes[i].layerColors.Length)
                            {
                                layers[i]++;
                                capacities[i] = board.holes[i].layerCounts[layers[i]];
                            }
                            else
                            {
                                var p = (int[])current.values.Clone();
                                p[i] = -1;
                                current = new Positions(p);
                                HoleFinished(i);
                            }
                        }

                        break;
                    }
                }
                while (changed);
                return events;
            }

            private void CatFinished(CatInput cat)
            {
                for (int i = 0; i < board.cats.Count; i++)
                    if (catsAlive[i] && board.cats[i].numIced > 0)
                        board.cats[i].numIced--;
                for (int b = 0; b < board.boxes.Count; b++)
                    for (int c = boxOffsets[b]; c < board.boxes[b].cats.Length; c++)
                        if (board.boxes[b].cats[c].numIced > 0)
                            board.boxes[b].cats[c].numIced--;
                if (cat == null)
                    return;
                if (cat.keyColorId >= 0)
                {
                    int keys = 0;
                    for (int c = 0; c < catsAlive.Length; c++)
                        if (catsAlive[c] && board.cats[c].keyColorId == cat.keyColorId)
                            keys++;
                    for (int b = 0; b < boxOffsets.Length; b++)
                        for (int c = boxOffsets[b]; c < board.boxes[b].cats.Length; c++)
                            if (board.boxes[b].cats[c].keyColorId == cat.keyColorId)
                                keys++;
                    if (keys == 0)
                        foreach (var h in board.holes)
                            if (h.lockColorId == cat.keyColorId)
                                h.locked = false;
                }

                if (cat.pickaxeColorId >= 0)
                    foreach (var cover in board.covers)
                        if (cover.remainingHits > 0)
                        {
                            cover.remainingHits--;
                            break;
                        }
            }

            private void HoleFinished(int source)
            {
                for (int i = 0; i < board.holes.Count; i++)
                    if (i != source && current.GetValue(i) >= 0)
                    {
                        if (board.holes[i].numIced > 0)
                            board.holes[i].numIced--;
                        if (board.holes[i].hiddenCount > 0)
                            board.holes[i].hiddenCount--;
                    }

                foreach (var box in board.boxes)
                    if (box.requiredHolesToUnlock > 0)
                        box.requiredHolesToUnlock--;
            }

            private void Prepare()
            {
                CheckBudget();
                targetDistances = new Dictionary<long, int[]>();
                catEntryBlocks = new int[capacities.Length][];
                valid = new bool[capacities.Length][];
                goals = new bool[capacities.Length][];
                distances = new int[capacities.Length][];
                goalAnchors = new int[capacities.Length][];
                bool[] finishedCells = null;
                int[] finishedDistances = null;
                for (int i = 0; i < capacities.Length; i++)
                {
                    CheckBudget();
                    // Finished holes cannot generate moves or consume goals. Keep
                    // indexed arrays for linked groups, without rebuilding their graph.
                    if (current.GetValue(i) < 0)
                    {
                        if (finishedCells == null)
                        {
                            finishedCells = new bool[count];
                            finishedDistances = new int[count];
                            for (int p = 0; p < count; p++)
                                finishedDistances[p] = int.MaxValue;
                        }

                        valid[i] = finishedCells;
                        goals[i] = finishedCells;
                        distances[i] = finishedDistances;
                        goalAnchors[i] = Array.Empty<int>();
                        continue;
                    }

                    CellMask bad = CoverMask(), target = CellMask.Zero;
                    bool canEat = board.holes[i].numIced == 0 && !board.holes[i].locked && board.holes[i].hiddenCount == 0;
                    for (int c = 0; c < catsAlive.Length; c++)
                        if (catsAlive[c])
                        {
                            if (canEat && board.cats[c].numIced == 0 && board.cats[c].color == Color(i))
                                target |= catMasks[c];
                            else
                                bad |= catMasks[c];
                        }

                    valid[i] = new bool[count];
                    goals[i] = new bool[count];
                    distances[i] = new int[count];
                    var anchors = new List<int>();
                    var queue = new Queue<int>();
                    for (int p = 0; p < count; p++)
                    {
                        distances[i][p] = int.MaxValue;
                        valid[i][p] = !masks[i][p].IsZero && (masks[i][p] & bad).IsZero;
                        if (valid[i][p])
                            foreach (var path in board.colorPaths)
                                if (!(masks[i][p] & Bit(path.position)).IsZero && (Color(i) >= 1000 || Color(i) < 0 || board.holes[i].hiddenCount > 0 || Color(i) % 10 != path.color))
                                {
                                    valid[i][p] = false;
                                    break;
                                }

                        goals[i][p] = valid[i][p] && canEat && !(masks[i][p] & target).IsZero;
                        if (valid[i][p] && canEat && !goals[i][p])
                            for (int b = 0; b < boxOffsets.Length; b++)
                                if (BoxTouches(i, p, b))
                                {
                                    goals[i][p] = true;
                                    break;
                                }

                        if (goals[i][p])
                        {
                            anchors.Add(p);
                            queue.Enqueue(p);
                            distances[i][p] = 0;
                        }
                    }

                    goalAnchors[i] = anchors.ToArray();
                    while (queue.Count > 0)
                    {
                        int p = queue.Dequeue();
                        foreach (int q in neighbors[p])
                            if (CanStep(i, p, q) && valid[i][q] && distances[i][q] == int.MaxValue)
                            {
                                distances[i][q] = distances[i][p] + 1;
                                queue.Enqueue(q);
                            }
                    }
                }

                routeFixedMask = ImmovableMask();
            }

            private CellMask Occupancy(Positions state)
            {
                if (state.hasOccupancy)
                    return state.occupied;
                CellMask occupied = CellMask.Zero;
                for (int i = 0; i < capacities.Length; i++)
                    if (state.GetValue(i) >= 0)
                        occupied |= masks[i][state.GetValue(i)];
                state.occupied = occupied;
                state.hasOccupancy = true;
                return occupied;
            }

            private bool HasMovableTarget()
            {
                return HasRelaxedGroupTarget();
            }

            // Other holes are removed, but each surviving link member must follow
            // its own static legality and gates. Failure is a necessary dead end;
            // success remains optimistic and never certifies a playable move.
            private bool HasRelaxedGroupTarget()
            {
                CheckBudget();
                string key = FullStateKey();
                if (relaxedGroupTargets.TryGetValue(key, out bool reachable))
                    return reachable;
                reachable = ComputeRelaxedGroupTarget();
                if (relaxedGroupTargets.Count >= 4096)
                    relaxedGroupTargets.Clear();
                relaxedGroupTargets[key] = reachable;
                return reachable;
            }

            private bool ComputeRelaxedGroupTarget()
            {
                CellMask fixedMask = ImmovableMask();
                var checkedMembers = new HashSet<int>();
                for (int root = 0; root < capacities.Length; root++)
                {
                    if (current.GetValue(root) < 0 || checkedMembers.Contains(root))
                        continue;
                    var group = linkedGroups[root];
                    bool draggable = true, target = false;
                    foreach (int member in group)
                    {
                        checkedMembers.Add(member);
                        if (current.GetValue(member) < 0)
                            continue;
                        if (!CanDrag(member, current.GetValue(member)))
                            draggable = false;
                        if (goalAnchors[member].Length > 0)
                            target = true;
                    }

                    if (!draggable || !target)
                        continue;
                    var seen = new bool[count];
                    var queue = new Queue<int>();
                    int initial = current.GetValue(root);
                    seen[initial] = true;
                    queue.Enqueue(initial);
                    while (queue.Count > 0)
                    {
                        CheckBudget();
                        int p = queue.Dequeue();
                        foreach (int q in neighbors[p])
                        {
                            if (seen[q])
                                continue;
                            int dx = CellAt(q).x - initial % board.width, dy = CellAt(q).y - initial / board.width;
                            int oldDx = CellAt(p).x - initial % board.width, oldDy = CellAt(p).y - initial / board.width;
                            bool legal = true, eats = false;
                            foreach (int member in group)
                            {
                                int origin = current.GetValue(member);
                                if (origin < 0)
                                    continue;
                                var dest = new Cell(CellAt(origin).x + dx, CellAt(origin).y + dy);
                                if (!Inside(dest))
                                {
                                    legal = false;
                                    break;
                                }

                                int destination = Index(dest), from = origin + oldDx + oldDy * board.width;
                                if (!valid[member][destination] || !(masks[member][destination] & fixedMask).IsZero || !CanStep(member, from, destination) || !CanEnterCat(member, destination, Direction(CellAt(p), CellAt(q))))
                                {
                                    legal = false;
                                    break;
                                }

                                if (goals[member][destination])
                                    eats = true;
                            }

                            if (!legal)
                                continue;
                            if (eats)
                                return true;
                            seen[q] = true;
                            queue.Enqueue(q);
                        }
                    }
                }

                return false;
            }

            private CellMask ImmovableMask()
            {
                var groups = new List<int[]>();
                var roots = new List<int>();
                var seen = new HashSet<int>();
                var fixedGroups = new HashSet<int>();
                CellMask fixedMask = CellMask.Zero;
                for (int root = 0; root < capacities.Length; root++)
                {
                    if (current.GetValue(root) < 0 || seen.Contains(root))
                        continue;
                    int index = groups.Count;
                    var group = linkedGroups[root];
                    groups.Add(group);
                    roots.Add(root);
                    bool fixedGroup = false, vertical = false, horizontal = false;
                    foreach (int member in group)
                    {
                        seen.Add(member);
                        if (current.GetValue(member) < 0)
                            continue;
                        if (!CanDrag(member, current.GetValue(member)))
                            fixedGroup = true;
                        vertical |= board.holes[member].movementType == 1;
                        horizontal |= board.holes[member].movementType == 2;
                    }

                    if (fixedGroup || vertical && horizontal)
                        fixedGroups.Add(index);
                }

                void AddMask(int index)
                {
                    foreach (int member in groups[index])
                        if (current.GetValue(member) >= 0)
                            fixedMask |= masks[member][current.GetValue(member)];
                }

                foreach (int index in fixedGroups)
                    AddMask(index);
                bool changed;
                do
                {
                    changed = false;
                    for (int index = 0; index < groups.Count; index++)
                    {
                        CheckBudget();
                        if (fixedGroups.Contains(index))
                            continue;
                        int origin = current.GetValue(roots[index]);
                        bool movable = false;
                        foreach (int q in neighbors[origin])
                        {
                            int dx = CellAt(q).x - CellAt(origin).x, dy = CellAt(q).y - CellAt(origin).y;
                            bool legal = true;
                            foreach (int member in groups[index])
                            {
                                int p = current.GetValue(member);
                                if (p < 0)
                                    continue;
                                var cell = new Cell(CellAt(p).x + dx, CellAt(p).y + dy);
                                if (!Inside(cell))
                                {
                                    legal = false;
                                    break;
                                }

                                int destination = Index(cell);
                                if (!valid[member][destination] || !CanStep(member, p, destination) || !CanEnterCat(member, destination, Direction(CellAt(origin), CellAt(q))) || !(masks[member][destination] & fixedMask).IsZero)
                                {
                                    legal = false;
                                    break;
                                }
                            }

                            if (legal)
                            {
                                movable = true;
                                break;
                            }
                        }

                        if (movable)
                            continue;
                        fixedGroups.Add(index);
                        AddMask(index);
                        changed = true;
                    }
                }
                while (changed);
                return fixedMask;
            }

            // Necessary matching only: ignore other holes, cats, covers, gates,
            // locks, ice, linked partners, queue order and layer order. Future
            // colors may be mixed along a path. Failure in this enlarged model
            // proves that a remaining cat cannot fit the available capacities.
            private bool HasRelaxedCatAssignment()
            {
                var pending = new List<(int color, CellMask target)>();
                for (int c = 0; c < catsAlive.Length; c++)
                    if (catsAlive[c])
                        pending.Add((board.cats[c].color, catMasks[c]));
                for (int b = 0; b < boxOffsets.Length; b++)
                    for (int c = boxOffsets[b]; c < board.boxes[b].colors.Length; c++)
                        pending.Add((board.boxes[b].colors[c], boxMasks[b]));
                if (pending.Count == 0)
                    return true;
                var units = new List<(int hole, int color)>();
                var reachable = new bool[capacities.Length][];
                for (int i = 0; i < capacities.Length; i++)
                {
                    CheckBudget();
                    if (current.GetValue(i) < 0)
                        continue;
                    var colors = new HashSet<int>();
                    var hole = board.holes[i];
                    int last = hole.layerColors.Length == 0 ? layers[i] + 1 : hole.layerColors.Length;
                    for (int layer = layers[i]; layer < last; layer++)
                    {
                        int color = hole.layerColors.Length == 0 ? hole.color : hole.layerColors[layer];
                        colors.Add(color);
                        int capacity = layer == layers[i] ? capacities[i] : hole.layerCounts[layer];
                        for (int unit = 0; unit < Math.Min(capacity, pending.Count); unit++)
                            units.Add((i, color));
                    }

                    bool Allowed(int anchor)
                    {
                        // Masks already exclude immutable obstacles. Removable
                        // covers and other holes remain relaxed here.
                        if (masks[i][anchor].IsZero)
                            return false;
                        foreach (int color in colors)
                        {
                            bool ok = true;
                            foreach (var path in board.colorPaths)
                                if (!(masks[i][anchor] & Bit(path.position)).IsZero && (color < 0 || color >= 1000 || color % 10 != path.color))
                                {
                                    ok = false;
                                    break;
                                }

                            if (ok)
                                return true;
                        }

                        return false;
                    }

                    var seen = new bool[count];
                    var queue = new Queue<int>();
                    int origin = current.GetValue(i);
                    seen[origin] = true;
                    queue.Enqueue(origin);
                    while (queue.Count > 0)
                    {
                        int p = queue.Dequeue();
                        foreach (int q in neighbors[p])
                            if (!seen[q] && CanStep(i, p, q) && Allowed(q))
                            {
                                seen[q] = true;
                                queue.Enqueue(q);
                            }
                    }

                    reachable[i] = seen;
                }

                if (units.Count < pending.Count)
                    return false;
                var edges = new bool[pending.Count][];
                for (int cat = 0; cat < pending.Count; cat++)
                {
                    CheckBudget();
                    edges[cat] = new bool[units.Count];
                    var access = new bool[capacities.Length];
                    for (int hole = 0; hole < capacities.Length; hole++)
                    {
                        if (reachable[hole] == null)
                            continue;
                        for (int p = 0; p < count; p++)
                            if (reachable[hole][p] && !(masks[hole][p] & pending[cat].target).IsZero)
                            {
                                access[hole] = true;
                                break;
                            }
                    }

                    for (int unit = 0; unit < units.Count; unit++)
                        edges[cat][unit] = pending[cat].color == units[unit].color && access[units[unit].hole];
                }

                var assigned = new int[units.Count];
                for (int unit = 0; unit < assigned.Length; unit++)
                    assigned[unit] = -1;
                bool Augment(int cat, bool[] seen)
                {
                    for (int unit = 0; unit < units.Count; unit++)
                    {
                        if (!edges[cat][unit] || seen[unit])
                            continue;
                        seen[unit] = true;
                        if (assigned[unit] < 0 || Augment(assigned[unit], seen))
                        {
                            assigned[unit] = cat;
                            return true;
                        }
                    }

                    return false;
                }

                for (int cat = 0; cat < pending.Count; cat++)
                    if (!Augment(cat, new bool[units.Count]))
                        return false;
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
                if (relaxedProgressionCache.TryGetValue(key, out bool cached))
                    return cached;
                var alive = (bool[])catsAlive.Clone();
                var offsets = (int[])boxOffsets.Clone();
                // Preserve exposed tokens as witnesses for every possible hole.
                // Removing a cat greedily for one hole must not steal another
                // hole's hypothetical opportunity to unlock its next layer.
                var exposed = new Dictionary<int, CellMask>();
                void Expose(int color, CellMask target)
                {
                    exposed.TryGetValue(color, out var previous);
                    exposed[color] = previous | target;
                }

                for (int c = 0; c < alive.Length; c++)
                    if (alive[c])
                        Expose(board.cats[c].color, catMasks[c]);
                for (int b = 0; b < offsets.Length; b++)
                    if (offsets[b] < board.boxes[b].colors.Length)
                        Expose(board.boxes[b].colors[offsets[b]], boxMasks[b]);
                var colors = new HashSet<int>[capacities.Length];
                var witnessed = new HashSet<int>[capacities.Length];
                for (int i = 0; i < capacities.Length; i++)
                    if (current.GetValue(i) >= 0)
                    {
                        colors[i] = new HashSet<int>
                        {
                            Color(i)
                        };
                        witnessed[i] = new HashSet<int>();
                    }

                bool changed;
                do
                {
                    token.ThrowIfCancellationRequested();
                    changed = false;
                    for (int i = 0; i < colors.Length; i++)
                    {
                        if (colors[i] == null)
                            continue;
                        foreach (int color in new List<int>(colors[i]))
                        {
                            CellMask blocked = CellMask.Zero;
                            exposed.TryGetValue(color, out var targets);
                            for (int c = 0; c < alive.Length; c++)
                                if (alive[c])
                                {
                                    if (board.cats[c].color != color)
                                        blocked |= catMasks[c];
                                }

                            for (int p = 0; p < count; p++)
                            {
                                var mask = masks[i][p];
                                // CanDrop can consume at an already occupied
                                // anchor after a layer/box change, even if that
                                // footprint contains another-color cat. Only
                                // entering a new anchor requires cat clearance.
                                if (mask.IsZero || (p != current.GetValue(i) && !(mask & blocked).IsZero) || (mask & targets).IsZero)
                                    continue;
                                if (witnessed[i].Add(color))
                                    changed = true;
                                bool consumed = false;
                                for (int c = 0; c < alive.Length; c++)
                                    if (alive[c] && board.cats[c].color == color && !(mask & catMasks[c]).IsZero)
                                    {
                                        alive[c] = false;
                                        consumed = true;
                                    }

                                for (int b = 0; b < offsets.Length; b++)
                                    if (offsets[b] < board.boxes[b].colors.Length && board.boxes[b].colors[offsets[b]] == color && !(mask & boxMasks[b]).IsZero)
                                    {
                                        offsets[b]++;
                                        consumed = true;
                                        if (offsets[b] < board.boxes[b].colors.Length)
                                            Expose(board.boxes[b].colors[offsets[b]], boxMasks[b]);
                                    }

                                if (consumed)
                                    changed = true;
                            }
                        }

                        var layersForHole = board.holes[i].layerColors;
                        for (int layer = layers[i] + 1; layer < layersForHole.Length; layer++)
                            if (witnessed[i].Contains(layersForHole[layer - 1]) && colors[i].Add(layersForHole[layer]))
                                changed = true;
                    }
                }
                while (changed);
                bool possible = true;
                foreach (bool remains in alive)
                    if (remains)
                        possible = false;
                for (int b = 0; b < offsets.Length; b++)
                    if (offsets[b] < board.boxes[b].colors.Length)
                        possible = false;
                if (relaxedProgressionCache.Count >= 2048)
                    relaxedProgressionCache.Clear();
                relaxedProgressionCache[key] = possible;
                return possible;
            }

            private List<Candidate> Generate(Positions state)
            {
                CheckBudget();
                var key = (valid, state);
                if (moveCache.TryGetValue(key, out var cached))
                {
                    var copy = new List<Candidate>(cached.Length);
                    foreach (var move in cached)
                        copy.Add(new Candidate { hole = move.hole, destination = move.destination, state = move.state, pathParents = move.pathParents, pathLength = move.pathLength, terminal = move.terminal,
                            routeContinuation = move.routeContinuation, consumptionUtility = move.consumptionUtility, futureRank = move.futureRank });
                    return copy;
                }

                var moves = GenerateUncached(state);
                if (moves.Count <= 8192)
                {
                    if (cachedMoveCount + moves.Count > 8192 || moveCache.Count >= 512)
                    {
                        moveCache.Clear();
                        cachedMoveCount = 0;
                    }

                    moveCache[key] = moves.ToArray();
                    cachedMoveCount += moves.Count;
                }

                return moves;
            }

            private List<Candidate> GenerateUncached(Positions state)
            {
                var result = new List<Candidate>();
                var occupied = Occupancy(state);
                // Only parent trees escape through candidates. Queue entries and
                // lengths are overwritten before use and can be reused per engine.
                if (generationQueue == null)
                    generationQueue = new int[count];
                if (generationPathLengths == null)
                    generationPathLengths = new int[count];
                for (int i = 0; i < capacities.Length; i++)
                {
                    CheckBudget();
                    int start = state.GetValue(i);
                    if (start < 0)
                        continue;
                    var group = linkedGroups[i];
                    bool draggable = true;
                    int representative = -1;
                    var blocked = occupied;
                    foreach (int member in group)
                        if (state.GetValue(member) >= 0)
                        {
                            if (representative < 0)
                                representative = member;
                            if (!CanDrag(member, state.GetValue(member)))
                                draggable = false;
                            blocked ^= masks[member][state.GetValue(member)];
                        }

                    if (representative != i || !draggable)
                        continue;
                    var parent = new int[count];
                    for (int p = 0; p < count; p++)
                        parent[p] = -1;
                    var pathLengths = generationPathLengths;
                    pathLengths[start] = 1;
                    int head = 0, tail = 0;
                    generationQueue[tail++] = start;
                    parent[start] = start;
                    while (head < tail)
                    {
                        CheckBudget();
                        int p = generationQueue[head++];
                        foreach (int q in neighbors[p])
                        {
                            if (parent[q] >= 0)
                                continue;
                            int dx = CellAt(q).x - CellAt(start).x, dy = CellAt(q).y - CellAt(start).y;
                            int stepDx = CellAt(q).x - CellAt(p).x, stepDy = CellAt(q).y - CellAt(p).y;
                            int direction = Direction(CellAt(p), CellAt(q));
                            bool legal = true, terminal = false;
                            foreach (int member in group)
                                if (state.GetValue(member) >= 0)
                                {
                                    int old = state.GetValue(member);
                                    var cell = new Cell(CellAt(old).x + dx, CellAt(old).y + dy);
                                    if (!Inside(cell))
                                    {
                                        legal = false;
                                        break;
                                    }

                                    int destination = Index(cell);
                                    var previous = new Cell(cell.x - stepDx, cell.y - stepDy);
                                    if (!CanStep(member, Index(previous), destination) || !valid[member][destination] || !(masks[member][destination] & blocked).IsZero || !CanEnterCat(member, destination, direction))
                                    {
                                        legal = false;
                                        break;
                                    }

                                    terminal |= goals[member][destination];
                                }

                            if (!legal)
                                continue;
                            parent[q] = p;
                            pathLengths[q] = pathLengths[p] + 1;
                            if (!terminal)
                                generationQueue[tail++] = q;
                            var nextState = state.Moved(group, q - start);
                            // Parent links are assigned once in BFS. Candidates share this
                            // tree and only materialize a drag when it enters a plan.
                            // Only this linked group moved. Footprints are immutable across
                            // mechanic phases, so the other groups retain their occupancy.
                            var nextOccupied = blocked;
                            foreach (int member in group)
                                if (nextState.GetValue(member) >= 0)
                                    nextOccupied |= masks[member][nextState.GetValue(member)];
                            nextState.occupied = nextOccupied;
                            nextState.hasOccupancy = true;
                            result.Add(new Candidate { hole = i, destination = q, state = nextState, terminal = terminal, pathParents = parent, pathLength = pathLengths[q] });
                        }
                    }
                }

                return result;
            }

            private long Score(Positions state, bool includeClearance = true)
            {
                var cacheKey = (valid, state, context.aggregation, prioritizeRouteKeys);
                if (!includeClearance && cheapScoreCache.TryGetValue(cacheKey, out long cachedScore))
                    return cachedScore;
                var occupied = Occupancy(state);
                long score = long.MaxValue, sum = 0;
                for (int i = 0; i < capacities.Length; i++)
                {
                    int p = state.GetValue(i);
                    if (p < 0 || !CanDrag(i, p))
                        continue;
                    {
                        bool draggable = true;
                        foreach (int member in linkedGroups[i])
                            if (state.GetValue(member) >= 0 && !CanDrag(member, state.GetValue(member)))
                                draggable = false;
                        if (!draggable)
                            continue;
                    }

                    // Aggregate only goals reachable in this mechanic phase. A hole
                    // behind other-colored cats may become relevant after consumption.
                    // Keep invalid starting anchors: a layer transition can leave a
                    // hole over an old-color cat, yet a legal exit can still reach a goal.
                    if ((context.aggregation == RouteAggregation.ReachableSum) && valid[i][p] && distances[i][p] == int.MaxValue)
                        continue;
                    long holeScore = long.MaxValue;
                    {
                        var blockers = occupied;
                        foreach (int member in linkedGroups[i])
                            if (state.GetValue(member) >= 0)
                                blockers ^= masks[member][state.GetValue(member)];
                        holeScore = linkedGroups[i].Length > 1 ? LinkedRouteDistance(i, state, blockers) : OccupancyPathDistance(i, p, blockers);
                        if (includeClearance && linkedGroups[i].Length == 1)
                            holeScore += RouteClearancePenalty(i, state, blockers);
                        if (prioritizeRouteKeys && linkedGroups[i].Length == 1)
                        {
                            foreach (int target in goalAnchors[i])
                            {
                                bool unlocks = false;
                                for (int c = 0; c < catsAlive.Length && !unlocks; c++)
                                {
                                    var cat = board.cats[c];
                                    if (!catsAlive[c] || cat.keyColorId < 0 || cat.color != Color(i) || (masks[i][target] & (CellMask.One << Index(cat.position))).IsZero)
                                        continue;
                                    for (int h = 0; h < capacities.Length; h++)
                                        if (state.GetValue(h) >= 0 && board.holes[h].locked && board.holes[h].lockColorId == cat.keyColorId)
                                        {
                                            unlocks = true;
                                            break;
                                        }
                                }

                                if (!unlocks)
                                    continue;
                                long keyCost = OccupancyPathDistance(i, p, blockers, target);
                                if (keyCost >= long.MaxValue / 8)
                                    continue;
                                keyCost += RouteClearancePenalty(i, state, blockers, target);
                                holeScore = Math.Min(holeScore, keyCost / 8);
                            }
                        }
                    }

                    score = Math.Min(score, holeScore);
                    if (context.aggregation == RouteAggregation.Minimum || holeScore == long.MaxValue)
                        continue;
                    sum = sum > long.MaxValue - holeScore ? long.MaxValue : sum + holeScore;
                }

                long value = score == long.MaxValue || context.aggregation == RouteAggregation.Minimum ? score : sum;
                if (!includeClearance)
                {
                    if (cheapScoreCache.Count >= 16384)
                        cheapScoreCache.Clear();
                    cheapScoreCache[cacheKey] = value;
                }
                return value;
            }

            // Inspect a suggested corridor before favoring its target. A blocker
            // must have a reachable parking footprint outside the whole corridor,
            // not merely one legal adjacent move. Other movable groups are ignored
            // in this test: a failed test discourages this route, never prunes the
            // search (another route or a consumption may unlock it).
            private long RouteClearancePenalty(int hole, Positions state, CellMask blockers, int target = -1)
            {
                var key = (valid, hole, target, state);
                if (routeClearanceScores.TryGetValue(key, out long cached))
                    return cached;
                long value = ComputeRouteClearancePenalty(hole, state, blockers, target);
                if (routeClearanceScores.Count >= 4096)
                    routeClearanceScores.Clear();
                routeClearanceScores[key] = value;
                return value;
            }

            private long ComputeRouteClearancePenalty(int hole, Positions state, CellMask blockers, int target)
            {
                var key = (valid, hole, target, blockers, 8, 1);
                if (!occupancyDistances.TryGetValue(key, out var costs))
                    return 0;
                int anchor = state.GetValue(hole);
                CellMask corridor = masks[hole][anchor];
                var routeSeen = routeSeenScratch;
                Array.Clear(routeSeen, 0, count);
                for (int step = 0; step < count && (target >= 0 ? anchor != target : !goals[hole][anchor]); step++)
                {
                    routeSeen[anchor] = true;
                    int next = -1;
                    long bestCost = long.MaxValue;
                    foreach (int q in neighbors[anchor])
                    {
                        if (routeSeen[q] || !valid[hole][q] || !(masks[hole][q] & routeFixedMask).IsZero || !CanStep(hole, anchor, q) || !CanEnterCat(hole, q, Direction(CellAt(anchor), CellAt(q))))
                            continue;
                        if (costs[q] < bestCost)
                        {
                            next = q;
                            bestCost = costs[q];
                        }
                    }

                    if (next < 0 || bestCost >= long.MaxValue / 8)
                        return 0;
                    anchor = next;
                    corridor |= masks[hole][anchor];
                }

                if (target >= 0 ? anchor != target : !goals[hole][anchor])
                    return 0;
                long penalty = 0;
                var checkedMembers = new bool[capacities.Length];
                checkedMembers[hole] = true;
                for (int root = 0; root < capacities.Length; root++)
                {
                    if (checkedMembers[root] || state.GetValue(root) < 0)
                        continue;
                    var group = linkedGroups[root];
                    CellMask footprint = CellMask.Zero;
                    bool draggable = true;
                    foreach (int member in group)
                    {
                        checkedMembers[member] = true;
                        int p = state.GetValue(member);
                        if (p < 0)
                            continue;
                        footprint |= masks[member][p];
                        draggable &= CanDrag(member, p);
                    }

                    if ((footprint & corridor).IsZero)
                        continue;
                    if (!draggable)
                    {
                        penalty += 100000;
                        continue;
                    }

                    var settled = parkingSettledScratch;
                    Array.Clear(settled, 0, count);
                    var parkingCosts = parkingCostsScratch;
                    var parkingGoals = parkingGoalsScratch;
                    Array.Clear(parkingGoals, 0, count);
                    for (int p = 0; p < count; p++)
                        parkingCosts[p] = long.MaxValue;
                    int origin = state.GetValue(root);
                    parkingCosts[origin] = 0;
                    var parkingBlockers = Occupancy(state) & ~footprint;
                    bool cleared = false;
                    for (int step = 0; step < count && !cleared; step++)
                    {
                        token.ThrowIfCancellationRequested();
                        int parkingAnchor = -1;
                        for (int p = 0; p < count; p++)
                            if (!settled[p] && parkingCosts[p] != long.MaxValue && (parkingAnchor < 0 || parkingCosts[p] < parkingCosts[parkingAnchor]))
                                parkingAnchor = p;
                        if (parkingAnchor < 0)
                            break;
                        settled[parkingAnchor] = true;
                        if (parkingGoals[parkingAnchor])
                        {
                            penalty += parkingCosts[parkingAnchor];
                            cleared = true;
                            break;
                        }

                        foreach (int q in neighbors[parkingAnchor])
                        {
                            if (settled[q])
                                continue;
                            int dx = CellAt(q).x - CellAt(origin).x, dy = CellAt(q).y - CellAt(origin).y;
                            int oldDx = CellAt(parkingAnchor).x - CellAt(origin).x;
                            int oldDy = CellAt(parkingAnchor).y - CellAt(origin).y;
                            bool legal = true, eats = false;
                            CellMask moved = CellMask.Zero;
                            foreach (int member in group)
                            {
                                int p = state.GetValue(member);
                                if (p < 0)
                                    continue;
                                var cell = new Cell(CellAt(p).x + dx, CellAt(p).y + dy);
                                if (!Inside(cell))
                                {
                                    legal = false;
                                    break;
                                }

                                int dest = Index(cell), from = p + oldDx + oldDy * board.width;
                                if (!valid[member][dest] || !(masks[member][dest] & (routeFixedMask & ~footprint)).IsZero || !CanStep(member, from, dest) || !CanEnterCat(member, dest, Direction(CellAt(parkingAnchor), CellAt(q))))
                                {
                                    legal = false;
                                    break;
                                }

                                moved |= masks[member][dest];
                                eats |= goals[member][dest];
                            }

                            if (!legal)
                                continue;
                            var overlap = moved & parkingBlockers;
                            int blocked = 0;
                            blocked += overlap.PopCount();
                            long nextCost = parkingCosts[parkingAnchor] + 8 + (long)blocked * 8 * 8;
                            if (nextCost < parkingCosts[q])
                                parkingCosts[q] = nextCost;
                            parkingGoals[q] = eats || (moved & corridor).IsZero;
                        }
                    }

                    if (!cleared)
                        penalty += 100000;
                }

                return penalty;
            }

            // Heuristic only: other movable holes can be crossed for a penalty.
            // Exact Generate/Replay still enforce occupancy, links and gates.
            private long OccupancyPathDistance(int hole, int anchor, CellMask blockers, int target = -1)
            {
                const long infinity = long.MaxValue / 8;
                var key = (valid, hole, target, blockers, 8, 1);
                if (!occupancyDistances.TryGetValue(key, out var costs))
                {
                    var graph = RouteGraph(hole);
                    costs = new long[count];
                    var penalties = distancePenaltiesScratch;
                    Array.Clear(penalties, 0, count);
                    for (int p = 0; p < count; p++)
                    {
                        CheckBudget();
                        costs[p] = infinity;
                        var overlap = masks[hole][p] & blockers;
                        int blocked = 0;
                        blocked += overlap.PopCount();
                        penalties[p] = (long)8 * blocked;
                    }

                    var heap = distanceHeapScratch;
                    heap.Clear();
                    void Push(int p, long cost)
                    {
                        heap.Add((p, cost));
                        int child = heap.Count - 1;
                        while (child > 0)
                        {
                            int parent = (child - 1) / 2;
                            if (heap[parent].cost <= heap[child].cost)
                                break;
                            var item = heap[parent];
                            heap[parent] = heap[child];
                            heap[child] = item;
                            child = parent;
                        }
                    }

                    bool Allowed(int p) => graph.allowed[p];
                    if (target >= 0)
                    {
                        if (Allowed(target))
                        {
                            costs[target] = penalties[target];
                            Push(target, costs[target]);
                        }
                    }
                    else
                        foreach (int goal in goalAnchors[hole])
                            if (Allowed(goal))
                            {
                                costs[goal] = penalties[goal];
                                Push(goal, costs[goal]);
                            }

                    while (heap.Count > 0)
                    {
                        CheckBudget();
                        var item = heap[0];
                        heap[0] = heap[heap.Count - 1];
                        heap.RemoveAt(heap.Count - 1);
                        int parent = 0;
                        while (parent * 2 + 1 < heap.Count)
                        {
                            int child = parent * 2 + 1;
                            if (child + 1 < heap.Count && heap[child + 1].cost < heap[child].cost)
                                child++;
                            if (heap[parent].cost <= heap[child].cost)
                                break;
                            var swap = heap[parent];
                            heap[parent] = heap[child];
                            heap[child] = swap;
                            parent = child;
                        }

                        if (item.cost != costs[item.p])
                            continue;
                        foreach (int p in graph.predecessors[item.p])
                        {
                            long cost = item.cost + 1 + penalties[p];
                            if (cost >= costs[p])
                                continue;
                            costs[p] = cost;
                            Push(p, cost);
                        }
                    }

                    if (occupancyDistances.Count >= 4096)
                        occupancyDistances.Clear();
                    occupancyDistances[key] = costs;
                }

                if (valid[hole][anchor])
                    return costs[anchor];
                // Leaving an initially invalid footprint can still be legal.
                long resultCost = infinity;
                foreach (int q in neighbors[anchor])
                    if (valid[hole][q] && CanStep(hole, anchor, q))
                        resultCost = Math.Min(resultCost, costs[q] + 1);
                return resultCost;
            }

            // The route of a linked group must fit every surviving partner. Scoring
            // members independently can prioritize an impossible relaxed route.
            private long LinkedRouteDistance(int hole, Positions state, CellMask blockers)
            {
                var members = linkedGroups[hole];
                int root = -1;
                foreach (int member in members)
                    if (current.GetValue(member) >= 0)
                    {
                        root = member;
                        break;
                    }

                if (root < 0)
                    return long.MaxValue / 8;
                var key = (valid, root, -2, blockers, 8, 1);
                if (!occupancyDistances.TryGetValue(key, out var costs))
                {
                    const long infinity = long.MaxValue / 8;
                    costs = new long[count];
                    var legal = distanceLegalScratch;
                    Array.Clear(legal, 0, count);
                    var terminal = distanceTerminalScratch;
                    Array.Clear(terminal, 0, count);
                    var penalties = distancePenaltiesScratch;
                    Array.Clear(penalties, 0, count);
                    int origin = current.GetValue(root);
                    int Destination(int member, int anchor)
                    {
                        int p = current.GetValue(member);
                        var cell = new Cell(CellAt(p).x + CellAt(anchor).x - CellAt(origin).x, CellAt(p).y + CellAt(anchor).y - CellAt(origin).y);
                        return Inside(cell) ? Index(cell) : -1;
                    }

                    for (int anchor = 0; anchor < count; anchor++)
                    {
                        costs[anchor] = infinity;
                        legal[anchor] = true;
                        CellMask footprint = CellMask.Zero;
                        foreach (int member in members)
                        {
                            if (current.GetValue(member) < 0)
                                continue;
                            int p = Destination(member, anchor);
                            if (p < 0 || !valid[member][p] || !(masks[member][p] & routeFixedMask).IsZero)
                            {
                                legal[anchor] = false;
                                break;
                            }

                            footprint |= masks[member][p];
                            terminal[anchor] |= goals[member][p];
                        }

                        var overlap = footprint & blockers;
                        int blocked = 0;
                        blocked += overlap.PopCount();
                        penalties[anchor] = (long)8 * blocked;
                    }

                    bool Edge(int from, int to)
                    {
                        int direction = Direction(CellAt(from), CellAt(to));
                        foreach (int member in members)
                        {
                            if (current.GetValue(member) < 0)
                                continue;
                            int a = Destination(member, from), b = Destination(member, to);
                            if (a < 0 || b < 0 || !CanStep(member, a, b) || !CanEnterCat(member, b, direction))
                                return false;
                        }

                        return true;
                    }

                    // Small board: an array Dijkstra avoids allocating heap nodes.
                    var settled = parkingSettledScratch;
                    Array.Clear(settled, 0, count);
                    for (int p = 0; p < count; p++)
                        if (legal[p] && terminal[p])
                            costs[p] = penalties[p];
                    for (int step = 0; step < count; step++)
                    {
                        token.ThrowIfCancellationRequested();
                        int next = -1;
                        for (int p = 0; p < count; p++)
                            if (!settled[p] && costs[p] < infinity && (next < 0 || costs[p] < costs[next]))
                                next = p;
                        if (next < 0)
                            break;
                        settled[next] = true;
                        foreach (int p in neighbors[next])
                        {
                            if (!legal[p] || !Edge(p, next))
                                continue;
                            long value = costs[next] + 1 + penalties[p];
                            if (value < costs[p])
                                costs[p] = value;
                        }
                    }

                    if (occupancyDistances.Count >= 4096)
                        occupancyDistances.Clear();
                    occupancyDistances[key] = costs;
                }

                return costs[state.GetValue(root)];
            }

            private int Compare(Candidate a, Candidate b)
            {
                int order = a.score.CompareTo(b.score);
                if (order == 0)
                    order = distances[a.hole][a.destination].CompareTo(distances[b.hole][b.destination]);
                if (order == 0)
                    order = board.holes[a.hole].id.CompareTo(board.holes[b.hole].id);
                return order != 0 ? order : a.destination.CompareTo(b.destination);
            }

            private int CompareTerminal(Candidate a, Candidate b)
            {
                {
                    int utilityOrder = ConsumptionUtility(b).CompareTo(ConsumptionUtility(a));
                    if (utilityOrder != 0)
                        return utilityOrder;
                }

                int futureOrder = (a.futureRank ?? long.MaxValue).CompareTo(b.futureRank ?? long.MaxValue);
                if (futureOrder != 0)
                    return futureOrder;
                int order = (capacities[a.hole] != 1).CompareTo(capacities[b.hole] != 1);
                if (order == 0)
                    order = a.pathLength.CompareTo(b.pathLength);
                if (order == 0)
                    order = board.holes[a.hole].id.CompareTo(board.holes[b.hole].id);
                return order != 0 ? order : a.destination.CompareTo(b.destination);
            }

            private bool AcceptTerminal(Candidate move)
            {
                return move.terminal && (excludedEvents == null || !excludedEvents.Contains((move.hole, move.destination)))
                    && (excludedEventStates == null || !excludedEventStates.Contains((move.hole, move.state))) && HasRouteContinuation(move);
            }

            // Inspect a candidate before committing its consumption. Failure of
            // the relaxed next-target test proves an immediate dead end; success
            // remains optimistic. No previously committed event is undone.
            private bool HasRouteContinuation(Candidate move)
            {
                if (move.routeContinuation.HasValue)
                    return move.routeContinuation.Value;
                var saved = SaveCheckpoint();
                try
                {
                    current = move.state;
                    Consume(move.hole);
                    bool possible = Cleared();
                    if (possible)
                        move.futureRank = 0;
                    if (!possible)
                    {
                        Prepare();
                        possible = HasRelaxedCatAssignment() && HasRelaxedProgression() && HasRelaxedGroupTarget();
                        if (possible)
                        {
                            var savedAggregation = context.aggregation;
                            bool savedKeys = prioritizeRouteKeys;
                            try
                            {
                                context.aggregation = RouteAggregation.Minimum;
                                prioritizeRouteKeys = false;
                                move.futureRank = Score(current, false);
                            }
                            finally
                            {
                                context.aggregation = savedAggregation;
                                prioritizeRouteKeys = savedKeys;
                            }
                        }
                    }

                    move.routeContinuation = possible;
                    return possible;
                }
                finally
                {
                    saved.Restore(this);
                }
            }

            private long ConsumptionUtility(Candidate move)
            {
                if (move.consumptionUtility.HasValue)
                    return move.consumptionUtility.Value;
                CheckBudget();
                var saved = SaveCheckpoint();
                try
                {
                    for (int i = 0; i < capacities.Length; i++)
                        saved.draggable[i] = CanDrag(i, move.state.GetValue(i));
                    current = move.state;
                    long utility = Consume(move.hole).Count;
                    for (int i = 0; i < capacities.Length; i++)
                    {
                        if (move.state.GetValue(i) < 0)
                            continue;
                        if (current.GetValue(i) < 0)
                            utility += board.holes[i].footprint.Length * 16L;
                        else if (!saved.draggable[i] && CanDrag(i, current.GetValue(i)))
                            utility += board.holes[i].footprint.Length * 8L;
                    }

                    for (int b = 0; b < board.boxes.Count; b++)
                        if (saved.PreviousBoxLock(this, b) > 0 && board.boxes[b].requiredHolesToUnlock == 0)
                            utility += 8;
                    move.consumptionUtility = utility;
                    return utility;
                }
                finally
                {
                    saved.Restore(this);
                }
            }

            private void Search(Positions state, Node node)
            {
                CheckBudget();
                if (node.depth >= Math.Min(bestDepth - 1, searchDepthLimit))
                    return;
                if (visited.TryGetValue(state, out int previous))
                {
                    if (previous <= node.depth)
                    {
                        stats.duplicates++;
                        return;
                    }

                    stats.reopened++;
                }

                visited[state] = node.depth;
                stats.expanded++;
                result.expanded++;
                if ((stats.expanded & 4095) == 0)
                    context.progress?.Invoke(result);
                var moves = Generate(state);
                stats.generated += moves.Count;
                Candidate terminal = null;
                foreach (var m in moves)
                    if (AcceptTerminal(m) && (terminal == null || CompareTerminal(m, terminal) < 0))
                        terminal = m;
                if (terminal != null)
                {
                    bestDepth = node.depth + 1;
                    best = new Node
                    {
                        parent = node,
                        move = terminal,
                        depth = bestDepth
                    };
                    if (stats.firstSolutionDepth == 0)
                        stats.firstSolutionDepth = bestDepth;
                    return;
                }

                if (node.depth + 1 >= Math.Min(bestDepth - 1, searchDepthLimit))
                    return;
                foreach (var m in moves)
                {
                    CheckBudget();
                    m.score = RankState(m.state);
                }

                moves.Sort(Compare);
                foreach (var move in moves)
                {
                    // An excluded consumption cannot become a parking step: it would
                    // change mechanics before subsequent search states are generated.
                    if (move.terminal)
                        continue;
                    Search(move.state, new Node { parent = node, move = move, depth = node.depth + 1 });
                    if (bestDepth == 1 || (best != null))
                        return;
                }
            }

            private string FullStateKey(bool includePositions = true)
            {
                var key = new System.Text.StringBuilder();
                for (int i = 0; i < capacities.Length; i++)
                {
                    var h = board.holes[i];
                    if (includePositions)
                        key.Append(current.GetValue(i)).Append(',');
                    key.Append(capacities[i]).Append(',').Append(layers[i]).Append(',').Append(h.numIced).Append(',').Append(h.hiddenCount).Append(',').Append(h.locked ? 1 : 0).Append(';');
                }

                for (int i = 0; i < catsAlive.Length; i++)
                    key.Append(catsAlive[i] ? 1 : 0).Append(',').Append(board.cats[i].numIced).Append(';');
                for (int i = 0; i < boxOffsets.Length; i++)
                {
                    key.Append(boxOffsets[i]).Append(',').Append(board.boxes[i].requiredHolesToUnlock).Append(':');
                    foreach (var cat in board.boxes[i].cats)
                        key.Append(cat.numIced).Append(',');
                    key.Append(';');
                }

                foreach (var cover in board.covers)
                    key.Append(cover.remainingHits).Append(';');
                return key.ToString();
            }
        }
    }
}

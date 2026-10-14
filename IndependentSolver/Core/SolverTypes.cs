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

    internal enum RouteAggregation { Minimum, ReachableSum }

    internal sealed class EngineContext
    {
        internal RouteAggregation aggregation;
        internal Action<EngineResult> progress;
        internal long expandedLimit = long.MaxValue;
    }

    [Serializable]
    internal sealed class HoleInput
    {
        public int id, color, remaining;
        // 0: free, 1: vertical, 2: horizontal (Hole.holeInfo.movementType).
        public int movementType;
        public int movementTypeLimit;
        public int countdownMoves;
        public int bombRemaining;
        public int holeType;
        public bool pendingSpawn;
        public bool axe;
        public bool isPlug;
        public int plugColorId = -1;
        public bool isOpenClose, closed;
        public int runesealedUnlockHoleIndex = -1;
        public int numIced, hiddenCount;
        public int lockColorId = -1;
        public bool locked;
        // Remaining layers, starting at the currently active layer.
        public int[] layerColors = Array.Empty<int>(), layerCounts = Array.Empty<int>();
        public int layerOffset;
        public GateInput[] gates = Array.Empty<GateInput>();
        public Cell expandCell = new Cell(-1,-1);
        public bool expandPicked;
        public Cell position;
        public Cell[] footprint = Array.Empty<Cell>();
    }

    [Serializable]
    internal sealed class CatInput
    {
        public bool pendingSpawn;
        public int id, color, numIced;
        public int keyColorId = -1, pickaxeColorId = -1;
        public int scissorsColorId = -1, mysteryBoxId = -1;
        public Cell position;
        public CatInput Copy() => (CatInput)MemberwiseClone();
    }

    [Serializable] internal sealed class SpawnerInput
    {
        public Cell position;
        public int direction, consumed;
        public int[] holeIds = Array.Empty<int>();
        public SpawnerInput Copy() => new SpawnerInput { position=position, direction=direction, consumed=consumed, holeIds=(int[])holeIds.Clone() };
    }

    [Serializable] internal sealed class PlugInput
    {
        public bool pendingSpawn;
        public int id, color;
        public Cell position;
        public bool alive = true;
        public PlugInput Copy() => (PlugInput)MemberwiseClone();
    }

    [Serializable] internal struct GateInput { public Cell local; public int directions; }
    [Serializable] internal struct LinkInput { public int holeId1, holeId2; }
    [Serializable] internal struct ColorPathInput { public Cell position; public int color; }
    [Serializable] internal sealed class FenceEdgeInput
    {
        public bool pendingSpawn;
        public Cell position;
        public int direction, color;
        public bool blocked = true;
        public FenceEdgeInput Copy() => (FenceEdgeInput)MemberwiseClone();
    }
    [Serializable] internal sealed class CoverInput
    {
        public bool pendingSpawn;
        public int id, remainingHits;
        public bool rope;
        public bool finishHoleCounter;
        public bool barricade;
        public bool secretLockCounter;
        public int color;
        public Cell[] cells = Array.Empty<Cell>();
    }

    [Serializable]
    internal sealed class BoxInput
    {
        public bool pendingSpawn;
        public int id;
        public Cell mouth;
        public int[] colors = Array.Empty<int>();
        public CatInput[] cats = Array.Empty<CatInput>();
        public Cell position;
        public int direction, requiredHolesToUnlock;
        public bool tower;
    }

    [Serializable]
    internal sealed class BarrieInput
    {
        public int id;
        public Cell[] positions=Array.Empty<Cell>(),switchPositions=Array.Empty<Cell>();
        public BarrieInput Copy()=>new BarrieInput{id=id,positions=(Cell[])positions.Clone(),switchPositions=(Cell[])switchPositions.Clone()};
    }

    [Serializable]
    internal sealed class ElevatorInput
    {
        public Cell position;
        public BoardInput child;
        public ElevatorInput Copy()=>new ElevatorInput{position=position,child=child.Copy()};
    }

    [Serializable]
    internal sealed class BoardInput
    {
        public int width, height;
        public List<Cell> obstacles = new List<Cell>();
        public List<HoleInput> holes = new List<HoleInput>();
        public List<SpawnerInput> spawners = new List<SpawnerInput>();
        public List<BarrieInput> barries = new List<BarrieInput>();
        public List<ElevatorInput> elevators = new List<ElevatorInput>();
        public List<PlugInput> plugs = new List<PlugInput>();
        public List<CatInput> cats = new List<CatInput>();
        public List<BoxInput> boxes = new List<BoxInput>();
        public List<LinkInput> links = new List<LinkInput>();
        public List<ColorPathInput> colorPaths = new List<ColorPathInput>();
        public List<CoverInput> covers = new List<CoverInput>();
        public List<FenceEdgeInput> fences = new List<FenceEdgeInput>();
        public List<string> ignoredMechanics = new List<string>();

        public BoardInput Copy()
        {
            var copy = new BoardInput { width = width, height = height };
            copy.obstacles.AddRange(obstacles);
            copy.ignoredMechanics.AddRange(ignoredMechanics);
            copy.links.AddRange(links); copy.colorPaths.AddRange(colorPaths);
            foreach (var fence in fences) copy.fences.Add(fence.Copy());
            foreach (var h in holes) copy.holes.Add(new HoleInput { id = h.id, color = h.color, remaining = h.remaining, movementType = h.movementType, movementTypeLimit = h.movementTypeLimit, countdownMoves = h.countdownMoves, bombRemaining = h.bombRemaining, holeType = h.holeType, pendingSpawn = h.pendingSpawn, axe = h.axe, isPlug = h.isPlug, plugColorId = h.plugColorId, isOpenClose = h.isOpenClose, closed = h.closed, runesealedUnlockHoleIndex = h.runesealedUnlockHoleIndex, numIced = h.numIced, hiddenCount = h.hiddenCount, lockColorId = h.lockColorId, locked = h.locked, layerOffset = h.layerOffset, layerColors = (int[])h.layerColors.Clone(), layerCounts = (int[])h.layerCounts.Clone(), gates = (GateInput[])h.gates.Clone(), expandCell = h.expandCell, expandPicked = h.expandPicked, position = h.position, footprint = (Cell[])h.footprint.Clone() });
            foreach (var spawner in spawners) copy.spawners.Add(spawner.Copy());
            foreach(var barrie in barries)copy.barries.Add(barrie.Copy());
            foreach(var elevator in elevators)copy.elevators.Add(elevator.Copy());
            foreach (var plug in plugs) copy.plugs.Add(plug.Copy());
            foreach (var c in cats) copy.cats.Add(c.Copy());
            foreach (var b in boxes)
            {
                var queue = new CatInput[b.cats.Length]; for (int i = 0; i < queue.Length; i++) queue[i] = b.cats[i].Copy();
                copy.boxes.Add(new BoxInput { pendingSpawn=b.pendingSpawn, id = b.id, mouth = b.mouth, position = b.position, direction = b.direction, tower = b.tower, requiredHolesToUnlock = b.requiredHolesToUnlock, colors = (int[])b.colors.Clone(), cats = queue });
            }
            foreach (var c in covers) copy.covers.Add(new CoverInput { pendingSpawn=c.pendingSpawn, id = c.id, remainingHits = c.remainingHits, rope = c.rope, finishHoleCounter = c.finishHoleCounter, barricade = c.barricade, secretLockCounter = c.secretLockCounter, color = c.color, cells = (Cell[])c.cells.Clone() });
            return copy;
        }
    }

    internal enum SolveStatus { Solved, NoSolutionWithinDepthLimit, TimedOut, Cancelled, InvalidInput, UnsupportedMechanics, NextCatFound, NextCatBudgetExhausted, NoNextCatReachable }

    [Serializable]
    internal sealed class HoleSnapshot
    {
        public int id, remaining, color, layer, numIced, hiddenCount;
        public int countdownMoves;
        public int bombRemaining;
        public int movementType, movementTypeLimit, runesealedUnlockHoleIndex = -1;
        public bool locked;
        public bool closed;
        public Cell position;
        public bool isPlug;
        public bool expandPicked;
        public bool pendingSpawn;
        public bool finished;
    }

    [Serializable]
    internal sealed class BoxSnapshot { public int id, consumed, remainingHolesToUnlock; }
    [Serializable] internal sealed class CatSnapshot { public int id, numIced; public int mysteryBoxId = -1; }
    [Serializable] internal sealed class CoverSnapshot { public int id, remainingHits; }

    [Serializable]
    internal sealed class BoardSnapshot
    {
        public List<HoleSnapshot> holes = new List<HoleSnapshot>();
        public List<int> spawnerOffsets = new List<int>();
        public List<int> remainingPlugIds = new List<int>();
        public List<int> remainingCatIds = new List<int>();
        public List<BoxSnapshot> boxes = new List<BoxSnapshot>();
        public List<CatSnapshot> cats = new List<CatSnapshot>();
        public List<CoverSnapshot> covers = new List<CoverSnapshot>();
        public List<bool> fenceEdgesBlocked = new List<bool>();
        public List<bool> barrieCellsBlocked = new List<bool>();
        public List<bool> pendingEntityFlags = new List<bool>();
        public List<int> elevatorCandidateStates = new List<int>();
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
    internal sealed class EngineMove
    {
        public int holeId;
        public Cell start;
        public List<Cell> path = new List<Cell>();
        public List<EatEvent> eaten = new List<EatEvent>();
        public BoardSnapshot expected;
    }

    internal sealed class SubproblemStats
    {
        public int firstSolutionDepth, uniqueBoards;
        public long expanded, duplicates, reopened, generated, prunedConsecutiveGroupDrags;
    }

    [Serializable]
    internal sealed class EngineResult
    {
        internal bool baselineBudgetExceeded;
        public SolveStatus status;
        public string message;

        public List<EngineMove> moves = new List<EngineMove>();
        public List<EatEvent> initialEaten = new List<EatEvent>();
        public BoardSnapshot finalState;
        public List<string> ignoredMechanics = new List<string>();
        public long expanded;
        internal string searchStage;
        internal long eventBacktracks;
        internal long abstractExpanded;
    }
}







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
    }

    [Serializable]
    internal sealed class HoleInput
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
    internal sealed class CatInput
    {
        public int id, color, numIced;
        public int keyColorId = -1, pickaxeColorId = -1;
        public Cell position;
        public CatInput Copy() => (CatInput)MemberwiseClone();
    }

    [Serializable] internal struct GateInput { public Cell local; public int directions; }
    [Serializable] internal struct LinkInput { public int holeId1, holeId2; }
    [Serializable] internal struct ColorPathInput { public Cell position; public int color; }
    [Serializable] internal sealed class CoverInput
    {
        public int id, remainingHits;
        public Cell[] cells = Array.Empty<Cell>();
    }

    [Serializable]
    internal sealed class BoxInput
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
    internal sealed class BoardInput
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

    internal enum SolveStatus { Solved, NoSolutionWithinDepthLimit, TimedOut, Cancelled, InvalidInput, UnsupportedMechanics, NextCatFound, NextCatBudgetExhausted, NoNextCatReachable }

    [Serializable]
    internal sealed class HoleSnapshot
    {
        public int id, remaining, color, layer, numIced, hiddenCount;
        public bool locked;
        public Cell position;
        public bool finished;
    }

    [Serializable]
    internal sealed class BoxSnapshot { public int id, consumed, remainingHolesToUnlock; }
    [Serializable] internal sealed class CatSnapshot { public int id, numIced; }
    [Serializable] internal sealed class CoverSnapshot { public int id, remainingHits; }

    [Serializable]
    internal sealed class BoardSnapshot
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
        public SolveStatus status;
        public string message;

        public List<EngineMove> moves = new List<EngineMove>();
        public List<EatEvent> initialEaten = new List<EatEvent>();
        public BoardSnapshot finalState;
        public List<string> ignoredMechanics = new List<string>();
        public long expanded;
    }
}


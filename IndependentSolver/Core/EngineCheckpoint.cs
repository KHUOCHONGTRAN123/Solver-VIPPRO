using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private bool[] routeSeenScratch, parkingSettledScratch, parkingGoalsScratch, distanceLegalScratch, distanceTerminalScratch, distanceSettledScratch;
            private long[] parkingCostsScratch, distancePenaltiesScratch;
            private List<(int p, long cost)> distanceHeapScratch;
            private Cell[] cellCoordinates;
            private void InitializeScratch()
            {
                routeSeenScratch = new bool[count]; parkingSettledScratch = new bool[count]; parkingGoalsScratch = new bool[count];
                distanceLegalScratch = new bool[count]; distanceTerminalScratch = new bool[count]; distanceSettledScratch = new bool[count];
                parkingCostsScratch = new long[count]; distancePenaltiesScratch = new long[count]; distanceHeapScratch = new List<(int, long)>(count);
                cellCoordinates = new Cell[count];
                for (int i = 0; i < count; i++) cellCoordinates[i] = new Cell(i % board.width, i / board.width);
            }
            private StateCheckpoint checkpoint;
            private readonly Dictionary<(bool[][] phase, Positions state, bool keys), long> rankCache = new Dictionary<(bool[][], Positions, bool), long>();
            private readonly Dictionary<(bool[][] phase, Positions state), Candidate[]> moveCache = new Dictionary<(bool[][], Positions), Candidate[]>();
            private int cachedMoveCount;
            private StateCheckpoint SaveCheckpoint()
            {
                if (checkpoint == null) checkpoint = new StateCheckpoint(this);
                checkpoint.Save(this); return checkpoint;
            }
            // No nested consumption simulations: buffers can be reused safely.
            // Immutable geometry/shape arrays never need to be copied.
            private sealed class StateCheckpoint
            {
                private readonly int[] savedCapacities, savedLayers, savedOffsets, mutable;
                private readonly bool[] savedAlive;
                internal readonly bool[] draggable;
                private Positions savedCurrent;
                private bool[][] savedValid, savedGoals;
                private int[][] savedDistances, savedAnchors, savedEntries;
                private Dictionary<long, int[]> savedTargets;
                private CellMask savedFixed;
                private bool active;
                internal StateCheckpoint(Engine engine)
                {
                    savedCapacities = new int[engine.capacities.Length]; savedLayers = new int[engine.layers.Length];
                    savedOffsets = new int[engine.boxOffsets.Length]; savedAlive = new bool[engine.catsAlive.Length];
                    draggable = new bool[engine.capacities.Length];
                    int size = engine.board.holes.Count * 3 + engine.board.cats.Count + engine.board.boxes.Count + engine.board.covers.Count;
                    foreach (var box in engine.board.boxes) size += box.cats.Length;
                    mutable = new int[size];
                }
                internal void Save(Engine e)
                {
                    if (active) throw new InvalidOperationException("Nested checkpoint is not supported.");
                    active = true; savedCurrent = e.current;
                    Array.Copy(e.capacities, savedCapacities, savedCapacities.Length); Array.Copy(e.layers, savedLayers, savedLayers.Length);
                    Array.Copy(e.boxOffsets, savedOffsets, savedOffsets.Length); Array.Copy(e.catsAlive, savedAlive, savedAlive.Length);
                    savedValid = e.valid; savedGoals = e.goals; savedDistances = e.distances; savedAnchors = e.goalAnchors;
                    savedTargets = e.targetDistances; savedEntries = e.catEntryBlocks; savedFixed = e.routeFixedMask;
                    int at = 0;
                    foreach (var hole in e.board.holes) { mutable[at++] = hole.numIced; mutable[at++] = hole.hiddenCount; mutable[at++] = hole.locked ? 1 : 0; }
                    foreach (var cat in e.board.cats) mutable[at++] = cat.numIced;
                    foreach (var box in e.board.boxes) { mutable[at++] = box.requiredHolesToUnlock; foreach (var cat in box.cats) mutable[at++] = cat.numIced; }
                    foreach (var cover in e.board.covers) mutable[at++] = cover.remainingHits;
                }
                internal int PreviousBoxLock(Engine e, int index)
                {
                    int at = e.board.holes.Count * 3 + e.board.cats.Count;
                    for (int i = 0; i < index; i++) at += 1 + e.board.boxes[i].cats.Length;
                    return mutable[at];
                }
                internal void Restore(Engine e)
                {
                    e.current = savedCurrent;
                    Array.Copy(savedCapacities, e.capacities, savedCapacities.Length); Array.Copy(savedLayers, e.layers, savedLayers.Length);
                    Array.Copy(savedOffsets, e.boxOffsets, savedOffsets.Length); Array.Copy(savedAlive, e.catsAlive, savedAlive.Length);
                    e.valid = savedValid; e.goals = savedGoals; e.distances = savedDistances; e.goalAnchors = savedAnchors;
                    e.targetDistances = savedTargets; e.catEntryBlocks = savedEntries; e.routeFixedMask = savedFixed;
                    int at = 0;
                    foreach (var hole in e.board.holes) { hole.numIced = mutable[at++]; hole.hiddenCount = mutable[at++]; hole.locked = mutable[at++] != 0; }
                    foreach (var cat in e.board.cats) cat.numIced = mutable[at++];
                    foreach (var box in e.board.boxes) { box.requiredHolesToUnlock = mutable[at++]; foreach (var cat in box.cats) cat.numIced = mutable[at++]; }
                    foreach (var cover in e.board.covers) cover.remainingHits = mutable[at++];
                    active = false;
                }
            }
        }
    }
}

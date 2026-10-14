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
                private string savedPhaseKey;
                private int savedPhaseId;
                private bool[][] savedValid, savedGoals;
                private int[][] savedDistances, savedAnchors, savedEntries, savedFenceSteps;
                private Dictionary<long, int[]> savedTargets;
                private CellMask savedFixed;
                private bool active;
                private bool[][] savedBarries;
                private bool[] savedPending;
                private int[][] savedElevators;
                internal StateCheckpoint(Engine engine)
                {
#if CATDOM_PROFILE
                    using var profile=engine.Profile("Checkpoint.Allocate");
#endif
                    savedCapacities = new int[engine.capacities.Length]; savedLayers = new int[engine.layers.Length];
                    savedOffsets = new int[engine.boxOffsets.Length]; savedAlive = new bool[engine.catsAlive.Length];
                    draggable = new bool[engine.capacities.Length];
                    int size = engine.board.holes.Count * 12 + engine.board.cats.Count * 2 + engine.board.boxes.Count + engine.board.covers.Count + engine.board.fences.Count + engine.board.plugs.Count + engine.board.spawners.Count;
                    foreach (var box in engine.board.boxes) size += box.cats.Length * 2;
                    mutable = new int[size];
                }
                internal void Save(Engine e)
                {
#if CATDOM_PROFILE
                    using var profile=e.Profile("Checkpoint.Save");
#endif
                    if (active) throw new InvalidOperationException("Nested checkpoint is not supported.");
                    active = true; savedCurrent = e.current;
                    if(e.barrieBlocked!=null)
                    {
                        savedBarries=new bool[e.barrieBlocked.Length][];
                        for(int g=0;g<savedBarries.Length;g++)savedBarries[g]=(bool[])e.barrieBlocked[g].Clone();
                    }
                    savedPhaseKey=e.cachedPhaseKey;
                    savedPhaseId=e.cachedPhaseId;
                    if(e.board.elevators.Count>0){savedPending=e.SavePendingEntities();savedElevators=e.SaveElevatorPlans();}
                    Array.Copy(e.capacities, savedCapacities, savedCapacities.Length); Array.Copy(e.layers, savedLayers, savedLayers.Length);
                    Array.Copy(e.boxOffsets, savedOffsets, savedOffsets.Length); Array.Copy(e.catsAlive, savedAlive, savedAlive.Length);
                    savedValid = e.valid; savedGoals = e.goals; savedDistances = e.distances; savedAnchors = e.goalAnchors;
                    savedFenceSteps = e.fenceStepPhase; savedTargets = e.targetDistances; savedEntries = e.catEntryBlocks; savedFixed = e.routeFixedMask;
                    int at = 0;
                    foreach (var hole in e.board.holes) { mutable[at++] = hole.numIced; mutable[at++] = hole.hiddenCount; mutable[at++] = hole.locked ? 1 : 0; mutable[at++] = hole.movementType; mutable[at++] = hole.movementTypeLimit; mutable[at++] = hole.runesealedUnlockHoleIndex; mutable[at++] = hole.closed ? 1 : 0; mutable[at++] = hole.countdownMoves; mutable[at++] = hole.bombRemaining; mutable[at++] = hole.isPlug ? 1 : 0; mutable[at++] = hole.pendingSpawn ? 1 : 0; mutable[at++] = hole.expandPicked ? 1 : 0; }
                    foreach (var cat in e.board.cats) { mutable[at++] = cat.numIced; mutable[at++] = cat.mysteryBoxId; }
                    foreach (var box in e.board.boxes) { mutable[at++] = box.requiredHolesToUnlock; foreach (var cat in box.cats) { mutable[at++] = cat.numIced; mutable[at++] = cat.mysteryBoxId; } }
                    foreach (var cover in e.board.covers) mutable[at++] = cover.remainingHits;
                    foreach (var fence in e.board.fences) mutable[at++] = fence.blocked ? 1 : 0;
                    foreach (var plug in e.board.plugs) mutable[at++] = plug.alive ? 1 : 0;
                    foreach (var spawner in e.board.spawners) mutable[at++] = spawner.consumed;
                }
                internal int PreviousBoxLock(Engine e, int index)
                {
                    int at = e.board.holes.Count * 12 + e.board.cats.Count * 2;
                    for (int i = 0; i < index; i++) at += 1 + e.board.boxes[i].cats.Length * 2;
                    return mutable[at];
                }
                internal void Restore(Engine e)
                {
#if CATDOM_PROFILE
                    using var profile=e.Profile("Checkpoint.Restore");
#endif
                    e.current = savedCurrent;
                    if(savedBarries!=null)
                        for(int g=0;g<savedBarries.Length;g++)Array.Copy(savedBarries[g],e.barrieBlocked[g],savedBarries[g].Length);
                    e.cachedPhaseKey=savedPhaseKey;
                    e.cachedPhaseId=savedPhaseId;
                    Array.Copy(savedCapacities, e.capacities, savedCapacities.Length); Array.Copy(savedLayers, e.layers, savedLayers.Length);
                    Array.Copy(savedOffsets, e.boxOffsets, savedOffsets.Length); Array.Copy(savedAlive, e.catsAlive, savedAlive.Length);
                    if(savedPending!=null)e.RestorePendingEntities(savedPending);
                    if(savedElevators!=null)e.RestoreElevatorPlans(savedElevators);
                    e.valid = savedValid; e.goals = savedGoals; e.distances = savedDistances; e.goalAnchors = savedAnchors;
                    e.fenceStepPhase = savedFenceSteps; e.targetDistances = savedTargets; e.catEntryBlocks = savedEntries; e.routeFixedMask = savedFixed;
                    int at = 0;
                    foreach (var hole in e.board.holes) { hole.numIced = mutable[at++]; hole.hiddenCount = mutable[at++]; hole.locked = mutable[at++] != 0; hole.movementType = mutable[at++]; hole.movementTypeLimit = mutable[at++]; hole.runesealedUnlockHoleIndex = mutable[at++]; hole.closed = mutable[at++] != 0; hole.countdownMoves = mutable[at++]; hole.bombRemaining = mutable[at++]; hole.isPlug = mutable[at++] != 0; hole.pendingSpawn = mutable[at++] != 0; hole.expandPicked = mutable[at++] != 0; }
                    foreach (var cat in e.board.cats) { cat.numIced = mutable[at++]; cat.mysteryBoxId = mutable[at++]; }
                    foreach (var box in e.board.boxes) { box.requiredHolesToUnlock = mutable[at++]; foreach (var cat in box.cats) { cat.numIced = mutable[at++]; cat.mysteryBoxId = mutable[at++]; } }
                    foreach (var cover in e.board.covers) cover.remainingHits = mutable[at++];
                    foreach (var fence in e.board.fences) fence.blocked = mutable[at++] != 0;
                    foreach (var plug in e.board.plugs) plug.alive = mutable[at++] != 0;
                    foreach (var spawner in e.board.spawners) spawner.consumed = mutable[at++];
                    e.ApplyExpandMasks();
                    active = false;
                }
            }
        }
    }
}







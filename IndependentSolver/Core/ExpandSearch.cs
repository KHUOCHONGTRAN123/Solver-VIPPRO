using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private sealed class ExpandParkingNode
            {
                internal ExpandParkingNode parent;
                internal Candidate move;
                internal Positions state;
                internal int depth;
                internal long rank;
            }

            // Between consumption events, expand pickup/release only changes the
            // footprint. Search parking positions in that fixed mechanic phase;
            // simulate consumption only at pickup or a terminal BFS destination.
            // Countdown/spawn/release-dependent mechanics retain the exact engine.
            private bool HasPermanentSandLink()
                => board.links.Exists(l=>board.holes[l.holeId1].color>=1000 || board.holes[l.holeId2].color>=1000);
            private int expandOrderVariant;
            private bool TryExpandPlan()
            {
                // Diversify event order before spending time on the full drag
                // graph. Each attempt reuses the exact pickup/release simulator.
                for(expandOrderVariant=0;expandOrderVariant<4;expandOrderVariant++)
                    if(TryExpandOrderedPlan())return true;
                return false;
            }

            private bool TryExpandOrderedPlan()
            {
                if (board.spawners.Count > 0 || board.elevators.Count > 0 || board.barries.Count > 0 ||
                    board.holes.Exists(h => h.countdownMoves > 0)) return false;
                Prepare();
                var initial = new StateCheckpoint(this);
                initial.Save(this);
                int moveCount = result.moves.Count;
                bool solved = false;
                try
                {
                    while (!Cleared())
                    {
                        CheckBudget();
                        int liveExpand=0;
                        for(int i=0;i<capacities.Length;i++)
                            if(current.GetValue(i)>=0 && ExpandGeometry.Enabled(board.holes[i]))liveExpand++;
                        TraceStage("expand-event-"+liveExpand+"-order-"+expandOrderVariant);
                        if(liveExpand==0 && board.plugs.TrueForAll(p=>!p.alive) &&
                            !board.holes.Exists(h=>current.GetValue(h.id)>=0 && (h.bombRemaining>0 || h.isPlug || h.isOpenClose)))
                        {
                            TraceStage("expand-to-ordinary");
                            RunOrdinaryPhases();
                            if(result.status!=SolveStatus.Solved)return false;
                            break;
                        }
                        if(liveExpand==0 && TryDecisionPlan())break;
                        if (!FindExpandEvent() && !FindExpandFocusedEvent() && (expandOrderVariant<3 || !FindExpandEvent(pressure:true) && !FindExpandEvent(retainParking:true,pressure:true)))
                        {
                            if(expandOrderVariant<3)
                            {
                                result.finalState=Snapshot();
                                TraceStage("expand-retry-"+expandOrderVariant);
                                return false;
                            }
                            // Preserve the completed events. A narrow parking
                            // beam can miss a corridor without invalidating the
                            // current phase; try its exact drag graph first.
                            result.finalState=Snapshot();
                            TraceStage("expand-stalled");
                            SearchCountdownDrags();
                            if (result.status != SolveStatus.Solved) return false;
                            break;
                        }
                    }
                    solved = true;
                    return true;
                }
                finally
                {
                    if (!solved)
                    {
                        initial.Restore(this);
                        result.moves.RemoveRange(moveCount, result.moves.Count - moveCount);
                    }
                }
            }

            private bool FindExpandFocusedEvent()
            {
                if(!HasPermanentSandLink() && !HasActiveBomb())return false;
                Prepare();
                var phase=new StateCheckpoint(this);phase.Save(this);
                var state=current;
                var geometry=BuildExpandRankGeometry(phase,state);
                var saved=CaptureExpandRouteGeometry(true);
                var targets=new List<(int hole,int destination,long rank)>();
                for(int hole=0;hole<capacities.Length;hole++)
                {
                    if(state.GetValue(hole)<0 || geometry[hole]==null)continue;
                    if(HasActiveBomb() && !Array.Exists(linkedGroups[hole],m=>state.GetValue(m)>=0 && board.holes[m].bombRemaining>0))
                    {
                        if(NonBombFinishWouldExpire(hole) || BombEventPriority(new Candidate{hole=hole,destination=state.GetValue(hole)})>1)continue;
                    }
                    UseExpandRouteGeometry(geometry[hole]);
                    var blocked=OccupancyExcept(state,linkedGroups[hole]);
                    if(linkedGroups[hole].Length>1)
                    {
                        long rank=LinkedRouteDistance(hole,state,blocked);
                        if(rank<long.MaxValue/16)targets.Add((hole,-1,rank));
                    }
                    else foreach(int destination in goalAnchors[hole])
                    {
                        long rank=OccupancyPathDistance(hole,state.GetValue(hole),blocked,destination);
                        if(rank<long.MaxValue/16)targets.Add((hole,destination,rank));
                    }
                }
                UseExpandRouteGeometry(saved);phase.Restore(this);
                targets.Sort((a,b)=>a.rank!=b.rank?a.rank.CompareTo(b.rank):a.hole!=b.hole?a.hole.CompareTo(b.hole):a.destination.CompareTo(b.destination));
                int attempts=0;
                foreach(var target in targets)
                {
                    if(attempts++>=12)break;
                    if(FindExpandEvent(focus:target.hole,focusDestination:target.destination))return true;
                }
                return false;
            }

            private bool FindExpandEvent(bool retainParking = false, bool pressure = false, int focus = -1, int focusDestination = -1, bool curtainFocus = false)
            {
                Prepare();
                var phase = new StateCheckpoint(this);
                phase.Save(this);
                var root = new ExpandParkingNode { state = current };
                var frontier = new List<ExpandParkingNode> { root };
                var seen = new HashSet<Positions> { current };
                var parking = retainParking ? new StableMinHeap<ExpandParkingNode>() : null;
                int serial=0;
                bool requireBombFocus=focus>=0 && !bombSoftFocus && !hasExpandableHoles && HasActiveBomb();
                bool requireFocus=requireBombFocus || curtainFocus;
                if(retainParking)
                {
                    TraceStage("expand-parking");
                    parking.Add((0,serial++,root));
                }
                var selectedOrder = new List<int>();
                for (int i = 0; i < capacities.Length; i++)
                    if (current.GetValue(i) >= 0) selectedOrder.Add(i);
                // A hole bomb counts other completed holes, not parking drags.
                selectedOrder.Sort((a,b) =>
                {
                    int aa = board.holes[a].bombRemaining > 0 ? board.holes[a].bombRemaining : int.MaxValue;
                    int bb = board.holes[b].bombRemaining > 0 ? board.holes[b].bombRemaining : int.MaxValue;
                    int order = aa.CompareTo(bb);
                    if(order!=0)return order;
                    if(HasActiveBomb())
                    {
                        int relevance=BombEventPriority(new Candidate{hole=a,destination=current.GetValue(a)}).CompareTo(BombEventPriority(new Candidate{hole=b,destination=current.GetValue(b)}));
                        if(relevance!=0)return relevance;
                    }
                    bool sandA=linkedGroups[a].Length>1 && Array.Exists(linkedGroups[a],m=>board.holes[m].color>=1000 && current.GetValue(m)>=0);
                    bool sandB=linkedGroups[b].Length>1 && Array.Exists(linkedGroups[b],m=>board.holes[m].color>=1000 && current.GetValue(m)>=0);
                    order=sandB.CompareTo(sandA);
                    if(order!=0)return order;
                    if(expandOrderVariant==1)
                    {
                        order=HasPermanentSandLink() ? ExpandGeometry.Enabled(board.holes[b]).CompareTo(ExpandGeometry.Enabled(board.holes[a])) : 0;
                        if(order!=0)return order;
                        order=capacities[a].CompareTo(capacities[b]);
                        if(order!=0)return order;
                    }
                    if(expandOrderVariant==3)
                    {
                        order=(linkedGroups[b].Length>1).CompareTo(linkedGroups[a].Length>1);
                        if(order!=0)return order;
                        order=capacities[a].CompareTo(capacities[b]);
                        if(order!=0)return order;
                    }
                    if(expandOrderVariant==2)return b.CompareTo(a);
                    return a.CompareTo(b);
                });
                int work = 0;
                ExpandRouteGeometry[] rankGeometry = null;
                // The final bomb cat may require parking several unrelated holes.
                // Give this small, urgent phase a wider frontier before restarting
                // the whole plan; ordinary Expand phases keep their measured budget.
                bool finalBombFocus=requireBombFocus &&
                    Array.Exists(linkedGroups[focus],h=>board.holes[h].bombRemaining>0 && capacities[h]==1);
                int workLimit=focus>=0 ? (finalBombFocus || curtainFocus ? 2048 : 256) : retainParking ? 16384 : 4096;
                while ((retainParking ? parking.Count>0 : frontier.Count>0) && work<workLimit)
                {
                    if(retainParking)
                    {
                        frontier.Clear();
                        frontier.Add(parking.Pop().node);
                    }
                    var next = new List<ExpandParkingNode>();
                    foreach (var node in frontier)
                    {
                        CheckBudget();
                        work++;
                        result.expanded++;
                        if ((work & 31) == 0) context.progress?.Invoke(result);
                        foreach (int selected in selectedOrder)
                        {
                            phase.Restore(this);
                            current = node.state;
                            var group = linkedGroups[selected];
                            int representative = -1;
                            bool draggable = true;
                            foreach (int member in group)
                                if (current.GetValue(member) >= 0)
                                {
                                    if (representative < 0) representative = member;
                                    if (!CanDrag(member,current.GetValue(member))) draggable = false;
                                }
                            if (representative != selected || !draggable) continue;
                            var start = current;
                            int plugs = AlivePlugCount();
                            var pickup = new EngineMove { holeId=board.holes[selected].id, start=CellAt(start.GetValue(selected)) };
                            pickup.path.Add(pickup.start);
                            bool expanding = Array.Exists(group,m=>ExpandGeometry.Enabled(board.holes[m]));
                            if (expanding) pickup.eaten.AddRange(BeginExpandPickup(selected));
                            if (BombFailed()) continue;
                            if (pickup.eaten.Count > 0 || AlivePlugCount() < plugs)
                            {
                                if ((!requireFocus || selected==focus) && FinishExpandEvent(selected,start,pickup,expanding))
                                {
                                    CommitExpandEvent(node,pickup);
                                    return true;
                                }
                                continue;
                            }
                            Prepare();
                            var picked = new StateCheckpoint(this);
                            picked.Save(this);
                            var candidates = GenerateUncached(current,selected,true);
                            foreach (var candidate in candidates)
                            {
                                if (candidate.terminal)
                                {
                                    picked.Restore(this);
                                    current = candidate.state;
                                    var move = new EngineMove { holeId=board.holes[selected].id, start=pickup.start };
                                    foreach (int p in candidate.path) move.path.Add(CellAt(p));
                                    move.eaten.AddRange(Consume(selected));
                                    if ((!requireFocus || selected==focus && (focusDestination<0 || candidate.destination==focusDestination)) &&
                                        (move.eaten.Count > 0 || AlivePlugCount() < plugs) && FinishExpandEvent(selected,start,move,expanding))
                                    {
                                        CommitExpandEvent(node,move);
                                        return true;
                                    }
                                }
                                else
                                {
                                    // Candidate occupancy used the expanded group.
                                    // The parking state is released/collapsed, so
                                    // do not reuse that footprint-specific cache.
                                    var parked = new Positions(candidate.state.values);
                                    if (seen.Add(parked))
                                        next.Add(new ExpandParkingNode { parent=node,move=candidate,state=parked,depth=node.depth+1 });
                                }
                            }
                        }
                        if (work >= workLimit) break;
                    }
                    phase.Restore(this);
                    if (rankGeometry == null) rankGeometry = BuildExpandRankGeometry(phase,root.state);
                    foreach (var node in next)
                    {
                        current = node.state;
                        node.rank = ExpandParkingRank(node.depth,rankGeometry,pressure,focus,focusDestination);
                    }
                    if(retainParking)
                    {
                        // Keep parking alternatives until the next consumption,
                        // without snapshots of future mechanic/queue branches.
                        foreach(var node in next)parking.Add((node.rank,serial++,node));
                    }
                    else
                    {
                        next.Sort((a,b) => a.rank.CompareTo(b.rank));
                        int beamWidth=focus>=0 ? (finalBombFocus || curtainFocus ? 64 : 16) : expandOrderVariant==2 && HasPermanentSandLink() ? 128 : 32;
                        if (next.Count > beamWidth) next.RemoveRange(beamWidth,next.Count-beamWidth);
                        frontier = next;
                    }
                }
                phase.Restore(this);
                return false;
            }

            private bool FinishExpandEvent(int selected, Positions start, EngineMove move, bool expanding)
            {
                if (BombFailed()) return false;
                if (expanding)
                {
                    SetExpandGroup(selected,false);
                    move.eaten.AddRange(Consume());
                }
                if(BombFailed() || !ResolveCountdownDrag(selected,start,move.eaten) || BombQueueDeadEnd() || !BombSourceSupplyPossible())return false;
                if(HasActiveBomb() && !bombSoftFocus && !hasExpandableHoles && !ConsumptionAssignmentPossible())return false;
                return ExpandEventAssignmentPossible();
            }

            private bool expandAssignmentProbe;
            private bool ExpandEventAssignmentPossible()
            {
                if(!HasPermanentSandLink() || board.holes.Exists(h=>ExpandGeometry.Enabled(h) && linkedGroups[h.id].Length>1))return true;
                var saved=(CellMask[][])masks.Clone();
                try
                {
                    // Accelerator-only lookahead. Use picked masks for future
                    // Expand capacity and retain ordinary linked geometry. The
                    // complete fallback never prunes using this probe.
                    Array.Copy(expandedMasks,masks,masks.Length);
                    expandAssignmentProbe=true;
                    return HasRelaxedCatAssignment();
                }
                finally { expandAssignmentProbe=false;Array.Copy(saved,masks,masks.Length); }
            }

            private void CommitExpandEvent(ExpandParkingNode node, EngineMove terminal)
            {
                var chain = new List<ExpandParkingNode>();
                for (var n=node;n.parent!=null;n=n.parent) chain.Add(n);
                chain.Reverse();
                foreach (var n in chain)
                {
                    var move = new EngineMove { holeId=board.holes[n.move.hole].id,start=CellAt(n.parent.state.GetValue(n.move.hole)) };
                    foreach (int p in n.move.path) move.path.Add(CellAt(p));
                    result.moves.Add(move);
                }
                result.moves.Add(terminal);
            }

            private readonly Dictionary<int,ExpandRouteGeometry[]> expandDragGeometry = new Dictionary<int,ExpandRouteGeometry[]>();
            private long ExpandDragRouteRank()
            {
                // Settled full-drag nodes share exact mechanic-phase IDs, even
                // though their parking positions differ. Targets must use the
                // picked shape; resting-cell distance can favor a blocked route.
                FullStateKey(false);
                int phaseId=cachedPhaseId;
                if(!expandDragGeometry.TryGetValue(phaseId,out var geometry))
                {
                    var phase=new StateCheckpoint(this);
                    phase.Save(this);
                    geometry=BuildExpandRankGeometry(phase,current);
                    if(expandDragGeometry.Count>=32)expandDragGeometry.Clear();
                    expandDragGeometry[phaseId]=geometry;
                }
                return ExpandParkingRank(0,geometry,pressure:true);
            }

            private sealed class ExpandRouteGeometry
            {
                internal CellMask[][] masks;
                internal bool[][] valid,goals;
                internal int[][] distances,anchors,entries;
                internal Dictionary<long,int[]> targets;
            }

            private ExpandRouteGeometry CaptureExpandRouteGeometry(bool cloneMasks)
                => new ExpandRouteGeometry { masks=cloneMasks ? (CellMask[][])masks.Clone() : masks,
                    valid=valid,goals=goals,distances=distances,anchors=goalAnchors,entries=catEntryBlocks,targets=targetDistances };

            private void UseExpandRouteGeometry(ExpandRouteGeometry geometry)
            {
                Array.Copy(geometry.masks,masks,masks.Length);valid=geometry.valid;goals=geometry.goals;distances=geometry.distances;
                goalAnchors=geometry.anchors;catEntryBlocks=geometry.entries;targetDistances=geometry.targets;
            }

            private ExpandRouteGeometry[] BuildExpandRankGeometry(StateCheckpoint phase,Positions state)
            {
                var geometry=new ExpandRouteGeometry[capacities.Length];
                for(int selected=0;selected<capacities.Length;selected++)
                {
                    phase.Restore(this);current=state;
                    if(current.GetValue(selected)<0)continue;
                    int representative=-1;
                    foreach(int member in linkedGroups[selected])
                        if(current.GetValue(member)>=0 && representative<0)representative=member;
                    if(representative!=selected)continue;
                    SetExpandGroup(selected,true);
                    Prepare();
                    geometry[selected]=CaptureExpandRouteGeometry(true);
                }
                phase.Restore(this);
                return geometry;
            }

            // Distinguish cat/wall constraints from a group immobilized by
            // parked holes. Clearing the latter may enable a helper with no
            // immediate target, which minimum target distance alone ignores.
            private bool ExpandGroupBlockedByParking(int selected,Positions state,CellMask blocked)
            {
                int origin=state.GetValue(selected);
                bool staticExit=false;
                foreach(int destination in neighbors[origin])
                {
                    int dx=CellAt(destination).x-CellAt(origin).x,dy=CellAt(destination).y-CellAt(origin).y;
                    int direction=Direction(CellAt(origin),CellAt(destination));
                    bool legal=true,occupied=false;
                    foreach(int member in linkedGroups[selected])
                    {
                        int anchor=state.GetValue(member);
                        if(anchor<0)continue;
                        var p=new Cell(CellAt(anchor).x+dx,CellAt(anchor).y+dy);
                        if(!Inside(p)){legal=false;break;}
                        int q=Index(p);
                        if(!valid[member][q] || !CanStep(member,anchor,q) || !CanEnterCat(member,q,direction))
                        {legal=false;break;}
                        occupied |= !(masks[member][q]&blocked).IsZero;
                    }
                    if(!legal)continue;
                    staticExit=true;
                    if(!occupied)return false;
                }
                return staticExit;
            }

            private long ExpandParkingRank(int depth,ExpandRouteGeometry[] geometry,bool pressure,int focus=-1,int focusDestination=-1)
            {
                var resting=current;
                var saved=CaptureExpandRouteGeometry(true);
                long minimum=long.MaxValue/8,sum=0;
                int blockedGroups=0;
                try
                {
                    // Tables for each picked group are built once per mechanic
                    // phase. Ranking never consumes or changes footprint flags,
                    // and uses explicit masks instead of positional occupancy.
                    for(int selected=0;selected<capacities.Length;selected++)
                    {
                        if(focus>=0 && selected!=focus)continue;
                        if(HasActiveBomb() && NonBombFinishWouldExpire(selected))continue;
                        UseExpandRouteGeometry(saved);
                        int anchor=resting.GetValue(selected);
                        if(anchor<0 || geometry[selected]==null)continue;
                        var group=linkedGroups[selected];
                        bool draggable=true;
                        foreach(int member in group)
                            if(resting.GetValue(member)>=0 && !CanDrag(member,resting.GetValue(member)))draggable=false;
                        if(!draggable)continue;
                        UseExpandRouteGeometry(geometry[selected]);
                        var blocked=OccupancyExcept(resting,group);
                        if(pressure && ExpandGroupBlockedByParking(selected,resting,blocked))blockedGroups++;
                        long cost=group.Length>1 ? LinkedRouteDistance(selected,resting,blocked)
                            : OccupancyPathDistance(selected,anchor,blocked,focus>=0 ? focusDestination : -1);
                        foreach(int member in group)
                            if(resting.GetValue(member)>=0 && goals[member][resting.GetValue(member)])cost=0;
                        if(cost>=long.MaxValue/16)continue;
                        minimum=Math.Min(minimum,cost);
                        sum+=Math.Min(cost,1023);
                    }
                }
                finally { UseExpandRouteGeometry(saved); }
                if(minimum>=long.MaxValue/16)return long.MaxValue/2+depth;
                if(pressure)return minimum*16+sum+blockedGroups*128L+depth;
                return minimum*1024+Math.Min(sum,1023)+depth;
            }
        }
    }
}

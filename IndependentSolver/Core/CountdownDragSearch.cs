using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private sealed class DragStateNode
            {
                internal DragStateNode parent;
                internal EngineMove move;
                internal StateCheckpoint state;
                internal int depth;
                internal bool expanded;
            }
            private bool BombFailed() => board.holes.Exists(h => h.bombRemaining < 0);
            private readonly List<EatEvent> noDragConsumption=new List<EatEvent>();
            private int AlivePlugCount(){int count=0;foreach(var plug in board.plugs)if(plug.alive)count++;return count;}
            private bool ResolveCountdownDrag(int selected, Positions start, List<EatEvent> eaten)
            {
                foreach (int member in linkedGroups[selected])
                {
                    var hole=board.holes[member];
                    if(start.GetValue(member)<0 || hole.countdownMoves<=0)continue;
                    bool ate=false;
                    foreach(var eat in eaten)if(eat.holeId==hole.id && eat.catId>=0){ate=true;break;}
                    // Finished holes are permitted to reach zero on their last drag.
                    bool moved=current.GetValue(member)<0 || current.GetValue(member)!=start.GetValue(member);
                    if(moved||ate)
                    {
                        InvalidatePhaseKey();
                        hole.countdownMoves--;
                        if(hole.countdownMoves==0 && current.GetValue(member)>=0)return false;
                    }
                }
                return true;
            }
            private long DragRank(int depth)
            {
#if CATDOM_PROFILE
                using var profile=Profile("DragRank");
#endif
                int cats=0;
                foreach(bool alive in catsAlive)if(alive)cats++;
                // A pending elevator cat is still a future win requirement.
                // Counting it now prevents birth from looking like lost progress.
                foreach(var plan in elevatorPlans)foreach(var candidate in plan.candidates)
                    if(candidate.kind==ElevatorObjectKind.Cat && candidate.state==0)cats++;
                for(int b=0;b<boxOffsets.Length;b++)cats+=board.boxes[b].colors.Length-boxOffsets[b];
                // Ordering only: use the prepared legal distances without the
                // costly assignment score; retain every legal state in frontier.
                int route=999;
                for(int i=0;i<capacities.Length;i++)
                {
                    int anchor=current.GetValue(i);
                    if(anchor>=0 && CanDrag(i,anchor))
                        route=Math.Min(route,distances[i][anchor]);
                }
                long progress=cats*10000L;
                // Socket removal and queue release enable later consumption.
                // Rank these exact successor states ahead of arbitrary parking;
                // retain every legal state, including delayed releases.
                foreach(var plug in board.plugs)if(plug.alive)progress+=1000;
                foreach(var spawner in board.spawners)progress+=(spawner.holeIds.Length-spawner.consumed)*1000L;
                int spawnBlocks=0;
                if(elevatorPlans.Count>0)
                {
                    var occupied=Occupancy(current);
                    foreach(var plan in elevatorPlans)
                    {
                        bool pending=false;
                        foreach(var candidate in plan.candidates)if(candidate.state==0){pending=true;break;}
                        if(!pending)continue;
                        foreach(var cell in plan.area)
                            if(Inside(cell) && !(occupied&Bit(cell)).IsZero)spawnBlocks++;
                    }
                }
                if(board.spawners.Count>0)
                {
                    var occupied=Occupancy(current);
                    foreach(var spawner in board.spawners)
                    {
                        if(spawner.consumed>=spawner.holeIds.Length)continue;
                        var next=board.holes.Find(h=>h.id==spawner.holeIds[spawner.consumed]);
                        foreach(var local in next.footprint)
                        {
                            var cell=new Cell(next.position.x+local.x,next.position.y+local.y);
                            if(Inside(cell) && !(occupied&Bit(cell)).IsZero)spawnBlocks++;
                            int cat=Inside(cell)?catAtCell[Index(cell)]:-1;
                            if(cat>=0 && catsAlive[cat])spawnBlocks++;
                            foreach(var plug in board.plugs)
                                if(plug.alive && plug.position.Equals(cell))spawnBlocks++;
                        }
                    }
                }
                // A bounded tie preference for clearing the next birth region.
                // Queue advancement and cat consumption retain larger rewards.
                if(HasActiveBomb())return BombDragRank(depth);
                if(board.holes.Exists(h=>h.isOpenClose) && !hasExpandableHoles && board.spawners.Count==0 && elevatorPlans.Count==0)
                    return CurtainDragRank(cats,depth,route);
                if(hasExpandableHoles)
                {
                    long footprintRoute=ExpandDragRouteRank();
                    // Preserve strict consumption preference while ordering its
                    // parking arrangements by the actual picked route pressure.
                    return cats*100000L+Math.Min(footprintRoute,90000)+Math.Min(depth,999)*10L;
                }
                return progress + Math.Min(spawnBlocks*40,900) + depth*10L + route;
            }
            private bool fullDragSearchActive;
            private CellMask PreparedFixedMask()=>fullDragSearchActive ? CellMask.Zero : ImmovableMask();
            private void SearchCountdownDrags()
            {
                fullDragSearchActive=true;
                try { SearchFullDragStates(); }
                finally { fullDragSearchActive=false; }
            }
            private void SearchFullDragStates()
            {
                TraceStage("full-drag");
                stats=new SubproblemStats();
                Prepare();
                var root=new DragStateNode{state=new StateCheckpoint(this)};
                root.state.Save(this);
                var open=new StableMinHeap<DragStateNode>();
                bool useSpawnerPortfolio=board.spawners.Count>0 && !hasExpandableHoles && elevatorPlans.Count==0 && !board.holes.Exists(h=>h.bombRemaining>0);
                var delayed=useSpawnerPortfolio ? new StableMinHeap<DragStateNode>() : null;
                int schedulingTurn=0;
                var seen=new HashSet<string>{FullStateKey()};
                int serial=0;
                open.Add((DragRank(0),serial++,root));
                if(useSpawnerPortfolio)delayed.Add((SpawnerPortfolioRank(0),serial++,root));
                while(open.Count>0)
                {
                    CheckBudget();
                    var source=useSpawnerPortfolio && (++schedulingTurn%4)==0 && delayed.Count>0 ? delayed : open;
                    var node=source.Pop().node;
                    if(useSpawnerPortfolio)
                    {
                        if(node.expanded)continue;
                        node.expanded=true;
                    }
                    node.state.Restore(this);
                    if(Cleared())
                    {
                        var chain=new List<DragStateNode>();
                        for(var n=node;n.parent!=null;n=n.parent)chain.Add(n);
                        chain.Reverse();
                        foreach(var step in chain)
                        {
                            step.state.Restore(this);
                            step.move.expected=Snapshot();
                            result.moves.Add(step.move);
                        }
                        result.status=SolveStatus.Solved;
                        result.message="All playable holes finished and cats cleared.";
                        return;
                    }
                    Prepare();
                    result.expanded++;stats.expanded++;
                    if((result.expanded&31)==0)context.progress?.Invoke(result);
                    // Snapshot this phase once for all successor simulations.
                    var parent=new StateCheckpoint(this);parent.Save(this);
                    if(hasExpandableHoles)
                    {
                        for(int selected=0;selected<capacities.Length;selected++)
                        {
                            CheckBudget();parent.Restore(this);
                            if(current.GetValue(selected)<0)continue;
                            var group=linkedGroups[selected];
                            int representative=-1;bool draggable=true;
                            foreach(int member in group)
                                if(current.GetValue(member)>=0)
                                {
                                    if(representative<0)representative=member;
                                    if(!CanDrag(member,current.GetValue(member)))draggable=false;
                                }
                            if(representative!=selected || !draggable)continue;
                            var start=current;
                            var beforeLayers=(int[])layers.Clone();
                            var pickup=new EngineMove{holeId=board.holes[selected].id,start=CellAt(start.GetValue(selected))};
                            pickup.path.Add(pickup.start);
                            bool expanding=Array.Exists(group,m=>ExpandGeometry.Enabled(board.holes[m]));
                            if(expanding)pickup.eaten.AddRange(BeginExpandPickup(selected));
                            if(BombFailed())continue;
                            bool stopped=false;
                            foreach(int member in group)
                                if(start.GetValue(member)>=0 && (current.GetValue(member)<0 || layers[member]!=beforeLayers[member]))stopped=true;
                            Prepare();
                            var phase=new StateCheckpoint(this);phase.Save(this);
                            if(expanding)EnqueueRelease(selected,start,pickup);
                            if(stopped)continue;
                            phase.Restore(this);
                            var candidates=GenerateUncached(current,selected,true);
                            foreach(var candidate in candidates)
                            {
                                phase.Restore(this);
                                ExploreDrag(candidate,start,pickup);
                            }
                        }
                    }
                    else
                    {
                        // OnHoleEndDrag checks spawners even when the hole is
                        // released at its starting anchor. No countdown move is
                        // charged without movement or board-cat consumption.
                        if(board.spawners.Count>0)
                            for(int selected=0;selected<capacities.Length;selected++)
                            {
                                parent.Restore(this);
                                if(current.GetValue(selected)<0)continue;
                                bool draggable=true;
                                foreach(int member in linkedGroups[selected])
                                    if(current.GetValue(member)>=0 && !CanDrag(member,current.GetValue(member)))draggable=false;
                                if(!draggable)continue;
                                var release=new EngineMove{holeId=board.holes[selected].id,start=CellAt(current.GetValue(selected))};
                                release.path.Add(release.start);
                                EnqueueRelease(selected,current,release);
                            }
                        parent.Restore(this);
                        var candidates=Generate(current);
                        foreach(var candidate in candidates)
                        {
                            CheckBudget();parent.Restore(this);
                            ExploreDrag(candidate,current,null);
                        }
                    }

                    void EnqueueRelease(int selected,Positions dragStart,EngineMove move)
                    {
                        var settledMove=new EngineMove{holeId=move.holeId,start=move.start,path=new List<Cell>(move.path),eaten=new List<EatEvent>(move.eaten)};
                        if(hasExpandableHoles)
                        {
                            SetExpandGroup(selected,false);
                            settledMove.eaten.AddRange(Consume());
                        }
                        if(BombFailed() || !ResolveCountdownDrag(selected,dragStart,settledMove.eaten))return;
                        bool spawned=ResolveSpawnerRelease();
                        if(hasExpandableHoles || spawned)settledMove.eaten.AddRange(Consume());
                        RefreshBarries();
                        if(RefreshElevators())settledMove.eaten.AddRange(Consume());
                        if(BombFailed())return;
                        if(BombQueueDeadEnd())return;
                        string key=FullStateKey();
                        if(!seen.Add(key))return;
                        Prepare();
                        var child=new DragStateNode{parent=node,move=settledMove,depth=node.depth+1,state=new StateCheckpoint(this)};
                        child.state.Save(this);
                        open.Add((DragRank(child.depth),serial++,child));
                        if(useSpawnerPortfolio)delayed.Add((SpawnerPortfolioRank(child.depth),serial++,child));
                    }

                    void ExploreDrag(Candidate candidate,Positions dragStart,EngineMove prefix)
                    {
                        CheckBudget();
                        var oldLayers=(int[])layers.Clone();
                        var move=new EngineMove{holeId=board.holes[candidate.hole].id,start=CellAt(dragStart.GetValue(candidate.hole))};
                        if(prefix!=null){move.path.AddRange(prefix.path);move.eaten.AddRange(prefix.eaten);}
                        int skip=prefix==null?0:1;
                        var path=candidate.path;
                        for(int i=skip;i<path.Length;i++)move.path.Add(CellAt(path[i]));
                        int plugsBefore=candidate.terminal || hasExpandableHoles ? AlivePlugCount() : 0;
                        current=candidate.state;
                        // BFS stops at every prepared target for any linked
                        // member. A nonterminal path changes positions only;
                        // other groups were already settled at the parent.
                        var newlyEaten=hasExpandableHoles || candidate.terminal ? Consume(candidate.hole) : noDragConsumption;
                        move.eaten.AddRange(newlyEaten);
                        if(BombFailed())return;
                        if(newlyEaten.Count>0 && !ConsumptionAssignmentPossible())return;
                        bool canContinue=(newlyEaten.Count>0 || AlivePlugCount()<plugsBefore) && current.GetValue(candidate.hole)>=0;
                        foreach(int member in linkedGroups[candidate.hole])
                            if(candidate.state.GetValue(member)>=0 && (current.GetValue(member)<0 || oldLayers[member]!=layers[member]))canContinue=false;
                        // Only a continuing drag restores its uncharged state.
                        // Parking and terminal successors need no extra arrays.
                        StateCheckpoint uncharged=null;
                        if(canContinue)
                        {
                            uncharged=new StateCheckpoint(this);
                            uncharged.Save(this);
                        }
                        EnqueueRelease(candidate.hole,dragStart,move);
                        // Consumption without layer/finish can continue inside the
                        // same physical drag. Counter is charged only at release.
                        if(canContinue)
                        {
                            uncharged.Restore(this);Prepare();
                            result.expanded++;stats.expanded++;
                            context.progress?.Invoke(result);CheckBudget();
                            var extensions=GenerateUncached(current,candidate.hole,true);
                            var phase=new StateCheckpoint(this);phase.Save(this);
                            foreach(var next in extensions)
                                if(next.hole==candidate.hole)
                                {
                                    phase.Restore(this);
                                    ExploreDrag(next,dragStart,move);
                                }
                        }
                    }
                }
                result.status=SolveStatus.NoNextCatReachable;
                result.message="All reachable drag states exhausted within countdown constraints.";
            }
        }
    }
}










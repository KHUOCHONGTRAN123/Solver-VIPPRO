using System;
using System.Collections.Generic;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private bool preferBombEvents=true;
            private bool bombSoftFocus;

            private bool TryBombPlan()
            {
                if(hasExpandableHoles)return false;
                for(expandOrderVariant=0;expandOrderVariant<3;expandOrderVariant++)
                    if(TryBombOrderedPlan(soft:true))return true;
                for(expandOrderVariant=0;expandOrderVariant<3;expandOrderVariant++)
                {
                    if(TryBombOrderedPlan())return true;
                    if(expandOrderVariant==0 && TryBombEventPlan())return true;
                }
                return false;
            }

            private bool TryBombEventPlan()
            {
                if(board.spawners.Count>0 || board.elevators.Count>0 || board.barries.Count>0 ||
                    board.holes.Exists(h=>h.countdownMoves>0 || h.isPlug || h.isOpenClose) || board.plugs.Exists(p=>p.alive))return false;
                Prepare();var root=new StateCheckpoint(this);root.Save(this);
                int rootMoves=result.moves.Count;long end=result.expanded+8000;
                var visitedPhases=new HashSet<string>();bool solved=false;
                try { solved=Visit(0);return solved; }
                finally
                {
                    decisionPhaseLimit=0;decisionTotalEnd=0;
                    if(!solved){root.Restore(this);result.moves.RemoveRange(rootMoves,result.moves.Count-rootMoves);}
                }
                bool Visit(int depth)
                {
                    CheckBudget();
                    if(Cleared())return true;
                    if(depth>=96 || result.expanded>=end || BombFailed())return false;
                    Prepare();
                    if(!visitedPhases.Add(FullStateKey()))return false;
                    var phase=new StateCheckpoint(this);phase.Save(this);
                    int moves=result.moves.Count;bool won=false;
                    try
                    {
                        if(!HasActiveBomb())
                        {
                            decisionPhaseStart=result.expanded;decisionPhaseLimit=2000;decisionTotalEnd=Math.Min(end,result.expanded+2000);
                            try { RunOrdinaryPhases();won=result.status==SolveStatus.Solved; }
                            catch(DecisionBudgetExceeded) { }
                            finally {decisionPhaseLimit=0;decisionTotalEnd=0;}
                            return won;
                        }
                        var targets=new List<int>();
                        for(int h=0;h<capacities.Length;h++)
                            if(current.GetValue(h)>=0 && Array.Find(linkedGroups[h],m=>current.GetValue(m)>=0)==h && !NonBombFinishWouldExpire(h))targets.Add(h);
                        targets.Sort((a,b)=>BombEventPriority(new Candidate{hole=a,destination=current.GetValue(a)}).CompareTo(BombEventPriority(new Candidate{hole=b,destination=current.GetValue(b)})));
                        foreach(int h in targets)
                        {
                            if(result.expanded>=end)break;
                            phase.Restore(this);result.moves.RemoveRange(moves,result.moves.Count-moves);
                            TraceStage("bomb-decision-"+depth+"-"+h);
                            if(FindExpandEvent(focus:h) && Visit(depth+1)){won=true;return true;}
                        }
                        return false;
                    }
                    finally {if(!won){phase.Restore(this);result.moves.RemoveRange(moves,result.moves.Count-moves);}}
                }
            }

            private bool TryBombOrderedPlan(bool soft=false)
            {
                if(hasExpandableHoles || board.spawners.Count>0 || board.elevators.Count>0 || board.barries.Count>0 || board.holes.Exists(h=>h.countdownMoves>0))return false;
                Prepare();var initial=new StateCheckpoint(this);initial.Save(this);
                int startMoves=result.moves.Count;bool solved=false;
                bombSoftFocus=soft;
                try
                {
                    while(!Cleared())
                    {
                        CheckBudget();Prepare();
                        if(!HasActiveBomb())
                        {
                            if(board.plugs.TrueForAll(p=>!p.alive) && !board.holes.Exists(h=>h.isPlug || h.isOpenClose))
                            {
                                decisionPhaseStart=result.expanded;decisionPhaseLimit=4000;decisionTotalEnd=result.expanded+4000;
                                RunOrdinaryPhases();
                            }
                            else SearchCountdownDrags();
                            solved=result.status==SolveStatus.Solved;
                            return solved;
                        }
                        int focus=-1;
                        for(int i=0;i<capacities.Length;i++)
                            if(current.GetValue(i)>=0 && board.holes[i].bombRemaining>0 && (focus<0 || board.holes[i].bombRemaining<board.holes[focus].bombRemaining))focus=i;
                        int representative=-1;
                        foreach(int member in linkedGroups[focus])if(current.GetValue(member)>=0 && representative<0)representative=member;
                        TraceStage("bomb-event-"+focus);
                        // Reuse exact fixed-phase parking/pickup simulation;
                        // ordinary bomb holes have no footprint mode change.
                        if(!FindExpandEvent(focus:representative) && !FindExpandEvent() && !FindExpandFocusedEvent())return false;
                    }
                    solved=true;return true;
                }
                catch(DecisionBudgetExceeded){return false;}
                finally
                {
                    bombSoftFocus=false;
                    decisionPhaseLimit=0;decisionTotalEnd=0;
                    if(!solved)
                    {
                        result.finalState=Snapshot();TraceStage("bomb-retry-"+expandOrderVariant);
                    }
                    if(!solved){initial.Restore(this);result.moves.RemoveRange(startMoves,result.moves.Count-startMoves);}
                }
            }

            // Accelerator lookahead: ignore movement, gates, ice and queue
            // prefixes, but count finishes forced by other-colored cats at a
            // source footprint. Reject only this greedy event, not full search.
            private readonly Dictionary<string,bool> bombSupplyChecks=new Dictionary<string,bool>();
            private bool BombSourceSupplyPossible()
            {
                if(board.spawners.Count>0 || board.elevators.Count>0 || board.holes.Exists(h=>h.layerColors.Length>1))return true;
                if(!HasActiveBomb())return true;
                string key=FullStateKey();
                if(bombSupplyChecks.TryGetValue(key,out bool cached))return cached;
                bool possible=true;
                for(int bomb=0;bomb<capacities.Length && possible;bomb++)
                {
                    if(current.GetValue(bomb)<0 || board.holes[bomb].bombRemaining<=0)continue;
                    int color=Color(bomb),supply=0;
                    var slack=new Dictionary<int,int>();
                    for(int h=0;h<capacities.Length;h++)
                        if(h!=bomb && current.GetValue(h)>=0 && Color(h)<1000)
                        {slack.TryGetValue(Color(h),out int amount);slack[Color(h)]=amount+Math.Max(0,capacities[h]-1);}
                    bool SourceFits(CellMask source)
                    {
                        for(int p=0;p<count;p++)
                        {
                            var mask=masks[bomb][p];
                            if(mask.IsZero || (mask&source).IsZero)continue;
                            if(p==current.GetValue(bomb))return true;
                            var blockers=new Dictionary<int,int>();
                            for(int c=0;c<catsAlive.Length;c++)
                                if(catsAlive[c] && board.cats[c].color!=color && !(mask&catMasks[c]).IsZero)
                                {int other=board.cats[c].color;blockers.TryGetValue(other,out int amount);blockers[other]=amount+1;}
                            int forced=0;
                            foreach(var pair in blockers)
                            {slack.TryGetValue(pair.Key,out int free);forced+=Math.Max(0,pair.Value-free);}
                            if(forced<board.holes[bomb].bombRemaining)return true;
                        }
                        return false;
                    }
                    for(int c=0;c<catsAlive.Length;c++)
                        if(catsAlive[c] && board.cats[c].color==color && SourceFits(catMasks[c]))supply++;
                    for(int box=0;box<boxOffsets.Length;box++)
                        if(SourceFits(boxMasks[box]))
                            for(int token=boxOffsets[box];token<board.boxes[box].colors.Length;token++)
                                if(board.boxes[box].colors[token]==color)supply++;
                    possible=supply>=capacities[bomb];
                }
                if(bombSupplyChecks.Count>=512)bombSupplyChecks.Clear();
                bombSupplyChecks[key]=possible;
                return possible;
            }

            private bool NonBombFinishWouldExpire(int hole)
            {
                if(board.holes[hole].bombRemaining>0 || linkedGroups[hole].Length>1 || capacities[hole]!=1 || layers[hole]+1<board.holes[hole].layerColors.Length)return false;
                for(int i=0;i<capacities.Length;i++)
                    if(current.GetValue(i)>=0 && board.holes[i].bombRemaining==1)return true;
                return false;
            }

            private bool HasActiveBomb()
            {
                for(int i=0;i<capacities.Length;i++)
                    if(current.GetValue(i)>=0 && board.holes[i].bombRemaining>0)return true;
                return false;
            }

            private Positions bombPreferenceState;
            private bool bombPreferenceSoft;
            private bool[][] bombPreferencePhase;
            private readonly HashSet<int> bombHelpfulColors=new HashSet<int>();
            private readonly HashSet<int> bombKeyColors=new HashSet<int>();
            private CellMask bombCorridor;
            private bool bombNeedsCompletion;

            private void PrepareBombPreferences()
            {
                if(bombPreferenceSoft==bombSoftFocus && ReferenceEquals(current,bombPreferenceState) && ReferenceEquals(valid,bombPreferencePhase))return;
                bombPreferenceSoft=bombSoftFocus;
                bombPreferenceState=current; bombPreferencePhase=valid;
                bombHelpfulColors.Clear();bombKeyColors.Clear();bombCorridor=CellMask.Zero;
                bombNeedsCompletion=false;
                for(int bomb=0;bomb<capacities.Length;bomb++)
                {
                    int anchor=current.GetValue(bomb);
                    if(anchor<0 || board.holes[bomb].bombRemaining<=0)continue;
                    var hole=board.holes[bomb];
                    if(hole.locked && hole.lockColorId>=0)bombKeyColors.Add(hole.lockColorId);
                    bombNeedsCompletion |= hole.numIced>0 || hole.hiddenCount>0 || hole.runesealedUnlockHoleIndex>=0;
                    int available=0,color=Color(bomb);
                    for(int c=0;c<catsAlive.Length;c++)if(catsAlive[c] && board.cats[c].color==color)available++;
                    // A cat can be exposed yet unreachable by the bomb shape:
                    // other colors inside every source footprint need helper drops.
                    // Choose the least obstructed footprint per source, ignoring
                    // parked holes so this remains a cheap ordering estimate.
                    for(int source=0;!bombSoftFocus && source<catsAlive.Length+boxOffsets.Length;source++)
                    {
                        CellMask sourceMask;
                        if(source<catsAlive.Length)
                        {
                            if(!catsAlive[source] || board.cats[source].color!=color)continue;
                            sourceMask=catMasks[source];
                        }
                        else
                        {
                            int box=source-catsAlive.Length;
                            if(available>=capacities[bomb] || Array.IndexOf(board.boxes[box].colors,color,boxOffsets[box])<0)continue;
                            sourceMask=boxMasks[box];
                        }
                        int best=-1,bestBlocks=int.MaxValue;
                        for(int p=0;p<count;p++)
                        {
                            var mask=masks[bomb][p];
                            if(mask.IsZero || (mask&sourceMask).IsZero)continue;
                            int blocks=0;
                            for(int c=0;c<catsAlive.Length;c++)
                                if(catsAlive[c] && board.cats[c].color!=color && !(mask&catMasks[c]).IsZero)blocks++;
                            if(blocks<bestBlocks){best=p;bestBlocks=blocks;}
                        }
                        if(best<0 || bestBlocks==0)continue;
                        for(int c=0;c<catsAlive.Length;c++)
                            if(catsAlive[c] && board.cats[c].color!=color && !(masks[bomb][best]&catMasks[c]).IsZero)
                                bombHelpfulColors.Add(board.cats[c].color);
                    }
                    if(available>0)
                        for(int c=0;c<catsAlive.Length;c++)
                            if(catsAlive[c] && board.cats[c].color==color && board.cats[c].numIced>0)bombHelpfulColors.UnionWith(board.holes.ConvertAll(h=>h.color));
                    if(available<capacities[bomb])
                        foreach(var box in board.boxes)
                        {
                            int index=board.boxes.IndexOf(box),end=Array.IndexOf(box.colors,color,boxOffsets[index]);
                            if(end<0)continue;
                            for(int p=boxOffsets[index];p<end;p++)bombHelpfulColors.Add(box.colors[p]);
                            if(box.requiredHolesToUnlock>0)bombNeedsCompletion=true;
                        }
                    if(!CanDrag(bomb,anchor))continue;
                    var blocked=OccupancyExcept(current,linkedGroups[bomb]);
                    if(linkedGroups[bomb].Length>1)continue;
                    OccupancyPathDistance(bomb,anchor,blocked);
                    if(!occupancyDistances.TryGetValue((valid,bomb,-1,blocked,8,1),out var costs))continue;
                    var seen=new bool[count];
                    for(int steps=0;steps<count;steps++)
                    {
                        seen[anchor]=true;bombCorridor|=masks[bomb][anchor];
                        if(goals[bomb][anchor])break;
                        int next=-1;
                        foreach(int q in neighbors[anchor])
                            if(!seen[q] && valid[bomb][q] && CanStep(bomb,anchor,q) &&
                                CanEnterCat(bomb,q,Direction(CellAt(anchor),CellAt(q))) &&
                                costs[q]<long.MaxValue/8 && (next<0 || costs[q]<costs[next]))next=q;
                        if(next<0)break;
                        anchor=next;
                    }
                }
            }

            private int BombEventPriority(Candidate move)
            {
                if(board.holes[move.hole].bombRemaining>0)return 0;
                PrepareBombPreferences();
                if(bombHelpfulColors.Contains(Color(move.hole)))return 1;
                var destination=masks[move.hole][move.destination];
                for(int c=0;c<catsAlive.Length;c++)
                    if(catsAlive[c] && bombKeyColors.Contains(board.cats[c].keyColorId) && !(destination&catMasks[c]).IsZero)return 1;
                int start=current.GetValue(move.hole);
                if(start>=0 && !(masks[move.hole][start]&bombCorridor).IsZero)return 1;
                if(bombNeedsCompletion && capacities[move.hole]==1)return 2;
                return 3;
            }

            // Ordering only. Unrelated cat drops receive no general progress
            // reward while a bomb is live. Clearing bombs dominates; useful
            // queue advancement, unlocking and corridor parking break ties.
            private long BombDragRank(int depth)
            {
                long score=0;
                int focus=-1;
                for(int i=0;i<capacities.Length;i++)
                    if(current.GetValue(i)>=0 && board.holes[i].bombRemaining>0 &&
                        (focus<0 || board.holes[i].bombRemaining<board.holes[focus].bombRemaining ||
                         board.holes[i].bombRemaining==board.holes[focus].bombRemaining && capacities[i]<capacities[focus]))focus=i;
                for(int bomb=0;bomb<capacities.Length;bomb++)
                {
                    int anchor=current.GetValue(bomb),budget=board.holes[bomb].bombRemaining;
                    if(anchor<0 || budget<=0)continue;
                    var h=board.holes[bomb];int color=Color(bomb),available=0;
                    score+=1000000000L+capacities[bomb]*100000L+100000L/((long)budget*budget);
                    score+=h.numIced*20000L+h.hiddenCount*20000L+(h.locked?25000L:0);
                    var ice=new List<int>();
                    for(int c=0;c<catsAlive.Length;c++)
                        if(catsAlive[c] && board.cats[c].color==color)
                        {
                            available++;
                            ice.Add(board.cats[c].numIced);
                        }
                    for(int box=0;box<boxOffsets.Length;box++)
                        for(int token=boxOffsets[box];token<board.boxes[box].cats.Length;token++)
                            if(board.boxes[box].cats[token].color==color)ice.Add(board.boxes[box].cats[token].numIced);
                    ice.Sort();
                    for(int token=0;token<Math.Min(capacities[bomb],ice.Count);token++)score+=ice[token]*10000L;
                    if(available<capacities[bomb])
                    {
                        var prefixes=new List<int>();
                        for(int box=0;box<boxOffsets.Length;box++)
                        {
                            int blockers=board.boxes[box].requiredHolesToUnlock*4;
                            for(int token=boxOffsets[box];token<board.boxes[box].colors.Length;token++)
                                if(board.boxes[box].colors[token]==color)prefixes.Add(blockers);
                                else blockers++;
                        }
                        prefixes.Sort();
                        int needed=capacities[bomb]-available;
                        for(int token=0;token<needed;token++)
                            score+=(token<prefixes.Count ? Math.Min(prefixes[token],100) : 100)*10000L;
                    }
                    long route=long.MaxValue/8;
                    if(CanDrag(bomb,anchor))
                    {
                        var blocked=OccupancyExcept(current,linkedGroups[bomb]);
                        route=linkedGroups[bomb].Length>1?LinkedRouteDistance(bomb,current,blocked):OccupancyPathDistance(bomb,anchor,blocked);
                    }
                    score+=Math.Min(route,1000)*(bomb==focus ? 1000L : 10L);
                }
                return score+Math.Min(depth,9999);
            }
        }
    }
}

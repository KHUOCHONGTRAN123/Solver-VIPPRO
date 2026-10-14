using System;

namespace CatDom.CoreSolver
{
    public static partial class CatLevelSolver
    {
        private sealed partial class Engine
        {
            private long CurtainDragRank(int cats,int depth,int route)
            {
                long priority=cats*10000L;
                int bestRoute=999;
                for(int i=0;i<capacities.Length;i++)
                {
                    int anchor=current.GetValue(i);
                    if(anchor<0 || !board.holes[i].isOpenClose)continue;
                    priority+=capacities[i]*2000L;
                    if(board.holes[i].closed){priority+=3000;continue;}
                    if(!CanDrop(i,anchor))continue;
                    var blocked=OccupancyExcept(current,linkedGroups[i]);
                    long distance=linkedGroups[i].Length>1 ? LinkedRouteDistance(i,current,blocked) : OccupancyPathDistance(i,anchor,blocked);
                    bestRoute=Math.Min(bestRoute,(int)Math.Min(distance,999));
                }
                // Tie pressure follows the open curtain's corridor instead of
                // whichever ordinary hole has the nearest unrelated cat.
                return priority+Math.Min(bestRoute,999)*4L+AlivePlugCount()*1000L+depth*10L+route;
            }
            private readonly System.Collections.Generic.Dictionary<string,System.Collections.Generic.Dictionary<int,int>> curtainHelperCache=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.Dictionary<int,int>>();
            private bool TryCurtainPlan()
            {
                if(!board.holes.Exists(h=>h.isOpenClose) || hasExpandableHoles || board.spawners.Count>0 || board.elevators.Count>0 || board.barries.Count>0 || HasActiveBomb() || board.holes.Exists(h=>h.countdownMoves>0))return false;
                // The event planner already uses CurtainEventPriority. Let
                // ordinary short plans finish before the parking accelerator.
                if(TryDecisionPlan(2048))return true;
                Prepare();var initial=new StateCheckpoint(this);initial.Save(this);
                int countBefore=result.moves.Count;bool solved=false;
                int savedOrder=expandOrderVariant;
                try
                {
                    for(int order=0;order<2;order++)
                    {
                        expandOrderVariant=order==0 ? 0 : 2;
                        initial.Restore(this);result.moves.RemoveRange(countBefore,result.moves.Count-countBefore);
                        long end=result.expanded+12000;
                        for(int phase=0;phase<256 && result.expanded<end;phase++)
                        {
                            if(Cleared()){solved=true;return true;}
                            Prepare();
                            var open=new System.Collections.Generic.List<int>();
                            for(int i=0;i<capacities.Length;i++)
                                if(current.GetValue(i)>=0 && board.holes[i].isOpenClose && !board.holes[i].closed && CanDrop(i,current.GetValue(i)))open.Add(i);
                            open.Sort((a,b)=>order==0 ? capacities[b].CompareTo(capacities[a]) : capacities[a].CompareTo(capacities[b]));
                            if(context.progress!=null){result.finalState=Snapshot();TraceStage("curtain-order-"+order+"-phase-"+phase+"-open-"+open.Count);}
                            bool found=false;
                            // Prefer an already available Curtain drop before
                            // enumerating parking arrangements for its route.
                            Candidate direct=null;
                            foreach(var candidate in Generate(current))
                                if(candidate.terminal && Array.Exists(linkedGroups[candidate.hole],m=>open.Contains(m)) && AcceptTerminal(candidate) &&
                                    (direct==null || CompareTerminal(candidate,direct)<0))direct=candidate;
                            if(direct!=null)
                            {
                                AppendPlan(new Node{parent=new Node(),move=direct,depth=1});
                                found=true;
                            }
                            foreach(int hole in open)
                                if(!found && FindExpandEvent(focus:hole,curtainFocus:true)){found=true;break;}
                            if(!found && open.Count>0)
                            {
                                var helpful=CurtainHelperColors();
                                var helpers=new System.Collections.Generic.List<int>();
                                for(int i=0;i<capacities.Length;i++)
                                    if(current.GetValue(i)>=0 && helpful.ContainsKey(Color(i)))helpers.Add(i);
                                helpers.Sort((a,b)=>helpful[Color(b)].CompareTo(helpful[Color(a)]));
                                foreach(int helper in helpers)
                                    if(FindExpandEvent(focus:helper,curtainFocus:true)){found=true;break;}
                            }
                            if(!found && !FindExpandEvent())
                            {
                                if(context.progress!=null){result.finalState=Snapshot();TraceStage("curtain-stalled-"+order+"-phase-"+phase);}
                                // Preserve useful curtain-first consumption
                                // while resolving the final parking bottleneck.
                                // A bounded failure restores this attempt's
                                // initial board before trying another order.
                                int previousLimit=decisionPhaseLimit;
                                long previousStart=decisionPhaseStart,previousEnd=decisionTotalEnd;
                                bool previousDrag=fullDragSearchActive;
                                try
                                {
                                    decisionPhaseStart=result.expanded;
                                    decisionPhaseLimit=8000;
                                    decisionTotalEnd=result.expanded+8000;
                                    fullDragSearchActive=true;
                                    SearchFullDragStates();
                                    if(Cleared()){solved=true;return true;}
                                }
                                catch(DecisionBudgetExceeded){ }
                                finally
                                {
                                    decisionPhaseLimit=previousLimit;decisionPhaseStart=previousStart;decisionTotalEnd=previousEnd;
                                    fullDragSearchActive=previousDrag;
                                }
                                break;
                            }
                        }
                        if(Cleared()){solved=true;return true;}
                    }
                    return false;
                }
                finally
                {
                    expandOrderVariant=savedOrder;
                    if(!solved){initial.Restore(this);result.moves.RemoveRange(countBefore,result.moves.Count-countBefore);}
                }
            }

            // Ordering only. A completion that opens a blocked curtain or
            // clears a necessary corridor remains a legal alternative.
            private long CurtainEventPriority(Candidate move)
            {
                if(!board.holes.Exists(h=>h.isOpenClose))return 0;
                if(move.curtainPriority.HasValue)return move.curtainPriority.Value;
                CheckBudget();
                var saved=SaveCheckpoint();
                try
                {
                    current=move.state;
                    var helperColors=CurtainHelperColors();
                    var opportunities=new bool[capacities.Length];
                    for(int i=0;i<capacities.Length;i++)
                    {
                        var hole=board.holes[i];
                        int anchor=current.GetValue(i);
                        if(!hole.isOpenClose || hole.closed || !CanDrop(i,anchor))continue;
                        if(goals[i][anchor]){opportunities[i]=true;continue;}
                        int representative=-1;
                        foreach(int member in linkedGroups[i])
                            if(current.GetValue(member)>=0){representative=member;break;}
                        if(representative<0)continue;
                        foreach(var candidate in GenerateUncached(current,representative))
                        {
                            int target=candidate.state.GetValue(i);
                            if(candidate.terminal && target>=0 && goals[i][target] && CanDrop(i,target))
                            {opportunities[i]=true;break;}
                        }
                    }
                    var eaten=Consume(move.hole);
                    long priority=0;
                    foreach(var eat in eaten)
                    {
                        if(board.holes.Exists(h=>h.id==eat.holeId && h.isOpenClose))priority-=100;
                        else if(helperColors.TryGetValue(eat.color,out int relevance))priority-=Math.Min(relevance,8)*10;
                    }
                    for(int i=0;i<capacities.Length;i++)
                        if(opportunities[i] && current.GetValue(i)>=0 && board.holes[i].closed)
                            priority+=1000;
                    move.curtainPriority=priority;
                    return priority;
                }
                finally{saved.Restore(this);}
            }

            private System.Collections.Generic.Dictionary<int,int> CurtainHelperColors()
            {
                var key=new System.Text.StringBuilder(FullStateKey(false));
                for(int i=0;i<capacities.Length;i++)
                    if(board.holes[i].isOpenClose)key.Append('|').Append(i).Append(':').Append(current.GetValue(i));
                string cacheKey=key.ToString();
                if(curtainHelperCache.TryGetValue(cacheKey,out var cached))return cached;
                var colors=new System.Collections.Generic.Dictionary<int,int>();
                for(int hole=0;hole<capacities.Length;hole++)
                {
                    int anchor=current.GetValue(hole);
                    if(anchor<0 || !board.holes[hole].isOpenClose || board.holes[hole].closed)continue;
                    int color=Color(hole);
                    for(int cat=0;cat<catsAlive.Length;cat++)
                    {
                        if(!catsAlive[cat] || board.cats[cat].color!=color)continue;
                        CellMask best=CellMask.Zero;long bestCost=long.MaxValue;
                        for(int p=0;p<count;p++)
                        {
                            var mask=masks[hole][p];
                            if(mask.IsZero || (mask&catMasks[cat]).IsZero)continue;
                            int blockers=0;
                            for(int c=0;c<catsAlive.Length;c++)
                                if(catsAlive[c] && board.cats[c].color!=color && !(mask&catMasks[c]).IsZero)blockers++;
                            long cost=blockers*1000L+Math.Abs(CellAt(p).x-CellAt(anchor).x)+Math.Abs(CellAt(p).y-CellAt(anchor).y);
                            if(cost<bestCost){bestCost=cost;best=mask;}
                        }
                        for(int c=0;c<catsAlive.Length;c++)
                            if(catsAlive[c] && board.cats[c].color!=color && !(best&catMasks[c]).IsZero)
                            {int other=board.cats[c].color;colors.TryGetValue(other,out int weight);colors[other]=weight+1;}
                    }
                    for(int box=0;box<boxOffsets.Length;box++)
                    {
                        int target=-1;
                        for(int token=boxOffsets[box];token<board.boxes[box].colors.Length;token++)
                            if(board.boxes[box].colors[token]==color){target=token;break;}
                        for(int token=boxOffsets[box];token<target;token++)
                        {int other=board.boxes[box].colors[token];colors.TryGetValue(other,out int weight);colors[other]=weight+1;}
                    }
                }
                if(curtainHelperCache.Count>=256)curtainHelperCache.Clear();
                curtainHelperCache[cacheKey]=colors;
                return colors;
            }
        }
    }
}
